using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class SpyMotion
{
    private static int frame=-1;private static IntPtr stage;
    private static InputAction tuneLeft,tuneRight,lockAction;
    private static bool armed,press,heldLeft,heldRight,previousPress;
    internal static bool Active {get {var host=SpyMiniGameManager.LocalInstance;return host!=null&&host.IsSessionActive;}}
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.spy-controls");
        h.Patch(typeof(FrequencyTuneMiniGame).GetMethod("StartGame"),postfix:Hook(nameof(TuningStarted)));
        h.Patch(typeof(SignalLockMiniGame).GetMethod("StartGame"),postfix:Hook(nameof(LockStarted)));
        h.Patch(typeof(InputAction).GetMethod("IsPressed"),postfix:Hook(nameof(Held)));
        h.Patch(typeof(InputAction).GetMethod("WasPerformedThisFrame"),postfix:Hook(nameof(Pressed)));
    }
    private static void TuningStarted(FrequencyTuneMiniGame __instance){tuneLeft=__instance.tuneLeftAction;tuneRight=__instance.tuneRightAction;armed=false;}
    private static void LockStarted(SignalLockMiniGame __instance){lockAction=__instance.lockAction;armed=false;}
    private static HarmonyMethod Hook(string n)=>new(typeof(SpyMotion).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static));
    private static void Sample()
    {
        if(frame==Time.frameCount)return;frame=Time.frameCount;press=heldLeft=heldRight=false;
        var host=SpyMiniGameManager.LocalInstance;var hud=HUDManager.LocalInstance;
        var current=host!=null&&host.IsSessionActive?host.activeStage:null;
        if(current==null||!MotionInput.Enabled||!Probe.Focused||(hud!=null&&hud.IsEscapeMenuOpen)){armed=false;stage=IntPtr.Zero;return;}
        ControllerTransport.Sample();var l=VrControls.OffInput;var r=VrControls.MainInput;
        if(!l.Tracked||!r.Tracked){armed=false;return;}
        if(stage!=current.Pointer){stage=current.Pointer;armed=false;tuneLeft=host.frequencyTuneGame?.tuneLeftAction;tuneRight=host.frequencyTuneGame?.tuneRightAction;lockAction=host.signalLockGame?.lockAction;}
        // Stage ownership is set before StartGame creates its action objects.
        // Fill initially absent references once creation completes.
        if(tuneLeft==null)tuneLeft=host.frequencyTuneGame?.tuneLeftAction;
        if(tuneRight==null)tuneRight=host.frequencyTuneGame?.tuneRightAction;
        if(lockAction==null)lockAction=host.signalLockGame?.lockAction;
        bool button=r.Trigger||r.Primary;
        if(!armed){armed=!button&&Mathf.Abs(l.Stick.x)<.25f;previousPress=button;return;}
        press=button&&!previousPress;previousPress=button;
        heldLeft=l.Stick.x<-.4f;heldRight=l.Stick.x>.4f;
    }

    private static void Held(InputAction __instance,ref bool __result)
    {if(!MotionInput.Enabled)return;Sample();if(!armed)return;if(tuneLeft!=null&&__instance.Pointer==tuneLeft.Pointer)__result|=heldLeft;else if(tuneRight!=null&&__instance.Pointer==tuneRight.Pointer)__result|=heldRight;}
    private static void Pressed(InputAction __instance,ref bool __result)
    {if(!MotionInput.Enabled)return;Sample();if(armed&&lockAction!=null&&__instance.Pointer==lockAction.Pointer)__result|=press;}
}
