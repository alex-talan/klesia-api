# PostToolUse hook (Write|Edit|MultiEdit): applies the .editorconfig whitespace rules to the edited C# file.
# Best effort: it never blocks and never fails the tool call.
$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$payload = $raw | ConvertFrom-Json
$path = [string]$payload.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($path) -or $path -notmatch '\.cs$') { exit 0 }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { exit 0 }

$root = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

Push-Location $root
try {
    $relative = [System.IO.Path]::GetRelativePath($root, $path)
    dotnet format whitespace --folder --include "$relative" --verbosity quiet | Out-Null
}
finally {
    Pop-Location
}

exit 0
