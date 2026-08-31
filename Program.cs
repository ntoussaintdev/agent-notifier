// Source supplied by the user: localhost notification tray daemon.
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AgentNotifier;

internal static class Program
{
    private const int DefaultPort = 47821;
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly object LogLock = new();

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\AgentNotifier.Server.47821", out bool createdNew);
        if (!createdNew) return;

        if (!TryGetStartupPort(args, out int port, out string? portError))
        {
            MessageBox.Show(portError, "AgentNotifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        WebApplication? server = null;
        AppNotificationManager? notificationManager = null;

        try
        {
            notificationManager = AppNotificationManager.Default;
            notificationManager.NotificationInvoked += (_, _) => { };
            notificationManager.Register();
            Log("Windows notification manager registered.");
            server = StartServer(notificationManager, port);
            server.StartAsync().GetAwaiter().GetResult();
            SaveLastPort(port);
            Log($"AgentNotifier started on http://localhost:{port}");
            ShowStartupNotification(notificationManager, port);
            using var portChangeGate = new SemaphoreSlim(1, 1);
            using var tray = new TrayApplicationContext(port, async newPort =>
            {
                await portChangeGate.WaitAsync();

                if (newPort == port)
                {
                    portChangeGate.Release();
                    return PortChangeResult.Success;
                }

                WebApplication? replacement = null;
                WebApplication? previous = server;
                int previousPort = port;

                try
                {
                    if (previous is null)
                        throw new InvalidOperationException("The current HTTP server is not available.");

                    // Stop first so changing the port behaves like a clean
                    // close and reopen of the listener. Awaiting keeps the
                    // Windows message loop responsive while Kestrel stops.
                    await previous.StopAsync(TimeSpan.FromSeconds(3));
                    server = null;

                    replacement = StartServer(notificationManager!, newPort);
                    await replacement.StartAsync();

                    server = replacement;
                    replacement = null;
                    port = newPort;
                    SaveLastPort(port);

                    try { await previous.DisposeAsync(); }
                    catch (Exception disposeError) { Log($"Previous server disposal error: {disposeError}"); }

                    Log($"AgentNotifier moved to http://localhost:{port}");
                    ShowPortChangedNotification(
                        notificationManager!,
                        previousPort,
                        port);
                    return PortChangeResult.Success;
                }
                catch (Exception ex)
                {
                    if (replacement is not null)
                    {
                        try { await replacement.DisposeAsync(); }
                        catch { }
                    }

                    // A failed replacement must never terminate the tray
                    // app. Restore the previous listener when possible.
                    if (server is null && previous is not null)
                    {
                        try
                        {
                            await previous.StartAsync();
                            server = previous;
                            Log($"Port change failed; restored localhost:{port}.");
                        }
                        catch (Exception restoreError)
                        {
                            Log($"Could not restore localhost:{port}: {restoreError}");
                        }
                    }

                    Log($"Port change failed: {ex}");
                    return new PortChangeResult(
                        false,
                        $"Could not listen on localhost:{newPort}.\n\n{ex.Message}\n\nThe previous port ({port}) was restored when possible.");
                }
                finally
                {
                    portChangeGate.Release();
                }
            });
            Application.Run(tray);
            Log("Exit requested.");
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
                MessageBox.Show($"""
                    AgentNotifier could not start.

                    {ex.Message}

                    Log file:

                    {GetLogFilePath()}
                    """, "AgentNotifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (server is not null)
            {
                try { server.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
                catch (Exception ex) { Log($"Server shutdown error: {ex}"); }
                try { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
            if (notificationManager is not null)
            {
                try { notificationManager.Unregister(); Log("Windows notification manager unregistered."); }
                catch (Exception ex) { Log($"Notification unregister error: {ex}"); }
            }
            Log("AgentNotifier stopped.");
        }
    }

    private static bool TryGetStartupPort(
        string[] args,
        out int port,
        out string? error)
    {
        port = DefaultPort;
        error = null;
        bool portSpecified = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            string? value = argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase)
                ? argument[7..]
                : argument.Equals("--port", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length
                    ? args[++index]
                    : null;

            if (value is null)
            {
                if (argument.Equals("--port", StringComparison.OrdinalIgnoreCase))
                {
                    error = "The --port option needs a value from 1 to 65535.";
                    return false;
                }

                continue;
            }

            if (!TryParsePort(value, out port))
            {
                error = $"'{value}' is not a valid port. Use a whole number from 1 to 65535.";
                return false;
            }

            portSpecified = true;
        }

        if (!portSpecified)
            port = LoadLastPort() ?? DefaultPort;

        return true;
    }

    private static bool TryParsePort(string value, out int port) =>
        int.TryParse(value, out port) && port is >= 1 and <= 65535;

    private static WebApplication StartServer(
        AppNotificationManager notificationManager,
        int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port);
            options.Limits.MaxRequestBodySize = MaxRequestBytes;
        });
        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { ok = true, service = "agent-notify", port }));
        app.MapPost("/notify", (NotifyRequest body) =>
        {
            try
            {
                string level = string.IsNullOrWhiteSpace(body.Level) ? "success" : body.Level.Trim().ToLowerInvariant();
                if (level is not ("info" or "success" or "warning" or "error"))
                    return Results.BadRequest(new { ok = false, error = "level must be info, success, warning, or error" });
                string source = NormalizeText(body.Source, "agent", 80);
                string title = NormalizeText(body.Title, DefaultTitle(level), 120);
                string message = NormalizeText(body.Message, "Task completed.", 1500);
                Uri? openUri = null;
                if (!string.IsNullOrWhiteSpace(body.Url) && !TryGetAllowedUri(body.Url, out openUri))
                    return Results.BadRequest(new { ok = false, error = "url must use an allowed URI scheme", allowedSchemes = new[] { "https", "http", "vscode", "vscode-insiders", "cursor" } });

                var toastBuilder = new AppNotificationBuilder().AddText(title).AddText(message).AddText($"{LevelLabel(level)} \u2022 {source}").SetAudioEvent(SoundForLevel(level));
                AddTaskCompleteImage(toastBuilder);
                if (openUri is not null) toastBuilder.AddButton(new AppNotificationButton("Open").SetInvokeUri(openUri));
                AppNotification notification = toastBuilder.BuildNotification();
                if (!string.IsNullOrWhiteSpace(body.Id))
                {
                    notification.Tag = MakeNotificationTag(source, body.Id);
                    notification.Group = "agentnotify";
                }
                notificationManager.Show(notification);
                Log($"Notification: source={source}, level={level}, id={body.Id ?? "-"}, title={title}");
                return Results.Ok(new { ok = true, source, level, id = body.Id });
            }
            catch (Exception ex)
            {
                Log($"Notification error: {ex}");
                return Results.Json(new { ok = false, error = "notification_failed" }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
        return app;
    }

    private sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private int _port;

        public TrayApplicationContext(
            int port,
            Func<int, Task<PortChangeResult>> setPort)
        {
            _port = port;
            _menu = new ContextMenuStrip();
            var nameItem = new ToolStripMenuItem("AgentNotifier") { Enabled = false };
            _statusItem = new ToolStripMenuItem($"Listening on localhost:{port}") { Enabled = false };
            var setPortItem = new ToolStripMenuItem("Set port…");
            var exitItem = new ToolStripMenuItem("Exit");

            setPortItem.Click += async (_, _) =>
            {
                int? selectedPort = PromptForPort(_port);

                if (selectedPort is not int newPort)
                    return;

                setPortItem.Enabled = false;

                try
                {
                    PortChangeResult result = await setPort(newPort);

                    if (result.Succeeded)
                    {
                        _port = newPort;
                        _statusItem.Text = $"Listening on localhost:{newPort}";
                        _notifyIcon!.Text = $"AgentNotifier - localhost:{newPort}";
                    }
                    else
                    {
                        MessageBox.Show(
                            result.Error,
                            "AgentNotifier",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
                finally
                {
                    setPortItem.Enabled = true;
                }
            };

            exitItem.Click += (_, _) => { _notifyIcon!.Visible = false; ExitThread(); };
            _menu.Items.Add(nameItem); _menu.Items.Add(_statusItem); _menu.Items.Add(new ToolStripSeparator()); _menu.Items.Add(setPortItem); _menu.Items.Add(exitItem);
            Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Exclamation;
            _notifyIcon = new NotifyIcon { Icon = appIcon, Text = $"AgentNotifier - localhost:{port}", ContextMenuStrip = _menu, Visible = true };
        }

        private static int? PromptForPort(int currentPort)
        {
            using Image? taskCompleteImage = LoadTaskCompleteImage();
            using var dialog = new Form
            {
                Text = "Set AgentNotifier port",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(480, 250)
            };
            var label = new Label { Text = "Localhost port (1–65535):", AutoSize = true, Location = new Point(194, 34) };
            var input = new TextBox { Text = currentPort.ToString(), Location = new Point(197, 58), Width = 264 };
            input.Enter += (_, _) => input.SelectAll();
            var detail = new Label
            {
                Text = "AgentNotifier will stop listening, then start on the selected port.",
                Location = new Point(197, 98),
                Size = new Size(264, 48)
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(305, 194), Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(386, 194), Width = 75 };
            var robot = new PictureBox
            {
                Image = taskCompleteImage,
                Location = new Point(16, 26),
                Size = new Size(160, 160),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            dialog.Controls.AddRange(new Control[] { robot, label, input, detail, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            while (dialog.ShowDialog() == DialogResult.OK)
            {
                if (TryParsePort(input.Text, out int port))
                    return port;

                MessageBox.Show("Enter a whole number from 1 to 65535.", "AgentNotifier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return null;
        }

        private static Image? LoadTaskCompleteImage()
        {
            try
            {
                string imageFile = Path.Combine(
                    AppContext.BaseDirectory,
                    "Resources",
                    "task-complete-robot.png");

                return File.Exists(imageFile)
                    ? Image.FromFile(imageFile)
                    : null;
            }
            catch (Exception ex)
            {
                Log($"Could not load the port dialog image: {ex}");
                return null;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _menu.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private static AppNotificationSoundEvent SoundForLevel(string level) => level switch
    {
        "info" => AppNotificationSoundEvent.Default,
        "success" => AppNotificationSoundEvent.Reminder,
        "warning" => AppNotificationSoundEvent.Alarm2,
        "error" => AppNotificationSoundEvent.Alarm,
        _ => AppNotificationSoundEvent.Default
    };

    private static void ShowPortChangedNotification(
        AppNotificationManager notificationManager,
        int previousPort,
        int newPort)
    {
        try
        {
            var toastBuilder = new AppNotificationBuilder()
                .AddText("AgentNotifier port changed")
                .AddText(
                    $"Listening moved from localhost:{previousPort} to localhost:{newPort}.")
                .AddText("SUCCESS • AgentNotifier")
                .SetAudioEvent(AppNotificationSoundEvent.Reminder);

            AddTaskCompleteImage(toastBuilder);
            AppNotification notification = toastBuilder.BuildNotification();

            notificationManager.Show(notification);
        }
        catch (Exception ex)
        {
            // The listener has already changed successfully; a toast failure
            // must not undo that operation.
            Log($"Port change notification error: {ex}");
        }
    }

    private static void ShowStartupNotification(
        AppNotificationManager notificationManager,
        int port)
    {
        try
        {
            var toastBuilder = new AppNotificationBuilder()
                .AddText("AgentNotifier started")
                .AddText($"Listening on localhost:{port}.")
                .AddText("SUCCESS • AgentNotifier")
                .SetAudioEvent(AppNotificationSoundEvent.Default);

            AddTaskCompleteImage(toastBuilder);
            AppNotification notification = toastBuilder.BuildNotification();

            notificationManager.Show(notification);
        }
        catch (Exception ex)
        {
            Log($"Startup notification error: {ex}");
        }
    }

    private static void AddTaskCompleteImage(
        AppNotificationBuilder toastBuilder)
    {
        try
        {
            string imageFile = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "task-complete-robot.png");

            if (File.Exists(imageFile))
            {
                toastBuilder.SetAppLogoOverride(
                    new Uri(imageFile),
                    AppNotificationImageCrop.Circle,
                    "Task complete robot");
            }
            else
            {
                Log($"Toast image not found: {imageFile}");
            }
        }
        catch (Exception ex)
        {
            // Notifications remain usable even if the optional artwork fails.
            Log($"Toast image error: {ex}");
        }
    }

    private static string DefaultTitle(string level) => level switch { "info" => "Agent notification", "success" => "Task complete", "warning" => "Agent warning", "error" => "Agent error", _ => "Agent notification" };
    private static string LevelLabel(string level) => level switch { "info" => "INFO", "success" => "SUCCESS", "warning" => "WARNING", "error" => "ERROR", _ => level.ToUpperInvariant() };
    private static bool TryGetAllowedUri(string value, out Uri? uri)
    {
        uri = null;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)) return false;
        if (parsed.Scheme.ToLowerInvariant() is not ("https" or "http" or "vscode" or "vscode-insiders" or "cursor")) return false;
        uri = parsed; return true;
    }
    private static string MakeNotificationTag(string source, string id) => "n" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{id}"))).ToLowerInvariant()[..15];
    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length > maxLength ? result[..(maxLength - 1)] + "…" : result;
    }
    private static string GetLogFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentNotifier", "agentnotify.log");

    private static string GetSettingsFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentNotifier", "settings.json");

    private static int? LoadLastPort()
    {
        try
        {
            string settingsFile = GetSettingsFilePath();

            if (!File.Exists(settingsFile))
                return null;

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile));
            return settings is not null && TryParsePort(settings.Port.ToString(), out int port)
                ? port
                : null;
        }
        catch (Exception ex)
        {
            Log($"Could not read saved port: {ex}");
            return null;
        }
    }

    private static void SaveLastPort(int port)
    {
        try
        {
            string settingsFile = GetSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(new AppSettings(port)));
        }
        catch (Exception ex)
        {
            Log($"Could not save port {port}: {ex}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                try
                {
                    AppendLog(GetLogFilePath(), message);
                }
                catch
                {
                    // Useful when the process is launched by a service or a
                    // restricted account without access to the user profile.
                    AppendLog(
                        Path.Combine(AppContext.BaseDirectory, "agentnotify.log"),
                        message);
                }
            }
        }
        catch { }
    }

    private static void AppendLog(string logFile, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        File.AppendAllText(
            logFile,
            $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
    }

    private sealed record PortChangeResult(bool Succeeded, string? Error)
    {
        public static PortChangeResult Success { get; } = new(true, null);
    }

    private sealed record AppSettings(int Port);

    public sealed record NotifyRequest(string? Source, string? Title, string? Message, string? Level, string? Url, string? Id);
}
