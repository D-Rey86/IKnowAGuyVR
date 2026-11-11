using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in native game/ownership fixture. Never compiled into the installed build.
internal static class DistanceGripAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_DISTANCE_GRIP_AUDIT")=="1";
 private static int phase;private static float next=24;private static InteractionManager owner;
 private static GameObject fixture,near,blocker;private static HoldableBase holdable;private static Vector3 target,placement;
 private static Collider collider;private static int frames;private static float error;
 private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
 private static Vector3 Point(float side=0)
 {Check(StereoTrial.TryHeadWorld(out var p,out var q),"head unavailable");return p+q*(Vector3.forward*1.3f+Vector3.right*side-Vector3.up*.25f);}
 private static void Position(GameObject item,Vector3 center)
 {var c=item.GetComponent<Collider>();item.transform.position+=center-c.bounds.center;var body=item.GetComponent<Rigidbody>();if(body!=null)body.isKinematic=true;Physics.SyncTransforms();}
 private static GameObject Clone(GameObject source)
 {var item=UnityEngine.Object.Instantiate(source);item.GetComponent<Unity.Netcode.NetworkObject>().Spawn();return item;}
 internal static void Locked()
 {
  if(!enabled||owner==null||!ObjectGrabs.Grabbed||owner.heldItems.Count!=1)return;
  Check(MotionHeldItems.TryAnchor(owner,out var p,out _,out _),"held pose unavailable");
  frames++;error=Math.Max(error,(owner.heldItems[0].transform.position-p).magnitude);
 }
 internal static void Restored(float position,float rotation)
 {if(enabled)Check(position<.000001f&&rotation<.000001f,"held pose restoration failed");}
 internal static void Tick()
 {
  if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{float delay=.75f;switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("hands reset");break;
   case 1:
    PizzaBox source=null;foreach(var b in UnityEngine.Object.FindObjectsOfType<PizzaBox>())if(b.CanPickup()){source=b;break;}
    Check(source!=null,"native pizza-box fixture unavailable");fixture=Clone(source.gameObject);collider=fixture.GetComponent<Collider>();holdable=fixture.GetComponent<HoldableBase>();target=Point(-.2f);Position(fixture,target);
    Send(MotionPointer.AimAtWorld(target,true)+"\nhand r aimtrim 0 80");break;
   case 2:
    Check(!InteractionReticle.Visible,"idle dot visible");Check(StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var lp,out _)&&(collider.ClosestPoint(lp)-lp).magnitude>.3f,"distance fixture is physically reachable");
    Send("shot distance-idle\ngrip l 1");break;
   case 3:
    Check(owner.heldItems.Contains(fixture)&&ObjectGrabs.LeftHand&&holdable.isHeldNetwork.Value,"left native distance pickup failed");
    Plugin.Line("DISTANCE_LEFT_PICKUP_PASS");Send("stick l 0 .4");delay=1.5f;break;
   case 4:Send("stick l 0 0\nshot distance-left-held\nhand l aimtrim -80 0");break;
   case 5:Check(owner.currentPreviewObject==null||!owner.currentPreviewObject.activeInHierarchy,"unexpected placement preview in drop control");Send("grip l 0");break;
   case 6:
    Check(owner.heldItems.Count==0&&!holdable.isHeldNetwork.Value,"left release did not drop natively");
    target=Point(.2f);Position(fixture,target);Send("hands reset");break;
   case 7:Send(MotionPointer.AimAtWorld(target)+"\nhand l aimtrim 0 -80");break;
   case 8:Send("grip r 1");break;
   case 9:
    Check(owner.heldItems.Contains(fixture)&&!ObjectGrabs.LeftHand&&holdable.isHeldNetwork.Value,"right native distance pickup failed");Plugin.Line("DISTANCE_RIGHT_PICKUP_PASS");Send("focus lose");break;
   case 10:Check(owner.heldItems.Contains(fixture),"focus loss dropped item");Send("focus regain\ngrip r 0");break;
   case 11:Check(owner.heldItems.Contains(fixture),"neutral return dropped item");Send("grip r 1\nhand r aimtrim -80 0");break;
   case 12:Send("grip r 0");break;
   case 13:
    Check(owner.heldItems.Count==0,"rearm/release failed");Send("hands reset");target=Point();Position(fixture,target);
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var grip,out _),"left pose missing");
    blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="DistanceGrip_Occluder";blocker.layer=fixture.layer;blocker.transform.position=Vector3.Lerp(grip,target,.5f);blocker.transform.localScale=Vector3.one*.45f;Physics.SyncTransforms();
    Send(MotionPointer.AimAtWorld(target,true));break;
   case 14:Send("grip l 1");break;
   case 15:
    Plugin.Line("DISTANCE_OCCLUSION_CONTROL hit="+owner.rayHit.collider?.name+" selected="+owner.currentHoldable?.GetGameObject()?.name);
    Check(owner.heldItems.Count==0&&!holdable.isHeldNetwork.Value,"pickup went through obstacle");Plugin.Line("DISTANCE_OCCLUSION_PASS");Send("grip l 0");UnityEngine.Object.Destroy(blocker);break;
   case 16:
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var hand,out _),"left missing");near=Clone(fixture);Position(near,hand);
    target=Point();Position(fixture,target);Send(MotionPointer.AimAtWorld(target,true));break;
   case 17:Send("grip l 1");break;
   case 18:
    Check(owner.heldItems.Contains(near)&&!owner.heldItems.Contains(fixture)&&ObjectGrabs.LeftHand,"near target did not win");Plugin.Line("DISTANCE_NEAR_PRIORITY_PASS");
    BuildableCounter counter=null;Collider cc=null;float best=float.PositiveInfinity;
    foreach(var c in UnityEngine.Object.FindObjectsOfType<BuildableCounter>())
    {if(!c.AcceptsPlacedObjects)continue;var col=c.GetComponent<Collider>();if(col==null)col=c.GetComponentInChildren<Collider>();if(col==null)continue;float d=(col.bounds.center-owner.transform.position).sqrMagnitude;if(d<best){best=d;counter=c;cc=col;}}
    Check(counter!=null,"counter fixture missing");target=cc.bounds.center+Vector3.up*cc.bounds.extents.y;
    Check(StereoTrial.TryHeadWorld(out var head,out var facing),"head missing");var forward=Vector3.ProjectOnPlane(facing*Vector3.forward,Vector3.up).normalized;
    owner.movementController.Teleport(owner.movementController.transform.position+Vector3.ProjectOnPlane(target-head,Vector3.up)-forward*1.1f);break;
   case 19:Send(MotionPointer.AimAtWorld(target,true)+"\nhand r aimtrim 0 80");break;
   case 20:
    Check(owner.currentPreviewObject!=null&&owner.currentPreviewObject.activeInHierarchy&&owner.lastPlacementPointForPreview!=null,"left-hand green preview missing");placement=owner.currentPreviewObject.transform.position;Send("shot distance-left-placement\ngrip l 0");break;
   case 21:
    Check(owner.heldItems.Count==0&&!near.GetComponent<HoldableBase>().isHeldNetwork.Value,"left grip did not place");Check((near.transform.position-placement).magnitude<.01f,"left native placement did not match preview");Plugin.Line("DISTANCE_LEFT_PLACEMENT_PASS");
    Check(frames>60&&error<.00001f,"rigid attachment drift "+error);Check(!InteractionReticle.Visible,"dot persisted after placement");
    Plugin.Line("DISTANCE_GRIP_AUDIT_COMPLETE rigidFrames="+frames+" maxError="+error);UnityEngine.Object.Destroy(fixture);UnityEngine.Object.Destroy(near);phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+delay;}
  catch(Exception e){Plugin.Line("DISTANCE_GRIP_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
}
