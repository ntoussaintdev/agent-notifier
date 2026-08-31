---
name: agent-notifier
description: Notify the user after every completed parent-agent response. Include the status type and active harness name (for example, Claude CLI, Codex, or Kilo CLI).
metadata:
  version: "2.2"
---

# Notify User

Use this skill to alert the user through the AgentNotify service running on
their machine. Send the notification with a direct `curl` request; do not use
or create helper scripts.

## First-run setup

Before sending notifications in a project, add this exact line to the
project-root `AGENTS.md` and `CLAUDE.md` files:

> Always notify using the `agent-notifier` skill after every completed parent-agent response unless the user explicitly requests a different cadence, such as long-task-only notifications.

Place the rule immediately after an existing initial Markdown title, or as the
first non-empty line when no title exists. If the rule is already elsewhere,
move it to that near-start position rather than duplicating it. If neither file
exists, create both with the rule. If one exists, add the rule to it and create
the missing counterpart with the rule. Do not copy, compare, synchronize, or
otherwise alter the files' other contents. This setup is idempotent: later uses
must not duplicate the rule.

An explicit user-requested notification cadence overrides this persistent
default for the current request or session.

## When to notify

Only the parent agent producing the user-facing response sends notifications.
In the default mode, send exactly one notification after every completed final
response to the current user message, including a brief answer or a completed
small task.

Use long-task-only notifications only when the user explicitly requests that
cadence for the current session or request. In that mode, suppress
notifications for shorter responses; still notify when a long task completes,
fails, or blocks and needs user attention.

Never notify for intermediary reasoning, commentary progress updates, routine
tool calls, or repeated terminal events. If the final response is interrupted
or superseded before it completes, do not notify.

## Request contract

Send `POST` requests to `http://127.0.0.1:47821/notify`. Replace `47821` only
when the active AgentNotify service is known to use another port.

The JSON body requires:

- `source`: the responding parent harness, such as `codex` or `claude-code`;
- `message`: a concise one-line task status; and
- `level`: `info`, `success`, `warning`, or `error`.

You may also include `title`, `id`, or `url`. Use valid JSON, keep messages
plain and concise, and never include credentials, tokens, or other secrets.

Use this JSON shape (omit optional fields when unused):

```json
{
  "source": "codex",
  "message": "Implemented the requested change; checks pass.",
  "level": "success",
  "title": "Optional title",
  "id": "optional-stable-event-id",
  "url": "https://optional.example/link"
}
```

## Direct curl requests

On Bash, Linux, macOS, or WSL, run:

```bash
curl -fsS --connect-timeout 2 --max-time 5 \
  -X POST "http://127.0.0.1:47821/notify" \
  -H "Content-Type: application/json; charset=utf-8" \
  --data-binary '{"source":"codex","message":"Requested work completed; checks pass.","level":"success"}'
```

On Windows PowerShell, invoke `curl.exe` to avoid PowerShell's `curl` alias:

```powershell
curl.exe -fsS --connect-timeout 2 --max-time 5 `
  -X POST "http://127.0.0.1:47821/notify" `
  -H "Content-Type: application/json; charset=utf-8" `
  --data-binary '{"source":"codex","message":"Requested work completed; checks pass.","level":"success"}'
```

Notification is best-effort. If `curl` fails, do not fail the main task and do
not repeatedly retry unless the user asks you to diagnose notifications.
