# I Know a Guy VR

An unofficial PCVR mod for **I Know a Guy: Shady Life Simulator**, with stereo OpenXR rendering, motion controls, physical item handling, and an interactive wrist dashboard.

**Version 1.0.0** is the first public release.

## Features

- **OpenXR stereo.** Look and lean naturally with headset tracking and views sized for your headset. Adjust resolution scale, world scale, and stereo separation in VR settings.
- **Motion controls.** Grab items with either hand, tap grip to keep holding, and use a held item on a target with the primary trigger. Work kitchen machines, box pizzas, and physically swing a fist or bat.
- **Wrist dashboard and map.** Check time, money, debt, reputation, quests, and orders in one view. Open the map from your wrist, collapse sections, or pin the journal in the world and adjust it while holding its title bar.
- **Item belt.** Equip, stow, and swap items from belt slots. Ingredient boxes keep their picture identifiers so you can tell them apart.
- **Menus and machines.** Point and select in menus, use VR prompts and the on-screen keyboard, and choose between Immersive and Classic UI. Open or close the pizza recipe book with a face button.
- **Movement and comfort.** Choose smooth or snap turning and smooth or teleport movement. Set your dominant hand, swap sticks independently, calibrate seated height, and adjust the wrist display and belt.
- **Regular gamepad mode.** Play with a gamepad while keeping headset tracking and stereo rendering.
- **Multiplayer VR movement.** Share tracked head, body, hands, fingers, and held-item movement with other mod users. The flatscreen Companion lets players see that movement without a headset. The host needs either package to relay poses.
- **Local diagnostics.** Create an optional support report from VR settings and review it before sharing.

Open **Settings > VR** for configuration and the motion controller reference. [Controller bindings](Docs/CONTROLS.md)

## Install

Download the **VR** package to play in VR, or the **Companion** package to see other players' tracked movement while playing flatscreen. Install one package per player. Extract the entire ZIP, close the game, run `Install.cmd`, and select the folder containing `I Know a Guy.exe`. [GitHub releases](https://github.com/D-Rey86/IKnowAGuyVR/releases)

The installer checks the supported game version, downloads pinned dependencies from their official sources, and backs up replaced files. It preserves saves and settings. See [installation, updates, disabling and rollback](Docs/INSTALLATION.md).

If the installer cannot run, follow [manual installation and removal](Docs/MANUAL-INSTALL.md). The release ZIP also includes a copy of the manual guide and the motion controller binding reference in its top folder.

Requires Windows x64, Direct3D 11, Steam build **25861408**, and a working OpenXR PCVR setup for VR play. This is not a standalone headset app. Start your headset/runtime before launching the game normally through Steam.

## Compatibility and support

[Compatibility and known limitations](Docs/COMPATIBILITY.md) distinguishes physical experience, simulator coverage and unverified configurations. Outdoor FPS varies, full runtime restart during play is unsupported, and a reported Virtual Desktop fatal exit remains unexplained. Additional hardware beta and Internet multiplayer testing are not release gates; local multiplayer tests have passed.

For problems, follow the troubleshooting guide and review any diagnostic ZIP before sharing it through the repository's Issues tab. [Troubleshooting and diagnostic reports](Docs/SUPPORT.md)

## Source and release information

Original mod code is [MIT licensed](LICENSE). Dependencies retain their own [licenses and notices](THIRD-PARTY-NOTICES.md). Game binaries, assets, generated interop, personal settings and saves are not included in the public source package.

- [Build from source](Docs/BUILDING.md)
- [Release notes](Docs/RELEASE-NOTES.md)

This community project is not affiliated with the game's developers, Unity, or headset/runtime vendors.
