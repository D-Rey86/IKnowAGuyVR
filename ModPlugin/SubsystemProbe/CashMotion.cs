using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class CashMotion
{
    private static bool scoped;
    private static CashRegisterMachine cached;
    internal static bool Active=>PointerActive&&cached.currentMode==CashRegisterMode.Cash;
    internal static bool PointerActive
    {
        get
        {
            if(!MotionInput.Enabled||!Probe.Focused||!MachineInteractionBase.IsLocalPlayerInMachineMode||MachineInteractionBase.isLocalMachineTransitionInProgress)return false;
            var machine=MachineInteractionBase.CurrentActiveMachine;if(machine==null)return false;
            if(cached==null||cached.Pointer!=machine.Pointer)cached=machine.TryCast<CashRegisterMachine>();
            var hud=HUDManager.LocalInstance;
            return cached!=null&&!cached.isProcessingPayment&&cached.isPaymentPending&&!cached.IsPowerOut&&(hud==null||!hud.IsEscapeMenuOpen);
        }
    }
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.cash-pointer");
        h.Patch(typeof(CashRegisterMachine).GetMethod("HandleMouseHover"),prefix:Hook(nameof(Begin)),finalizer:Hook(nameof(End)));
        h.Patch(typeof(Physics).GetMethod("Raycast",new[]{typeof(Ray),typeof(RaycastHit).MakeByRefType(),typeof(float),typeof(int)}),prefix:Hook(nameof(Aim)),postfix:Hook(nameof(Hit)));
    }
    private static HarmonyMethod Hook(string n)=>new(typeof(CashMotion).GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic));
    private static void Begin(CashRegisterMachine __instance,out bool __state)
    {__state=scoped;scoped=PointerActive&&cached.Pointer==__instance.Pointer&&MotionPointer.TryControllerRay(out _);}
    private static void End(bool __state){scoped=__state;}
    private static void Aim(ref Ray ray){if(scoped&&MotionPointer.TryControllerRay(out var controller))ray=controller;}
    private static void Hit(RaycastHit hitInfo,bool __result){if(scoped&&__result)MotionPointer.RecordMachineHit(hitInfo.point);}
}
