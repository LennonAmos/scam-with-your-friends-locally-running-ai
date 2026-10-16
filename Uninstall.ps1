param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$SetupPath = ''
)
$ErrorActionPreference = 'Stop'
if (!$SetupPath) {
    foreach ($candidate in @((Join-Path $PSScriptRoot 'setup.exe'), (Join-Path $PSScriptRoot 'dist/setup.exe'))) {
        if (Test-Path -LiteralPath $candidate) { $SetupPath = $candidate; break }
    }
}
if (!$SetupPath) { throw 'Download setup.exe beside this script, or run Build-Release.ps1 first.' }
& $SetupPath --game-path $GamePath --uninstall --no-pause
if ($LASTEXITCODE -ne 0) { throw 'Setup did not complete. Read its output for details.' }
