using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class OvenCycleAudit
{
 private static bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_OVEN_CYCLE")=="1";
 private static bool physical=Environment.GetEnvironmentVariable("IKAG_XR_OVEN_PHYSICAL")=="1";
 private static bool left=Environment.GetEnvironmentVariable("IKAG_XR_OVEN_LEFT")=="1";
 private static string Grip(bool value)=>"grip "+(left?"l":"r")+" "+(value?"1":"0");
 private static string Aim(Vector3 point)
 {
  if(!physical)return MotionPointer.AimAtWorld(point,left);
  StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var origin,out var rotation);
  var p=Quaternion.Inverse(rotation)*(point-origin);
  return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");
 }
 private static bool Attached()=>physical?KitchenHands.Pose(left,out _,out _):KitchenHands.DistancePoint(out _);
 private static int phase;private static float next=25,deadline;private static Oven oven;private static OvenHandle handle;private static Pizza pizza;private static InteractionManager player;private static Vector3 closed,opened;private static int cycle;
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 private static void State(string tag)=>Plugin.Line("OVEN_STATE "+tag+" motion="+handle?.handleMotion+" cycle="+cycle+" pizza="+oven?.currentPizza?.name+" enabled="+handle?.isEnabled+" external="+handle?.hasExternalProgress+" resetting="+handle?.isResetting+" drag="+handle?.isDragging+" value="+handle?.currentLocalRotationY+" target="+handle?.targetLocalRotationY+" netProgress="+oven?.networkHandleProgress.Value+" used="+oven?.networkHasUsedHandle.Value+" cook="+oven?.isCooking+" local="+oven?.isInUseLocally+" mode="+MachineInteractionBase.IsLocalPlayerInMachineMode+" can="+oven?.CanInteract());
 internal static void Tick()
 {
  if(!enabled||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try
  {
   switch(phase)
   {
    case 0:
     UiInputAudit.Dump("Assembly-CSharp.dll","","Oven","OnEnterMachineMode","OnExitMachineMode","OnMachineUseStateChanged","OnNetworkHandleProgressChanged","OnNetworkHasUsedHandleChanged","PlacePizzaLocal","RequestPlacePizzaRpc","OnPizzaTaken","ClearOvenNetworkState","OnHandleProgressChanged","UpdateHandleProgressRpc","OnHandleCookingStarted","OnHandleCookingStopped","OnTrayFullyOut");
     UiInputAudit.Dump("Assembly-CSharp.dll","","OvenHandle","SetEnabled","ResetHandle","ResetToStartPosition","SetProgressExternal","StartDragging");
     foreach(var o in UnityEngine.Object.FindObjectsOfType<Oven>()){oven=o;handle=o.ovenHandle;State("saved-scene");}
     player=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("input clear\nhands reset");
     var prefab=Environment.GetEnvironmentVariable("IKAG_XR_OVEN_SCENE")=="1"?oven:Resources.FindObjectsOfTypeAll<Oven>()[0];var clone=UnityEngine.Object.Instantiate(prefab.gameObject);clone.transform.position=player.transform.position+Vector3.right*3;clone.SetActive(true);var net=clone.GetComponent<NetworkObject>();if(!net.IsSpawned)net.Spawn();oven=clone.GetComponent<Oven>();handle=oven.ovenHandle;
     foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()){pizza=p;break;}Check(pizza!=null,"No pizza fixture");
     var cam=player.interactionCamera;clone.transform.rotation=Quaternion.FromToRotation(Vector3.ProjectOnPlane(oven.CameraViewPoint.forward,Vector3.up),Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up))*clone.transform.rotation;
     Physics.SyncTransforms();clone.transform.position+=cam.transform.position+cam.transform.forward*1.5f-Vector3.up*.25f-handle.GetComponentInChildren<Collider>().bounds.center;Physics.SyncTransforms();State("fixture");break;
    case 1:
     var toppings=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();toppings.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));pizza.RestoreFromSave(DoughState.SaucedDough,0,false,CookingQuality.Raw,toppings);
     Check(player.TryPickupItem(pizza.Cast<IHoldable>()),"Pickup fixture pizza failed");oven.OnInteract();break;
    case 2:
     State("placed");Check(oven.currentPizza!=null,"Native oven placement failed");AimEndpoints();Send(Aim(opened));break;
    case 3:Send(Grip(true));break;
    case 4:State("grab-close");Check(Attached(),"Cannot grab handle to close");Send(Aim(closed));deadline=Time.realtimeSinceStartup+5;break;
    case 5:
     if(handle.GetProgress()<.99f){Check(Time.realtimeSinceStartup<deadline,"Handle did not close");next=Time.realtimeSinceStartup+.5f;return;}
     State("closed");Send(Grip(false));deadline=Time.realtimeSinceStartup+60;break;
    case 6:
     if(!oven.IsPizzaPerfectlyCooked()){Check(Time.realtimeSinceStartup<deadline,"Pizza did not cook");next=Time.realtimeSinceStartup+.5f;return;}
     State("cooked");Send(Aim(handle.GetComponentInChildren<Collider>().bounds.center));break;
    case 7:Send(Grip(true));break;
    case 8:State("grab-open");Check(Attached(),"Cannot regrab handle to open");Send(Aim(opened));deadline=Time.realtimeSinceStartup+5;break;
    case 9:
     if(handle.GetProgress()>.01f){Check(Time.realtimeSinceStartup<deadline,"Handle did not open");next=Time.realtimeSinceStartup+.5f;return;}
     State("opened");Send(Grip(false));break;
    case 10:Check(player.TryPickupItem(pizza.Cast<IHoldable>()),"Cooked pizza pickup failed");State("pizza-taken");break;
    case 11:
     Check(!oven.HasPizza(),"Oven retained removed pizza");player.DropLastItem();cycle++;Plugin.Line("OVEN_CYCLE_PASS cycle="+cycle);
     if(cycle<2){phase=1;next=Time.realtimeSinceStartup+1;return;}
     Plugin.Line("OVEN_REPEAT_COMPLETE physical="+physical+" left="+left);StereoTrial.DiagnosticMetrics();if(Environment.GetEnvironmentVariable("IKAG_XR_OVEN_L2_GATE")=="1")Environment.SetEnvironmentVariable("IKAG_XR_HANDLE_ASSET_AUDIT","1");enabled=false;return;
   }
   phase++;next=Time.realtimeSinceStartup+1;
  }
  catch(Exception e){State("failed");Plugin.Line("OVEN_REPEAT_FAILED phase="+phase+" "+e);Send("input clear");enabled=false;}
 }
 private static void AimEndpoints()
 {
  var t=handle.transform;var contact=handle.GetComponentInChildren<Collider>().bounds.center;var local=t.parent.InverseTransformPoint(contact);opened=contact;
  var axis=handle.rotationAxis==NeedleRotationAxis.X?Vector3.right:handle.rotationAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;
  if(handle.handleMotion==HandleMotion.Rotate)closed=t.parent.TransformPoint(t.localPosition+Quaternion.AngleAxis(handle.maxLocalRotationY-handle.currentLocalRotationY,axis)*(local-t.localPosition));
  else {axis=handle.slideAxis==NeedleRotationAxis.X?Vector3.right:handle.slideAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;closed=contact+t.parent.TransformVector(axis*(handle.slideClosedValue-handle.slideOpenValue));}
 }
}
