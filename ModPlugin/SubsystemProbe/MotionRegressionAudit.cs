using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Bounded scene-transition and anatomical-axis observations. Diagnostic build only.
internal static class MotionRegressionAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_REGRESSION")=="1";
    private static readonly bool wheelExit=Environment.GetEnvironmentVariable("IKAG_XR_REGRESSION_WHEEL_EXIT")=="1";
    private static int phase,bodySamples;
    private static bool initialMenu;
    private static float next=Environment.GetEnvironmentVariable("IKAG_XR_MACHINE_EXIT_AUDIT")=="1"?35:18;
    private static readonly BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
    [DllImport("UnityOpenXR",EntryPoint="NativeConfig_GetUseOpenXRPredictedTime")]
    [return:MarshalAs(UnmanagedType.I1)]private static extern bool PredictedTime();
    internal static void Body(Animator animator)
    {
        if(!enabled||bodySamples++>=3)return;
        foreach(bool left in new[]{true,false})
        {
            var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            var index=animator.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal);
            var pinky=animator.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal);
            var raw=left?ControllerTransport.Left:ControllerTransport.Right;
            if(StereoTrial.TryTrackingPose(raw.Position,raw.Rotation,out var p,out var q))
            {
                var x=Vector3.Cross(index.position-hand.position,pinky.position-hand.position).normalized;
                var z=(index.position-pinky.position).normalized;
                Plugin.Line($"REGRESSION_HAND left={left} palmXDot={Vector3.Dot(x,q*Vector3.right):R} thumbZDot={Vector3.Dot(z,q*Vector3.forward):R} wrist={hand.position:R} index={index.position:R} pinky={pinky.position:R} grip={p:R}/{q:R}");
            }
        }
    }
    private static void Snapshot(string stage)
    {
        object Field(string n)=>typeof(MotionPointer).GetField(n,flags).GetValue(null);
        var laser=Field("laser") as LineRenderer;
        var material=laser==null?null:laser.sharedMaterial;
        var shader=material==null?null:material.shader;
        ControllerTransport.Sample();var r=ControllerTransport.Right;
        bool mapped=StereoTrial.TryTrackingPose(r.AimPosition,r.AimRotation,out var p,out var q);
        var cam=Camera.main;
        Plugin.Line($"REGRESSION_CONTEXT stage={stage} wheelOpen={MotionInput.WheelOpen}");
        Plugin.Line($"REGRESSION_POINTER stage={stage} menu={StereoTrial.MenuActive} live={StereoTrial.GameplayActive} focus={Probe.Focused} cursor={Cursor.visible}/{Cursor.lockState} tracked={r.Tracked} mapped={mapped} ray={Field("validRay")} shaderTried={Field("shaderTried")} laser={(laser==null?"null":laser.name)} active={(laser!=null&&laser.gameObject.activeInHierarchy)} enabled={(laser!=null&&laser.enabled)} layer={(laser==null?-1:laser.gameObject.layer)} mask={(cam==null?0:cam.cullingMask):X8} shader={(shader==null?"null":shader.name)} material={(material==null?"null":material.name)} menuDoc={Field("menuDocument")!=null} p={p:R} q={q:R}");
        if(laser!=null)Plugin.Line($"REGRESSION_LASER stage={stage} start={laser.GetPosition(0):R} end={laser.GetPosition(1):R} colors={laser.startColor}/{laser.endColor} material={(material==null?Color.clear:material.color)} baseColor={(material!=null&&material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):Color.clear)} visible={laser.isVisible} forceOff={laser.forceRenderingOff}");
    }
    private static void AimMenu()
    {
        var menu=UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();
        RenderTextureScreenInput surface=null;
        foreach(var s in UnityEngine.Object.FindObjectsOfType<RenderTextureScreenInput>(true))if(s.uiDocument!=null&&s.uiDocument.Pointer==menu.uiDocument.Pointer){surface=s;break;}
        if(surface==null)throw new InvalidOperationException("Menu surface missing");
        var size=menu.uiDocument.rootVisualElement.layout;var uv=menu.btnSettings.worldBound.center;var rect=surface.screenRect;
        var origin=new Vector3(Mathf.Lerp(rect.x,rect.z,uv.x/size.width),Mathf.Lerp(rect.y,rect.w,1-uv.y/size.height),-1);
        float nearest=float.PositiveInfinity;var vertices=surface.vertices;var triangles=surface.triangles;
        for(int i=0;i<triangles.Length;i+=3)if(RenderTextureScreenInput.RayTriangle(origin,Vector3.forward,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],out var hit)&&hit<nearest)nearest=hit;
        if(!float.IsFinite(nearest))throw new InvalidOperationException("Native menu triangles missed");
        string command=MotionPointer.AimAtWorld(surface.screenMesh.transform.TransformPoint(origin+Vector3.forward*nearest));
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
        Plugin.Line("REGRESSION_MENU_AIM "+command);
    }
    internal static void Tick()
    {
        if(!enabled||phase>8)return;
        try
        {
            if(!initialMenu&&StereoTrial.MenuRendered){initialMenu=true;Snapshot("initial-menu");File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"shot regression-menu-initial\n");}
            if(Time.realtimeSinceStartup<next)return;
            if(phase==0)
            {
                if(!StereoTrial.GameplayActive)return;
                Plugin.Line("REGRESSION_OPENXR_PREDICTED_TIME "+PredictedTime());
                UiInputAudit.Dump("Assembly-CSharp.dll","","MachineInteractionBase","Update","TriggerExit","ExitMachineModeLocal");
                UiInputAudit.Dump("Assembly-CSharp.dll","UI.MainMenu","MainMenuManager","OnEnable","OnDisable");
                Snapshot("live");
                File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),wheelExit?"hands reset\nhand l grip pose -0.15 1.45 -0.35 -66.8014 0 21.5\nhead rot 23.1986 -21.5 0\n":"hands reset\nhead rot 0 0 0\n");
            }
            else if(phase==1&&wheelExit)
            {
                Plugin.Line("REGRESSION_WHEEL_BEFORE_EXIT "+MotionInput.WheelOpen);
                if(MotionInput.WheelOpen)throw new InvalidOperationException("Removed palm gesture opened wheel");
                // Exercise the scene-teardown boundary: the outgoing input
                // component stops updating before the new menu owns input.
                Core.Input.InputManager.Instance.enabled=false;
                HUDManager.LocalInstance.ExitToMainMenu();phase=3;
            }
            else if(phase==1){Snapshot("before-pause");HUDManager.LocalInstance.ShowEscapeMenu();}
            else if(phase==2){Snapshot("paused");HUDManager.LocalInstance.ShowExitConfirm();}
            else if(phase==3){Snapshot("exit-confirm");HUDManager.LocalInstance.ExitToMainMenu();}
            else
            {
                if(!StereoTrial.MenuRendered)return;
                Snapshot("returned-"+phase);
                if(phase==4)AimMenu();
                if(phase==5)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"shot regression-menu-aimed\n");
                if(phase==6)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"trigger r 1\n");
                if(phase==7)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"trigger r 0\n");
                if(phase==8){var menu=UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();Plugin.Line("REGRESSION_MENU_CLICK settings="+(menu.activePage.Pointer==menu.settingsPage.Pointer));}
                if(phase==8)Plugin.Line("MOTION_REGRESSION_AUDIT_COMPLETE");
            }
            ++phase;next=Time.realtimeSinceStartup+1;
        }
        catch(Exception e){Plugin.Line("MOTION_REGRESSION_AUDIT_FAILED "+e);phase=9;}
    }
}
