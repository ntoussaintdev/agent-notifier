$ErrorActionPreference = "Stop"

$DefaultPort = 47821

function Show-Usage {
    @"
Usage:
  notify.ps1 --message <text> --harness <name> [options]

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
"@
}

function Require-Value {
    param(
        [string]$Flag,
        [int]$Index,
        [object[]]$Arguments
    )

    if (($Index + 1) -ge $Arguments.Count) {
        throw "Missing value after $Flag"
    }
}

function Validate-Port {
    param([string]$Value)

    $parsed = 0

    if (-not [int]::TryParse($Value, [ref]$parsed) -or
        $parsed -lt 1 -or
        $parsed -gt 65535) {
        throw "Port must be an integer from 1 to 65535. Current value: $Value"
    }

    return $parsed
}

$message = $null
$harness = $null
$level = "success"
$title = $null
$id = $null
$url = $null
$port = $null

for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string]$args[$i]

    switch ($arg) {
        "--message" {
            Require-Value $arg $i $args
            $i++
            $message = [string]$args[$i]
        }

        "--harness" {
            Require-Value $arg $i $args
            $i++
            $harness = [string]$args[$i]
        }

        "--port" {
            Require-Value $arg $i $args
            $i++
            $port = [string]$args[$i]
        }

        "--level" {
            Require-Value $arg $i $args
            $i++
            $level = [string]$args[$i]
        }

        "--title" {
            Require-Value $arg $i $args
            $i++
            $title = [string]$args[$i]
        }

        "--id" {
            Require-Value $arg $i $args
            $i++
            $id = [string]$args[$i]
        }

        "--url" {
            Require-Value $arg $i $args
            $i++
            $url = [string]$args[$i]
        }

        "--help" {
            Show-Usage
            exit 0
        }

        "-h" {
            Show-Usage
            exit 0
        }

        default {
            throw "Unknown argument: $arg`n`n$(Show-Usage)"
        }
    }
}

if ([string]::IsNullOrWhiteSpace($message)) {
    throw "--message is required.`n`n$(Show-Usage)"
}

if ([string]::IsNullOrWhiteSpace($harness)) {
    throw "--harness is required.`n`n$(Show-Usage)"
}

$level = $level.Trim().ToLowerInvariant()

if ($level -notin @(
        "info",
        "success",
        "warning",
        "error")) {
    throw "--level must be one of: info, success, warning, error"
}

# Port precedence:
#   1. --port
#   2. AGENT_NOTIFY_PORT
#   3. default 47821
if ([string]::IsNullOrWhiteSpace($port)) {
    $port = $env:AGENT_NOTIFY_PORT
}

if ([string]::IsNullOrWhiteSpace($port)) {
    $port = [string]$DefaultPort
}

$port = Validate-Port $port

$endpoint = "http://127.0.0.1:$port"

$payload = [ordered]@{
    source  = $harness.Trim()
    message = $message
    level   = $level
}

if (-not [string]::IsNullOrWhiteSpace($title)) {
    $payload.title = $title
}

if (-not [string]::IsNullOrWhiteSpace($id)) {
    $payload.id = $id
}

if (-not [string]::IsNullOrWhiteSpace($url)) {
    $payload.url = $url
}

$json =
    $payload |
    ConvertTo-Json -Compress -Depth 4

try {
    Invoke-RestMethod `
        -Method Post `
        -Uri "$endpoint/notify" `
        -ContentType "application/json; charset=utf-8" `
        -Body $json `
        -TimeoutSec 5 |
        Out-Null

    Write-Output "AgentNotify: notification sent via $endpoint"
    exit 0
}
catch {
    Write-Error @"
AgentNotify: failed to send notification to $endpoint

$($_.Exception.Message)

Port precedence:
  --port
  AGENT_NOTIFY_PORT
  default: $DefaultPort
"@

    exit 1
}
