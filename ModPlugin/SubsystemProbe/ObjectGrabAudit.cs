using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in baseline observation and later acceptance fixtures; excluded from release.
internal static class ObjectGrabAudit
{
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_OBJECT_GRAB_AUDIT");
 private static int phase;private static float next=24;private static InteractionManager owner;
 private static readonly List<object> samples=new();private static int sampled=-1;
 private static GameObject fixture;private static HoldableBase holdable;private static int leftPulses,rightPulses,lockedFrames;private static float lockError;private static Vector3 leftWorld;
 internal static bool Complete{get;private set;}
 internal static void Restored(float p,float q){if(mode=="acceptance"&&(p>.000001f||q>.000001f))throw new Exception("held root restoration failed "+p+"/"+q);}
 private static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
 private static float[] Q(Quaternion p)=>new[]{p.x,p.y,p.z,p.w};
 internal static void Haptic(bool left,float amplitude,float duration,bool sent){if(mode!=null){Plugin.Line("GRAB_HAPTIC left="+left+" amplitude="+amplitude+" duration="+duration+" sent="+sent);if(sent){if(left)leftPulses++;else rightPulses++;}}}
 private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
 internal static void BeforeRender()
 {
  if(mode!="discovery"||owner==null||phase<2||phase>5||sampled==Time.frameCount)return;sampled=Time.frameCount;
  var item=owner.cachedTopItem;if(item==null)return;
  var hand=ControllerTransport.Right;if(!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))return;
  var rb=item.GetComponent<Rigidbody>();
  samples.Add(new{phase,frame=Time.frameCount,position=V(item.transform.position),grip=V(p),relative=V(Quaternion.Inverse(q)*(item.transform.position-p)),rotation=Q(item.transform.rotation),anchor=V(owner.holdPosition.position),velocity=V(rb==null?Vector3.zero:rb.linearVelocity),kinematic=rb!=null&&rb.isKinematic,interpolation=rb?.interpolation.ToString(),camera=V(owner.interactionCamera.transform.position)});
 }
 internal static void Locked()
 {
  if(mode=="acceptance"&&owner!=null&&owner.cachedTopItem!=null&&MotionHeldItems.TryAnchor(owner,out var expected,out _,out _)){lockedFrames++;lockError=Math.Max(lockError,(owner.cachedTopItem.transform.position-expected).magnitude);return;}
  if(mode!="discovery"||owner==null||phase<2||phase>5)return;
  var item=owner.cachedTopItem;var hand=ControllerTransport.Right;
  if(item!=null&&StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))samples.Add(new{locked=true,phase,frame=Time.frameCount,relative=V(Quaternion.Inverse(q)*(item.transform.position-p))});
 }
 internal static void Tick()
 {
  if(mode==null||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  if(mode=="acceptance"){Acceptance();return;}
  try {float delay=1;
   switch(phase)
   {
    case 0:
     owner=InteractionManager.LocalInstance;
     UiInputAudit.Dump("Assembly-CSharp.dll","","InteractionManager","FixedUpdate","LateUpdate","PickupItem","TryPickupItem","TryPickupItemToEmptySlot","DropLastItem","AddItemToHeldList","DetectObjectsInSphere","UpdateCachedItem","UpdateItemCache","GetHoldingConfiguration");
     UiInputAudit.Dump("Assembly-CSharp.dll","","HoldableBase","OnPickup","OnDrop","CanPickup","RequestPickup","RequestDrop","Awake","OnEnable","OnDisable","OnNetworkSpawn","OnNetworkDespawn");
     UiInputAudit.Dump("UnityEngine.PhysicsModule.dll","UnityEngine","Rigidbody","AddForce","set_linearVelocity","get_linearVelocity","MovePosition","set_position","get_position");
     var klass=IL2CPP.GetIl2CppClass("Assembly-CSharp.dll","","InteractionManager");IntPtr it=IntPtr.Zero;
     for(IntPtr f;(f=IL2CPP.il2cpp_class_get_fields(klass,ref it))!=IntPtr.Zero;)Plugin.Line("GRAB_FIELD "+System.Runtime.InteropServices.Marshal.PtrToStringAnsi(IL2CPP.il2cpp_field_get_name(f))+" offset="+IL2CPP.il2cpp_field_get_offset(f));
     var inventory=InventoryManager.LocalInstance;for(int i=0;i<inventory.totalSlots;i++){var obj=inventory.GetItemInSlot(i);Plugin.Line("GRAB_SLOT "+i+" item="+obj?.name);}
     inventory.SetSelectedSlot(1);Send("hands reset");break;
    case 1:if(owner.cachedTopItem==null)throw new Exception("Selected baseline fixture missing");Plugin.Line("GRAB_BASELINE_ITEM "+owner.cachedTopItem.name+" held="+owner.heldItems.Count+" position="+owner.cachedTopItem.transform.position+" target="+owner.targetHoldingPosition+" rotation="+owner.targetHoldingRotation);break;
    case 2:Send("stick l 0 .5");delay=2;break;
    case 3:Send("stick l 0 0");delay=1;break;
    case 4:Send("head rot 20 -10 0\nstick l 0 -.5");delay=2;break;
    case 5:Send("stick l 0 0\nhead rot 0 0 0\nshot grab-baseline-held");break;
    case 6:File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"held-baseline.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{IncludeFields=true}));Plugin.Line("OBJECT_GRAB_DISCOVERY_COMPLETE samples="+samples.Count);phase=99;return;
   }
   ++phase;next=Time.realtimeSinceStartup+delay;
  }catch(Exception e){Plugin.Line("OBJECT_GRAB_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static string Hand(bool left,Vector3 world)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;
  Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q),"no hand pose");
  var local=h.Position+Quaternion.Inverse(q*Quaternion.Inverse(h.Rotation))*(world-p);
  return FormattableString.Invariant($"hand {(left?"l":"r")} grip pos {local.x:R} {local.y:R} {-local.z:R}");
 }
 private static void Acceptance()
 {
  try{float delay=.7f;switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("hands reset");
    var hp=IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.InputDevices::SendHapticImpulse");var bytes=new byte[32];System.Runtime.InteropServices.Marshal.Copy(hp,bytes,0,bytes.Length);Plugin.Line("HAPTIC_GENERIC_BYTES "+Convert.ToHexString(bytes));
    break;
   case 1:
    Check(!owner.IsHoldingItem(),"empty native slot needed");Check(ObjectGrabs.Registered>0,"registry empty");
    HoldableBase source=null;foreach(var box in UnityEngine.Object.FindObjectsOfType<PizzaBox>())if(box.CanPickup()&&!box.isHeldNetwork.Value){source=box;break;}
    Check(source!=null,"no native loose pizza box fixture");fixture=UnityEngine.Object.Instantiate(source.gameObject);holdable=fixture.GetComponent<HoldableBase>();
    fixture.GetComponent<Unity.Netcode.NetworkObject>().Spawn();
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out leftWorld,out _),"no left pose");
    var c=fixture.GetComponent<Collider>();fixture.transform.position+=leftWorld-c.bounds.center;var rb=fixture.GetComponent<Rigidbody>();rb.isKinematic=true;Physics.SyncTransforms();
    Plugin.Line("GRAB_FIXTURE "+fixture.name+" can="+holdable.CanPickup()+" collider="+c.GetType().Name+" enabled="+c.enabled+" registered="+ObjectGrabs.Registered);Send("grip l 1");break;
   case 2:
    ObjectGrabs.Trace(fixture);MotionInput.Trace();
    Check(owner.cachedTopItem!=null&&owner.cachedTopItem.Pointer==fixture.Pointer&&ObjectGrabs.LeftHand,"left grab failed");
    Plugin.Line("GRAB_LEFT_PASS heldNetwork="+holdable.isHeldNetwork.Value+" owner="+holdable.OwnerClientId+" local="+owner.OwnerClientId);Check(holdable.isHeldNetwork.Value,"pickup skipped native network ownership");Send("stick l 0 .5");delay=2;break;
   case 3:Check(lockedFrames>30&&lockError<.0001f,"held visual drift "+lockError);Check(StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out leftWorld,out _),"left pose gone");Send("stick l 0 0\nshot grab-left-walking\n"+Hand(false,leftWorld));break;
   case 4:Send("grip r 1\ngrip l 0");break;
   case 5:Check(ObjectGrabs.Grabbed&&!ObjectGrabs.LeftHand&&owner.heldItems.Count==1,"right transfer failed");Send("grip l 0");break;
   case 6:Check(owner.heldItems.Count==1,"old grip release dropped transfer");Send("shot grab-right-transfer\ngrip r 0");break;
   case 7:
    Check(!owner.IsHoldingItem()&&!ObjectGrabs.Grabbed,"grip release did not drop");Check(!holdable.isHeldNetwork.Value,"native ownership not released");Check(leftPulses>0&&rightPulses>0,"no successful bilateral haptics");
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var rightWorld,out _),"right pose missing");
    var collider=fixture.GetComponent<Collider>();Check(collider.enabled,"native drop left collider disabled");fixture.transform.position+=rightWorld-collider.bounds.center;var body=fixture.GetComponent<Rigidbody>();body.isKinematic=false;body.linearVelocity=Vector3.zero;Physics.SyncTransforms();Send("grip r 1");break;
   case 8:Check(ObjectGrabs.Grabbed&&!ObjectGrabs.LeftHand&&owner.heldItems.Count==1&&holdable.isHeldNetwork.Value,"right loose-object pickup failed");Send("focus lose");break;
   case 9:Check(owner.heldItems.Count==1,"focus loss dropped item");Send("focus regain");break;
   case 10:Send("hand r valid off");break;
   case 11:Check(owner.heldItems.Count==1,"tracking loss dropped item");Send("grip r 0\nhand r valid on");break;
   case 12:Check(owner.heldItems.Count==1,"neutral on tracking return dropped item");Send("grip r 1");break;
   case 13:Send("grip r 0");break;
   case 14:Check(owner.heldItems.Count==0,"explicit regrip/release failed");InventoryManager.LocalInstance.SetSelectedSlot(1);Send("hands reset");break;
   case 15:Check(owner.cachedTopItem!=null&&owner.cachedTopItem.GetComponent<PhotoCamera>()!=null,"camera slot missing");Check(StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var currentRight,out _),"right pose missing");Send(Hand(true,currentRight));break;
   case 16:Send("grip l 1");break;
   case 17:Check(ObjectGrabs.LeftHand,"selected camera left acquisition failed");Send("hand l grip rot 45 10 0\ntrigger l pull 100");break;
   case 18:Check(MotionInput.PhotoMode,"camera mode regressed");Send("trigger r pull 100\nshot grab-camera-left");delay=2;break;
   case 19:Check(ObjectGrabs.LeftHand&&owner.heldItems.Count==1,"camera use lost grip");Send("trigger l pull 100");break;
   case 20:InventoryManager.LocalInstance.SetSelectedSlot(7);Send("grip l 0\nhands reset");break;
   case 21:
    Check(owner.heldItems.Count==0&&!ObjectGrabs.Grabbed,"inventory switch retained stale grab");
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var rp,out _),"right pose lost");var col=fixture.GetComponent<Collider>();fixture.transform.position+=rp-col.bounds.center;holdable.disableColliderWhenHeld=false;holdable.isHeldNetwork.Value=true;col.enabled=true;var rigid=fixture.GetComponent<Rigidbody>();rigid.isKinematic=true;Physics.SyncTransforms();Check(!holdable.CanPickup(),"native blocked-pickup control not active");Send("grip r 1");break;
   case 22:Check(fixture.GetComponent<Collider>().enabled,"blocked-pickup geometry control disabled");Check(owner.heldItems.Count==0&&!ObjectGrabs.Grabbed,"native pickup denial bypassed");holdable.isHeldNetwork.Value=false;fixture.GetComponent<Rigidbody>().isKinematic=true;Send("grip r 0");break;
   case 23:Check(holdable.CanPickup(),"native pickup control did not recover");Send("grip r 1");break;
   case 24:Check(ObjectGrabs.Grabbed&&owner.heldItems.Count==1,"ordinary re-pickup failed");Send("btn y press 100");break;
   case 25:Check(owner.heldItems.Count==0&&!ObjectGrabs.Grabbed,"Y drop regressed");Send("grip r 0");Plugin.Line("OBJECT_GRAB_ACCEPTANCE_COMPLETE left/right pickups and transfer, native pickup/drop/denial, simultaneous hand-off, focus/tracking suspension, camera use, inventory switch, Y drop, rigid walking pose frames="+lockedFrames+" maxError="+lockError+" haptics="+leftPulses+"/"+rightPulses);UnityEngine.Object.Destroy(fixture);Complete=true;phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+delay;}
  catch(Exception e){Plugin.Line("OBJECT_GRAB_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
}
