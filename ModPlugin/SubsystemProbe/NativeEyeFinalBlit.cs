using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using UnityEngine.Rendering.RenderGraphModule;

namespace IKnowAGuyVR.SubsystemProbe;

// Trial only. The physical destination is checked inside the final postprocess pass.
internal static class NativeEyeFinalBlit
{
    private static Harmony patch;
    private static bool active, finalCopy;
    private static bool emptyOverlay;
    private static UniversalCameraData pendingResolve;
    private static int targetId;
    internal static int Applied { get; private set; }
    internal static int Calls { get; private set; }
    internal static int UIApplied { get; private set; }
    internal static string Failure { get; private set; }
    internal static bool IsEyeTarget(RenderTexture texture) => active && texture != null && texture.GetInstanceID() == targetId;

    internal static void Install()
    {
        Applied = Calls = UIApplied = 0; Failure = null;
        patch = new Harmony("iknowaguyvr.bounded-native-eye-origin");
        patch.Patch(typeof(UniversalRenderPipeline).GetMethod("RenderSingleCamera",
            new[] { typeof(ScriptableRenderContext), typeof(UniversalCameraData) }),
            prefix: Hook(nameof(CameraBegin)));
        var closure = typeof(PostProcessPassRenderGraph.__c).GetMethod("_RenderFinalBlit_b__100_0");
        if (closure == null) throw new MissingMethodException("Measured final postprocess callback");
        patch.Patch(closure, prefix: Hook(nameof(FinalBegin)), postfix: Hook(nameof(FinalEnd)));
        var stackClosure = typeof(FinalBlitPass.__c).GetMethod("_Render_b__15_0");
        if (stackClosure == null) throw new MissingMethodException("Stack final-copy callback");
        patch.Patch(stackClosure, prefix: Hook(nameof(StackBegin)), postfix: Hook(nameof(FinalEnd)));
        var blit = typeof(Blitter).GetMethod("BlitTexture", new[] { typeof(RasterCommandBuffer), typeof(RTHandle), typeof(Vector4), typeof(Material), typeof(int) });
        if (blit == null) throw new MissingMethodException("Raster Blitter.BlitTexture");
        patch.Patch(blit, prefix: Hook(nameof(Blit)));
        if (Probe.ScreenUI)
        {
            patch.Patch(typeof(UniversalRenderer).GetMethod("OnAfterRendering"),
                postfix: Hook(nameof(ScreenUI)));
            EyeScreenUI.Install(patch);
            OverlayCursor.Install();
        }
    }
    private static HarmonyMethod Hook(string name) => new(typeof(NativeEyeFinalBlit).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
    internal static void Begin(int instanceId, bool omitEmptyOverlay = false)
    { targetId = instanceId; active = true; emptyOverlay = omitEmptyOverlay; }
    internal static void End()
    {
        if (pendingResolve != null)
        {
            pendingResolve.resolveFinalTarget = true;
            pendingResolve = null;
            Failure = "Empty overlay final copy was not scheduled";
        }
        active = false; emptyOverlay = false; finalCopy = false; EyeScreenUI.EndEye();
    }
    internal static void Remove() { UiEvaluationBridge.Remove(); UiCommandBridge.Remove(); End(); patch?.UnpatchSelf(); patch = null; OverlayCursor.Suspend(); }

    private static void CameraBegin(UniversalCameraData cameraData)
    {
        if (!active || !emptyOverlay || cameraData.renderType != CameraRenderType.Base ||
            !IsEyeTarget(cameraData.targetTexture)) return;
        // Keep the original base post-processing followed by the overlay's copy.
        // Resolving the base directly would activate extra FXAA/FSR passes.
        if (!cameraData.resolveFinalTarget || pendingResolve != null)
        { Failure = "Unexpected empty overlay camera scope"; return; }
        pendingResolve = cameraData;
        cameraData.resolveFinalTarget = false;
    }

    private static void FinalBegin(PostProcessPassRenderGraph.PostProcessingFinalBlitPassData data)
    {
        if (!active) return;
        try
        {
            SetDestination(data.destinationTexture);
        }
        catch (Exception e) { Failure = e.ToString(); }
    }
    private static void StackBegin(FinalBlitPass.PassData data)
    {
        if (!active) return;
        try
        {
            if (Calls == 0) Plugin.Line($"STACK_FINAL_VIEWPORT fullScreen={data.useFullScreenViewport} rect={data.cameraData.pixelRect}");
            SetDestination(data.destination);
        }
        catch (Exception e) { Failure = e.ToString(); }
    }
    private static void SetDestination(RTHandle destination)
    {
        var id = destination.nameID;
        ++Calls;
        if (Calls == 1) Plugin.Line($"FINAL_COPY_DEST expected={targetId} actual={id.m_InstanceID} type={id.m_Type}");
        finalCopy = id.m_Type == BuiltinRenderTextureType.RenderTexture && id.m_InstanceID == targetId;
    }
    private static void FinalEnd() { finalCopy = false; }
    private static void ScreenUI(UniversalRenderer __instance, RenderGraph __0)
    {
        if (!active) return;
        try
        {
            var frame = __instance.frameData;
            var data = frame.Get<UniversalCameraData>();
            if (pendingResolve != null && pendingResolve.Pointer == data.Pointer)
            {
                data.resolveFinalTarget = true;
                pendingResolve = null;
                var finalResources = frame.Get<UniversalResourceData>();
                if (finalResources.isActiveTargetBackBuffer)
                    throw new InvalidOperationException("Empty overlay source was already resolved");
                var source = finalResources.cameraColor;
                var target = finalResources.backBufferColor;
                __instance.m_FinalBlitPass.Render(__0, frame, data, ref source, ref target,
                    finalResources.overlayUITexture);
                finalResources.SwitchActiveTexturesToBackbuffer();
            }
            if (!data.resolveFinalTarget || !data.isLastBaseCamera || !IsEyeTarget(data.targetTexture)) return;
            // The native OnAfterRendering inlines the resolve-to-screen test and
            // skips this existing pass for RT targets. Preserve every screen/HDR
            // decision and explicitly reuse only its final overlay pass here.
            if (data.rendersOverlayUI || data.isHDROutputActive) throw new InvalidOperationException("Unexpected existing eye UI route");
            var resources = frame.Get<UniversalResourceData>();
            var color = resources.backBufferColor;
            var depth = resources.backBufferDepth;
            __instance.m_DrawOverlayUIPass.RenderOverlay(__0, frame, ref color, ref depth);
            if (++UIApplied == 1) Plugin.Line($"NATIVE_EYE_UI camera={data.camera.name} target={targetId}");
        }
        catch (Exception e) { Failure = e.ToString(); }
    }
    private static void Blit(ref Vector4 scaleBias)
    {
        if (!active || !finalCopy) return;
        // Reverse the sampled Y interval only in the measured final postprocess
        // copy to this XR-owned target. Preserve any runtime/dynamic-resolution scale.
        float previousY = scaleBias.y, previousBias = scaleBias.w;
        scaleBias.w += scaleBias.y;
        scaleBias.y = -scaleBias.y;
        ++Applied;
        if (Applied == 1) Plugin.Line($"FINAL_COPY_ORIGIN y={previousY:R}->{scaleBias.y:R} bias={previousBias:R}->{scaleBias.w:R}");
    }
}
