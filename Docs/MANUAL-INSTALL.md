# Manual installation for I Know a Guy VR

Use these steps to install the VR mod or flatscreen Companion with File Explorer and Notepad. Choose the **VR** package for headset play or the **Companion** package to see tracked VR movement while playing flatscreen. Install one of the two plugins.

## Prepare the game folder

1. Close the game. In Steam, right-click the game and choose **Manage > Browse local files**. The folder containing `I Know a Guy.exe` is the **game folder** in the steps below.
2. This package supports Steam build **25861408**. To check the installed build, go up from the game folder to `common`, then up to `steamapps`. Open `appmanifest_4173620.acf` in Notepad and read the number next to `buildid`. If the build differs, wait for a compatible release. Resolve any unsupported build or conflicting loader error from the installer before proceeding.
3. Extract your mod ZIP to a separate folder. In File Explorer, turn on **File name extensions** so you can see the full filenames. Create a backup folder outside the game folder. Before replacing or editing any file below, copy the original into that backup and keep track of its location. Keep a list of newly added files so you can undo the installation.

## Set up BepInEx for either package

If you already have the exact BepInEx build below working with this game, keep it and proceed to the plugin steps. You can check the version in the first lines of `BepInEx/LogOutput.log` if it exists. If another loader or BepInEx version is installed, resolve the conflict before copying anything over it.

1. Download and extract [BepInEx 6.0.0-be.785+6abdba4 for Unity IL2CPP on Windows x64](https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip).
2. Copy the extracted `BepInEx` and `dotnet` folders and the files `winhttp.dll`, `doorstop_config.ini`, and `.doorstop_version` into the game folder. These belong next to `I Know a Guy.exe`. Leave the game's own files, including its changelog, in place.
3. Open `BepInEx` in the game folder and create a `plugins` folder if it is missing.

## Install the Companion for flatscreen play

From the extracted Companion package, copy `payload/BepInEx/plugins/IKnowAGuyVR.Companion.dll` into the game's `BepInEx/plugins` folder. If VR is already installed, use the manual removal steps below before switching to Companion. Skip the VR files and boot edit and go to **First launch**.

## Install the VR mod

1. Download [Unity OpenXR 1.17.1](https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.17.1.tgz). Open the downloaded `.tgz` in an archive app that supports GZIP and TAR, such as [7-Zip](https://www.7-zip.org/). Open the inner `.tar` and extract its `package` folder somewhere outside the game folder.
2. Copy the files in this table. Create any missing destination folders inside the game folder. If a native OpenXR DLL already exists from another setup and you cannot confirm it is the same version, resolve the conflict before replacing it.

| File in the extracted download | Destination inside the game folder |
| --- | --- |
| Mod ZIP: `payload/BepInEx/plugins/IKnowAGuyVR.SubsystemProbe.dll` | `BepInEx/plugins/` |
| Mod ZIP: `payload/I Know a Guy_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` | `I Know a Guy_Data/UnitySubsystems/UnityOpenXR/` |
| Unity download: `package/Runtime/windows/x64/UnityOpenXR.dll` | `I Know a Guy_Data/Plugins/x86_64/` |
| Unity download: `package/RuntimeLoaders/windows/x64/openxr_loader.dll` | `I Know a Guy_Data/Plugins/x86_64/` |
| Unity download: `package/LICENSE.md` and `package/Third Party Notices.md` | `.IKnowAGuyVR/licenses/` |

3. Back up `I Know a Guy_Data/boot.config`. Open the original in Notepad and add the line below at the end. Use **Save** to keep the filename `boot.config`.

   ```text
   xrsdk-pre-init-library=UnityOpenXR
   ```

   If the same line is already present once, leave it in place. If there is a different `xrsdk-pre-init-library` entry or more than one entry, resolve that conflict first. Keep every other line as it was.

## First launch and updates

For VR, ready your headset and selected OpenXR runtime. Launch normally through Steam. Leave the first launch running while BepInEx downloads the Unity libraries and generates its local interop files. For VR, open **Settings > VR** after startup.

For updates, close the game and back up the current plugin and any other files you replace. If the new package specifies the same game build and dependencies, copy its plugin and, for VR, its subsystem manifest to the same locations above. Leave `BepInEx/config` in place to keep your preferences.

## Manual removal and rollback

1. Close the game. To disable or remove the mod, move its DLL out of `BepInEx/plugins` and into your backup folder. If you previously used the managed installer to disable the mod, move its corresponding `.dll.disabled` file out too.
2. For VR, remove the `xrsdk-pre-init-library=UnityOpenXR` line added for this mod from `I Know a Guy_Data/boot.config`. If the only edit since installation was adding that line, you can instead restore the original backup. If another setup owned the line before this installation, keep its original configuration.
3. For a full rollback of your manual changes, restore any files you replaced from your backup and remove only the files you added. Keep shared BepInEx files if other mods use them. Leave saves, `BepInEx/config`, and files modified by other mods in place.

To re-enable a temporarily disabled VR setup, move the plugin back to `BepInEx/plugins` and restore the VR boot line after confirming the supported game build. For Companion, moving its plugin back is sufficient.
