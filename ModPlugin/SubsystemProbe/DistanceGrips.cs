using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Select one hand for the game's existing world query. Consume a grip only after
// that query completes; no second raycast, overlap or independent target finder.
internal static class DistanceGrips
{
 private static bool pending,left,interacting;private static int requestedFrame,queryFrame=-1;private static bool queryLeft;private static bool consumedLeft,consumedRight;
 private static InteractionManager owner;private static GameObject pickup;private static IHoldable candidate;private static IInteractable interactable;private static IHoldInteractable hold;private static GameObject fallback;
 internal static bool AimLeft=>pending||interacting?left:KitchenHands.AcquiringRemote?KitchenHands.RemoteLeft:ObjectGrabs.LeftHand;
 internal static bool InteractHeld=>interacting&&(left?ControllerTransport.Left:ControllerTransport.Right).Grip;
 internal static bool OwnsGrip(bool useLeft)=>(useLeft?consumedLeft:consumedRight)||((pending||interacting)&&left==useLeft);
 internal static bool QueryMatches(bool useLeft)=>queryFrame>=0&&Time.frameCount-queryFrame<=1&&queryLeft==useLeft;
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.distance-grips");
  h.Patch(typeof(InteractionManager).GetMethod("HandleInteraction"),prefix:Hook(nameof(BeforeInteraction)));
  h.Patch(typeof(InteractionManager).GetMethod("Update"),postfix:Hook(nameof(AfterUpdate)));
 }
 private static HarmonyMethod Hook(string name)=>new(typeof(DistanceGrips).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
 internal static void Reset(){pending=interacting=false;owner=null;pickup=null;candidate=null;interactable=null;hold=null;fallback=null;queryFrame=-1;}
 internal static void Input()
 {
  if(!ControllerTransport.Left.Grip)consumedLeft=false;if(!ControllerTransport.Right.Grip)consumedRight=false;
  if((pending||interacting)&&(!(left?ControllerTransport.Left:ControllerTransport.Right).Grip||!Probe.Focused||(pending&&Time.frameCount-requestedFrame>3)))Reset();
 }
 internal static void Queue(InteractionManager manager,bool useLeft,GameObject regrip=null)
 {
  if(MotionShovel.OwnsGrip(useLeft)||PhysicalHeldItems.OwnsGrip(useLeft)||WorldPress.OwnsGrip(useLeft)||pending||interacting||(useLeft?consumedLeft:consumedRight)||!manager.IsInteractionEnabled||manager.skatingSuppressed)return;
  owner=manager;left=useLeft;pending=true;requestedFrame=Time.frameCount;pickup=null;fallback=regrip;
 }
 internal static void Queried(InteractionManager manager,bool controllerAim,bool useLeft)
 {if(controllerAim&&manager._IsOwner_k__BackingField){queryFrame=Time.frameCount;queryLeft=useLeft;}}
 private static void AfterUpdate(InteractionManager __instance)
 {
  if(owner==null||__instance.Pointer!=owner.Pointer)return;
  Input();if(owner==null)return;
  var hand=left?ControllerTransport.Left:ControllerTransport.Right;
  if(!StereoTrial.GameplayActive||!Probe.Focused||!hand.Tracked||!owner.IsInteractionEnabled||owner.skatingSuppressed||MotionInput.WheelOpen||MotionInput.PhotoMode||ImmersiveHud.InputOwned||MotionInput.ConversationActive)
  {Reset();return;}
  // Adopt only the exact native candidate after the native consumer/hold action,
  // never an unrelated inventory switch.
  if(interacting&&pickup!=null&&owner.heldItems.Contains(pickup))
  {ObjectGrabs.AdoptDistance(owner,pickup,left);Reset();return;}
  if(interacting&&pickup==null&&owner.activeHoldInteractable==null)Reset();
 }
 private static bool BeforeInteraction(InteractionManager __instance)
 {
  if(pending&&WorldPress.OwnsGrip(left)){Reset();return true;}
  if(interacting&&owner!=null&&__instance.Pointer==owner.Pointer&&pickup!=null&&owner.heldItems.Contains(pickup))
  {ObjectGrabs.AdoptDistance(owner,pickup,left);Reset();return true;}
  if(interacting&&owner!=null&&__instance.Pointer==owner.Pointer)
  {
   bool samePickup=candidate!=null&&owner.currentHoldable!=null&&owner.currentHoldable.Pointer==candidate.Pointer;
   bool sameInteraction=interactable!=null&&owner.currentInteractable!=null&&owner.currentInteractable.Pointer==interactable.Pointer;
   bool sameHold=hold!=null&&owner.activeHoldInteractable!=null&&InteractionManager.SharesHoldGroup(hold,owner.activeHoldInteractable);
   if(!samePickup&&!sameInteraction&&!sameHold)Reset();
  }
  if(!pending||owner==null||__instance.Pointer!=owner.Pointer)return true;
  Input();if(!pending||owner==null)return true;
  if(!StereoTrial.GameplayActive||!Probe.Focused||!owner.IsInteractionEnabled||owner.skatingSuppressed||MotionInput.WheelOpen||MotionInput.PhotoMode||ImmersiveHud.InputOwned||MotionInput.ConversationActive)
  {Reset();return true;}
  if(queryFrame!=Time.frameCount||queryLeft!=left)return true;
  pending=false;
  if(KitchenHands.TryDistance(left)){if(left)consumedLeft=true;else consumedRight=true;return false;}
  bool canInteract=owner.currentInteractable!=null&&owner.currentInteractable.CanInteract();
  bool canPickup=owner.currentHoldable!=null&&owner.currentHoldable.CanPickup();
  if(!canInteract&&!canPickup)
  {
   var manager=owner;var item=fallback;bool hand=left;Reset();
   if(item!=null)ObjectGrabs.Regrip(manager,item,hand);
   return true;
  }
  interactable=canInteract?owner.currentInteractable:null;hold=interactable?.TryCast<IHoldInteractable>();
  candidate=canPickup?owner.currentHoldable:null;pickup=candidate?.GetGameObject();interacting=true;
  if(left)consumedLeft=true;else consumedRight=true;
  if(pickup==null)MotionHaptics.Pulse(left,.16f,.025f);
  // Native Update calls this after ray/sphere/selection and clears its input
  // flags at the end. Stage X's native flag here, then let the existing consumer
  // execute once in that same Update. No delayed synthetic press can leak into
  // the following frame or a different target/hand.
  owner.HandleInteractPressed();
  return true;
 }
#if IKAG_DIAGNOSTICS
 internal static bool Pending=>pending;
 internal static int QueryFrame=>queryFrame;
#endif
}
