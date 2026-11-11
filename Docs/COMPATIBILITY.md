# Compatibility and known limitations

The intended target is Windows x64, Direct3D 11, with a normal two-eye OpenXR PCVR configuration. Eye projection, eye orientation, IPD and recommended resolution come from the active runtime. The mod does not require an NVIDIA-specific rendering API.

| Area | Evidence and limits |
| --- | --- |
| Existing user's headset setup | Development builds have been used in real gameplay; performance varies by scene |
| Asymmetric and canted views, different eye sizes and refresh rates | Isolated simulator checks passed; this is not certification of every physical headset |
| AMD GPUs and additional physical headsets | Not physically verified; report issues with a diagnostic report |
| SteamVR | Earlier real-runtime tests with a virtual headset and simulator coverage; no broad physical-device certification |
| VDXR | User gameplay experience exists; a separate reported native fatal exit remains unexplained |
| Meta native PC runtime and other runtime/headset combinations | Not independently physically verified |
| Runtime restarts or full headset reconnect mid-game | Automatic full-session restart is unsupported; restart the game if necessary |
| Direct3D 12, Vulkan, standalone headsets, Linux and four-view configurations | Outside this release's supported scope |

## Performance and stability

Outdoor views can fall below the selected headset refresh rate. The latest optimization removes redundant empty-camera work while preserving image quality, but it does not promise a steady 90, 120 or 144 FPS. Hardware, runtime resolution and the game's scene complexity all affect performance.

The measured fog-history leak on scene reload and a reproduced ragdoll stereo-validation freeze have been fixed. Subsequent isolated scene transitions, tracking recovery and repeated knockdowns passed. A separate crash recorded in Virtual Desktop's runtime has not been assigned a proven root cause or claimed fixed. Report recurring crashes or frozen headset output even if the monitor continues updating.

## Multiplayer

The VR mod includes tracked head/body/hand/finger and held-item replication. Flatscreen players can install the lightweight Companion package to see those movements without starting VR.

The **host must have either the VR mod or Companion**. Every recipient who wants to see tracked movement also needs either package. An unmodified host cannot relay poses; an unmodified recipient keeps normal game animation. Install only one package per player. Pose protocol 1 is shared by both packages; mismatched protocols fall back rather than interpreting incompatible data.

Two- and three-client local tests covered VR and companion hosts, relay, VR/flatscreen receivers, held items and stale-pose fallback. Internet/Steam lobby sessions, all avatars and every held-item combination have not been fully verified. There is no external pose server or new account requirement. Pose updates are visual; native gameplay still controls damage, physics and inventory.

## Game updates and other mods

This release is tied to the game binaries listed in `package.json`. The installer rejects other binaries. The runtime also checks the native rendering contract, but that is not a guarantee that every earlier plugin initialization step is safe on an unsupported update. Disable the mod when the game updates until compatibility is confirmed.

Other mods that change rendering, camera stacks, input, player rigs or held-item presentation can conflict. Preserve the original installation and report the other mod versions when describing a problem.
