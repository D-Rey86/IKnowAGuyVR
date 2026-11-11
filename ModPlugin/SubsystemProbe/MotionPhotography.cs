using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// Reuse the game's photographic camera, render target and subject queries.
// Its view matrix is implicit in the verified baseline; only pose is scoped.
internal static class MotionPhotography
{
    internal struct Scope { internal HeadInteractionAim.Scope Pose;internal bool Applied,Capture,OverlayEnabled;internal Camera Overlay; }
    private static int depth;
    internal static bool Capturing {get;private set;}
    private static HarmonyMethod Hook(string name)=>new(typeof(MotionPhotography).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.motion-photography");
        foreach(string method in new[]{"CaptureScreenshot","UpdateLiveSubjectDetection","DetectPhotoSubjects"})
            h.Patch(typeof(PhotoCamera).GetMethod(method),prefix:Hook(nameof(Begin)),finalizer:Hook(nameof(End)));
    }
    private static void Begin(PhotoCamera __instance,MethodBase __originalMethod,out Scope __state)
    {
        __state=default;
        if(depth!=0||!StereoTrial.GameplayActive||!Probe.Focused)return;
        var interaction=InteractionManager.LocalInstance;
        if(interaction==null||interaction.cachedTopItem==null||interaction.cachedTopItem.Pointer!=__instance.gameObject.Pointer)return;
        ControllerTransport.Sample();
        if(!MotionHeldItems.TryUsePose(interaction,out var p,out var q))return;
        if(__instance.captureCamera==null)__instance.FindCaptureCamera();
        var camera=__instance.captureCamera;
        if(camera==null||!StereoTrial.IsGameplayCamera(camera))return;
        var t=camera.transform;
        __state=new Scope{Pose=new HeadInteractionAim.Scope{Transform=t,Position=t.localPosition,Rotation=t.localRotation},Applied=true,Capture=__originalMethod.Name=="CaptureScreenshot"};
        ++depth;if(__state.Capture)Capturing=true;
        t.SetPositionAndRotation(p,q);
        if(__state.Capture)
        {
            __state.Overlay=StereoTrial.GameplayOverlay;
            if(__state.Overlay!=null){__state.OverlayEnabled=__state.Overlay.enabled;__state.Overlay.enabled=false;}
            MotionBody.HideForPhoto();
        }
#if IKAG_DIAGNOSTICS
        if(__state.Capture)Plugin.Line($"PHOTO_CAPTURE_POSE originError={(t.position-p).magnitude:R} directionError={(t.forward-q*Vector3.forward).magnitude:R} position={p:R} direction={t.forward:R} zoom={__instance.currentZoom:R} fov={camera.fieldOfView:R}");
#endif
    }
    private static void End(Scope __state)
    {
        if(!__state.Applied)return;
        if(__state.Pose.Transform!=null)__state.Pose.Transform.SetLocalPositionAndRotation(__state.Pose.Position,__state.Pose.Rotation);
        --depth;
        if(__state.Capture)
        {
            if(__state.Overlay!=null)__state.Overlay.enabled=__state.OverlayEnabled;
            MotionBody.RestoreAfterPhoto();Capturing=false;
#if IKAG_DIAGNOSTICS
            Plugin.Line($"PHOTO_CAPTURE_RESTORED pose={(__state.Pose.Transform!=null&&__state.Pose.Transform.localPosition==__state.Pose.Position&&__state.Pose.Transform.localRotation==__state.Pose.Rotation)} overlay={(__state.Overlay==null||__state.Overlay.enabled==__state.OverlayEnabled)}");
#endif
        }
    }
}
