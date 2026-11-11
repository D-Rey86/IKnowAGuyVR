using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class RepairMotion
{
    private static Transform aim;private static int frame=-1,tipFrame=-1;
    private static RepairFocusMinigame active;
    private static Vector3 leftTip,rightTip;private static bool leftValid,rightValid;
    internal static bool Active=>active!=null&&!active.ending&&active.isActiveAndEnabled;
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.repair-aim");
        h.Patch(typeof(RepairFocusMinigame).GetMethod("IsAimedAt"),prefix:Hook(nameof(Aim)));
        h.Patch(typeof(RepairFocusMinigame).GetMethod("Initialize"),postfix:Hook(nameof(Bound)));
        foreach(var method in new[]{"End","OnDisable","OnDestroy"})h.Patch(typeof(RepairFocusMinigame).GetMethod(method),prefix:Hook(nameof(Ended)));
    }
    private static HarmonyMethod Hook(string name)=>new(typeof(RepairMotion).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
    private static void Bound(RepairFocusMinigame __instance,bool __result){if(__result){active=__instance;tipFrame=frame=-1;}}
    private static void Ended(RepairFocusMinigame __instance){if(active!=null&&active.Pointer==__instance.Pointer){active=null;leftValid=rightValid=false;tipFrame=-1;}}
    private static bool Finger(bool left,out Vector3 tip)
    {
        tip=default;var hand=left?ControllerTransport.Left:ControllerTransport.Right;var owner=PhysicalHeldItems.Owner;
        if(owner==null||!hand.Tracked||!hand.Grip||hand.TriggerValue>.1f||(owner.IsHoldingItem()&&ObjectGrabs.LeftHand==left)||PhysicalHeldItems.Attached(left)||MotionShovel.OwnsGrip(left)||KitchenHands.Pose(left,out _,out _)||!MotionBody.TryIndexGeometry(owner,left,out var offset,out _)||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))return false;
        tip=p+q*offset;return true;
    }
    private static bool Touch(RepairFocusMinigame.Ring ring,Vector3 tip)
    {
        if(ring.root==null||!ring.placed||ring.popping||ring.radius<=0)return false;
        // Native displayed circle plane and world radius. A 15 mm contact band
        // allows a fingertip to rest against the surface without exact zero depth.
        var delta=tip-ring.worldPosition;var normal=ring.root.transform.forward;float depth=Vector3.Dot(delta,normal);
        return Mathf.Abs(depth)<=.015f&&(delta-normal*depth).sqrMagnitude<=ring.radius*ring.radius;
    }
    private static bool Aim(RepairFocusMinigame __instance,RepairFocusMinigame.Ring ring,ref Transform eye,ref bool __result)
    {
        if(!MotionInput.Enabled||!StereoTrial.GameplayActive)return true;
        if(!Probe.Focused||!ControllerTransport.HeadTracked){__result=false;return false;}
        if(tipFrame!=Time.frameCount){tipFrame=Time.frameCount;leftValid=Finger(true,out leftTip);rightValid=Finger(false,out rightTip);}
        if(ring!=null&&((leftValid&&Touch(ring,leftTip))||(rightValid&&Touch(ring,rightTip)))){__result=true;return false;}
        if(!MotionPointer.TryControllerRay(DistanceGrips.AimLeft,out var ray)){__result=false;return false;}
        if(aim==null){var go=new GameObject("IKAG_RepairAim"){hideFlags=HideFlags.HideAndDontSave};Object.DontDestroyOnLoad(go);aim=go.transform;}
        if(frame!=Time.frameCount){frame=Time.frameCount;aim.SetPositionAndRotation(ray.origin,Quaternion.LookRotation(ray.direction));}
        eye=aim;return true;
    }
    internal static bool Pointer(out Vector3 point)
    {
        point=default;if(!Active||!MotionInput.Enabled||!StereoTrial.GameplayActive||!Probe.Focused||!MotionPointer.TryControllerRay(DistanceGrips.AimLeft,out var ray))return false;
        var rings=active.rings;if(rings==null)return false;float nearest=float.PositiveInfinity;
        for(int i=0;i<rings.Count;i++)
        {
            var ring=rings[i];if(ring==null||ring.root==null||!ring.placed||ring.popping)continue;
            var n=ring.root.transform.forward;float denom=Vector3.Dot(n,ray.direction);if(Mathf.Abs(denom)<.0001f)continue;
            float t=Vector3.Dot(ring.worldPosition-ray.origin,n)/denom;if(t>0&&t<nearest){nearest=t;point=ray.GetPoint(t);}
        }
        return nearest<float.PositiveInfinity;
    }
}
