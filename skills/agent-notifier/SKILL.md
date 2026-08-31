---
name: agent-notifier
description: Always send a notification to the user after every completed agent response. Include the status type and active harness name (for example, Claude CLI, Codex, or Kilo CLI).
metadata:
  version: "1.8"
---

# Notify User

Use this skill to alert the user through the AgentNotify service running on their Windows machine.

It requires shell execution: PowerShell on Windows, or Bash and curl on Linux/WSL. AgentNotify must be reachable on the selected port.

On Windows, invoke `notify.ps1` through `powershell.exe -NoProfile -ExecutionPolicy Bypass -File`; do not execute the script directly.

## When to notify

Default behavior is mandatory: send one notification after every completed agent response, including brief answers and small completed tasks. Do this without waiting for the user to request a notification.

Use long-task-only notifications only when the user explicitly instructs that behavior for the current session or request. In that case, suppress notifications for shorter responses; do not pass notification mode to the script.

In `always` mode, send exactly one notification after this response to the current user message is complete, including a brief answer or a completed small task.

In `long-tasks` mode, send one notification only when:

- a substantial requested task has completed;
- a long-running command, build, test, migration, or agent workflow has finished;
- the task failed in a way that needs the user's attention;
- progress is blocked and the agent cannot continue without user input.

Only the agent producing the response for the current user message may send this notification. Never notify for subagent completion, intermediate steps, routine tool calls, or repeated terminal events. If the response is interrupted or superseded before it completes, do not notify.

Notification is best-effort. If sending it fails, do not treat the user's main task as failed and do not repeatedly retry unless the user asks you to debug notifications.

## Script location

The notification scripts are in this skill's `bin/` directory:

- Windows: `bin/notify.ps1`
- Linux / WSL: `bin/notify.sh`

Resolve the directory containing this `SKILL.md` to an absolute path before invoking a script. Prefer the absolute script path. Do not assume the agent's current working directory is the skill directory.

## Required values

Always provide:

- `--message`: a short, useful description of what finished or what needs attention.
- `--harness`: the current agent harness, for example `claude-code`, `codex`, `cursor`, `aider`, or another stable harness name. This identifies the harness in the notification and must always name the actual responding harness.

Use `agent` only if the harness genuinely cannot be identified. Never omit the field or use a different harness's name.

## Optional values

- `--level`: `info`, `success`, `warning`, or `error`. Default: `success`.
- `--title`: custom toast title. Usually omit it and let AgentNotify choose the default.
- `--id`: stable identifier for a task/event. Reusing the same source/harness and ID lets the server replace the previous notification.
- `--url`: optional HTTP/HTTPS/editor URI for the toast's Open button.
- `--port`: AgentNotify's localhost port. Pass this directly to the script when the notifier is using a non-default port.

When `--port` is omitted, the scripts use `AGENT_NOTIFY_PORT`, then the default port `47821`.

## Windows invocation

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<absolute-skill-dir>\bin\notify.ps1" --message "<one-line summary>" --harness "<harness>" --level <info|success|warning|error> --port <port>
```

Example:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\path\to\skills\notify-user\bin\notify.ps1" --message "Authentication flow implemented; all tests pass." --harness "codex" --level success --port 49000
```

## Linux / WSL invocation

Run:

```bash
bash "<absolute-skill-dir>/bin/notify.sh" --message "<one-line summary>" --harness "<harness>" --level <info|success|warning|error> --port <port>
```

Example:

```bash
bash "/path/to/skills/notify-user/bin/notify.sh" --message "Authentication flow implemented; all tests pass." --harness "claude-code" --level success --port 49000
```

## Severity

Always pass `--level` explicitly. Use:

- `success` when the requested work completed normally;
- `info` for useful completion/status information that is not specifically success/failure;
- `warning` when user attention is required but the task has not definitively failed;
- `error` when the task failed or cannot proceed.

## Message quality

Use a single concise line that summarizes the completed response. Prefer:

> Finished the API refactor; 128 tests pass.

over:

> Done.

Do not put secrets, credentials, tokens, or unnecessarily sensitive content in notification messages.

## Port behavior

Both scripts send to `http://127.0.0.1:<port>`. Port precedence is:

1. `--port <port>` passed to the called script;
2. `AGENT_NOTIFY_PORT` environment variable;
3. default port `47821`.

Use `--port` whenever the active AgentNotify instance is configured to a non-default port. The scripts validate that the value is an integer from 1 to 65535 before sending a request.
