using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

// Four bounded local input controls. Never enabled in the normal installation.
internal static class PitchInputControl
{
    private static int samples;
    private static bool pending;
    private static Core.Input.InputManager input;
    private static Vector2 savedRaw;
    private static float vertical, horizontal, targetVertical, targetHorizontal;
    private static string path;
    internal static void Begin()
    {
        path = Path.Combine(BepInEx.Paths.GameRootPath, "PitchInputControl.log");
        File.WriteAllText(path, "Bounded native cached-input control\n");
        var patch = new Harmony("iknowaguyvr.pitch-input-control");
        patch.Patch(typeof(PlayerMovementController).GetMethod("CacheInput"), prefix: Hook(nameof(Inject)), postfix: Hook(nameof(RestoreRaw)));
        patch.Patch(typeof(PlayerMovementController).GetMethod("HandleCameraRotation"), prefix: Hook(nameof(Before)), postfix: Hook(nameof(After)));
    }
    private static HarmonyMethod Hook(string name) => new(typeof(PitchInputControl).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
    private static void Inject(PlayerMovementController __instance)
    {
        if (samples >= 4 || !__instance.IsOwner || !__instance.canLook || __instance.forcedLookActive || __instance.IsLookSuppressed) return;
        if (input == null) input = Core.Input.InputManager.Instance;
        if (input == null) return;
        savedRaw = input.lookInput;
        input.lookInput = samples % 2 == 0 ? new Vector2(12, 9) : new Vector2(-12, -9);
        pending = true;
    }
    private static void RestoreRaw() { if (pending) input.lookInput = savedRaw; }
    private static void Before(PlayerMovementController __instance)
    {
        if (!pending || !__instance.IsOwner) return;
        vertical = __instance.verticalRotation; horizontal = __instance.horizontalRotation;
        targetVertical = __instance.targetVerticalRotation; targetHorizontal = __instance.targetHorizontalRotation;
    }
    private static void After(PlayerMovementController __instance)
    {
        if (!pending || !__instance.IsOwner) return;
        var filtered = __instance.lookInput;
        float deltaV = __instance.targetVerticalRotation - targetVertical;
        float deltaH = __instance.targetHorizontalRotation - targetHorizontal;
        bool pass = filtered.y == 0 && Math.Abs(filtered.x) == 12 && deltaV == 0 && deltaH != 0 && input.lookInput == savedRaw;
        File.AppendAllText(path, $"CONTROL sample={samples} filtered={filtered} pitchDelta={deltaV:R} yawDelta={deltaH:R} sourceRestored={input.lookInput == savedRaw} PASS={pass}\n");
        __instance.verticalRotation = vertical; __instance.horizontalRotation = horizontal;
        __instance.targetVerticalRotation = targetVertical; __instance.targetHorizontalRotation = targetHorizontal;
        pending = false;
        ++samples;
        if (samples == 4) File.AppendAllText(path, "COMPLETE four controls; original rotation fields restored\n");
    }
}
