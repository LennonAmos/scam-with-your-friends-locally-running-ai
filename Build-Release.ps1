param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$Compiler = '',
    [string]$FrameworkReferences = '',
    [string]$ToolsDirectory = (Join-Path $env:TEMP 'ScamOllama-build'),
    [string]$OutputDirectory = '',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
$version = '1.0.1'
# Explicit files only: never recursively package a development directory.
$sources = @(
    '.gitignore','AGENTS.md','README.md','LICENSE','Mono.Cecil-LICENSE.txt',
    'Build.ps1','Build-Release.ps1','Install.ps1','Uninstall.ps1',
    'src/Plugin.cs','src/LocalConfig.cs','src/OllamaProtocol.cs',
    'src/OllamaHttp.cs','src/DialogueTokenRepair.cs','src/Patcher.cs',
    'installer/Setup.cs','installer/Build.ps1','installer/setup.manifest',
    'tools/Mono.Cecil.dll','tools/Mono.Cecil.Rocks.dll'
)
function Check-PublishFiles {
    foreach ($relative in $sources) {
        $path = Join-Path $PSScriptRoot $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing public file: $relative" }
        if ([IO.Path]::GetExtension($path) -in @('.cs','.ps1','.md','.txt','.manifest')) {
            $text = [IO.File]::ReadAllText($path)
            if ($text -match '(?i)(?<![a-z0-9])[a-z]:[\\/]') { throw "Absolute local path in public source: $relative" }
            if ($text -match '(?i)(?:ghp_|gho_|github_pat_)[a-z0-9_]{20,}') { throw "Possible credential in public source: $relative" }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.git')) {
        $tracked = @(git -C $PSScriptRoot ls-files)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked files.' }
        foreach ($relative in $tracked) {
            if ($relative -notin $sources) { throw "Unexpected tracked file; publication stopped: $relative" }
        }
    }
    Write-Output 'Publish check passed: explicit product files, no absolute local paths or recognizable credentials.'
}
Check-PublishFiles
if ($CheckOnly) { return }
& (Join-Path $PSScriptRoot 'Build.ps1') -GamePath $GamePath -Compiler $Compiler -FrameworkReferences $FrameworkReferences -ToolsDirectory $ToolsDirectory
if (!$Compiler) { $Compiler = Join-Path $ToolsDirectory 'microsoft.net.compilers.toolset-4.14.0/tasks/net472/csc.exe' }
$pluginHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'payload/ScamOllama.dll')).Hash
$setupSource = Join-Path $PSScriptRoot 'installer/Setup.cs'
$source = [IO.File]::ReadAllText($setupSource)
$source = [regex]::Replace($source, 'const string PluginHash = "[A-F0-9]+";', ('const string PluginHash = "' + $pluginHash + '";'))
[IO.File]::WriteAllText($setupSource, $source)
Check-PublishFiles
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $ToolsDirectory 'release-staging'))
$stage = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ([Guid]::NewGuid().ToString('N'))))
if (!$stage.StartsWith($temporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging directory.' }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    foreach ($relative in ($sources + @('payload/ScamOllama.dll','tools/Patcher.exe'))) {
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relative) -Destination $destination
    }
    $package = Join-Path $OutputDirectory "ScamOllama-v$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $package -Force
    Copy-Item -LiteralPath $package -Destination (Join-Path $PSScriptRoot 'installer/Payload.zip') -Force
    & (Join-Path $PSScriptRoot 'installer/Build.ps1') -Compiler $Compiler -OutputPath (Join-Path $OutputDirectory 'setup.exe')
    $lines = @('setup.exe',"ScamOllama-v$version-win-x64.zip") | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $_)).Hash.ToLowerInvariant() + '  ' + $_
    }
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $lines)
    Write-Output "Built v$version release in $OutputDirectory"
}
finally {
    $checkedStage = [IO.Path]::GetFullPath($stage)
    if ($checkedStage.StartsWith($temporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $checkedStage)) {
        Remove-Item -LiteralPath $checkedStage -Recurse -Force
    }
}
