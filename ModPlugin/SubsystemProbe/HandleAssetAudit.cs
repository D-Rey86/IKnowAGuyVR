using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class HandleAssetAudit
{
 private static float peak;private static bool releaseWait;private static int phase,index=Environment.GetEnvironmentVariable("IKAG_XR_OVEN_L2_GATE")=="1"?3:Environment.GetEnvironmentVariable("IKAG_XR_LEGACY_HANDLE_AUDIT")=="1"?5:0;private static float next=24;private static bool left;
 private static MachineInteractionBase machine;private static MonoBehaviour handle;private static GameObject clone;
 private static PressHandle press;private static SlicerHandle slicer;private static SauceHandle sauce;private static OvenLevel2Handle oven;private static OvenHandle legacy;
 private static bool Remote=>Environment.GetEnvironmentVariable("IKAG_XR_HANDLE_REMOTE_AUDIT")=="1";
 private static Vector3 contact,destination,cameraPosition;private static Quaternion cameraRotation;private static Transform cameraParent;private static Camera camera;
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static string Hand(Vector3 world){StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var z,out var q);var p=Quaternion.Inverse(q)*(world-z);return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");}
 private static void Enable(){if(press!=null)press.isEnabled=true;else if(slicer!=null)slicer.isEnabled=true;else if(sauce!=null)sauce.isEnabled=true;else if(oven!=null)oven.isEnabled=true;else if(legacy!=null)legacy.isEnabled=true;}
 internal static void Tick()
 {
  if(Environment.GetEnvironmentVariable("IKAG_XR_HANDLE_ASSET_AUDIT")!="1"||!StereoTrial.GameplayActive||phase==99)return;
  // Fixture permits manipulating an empty machine; normal machine input still
  // requires the native item/recipe preconditions before it enables a handle.
  Enable();
  if(phase==4){float progress=press!=null?press.GetProgress():slicer!=null?slicer.GetProgress():sauce!=null?sauce.GetProgress():oven!=null?oven.GetProgress():legacy.GetProgress();peak=Mathf.Max(peak,progress);}
  if(Time.realtimeSinceStartup<next)return;
  try{float delay=.5f;switch(phase){
   case 0:
    UiInputAudit.Dump("Assembly-CSharp.dll","","MachineInteractionBase","EnterMachineModeLocal","ExitMachineModeLocal","OnInteract","RequestEnterMachineRpc","EnterMachineClientRpc","Update","CanInteract");
    UiInputAudit.Dump("Assembly-CSharp.dll","","DoughPressMachine","OnEnterMachineMode","OnExitMachineMode","OnInteract","Update");
    UiInputAudit.Dump("Assembly-CSharp.dll","","DoughPressMachineL2","OnEnterMachineMode","OnExitMachineMode");
    MonoBehaviour source=(index==0||index==4)?Resources.FindObjectsOfTypeAll<PressHandle>()[index==4?1:0]:index==1?Resources.FindObjectsOfTypeAll<SlicerHandle>()[0]:index==2?Resources.FindObjectsOfTypeAll<SauceHandle>()[0]:index==3?Resources.FindObjectsOfTypeAll<OvenLevel2Handle>()[0]:Resources.FindObjectsOfTypeAll<OvenHandle>()[0];
    var root=source.GetComponentInParent<MachineInteractionBase>(true);Check(root!=null,"prefab owner missing "+source.name);
    Plugin.Line("HANDLE_PREFAB "+index+" "+root.name);
    clone=UnityEngine.Object.Instantiate(root.gameObject);clone.transform.position=InteractionManager.LocalInstance.transform.position+Vector3.right*3;clone.SetActive(true);
    var net=clone.GetComponent<NetworkObject>();if(net!=null&&!net.IsSpawned)net.Spawn();
    machine=clone.GetComponent<MachineInteractionBase>();
    press=(index==0||index==4)?clone.GetComponentInChildren<PressHandle>(true):null;slicer=index==1?clone.GetComponentInChildren<SlicerHandle>(true):null;sauce=index==2?clone.GetComponentInChildren<SauceHandle>(true):null;oven=index==3?clone.GetComponentInChildren<OvenLevel2Handle>(true):null;legacy=index==5?clone.GetComponentInChildren<OvenHandle>(true):null;
    handle=press!=null?press:slicer!=null?slicer:sauce!=null?sauce:oven!=null?oven:legacy;Check(handle!=null,"cloned handle missing");camera=InteractionManager.LocalInstance.interactionCamera;cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;cameraParent=camera.transform.parent;Plugin.Line("FIXTURE_BOUNDS_BEFORE "+handle.GetComponentInChildren<Collider>().bounds.center);if(Remote)clone.transform.rotation=Quaternion.FromToRotation(Vector3.ProjectOnPlane(machine.CameraViewPoint.forward,Vector3.up),Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up))*clone.transform.rotation;Physics.SyncTransforms();Plugin.Line("FIXTURE_BOUNDS_AFTER "+handle.GetComponentInChildren<Collider>().bounds.center);clone.transform.position+=cameraPosition+camera.transform.forward*(Remote?1.5f:.65f)-Vector3.up*.25f-handle.GetComponentInChildren<Collider>().bounds.center;delay=2;break;
   case 1:
    if(oven!=null&&Environment.GetEnvironmentVariable("IKAG_XR_OVEN_L2_GATE")=="1"){oven.SetProgressExternal(0);Plugin.Line("OVEN_L2_NETWORK_REENTRY external="+oven.hasExternalProgress+" left="+left);}
    Plugin.Line("CAMERA_BEFORE_GRIP displacement="+(camera.transform.position-cameraPosition));cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;
    var c=handle.GetComponentInChildren<Collider>();Check(c!=null,"collider missing");contact=c.bounds.center;var t=handle.transform;var local=t.parent.InverseTransformPoint(contact);
    if(press!=null)destination=contact+t.parent.TransformVector(Vector3.up*(press.bottomLocalY-press.currentY));
    else if(slicer!=null)destination=contact+t.parent.TransformVector(Vector3.up*(slicer.bottomLocalY-slicer.currentY));
    else if(oven!=null)destination=contact+t.parent.TransformVector(Vector3.right*(oven.closedLocalX-oven.currentX));
    else if(legacy!=null){var axis=legacy.slideAxis==NeedleRotationAxis.X?Vector3.right:legacy.slideAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;if(legacy.handleMotion==HandleMotion.Rotate){axis=legacy.rotationAxis==NeedleRotationAxis.X?Vector3.right:legacy.rotationAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;destination=t.parent.TransformPoint(t.localPosition+Quaternion.AngleAxis(legacy.maxLocalRotationY-legacy.currentLocalRotationY,axis)*(local-t.localPosition));}else destination=contact+t.parent.TransformVector(axis*(legacy.slideClosedValue-legacy.slideOpenValue)*(1-legacy.GetProgress()));}
    else destination=t.parent.TransformPoint(t.localPosition+Quaternion.AngleAxis(sauce.endRotationX-sauce.currentRotationX,Vector3.right)*(local-t.localPosition));
    if(legacy!=null)Plugin.Line("LEGACY_BEFORE motion="+legacy.handleMotion+" progress="+legacy.GetProgress()+" current="+legacy.currentLocalRotationY+" open="+legacy.slideOpenValue+" closed="+legacy.slideClosedValue+" axis="+legacy.slideAxis+" destination="+destination+" contact="+contact);
    Send(Remote?MotionPointer.AimAtWorld(contact,left):Hand(contact));break;
   case 2:var im=InteractionManager.LocalInstance;Plugin.Line("RETICLE_HIT collider="+im.rayHit.collider?.name+" hasHit="+im.hasHit+" distance="+im.rayHit.distance+" point="+im.rayHit.point+" desired="+contact);Send(left?"grip l 1":"grip r 1");break;
   case 3:Plugin.Line("CAMERA_AFTER_GRIP displacement="+(camera.transform.position-cameraPosition));Plugin.Line("DIRECT_STATE mode="+MachineInteractionBase.IsLocalPlayerInMachineMode+" owner="+MachineInteractionBase.CurrentActiveMachine?.name+" cursor="+UnityEngine.Cursor.lockState+" focus="+Probe.Focused+" grip="+ControllerTransport.Right.Grip);Check(camera.transform.parent==cameraParent&&(camera.transform.position-cameraPosition).sqrMagnitude<.0001f&&Quaternion.Angle(camera.transform.rotation,cameraRotation)<.1f,"camera moved on grab");Check(Remote?KitchenHands.DistancePoint(out _):KitchenHands.Pose(left,out _,out _),"asset grab failed "+index);peak=0;if(Remote&&KitchenHands.DistancePoint(out var actual)){if(sauce!=null||(legacy!=null&&legacy.handleMotion==HandleMotion.Rotate)){var rt=handle.transform;var rl=rt.parent.InverseTransformPoint(actual);destination=rt.parent.TransformPoint(rt.localPosition+Quaternion.AngleAxis(sauce!=null?sauce.endRotationX-sauce.currentRotationX:legacy.maxLocalRotationY-legacy.currentLocalRotationY,sauce!=null?Vector3.right:legacy.rotationAxis==NeedleRotationAxis.X?Vector3.right:legacy.rotationAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward)*(rl-rt.localPosition));}else destination+=actual-contact;}Send(Remote?MotionPointer.AimAtWorld(destination,left):Hand(destination));delay=2;break;
   case 4:
    if(legacy!=null)Plugin.Line("LEGACY_AFTER motion="+legacy.handleMotion+" progress="+legacy.GetProgress()+" target="+legacy.targetLocalRotationY+" current="+legacy.currentLocalRotationY+" external="+legacy.hasExternalProgress+" enabled="+legacy.isEnabled+" busy="+legacy.isResetting);
    if(Remote){var dot=HUDManager.LocalInstance.crosshairDot;Check(HUDManager.LocalInstance.crosshairElement.resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.Flex,"remote reticle hidden");for(var el=dot;el!=null;el=el.parent)Plugin.Line("DOT_ANCESTOR "+el.name+" display="+el.resolvedStyle.display+" visibility="+el.resolvedStyle.visibility+" opacity="+el.resolvedStyle.opacity);Plugin.Line("REMOTE_DOT display="+dot.resolvedStyle.display+" visibility="+dot.resolvedStyle.visibility+" opacity="+dot.resolvedStyle.opacity);Check(!KitchenHands.Pose(false,out _,out _)&&!KitchenHands.Pose(true,out _,out _),"distance grab stretched hand");Check(KitchenHands.RemoteLeft==left&&InteractionReticle.Visible,"wrong hand or missing contextual dot");}MotionBody.RendererSnapshot();if(!left&&Environment.GetEnvironmentVariable("IKAG_XR_RENDERDOC")=="1")RenderDocCapture.Trigger();
    float progress=press!=null?press.GetProgress():slicer!=null?slicer.GetProgress():sauce!=null?sauce.GetProgress():oven!=null?oven.GetProgress():legacy.GetProgress();
    Plugin.Line("HANDLE_STATE type="+index+" peak="+peak+" progress="+progress+" pressAuto="+press?.isAutoCompleting+" triggered="+press?.hasTriggeredPress+" target="+press?.targetY+" current="+press?.currentY+" bottom="+press?.bottomLocalY);
    Check(peak>.99f,"native travel incomplete "+index+" peak="+peak+" progress="+progress);
    Vector3 p;Check(Remote?KitchenHands.DistancePoint(out p):KitchenHands.Pose(left,out p,out _),"hand detached");Plugin.Line("HANDLE_ASSET_PASS type="+index+" left="+left+" progress="+progress+" attached="+p);
    Send("shot handle-"+index+"-"+left);releaseWait=false;break;
   case 5:
    if(!releaseWait){Send(left?"grip l 0":"grip r 0");releaseWait=true;next=Time.realtimeSinceStartup+.5f;return;}
    Check(!KitchenHands.Pose(left,out _,out _)&&!KitchenHands.DistancePoint(out _),"hand did not release");
    Check(!MachineInteractionBase.IsLocalPlayerInMachineMode,"native ownership not released");if(!left&&(!Remote||Environment.GetEnvironmentVariable("IKAG_XR_HANDLE_BILATERAL_AUDIT")=="1")){left=true;if(press!=null)press.ResetHandle();else if(slicer!=null)slicer.ResetHandle();else if(sauce!=null)sauce.ResetHandle();else if(oven!=null)oven.ResetHandle();else legacy.ResetHandle();phase=1;next=Time.realtimeSinceStartup+1;return;}
    Check(!MachineInteractionBase.IsLocalPlayerInMachineMode,"native ownership not released");delay=2;break;
   case 6:UnityEngine.Object.Destroy(clone);machine=null;handle=null;press=null;slicer=null;sauce=null;oven=null;legacy=null;left=false;++index;if(index<(Environment.GetEnvironmentVariable("IKAG_XR_OVEN_L2_GATE")=="1"?4:Remote?6:5)){phase=0;next=Time.realtimeSinceStartup+1;return;}if(Remote&&Environment.GetEnvironmentVariable("IKAG_XR_LEGACY_HANDLE_AUDIT")!="1"){Plugin.Line("REMOTE_HANDLES_COMPLETE");Environment.SetEnvironmentVariable("IKAG_XR_HANDLE_REMOTE_AUDIT","0");index=0;phase=0;next=Time.realtimeSinceStartup+1;return;}Plugin.Line("HANDLE_ASSET_AUDIT_COMPLETE");phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+delay;
  }catch(Exception e){Plugin.Line("HANDLE_ASSET_FAILED phase="+phase+" type="+index+" "+e);Send("grip l 0\ngrip r 0");phase=99;}
 }
}
