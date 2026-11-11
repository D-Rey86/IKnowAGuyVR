using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class NaturalCatalogueAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_NATURAL_CATALOGUE")=="1";
 private static readonly List<MonoBehaviour> sources=new();private static readonly HashSet<string> types=new();
 private static int index,stage,passed,skipped;private static float next=25;private static GameObject clone;private static IHoldable hold;private static InteractionManager owner;private static bool left;
 private static string dir=>Environment.GetEnvironmentVariable("XRSIM_DIR");
 private static int measured=-1;private static bool measuredLeft;
 internal static void Rendered()
 {
  if(!enabled||stage!=3||clone==null||(measured==index&&measuredLeft==left))return;measured=index;measuredLeft=left;
  Plugin.Line("CATALOGUE_CONTACT index="+index+" left="+left+" error="+Vector3.Distance(NaturalItemGrip.RenderedContact(left),MotionBody.RenderedContact(left,NaturalItemGrip.FlatHand,NaturalItemGrip.PinchingHand)));
 }
 private static void Send(string s)=>File.WriteAllText(Path.Combine(dir,"command.txt"),s+"\n");
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 internal static void Tick()
 {
  if(!enabled||stage==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{
   if(stage==0)
   {
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);var seen=new HashSet<string>();
    UiInputAudit.Dump("Assembly-CSharp.dll","","PaintingItem","CanPickup","CanInteract","OnInteract","OnPickupForReplacement");
    var filter=Environment.GetEnvironmentVariable("IKAG_XR_CATALOGUE_FILTER");
    foreach(var h in Resources.FindObjectsOfTypeAll<MonoBehaviour>())if(!h.gameObject.scene.IsValid()&&h.TryCast<IHoldable>()!=null&&seen.Add(h.GetIl2CppType().Name+"/"+h.name)&&(filter==null||Array.IndexOf(filter.Split(','),h.GetIl2CppType().Name)>=0))sources.Add(h);
    Plugin.Line("CATALOGUE_BEGIN count="+sources.Count);Send("input clear\nhands reset\nhand r aimtrim 0 80\ngrip r 1\ngrip l 1");stage=1;
   }
   else if(stage==1)
   {
    if(index>=sources.Count){Plugin.Line("CATALOGUE_COMPLETE passed="+passed+" skipped="+skipped);StereoTrial.DiagnosticMetrics();Send("input clear\nhands reset");stage=99;return;}
    var source=sources[index];clone=UnityEngine.Object.Instantiate(source.gameObject);clone.transform.position=owner.transform.position+Vector3.up*3;
    clone.SetActive(true);var net=clone.GetComponent<NetworkObject>();if(net==null)throw new Exception("Prefab has no root NetworkObject");if(!net.IsSpawned)net.Spawn();
    var painting=clone.GetComponent<PaintingItem>();if(painting!=null){painting.Unlock();painting.isPlacedOnWall.Value=false;}
    if(Environment.GetEnvironmentVariable("IKAG_XR_CATALOGUE_INSPECT")=="1")foreach(var t in clone.GetComponentsInChildren<Transform>(true)){var mf=t.GetComponent<MeshFilter>();var c=t.GetComponent<Collider>();Plugin.Line("CARRIER_NODE "+t.name+" p="+clone.transform.InverseTransformPoint(t.position).ToString("R")+" q="+(Quaternion.Inverse(clone.transform.rotation)*t.rotation).ToString("R")+" scale="+t.lossyScale.ToString("R")+" mesh="+mf?.sharedMesh?.name+" meshBounds="+mf?.sharedMesh?.bounds+" collider="+c?.GetIl2CppType().Name+" bounds="+c?.bounds);}
    var roach=clone.GetComponent<RoachPickup>();if(roach!=null)roach.SetRevealed(true);
    var pizza=clone.GetComponent<Pizza>();if(pizza!=null){var toppings=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();toppings.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));pizza.RestoreFromSave(DoughState.CookedDough,100,true,CookingQuality.Perfect,toppings);}
    foreach(var c in clone.GetComponents<MonoBehaviour>()){hold=c.TryCast<IHoldable>();if(hold!=null)break;}
    Check(hold!=null,"No holdable component");
    var rb=clone.GetComponent<Rigidbody>();if(rb!=null)rb.isKinematic=true;
    Send("hand r aimtrim 0 0\nhand l aimtrim 0 0\nhand r grip pose .22 1.25 -.55 0 0 0\nhand l grip pose -.22 1.25 -.55 0 0 0\nhead rot 0 -15 0");left=false;stage=2;
   }
   else if(stage==2)
   {
    bool picked=owner.TryPickupItem(hold);if(!picked)Plugin.Line("CATALOGUE_PICKUP_REJECT name="+sources[index].name+" count="+owner.heldItems.Count+" max="+owner.GetEffectiveMaxStackSize()+" canPickup="+hold.CanPickup()+" object="+hold.GetGameObject()?.name);
    Check(picked,"Native pickup rejected after initialization");
    stage=20;next=Time.realtimeSinceStartup+.4f;return;
   }
   else if(stage==20)
   {
    ObjectGrabs.AdoptDistance(owner,clone,false);
    Check(NaturalItemGrip.TryPose(owner,out _,out _,out _),"No natural contact profile/palm measurement");
    if(NaturalItemGrip.FlatHand)Send("hand r grip pose .22 1.25 -.55 0 0 90");
    stage=3;
   }
   else if(stage==3)
   {
    Check(owner.heldItems.Contains(clone),"Held item disappeared");
    if(NaturalItemGrip.FlatHand){var h=left?ControllerTransport.Left:ControllerTransport.Right;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var rot);MotionBody.TryCarryFrame(left,out _,out _,out var normal,out _,out _);Plugin.Line("CATALOGUE_PALM_UP index="+index+" dot="+Vector3.Dot(rot*normal,Vector3.up));}
    Plugin.Line("CATALOGUE_ITEM index="+index+" name="+sources[index].name+" type="+sources[index].GetIl2CppType().Name+" left="+left+" flat="+NaturalItemGrip.FlatHand);
    Send("shot catalogue-"+index+"-"+(left?"left":"right"));
    if(Environment.GetEnvironmentVariable("IKAG_XR_CATALOGUE_INSPECT")=="1"){stage=99;return;}
    if(!left&&types.Add(sources[index].GetIl2CppType().Name)){stage=4;}else stage=5;
   }
   else if(stage==4)
   {
    ObjectGrabs.AdoptDistance(owner,clone,true);left=true;
    if(NaturalItemGrip.FlatHand)Send("hand l grip pose -.22 1.25 -.55 0 0 -90");stage=3;
   }
   else if(stage==5){passed++;Cleanup();index++;stage=1;}
   next=Time.realtimeSinceStartup+.4f;
  }catch(Exception e){Plugin.Line("CATALOGUE_SKIP index="+index+" name="+(index<sources.Count?sources[index].name:"-")+" stage="+stage+" "+e);skipped++;Cleanup();index++;stage=1;next=Time.realtimeSinceStartup+.4f;}
 }
 private static void Cleanup()
 {
  if(owner!=null&&owner.IsHoldingItem())owner.ForceDropItem();
  if(clone!=null){var net=clone.GetComponent<NetworkObject>();if(net!=null&&net.IsSpawned)net.Despawn();else UnityEngine.Object.Destroy(clone);}clone=null;hold=null;
 }
}
