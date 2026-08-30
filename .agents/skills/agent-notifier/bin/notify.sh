#!/usr/bin/env bash

set -u

DEFAULT_PORT=47821

usage() {
  cat <<'EOF'
Usage:
  notify.sh --message <text> --harness <name> [options]

Required:
  --message <text>       Notification message
  --harness <name>       Agent harness/source, e.g. claude-code, codex

Options:
  --port <port>          AgentNotify port
  --level <level>        info | success | warning | error (default: success)
  --title <text>         Optional notification title
  --id <id>              Optional stable notification ID
  --url <uri>            Optional URI for the toast's Open button
  --help                 Show this help

Port precedence:
  1. --port
  2. AGENT_NOTIFY_PORT environment variable
  3. 47821
EOF
}

die() {
  printf 'AgentNotify: %s\n' "$*" >&2
  exit 1
}

need_value() {
  local flag="$1"
  local remaining="$2"

  if (( remaining < 2 )); then
    die "missing value after $flag"
  fi
}

validate_port() {
  local value="$1"

  if [[ ! "$value" =~ ^[0-9]+$ ]] ||
     (( value < 1 || value > 65535 )); then
    die "port must be an integer from 1 to 65535. Current value: $value"
  fi
}

json_escape() {
  local s="$1"

  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\n'/\\n}
  s=${s//$'\r'/\\r}
  s=${s//$'\t'/\\t}
  s=${s//$'\b'/\\b}
  s=${s//$'\f'/\\f}

  printf '%s' "$s"
}

message=""
harness=""
level="success"
title=""
id=""
url=""
port=""

while (($#)); do
  case "$1" in
    --message)
      need_value "$1" "$#"
      message="$2"
      shift 2
      ;;

    --harness)
      need_value "$1" "$#"
      harness="$2"
      shift 2
      ;;

    --port)
      need_value "$1" "$#"
      port="$2"
      shift 2
      ;;

    --level)
      need_value "$1" "$#"
      level="$2"
      shift 2
      ;;

    --title)
      need_value "$1" "$#"
      title="$2"
      shift 2
      ;;

    --id)
      need_value "$1" "$#"
      id="$2"
      shift 2
      ;;

    --url)
      need_value "$1" "$#"
      url="$2"
      shift 2
      ;;

    --help|-h)
      usage
      exit 0
      ;;

    *)
      die "unknown argument: $1"$'\n\n'"$(usage)"
      ;;
  esac
done

[[ -n "$message" ]] ||
  die "--message is required"$'\n\n'"$(usage)"

[[ -n "$harness" ]] ||
  die "--harness is required"$'\n\n'"$(usage)"

level=$(
  printf '%s' "$level" |
    tr '[:upper:]' '[:lower:]'
)

case "$level" in
  info|success|warning|error)
    ;;
  *)
    die "--level must be one of: info, success, warning, error"
    ;;
esac

command -v curl >/dev/null 2>&1 ||
  die "curl is required but was not found in PATH"

# Port precedence:
#   1. --port
#   2. AGENT_NOTIFY_PORT
#   3. default 47821
if [[ -z "$port" ]]; then
  port="${AGENT_NOTIFY_PORT:-}"
fi

if [[ -z "$port" ]]; then
  port="$DEFAULT_PORT"
fi

validate_port "$port"

endpoint="http://127.0.0.1:${port}"

source_json=$(json_escape "$harness")
message_json=$(json_escape "$message")
level_json=$(json_escape "$level")

payload=$(printf \
  '{"source":"%s","message":"%s","level":"%s"' \
  "$source_json" \
  "$message_json" \
  "$level_json")

if [[ -n "$title" ]]; then
  payload+=',"title":"'"$(json_escape "$title")"'"'
fi

if [[ -n "$id" ]]; then
  payload+=',"id":"'"$(json_escape "$id")"'"'
fi

if [[ -n "$url" ]]; then
  payload+=',"url":"'"$(json_escape "$url")"'"'
fi

payload+='}'

if curl -fsS \
    --connect-timeout 2 \
    --max-time 5 \
    -X POST \
    -H 'Content-Type: application/json; charset=utf-8' \
    --data-binary "$payload" \
    "$endpoint/notify" \
    >/dev/null; then

  printf \
    'AgentNotify: notification sent via %s\n' \
    "$endpoint"

  exit 0
fi

cat >&2 <<EOF
AgentNotify: failed to send notification to $endpoint

Port precedence:
  --port
  AGENT_NOTIFY_PORT
  default: $DEFAULT_PORT
EOF

exit 1
