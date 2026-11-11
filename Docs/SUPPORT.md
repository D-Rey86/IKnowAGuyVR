# Troubleshooting and reporting problems

If VR does not start, check that the headset is connected, the intended OpenXR runtime is ready, the game build is supported, and the package installed successfully. Launch through Steam normally. On the first launch, wait for BepInEx's interop generation to finish. `BepInEx/LogOutput.log` records its progress or failure.

If the headset freezes but the monitor continues, or the game crashes, keep the logs before launching again because a new session can overwrite them. Record what you were doing, whether only the headset froze, and whether the game closed by itself or during exit.

## Diagnostic report

In **Settings > VR > Support**, choose **Create Diagnostic Report**. Reports are saved under the game folder at `BepInEx/diagnostics/IKnowAGuyVR`. Nothing is uploaded automatically.

The default report includes versions, hardware/runtime details, graphics and VR settings, tracking state and the plugin hash. **Include recent logs** is optional and unchecked by default. Existing game or other-mod logs can contain names or network details; review the ZIP before sharing it. Saves and screenshots are not collected by the report. A snapshot is not a recording of the preceding gameplay.

When reporting an issue, include:

- Mod and game version; headset, OpenXR runtime, GPU and other installed mods.
- What happened, what you expected, and steps that reproduce it if known.
- Whether it happens with motion controllers or gamepad, and any relevant handedness/seated/UI settings.
- The reviewed diagnostic ZIP, or the last session's `SubsystemProbe.log`, `StereoTrial.log`, and `BepInEx/LogOutput.log` if the menu cannot be reached.
- A short video or screenshot when it explains a visual or interaction problem.

Review your report for private information before posting it. Attach a save only if it is needed and you are comfortable sharing it. [Report an issue on GitHub](https://github.com/D-Rey86/IKnowAGuyVR/issues)

Installer failures happen before game files are changed when preflight checks fail. A failed write triggers restoration from the recorded backup. Keep the error message and `.IKnowAGuyVR` receipts. Rollback refuses to overwrite files changed after installation; inspect the conflict instead of deleting the backups.
