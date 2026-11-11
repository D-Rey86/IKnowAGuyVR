# Manual installation for I Know a Guy VR

Use the files in this ZIP to install the VR mod or flatscreen Companion with File Explorer and Notepad. Choose the **VR** package for headset play or the **Companion** package to see tracked VR movement while playing flatscreen. Install one of the two plugins.

## Prepare the game folder

1. Close the game. In Steam, right-click the game and choose **Manage > Browse local files**. The folder containing `I Know a Guy.exe` is the **game folder** in the steps below.
2. This package supports Steam build **25861408**. To check the installed build, go up from the game folder to `common`, then up to `steamapps`. Open `appmanifest_4173620.acf` in Notepad and read the number next to `buildid`. If the build differs, wait for a compatible release. Resolve any unsupported build or conflicting loader error from the installer before proceeding.
3. Extract your mod ZIP to a separate folder. In File Explorer, turn on **File name extensions** so you can see the full filenames. Create a backup folder outside the game folder. Before replacing or editing any file below, copy the original into that backup and keep track of its location. Keep a list of newly added files so you can undo the installation.

## Set up BepInEx for either package

The release ZIP includes BepInEx **6.0.0-be.785+6abdba4** for Windows x64 and the **6000.3.21** Unity libraries. All source paths below start in the extracted release folder. A working installation of this exact BepInEx build can reuse its existing loader files. Keep your existing configuration. If a different loader is installed, resolve the conflict before replacing its files.

1. Copy `payload/dotnet`, `payload/winhttp.dll`, and `payload/.doorstop_version` into the game folder. Copy `payload/doorstop_config.ini` if the game folder does not already have a working BepInEx configuration file with that name.
2. Open or create `BepInEx` inside the game folder. Copy the `core` folder from `payload/BepInEx` into it and create a `plugins` folder if needed.
3. Copy `payload/BepInEx/unity-libs/6000.3.21.zip` to the game's `BepInEx/unity-libs` folder. Create the folder if it is missing. Leave the inner ZIP intact. BepInEx reads it on first launch with its default configuration.

## Install the Companion for flatscreen play

From the extracted Companion package, copy `payload/BepInEx/plugins/IKnowAGuyVR.Companion.dll` into the game's `BepInEx/plugins` folder. If VR is already installed, use the manual removal steps below before switching to Companion. Skip the VR files and boot edit and go to **First launch**.

## Install the VR mod

1. Copy the bundled Unity OpenXR **1.17.1** files and the VR plugin from the paths below. Create missing destination folders inside the game folder. If an OpenXR DLL already exists from another setup and you cannot confirm it matches the bundled file, resolve that conflict first.

| File in the extracted release | Destination inside the game folder |
| --- | --- |
| `payload/BepInEx/plugins/IKnowAGuyVR.SubsystemProbe.dll` | `BepInEx/plugins/` |
| `payload/I Know a Guy_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` | `I Know a Guy_Data/UnitySubsystems/UnityOpenXR/` |
| `payload/I Know a Guy_Data/Plugins/x86_64/UnityOpenXR.dll` | `I Know a Guy_Data/Plugins/x86_64/` |
| `payload/I Know a Guy_Data/Plugins/x86_64/openxr_loader.dll` | `I Know a Guy_Data/Plugins/x86_64/` |

2. Back up `I Know a Guy_Data/boot.config`. Open the original in Notepad and add the line below at the end. Use **Save** to keep the filename `boot.config`.

   ```text
   xrsdk-pre-init-library=UnityOpenXR
   ```

   If the same line is already present once, leave it in place. If there is a different `xrsdk-pre-init-library` entry or more than one entry, resolve that conflict first. Keep every other line as it was.

## First launch and updates

For VR, ready your headset and selected OpenXR runtime. Launch normally through Steam. Leave the first launch running while BepInEx generates its interop files from the bundled libraries and the installed game. For VR, open **Settings > VR** after startup.

For updates, close the game and back up the current plugin and any other files you replace. If the new package specifies the same game build and dependencies, copy its plugin and, for VR, its subsystem manifest to the same locations above. Leave `BepInEx/config` in place to keep your preferences.

## Manual removal and rollback

1. Close the game. To disable or remove the mod, move its DLL out of `BepInEx/plugins` and into your backup folder. If you previously used the managed installer to disable the mod, move its corresponding `.dll.disabled` file out too.
2. For VR, remove the `xrsdk-pre-init-library=UnityOpenXR` line added for this mod from `I Know a Guy_Data/boot.config`. If the only edit since installation was adding that line, you can instead restore the original backup. If another setup owned the line before this installation, keep its original configuration.
3. For a full rollback of your manual changes, restore any files you replaced from your backup and remove only the files you added. Keep shared BepInEx files if other mods use them. Leave saves, `BepInEx/config`, and files modified by other mods in place.

To re-enable a temporarily disabled VR setup, move the plugin back to `BepInEx/plugins` and restore the VR boot line after confirming the supported game build. For Companion, moving its plugin back is sufficient.
