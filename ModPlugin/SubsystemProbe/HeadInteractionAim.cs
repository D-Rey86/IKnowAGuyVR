using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

internal static class HeadInteractionAim
{
    internal struct Scope
    {
        internal Transform Transform;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal bool ControllerAim;
        internal bool ControllerLeft;
    }
    internal static void Install()
    {
        var patch = new Harmony("iknowaguyvr.head-interaction-aim");
        foreach (string method in MotionInput.Enabled ? new[] { "PerformRaycast", "FindClosestInteractable", "IsStillFacingHoldTarget", "DropLastItem", "SpawnPunchCollider", "HandleDartThrow", "HandleHammerSwing" } : new[] { "PerformRaycast", "FindClosestInteractable", "IsStillFacingHoldTarget" })
            patch.Patch(typeof(InteractionManager).GetMethod(method),
                prefix: new HarmonyMethod(typeof(HeadInteractionAim).GetMethod(method=="DropLastItem"||method=="HandleDartThrow"||method=="HandleHammerSwing"?nameof(BeginHeld):nameof(Begin), BindingFlags.NonPublic | BindingFlags.Static)),
                postfix: method=="PerformRaycast"?new HarmonyMethod(typeof(HeadInteractionAim).GetMethod(nameof(Captured), BindingFlags.NonPublic | BindingFlags.Static)):method=="SpawnPunchCollider"?new HarmonyMethod(typeof(HeadInteractionAim).GetMethod(nameof(Punched),BindingFlags.NonPublic|BindingFlags.Static)):null,
                finalizer: new HarmonyMethod(typeof(HeadInteractionAim).GetMethod(nameof(End), BindingFlags.NonPublic | BindingFlags.Static)));
    }
    private static void Begin(InteractionManager __instance, out Scope __state)
    {Apply(__instance,false,out __state);}
    private static void BeginHeld(InteractionManager __instance,out Scope __state)
    {Apply(__instance,true,out __state);}
    private static void Apply(InteractionManager __instance,bool heldAction,out Scope __state)
    {
        __state = default;
        if (!__instance._IsOwner_k__BackingField || !StereoTrial.TryInteractionPose(__instance.interactionCamera, out var transform, out var position, out var rotation)) return;
        if (MotionInput.Enabled)
        {
            ControllerTransport.Sample();DistanceGrips.Input();bool useLeft=DistanceGrips.AimLeft;var hand=useLeft?ControllerTransport.Left:ControllerTransport.Right;
            if(MotionTools.TryActionPose(out var tp,out var tq)){position=tp;rotation=tq;__state.ControllerAim=true;}
            else if(MotionPunch.TryPose(out var mp,out var mq)){position=mp;rotation=mq;__state.ControllerAim=true;}
            else if(heldAction&&MotionHeldItems.TryUsePose(__instance,out var ip,out var iq)){position=ip;rotation=iq;__state.ControllerAim=true;}
            else if(hand.Tracked && StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out var rp,out var rq)) {position=rp;rotation=rq;__state.ControllerAim=true;__state.ControllerLeft=useLeft;}
        }
        __state.Transform=transform;__state.Position=transform.localPosition;__state.Rotation=transform.localRotation;
        transform.SetPositionAndRotation(position, rotation);
    }
    private static void Captured(InteractionManager __instance,Scope __state)
    {if(__instance._IsOwner_k__BackingField){InteractionReticle.Record(__instance,__state.ControllerAim);DistanceGrips.Queried(__instance,__state.ControllerAim,__state.ControllerLeft);}}
    private static void Punched(InteractionManager __instance)
    {if(__instance._IsOwner_k__BackingField)MotionHaptics.Pulse(MotionPunch.TryPose(out _,out _)&&MotionPunch.StrikingLeft,.5f,.045f);}
    private static void End(Scope __state)
    {
        if (__state.Transform != null) __state.Transform.SetLocalPositionAndRotation(__state.Position, __state.Rotation);
    }
}
