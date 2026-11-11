using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionChest
{
 private static ChestItem chest;private static bool grabbed,wasGrip,armed,left,trigger;
 private static Bounds bounds;private static Matrix4x4 closedToRoot;private static Quaternion baseRotation,handFromLid;
 private static Vector3 gripInLid;private static float angle,startAngle,startVector;
 internal static int Revision{get;private set;}
 internal static bool OwnsGrip(bool useLeft)=>grabbed&&PhysicalHeldItems.Selected&&PhysicalHeldItems.Chest==chest&&useLeft!=left;
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.physical-chest");
  h.Patch(typeof(ChestItem).GetMethod("PollPeekInput"),prefix:Hook(nameof(Peek)));
  h.Patch(typeof(ChestItem).GetMethod("AnimateLid"),prefix:Hook(nameof(Animate)));
 }
 private static HarmonyMethod Hook(string n)=>new(typeof(MotionChest).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static));
 internal static void Reset(){if(grabbed)Revision++;grabbed=wasGrip=armed=trigger=false;chest=null;}
 private static bool Peek(ChestItem __instance)=>!(MotionInput.Enabled&&StereoTrial.GameplayActive&&PhysicalHeldItems.Selected&&__instance==PhysicalHeldItems.Chest);
 private static bool Animate(ChestItem __instance)=>!(grabbed&&__instance==chest);
 internal static void Input()
 {
  var current=PhysicalHeldItems.Chest;var h=PhysicalHeldItems.Other;
  if(chest!=current)
  {
   Reset();chest=current;left=PhysicalHeldItems.Left;
   if(chest!=null&&chest.lid!=null)
   {
    var lid=chest.lid;var mesh=lid.GetComponent<MeshFilter>();bounds=mesh==null||mesh.sharedMesh==null?default:mesh.sharedMesh.bounds;
    closedToRoot=chest.transform.worldToLocalMatrix*lid.parent.localToWorldMatrix*Matrix4x4.TRS(lid.localPosition,chest.closedLocalRotation,lid.localScale);
    baseRotation=Quaternion.Inverse(chest.transform.rotation)*lid.parent.rotation*chest.closedLocalRotation;
   }
  }
  bool allowed=chest!=null&&chest.lid!=null&&bounds.size.sqrMagnitude>0&&PhysicalHeldItems.Ready&&!chest.IsBuried;
  if(!allowed||left!=PhysicalHeldItems.Left){if(grabbed){grabbed=false;Revision++;}left=PhysicalHeldItems.Left;armed=false;wasGrip=h.Grip;trigger=MotionInput.PrimaryHeld;return;}
  if(!h.Grip){armed=true;if(grabbed){grabbed=false;Revision++;chest.RequestLidOpen(angle>=chest.openAngleX*.5f);}}
  if(!PhysicalHeldItems.TryRoot(out var p,out var q)||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var hand,out var rotation)){wasGrip=h.Grip;return;}
  var matrix=Matrix4x4.TRS(p,q,PhysicalHeldItems.Scale)*closedToRoot;var baseQ=q*baseRotation;
  if(h.Grip&&!wasGrip&&armed&&!MotionCarry.OwnsGrip(!left))
  {
   var turn=Quaternion.AngleAxis(chest.currentAngle,Vector3.right);var local=Quaternion.Inverse(turn)*matrix.inverse.MultiplyPoint3x4(hand);var contact=bounds.ClosestPoint(local);
   var world=matrix.MultiplyPoint3x4(turn*contact);
   if((world-hand).sqrMagnitude<=.12f*.12f&&new Vector2(local.y,local.z).sqrMagnitude>.12f*.12f)
   {grabbed=true;armed=false;angle=startAngle=chest.currentAngle;gripInLid=local;var v=matrix.inverse.MultiplyPoint3x4(hand);startVector=Mathf.Atan2(v.y,-v.z)*Mathf.Rad2Deg;handFromLid=Quaternion.Inverse(baseQ*turn)*rotation;DistanceGrips.Reset();Revision++;MotionHaptics.Pulse(!left);}
  }
  if(grabbed)
  {
   var v=matrix.inverse.MultiplyPoint3x4(hand);float measured=Mathf.Atan2(v.y,-v.z)*Mathf.Rad2Deg;
   angle=Mathf.Clamp(startAngle+Mathf.DeltaAngle(startVector,measured),Mathf.Min(0,chest.openAngleX),Mathf.Max(0,chest.openAngleX));
   chest.currentAngle=angle;bool open=angle>=chest.openAngleX*.5f;if(open!=chest.lastSentOpen)chest.RequestLidOpen(open);chest.ApplyLidAngle();
  }
  bool rt=MotionInput.PrimaryHeld;if(!grabbed&&rt!=trigger)chest.RequestLidOpen(rt);trigger=rt;wasGrip=h.Grip;
 }
 internal static bool HandPose(bool useLeft,out Vector3 p,out Quaternion q)
 {
  p=default;q=Quaternion.identity;if(!OwnsGrip(useLeft)||!Probe.Focused||!PhysicalHeldItems.TryRoot(out var root,out var rotation))return false;
  var turn=Quaternion.AngleAxis(angle,Vector3.right);p=(Matrix4x4.TRS(root,rotation,PhysicalHeldItems.Scale)*closedToRoot).MultiplyPoint3x4(turn*gripInLid);q=rotation*baseRotation*turn*handFromLid;return true;
 }
}
