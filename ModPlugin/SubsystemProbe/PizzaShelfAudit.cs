using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Diagnostic-only native ownership/render census and acceptance fixture.
internal static class PizzaShelfAudit
{
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_PIZZA_SHELF_AUDIT");
 private static int phase;private static float next=24;private static InteractionManager owner;private static Pizza pizza;private static CargoBox box;private static CargoBoxShelfSlot slot;
 private static Renderer[] renderers;private static bool[] gpuFlags,originalGpuFlags;private static int before=-1,after=-1;
 internal static bool PerformanceReady{get;private set;}
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
 private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
 internal static void Attachment(bool posed)
 {
  if(mode==null||mode=="performance"||pizza==null||phase<2||phase>5||renderers==null||(posed?after:before)==phase)return;
  if(posed)after=phase;else before=phase;
  var root=pizza.transform;Plugin.Line("PIZZA_ROOT phase="+phase+" posed="+posed+" position="+root.position+" rotation="+root.rotation);
  foreach(var r in renderers)
  {
   if(r==null||!r.gameObject.activeInHierarchy)continue;var skin=r.TryCast<SkinnedMeshRenderer>();var bone=skin?.rootBone;
   Plugin.Line("PIZZA_RENDERER phase="+phase+" posed="+posed+" node="+r.name+" parent="+r.transform.parent?.name+" position="+r.transform.position+" bounds="+r.bounds.center+" skin="+(skin!=null)+" force="+skin?.forceMatrixRecalculationPerRender+" bone="+bone?.name+" bonePosition="+bone?.position+" boneChild="+(bone!=null&&bone.IsChildOf(root))+" static="+r.isPartOfStaticBatch+" shader="+r.sharedMaterial?.shader?.name+" matrix="+r.localToWorldMatrix.ToString("R")+" transform="+r.transform.localToWorldMatrix.ToString("R"));
  }
 }
 internal static void Tick()
 {
  if(mode==null||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  if(mode=="release"){ReleaseTick();return;}
  try{float delay=1;switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
    if(mode!="performance"){
    UiInputAudit.Dump("Assembly-CSharp.dll","","CargoBoxShelfSlot","OnInteract","CanInteract","Initialize");
    UiInputAudit.Dump("Assembly-CSharp.dll","","CargoBoxShelf","PlaceCargoBoxInSlot","PlaceBoxOnServer","SnapBoxToSlot","IsSlotOccupied");
    UiInputAudit.Dump("Assembly-CSharp.dll","","Pizza","LateUpdate","OnPickup","ActivateIngredientVisualLocal","AddSpecificIngredientWithPlacement","BakeNonShadyIngredientsCombined","StartFollowing");
    }
    Send("hands reset");break;
   case 1:
    foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()&&(pizza==null||p.GetNetworkIngredientsCount()>pizza.GetNetworkIngredientsCount()))pizza=p;
    Check(pizza!=null,"native pizza fixture unavailable");Plugin.Line("PIZZA_NATIVE name="+pizza.name+" ingredients="+pizza.GetNetworkIngredientsCount()+" baked="+pizza.ingredientsBaked+" station="+pizza.currentStation?.name);
    if(pizza.GetNetworkIngredientsCount()==0||mode=="render-acceptance")
    {
     var data=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();data.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));data.Add(new NetworkIngredientData(IngredientType.Pepperoni,1));data.Add(new NetworkIngredientData(IngredientType.Olives,2));data.Add(new NetworkIngredientData(IngredientType.Steak,0));
     pizza.RestoreFromSave(DoughState.SaucedDough,100,true,pizza.cookingQuality,data);
    }
    var all=pizza.GetComponentsInChildren<Renderer>(true);renderers=new Renderer[all.Length];originalGpuFlags=new bool[all.Length];for(int i=0;i<all.Length;i++){renderers[i]=all[i];originalGpuFlags[i]=all[i].allowGPUDrivenRendering;}Plugin.Line("PIZZA_RENDERER_COUNT "+renderers.Length);
    Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"native pizza pickup failed");break;
   case 2:
    Check(StereoTrial.TryHeadWorld(out var head,out var facing),"head pose missing");
    var raw=ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(raw.Position,raw.Rotation,out var grip,out var world),"hand pose missing");
    var target=head+facing*Vector3.forward*.8f-facing*Vector3.up*.16f;var local=raw.Position+Quaternion.Inverse(world*Quaternion.Inverse(raw.Rotation))*(target-grip);
    Send(FormattableString.Invariant($"hand r grip pos {local.x:R} {local.y:R} {-local.z:R}"));
    Check(StereoTrial.TryTrackingPose(raw.AimPosition,raw.AimRotation,out _,out var aim),"aim pose missing");owner.targetHoldingRotation=Quaternion.Inverse(aim)*Quaternion.LookRotation(facing*Vector3.up,-(facing*Vector3.forward));break;
   case 3:if(mode=="performance"){PerformanceReady=true;phase=99;Plugin.Line("PIZZA_PERFORMANCE_FIXTURE_READY");return;}Send("shot pizza-held-baseline");break;
   case 4:
    if(mode=="gpu-control")
    {gpuFlags=new bool[renderers.Length];for(int i=0;i<renderers.Length;i++){gpuFlags[i]=renderers[i].allowGPUDrivenRendering;renderers[i].allowGPUDrivenRendering=false;}Plugin.Line("PIZZA_GPU_CONTROL_EXCLUDED");}
    else if(mode=="render-acceptance"){Send("stick l 0 .5");delay=2;}
    else Send("hand r aimtrim -10 15");
    if(Environment.GetEnvironmentVariable("IKAG_XR_RENDERDOC")=="1")RenderDocCapture.Trigger();break;
   case 5:Send("stick l 0 0\nshot pizza-toppings-moved");break;
   case 6:if(gpuFlags!=null)for(int i=0;i<renderers.Length;i++)renderers[i].allowGPUDrivenRendering=gpuFlags[i];owner.DropLastItem();break;
   case 7:
    if(mode=="render-acceptance"){for(int i=0;i<renderers.Length;i++)Check(renderers[i].allowGPUDrivenRendering==originalGpuFlags[i],"held GPU flag not restored: "+renderers[i].name);Plugin.Line("PIZZA_RENDERER_RESTORATION_PASS");}
    foreach(var b in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(b.CanPickup()&&!b.isEmpty&&!b.isMachineBox&&b.assignedShelf!=null){box=b;break;}
    Check(box!=null,"no eligible native shelf cargo box");var parent=box.assignedShelf;int previousIndex=box.assignedShelfSlotIndex;
    foreach(var s in parent.slots)if(s.slotIndex==previousIndex){slot=s;break;}
    Check(slot!=null&&owner.TryPickupItem(box.Cast<IHoldable>()),"native box pickup failed");break;
   case 8:
    foreach(var s in UnityEngine.Object.FindObjectsOfType<CargoBoxShelfSlot>())
    {
     if(s.parentShelf==null)continue;Plugin.Line("SHELF_SLOT name="+s.name+" index="+s.slotIndex+" occupied="+s.parentShelf.IsSlotOccupied(s.slotIndex)+" enabled="+s.slotCollider?.enabled+" bounds="+s.slotCollider?.bounds+" anchor="+s.parentShelf.GetSlotTransform(s.slotIndex)?.position);
    }
    Check(!slot.parentShelf.IsSlotOccupied(slot.slotIndex),"pickup did not clear native shelf slot");
    Plugin.Line("SHELF_CONTROL can="+slot.CanInteract()+" held="+owner.heldItems.Count+" box="+box.name+" root="+box.transform.position+" collider="+box.col?.bounds+" slot="+slot.slotIndex);
    slot.parentShelf.PlaceCargoBoxInSlot(slot.slotIndex,box,owner);break;
   case 9:
    Check(slot.parentShelf.IsSlotOccupied(slot.slotIndex)&&box.isOnShelfSlot&&owner.heldItems.Count==0,"native shelf placement control failed");
    Plugin.Line("NATIVE_SHELF_CONTROL_PASS index="+slot.slotIndex+" position="+box.transform.position+" anchor="+slot.parentShelf.GetSlotTransform(slot.slotIndex).position+" assigned="+box.assignedShelfSlotIndex+" stored="+box.isStoredNetwork.Value+" scale="+box.transform.lossyScale);
    Plugin.Line("PIZZA_SHELF_DISCOVERY_COMPLETE");phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+delay;}
  catch(Exception e){Plugin.Line("PIZZA_SHELF_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
 private static Vector3 slotCenter;private static CargoBox rejected;private static PizzaStationBase station;private static Collider stationCollider;
 private static string Hand(bool left,Vector3 world)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q),"no hand pose");
  var local=h.Position+Quaternion.Inverse(q*Quaternion.Inverse(h.Rotation))*(world-p);
  return FormattableString.Invariant($"hand {(left?"l":"r")} grip pos {local.x:R} {local.y:R} {-local.z:R}");
 }
 private static string CenterAt(bool left,GameObject item,Vector3 destination)
 {
  Check(MotionHeldItems.TryAnchor(owner,out var p,out var q,out var relative),"no item pose");
  var c=item.GetComponent<CargoBox>().col.Cast<BoxCollider>();var root=item.transform;
  var center=root.InverseTransformPoint(c.transform.TransformPoint(c.center));var current=p+(q*relative)*Vector3.Scale(root.lossyScale,center);
  var h=left?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var grip,out _),"grip missing");
  return Hand(left,grip+destination-current);
 }
 private static void ReleaseTick()
 {
  try{float delay=.8f;switch(phase){
   case 0:owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("hands reset");break;
   case 1:
    foreach(var b in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(b.CanPickup()&&!b.isEmpty&&!b.isMachineBox&&b.assignedShelf!=null){box=b;break;}
    Check(box!=null,"no shelf box fixture");foreach(var s in box.assignedShelf.slots)if(s.slotIndex==box.assignedShelfSlotIndex){slot=s;break;}
    Check(slot!=null&&owner.TryPickupItem(box.Cast<IHoldable>()),"box pickup failed");break;
   case 2:slotCenter=slot.slotCollider.bounds.center;Send("grip r 1");break;
   case 3:Check(ObjectGrabs.Grabbed&&!ObjectGrabs.LeftHand,"right grip did not latch");Send(CenterAt(false,box.gameObject,slotCenter)+"\nhand r aimtrim 0 80");break;
   case 4:Check(slot.CanInteract(),"native shelf validator rejected fixture");Plugin.Line("RELEASE_SHELF_BEFORE preview="+owner.currentPreviewObject?.name+" center="+slotCenter);Send("grip r 0");break;
   case 5:
    Check(owner.heldItems.Count==0&&box.isOnShelfSlot&&slot.parentShelf.IsSlotOccupied(slot.slotIndex),"right release failed native snap");
    Check((box.transform.position-slot.parentShelf.GetSlotTransform(slot.slotIndex).position).magnitude<.001f,"slot root mismatch");Plugin.Line("RELEASE_SHELF_RIGHT_PASS");
    Check(owner.TryPickupItem(box.Cast<IHoldable>()),"second box pickup failed");Send("hands reset");break;
   case 6:Check(StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var right,out _),"right pose missing");Send(Hand(true,right));break;
   case 7:Send("grip l 1");break;
   case 8:Check(ObjectGrabs.LeftHand,"left grip did not latch");Send(CenterAt(true,box.gameObject,slotCenter)+"\nhand r aimtrim 0 80");break;
   case 9:Send("grip l 0");break;
   case 10:
    Check(owner.heldItems.Count==0&&box.isOnShelfSlot&&slot.parentShelf.IsSlotOccupied(slot.slotIndex),"left release failed native snap");Plugin.Line("RELEASE_SHELF_LEFT_PASS");
    foreach(var b in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(b.Pointer!=box.Pointer&&b.CanPickup()&&!b.isEmpty&&!b.isMachineBox){rejected=b;break;}
    Check(rejected!=null&&owner.TryPickupItem(rejected.Cast<IHoldable>()),"occupied-slot fixture missing");Send("hands reset");break;
   case 11:Send("grip r 1");break;
   case 12:Check(ObjectGrabs.Grabbed,"rejected fixture grip missing");Send(CenterAt(false,rejected.gameObject,slotCenter)+"\nhand r aimtrim 0 80");break;
   case 13:Check(!slot.CanInteract(),"occupied-slot negative control invalid");Check(owner.currentPreviewObject==null||!owner.currentPreviewObject.activeInHierarchy,"unexpected generic preview in negative fixture");Send("grip r 0");break;
   case 14:
    Check(owner.heldItems.Count==0&&!rejected.isOnShelfSlot&&box.isOnShelfSlot,"occupied slot overwritten or ordinary drop lost");Plugin.Line("RELEASE_OCCUPIED_DROP_PASS");
    foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()&&p.currentStation!=null){pizza=p;station=p.currentStation;break;}
    Check(pizza!=null&&owner.TryPickupItem(pizza.Cast<IHoldable>()),"pizza fixture unavailable");
    Check(station!=null,"empty pizza station missing");stationCollider=station.GetComponent<Collider>();if(stationCollider==null)stationCollider=station.GetComponentInChildren<Collider>();Check(stationCollider!=null,"station collider missing");Send("hands reset");if(Environment.GetEnvironmentVariable("IKAG_XR_RELEASE_LEFT_AUDIT")=="1"){phase=50;next=Time.realtimeSinceStartup+delay;return;}break;
   case 15:Send("grip r 1");break;
   case 16:
    Check(ObjectGrabs.Grabbed,"pizza grip did not latch");Check(station.currentPizza==null,"pickup did not free original station");Check(StereoTrial.TryHeadWorld(out var head,out var facing),"no head pose");var point=stationCollider.bounds.center;var forward=Vector3.ProjectOnPlane(facing*Vector3.forward,Vector3.up).normalized;var delta=Vector3.ProjectOnPlane(point-head,Vector3.up)-forward*1.1f;owner.movementController.Teleport(owner.movementController.transform.position+delta);break;
   case 17:Send(MotionPointer.AimAtWorld(stationCollider.bounds.center));break;
   case 18:Plugin.Line("RELEASE_GENERIC_BEFORE preview="+owner.currentPreviewObject?.name+" active="+owner.currentPreviewObject?.activeInHierarchy+" target="+owner.lastPlacementPointForPreview?.name+" closest="+owner.closestInteractable?.name+" station="+station.name+" targetPos="+stationCollider.bounds.center+" ray="+owner.interactionRay+" hit="+owner.hasHit+"/"+owner.rayHit.collider?.name+"/"+owner.rayHit.point);Check(owner.currentPreviewObject!=null&&owner.currentPreviewObject.activeInHierarchy&&owner.lastPlacementPointForPreview!=null,"native pizza green preview unavailable");Send("grip r 0");break;
   case 19:
    Check(owner.heldItems.Count==0&&station.currentPizza!=null&&station.currentPizza.Pointer==pizza.Pointer,"green pizza preview release did not place");Plugin.Line("RELEASE_GREEN_PIZZA_PASS");
    Plugin.Line("RELEASE_PLACEMENT_ACCEPTANCE_COMPLETE");phase=99;return;
   case 50:
    Check(StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var rightGrip,out _),"right grip unavailable");Send(Hand(true,rightGrip)+"\nhand l aimtrim -80 0");break;
   case 51:Send("grip l 1");break;
   case 52:
    Check(ObjectGrabs.Grabbed&&ObjectGrabs.LeftHand,"left pizza regrip failed");Check(station.currentPizza==null,"original station not freed");Check(StereoTrial.TryHeadWorld(out var h,out var f),"head unavailable");var d=Vector3.ProjectOnPlane(stationCollider.bounds.center-h,Vector3.up)-Vector3.ProjectOnPlane(f*Vector3.forward,Vector3.up).normalized*1.1f;owner.movementController.Teleport(owner.movementController.transform.position+d);break;
   case 53:Send(MotionPointer.AimAtWorld(stationCollider.bounds.center,true)+"\nhand r aimtrim 0 80");break;
   case 54:
    Plugin.Line("RELEASE_LEFT_PREVIEW hit="+owner.rayHit.collider?.name+" point="+owner.lastPlacementPointForPreview?.name);Check(owner.currentPreviewObject!=null&&owner.currentPreviewObject.activeInHierarchy&&owner.lastPlacementPointForPreview!=null,"left native pizza green preview unavailable");Send("shot left-green-preview\ngrip l 0");break;
   case 55:
    Check(owner.heldItems.Count==0&&station.currentPizza!=null&&station.currentPizza.Pointer==pizza.Pointer,"left green pizza release failed");Plugin.Line("RELEASE_LEFT_GREEN_PIZZA_PASS");Plugin.Line("RELEASE_PLACEMENT_ACCEPTANCE_COMPLETE");phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+delay;}
  catch(Exception e){Plugin.Line("RELEASE_PLACEMENT_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
}
