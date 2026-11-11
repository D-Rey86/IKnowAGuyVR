#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9.-]+$')][string]$Tag,
      [string]$GameDir,[string]$DependencyCache)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$GameDir){$GameDir=Join-Path $root 'Builds/CurrentGameXR'}
if(!$DependencyCache){$DependencyCache=Join-Path $root 'Tools/Downloads/release-dependencies'}
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
$deps=Get-Content -LiteralPath (Join-Path $root 'Release/dependencies.json') -Raw|ConvertFrom-Json
function Get-VerifiedDependency($Dependency){
    $path=Join-Path $DependencyCache $Dependency.Archive
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing build input $($Dependency.Archive) in $DependencyCache"}
    if((Get-FileHash -LiteralPath $path).Hash -ne $Dependency.SHA256){throw "Dependency checksum mismatch: $path"}
    return (Resolve-Path -LiteralPath $path).Path
}
$bepDir=Join-Path $output 'dependency-stage/bep'
Expand-Archive -LiteralPath (Get-VerifiedDependency $deps.BepInEx) -DestinationPath $bepDir
$unityLibs=Get-VerifiedDependency $deps.UnityBaseLibraries
$xrDir=Join-Path $output 'dependency-stage/xr'
New-Item -ItemType Directory -Path $xrDir -Force|Out-Null
& tar.exe -xzf (Get-VerifiedDependency $deps.OpenXR) -C $xrDir 'package/Runtime/windows/x64/UnityOpenXR.dll' 'package/RuntimeLoaders/windows/x64/openxr_loader.dll' 'package/LICENSE.md' 'package/Third Party Notices.md'
if($LASTEXITCODE -ne 0){throw 'Could not extract Unity OpenXR build inputs.'}
$provider=Join-Path $xrDir 'package/Runtime/windows/x64/UnityOpenXR.dll'
$loader=Join-Path $xrDir 'package/RuntimeLoaders/windows/x64/openxr_loader.dll'
if((Get-FileHash -LiteralPath $provider).Hash -ne $deps.OpenXR.ProviderSHA256 -or (Get-FileHash -LiteralPath $loader).Hash -ne $deps.OpenXR.LoaderSHA256){throw 'Unexpected OpenXR binary identity.'}
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
    Copy-Item -LiteralPath (Join-Path $bepDir 'BepInEx/core') -Destination (Join-Path $payload 'BepInEx') -Recurse
    Copy-Item -LiteralPath (Join-Path $bepDir 'dotnet') -Destination $payload -Recurse
    foreach($name in @('winhttp.dll','.doorstop_version','doorstop_config.ini')){Copy-Item -LiteralPath (Join-Path $bepDir $name) -Destination $payload}
    $libsDir=Join-Path $payload 'BepInEx/unity-libs'
    New-Item -ItemType Directory -Path $libsDir|Out-Null
    Copy-Item -LiteralPath $unityLibs -Destination $libsDir
    if($role -eq 'VR'){
        $manifests=Join-Path $payload 'I Know a Guy_Data/UnitySubsystems/UnityOpenXR'
        New-Item -ItemType Directory -Path $manifests -Force|Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'Release/UnitySubsystemsManifest.json') -Destination $manifests
        $native=Join-Path $payload 'I Know a Guy_Data/Plugins/x86_64'
        New-Item -ItemType Directory -Path $native -Force|Out-Null
        Copy-Item -LiteralPath $provider,$loader -Destination $native
    }
    foreach($name in @('Manage-Mod.ps1','dependencies.json')){Copy-Item -LiteralPath (Join-Path $root "Release/$name") -Destination $package}
    foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package}
    Copy-Item -LiteralPath (Join-Path $root 'Docs') -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'Docs/MANUAL-INSTALL.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $root 'Docs/CONTROLS.md') -Destination (Join-Path $package 'MOTION-CONTROLLER-BINDINGS.md')
    Copy-Item -LiteralPath (Join-Path $root 'LICENSES') -Destination $package -Recurse
    $licenses=Join-Path $package 'LICENSES'
    foreach($name in @('BepInEx-LICENSE.txt','BepInEx-source.zip','UnityDoorstop-LICENSE.txt','UnityDoorstop-source.zip','dotnet-LICENSE.txt','dotnet-THIRD-PARTY-NOTICES.txt')){
        Copy-Item -LiteralPath (Join-Path $DependencyCache $name) -Destination $licenses
    }
    if($role -eq 'VR'){
        Copy-Item -LiteralPath (Join-Path $xrDir 'package/LICENSE.md') -Destination (Join-Path $licenses 'Unity-OpenXR-LICENSE.md')
        Copy-Item -LiteralPath (Join-Path $xrDir 'package/Third Party Notices.md') -Destination (Join-Path $licenses 'Unity-OpenXR-Third-Party-Notices.md')
    }
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
