---
name: agent-notifier
description: Send a desktop notification through the local AgentNotify HTTP service when a substantial agent task completes, fails, or becomes blocked and needs user attention. Use after long-running or unattended work; do not use for trivial conversational replies or routine intermediate steps.
compatibility: Requires shell execution. Windows uses PowerShell. Linux/WSL uses Bash and curl. AgentNotify must be reachable on the supplied --port, AGENT_NOTIFY_PORT, or the default port 47821.
metadata:
  version: "1.1"
---

# Notify User

Use this skill to alert the user through the AgentNotify service running on their Windows machine.

## When to notify

Send one notification when:

- a substantial requested task has completed;
- a long-running command, build, test, migration, or agent workflow has finished;
- the task failed in a way that needs the user's attention;
- progress is blocked and the agent cannot continue without user input.

Do not notify:

- for trivial conversational answers;
- after every intermediate step;
- for routine tool calls;
- repeatedly for the same event.

Notification is best-effort. If sending it fails, do not treat the user's main task as failed and do not repeatedly retry unless the user asks you to debug notifications.

## Script location

The notification scripts are in this skill's `bin/` directory:

- Windows: `bin/notify.ps1`
- Linux / WSL: `bin/notify.sh`

Resolve the directory containing this `SKILL.md` to an absolute path before invoking a script. Prefer the absolute script path. Do not assume the agent's current working directory is the skill directory.

## Required values

Always provide:

- `--message`: a short, useful description of what finished or what needs attention.
- `--harness`: the current agent harness, for example `claude-code`, `codex`, `cursor`, `aider`, or another stable harness name.

Use `agent` only if the harness genuinely cannot be identified.

## Optional values

- `--level`: `info`, `success`, `warning`, or `error`. Default: `success`.
- `--title`: custom toast title. Usually omit it and let AgentNotify choose the default.
- `--id`: stable identifier for a task/event. Reusing the same source/harness and ID lets the server replace the previous notification.
- `--url`: optional HTTP/HTTPS/editor URI for the toast's Open button.
- `--port`: AgentNotify's localhost port. Pass this directly to the script when the notifier is using a non-default port.

When `--port` is omitted, the scripts use `AGENT_NOTIFY_PORT` and then the default port `47821`.

## Windows invocation

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<absolute-skill-dir>\bin\notify.ps1" --message "<brief message>" --harness "<harness>" --level success --port <port>
```

Example:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\path\to\skills\notify-user\bin\notify.ps1" --message "Finished implementing the authentication flow and all tests pass." --harness "codex" --level success --port 49000
```

## Linux / WSL invocation

Run:

```bash
bash "<absolute-skill-dir>/bin/notify.sh" --message "<brief message>" --harness "<harness>" --level success --port <port>
```

Example:

```bash
bash "/path/to/skills/notify-user/bin/notify.sh" --message "Finished implementing the authentication flow and all tests pass." --harness "claude-code" --level success --port 49000
```

## Severity

Use:

- `success` when the requested work completed normally;
- `info` for useful completion/status information that is not specifically success/failure;
- `warning` when user attention is required but the task has not definitively failed;
- `error` when the task failed or cannot proceed.

## Message quality

Keep notifications concise and specific. Prefer:

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
