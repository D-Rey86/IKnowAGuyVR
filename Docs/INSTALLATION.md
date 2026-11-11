# Installing and managing I Know a Guy VR

Use the **VR** package for playing in VR. Use the **Companion** package only for flatscreen players who want to see other players' tracked VR movement. Install one package per player; the VR package already includes the companion's receiving and relay features.

## Requirements

- Windows x64, Direct3D 11, and a purchased copy of the supported game build.
- Steam app **4173620**, build **25861408**, Unity **6000.3.21f1**. The installer checks the exact game and engine binary hashes, not just a version label.
- For VR: a working PCVR headset and its selected OpenXR runtime. Standalone headset installation is not supported.
- One release ZIP for your selected package. It contains the mod and the required loader files.

If the installer cannot run, follow the [manual installation guide](MANUAL-INSTALL.md). It covers both packages using File Explorer, an archive app, and a text editor.

## First installation

1. Close the game. In Steam, open the game's installed files location and copy the folder path containing `I Know a Guy.exe`.
2. Extract the entire mod ZIP into a separate folder. Do not copy just the plugin for a fresh install.
3. Run `Install.cmd`, paste the game folder path when prompted, and wait for the completion message. The installer uses the dependency files inside the ZIP. It checks hashes before modifying the game.
4. Connect your headset and ready your chosen OpenXR runtime, then launch the game normally through Steam. The first launch can take longer while BepInEx generates interop assemblies from the installed game and bundled libraries. Let that finish.
5. Open **Settings > VR**. Set turning, locomotion, dominant hand, seated height, belt placement, and wrist size as desired.

The installer preserves saves and existing mod settings, adds only the required XR pre-init line to `boot.config`, and does not change graphics-job flags or the system OpenXR selection. It refuses conflicting loader binaries or an unsupported game version. It never launches the game itself.

## Updating

Close the game and run `Install.cmd` from the new package. Keep your existing settings. If you used the old development setup, the first managed update also backs up any files it replaces. Do not install both VR and Companion plugins.

If a game update is rejected, wait for a compatible mod release. Do not bypass the binary check or replace the game's executable or game assemblies with files from another version.

## Disable and enable

Run `Disable.cmd` with the game closed to play without this plugin. For VR it also removes the mod's XR pre-init entry. `Enable.cmd` restores the disabled plugin and the entry after checking the game version. Other mods, saves, and settings remain in place.

## Uninstall and rollback

`Uninstall.cmd` removes this package's plugin (including its disabled copy) and, for VR, the XR pre-init entry. Shared BepInEx files, native dependency files, generated caches, settings, and diagnostic reports remain so other mods and preferences are preserved. These inactive files do not enable VR without the plugin and pre-init entry.

`Rollback.cmd` reverses the last managed operation, including an update, disable, enable, or uninstall. Backups and receipts are stored under `.IKnowAGuyVR` in the game directory. Rollback checks that managed files have not changed since the operation; it stops rather than overwriting later edits. Repeated rollback walks backward through recorded operations. A rollback to a pre-existing development installation restores that installation; use Uninstall to remove its active VR plugin.

Do not delete `.IKnowAGuyVR` if you want to retain rollback. Do not delete the entire BepInEx directory when other mods use it.

For a manual setup, use the [manual removal and rollback steps](MANUAL-INSTALL.md#manual-removal-and-rollback).
