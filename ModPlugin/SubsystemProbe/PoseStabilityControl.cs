using System;
using System.IO;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in research only; excluded from the normal build.
internal static class PoseStabilityControl
{
    private static readonly bool enabled = Environment.GetEnvironmentVariable("IKAG_XR_POSE_STABILITY") == "1";
    private static int samples;
    internal static void TrackedCenter(Transform transform)
    {
        if (enabled) Plugin.Line("POSE_TRACKED_CENTER " + transform.localToWorldMatrix.ToString("R"));
    }
    internal static void CullingControls(Camera camera)
    {
        if (!enabled) return;
        var cull = camera.cullingMatrix; var projection = camera.projectionMatrix; var view = camera.worldToCameraMatrix;
        bool positive = StereoTrial.CullingMatches(cull, projection, view);
        cull.m03 += 1;
        bool displaced = StereoTrial.CullingMatches(cull, projection, view);
        cull = camera.cullingMatrix; var wrongProjection = projection; wrongProjection.m00 *= 1.01f;
        bool wrongFov = StereoTrial.CullingMatches(cull, wrongProjection, view);
        cull.m00 = float.NaN;
        bool nonfinite = StereoTrial.CullingMatches(cull, projection, view);
        Plugin.Line($"CULLING_CONTROLS valid={positive} displacedAccepted={displaced} wrongFovAccepted={wrongFov} nonfiniteAccepted={nonfinite} PASS={positive && !displaced && !wrongFov && !nonfinite}");
    }
    internal static void Alignment(Camera camera, Quaternion head)
    {
        if (!enabled) return;
        Plugin.Line($"POSE_ALIGNMENT camera={camera.name} worldPosition={camera.transform.position.ToString("R")} worldRotation={camera.transform.rotation.ToString("R")} head={head.ToString("R")}");
        var player = camera.GetComponentInParent<PlayerMovementController>();
        if (player != null)
        {
            Plugin.Line($"POSE_GAME_LOOK vertical={player.verticalRotation:R} targetVertical={player.targetVerticalRotation:R} horizontal={player.horizontalRotation:R} targetHorizontal={player.targetHorizontalRotation:R}");
            PitchControls(player);
        }
        for (var t = camera.transform; t != null; t = t.parent)
            Plugin.Line($"POSE_PARENT name={t.name} localRotation={t.localRotation.ToString("R")} worldRotation={t.rotation.ToString("R")}");
        samples = 0;
    }
    private static void PitchControls(PlayerMovementController player)
    {
        var savedLook = player.lookInput;
        float savedPitch = player.verticalRotation, savedTarget = player.targetVerticalRotation;
        bool savedForced = player.forcedLookActive, savedCanLook = player.canLook;
        try
        {
            player.lookInput = new Vector2(12, 9); player.verticalRotation = 17; player.targetVerticalRotation = 23;
            player.canLook = true; player.forcedLookActive = true;
            HeadsetOnlyPitch.Filter(player);
            bool forcedKept = player.verticalRotation == 17 && player.targetVerticalRotation == 23;
            player.canLook = false; player.forcedLookActive = false;
            HeadsetOnlyPitch.Filter(player);
            bool inactiveKept = player.verticalRotation == 17 && player.targetVerticalRotation == 23;
            player.canLook = true;
            HeadsetOnlyPitch.Filter(player);
            bool ordinaryLevel = player.verticalRotation == 0 && player.targetVerticalRotation == 0 && player.lookInput == new Vector2(12, 0);
            Plugin.Line($"PITCH_STATE_CONTROLS forcedKept={forcedKept} inactiveKept={inactiveKept} ordinaryLevel={ordinaryLevel} PASS={forcedKept && inactiveKept && ordinaryLevel}");
        }
        finally
        {
            player.lookInput = savedLook; player.verticalRotation = savedPitch; player.targetVerticalRotation = savedTarget;
            player.forcedLookActive = savedForced; player.canLook = savedCanLook;
        }
    }
    internal static void Frame(bool gameplay, int pair, string path)
    {
        if (!enabled || path == null) return;
        if (!gameplay && pair == 120) File.WriteAllText(path, "head rot 35 20 10\n");
        if (!gameplay || pair < 120 || pair % 8 != 0 || samples >= 1200) return;
        // Fixed deterministic coverage of yaw, pitch and roll, identical for A/B.
        float yaw = (samples * 137 % 350) - 175;
        float pitch = (samples * 47 % 150) - 75;
        float roll = (samples * 23 % 70) - 35;
        File.WriteAllText(path, $"head rot {yaw} {pitch} {roll}\n");
        if (samples % 200 == 0) Plugin.Line($"POSE_SWEEP sample={samples} pair={pair} yaw={yaw} pitch={pitch} roll={roll}");
        if (++samples == 1200) { File.WriteAllText(path, "head rot 35 0 0\nshot pose-sweep-complete\n"); StereoTrial.DiagnosticMetrics(); Plugin.Line("POSE_SWEEP_COMPLETE 1200 head poses"); }
    }
}
