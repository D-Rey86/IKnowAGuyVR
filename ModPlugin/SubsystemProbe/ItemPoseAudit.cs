using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ItemPoseAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_ITEM_POSES")=="1";
 private static HoldableBase[] items;private static int index,phase,frames,fits;private static float next=25;private static Animator animator;private static readonly Quaternion[] before=new Quaternion[30];private static bool pending;
 private static readonly List<int> inventorySlots=new();private static int inventoryIndex;
 private static Collider[] checkedColliders;private static bool[] checkedEnabled;
 private static void SaveColliders(GameObject item){var all=item.GetComponentsInChildren<Collider>(true);checkedColliders=new Collider[all.Length];checkedEnabled=new bool[all.Length];for(int i=0;i<all.Length;i++){checkedColliders[i]=all[i];checkedEnabled[i]=all[i].enabled;}}
 private static void CheckColliders(){for(int i=0;i<checkedColliders.Length;i++)if(checkedColliders[i]!=null&&checkedColliders[i].enabled!=checkedEnabled[i])throw new Exception("Collider enable state leaked: "+checkedColliders[i].name);}
 private static long applyAt,allocatedAt;private static int fitsAt;private static double maxFitMs;private static long steadyBytes;private static int steadyFrames;
 internal static void ApplyStart(){if(!enabled)return;fitsAt=fits;allocatedAt=GC.GetAllocatedBytesForCurrentThread();applyAt=System.Diagnostics.Stopwatch.GetTimestamp();}
 internal static void ApplyEnd(){if(!enabled)return;double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-applyAt)*1000d/System.Diagnostics.Stopwatch.Frequency;if(fits!=fitsAt)maxFitMs=Math.Max(maxFitMs,ms);else{steadyBytes+=GC.GetAllocatedBytesForCurrentThread()-allocatedAt;steadyFrames++;}}
 private static readonly HashSet<string> types=new();private static readonly HashSet<string> profiles=new();
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 internal static void Before(Animator a){if(!enabled)return;animator=a;for(int i=0;i<30;i++){var b=a.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal+i));if(b!=null)before[i]=b.localRotation;}pending=true;}
 internal static void Restored(){if(!pending)return;pending=false;for(int i=0;i<30;i++){var b=animator.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal+i));if(b!=null&&1-Mathf.Abs(Quaternion.Dot(b.localRotation,before[i]))>.000001f)throw new Exception("Finger restoration failed "+i);}frames++;}
 internal static void Fitted(GameObject item,bool left,Quaternion[] pose,Quaternion[] open,Quaternion[] closed,Transform[] bones)
 {
  if(!enabled)return;fits++;string signature="";
  for(int f=0;f<5;f++){int at=(left?0:15)+f*3;signature+=Mathf.RoundToInt(Quaternion.Angle(open[at],pose[at])*100)+",";}
  profiles.Add(signature);Plugin.Line("ITEM_FIT name="+item.name+" left="+left+" profile="+signature);
 }
 internal static void Unsupported(Collider c){if(!enabled)return;var mesh=c.TryCast<MeshCollider>();Plugin.Line("ITEM_UNSUPPORTED name="+c.name+" convex="+mesh?.convex+" readable="+mesh?.sharedMesh?.isReadable);}
 internal static void Tick()
 {
  if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  var owner=InteractionManager.LocalInstance;
  try{
  if(items==null){var inventory=InventoryManager.LocalInstance;for(int i=0;i<inventory.totalSlots;i++)if(inventory.GetItemInSlot(i)!=null)inventorySlots.Add(i);inventory.SetSelectedSlot(7);var list=new List<HoldableBase>();foreach(var h in UnityEngine.Object.FindObjectsOfType<HoldableBase>())if(h.CanPickup()&&types.Add(h.GetIl2CppType().Name))list.Add(h);items=list.ToArray();Send("hands reset\nhand l aimtrim 0 80\nhand r aimtrim 0 80\ngrip l 1\ngrip r 1");next=Time.realtimeSinceStartup+1;return;}
  if(index>=items.Length){
   if(inventoryIndex<inventorySlots.Count){if(phase==0){InventoryManager.LocalInstance.SetSelectedSlot(inventorySlots[inventoryIndex]);phase=1;}else{Plugin.Line("ITEM_INVENTORY name="+owner.cachedTopItem?.name);Send("shot item-inventory-"+inventoryIndex);InventoryManager.LocalInstance.SetSelectedSlot(7);inventoryIndex++;phase=0;}next=Time.realtimeSinceStartup+1;return;}
   if(profiles.Count<3)throw new Exception("Item fits did not produce distinct poses");Plugin.Line("ITEM_POSE_ACCEPTANCE_COMPLETE types="+items.Length+" profiles="+profiles.Count+" fits="+fits+" restoredFrames="+frames+" maxFitMs="+maxFitMs+" steadyBytes="+steadyBytes+" steadyFrames="+steadyFrames);StereoTrial.DiagnosticMetrics();Send("input clear\nhands reset");phase=99;return;}
  var item=items[index];bool left=index%2==1;
  if(phase==0){Send("hand l aimtrim 0 0\nhand r aimtrim 0 0");if(!item.CanPickup()||!owner.TryPickupItem(item.Cast<IHoldable>())){index++;return;}phase=1;}
  else if(phase==1){SaveColliders(item.gameObject);ObjectGrabs.AdoptDistance(owner,item.gameObject,left);phase=2;}
  else if(phase==2){CheckColliders();Plugin.Line("ITEM_POSE_CHECK type="+item.GetIl2CppType().Name+" left="+ObjectGrabs.LeftHand+" fits="+fits);Send("shot item-pose-"+index+"\nhead rot 15 -10 0\nstick l 0 .3");phase=3;}
  else {CheckColliders();Send("stick l 0 0\nhead rot 0 0 0");owner.DropLastItem();index++;phase=0;}
  next=Time.realtimeSinceStartup+.8f;
  }catch(Exception e){Plugin.Line("ITEM_POSE_FAILED "+e);Send("input clear");phase=99;}
 }
}

