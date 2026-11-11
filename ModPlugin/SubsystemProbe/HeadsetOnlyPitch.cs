using HarmonyLib;

namespace IKnowAGuyVR.SubsystemProbe;

internal static class HeadsetOnlyPitch
{
    internal static void Install()
    {
        var patch = new Harmony("iknowaguyvr.headset-only-pitch");
        patch.Patch(typeof(PlayerMovementController).GetMethod("CacheInput"),
            postfix: new HarmonyMethod(typeof(HeadsetOnlyPitch).GetMethod(nameof(Filter), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
    }
    internal static void Filter(PlayerMovementController __instance)
    {
        if (!__instance._IsOwner_k__BackingField) return;
        // Save restoration can load an old pitch even though vertical input is
        // now zero (the reported save carries 12.997 degrees). Neutralize only
        // ordinary player look; leave the game's forced camera sequences alone.
        if (__instance.canLook && !__instance.forcedLookActive)
        {
            if (__instance.verticalRotation != 0) __instance.verticalRotation = 0;
            if (__instance.targetVerticalRotation != 0) __instance.targetVerticalRotation = 0;
        }
        // Current native CacheInput copies InputManager.lookInput into this field;
        // HandleCameraRotation consumes y for pitch and x for yaw. Leave the
        // source input intact for menus, pointer handling and other consumers.
        var look = __instance.lookInput;
        if (look.y == 0) return;
        look.y = 0;
        __instance.lookInput = look;
    }
}
