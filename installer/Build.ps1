param(
    [string]$Compiler = '',
    [string]$OutputPath = '',
    [string]$ToolsDirectory = (Join-Path $env:TEMP 'ScamOllama-build')
)
$ErrorActionPreference = 'Stop'
if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'setup.exe' }
if (!$Compiler) {
    $directory = Join-Path $ToolsDirectory 'microsoft.net.compilers.toolset-4.14.0'
    $Compiler = Join-Path $directory 'tasks\net472\csc.exe'
    if (!(Test-Path -LiteralPath $Compiler)) {
        New-Item -ItemType Directory -Force -Path $ToolsDirectory | Out-Null
        $zip = Join-Path $ToolsDirectory 'roslyn-4.14.0.zip'
        Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/4.14.0/microsoft.net.compilers.toolset.4.14.0.nupkg' -OutFile $zip
        Expand-Archive -LiteralPath $zip -DestinationPath $directory -Force
    }
}
& $Compiler /nologo /target:exe /platform:x64 /optimize+ /debug- /deterministic+ "/pathmap:$PSScriptRoot=/_/ScamOllama/installer" "/out:$OutputPath" "/win32manifest:$(Join-Path $PSScriptRoot 'setup.manifest')" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$(Join-Path $PSScriptRoot 'Payload.zip'),ScamOllama.Payload" (Join-Path $PSScriptRoot 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
Write-Output "Built standalone console installer: $OutputPath"
