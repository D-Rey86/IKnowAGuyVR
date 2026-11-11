#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$ReferenceGame,
      [Parameter(Mandatory)][string]$WorkRoot)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $WorkRoot){throw 'Choose a fresh fixture root.'}
$package=(Resolve-Path -LiteralPath $PackagePath).Path
$reference=(Resolve-Path -LiteralPath $ReferenceGame).Path
$work=[IO.Path]::GetFullPath($WorkRoot)
New-Item -ItemType Directory -Path $work|Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw|ConvertFrom-Json
$script=Join-Path $package 'Manage-Mod.ps1'
$results=New-Object 'System.Collections.Generic.List[string]'
function Assert($Condition,[string]$Message){if(!$Condition){throw "FAILED: $Message"};$results.Add($Message)}
function Hash([string]$File){if(Test-Path -LiteralPath $File){return (Get-FileHash -LiteralPath $File).Hash};return $null}
function Fixture([string]$Name){
    $dir=Join-Path $work $Name;New-Item -ItemType Directory -Path (Join-Path $dir 'I Know a Guy_Data'),(Join-Path $dir 'BepInEx/config') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'I Know a Guy.exe'),'Fixture only; never launch')
    foreach($name in @('UnityPlayer.dll','GameAssembly.dll')){
        New-Item -ItemType HardLink -Path (Join-Path $dir $name) -Target (Join-Path $reference $name)|Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $dir 'I Know a Guy_Data/boot.config'),"gfx-enable-gfx-jobs=1`r`ncustom-setting=keep`r`n")
    [IO.File]::WriteAllText((Join-Path $dir 'BepInEx/config/iknowaguyvr.subsystemprobe.cfg'),"[Gameplay]`r`nLeftHanded = true`r`n")
    return $dir
}
function Run([string]$Action,[string]$Dir){& $script -Action $Action -GamePath $Dir | Out-Host}
function Reject([scriptblock]$Call,[string]$Match,[string]$Label){
    $errorText=$null;try{& $Call}catch{$errorText=$_.Exception.Message}
    Assert ($errorText -and $errorText -match $Match) "$Label ($errorText)"
}
$game=Fixture 'clean';$boot=Join-Path $game 'I Know a Guy_Data/boot.config'
$original=Hash $boot;$settings=Hash (Join-Path $game 'BepInEx/config/iknowaguyvr.subsystemprobe.cfg')
$plugin=Join-Path $game ('BepInEx/plugins/'+$manifest.Plugin)
Run Install $game
Assert ((Hash (Join-Path $game 'BepInEx/unity-libs/6000.3.21.zip')) -eq ($manifest.Payload|Where-Object Path -eq 'BepInEx/unity-libs/6000.3.21.zip').SHA256) 'First-launch Unity libraries installed from the ZIP'
Assert ((Hash $plugin) -eq ($manifest.Payload|Where-Object Path -eq ('BepInEx/plugins/'+$manifest.Plugin)).SHA256) 'Clean install has exact shipping plugin'
Assert ((Hash (Join-Path $game 'BepInEx/config/iknowaguyvr.subsystemprobe.cfg')) -eq $settings) 'Settings preserved byte for byte'
Assert ((Get-Content $boot -Raw) -match 'custom-setting=keep') 'Unrelated boot settings preserved'
if($manifest.Role -eq 'VR'){Assert ((Get-Content $boot -Raw) -match 'xrsdk-pre-init-library=UnityOpenXR') 'VR pre-init installed'}
else{Assert ((Hash $boot) -eq $original) 'Companion does not alter XR bootstrap'}
$state=Join-Path $game ('.IKnowAGuyVR/'+$manifest.Role+'-state.json');$stateHash=Hash $state
Run Install $game;Assert ((Hash $state) -eq $stateHash) 'Repeated installation is idempotent'
Run Disable $game;Assert (!(Test-Path $plugin) -and (Test-Path ($plugin+'.disabled'))) 'Disable moves only the selected plugin'
Assert ((Get-Content $boot -Raw) -notmatch 'xrsdk-pre-init-library') 'Disabled state does not preinitialize XR'
Run Enable $game;Assert ((Test-Path $plugin) -and !(Test-Path ($plugin+'.disabled'))) 'Enable restores selected plugin'
Run Uninstall $game;Assert (!(Test-Path $plugin) -and !(Test-Path ($plugin+'.disabled'))) 'Uninstall removes both plugin forms'
Assert ((Test-Path (Join-Path $game 'BepInEx/core/BepInEx.Core.dll'))) 'Uninstall preserves shared loader'
Run Rollback $game;Assert (Test-Path $plugin) 'Rollback reverses uninstall'
Run Rollback $game;Assert (Test-Path ($plugin+'.disabled')) 'Rollback reverses enable'
Run Rollback $game;Assert (Test-Path $plugin) 'Rollback reverses disable'
# A modified file must block the full rollback, with no earlier files partially restored.
[IO.File]::AppendAllText($plugin,'external-change')
Reject {Run Rollback $game} 'Changed since installation' 'Rollback preserves later edits'
Assert (Test-Path (Join-Path $game 'winhttp.dll')) 'Rejected rollback does not partially remove dependencies'
Copy-Item -LiteralPath (Join-Path $package ('payload/BepInEx/plugins/'+$manifest.Plugin)) -Destination $plugin -Force
Run Rollback $game
Assert (!(Test-Path $plugin) -and !(Test-Path (Join-Path $game 'winhttp.dll'))) 'Clean-install rollback removes newly installed files'
Assert ((Hash $boot) -eq $original) 'Rollback restores exact original boot bytes'
Assert ((Hash (Join-Path $game 'BepInEx/config/iknowaguyvr.subsystemprobe.cfg')) -eq $settings) 'All management operations preserve settings'
$bad=Fixture 'unsupported'
# Break the fixture hardlink by deleting its directory entry before creating bad bytes.
Remove-Item -LiteralPath (Join-Path $bad 'GameAssembly.dll');[IO.File]::WriteAllText((Join-Path $bad 'GameAssembly.dll'),'unsupported')
Reject {Run Install $bad} 'Unsupported game build' 'Unsupported game rejected before mutation'
Assert (!(Test-Path (Join-Path $bad 'BepInEx/plugins'))) 'Unsupported game leaves no plugin'
$conflict=Fixture 'loader-conflict';[IO.File]::WriteAllText((Join-Path $conflict 'winhttp.dll'),'another loader')
Reject {Run Install $conflict} 'Conflicting dependency preserved' 'Foreign loader rejected before mutation'
Assert (([IO.File]::ReadAllText((Join-Path $conflict 'winhttp.dll'))) -eq 'another loader') 'Foreign loader bytes preserved'
Assert (!(Test-Path (Join-Path $conflict 'BepInEx/plugins'))) 'Dependency conflict does not install plugin'
if($manifest.Role -eq 'VR'){
    $other=Fixture 'other-xr';[IO.File]::AppendAllText((Join-Path $other 'I Know a Guy_Data/boot.config'),"xrsdk-pre-init-library=OtherProvider`r`n")
    Reject {Run Install $other} 'Another XR pre-init setting exists' 'Foreign XR provider preserved'
    Assert (!(Test-Path (Join-Path $other 'BepInEx/plugins'))) 'XR conflict does not partially install'
}
$corrupt=Join-Path $work 'corrupt-package';Copy-Item -LiteralPath $package -Destination $corrupt -Recurse
[IO.File]::AppendAllText((Join-Path $corrupt ('payload/BepInEx/plugins/'+$manifest.Plugin)),'corrupt')
$target=Fixture 'bad-package'
Reject {& (Join-Path $corrupt 'Manage-Mod.ps1') -GamePath $target} 'Package checksum mismatch' 'Corrupt package rejected'
Assert (!(Test-Path (Join-Path $target 'BepInEx/plugins'))) 'Corrupt package leaves no plugin'
# A write failure after earlier files were installed must restore the original state.
$locked=Fixture 'locked-write';$lockedBoot=Join-Path $locked 'I Know a Guy_Data/boot.config'
if($manifest.Role -eq 'VR'){
    $lock=[IO.File]::Open($lockedBoot,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{Reject {Run Install $locked} 'being used|another process|access|used by' 'Failed write triggers restoration'}finally{$lock.Dispose()}
    Assert (!(Test-Path (Join-Path $locked ('BepInEx/plugins/'+$manifest.Plugin))) -and !(Test-Path (Join-Path $locked 'winhttp.dll'))) 'Failed installation restores earlier writes'
    Assert ((Hash $lockedBoot) -eq $original) 'Failed installation preserves locked original'
}
$upgrade=Fixture 'legacy-upgrade';$upgradePlugin=Join-Path $upgrade ('BepInEx/plugins/'+$manifest.Plugin)
New-Item -ItemType Directory -Path (Split-Path -Parent $upgradePlugin)|Out-Null
[IO.File]::WriteAllText($upgradePlugin,'previous plugin bytes')
Run Install $upgrade;Run Rollback $upgrade
Assert (([IO.File]::ReadAllText($upgradePlugin)) -eq 'previous plugin bytes') 'Upgrade rollback restores pre-existing plugin'
@{Role=$manifest.Role;Version=$manifest.Version;Passed=$results.Count;Checks=@($results)}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding UTF8
Write-Output "$($results.Count) installer checks passed."
