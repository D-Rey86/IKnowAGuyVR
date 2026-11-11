# I Know a Guy VR

An unofficial PCVR mod for **I Know a Guy: Shady Life Simulator**, with stereo OpenXR rendering, motion controls, physical item handling, and an interactive wrist dashboard.

**Version 1.0.0** is the first public release.

## Install

Download the **VR** package to play in VR, or the **Companion** package to see other players' tracked movement while playing flatscreen. Install one package per player. Extract the entire ZIP, close the game, run `Install.cmd`, and select the folder containing `I Know a Guy.exe`. [GitHub releases](https://github.com/D-Rey86/IKnowAGuyVR/releases)

The installer checks the supported game version, downloads pinned dependencies from their official sources, and backs up replaced files. It preserves saves and settings. See [installation, updates, disabling and rollback](Docs/INSTALLATION.md).

If the installer cannot run, follow [manual installation and removal](Docs/MANUAL-INSTALL.md). The release ZIP also includes a copy of the manual guide and the motion controller binding reference in its top folder.

Requires Windows x64, Direct3D 11, Steam build **25861408**, and a working OpenXR PCVR setup for VR play. This is not a standalone headset app. Start your headset/runtime before launching the game normally through Steam.

## Features

- Runtime-derived stereo views, head tracking and motion controllers.
- Physical grabbing, tap-to-hold, held-item use, tools, punching and bat swings.
- A wrist dashboard with status, quests, orders and Map; clickable sections and a movable pinned journal.
- An item belt with identifiable miniature ingredient boxes.
- VR-aware prompts and menus, including computers and machines.
- Resolution scaling, world scale, stereo separation, smooth/snap turns, smooth/teleport movement and regular gamepad mode.
- Left-handed controls, independent stick swapping, seated calibration, and adjustable wrist/belt placement.
- Tracked multiplayer movement with a lightweight flatscreen companion.
- Opt-in diagnostic reports saved locally; nothing uploads automatically.

Open **Settings > VR** for configuration and the current motion-controller reference. Read the [controls guide](Docs/CONTROLS.md) for the default bindings and interaction details.

## Compatibility and support

[Compatibility and known limitations](Docs/COMPATIBILITY.md) distinguishes physical experience, simulator coverage and unverified configurations. Outdoor FPS varies, full runtime restart during play is unsupported, and a reported Virtual Desktop fatal exit remains unexplained. Additional hardware beta and Internet multiplayer testing are not release gates; local multiplayer tests have passed.

For problems, follow the troubleshooting guide and review any diagnostic ZIP before sharing it through the repository's Issues tab. [Troubleshooting and diagnostic reports](Docs/SUPPORT.md)

## Source and release information

Original mod code is [MIT licensed](LICENSE). Dependencies retain their own [licenses and notices](THIRD-PARTY-NOTICES.md). Game binaries, assets, generated interop, personal settings and saves are not included in the public source package.

- [Build from source](Docs/BUILDING.md)
- [Release notes](Docs/RELEASE-NOTES.md)

This community project is not affiliated with the game's developers, Unity, or headset/runtime vendors.
