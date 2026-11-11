using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class HeadAimAudit
{
    private static int phase;
    private static float next;
    private static Vector3 expectedPosition, expectedForward, savedPosition;
    private static Quaternion savedRotation;
    private static Transform target;
    private static bool sample;
    internal static void Install()
    {
        var patch = new Harmony("iknowaguyvr.head-aim-audit");
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        patch.Patch(typeof(InteractionManager).GetMethod("PerformRaycast"),
            prefix: new HarmonyMethod(typeof(HeadAimAudit).GetMethod(nameof(Before), flags)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(HeadAimAudit).GetMethod(nameof(AfterRay), flags)),
            finalizer: new HarmonyMethod(typeof(HeadAimAudit).GetMethod(nameof(Restored), flags)) { priority = Priority.Last });
        patch.Patch(typeof(InteractionManager).GetMethod("FindClosestInteractable"), postfix: new HarmonyMethod(typeof(HeadAimAudit).GetMethod(nameof(Selection), flags)));
    }
    private static void Before(InteractionManager __instance)
    {
        sample = false;
        if (phase >= 6 || Time.realtimeSinceStartup < next || !__instance._IsOwner_k__BackingField) return;
        if (!StereoTrial.TryInteractionPose(__instance.interactionCamera, out target, out expectedPosition, out var rotation)) return;
        expectedForward = rotation * Vector3.forward;
        savedPosition = target.localPosition; savedRotation = target.localRotation;
        sample = true;
    }
    private static void AfterRay(InteractionManager __instance)
    {
        if (!sample) return;
        var ray = __instance.interactionRay;
        Plugin.Line($"HEAD_AIM_CONTROL phase={phase} origin={ray.origin.ToString("R")} direction={ray.direction.ToString("R")} originError={(ray.origin-expectedPosition).magnitude:R} directionError={(ray.direction-expectedForward).magnitude:R} hit={__instance.rayHit.point.ToString("R")}");
    }
    private static void Restored()
    {
        if (sample) Plugin.Line($"HEAD_AIM_RESTORE phase={phase} positionError={(target.localPosition-savedPosition).magnitude:R} rotationEqual={target.localRotation.Equals(savedRotation)}");
    }
    private static void Selection(InteractionManager __instance)
    {
        if (!sample) return;
        Plugin.Line($"HEAD_AIM_SELECTION phase={phase} target={(__instance.closestInteractable == null ? "none" : __instance.closestInteractable.name)}");
        var commands = new[] { "head rot 0 40 0", "head rot 0 -40 0", "head rot 45 0 0", "head pos 0.1 1.7 0", "head rot 0 0 0\nhead pos 0 1.6 0", "shot head-aim-final" };
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"), "command.txt"), commands[phase] + "\n");
        ++phase; next = Time.realtimeSinceStartup + 2;
        if (phase == 6) Plugin.Line("HEAD_AIM_CONTROLS_COMPLETE");
        sample = false;
    }
}
