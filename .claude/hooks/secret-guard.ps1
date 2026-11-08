# PreToolUse hook (Write|Edit|MultiEdit): blocks writing credentials into tracked files.
# Exit code 2 = block the tool call and show the message to Claude.
# Personal values belong in the git-ignored appsettings.Local.json, environment variables or a secret store.
$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$payload = $raw | ConvertFrom-Json
$toolInput = $payload.tool_input
if ($null -eq $toolInput) { exit 0 }

$path = [string]$toolInput.file_path
$normalized = $path -replace '\\', '/'

# Files that are allowed to hold personal values or that legitimately contain the patterns.
if ($normalized -match '/\.claude/hooks/' -or $normalized -match 'appsettings\.Local\.json$' -or $normalized -match '\.user$') { exit 0 }

$parts = @([string]$toolInput.content, [string]$toolInput.new_string)
if ($toolInput.edits) { foreach ($edit in $toolInput.edits) { $parts += [string]$edit.new_string } }
$text = $parts -join "`n"
if ([string]::IsNullOrWhiteSpace($text)) { exit 0 }

$rules = @(
    @{ Name = 'connection-string password';   Pattern = '(?i)\b(password|pwd)\s*=\s*[^;"''\s<{$%][^;"'']*' },
    @{ Name = 'storage/service bus key';      Pattern = '(?i)\b(AccountKey|SharedAccessKey)\s*=\s*[A-Za-z0-9+/=]{20,}' },
    @{ Name = 'SAS token signature';          Pattern = '(?i)[?&]sig=[A-Za-z0-9%+/=]{20,}' },
    @{ Name = 'JWT / bearer token';           Pattern = 'eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' },
    @{ Name = 'client secret';                Pattern = '(?i)"?client_?secret"?\s*[:=]\s*"[^"<{$%]{8,}"' },
    @{ Name = 'private key';                  Pattern = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' }
)

$hits = @()
foreach ($rule in $rules) {
    if ($text -match $rule.Pattern) { $hits += $rule.Name }
}

if ($hits.Count -gt 0) {
    [Console]::Error.WriteLine("Blocked: '$path' would contain a possible secret ($($hits -join ', ')).")
    [Console]::Error.WriteLine("Never put credentials in tracked files. Use placeholders such as <your-value>, appsettings.Local.json (git-ignored), environment variables or Azure Key Vault / managed identity.")
    exit 2
}

exit 0
