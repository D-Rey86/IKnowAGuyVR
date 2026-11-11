# Third party dependencies

The MIT license in this repository covers the original mod source and documentation. It does not relicense the game, Unity, or third-party dependencies. Release archives do not contain game assemblies, generated game interop, saves, or the Unity editor.

The adapted keyboard retains the MIT notice in `LICENSES/SupermarketSimulatorVR-keyboard.txt`. The rest-rig body alignment approach retains its source attribution and Unlicense text in `LICENSES/EnshroudedVR-body-alignment.txt`. Both notices accompany the binary and source packages.

The installer downloads these pinned archives directly from their publishers and verifies SHA256 before extracting them. Exact URLs and hashes are in `dependencies.json` in each package and `Release/dependencies.json` in the source.

| Dependency | Version | Upstream and terms |
| --- | --- | --- |
| BepInEx IL2CPP for Windows x64 | 6.0.0-be.785+6abdba4 | [BepInEx build server](https://builds.bepinex.dev/projects/bepinex_be), [source and license](https://github.com/BepInEx/BepInEx/tree/6abdba47eeebe08552282e7a58ef0f4a9ab60b62) |
| Unity OpenXR native provider | 1.17.1 | [Unity package documentation](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.17/manual/index.html), [Unity Package Distribution License](https://unity.com/legal/licenses/unity-package-distribution-license) |
| OpenXR loader distributed in the Unity package | Package 1.17.1 | [Khronos OpenXR SDK](https://github.com/KhronosGroup/OpenXR-SDK), Apache 2.0; see the package's third-party notices |

The Unity package's LICENSE.md and Third Party Notices.md are copied into the game's `.IKnowAGuyVR/licenses` directory. The mod archive itself does not redistribute those native dependency binaries. BepInEx supplies its own .NET runtime and supporting components; their upstream licenses continue to apply. Installing the mod does not install a headset runtime, change the active OpenXR runtime, or grant a license to the game.

I Know a Guy: Shady Life Simulator and its assets belong to their respective owners. This is an unofficial community mod.
