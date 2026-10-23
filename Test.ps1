param([Parameter(Mandatory=$true)][string]$RuntimeRoot,[string]$BuildToolsRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'GTFO-Mod-Build-and-Verification-Tools'))
$ErrorActionPreference='Stop'
& (Join-Path $BuildToolsRoot 'tools/Test-Recipes.ps1') -RepositoryRoot $PSScriptRoot -RuntimeRoot $RuntimeRoot
$build=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'out/build-manifest.json') -Raw|ConvertFrom-Json
$gameReferenceRoot=$build.GameRoot
$out=Join-Path $PSScriptRoot 'out/tests/ownership';New-Item -ItemType Directory -Path $out -Force|Out-Null
$refs=@(Get-ChildItem -LiteralPath $RuntimeRoot -Filter '*.dll'|Where-Object{$_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.'}|ForEach-Object FullName)
$harmony=Join-Path $gameReferenceRoot 'BepInEx/core/0Harmony.dll'
function Compile([string]$name,[string]$target,[string[]]$sources,[string[]]$extra){
 $files=@($sources|ForEach-Object{Join-Path $PSScriptRoot $_})
 & (Join-Path $BuildToolsRoot 'tools/Compile.ps1') -Sources $files -References @($refs+$extra) -Output (Join-Path $out $name) -Executable:($target -eq 'exe') | Tee-Object -FilePath (Join-Path $out ($name+'-compile.log')) | Out-Null
}
Compile 'TheArchive.Core.dll' 'library' @('tests/OwnershipFramework.cs') @()
Compile 'Hikaria.Core.dll' 'library' @('tests/OwnershipCore.cs') @(Join-Path $out 'TheArchive.Core.dll')
Compile 'OwnershipTests.dll' 'exe' @('src/MaxConsumables/CoreOverlap.cs','tests/OwnershipTests.cs') @($harmony,(Join-Path $out 'TheArchive.Core.dll'))
# The actual Harmony dependency stays next to the runner. Resolve its supporting
# assemblies from the installed framework, without writing in the game folder.
Copy-Item -LiteralPath $harmony -Destination $out -Force
'{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"}}}' | Set-Content -LiteralPath (Join-Path $out 'OwnershipTests.runtimeconfig.json') -Encoding utf8
foreach($scenario in @('early','late')){
    & dotnet (Join-Path $out 'OwnershipTests.dll') (Join-Path $gameReferenceRoot 'BepInEx\core') (Join-Path $out 'Hikaria.Core.dll') $scenario | Tee-Object -FilePath (Join-Path $out ($scenario+'.log'))
    if($LASTEXITCODE -ne 0){throw "所有权实际Harmony测试失败：$scenario"}
}
