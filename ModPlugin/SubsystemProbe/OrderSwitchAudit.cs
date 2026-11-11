using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class OrderSwitchAudit
{
 private static bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_ORDER_SWITCH")=="1";
 private static int phase;private static float next=25;private static SwitchArm target;private static Transform lever;private static Vector3 point,attachedLocal,originalPosition;private static Quaternion initialRotation;private static bool initialOn;
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 private static string Hand(bool left,Vector3 world)
 {StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var p,out var q);var v=Quaternion.Inverse(q)*(world-p);return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {v.x:R} {v.y:R} {-v.z:R} 0 0 0");}
 private static Vector3 Destination(float amount)
 {
  var local=lever.parent.InverseTransformPoint(point)-lever.localPosition;
  var end=initialOn?target.offRotation:target.onRotation;
  return lever.parent.TransformPoint(lever.localPosition+Quaternion.Slerp(Quaternion.identity,end*Quaternion.Inverse(initialRotation),amount)*local);
 }
 private static void Attached(bool left)
 {
  Check(OrderSwitchHands.Pose(left,out var p,out _),"Hand not attached");
  Check(Vector3.Distance(lever.InverseTransformPoint(p),attachedLocal)<.0001f,"Contact slipped on handle");
  Check(target.IsOn==initialOn,"State changed before grip release");
 }
 private static Vector3 GripPoint()
 {
  float best=-1;Vector3 result=default;
  foreach(var f in lever.GetComponentsInChildren<MeshFilter>())
  {
   var mesh=f.sharedMesh;Plugin.Line("SWITCH_MESH name="+f.name+" bounds="+mesh.bounds);
   // Shipping meshes discard CPU vertex arrays; their authored bounds remain available.
   var b=mesh.bounds;
   for(int i=0;i<4;i++)
   {var v=b.center+new Vector3(0,(i&1)==0?-b.extents.y:b.extents.y,(i&2)==0?-b.extents.z:b.extents.z);var world=f.transform.TransformPoint(v);var p=lever.parent.InverseTransformPoint(world)-lever.localPosition;float d=Vector3.ProjectOnPlane(p,Vector3.right).sqrMagnitude;if(d>best){best=d;result=world;}}
  }
  Check(best>0,"Missing switch geometry");return result;
 }
 internal static void Tick()
 {
  if(!enabled||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try
  {
   switch(phase)
   {
    case 0:
     target=CourierOrderManager.Instance.electricitySwitch;Check(target!=null&&target.CanInteract(),"Online switch unavailable");lever=target.handleTransform;
     InventoryManager.LocalInstance.SetSelectedSlot(7);Check(!InteractionManager.LocalInstance.IsHoldingItem(),"Fixture hand occupied");
     originalPosition=target.transform.position;point=GripPoint();var camera=InteractionManager.LocalInstance.interactionCamera;target.transform.position+=camera.transform.position+camera.transform.forward*.65f-point;Physics.SyncTransforms();
     Send("input clear\nhands reset");break;
    case 1:point=GripPoint();initialOn=target.IsOn;initialRotation=lever.localRotation;Send(Hand(false,point));break;
    case 2:Send("grip r 1");break;
    case 3:Check(OrderSwitchHands.Pose(false,out var p,out _),"Right grab failed");attachedLocal=lever.InverseTransformPoint(p);Send(Hand(false,Destination(.35f)));break;
    case 4:Attached(false);Check(Quaternion.Angle(lever.localRotation,initialRotation)>30,"Lever did not follow intermediate motion");MotionBody.RendererSnapshot();Send("shot switch-right-mid\n"+Hand(false,Destination(1)));break;
    case 5:Attached(false);Check(Quaternion.Angle(lever.localRotation,initialOn?target.offRotation:target.onRotation)<.1f,"Right handle did not reach authored stop");Send("grip r 0");break;
    case 6:Check(target.IsOn!=initialOn&&CourierOrderManager.Instance.IsElectricityOn()==target.IsOn,"Native online state did not follow release");Check(!OrderSwitchHands.Active,"Right release latched");Plugin.Line("ORDER_SWITCH_RIGHT_PASS on="+target.IsOn);Send("hands reset");break;
    case 7:point=GripPoint();initialOn=target.IsOn;initialRotation=lever.localRotation;Send(Hand(true,point));break;
    case 8:Send("grip l 1");break;
    case 9:Check(OrderSwitchHands.Pose(true,out var l,out _),"Left grab failed");attachedLocal=lever.InverseTransformPoint(l);Send(Hand(true,Destination(1)));break;
    case 10:Attached(true);Check(Quaternion.Angle(lever.localRotation,initialOn?target.offRotation:target.onRotation)<.1f,"Left handle did not reach authored stop");Send("shot switch-left\ngrip l 0");break;
    case 11:Check(target.IsOn!=initialOn&&CourierOrderManager.Instance.IsElectricityOn()==target.IsOn,"Left native state mismatch");Check(!OrderSwitchHands.Active,"Left release latched");Plugin.Line("ORDER_SWITCH_LEFT_PASS on="+target.IsOn);Send("hands reset");break;
    case 12:point=GripPoint();initialOn=target.IsOn;initialRotation=lever.localRotation;Send(Hand(false,point));break;
    case 13:Send("grip r 1");break;
    case 14:Check(OrderSwitchHands.Active,"Cancel grab failed");Send(Hand(false,Destination(.2f)));break;
    case 15:Send("grip r 0");break;
    case 16:Check(target.IsOn==initialOn&&Quaternion.Angle(lever.localRotation,initialRotation)<.1f,"Short release changed state");Send("grip r 1");break;
    case 17:Check(OrderSwitchHands.Active,"Focus-loss grab failed");Send(Hand(false,Destination(.8f)));break;
    case 18:Send("focus lose");break;
    case 19:Check(!OrderSwitchHands.Active&&target.IsOn==initialOn&&Quaternion.Angle(lever.localRotation,initialRotation)<.1f,"Focus loss did not cancel");Send("focus regain");break;
    case 20:Check(!OrderSwitchHands.Active,"Focus regain reacquired held grip");Send("input clear\nhands reset");Plugin.Line("ORDER_SWITCH_CANCEL_PASS");break;
    case 21:Send(MotionPointer.AimAtWorld(target.GetComponent<Collider>().bounds.center));break;
    case 22:Send("grip r 1");break;
    case 23:Check(target.IsOn!=initialOn&&!OrderSwitchHands.Active,"Distance grip toggle regressed");Send("grip r 0");Plugin.Line("ORDER_SWITCH_DISTANCE_PASS");break;
    case 24:Plugin.Line("ORDER_SWITCH_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();target.transform.position=originalPosition;Physics.SyncTransforms();Environment.SetEnvironmentVariable("IKAG_XR_HANDLE_ASSET_AUDIT","1");enabled=false;return;
   }
   phase++;next=Time.realtimeSinceStartup+.65f;
  }
  catch(Exception e){Plugin.Line("ORDER_SWITCH_FAILED phase="+phase+" "+e);Send("input clear\nfocus regain");enabled=false;}
 }
}
