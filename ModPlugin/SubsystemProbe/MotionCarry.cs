using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionCarry
{
 private static bool support,wasGrip,armed,left;private static Vector3 point,primaryPoint;
 private static Quaternion primaryFromRoot,otherFromRoot;
 internal static int Revision{get;private set;}
 internal static bool Supporting=>support&&PhysicalHeldItems.Selected&&PhysicalHeldItems.Carryable;
 internal static bool OwnsGrip(bool useLeft)=>Supporting&&useLeft!=left;
 internal static void Reset(){Release();wasGrip=armed=false;}
 private static void Release(){if(support){support=false;Revision++;}}
 internal static Vector3 BoxSurface(Bounds bounds,Vector3 p,Vector3 scale)
 {
  var q=bounds.ClosestPoint(p);if(!bounds.Contains(p))return q;
  int axis=0;float nearest=float.PositiveInfinity;bool high=false;
  for(int i=0;i<3;i++){float lo=(p[i]-bounds.min[i])*Mathf.Abs(scale[i]),hi=(bounds.max[i]-p[i])*Mathf.Abs(scale[i]);if(lo<nearest){nearest=lo;axis=i;high=false;}if(hi<nearest){nearest=hi;axis=i;high=true;}}
  q[axis]=high?bounds.max[axis]:bounds.min[axis];return q;
 }
 internal static void Input()
 {
  var h=PhysicalHeldItems.Other;
  if(!PhysicalHeldItems.Carryable||!NaturalItemGrip.Supports(PhysicalHeldItems.Item)||!PhysicalHeldItems.Ready||MotionChest.OwnsGrip(!PhysicalHeldItems.Left))
  {Release();armed=false;wasGrip=h.Grip;return;}
  if(left!=PhysicalHeldItems.Left){Release();left=PhysicalHeldItems.Left;armed=false;}
  if(!h.Grip){armed=true;Release();}
  if(h.Grip&&!wasGrip&&armed&&!support&&NaturalItemGrip.SemanticReady&&NaturalItemGrip.FlatHand&&PhysicalHeldItems.TryRoot(out var p,out var q)&&StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var hand,out var rotation)&&MotionBody.TryCarryFrame(!left,out var surface,out _,out _,out _,out _))
  {
   var scale=PhysicalHeldItems.Scale;var palm=hand+rotation*surface;if(!PhysicalHeldItems.TryCarryPoint(palm,p,q,out var contact)){wasGrip=h.Grip;return;}var world=p+q*Vector3.Scale(contact,scale);var primary=NaturalItemGrip.ContactPoint;
   if((world-palm).sqrMagnitude<=.12f*.12f&&Vector3.Scale(contact-primary,scale).sqrMagnitude>.16f*.16f&&StereoTrial.TryTrackingPose(PhysicalHeldItems.Hand.Position,PhysicalHeldItems.Hand.Rotation,out _,out var primaryRotation))
   {point=contact;primaryPoint=primary;primaryFromRoot=Quaternion.Inverse(q)*primaryRotation;otherFromRoot=Quaternion.Inverse(q)*rotation;support=true;armed=false;DistanceGrips.Reset();Revision++;MotionHaptics.Pulse(!left);}
  }
  wasGrip=h.Grip;
 }
 internal static void Rotate(GameObject item,Vector3 primaryPalm,ref Quaternion root)
 {
  if(!Supporting||item!=PhysicalHeldItems.Item)return;var h=PhysicalHeldItems.Other;
  if(!h.Grip||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q)||!MotionBody.TryCarryFrame(!left,out var surface,out _,out _,out _,out _))return;
  var desired=p+q*surface-primaryPalm;if(desired.sqrMagnitude<.15f*.15f)return;
  root=Quaternion.FromToRotation(root*Vector3.Scale(point-primaryPoint,PhysicalHeldItems.Scale),desired)*root;
 }
 internal static bool HandPose(bool useLeft,out Vector3 p,out Quaternion q)
 {
  p=default;q=Quaternion.identity;if(!Supporting||!Probe.Focused||!PhysicalHeldItems.TryRoot(out var root,out var rotation)||!MotionBody.TryCarryFrame(useLeft,out var surface,out _,out _,out _,out _))return false;
  q=rotation*(useLeft==left?primaryFromRoot:otherFromRoot);p=root+rotation*Vector3.Scale(useLeft==left?primaryPoint:point,PhysicalHeldItems.Scale)-q*surface;return true;
 }
}
