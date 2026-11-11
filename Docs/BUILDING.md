# Building the mod

The original mod code is MIT licensed. A purchased, supported game copy supplies local build references; game assemblies and generated interop are not redistributed in the source archive.

1. Install the pinned BepInEx IL2CPP build described in `Release/dependencies.json` into a supported game copy and launch once to generate `BepInEx/interop`.
2. Install a .NET SDK capable of building `net6.0` projects. The game plugin runs using BepInEx's supplied .NET runtime.
3. From the source root, build with an explicit game path:

```powershell
dotnet build ModPlugin/SubsystemProbe/SubsystemProbe.csproj -c Release -p:DiagnosticBuild=false '-p:GameDir=H:/Games/Steam/steamapps/common/I Know a Guy'
dotnet build ModPlugin/VRCompanion/VRCompanion.csproj -c Release '-p:GameDir=H:/Games/Steam/steamapps/common/I Know a Guy'
```

For release packages:

```powershell
powershell.exe -NoProfile -File Tools/Build-Release.ps1 -Tag 1.0.0-local -GameDir 'H:/Games/Steam/steamapps/common/I Know a Guy'
```

Use a fresh tag each time. Outputs go to `Builds/Releases/<tag>`: separate VR and Companion ZIPs, an allowlisted source ZIP, source-file hashes, artifact hashes and a build receipt. Version labels come from each plugin's source. A base Git commit alone does not identify uncommitted changes; the archived source and hashes do.

Normal Release builds exclude the optional gameplay audit classes. `-p:DiagnosticBuild=true` is for private research only; never package that assembly as a normal release. Package creation copies only the shipping plugin, installer, manifest and player documentation. No simulator, capture preloader, test harness or personal configuration is shipped.

Standalone checks live under `Tools/*Checks` and require a .NET 9 SDK for the projects targeting net9.0. For example, run `dotnet run --project Tools/WristSettingsChecks -c Release '-p:GameDir=H:/Games/Steam/steamapps/common/I Know a Guy'`. They verify math, settings migration, report handling and network serialization; they do not establish native gameplay or visual correctness. `Tools/ReleaseChecks/Check-Package.ps1` exercises installer transitions and failure cases against disposable fixtures using the supported game binaries. Its WorkRoot must be new and on the same volume as ReferenceGame because fixtures hardlink the immutable game binaries; never launch those fixtures.

The native rendering contract is specific to the binary hashes in `EngineBinaryContract.cs`. Updating those hashes without verifying native ABI and rendering behavior is not a compatibility fix.
