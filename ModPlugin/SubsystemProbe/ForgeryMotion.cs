using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ForgeryMotion
{
    private static bool scoped;
    private static ForgeryStation cached;
    private static IntPtr owner;
    internal static bool Active
    {
        get {
            if(!MotionInput.Enabled||!Probe.Focused||!MachineInteractionBase.IsLocalPlayerInMachineMode||MachineInteractionBase.isLocalMachineTransitionInProgress)return false;
            var machine=MachineInteractionBase.CurrentActiveMachine;if(machine==null)return false;
            if(owner!=machine.Pointer){owner=machine.Pointer;cached=machine.TryCast<ForgeryStation>();}
            var hud=HUDManager.LocalInstance;
            return cached!=null&&(hud==null||!hud.IsEscapeMenuOpen);
        }
    }
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.forgery-pointer");
        // RaycastDesk is inlined inside HandleMachineInput in the current game.
        foreach(var method in new[]{"HandleMachineInput","RaycastDesk"})
            h.Patch(typeof(ForgeryStation).GetMethod(method),prefix:Hook(nameof(Begin)),finalizer:Hook(nameof(End)));
        h.Patch(typeof(Physics).GetMethod("Raycast",new[]{typeof(Ray),typeof(RaycastHit).MakeByRefType(),typeof(float),typeof(int),typeof(QueryTriggerInteraction)}),prefix:Hook(nameof(Aim)),postfix:Hook(nameof(Hit)));
    }
    private static HarmonyMethod Hook(string name)=>new(typeof(ForgeryMotion).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
    private static void Begin(ForgeryStation __instance,out bool __state)
    {__state=scoped;scoped=Active&&cached.Pointer==__instance.Pointer&&MotionPointer.TryControllerRay(out _);}
    private static void End(bool __state){scoped=__state;}
    private static void Aim(ref Ray ray){if(scoped&&MotionPointer.TryControllerRay(out var controller))ray=controller;}
    private static void Hit(RaycastHit hitInfo,bool __result){if(scoped&&__result)MotionPointer.RecordMachineHit(hitInfo.point);}
}
