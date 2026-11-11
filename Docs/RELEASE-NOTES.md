# I Know a Guy VR 1.0.1

**1.0.1** bundles installation dependencies in both player ZIPs. BepInEx, its .NET runtime and the Unity libraries used for first-launch interop generation are included. The VR package also includes the native OpenXR provider and loader. Both installer and manual setup use these bundled files.

Fresh installation, rollback and VR gameplay UI were checked with dependency downloads blocked. The packages include the manual installation guide, motion controller reference and third-party license notices.

## Initial release

**1.0.0** is the first public release of I Know a Guy VR. It includes separate VR and flatscreen Companion packages, manual installation and rollback instructions, a motion controller reference, and MIT-licensed source.

Features include stereo OpenXR rendering, tracked motion controllers, physical item handling and melee, VR-aware prompts, an interactive wrist dashboard and map, a movable pinned journal, item belt, VR settings, left-handed and seated options, regular gamepad mode, local diagnostic reports, and multiplayer pose replication with an optional flatscreen companion.

Recent fixes include scene-reload fog-history cleanup, recovery from temporary tracking loss and delayed startup, ragdoll camera-validation tolerance, and removal of redundant empty overlay-camera work. The latter preserved visual settings and measured about 5% lower average whole-frame time in matched isolated outdoor tests; it is not a universal FPS guarantee.

Compatibility was checked with the OpenXR simulator and local multiplayer sessions. Broader physical headset coverage and Internet multiplayer remain unverified. [Compatibility and known limitations](COMPATIBILITY.md)
