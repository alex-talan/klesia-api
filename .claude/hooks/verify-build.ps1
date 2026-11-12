# Stop hook: before Claude finishes, build and test what changed.
# Exit code 2 = keep Claude working and show it the failure output.
$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if (-not [string]::IsNullOrWhiteSpace($raw)) {
    $payload = $raw | ConvertFrom-Json
    # Already continuing because of this hook: do not loop forever.
    if ($payload.stop_hook_active -eq $true) { exit 0 }
}

$root = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    [Console]::Error.WriteLine('verify-build: dotnet SDK not found on PATH; build and tests were NOT verified.')
    exit 0
}

$changed = @(git status --porcelain 2>$null)
if ($changed.Count -eq 0) { exit 0 }

$dotnetChanged = $changed | Where-Object { $_ -match '(src/|tests/|\.sln|\.csproj|Directory\.(Build|Packages)\.props|global\.json)' }
$databaseChanged = $changed | Where-Object { $_ -match 'database/' }

function Invoke-Step([string]$Title, [string[]]$Arguments) {
    $output = & dotnet @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $tail = ($output -split "`r?`n" | Select-Object -Last 60) -join "`n"
        [Console]::Error.WriteLine("$Title failed:`n$tail")
        exit 2
    }
}

if ($dotnetChanged) {
    Invoke-Step 'dotnet build' @('build', 'Klesia.sln', '--nologo', '-v', 'q')
    Invoke-Step 'dotnet test' @('test', 'Klesia.sln', '--no-build', '--nologo', '-v', 'q')
}

if ($databaseChanged) {
    Invoke-Step 'Database migrator build' @('build', 'database/Klesia.Database/Klesia.Database.csproj', '-c', 'Debug', '--nologo', '-v', 'q')
}

exit 0
