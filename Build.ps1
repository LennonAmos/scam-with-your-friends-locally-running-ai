param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$Compiler = '',
    [string]$FrameworkReferences = '',
    [string]$ToolsDirectory = (Join-Path $env:TEMP 'ScamOllama-build')
)
$ErrorActionPreference = 'Stop'
function Get-Package([string]$Name, [string]$Version) {
    $destination = Join-Path $ToolsDirectory "$Name-$Version"
    if (!(Test-Path -LiteralPath $destination)) {
        New-Item -ItemType Directory -Force -Path $ToolsDirectory | Out-Null
        $archive = Join-Path $ToolsDirectory "$Name-$Version.zip"
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$Name/$Version/$Name.$Version.nupkg" -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $destination
    }
    return $destination
}
if (!$Compiler) { $Compiler = Join-Path (Get-Package 'microsoft.net.compilers.toolset' '4.14.0') 'tasks\net472\csc.exe' }
if (!$FrameworkReferences) { $FrameworkReferences = Join-Path (Get-Package 'microsoft.netframework.referenceassemblies.net472' '1.0.3') 'build\.NETFramework\v4.7.2' }
$managed = Join-Path $GamePath 'Scam With Your Friends_Data\Managed'
$references = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { '/reference:' + (Join-Path $FrameworkReferences $_) }
$references += @('PocketTTS.dll','Kolkata.Speech.dll','Newtonsoft.Json.dll','Assembly-CSharp.dll','ScriptsAssDef.dll','Mirror.dll','UniTask.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.UIElementsModule.dll','UnityEngine.UnityWebRequestModule.dll','UnityEngine.InputLegacyModule.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$outPath = Join-Path $PSScriptRoot 'payload\ScamOllama.dll'
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
& $Compiler /nologo /target:library /langversion:latest /optimize+ /nostdlib+ /debug- /deterministic+ "/pathmap:$PSScriptRoot=/_/ScamOllama" "/out:$outPath" @references (Join-Path $PSScriptRoot 'src\OllamaProtocol.cs') (Join-Path $PSScriptRoot 'src\OllamaHttp.cs') (Join-Path $PSScriptRoot 'src\Plugin.cs') (Join-Path $PSScriptRoot 'src\LocalConfig.cs') (Join-Path $PSScriptRoot 'src\DialogueTokenRepair.cs') (Join-Path $PSScriptRoot 'src\RickBurps.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $outPath"

& $Compiler /nologo /target:exe /debug- /deterministic+ "/pathmap:$PSScriptRoot=/_/ScamOllama" "/out:$(Join-Path $PSScriptRoot 'tools\Patcher.exe')" "/reference:$(Join-Path $PSScriptRoot 'tools\Mono.Cecil.dll')" "/reference:$(Join-Path $PSScriptRoot 'tools\Mono.Cecil.Rocks.dll')" (Join-Path $PSScriptRoot 'src\Patcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed.' }
