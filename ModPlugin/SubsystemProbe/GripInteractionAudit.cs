using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using System.IO;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class GripInteractionAudit
{
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_GRIP_WORLD_AUDIT");
 private static bool done;
 internal static void Tick()
 {
  if(mode==null||done||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<24)return;
  if(mode=="world"){World();return;}done=true;
  InventoryManager.LocalInstance.SetSelectedSlot(7);
  UiInputAudit.Dump("Assembly-CSharp.dll","","InteractionManager","HandleHoldInteraction","HandleInteraction","HandleInteractPressed","IsStillFacingHoldTarget","CancelHoldInteraction");
  UiInputAudit.Dump("Assembly-CSharp.dll","Core.Input","InputManager","WasInteractPressedRaw","get_IsInteractHeld");
  var counts=new Dictionary<string,int>();
  foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
  {
   var i=m.TryCast<IInteractable>();if(i==null)continue;
   string type=Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(IL2CPP.il2cpp_object_get_class(m.Pointer)));
   counts.TryGetValue(type,out int n);counts[type]=n+1;
   if(n>=3)continue;
   try{Plugin.Line("GRIP_WORLD_TARGET type="+type+" name="+m.name+" position="+m.transform.position+" can="+i.CanInteract()+" hold="+(m.TryCast<IHoldInteractable>()!=null)+" pickup="+(m.TryCast<IHoldable>()!=null));}
   catch(Exception e){Plugin.Line("GRIP_WORLD_CENSUS_PROVIDER "+type+" "+e.Message);}
  }
  foreach(var row in counts)Plugin.Line("GRIP_WORLD_TYPE "+row.Key+" count="+row.Value);
  Plugin.Line("GRIP_WORLD_CENSUS_COMPLETE");
 }
 private static int phase;private static float next=24;private static InteractionManager owner;private static DoorInteractable door;private static bool wasOpen;
 private static OpenWorldDummyNPC npc;private static SabotageTarget repair;private static DarkWebComputer computer;private static Vector3 target;
 private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
 private static void Stand(Vector3 point)
 {
  Check(StereoTrial.TryHeadWorld(out var p,out var q),"head unavailable");var forward=Vector3.ProjectOnPlane(q*Vector3.forward,Vector3.up).normalized;
  owner.movementController.Teleport(owner.movementController.transform.position+Vector3.ProjectOnPlane(point-p,Vector3.up)-forward*1.25f);
 }
 private static Collider Col(Component c){var col=c.GetComponent<Collider>();return col!=null?col:c.GetComponentInChildren<Collider>();}
 private static Collider DoorCol()=>door.doorCollider!=null?door.doorCollider:Col(door);
 private static void StandAtDoor()
 {
  var c=DoorCol();var box=c.TryCast<BoxCollider>();Check(box!=null,"door visibility fixture needs authored box collider");
  var normal=c.transform.TransformDirection(box.size.x<box.size.z?Vector3.right:Vector3.forward).normalized;
  Check(StereoTrial.TryHeadWorld(out var head,out _),"head missing");if(Vector3.Dot(head-c.bounds.center,normal)<0)normal=-normal;
  owner.movementController.Teleport(owner.movementController.transform.position+Vector3.ProjectOnPlane(c.bounds.center+normal*1.3f-head,Vector3.up));
  Plugin.Line("ALL_GRIP_DOOR_FIXTURE collider="+c.name+" size="+box.size+" center="+c.bounds.center+" normal="+normal);
 }
 private static void World()
 {
  if(Time.realtimeSinceStartup<next)return;
  try{float delay=.7f;switch(phase){
   case 0:UiInputAudit.Dump("Assembly-CSharp.dll","","MachineInteractionBase","HandleMachineInput");owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("hands reset\n"+MotionPointer.AimAtWorld(ImmersiveHud.BoardClose));break;
   case 1:Send("grip r 1");break;
   case 2:Send("grip r 0");break;
   case 3:
    Check(!ImmersiveHud.BoardPinned,"grip did not close board");Plugin.Line("ALL_GRIP_BOARD_UI_PASS");
    float best=float.PositiveInfinity;foreach(var d in UnityEngine.Object.FindObjectsOfType<DoorInteractable>())if(d.CanInteract()&&d.IsSpawned){float distance=(d.transform.position-owner.transform.position).sqrMagnitude;if(distance<best){door=d;best=distance;}}
    Check(door!=null,"door missing");wasOpen=door.networkIsOpen.Value;StandAtDoor();Send("hands reset");break;
   case 4:Send(MotionPointer.AimAtWorld(DoorCol().bounds.center,true)+"\nhand r aimtrim 0 85");break;
   case 5:
    Check(MotionPointer.TryControllerRay(true,out var ray),"left aim unavailable");bool visible=Physics.Raycast(ray,out var hit,owner.rayDistance,owner.interactionLayers);
    Plugin.Line("ALL_GRIP_DOOR_VISIBILITY hit="+hit.collider?.name+" target="+DoorCol().name+" ray="+ray+" reach="+owner.rayDistance);
    Check(visible&&hit.collider!=null&&(hit.collider.Pointer==DoorCol().Pointer||hit.collider.transform.IsChildOf(door.transform)),"door visibility positive control failed");Send("grip l 1");break;
   case 6:
    Plugin.Line("ALL_GRIP_DOOR_STATE selected="+owner.currentInteractable?.GetTransform()?.name+" open="+door.networkIsOpen.Value+" hit="+owner.rayHit.collider?.name+" ray="+owner.interactionRay+" pending="+DistanceGrips.Pending+" cursor="+Cursor.lockState);
    Check(door.networkIsOpen.Value!=wasOpen,"left grip did not toggle door");Send("shot all-grip-door\nhand l aimtrim -80 0");break;
   case 7:Check(door.networkIsOpen.Value!=wasOpen,"held grip repeated door action");Send("grip l 0");InventoryManager.LocalInstance.SetSelectedSlot(1);break;
   case 8:Send(MotionPointer.AimAtWorld(DoorCol().bounds.center));break;
   case 9:Send("grip r 1");break;
   case 10:
    Check(door.networkIsOpen.Value==wasOpen,"right grip did not toggle door with equipped item");Check(owner.heldItems.Count==1&&owner.heldItems[0].GetComponent<PhotoCamera>()!=null,"door interaction dropped equipped camera");Plugin.Line("ALL_GRIP_DOORS_BOTH_PASS");
    Send("grip r 0");InventoryManager.LocalInstance.SetSelectedSlot(7);
    best=float.PositiveInfinity;foreach(var n in UnityEngine.Object.FindObjectsOfType<OpenWorldDummyNPC>()){Plugin.Line("ALL_GRIP_NPC_FIXTURE name="+n.name+" can="+n.CanInteract()+" collider="+Col(n)?.name);if(n.CanInteract()&&Col(n)!=null){float d=(n.transform.position-owner.transform.position).sqrMagnitude;if(d<best){best=d;npc=n;}}}
    Check(npc!=null,"NPC missing");target=Col(npc).bounds.center;Stand(target);break;
   case 11:Send(MotionPointer.AimAtWorld(Col(npc).bounds.center,true)+"\nhand r aimtrim 0 85");break;
   case 12:Send("grip l 1");delay=2;break;
   case 13:
    Check(npc.isInDialogLocally&&npc.networkIsInDialog.Value&&UIManager.LocalInstance.isDialogActive,"left grip did not open native NPC conversation");Plugin.Line("ALL_GRIP_NPC_DIALOG_PASS");Send("shot all-grip-dialog\ngrip l 0");npc.ForceCloseDialog();delay=1.5f;break;
   case 14:
    Check(!UIManager.LocalInstance.isDialogActive,"NPC cleanup failed");best=float.PositiveInfinity;
    foreach(var t in UnityEngine.Object.FindObjectsOfType<SabotageTarget>()){float d=(t.transform.position-owner.transform.position).sqrMagnitude;if(d<best){best=d;repair=t;}}
    Check(repair!=null,"repair target missing");repair.ServerBreak();target=repair.ActionPoint!=null?repair.ActionPoint.position:Col(repair).bounds.center;Stand(target);break;
   case 15:Check(repair.IsBroken&&repair.CanInteract(),"native broken repair control invalid");Send(MotionPointer.AimAtWorld(target));break;
   case 16:Send("grip r 1");delay=Math.Min(1.5f,repair.HoldDuration*.35f);break;
   case 17:
    Plugin.Line("ALL_GRIP_HOLD_STATE active="+(owner.activeHoldInteractable!=null)+" progress="+owner.pendingHoldProgress+" duration="+repair.HoldDuration+" selected="+owner.currentInteractable?.GetTransform()?.name);
    Check(owner.activeHoldInteractable!=null&&owner.pendingHoldProgress>0&&repair.IsBroken,"grip hold did not start native repair");Send("grip r 0");break;
   case 18:
    Check(owner.activeHoldInteractable==null&&repair.IsBroken,"hold release did not cancel repair");Plugin.Line("ALL_GRIP_HOLD_CANCEL_PASS");Send(MotionPointer.AimAtWorld(target,true)+"\nhand r aimtrim 0 85");break;
   case 19:Send("grip l 1");delay=repair.HoldDuration+1;break;
   case 20:Check(!repair.IsBroken,"held left grip did not complete native repair");Plugin.Line("ALL_GRIP_HOLD_COMPLETE_PASS");Send("grip l 0");break;
   case 21:
    computer=UnityEngine.Object.FindObjectOfType<DarkWebComputer>();Check(computer!=null,"computer missing");target=Col(computer).bounds.center;Stand(target);break;
   case 22:Send(MotionPointer.AimAtWorld(target,true));break;
   case 23:Send("grip l 1");delay=2;break;
   case 24:
    Check(MachineInteractionBase.IsLocalPlayerInMachineMode&&MachineInteractionBase.CurrentActiveMachine.Pointer==computer.Pointer,"left grip did not enter computer");Plugin.Line("ALL_GRIP_COMPUTER_ENTRY_PASS");Send("grip l 0\nbtn b press 100");delay=2;break;
   case 25:
    Check(!MachineInteractionBase.IsLocalPlayerInMachineMode,"computer exit failed");Plugin.Line("ALL_GRIP_WORLD_ACCEPTANCE_COMPLETE");done=true;return;
  }phase++;next=Time.realtimeSinceStartup+delay;}
  catch(Exception e){Plugin.Line("ALL_GRIP_WORLD_FAILED phase="+phase+" "+e);Send("input clear");done=true;}
 }

}
