#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Install','Rollback','Uninstall','Disable','Enable')][string]$Action='Install',
    [string]$GamePath,
    [string]$DependencyCache=(Join-Path $env:LOCALAPPDATA 'IKnowAGuyVR/Downloads')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$package=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json
if(!$GamePath){$GamePath=Read-Host 'Paste the game folder containing I Know a Guy.exe (without quotes)'}
$game=[IO.Path]::GetFullPath($GamePath.Trim().Trim('"')).TrimEnd('\','/')
if(!(Test-Path -LiteralPath (Join-Path $game 'I Know a Guy.exe') -PathType Leaf)){throw 'Select the game folder, not the Data or BepInEx folder.'}
if(Get-Process -Name 'I Know a Guy' -ErrorAction SilentlyContinue){throw 'Close the game before managing the mod. No running game was changed.'}

function ChildPath([string]$Root,[string]$Relative){
    if([IO.Path]::IsPathRooted($Relative)){throw "Absolute package path rejected: $Relative"}
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
    if(!$path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Path escapes its root: $Relative"}
    # Reject junctions/symlinks, including parent directories, before any write/delete.
    for($p=$path;$p -and $p.Length -ge $base.Length;$p=[IO.Path]::GetDirectoryName($p)){
        if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Linked path rejected: $p"}
    }
    return $path
}
function Hash([string]$Path){if(Test-Path -LiteralPath $Path -PathType Leaf){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash};return $null}
function WriteJson($Value,[string]$Path){
    $temp=$Path+'.tmp';[IO.File]::WriteAllText($temp,($Value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}
$stateDir=ChildPath $game '.IKnowAGuyVR'
$statePath=ChildPath $game ('.IKnowAGuyVR/'+$package.Role+'-state.json')
$state=$null;if(Test-Path -LiteralPath $statePath){$state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json}
$plugin='BepInEx/plugins/'+$package.Plugin
$disabled=$plugin+'.disabled'
$boot='I Know a Guy_Data/boot.config'
$preinit='xrsdk-pre-init-library=UnityOpenXR'

if($Action -eq 'Rollback'){
    if(!$state -or !$state.Receipt){throw 'No managed installation change to roll back.'}
    $receiptPath=ChildPath $stateDir $state.Receipt
    $receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
    $backup=Split-Path -Parent $receiptPath
    foreach($entry in $receipt.Files){
        $target=ChildPath $game $entry.Path
        if((Hash $target) -ne $entry.After){throw "Changed since installation; preserved: $($entry.Path). Resolve this conflict before rollback."}
        if($entry.Before -and (Hash (ChildPath $backup ('files/'+$entry.Path))) -ne $entry.Before){throw 'Backup is missing or changed. Rollback cancelled.'}
    }
    foreach($entry in $receipt.Files){
        $target=ChildPath $game $entry.Path
        if($entry.Before){Copy-Item -LiteralPath (ChildPath $backup ('files/'+$entry.Path)) -Destination $target -Force}
        elseif(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
    }
    WriteJson @{Receipt=$receipt.PreviousReceipt} $statePath
    Write-Output "Restored the state before $($receipt.Action) $($receipt.Version). Saves and settings were preserved."
    return
}

$plan=New-Object 'System.Collections.Generic.List[object]'
$work=Join-Path ([IO.Path]::GetTempPath()) ('IKnowAGuyVR-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
function AddFile([string]$Relative,[string]$Source,[bool]$Shared=$false){
    $target=ChildPath $game $Relative;$before=Hash $target;$after=Hash $Source
    if(!$after){throw "Missing source: $Source"}
    if($Shared -and $before -and $before -ne $after){throw "Conflicting dependency preserved: $Relative. This release requires BepInEx $($deps.BepInEx.Version); use a clean game folder or resolve the existing mod-loader conflict."}
    if($before -ne $after){$plan.Add(@{Path=$Relative;Source=$Source;Before=$before;After=$after})}
}
function DeleteFile([string]$Relative){$before=Hash (ChildPath $game $Relative);if($before){$plan.Add(@{Path=$Relative;Source=$null;Before=$before;After=$null})}}
function SetBoot([bool]$Enabled){
    $target=ChildPath $game $boot
    if(!(Test-Path -LiteralPath $target)){throw 'Game boot.config is missing.'}
    $text=[IO.File]::ReadAllText($target)
    $matches=[regex]::Matches($text,'(?m)^xrsdk-pre-init-library=([^\r\n]*)\r?$')
    if($matches.Count -gt 1 -or ($matches.Count -eq 1 -and $matches[0].Groups[1].Value -ne 'UnityOpenXR')){throw 'Another XR pre-init setting exists. It was preserved; resolve the conflict before continuing.'}
    if($Enabled -and $matches.Count -eq 0){$text=$text.TrimEnd("`r","`n")+"`r`n"+$preinit+"`r`n"}
    elseif(!$Enabled -and $matches.Count -eq 1){$text=[regex]::Replace($text,'(?m)^xrsdk-pre-init-library=UnityOpenXR\r?\n?','')}
    else{return}
    $source=Join-Path $work 'boot.config';[IO.File]::WriteAllText($source,$text,[Text.UTF8Encoding]::new($false));AddFile $boot $source
}
function Download($Dependency){
    New-Item -ItemType Directory -Path $DependencyCache -Force | Out-Null
    $file=ChildPath $DependencyCache $Dependency.Archive
    if(!(Test-Path -LiteralPath $file)){
        Write-Output "Downloading $($Dependency.Url)"
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $partial=$file+'.'+[Guid]::NewGuid().ToString('N')+'.partial'
        try{
            Invoke-WebRequest -UseBasicParsing -Uri $Dependency.Url -OutFile $partial -TimeoutSec 180
            if((Hash $partial) -ne $Dependency.SHA256){throw 'Downloaded archive failed its SHA256 check.'}
            Move-Item -LiteralPath $partial -Destination $file
        }finally{if(Test-Path -LiteralPath $partial){Remove-Item -LiteralPath $partial}}
    }
    if((Hash $file) -ne $Dependency.SHA256){throw "Dependency hash mismatch: $file. No game files have been changed."}
    return $file
}
try{
    if($Action -in @('Install','Enable')){
        foreach($binary in $package.GameBinaries.PSObject.Properties){
            if((Hash (ChildPath $game $binary.Name)) -ne $binary.Value){throw "Unsupported game build ($($binary.Name)). Expected Steam build $($package.SteamBuild). No files were installed."}
        }
    }
    if($Action -eq 'Install'){
        $deps=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw|ConvertFrom-Json
        foreach($entry in $package.Payload){
            $src=ChildPath (Join-Path $PSScriptRoot 'payload') $entry.Path
            if((Hash $src) -ne $entry.SHA256){throw "Package checksum mismatch: $($entry.Path)"}
            AddFile $entry.Path $src
        }
        $bep=Download $deps.BepInEx;$bepDir=Join-Path $work 'bep'
        Expand-Archive -LiteralPath $bep -DestinationPath $bepDir
        foreach($dir in @('BepInEx/core','dotnet')){
            Get-ChildItem -LiteralPath (Join-Path $bepDir $dir) -Recurse -File | ForEach-Object {
                AddFile $_.FullName.Substring($bepDir.Length+1) $_.FullName $true
            }
        }
        foreach($name in @('winhttp.dll','.doorstop_version')){AddFile $name (Join-Path $bepDir $name) $true}
        $doorstop=ChildPath $game 'doorstop_config.ini'
        if(Test-Path -LiteralPath $doorstop){
            $ini=Get-Content -LiteralPath $doorstop -Raw
            foreach($pattern in @('(?m)^enabled\s*=\s*true\s*$','(?m)^target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx.Unity.IL2CPP.dll\s*$','(?m)^coreclr_path\s*=\s*dotnet[\\/]coreclr.dll\s*$','(?m)^corlib_dir\s*=\s*dotnet\s*$','(?m)^boot_config_override\s*=\s*$')){
                if($ini -notmatch $pattern){throw 'Existing doorstop_config.ini is incompatible and was preserved.'}
            }
        }else{AddFile 'doorstop_config.ini' (Join-Path $bepDir 'doorstop_config.ini')}
        if($package.Role -eq 'VR'){
            if(Test-Path -LiteralPath (ChildPath $game 'BepInEx/plugins/IKnowAGuyVR.Companion.dll')){throw 'Remove the companion plugin before installing VR; VR already includes it.'}
            $xr=Download $deps.OpenXR;$xrDir=Join-Path $work 'xr';New-Item -ItemType Directory -Path $xrDir|Out-Null
            & tar.exe -xzf $xr -C $xrDir 'package/Runtime/windows/x64/UnityOpenXR.dll' 'package/RuntimeLoaders/windows/x64/openxr_loader.dll' 'package/LICENSE.md' 'package/Third Party Notices.md'
            if($LASTEXITCODE -ne 0){throw 'Could not extract the pinned Unity OpenXR package (Windows tar.exe is required).'}
            $provider=Join-Path $xrDir 'package/Runtime/windows/x64/UnityOpenXR.dll'
            $loader=Join-Path $xrDir 'package/RuntimeLoaders/windows/x64/openxr_loader.dll'
            if((Hash $provider) -ne $deps.OpenXR.ProviderSHA256 -or (Hash $loader) -ne $deps.OpenXR.LoaderSHA256){throw 'Unexpected OpenXR binary identity.'}
            AddFile 'I Know a Guy_Data/Plugins/x86_64/UnityOpenXR.dll' $provider $true
            AddFile 'I Know a Guy_Data/Plugins/x86_64/openxr_loader.dll' $loader $true
            AddFile '.IKnowAGuyVR/licenses/Unity-OpenXR-LICENSE.md' (Join-Path $xrDir 'package/LICENSE.md')
            AddFile '.IKnowAGuyVR/licenses/Unity-OpenXR-Third-Party-Notices.md' (Join-Path $xrDir 'package/Third Party Notices.md')
            SetBoot $true
        }elseif((Test-Path -LiteralPath (ChildPath $game 'BepInEx/plugins/IKnowAGuyVR.SubsystemProbe.dll')) -or ((Get-Content -LiteralPath (ChildPath $game $boot) -Raw) -match 'xrsdk-pre-init-library=UnityOpenXR')){throw 'Uninstall/disable VR before installing the flatscreen companion.'}
        DeleteFile $disabled
    }elseif($Action -eq 'Disable'){
        if(!(Test-Path -LiteralPath (ChildPath $game $plugin))){throw 'The plugin is not enabled.'}
        if(Test-Path -LiteralPath (ChildPath $game $disabled)){throw 'A disabled plugin already exists; preserved both files.'}
        AddFile $disabled (ChildPath $game $plugin);DeleteFile $plugin
        if($package.Role -eq 'VR'){SetBoot $false}
    }elseif($Action -eq 'Enable'){
        if(!(Test-Path -LiteralPath (ChildPath $game $disabled))){throw 'No disabled plugin found. Use Install first.'}
        if(Test-Path -LiteralPath (ChildPath $game $plugin)){throw 'An enabled plugin already exists; preserved both files.'}
        $otherPlugin=if($package.Role -eq 'VR'){'IKnowAGuyVR.Companion.dll'}else{'IKnowAGuyVR.SubsystemProbe.dll'}
        if(Test-Path -LiteralPath (ChildPath $game ('BepInEx/plugins/'+$otherPlugin))){throw 'The other package is enabled. Disable it before enabling this one.'}
        if($package.Role -eq 'VR'){
            $deps=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw|ConvertFrom-Json
            if((Hash (ChildPath $game 'I Know a Guy_Data/Plugins/x86_64/UnityOpenXR.dll')) -ne $deps.OpenXR.ProviderSHA256 -or (Hash (ChildPath $game 'I Know a Guy_Data/Plugins/x86_64/openxr_loader.dll')) -ne $deps.OpenXR.LoaderSHA256 -or !(Test-Path -LiteralPath (ChildPath $game 'I Know a Guy_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json'))){throw 'XR dependencies are missing or changed. Use Install to repair them before enabling VR.'}
        }
        AddFile $plugin (ChildPath $game $disabled);DeleteFile $disabled
        if($package.Role -eq 'VR'){SetBoot $true}
    }elseif($Action -eq 'Uninstall'){
        DeleteFile $plugin;DeleteFile $disabled
        if($package.Role -eq 'VR'){SetBoot $false}
    }
    if($plan.Count -eq 0){Write-Output 'Already in the requested state. Nothing changed.';return}
    # Stage every source before mutating files (Enable/Disable sources are in the game).
    for($i=0;$i -lt $plan.Count;$i++){
        if($plan[$i].Source){$copy=Join-Path $work ('stage-'+$i);Copy-Item -LiteralPath $plan[$i].Source -Destination $copy;$plan[$i].Source=$copy}
    }
    $transaction=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $backup=ChildPath $stateDir ($package.Role+'/'+$transaction)
    New-Item -ItemType Directory -Path $backup -Force|Out-Null
    $files=@();foreach($entry in $plan){
        $target=ChildPath $game $entry.Path
        if((Hash $target) -ne $entry.Before){throw "File changed during preparation: $($entry.Path)"}
        if($entry.Before){$dest=ChildPath $backup ('files/'+$entry.Path);New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath $target -Destination $dest}
        $files+=@{Path=$entry.Path;Before=$entry.Before;After=$entry.After}
    }
    $previous=$null;if($state){$previous=$state.Receipt}
    $receipt=@{Role=$package.Role;Version=$package.Version;Action=$Action;PreviousReceipt=$previous;Files=$files;Utc=[DateTime]::UtcNow.ToString('o')}
    WriteJson $receipt (Join-Path $backup 'receipt.json')
    $applied=New-Object 'System.Collections.Generic.List[object]'
    try{
        foreach($entry in $plan){
            $target=ChildPath $game $entry.Path
            $applied.Add($entry)
            if($entry.Source){New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null;Copy-Item -LiteralPath $entry.Source -Destination $target -Force}
            else{Remove-Item -LiteralPath $target -Force}
            if((Hash $target) -ne $entry.After){throw "Write verification failed: $($entry.Path)"}
        }
        WriteJson @{Receipt=$package.Role+'/'+$transaction+'/receipt.json'} $statePath
    }catch{
        foreach($entry in $applied){
            $target=ChildPath $game $entry.Path
            if((Hash $target) -eq $entry.Before){continue}
            if($entry.Before){Copy-Item -LiteralPath (ChildPath $backup ('files/'+$entry.Path)) -Destination $target -Force}
            elseif(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
        }
        throw
    }
    Write-Output "$Action complete: $($package.Role) $($package.Version). Backup: $backup"
    Write-Output 'Saves, mod settings and the selected system OpenXR runtime were not changed. No game was launched.'
    if($Action -eq 'Uninstall'){Write-Output 'Shared BepInEx/runtime files and generated caches remain for other mods. This package no longer enables its plugin.'}
}finally{
    $safe=[IO.Path]::GetFullPath($work)
    if($safe.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $safe) -match '^IKnowAGuyVR-[a-f0-9]{32}$'){
        Remove-Item -LiteralPath $safe -Recurse -Force
    }
}
