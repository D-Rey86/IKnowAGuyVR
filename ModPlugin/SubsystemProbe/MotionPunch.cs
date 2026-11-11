using System;
using UnityEngine;
using NumericsVector=System.Numerics.Vector3;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class MotionPunch
{
    private static readonly PunchGesture left=new(),right=new();
    private static bool dispatch;
    private static Vector3 strikePosition;
    private static Quaternion strikeRotation;
    internal static bool StrikingLeft{get;private set;}
    internal static void Reset(){left.Reset();right.Reset();}
    internal static bool TryPose(out Vector3 position,out Quaternion rotation)
    {position=strikePosition;rotation=strikeRotation;return dispatch;}
    private static NumericsVector N(Vector3 p)=>new(p.x,p.y,p.z);
    internal static void Tick(InteractionManager owner,bool placement)
    {
        if(owner==null||placement||KitchenHands.Acquiring||!ControllerTransport.HeadTracked||owner.IsHoldingItem()||!owner.interactionEnabled||owner.skatingSuppressed||MotionInput.PhotoMode||MotionInput.WheelOpen)
        {Reset();return;}
        double now=Time.realtimeSinceStartupAsDouble;
        Hand(owner,true,ControllerTransport.Left,left,now);Hand(owner,false,ControllerTransport.Right,right,now);
    }
    private static void Hand(InteractionManager owner,bool useLeft,ControllerTransport.Hand hand,PunchGesture gesture,double now)
    {
        if(DistanceGrips.OwnsGrip(useLeft)||!hand.Tracked||!MotionBody.TryPunchGeometry(owner,useLeft,out var axis,out var offset)){gesture.Reset();return;}
        var direction=hand.Rotation*axis;
        if(!gesture.Sample(N(hand.Position-ControllerTransport.HeadPosition),N(hand.Position),N(direction),hand.Grip&&hand.Trigger,now))return;
        if(Time.time-owner.lastPunchTime<owner.punchCooldown||InteractionManager.IsHeldItemUseBlocked)return;
        if(!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var grip,out var orientation))return;
        var velocity=new Vector3(gesture.Velocity.X,gesture.Velocity.Y,gesture.Velocity.Z).normalized;
        var forward=orientation*(Quaternion.Inverse(hand.Rotation)*velocity);
        var up=orientation*Vector3.right;if(Vector3.Cross(forward,up).sqrMagnitude<1e-6f)up=orientation*Vector3.up;
        strikePosition=grip+orientation*offset;strikeRotation=Quaternion.LookRotation(forward,up);
#if IKAG_DIAGNOSTICS
        GripPunchAudit.BeforeStrike(owner);
#endif
        dispatch=true;
        StrikingLeft=useLeft;
        try
        {
            owner.SpawnPunchCollider();owner.lastPunchTime=Time.time;
            owner.PlayPunchSwooshSoundServerRpc();
#if IKAG_DIAGNOSTICS
            GripPunchAudit.Strike(useLeft,strikePosition,gesture.Velocity.Length());
#endif
        }
        finally{dispatch=false;}
    }
}
