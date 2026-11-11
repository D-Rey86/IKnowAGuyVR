using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class CarryCatalogueAudit
{
 internal static readonly bool Enabled=Environment.GetEnvironmentVariable("IKAG_XR_CARRY_CATALOGUE")=="1";
 private static readonly List<HoldableBase> sources=new();private static readonly List<object> results=new();private static readonly HashSet<string> captures=new();
 private static InteractionManager owner;private static GameObject item;private static int index,phase,passed,notFlat,rejected,unsupported,tiny;private static float next=25;
 private static Vector3 origin;private static bool movement;private static Collider[] colliders;private static bool[] flags;
 private static double maxFit,totalFit;private static long fitBytes,fitAllocated;private static int fitCount,restores;private static float maxP,maxQ,maxS,maxContact;
 private static Transform[] bones;private static Vector3[] positions,scales;private static Quaternion[] rotations;private static bool pending,rendered;
 internal static long BeginFit(){if(!Enabled)return 0;fitAllocated=GC.GetAllocatedBytesForCurrentThread();return System.Diagnostics.Stopwatch.GetTimestamp();}
 internal static void EndFit(long start){if(start==0)return;double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;maxFit=Math.Max(maxFit,ms);totalFit+=ms;fitBytes+=GC.GetAllocatedBytesForCurrentThread()-fitAllocated;fitCount++;}
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void FinishItem(string status)
 {
  results.Add(new{name=sources[index].name,type=sources[index].GetIl2CppType().Name,status});
  if(owner.IsHoldingItem())owner.ForceDropItem();if(item!=null){var net=item.GetComponent<NetworkObject>();if(net.IsSpawned)net.Despawn();else UnityEngine.Object.Destroy(item);}item=null;index++;phase=1;rendered=false;Send("input clear\nhands reset");next=Time.realtimeSinceStartup+.25f;
 }
 private static void Fail(Exception e){Plugin.Line("CARRY_CATALOGUE_FAILED index="+index+" phase="+phase+" name="+item?.name+" "+e);phase=99;Send("input clear\nhands reset");}
 internal static void Before(Animator animator)
 {
  if(!Enabled||phase!=5||rendered||!MotionCarry.Supporting)return;
  if(bones==null){var all=animator.GetComponentsInChildren<Transform>(true);bones=new Transform[all.Length];positions=new Vector3[all.Length];scales=new Vector3[all.Length];rotations=new Quaternion[all.Length];for(int i=0;i<all.Length;i++)bones[i]=all[i];}
  for(int i=0;i<bones.Length;i++){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;scales[i]=bones[i].localScale;}pending=true;
 }
 internal static void Rendered()
 {
  if(!Enabled||!pending)return;
  try{Check(MotionCarry.Supporting,"Support vanished during drawing");foreach(bool left in new[]{false,true}){Check(MotionCarry.HandPose(left,out var p,out var q),"No carry hand");MotionBody.TryCarryFrame(left,out var surface,out _,out _,out _,out _);float error=Vector3.Distance(p+q*surface,MotionBody.RenderedContact(left,true,false));maxContact=Math.Max(maxContact,error);Check(error<.001f,"Carry hand drifted");}}catch(Exception e){Fail(e);}
 }
 internal static void Restored()
 {
  if(!pending)return;pending=false;rendered=true;
  try{for(int i=0;i<bones.Length;i++){maxP=Math.Max(maxP,Vector3.Distance(positions[i],bones[i].localPosition));maxQ=Math.Max(maxQ,1-Mathf.Abs(Quaternion.Dot(rotations[i],bones[i].localRotation)));maxS=Math.Max(maxS,Vector3.Distance(scales[i],bones[i].localScale));}restores++;Check(maxP<1e-6&&maxQ<1e-6&&maxS<1e-6,"Avatar restoration leaked");}catch(Exception e){Fail(e);}
 }
 internal static void Tick()
 {
  if(!Enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{float delay=.3f;switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);origin=owner.movementController.transform.position;movement=owner.movementController.enabled;owner.movementController.Teleport(origin+Vector3.up*20);owner.movementController.enabled=false;
    var seen=new HashSet<string>();foreach(var h in Resources.FindObjectsOfTypeAll<HoldableBase>())if(!h.gameObject.scene.IsValid()&&seen.Add(h.GetIl2CppType().Name+"/"+h.name))sources.Add(h);
    Send("input clear\nhands reset\nhead rot 0 -25 0\nhead pos 0 1.6 .8");Plugin.Line("CARRY_CATALOGUE_BEGIN sources="+sources.Count);break;
   case 1:
    if(index>=sources.Count)
    {
     owner.movementController.Teleport(origin);owner.movementController.enabled=movement;
     var result=new{passed,notFlat,rejected,unsupported,tiny,fitCount,maxFitMs=maxFit,meanFitMs=fitCount==0?0:totalFit/fitCount,fitBytes,restores,maxPosition=maxP,maxRotation=maxQ,maxScale=maxS,maxContact,items=results};
     File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"carry-catalogue.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Check(passed>=10&&restores>=passed,"Catalogue coverage incomplete");Plugin.Line("CARRY_CATALOGUE_COMPLETE passed="+passed+" nativeReject="+rejected+" shapeUnsupported="+unsupported+" small="+tiny+" maxFitMs="+maxFit);StereoTrial.DiagnosticMetrics();phase=99;Send("input clear\nhands reset");return;
    }
    item=UnityEngine.Object.Instantiate(sources[index].gameObject);item.transform.position=owner.transform.position+Vector3.up*3;item.SetActive(true);item.GetComponent<NetworkObject>().Spawn();
    var hold=item.GetComponent<HoldableBase>();if(!hold.CanPickup()||!owner.TryPickupItem(hold.Cast<IHoldable>())){rejected++;FinishItem("native-rejected");return;}
    Send("grip r 1\ngrip l 0");break;
   case 2:
    if(!owner.heldItems.Contains(item)){rejected++;FinishItem("native-consumed-or-replaced");return;}
    ObjectGrabs.AdoptDistance(owner,item,false);Check(PhysicalHeldItems.TryRoot(out _,out _),"Missing natural profile");
    if(!NaturalItemGrip.Supports(item)){notFlat++;FinishItem("one-hand-profile");return;}
    if(!PhysicalHeldItems.Carryable){unsupported++;FinishItem("unsupported-physical-shape");return;}
    colliders=item.GetComponentsInChildren<Collider>(true);flags=new bool[colliders.Length];for(int i=0;i<flags.Length;i++)flags[i]=colliders[i].enabled;
    Send("hand r grip pose .22 1.1 -.65 0 0 90");break;
   case 3:
    Check(PhysicalHeldItems.TryRoot(out var p,out var q),"Missing root for contact");var b=PhysicalHeldItems.Bounds;var scale=PhysicalHeldItems.Scale;var center=p+q*Vector3.Scale(b.center,scale);var main=p+q*Vector3.Scale(NaturalItemGrip.ContactPoint,scale);float best=0;Vector3 selected=default,axis=default;
    foreach(var direction in new[]{Vector3.left,Vector3.right,Vector3.back,Vector3.forward,Vector3.up})
    {
     var outside=center+q*direction*(Vector3.Scale(b.extents,scale).magnitude+.2f);if(!PhysicalHeldItems.TryCarryPoint(outside,p,q,out var local))continue;var point=p+q*Vector3.Scale(local,scale);float span=(point-main).sqrMagnitude;
     if(span>best){best=span;selected=point;axis=q*direction;}
    }
    if(best<.17f*.17f){tiny++;FinishItem("too-small-for-second-contact");return;}
    StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out _,out var primaryQ);MotionBody.TryCarryFrame(true,out var surface,out _,out var normal,out _,out _);var otherQ=Quaternion.FromToRotation(primaryQ*normal,-axis)*primaryQ;
    Send(PhysicalItemAudit.Pose(true,selected-otherQ*surface,otherQ));break;
   case 4:Send("grip l 1");break;
   case 5:
    Check(MotionCarry.Supporting&&MotionCarry.OwnsGrip(true)&&!ObjectGrabs.LeftHand&&!DistanceGrips.Pending&&!KitchenHands.Acquiring,"Secondary grip conflict");
    for(int i=0;i<flags.Length;i++)Check(colliders[i].enabled==flags[i],"Collider enable flag leaked");
    Check(!HandPhysics.ContactEnabled(true)&&!HandPhysics.ContactEnabled(false),"Attached palm proxy active");
    StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var lp,out var lq);Send(PhysicalItemAudit.Pose(true,lp+Vector3.up*.1f,lq));break;
   case 6:
    Check(rendered,"No rendered/restored carry sample");if(captures.Add(sources[index].GetIl2CppType().Name)){Send("shot carry-catalogue-"+index);delay=.7f;}break;
   case 7:Send("grip l 0");break;
   case 8:Check(!MotionCarry.Supporting&&owner.heldItems.Contains(item),"Support release dropped item");Send("grip r 0");break;
   case 9:Check(!owner.heldItems.Contains(item)&&!MotionCarry.Supporting,"Primary release did not drop");passed++;Plugin.Line("CARRY_CATALOGUE_PASS index="+index+" name="+sources[index].name+" type="+sources[index].GetIl2CppType().Name);FinishItem("pass");return;
  }phase++;next=Time.realtimeSinceStartup+delay;}catch(Exception e){Fail(e);}
 }
}
