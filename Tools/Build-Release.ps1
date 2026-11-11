#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9.-]+$')][string]$Tag,
      [string]$GameDir)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$GameDir){$GameDir=Join-Path $root 'Builds/CurrentGameXR'}
$output=Join-Path $root ('Builds/Releases/'+$Tag)
if(Test-Path -LiteralPath $output){throw 'Choose a new tag. Existing packages are immutable.'}
$game=(Resolve-Path -LiteralPath $GameDir).Path
New-Item -ItemType Directory -Path $output|Out-Null
$gameBinaries=[ordered]@{}
$contract=Get-Content -LiteralPath (Join-Path $root 'ModPlugin/SubsystemProbe/EngineBinaryContract.cs') -Raw
foreach($m in [regex]::Matches($contract,'\("([^"]+\.dll)", "([A-F0-9]{64})"\)')){
    $gameBinaries[$m.Groups[1].Value]=$m.Groups[2].Value
    if((Get-FileHash -LiteralPath (Join-Path $game $m.Groups[1].Value)).Hash -ne $m.Groups[2].Value){throw 'Build references do not match the supported game.'}
}
if($gameBinaries.Count -ne 2){throw 'Could not read the exact game binary contract.'}
$artifacts=@()
foreach($role in @('VR','Companion')){
    $project=if($role -eq 'VR'){'SubsystemProbe'}else{'VRCompanion'}
    $dll=if($role -eq 'VR'){'IKnowAGuyVR.SubsystemProbe.dll'}else{'IKnowAGuyVR.Companion.dll'}
    $source=Get-Content -LiteralPath (Join-Path $root "ModPlugin/$project/Plugin.cs") -Raw
    $version=[regex]::Match($source,'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
    if(!$version){throw 'Missing plugin version.'}
    $build=Join-Path $output ($role+'-build')
    & dotnet build (Join-Path $root "ModPlugin/$project/$project.csproj") -c Release '-p:DiagnosticBuild=false' "-p:GameDir=$game" -o $build --nologo
    if($LASTEXITCODE -ne 0){throw "$role build failed"}
    $package=Join-Path $output ('IKnowAGuyVR-'+$role+'-'+$version)
    $payload=Join-Path $package 'payload'
    $plugins=Join-Path $payload 'BepInEx/plugins'
    New-Item -ItemType Directory -Path $plugins -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $build $dll) -Destination $plugins
    if($role -eq 'VR'){
        $manifests=Join-Path $payload 'I Know a Guy_Data/UnitySubsystems/UnityOpenXR'
        New-Item -ItemType Directory -Path $manifests -Force|Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'Release/UnitySubsystemsManifest.json') -Destination $manifests
    }
    foreach($name in @('Manage-Mod.ps1','dependencies.json')){Copy-Item -LiteralPath (Join-Path $root "Release/$name") -Destination $package}
    foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package}
    Copy-Item -LiteralPath (Join-Path $root 'Docs') -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'Docs/MANUAL-INSTALL.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $root 'Docs/CONTROLS.md') -Destination (Join-Path $package 'MOTION-CONTROLLER-BINDINGS.md')
    Copy-Item -LiteralPath (Join-Path $root 'LICENSES') -Destination $package -Recurse
    foreach($action in @('Install','Rollback','Uninstall','Disable','Enable')){
        $cmd="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Manage-Mod.ps1`" -Action $action`r`nif errorlevel 1 echo Operation failed. See the message above.`r`npause`r`n"
        [IO.File]::WriteAllText((Join-Path $package ($action+'.cmd')),$cmd,[Text.Encoding]::ASCII)
    }
    $entries=@(Get-ChildItem -LiteralPath $payload -Recurse -File|ForEach-Object {@{Path=$_.FullName.Substring($payload.Length+1).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    @{Role=$role;Version=$version;Plugin=$dll;SteamApp='4173620';SteamBuild='25861408';GameBinaries=$gameBinaries;Payload=$entries}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding UTF8
    $zip=$package+'.zip';Compress-Archive -LiteralPath $package -DestinationPath $zip
    $artifacts+=@{Role=$role;Version=$version;File=(Split-Path -Leaf $zip);SHA256=(Get-FileHash -LiteralPath $zip).Hash;PluginSHA256=(Get-FileHash -LiteralPath (Join-Path $plugins $dll)).Hash}
}
# Explicit source allowlist: no game binaries, generated interop, settings, logs, saves or private evidence.
$sourceRoot=Join-Path $output 'Source';New-Item -ItemType Directory -Path $sourceRoot|Out-Null
foreach($dir in @('ModPlugin/SubsystemProbe','ModPlugin/NetworkPoseShared','ModPlugin/VRCompanion','Release','Docs','LICENSES','.github','Tools/ReleaseChecks','Tools/NetworkPoseChecks','Tools/PhysicalToolChecks','Tools/SupportReportChecks','Tools/WristSettingsChecks','Tools/GestureChecks','Tools/WorldPressChecks','Tools/ShovelChecks','Tools/OverlayDepthChecks')){
    $from=Join-Path $root $dir
    if(!(Test-Path -LiteralPath $from)){continue}
    Get-ChildItem -LiteralPath $from -Recurse -File|Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in '.cs','.csproj','.props','.ps1','.json','.md','.txt','.yml','.yaml' }|ForEach-Object {
        $relative=$_.FullName.Substring($root.Length+1);$dest=Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath $_.FullName -Destination $dest
    }
}
foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','.gitignore','Tools/Build-Release.ps1')){
    $dest=Join-Path $sourceRoot $name;New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $dest
}
$inventory=@(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File|ForEach-Object {@{Path=$_.FullName.Substring($sourceRoot.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$inventory|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'source-hashes.json') -Encoding UTF8
Compress-Archive -LiteralPath $sourceRoot -DestinationPath (Join-Path $output 'Source.zip')
$artifacts+=@{Role='Source';File='Source.zip';SHA256=(Get-FileHash -LiteralPath (Join-Path $output 'Source.zip')).Hash}
@{Tag=$Tag;Utc=[DateTime]::UtcNow.ToString('o');BaseCommit=(& git -C $root rev-parse HEAD);Source='Exact uncommitted source snapshot in Source.zip; source-hashes.json is authoritative.';Published=$false;Artifacts=$artifacts}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'build.json') -Encoding UTF8
$artifacts|ForEach-Object {"$($_.SHA256)  $($_.File)"}|Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Output $output
