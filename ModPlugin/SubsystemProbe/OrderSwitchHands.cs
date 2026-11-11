using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Physical access to the online-order manager's existing switch. Native network
// state, upgrade visibility and the ordinary distance-click action remain authoritative.
internal static class OrderSwitchHands
{
 private struct Geometry {internal Transform Transform;internal Bounds Bounds;}
 private static SwitchArm target;private static Transform lever,parent;private static Geometry[] geometry;
 private static bool held,left,startOn,wasLeft,wasRight,armedLeft,armedRight;
 private static Vector3 localContact,startVector;private static Quaternion localRotation;
 private static float progress,startProgress,span;
 internal static bool Active=>held;
 private static void Bind(SwitchArm next)
 {
  if(target==next&&target!=null&&lever!=null)return;
  Release(false);target=next;lever=parent=null;geometry=null;
  if(target==null||target.handleTransform==null)return;
  lever=target.handleTransform;parent=lever.parent;span=Mathf.DeltaAngle(target.offAngleX,target.onAngleX);
  var filters=lever.GetComponentsInChildren<MeshFilter>(true);geometry=new Geometry[filters.Length];
  for(int i=0;i<filters.Length;i++)if(filters[i].sharedMesh!=null)geometry[i]=new Geometry{Transform=filters[i].transform,Bounds=filters[i].sharedMesh.bounds};
 }
 internal static void Tick()
 {
  var l=ControllerTransport.Left;var r=ControllerTransport.Right;
  var manager=CourierOrderManager.Instance;Bind(manager!=null?manager.electricitySwitch:null);
  var owner=InteractionManager.LocalInstance;var hud=HUDManager.LocalInstance;
  bool live=MotionInput.Enabled&&StereoTrial.GameplayActive&&Probe.Focused&&(!KitchenHands.Acquiring||held)&&owner!=null&&owner.IsInteractionEnabled&&!owner.skatingSuppressed&&
   !MachineInteractionBase.IsLocalPlayerInMachineMode&&!MotionInput.PhotoMode&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive&&!ImmersiveHud.InputOwned&&
   Cursor.lockState==CursorLockMode.Locked&&(hud==null||!hud.IsEscapeMenuOpen)&&target!=null&&target.gameObject.activeInHierarchy&&lever!=null;
  if(!live){Release(false);armedLeft=armedRight=false;wasLeft=l.Grip;wasRight=r.Grip;return;}
  if(!l.Grip)armedLeft=true;if(!r.Grip)armedRight=true;
  if(held)
  {
   var hand=left?l:r;
   if(!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out _))Release(false);
   else if(target.IsOn!=startOn||target.isAnimating){held=false;}
   else if(!hand.Grip)Release(true);
   else
   {
    var vector=Vector3.ProjectOnPlane((parent==null?p:parent.InverseTransformPoint(p))-lever.localPosition,Vector3.right);
    if(vector.sqrMagnitude>.000001f)
    {
     progress=Mathf.Clamp01(startProgress+Vector3.SignedAngle(startVector,vector,Vector3.right)/span);
     lever.localRotation=Quaternion.Slerp(target.offRotation,target.onRotation,progress);
    }
   }
  }
  else if(target.CanInteract()&&!target.isAnimating&&Mathf.Abs(span)>.001f)
  {
   if(l.Grip&&!wasLeft&&armedLeft)TryGrab(true,l,owner);
   if(!held&&r.Grip&&!wasRight&&armedRight)TryGrab(false,r,owner);
  }
  wasLeft=l.Grip;wasRight=r.Grip;
 }
 private static void TryGrab(bool useLeft,ControllerTransport.Hand hand,InteractionManager owner)
 {
  if(!hand.Tracked||(owner.IsHoldingItem()&&ObjectGrabs.LeftHand==useLeft)||geometry==null||
   !StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))return;
  float best=.12f*.12f;Vector3 contact=default;bool found=false;
  // The native click collider is the fixed wall plate. Use the moving lever's
  // authored mesh bounds for proximity, without another collider or physics query.
  foreach(var g in geometry)
  {
   if(g.Transform==null||!g.Transform.gameObject.activeInHierarchy)continue;
   var closest=g.Transform.TransformPoint(g.Bounds.ClosestPoint(g.Transform.InverseTransformPoint(p)));
   float d=(closest-p).sqrMagnitude;if(d<best){found=true;best=d;contact=closest;}
  }
  if(!found)return;
  var vector=Vector3.ProjectOnPlane((parent==null?p:parent.InverseTransformPoint(p))-lever.localPosition,Vector3.right);
  if(vector.sqrMagnitude<.000001f)return;
  left=useLeft;held=true;startOn=target.IsOn;progress=startProgress=startOn?1:0;startVector=vector;
  localContact=lever.InverseTransformPoint(contact);localRotation=Quaternion.Inverse(lever.rotation)*q;
  MotionHaptics.Pulse(left);
 }
 private static void Release(bool commit)
 {
  if(!held)return;held=false;
  if(target==null||lever==null)return;
  // A remote state change/animation owns its own pose; never overwrite it.
  if(target.IsOn!=startOn||target.isAnimating)return;
  bool desired=progress>=.5f;
  if(commit&&desired!=target.IsOn&&target.gameObject.activeInHierarchy&&target.CanInteract())
  {target.OnInteract();MotionHaptics.Pulse(left,.16f,.025f);}
  else target.SnapHandleToState(target.IsOn);
 }
 internal static bool Pose(bool useLeft,out Vector3 position,out Quaternion rotation)
 {
  position=default;rotation=default;
  if(!held||left!=useLeft||lever==null)return false;
  position=lever.TransformPoint(localContact);rotation=lever.rotation*localRotation;return true;
 }
}
