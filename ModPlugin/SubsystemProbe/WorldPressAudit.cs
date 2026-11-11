using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class WorldPressAudit
{
 private static readonly bool cost=Environment.GetEnvironmentVariable("IKAG_XR_WORLD_PRESS_COST")=="1";
 private static readonly System.Reflection.FieldInfo costFrame=cost?typeof(WorldPress).GetField("frame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static):null;
 private static int costWarm,costCount;private static long costBytes,costTicks,costMax;
 private static void Cost()
 {
  if(!cost||costCount>=1200||!ShovelPerfAudit.Ready||++costWarm<300)return;
  // Re-run exactly the cached interaction path. Reflection setup is outside
  // the measured scope; this additional invocation exists only in diagnostics.
  costFrame.SetValue(null,-1);long bytes=GC.GetAllocatedBytesForCurrentThread(),at=System.Diagnostics.Stopwatch.GetTimestamp();WorldPress.Tick();long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-at;costTicks+=elapsed;costMax=Math.Max(costMax,elapsed);costBytes+=GC.GetAllocatedBytesForCurrentThread()-bytes;
  if(++costCount==1200)Plugin.Line("WORLD_PRESS_COST_COMPLETE samples="+costCount+" bytes="+costBytes+" meanMs="+(costTicks*1000d/System.Diagnostics.Stopwatch.Frequency/costCount)+" maxMs="+(costMax*1000d/System.Diagnostics.Stopwatch.Frequency));
 }
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_WORLD_PRESS_AUDIT");private static readonly bool enabled=mode!=null;
 private static InteractionManager owner;private static readonly List<WorldPress.Target> cases=new();private static int index,phase,activations,before,restores;private static float next=25,maxTip,maxP,maxQ,maxS;
 private static bool left,moving,saved;private static float moveAt;private static Vector3 from,to;private static Quaternion rotation;private static string state;private static int gateWaits;
 private static Transform[] bones;private static Vector3[] positions,scales;private static Quaternion[] rotations;
 private static int changedActorFrame=-1;private static GameObject wall;private static Vector3 actor;private static bool shopLock;
 private static CashRegisterMachine register;private static CourierOrderManager orders;private static CourierOrderMachine orderMachine;
 private static WorldPress.Target Target=>cases[index];private static WorldPress.Face Face=>Target.Faces[0];
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static bool Ready()
 {
  Prepare();
  if(Target.Action.CanInteract()){gateWaits=0;return true;}
  if(gateWaits++==0){var shop=Target.Component.TryCast<ShopShutterController>();Plugin.Line("WORLD_PRESS_WAIT_NATIVE target="+Target.Component.name+" latched="+Target.Latched+" animating="+(shop==null?"n/a":shop.isAnimating.ToString())+" duration="+(shop==null?"n/a":shop.animationDuration.ToString()));}
  Check(gateWaits<100,"Native target never re-enabled");next=Time.realtimeSinceStartup+.2f;return false;
 }
 private static void LoadMachine()
 {
  var burner=Target.Component.TryCast<IncineratorButton>();
  if(burner!=null){var machine=burner.incinerator;Check(!machine.HasItem()&&!machine.IsRunning(),"Burn fixture occupied");BurnableItem source=null;foreach(var x in Resources.FindObjectsOfTypeAll<BurnableItem>())if(!x.gameObject.scene.IsValid()&&x.burnableSO!=null&&(int)x.burnableSO.requiredMachineLevel<=(int)machine.GetMachineLevel()){source=x;break;}Check(source!=null,"No burn fixture");var obj=UnityEngine.Object.Instantiate(source.gameObject);obj.transform.position=owner.transform.position+Vector3.up*3;obj.SetActive(true);obj.GetComponent<Unity.Netcode.NetworkObject>().Spawn();obj.GetComponent<BurnableItem>().InitializeFromSO(source.burnableSO);machine.TryPlaceItem(obj);Check(machine.HasItem(),"Native burner placement rejected fixture");return;}
  var washer=Target.Component.Cast<WashingMachineButton>().machine;Check(!washer.AnyDrumHasItem()&&!washer.IsRunning(),"Wash fixture occupied");WashableItem wash=null;foreach(var x in Resources.FindObjectsOfTypeAll<WashableItem>())if(!x.gameObject.scene.IsValid()&&x.washableSO!=null&&(int)x.washableSO.requiredMachineLevel<=(int)washer.GetMachineLevel()){wash=x;break;}Check(wash!=null,"No wash fixture");var item=UnityEngine.Object.Instantiate(wash.gameObject);item.transform.position=owner.transform.position+Vector3.up*3;item.SetActive(true);item.GetComponent<Unity.Netcode.NetworkObject>().Spawn();item.GetComponent<WashableItem>().InitializeFromSO(wash.washableSO);item.GetComponent<WashableItem>().SetClean(false);washer.drums[0].TryPlaceItem(item);Check(washer.AnyDrumHasItem(),"Native drum placement rejected fixture");for(int i=0;i<washer.doors.Count;i++)if(washer.doors[i].IsOpen())washer.RequestToggleDoor(i);
 }
 private static void Prepare()
 {
  if(mode=="pos"){register.posInputBuffer="";register.UpdatePOSEnteredAmount();register.UpdateUI();}
  if(mode=="online"&&orders.IsElectricityOn()&&orders.isShopOpen&&orders.GetPendingOrders().Count==0){orders.GenerateRandomOrder();orderMachine.SyncStateFromManager();orderMachine.UpdateButtonStates();Plugin.Line("WORLD_PRESS_ORDER_FIXTURE pending="+orders.GetPendingOrders().Count+" npcs="+CourierOrderManager.registeredNPCs.Count);}
 }
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void Fail(Exception e){int failed=phase;moving=false;phase=99;Plugin.Line("WORLD_PRESS_FAILED index="+index+" phase="+failed+" "+e);Send("input clear\nhands reset");}
 internal static void Activated(MonoBehaviour c,bool side){if(enabled){Check(index<cases.Count&&c.Pointer==Target.Component.Pointer,"Wrong physical target");Check(side==left,"Wrong physical hand");activations++;}}
 internal static void Before(Animator a)
 {
  if(!enabled||phase==99||owner==null)return;if(bones==null){var all=a.GetComponentsInChildren<Transform>(true);bones=new Transform[all.Length];positions=new Vector3[all.Length];scales=new Vector3[all.Length];rotations=new Quaternion[all.Length];for(int i=0;i<all.Length;i++)bones[i]=all[i];}
  for(int i=0;i<bones.Length;i++){positions[i]=bones[i].localPosition;scales[i]=bones[i].localScale;rotations[i]=bones[i].localRotation;}saved=true;
 }
 internal static void Rendered()
 {if(!enabled||!saved||changedActorFrame==Time.frameCount)return;try{foreach(bool side in new[]{false,true})if(WorldPress.Tip(side,out var p)){float error=Vector3.Distance(p,MotionBody.RenderedIndex(side));maxTip=Math.Max(maxTip,error);Check(error<.001f,"Input/render fingertip mismatch "+error);}}catch(Exception e){Fail(e);}}
 internal static void Restored()
 {if(!saved)return;saved=false;try{for(int i=0;i<bones.Length;i++){maxP=Math.Max(maxP,Vector3.Distance(positions[i],bones[i].localPosition));maxS=Math.Max(maxS,Vector3.Distance(scales[i],bones[i].localScale));maxQ=Math.Max(maxQ,1-Mathf.Abs(Quaternion.Dot(rotations[i],bones[i].localRotation)));}restores++;Check(maxP<1e-6&&maxQ<1e-6&&maxS<1e-6,"Body restoration leaked");}catch(Exception e){Fail(e);}}
 private static string State(WorldPress.Target t)
 {
  if(mode=="online")return "orders/"+orders.GetPendingOrders().Count+"/"+orders.activeOrders.Count+"/"+orderMachine.GetFirstPendingOrderId();if(mode=="pos")return "pos/"+register.GetPOSInputBuffer();var burner=t.Component.TryCast<IncineratorButton>();if(burner!=null)return "burn/"+burner.incinerator.IsRunning();var washer=t.Component.TryCast<WashingMachineButton>();if(washer!=null)return "wash/"+washer.machine.IsRunning();var shop=t.Component.TryCast<ShopShutterController>();if(shop!=null)return "shop/"+shop.IsShopOpen();var door=t.Component.TryCast<BackroomDoorController>();if(door!=null){while(door.isSecondaryButton&&door.primaryController!=null)door=door.primaryController;return "door/"+door.GetIsOpen();}var light=t.Component.TryCast<ProjectOne.Gameplay.LightSwitch>();if(light!=null)return "light/"+light.isLightsOn;var bell=t.Component.TryCast<RegisterBell>();if(bell!=null)return "bell/"+bell.lastRingTime;return "other";
 }
 private static Vector3 Point(float distance)
 {Face.Refresh();var p=Face.Bounds.center;p[Face.Axis]=Face.Sign>0?Face.Bounds.max[Face.Axis]:Face.Bounds.min[Face.Axis];return Face.Matrix.MultiplyPoint3x4(p)+Face.Normal*distance;}
 private static void PoseTip(Vector3 tip)
 {
  Check(MotionBody.TryPointingGeometry(owner,left,out var offset,out var root),"Missing index geometry");MotionBody.TryCarryFrame(left,out _,out _,out var normal,out _,out _);
  var direction=(offset-root).normalized;var up=Vector3.ProjectOnPlane(Vector3.up,Face.Normal).normalized;if(up.sqrMagnitude<.01f)up=Vector3.forward;
  rotation=Quaternion.LookRotation(-Face.Normal,up)*Quaternion.Inverse(Quaternion.LookRotation(direction,normal));var grip=tip-rotation*offset;Send(PhysicalItemAudit.Pose(left,grip,rotation));
 }
 private static void Move(float depth)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out from,out rotation),"Missing hand pose");to=from+Point(depth)-Point(.06f);moveAt=Time.realtimeSinceStartup;moving=true;
 }

 private static void Edge()
 {
  var shop=Target.Component.Cast<ShopShutterController>();
  switch(phase){
   case 100:left=false;StereoTrial.TryHeadWorld(out var head,out _);var destination=Point(.7f);owner.movementController.Teleport(owner.movementController.transform.position+Vector3.ProjectOnPlane(destination-head,Vector3.up));Send("input clear\nhands reset");before=activations;state=State(Target);shopLock=shop.interactionLocked;break;
   case 101:PoseTip(Point(.06f));break;
   case 102:Send("grip r 1");break;
   case 103:Check(WorldPress.OwnsGrip(false),"Edge fixture missing proximity");actor=owner.movementController.transform.position;owner.movementController.Teleport(actor-Face.Normal*.08f);changedActorFrame=Time.frameCount;break;
   case 104:Check(activations==before&&State(Target)==state,"Artificial movement pressed button");owner.movementController.Teleport(actor);changedActorFrame=Time.frameCount;break;
   case 105:
    wall=new GameObject("FingertipOcclusionFixture");int mask=owner.interactionLayers.value&~0x404;int layer=0;while(layer<31&&(mask&(1<<layer))==0)layer++;wall.layer=layer;wall.transform.SetPositionAndRotation(Point(.025f),Quaternion.LookRotation(Face.Normal));wall.AddComponent<BoxCollider>().size=new Vector3(.5f,.5f,.004f);break;
   case 106:Move(-.005f);break;
   case 107:Check(activations==before&&Target.Latched,"Blocked press reached native action");UnityEngine.Object.Destroy(wall);PoseTip(Point(.06f));break;
   case 108:shop.interactionLocked=true;Check(!Target.Action.CanInteract(),"Native lock fixture failed");Move(-.005f);break;
   case 109:Check(activations==before&&Target.Latched,"Locked press reached native action");shop.interactionLocked=false;break;
   case 110:Check(activations==before&&State(Target)==state,"Unlock repeated resting contact");PoseTip(Point(.06f));break;
   case 111:Check(!Target.Latched,"Withdrawal did not clear rejected contact");Move(-.005f);break;
   case 112:Check(activations==before+1&&State(Target)!=state,"Valid press after withdrawal failed");shop.interactionLocked=shopLock;Send("input clear\nhands reset");Plugin.Line("WORLD_PRESS_COMPLETE edges=actor-motion,occlusion,native-lock,unlock-latch,withdrawal activations="+activations+" restores="+restores+" tipError="+maxTip);phase=99;return;
  }
  phase++;next=Time.realtimeSinceStartup+.8f;
 }
 internal static void Tick()
 {
  Cost();if(!enabled||phase==99||!StereoTrial.GameplayActive)return;
  try{
   if(moving){float t=Mathf.Clamp01((Time.realtimeSinceStartup-moveAt)/.5f);Send(PhysicalItemAudit.Pose(left,Vector3.Lerp(from,to,t),rotation));if(t>=1){moving=false;next=Time.realtimeSinceStartup+1;}return;}
   if(Time.realtimeSinceStartup<next)return;
   if(owner==null){owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("input clear\nhands reset");
    if(mode=="online"){orders=CourierOrderManager.Instance;orderMachine=UnityEngine.Object.FindObjectOfType<CourierOrderMachine>();if(!orders.shopShutterController.IsShopOpen())orders.shopShutterController.OnInteract();if(!orders.electricitySwitch.IsOn)orders.electricitySwitch.OnInteract();}
    if(mode=="pos"){register=UnityEngine.Object.FindObjectOfType<CashRegisterMachine>();register.currentMode=CashRegisterMode.CreditCard;register.isPaymentPending=true;register.orderTotal=999;register.posInputBuffer="";register.EnterMachineModeLocal();register.SetAllPOSKeysActive(true);register.cashRegisterUI.SetMode(CashRegisterMode.CreditCard);register.UpdateUI();}
    foreach(var t in WorldPress.Targets){var k=t.Component.GetIl2CppType().Name;if(mode=="machines"?k=="IncineratorButton"||k=="WashingMachineButton":mode=="edge"?k=="ShopShutterController":mode=="online"?k=="CourierOrderButton":mode=="pos"?k=="POSKeyButton"&&register.posKeyButtons.Contains(t.Component.TryCast<POSKeyButton>())&&(t.Component.name=="1"||t.Component.name=="2"):k=="ShopShutterController"||k=="BackroomDoorController"||k=="LightSwitch"||k=="RegisterBell")cases.Add(t);}Check(cases.Count>=(mode=="1"?4:mode=="edge"?1:2),"Physical button registry incomplete");if(mode=="edge")phase=100;next=Time.realtimeSinceStartup+2;return;}
   if(index>=cases.Count){Check(restores>100,"Missing restoration coverage");Plugin.Line("WORLD_PRESS_COMPLETE targets="+cases.Count+" activations="+activations+" restores="+restores+" tipError="+maxTip+" position="+maxP+" rotation="+maxQ+" scale="+maxS);phase=99;Send("input clear\nhands reset");return;}
   if(mode=="edge"){Edge();return;}
   switch(phase){
    case 0:left=mode=="machines"&&index%2!=0;if(mode=="machines")LoadMachine();Prepare();StereoTrial.TryHeadWorld(out var head,out _);var destination=Point(.7f);if(mode!="pos")owner.movementController.Teleport(owner.movementController.transform.position+Vector3.ProjectOnPlane(destination-head,Vector3.up));StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out _,out var basis);var up=Mathf.Abs(Vector3.Dot(Face.Normal,Vector3.up))>.99f?Vector3.forward:Vector3.up;var e=(Quaternion.Inverse(basis)*Quaternion.LookRotation(-Face.Normal,up)).eulerAngles;Send(FormattableString.Invariant($"input clear\nhands reset\nhead rot {-e.y:R} {-e.x:R} {-e.z:R}"));break;
    case 1:PoseTip(Point(.06f));break;
    case 2:Send(MotionPointer.AimAtWorld(Point(0),left)+"\ngrip "+(left?"l":"r")+" 1");before=activations;state=State(Target);break;
    case 3:Check(WorldPress.OwnsGrip(left),"Near control did not reserve grip");Check(WorldPress.Tip(left,out var placedTip)&&Vector3.Distance(placedTip,Point(.06f))<.001f,"Fixture pointing tip missed requested start");Check(activations==before&&State(Target)==state,"Grip activated before finger contact");if(!Ready())return;state=State(Target);Move(-.005f);break;
    case 4:Check(activations==before+1&&State(Target)!=state,"Finger press did not change native state "+Target.Component.name+" old="+state+" new="+State(Target));Send("shot world-press-"+index);if(mode=="machines"){Plugin.Line("WORLD_PRESS_TARGET_PASS type="+Target.Component.GetIl2CppType().Name+" state="+State(Target)+" left="+left);Send("input clear\nhands reset");index++;phase=-1;}break;
    case 5:Check(activations==before+1,"Resting finger repeated action");PoseTip(Point(.06f));break;
    case 6:
     if(!Ready())return;Plugin.Line("WORLD_PRESS_REPEAT_READY target="+Target.Component.name+" latched="+Target.Latched);before=activations;state=State(Target);Move(-.005f);break;
    case 7:Check(activations==before+1&&State(Target)!=state,"Withdrawal did not rearm");Send("grip r 0\ninput clear\nhands reset");left=true;break;
    case 8:PoseTip(Point(.06f));break;
    case 9:Send(MotionPointer.AimAtWorld(Point(0),true)+"\ngrip l 1");before=activations;state=State(Target);break;
    case 10:Check(activations==before,"Left grip activated before contact");if(!Ready())return;state=State(Target);Move(-.005f);break;
    case 11:Check(activations==before+1&&State(Target)!=state,"Left physical press failed");Plugin.Line("WORLD_PRESS_TARGET_PASS type="+Target.Component.GetIl2CppType().Name+" name="+Target.Component.name+" faces="+Target.Faces.Length);Send("input clear\nhands reset");index++;phase=-1;break;
   }
   phase++;next=Time.realtimeSinceStartup+.8f;
  }catch(Exception e){Fail(e);}
 }
}
