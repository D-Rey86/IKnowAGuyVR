using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Native inventory/ownership remains authoritative. This changes acquisition and
// the active hand, not the game's single selected held-item group.
internal static class ObjectGrabs
{
 private sealed class Target {internal HoldableBase Holdable;internal GameObject Item;internal Transform Transform;internal Collider[] Colliders;}
 private static readonly Dictionary<IntPtr,Target> targets=new();
 private static InteractionManager owner;private static GameObject held;
 private static bool discovered,left,latched,previousLeft,previousRight,armedLeft,armedRight,keepOnRelease;
 private static double pressedLeft=double.NaN,pressedRight=double.NaN;
 private static int beltDrawSlot = -1;
 private static Vector3 offset;private static Quaternion rotation;
 private static Vector3 lastPosition;private static Quaternion lastBasis;private static bool hasPose;
 private static int frame=-1;
 internal static bool LeftHand=>held!=null?left:VrControls.LeftHanded;
 internal static int PoseRevision{get;private set;}
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.object-grabs");
  h.Patch(typeof(HoldableBase).GetMethod("OnNetworkSpawn"),postfix:Hook(nameof(Spawned)));
  h.Patch(typeof(HoldableBase).GetMethod("OnNetworkDespawn"),prefix:Hook(nameof(Despawned)));
 }
 private static HarmonyMethod Hook(string name)=>new(typeof(ObjectGrabs).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
 private static void Spawned(HoldableBase __instance)=>Register(__instance);
 private static void Despawned(HoldableBase __instance)=>targets.Remove(__instance.Pointer);
 private static void Register(HoldableBase h)
 {
  if(h==null||targets.ContainsKey(h.Pointer)||!h.IsSpawned)return;
  var all=h.GetComponentsInChildren<Collider>(true);var colliders=new List<Collider>(all.Length);
  foreach(var c in all){var containing=c.GetComponentInParent<HoldableBase>(true);var mesh=c.TryCast<MeshCollider>();if(containing!=null&&containing.Pointer==h.Pointer&&(mesh==null||mesh.convex)&&!c.isTrigger)colliders.Add(c);}
  targets.Add(h.Pointer,new Target{Holdable=h,Item=h.gameObject,Transform=h.transform,Colliders=colliders.ToArray()});
 }
 internal static void Reset()
 {WorldPress.Reset();PhysicalHeldItems.Reset();MotionShovel.Reset();targets.Clear();ReleasePlacement.Reset();DistanceGrips.Reset();owner=null;held=null;discovered=latched=armedLeft=armedRight=previousLeft=previousRight=hasPose=keepOnRelease=false;pressedLeft=pressedRight=double.NaN;beltDrawSlot=-1;frame=-1;}
 internal static bool TryAnchor(InteractionManager manager,out Vector3 position,out Quaternion basis,out Quaternion relative)
 {
  position=default;basis=relative=Quaternion.identity;
  if(held==null||owner==null||owner.Pointer!=manager.Pointer||manager.heldItems==null||!manager.heldItems.Contains(held))return false;
  var hand=left?ControllerTransport.Left:ControllerTransport.Right;
  relative=rotation;
  if(!Probe.Focused||!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out basis)){position=lastPosition;basis=lastBasis;return hasPose;}
  position=p+basis*offset;lastPosition=position;lastBasis=basis;hasPose=true;return true;
 }
 internal static void Suspend(){DistanceGrips.Reset();BeltModels.Highlight(-1,-1);latched=armedLeft=armedRight=false;pressedLeft=pressedRight=double.NaN;previousLeft=ControllerTransport.Left.Grip;previousRight=ControllerTransport.Right.Grip;}
 internal static void Tick(InteractionManager manager,bool enabled,bool placement,Core.Input.InputManager input)
 {
  if(frame==Time.frameCount)return;frame=Time.frameCount;
  if(!StereoTrial.GameplayActive){Reset();return;}
  if(manager==null)return;
  if(owner==null||owner.Pointer!=manager.Pointer){owner=manager;held=null;latched=false;}
  if(!discovered){discovered=true;foreach(var h in UnityEngine.Object.FindObjectsOfType<HoldableBase>(true))Register(h);}
  var l=ControllerTransport.Left;var r=ControllerTransport.Right;
  if(held!=null&&!manager.heldItems.Contains(held)){held=null;latched=false;}
  if(!enabled||!Probe.Focused||!l.Tracked||!r.Tracked||KitchenHands.Acquiring||MachineInteractionBase.IsLocalPlayerInMachineMode){Suspend();return;}
  if(!l.Grip)armedLeft=true;if(!r.Grip)armedRight=true;
  bool pressL=l.Grip&&!previousLeft&&armedLeft,pressR=r.Grip&&!previousRight&&armedRight;
  if(pressL)pressedLeft=Time.realtimeSinceStartupAsDouble;
  if(pressR)pressedRight=Time.realtimeSinceStartupAsDouble;
  int beltLeft = placement ? -1 : PhysicalBelt.Target(manager, true, l);
  int beltRight = placement ? -1 : PhysicalBelt.Target(manager, false, r);
  var topItem = manager.cachedTopItem;
  bool itemInLeft = LeftHand;
  // Suppress an immediate release at the draw loop. Moving straight to
  // another loop still counts, even without leaving the belt's reach.
  if (held == null || (itemInLeft ? beltLeft : beltRight) != beltDrawSlot) beltDrawSlot = -1;
  BeltModels.Highlight(
   (previousLeft ? itemInLeft && beltLeft != beltDrawSlot && PhysicalBelt.CanStow(beltLeft, topItem) : PhysicalBelt.CanSelect(beltLeft, topItem)) ? beltLeft : -1,
   (previousRight ? !itemInLeft && beltRight != beltDrawSlot && PhysicalBelt.CanStow(beltRight, topItem) : PhysicalBelt.CanSelect(beltRight, topItem)) ? beltRight : -1);
  // Packing takes priority over transferring the pizza or queuing a second
  // interaction. The confirmation grip never becomes the item's owning hand.
  bool itemLeft=itemInLeft;
  if(!placement&&(itemLeft?pressR:pressL)&&PizzaBoxPlacement.TryPack(owner,owner.cachedTopItem,out var packed))
  {
   DistanceGrips.Reset();
   if(owner.heldItems.Contains(packed.gameObject))AdoptPacked(owner,packed.gameObject,itemLeft);
   if(itemLeft)armedRight=false;else armedLeft=false;
   previousLeft=l.Grip;previousRight=r.Grip;return;
  }
  if((pressL&&GrabBelt(true,beltLeft))||(pressR&&GrabBelt(false,beltRight)))
  {
   previousLeft=l.Grip;previousRight=r.Grip;return;
  }
  // Transfer first, so releasing the old hand in the same frame cannot drop it.
  if(!placement){if(pressL)TryGrab(true,l);if(pressR)TryGrab(false,r);}
  else if(held!=null&&(left?pressL:pressR))latched=true;
  var active=left?l:r;
  if(held!=null&&latched&&!active.Grip)
  {
   double duration=Time.realtimeSinceStartupAsDouble-(left?pressedLeft:pressedRight);
   // A deliberate dart throw keeps its motion-based release. Otherwise a
   // short pickup/regrip retains the item; a sustained grip uses native release.
   if(!placement&&MotionTools.TryReleaseDart(owner,held))
   {
    MotionHaptics.Pulse(left,.16f,.025f);latched=false;
    if(!owner.heldItems.Contains(held))held=null;
   }
   else if(duration>=0&&duration<.25)
   {
    ReleaseToBelt(left?beltLeft:beltRight,held,left);latched=false;
   }
   else if((!placement&&ReleasePlacement.TryShelf(owner,held))||MotionInput.ReleasePreview(input)||(!placement&&ReleaseHandoffs.TryRelease(owner,held)))
   {
    MotionHaptics.Pulse(left,.16f,.025f);latched=false;
    if(!owner.heldItems.Contains(held))held=null;
   }
   else if(ReleaseToBelt(left ? beltLeft : beltRight, held, left)||(keepOnRelease&&beltDrawSlot>=0))latched=false;
   else
   {
   MotionHeldItems.SnapForRelease(owner);
   float force=owner.dropForce,up=owner.dropUpwardForce;
   try{owner.dropForce=owner.dropUpwardForce=0;owner.DropLastItem();}
   finally{owner.dropForce=force;owner.dropUpwardForce=up;}
   if(!owner.heldItems.Contains(held)){MotionHaptics.Pulse(left,.16f,.025f);held=null;latched=false;}
   }
  }
  else
  {
   // Native interaction providers and hotbar selection can supply held items
   // without Attach(). Green-preview release must not depend on that latch.
   // With an explicit hand attachment, only its hand may release the item.
   bool releaseL=previousLeft&&armedLeft&&!l.Grip,releaseR=previousRight&&armedRight&&!r.Grip;
   bool released=held!=null?(left?releaseL:releaseR):releaseL||releaseR;
   if(released&&(MotionInput.ReleasePreview(input)||(!placement&&(held!=null||(VrControls.LeftHanded?releaseL:releaseR))&&ReleaseHandoffs.TryRelease(owner,owner.cachedTopItem))))
   {latched=false;MotionHaptics.Pulse(held!=null?left:releaseL,.16f,.025f);}
   else if (itemInLeft ? releaseL : releaseR)
       ReleaseToBelt(itemInLeft ? beltLeft : beltRight, owner.cachedTopItem, itemInLeft);
  }
  previousLeft=l.Grip;previousRight=r.Grip;
 }
 private static bool ReleaseToBelt(int target, GameObject item, bool useLeft)
 {
  if (target == beltDrawSlot || !PhysicalBelt.TryStow(target, item, out bool stored)) return false;
  latched = false;
  if (stored)
  {
   held = null;
   var items = owner.heldItems;
   if (items.Count > 0)
    keepOnRelease = AdoptDistance(owner, items[items.Count - 1], useLeft, requireGrip: false);
   else
    MotionHaptics.Pulse(useLeft, .16f, .025f);
  }
  return true;
 }
 private static bool GrabBelt(bool useLeft,int slot)
 {
  if(!PhysicalBelt.TrySelect(owner,slot,out var item))return false;
  if(item!=null)
  {
   keepOnRelease = AdoptDistance(owner,item,useLeft);
   beltDrawSlot = keepOnRelease ? slot : -1;
  }
  else if(useLeft)armedLeft=false;else armedRight=false;
  return true;
 }
 private static bool Near(Target target,Vector3 hand,out Vector3 contact)
 {
  contact=default;float best=.12f*.12f;
  if(target.Holdable==null||target.Item==null||!target.Item.activeInHierarchy)return false;
  bool found=false;
  foreach(var c in target.Colliders)
  {
   if(c==null||!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy)continue;
   if(c.bounds.SqrDistance(hand)>best)continue;
   var p=c.ClosestPoint(hand);
   float d=(p-hand).sqrMagnitude;if(d<=best){best=d;contact=p;found=true;}
  }
  return found;
 }
 private static void Attach(GameObject item,bool useLeft,Vector3 p,Quaternion q,Vector3 itemPosition,Quaternion itemRotation)
 {PoseRevision++;DistanceGrips.Reset();held=item;left=useLeft;offset=Quaternion.Inverse(q)*(itemPosition-p);rotation=Quaternion.Inverse(q)*itemRotation;lastPosition=itemPosition;lastBasis=q;hasPose=latched=true;keepOnRelease=false;beltDrawSlot=-1;MotionHaptics.Pulse(useLeft);}
 internal static void AdoptPacked(InteractionManager manager,GameObject item,bool useLeft)
 {
  var hand=useLeft?ControllerTransport.Left:ControllerTransport.Right;
  if(!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q)||!StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out _,out var aim))return;
  var config=manager.GetHoldingConfiguration(item);
  Attach(item,useLeft,p,q,p,aim*Quaternion.Euler(config.rotation));latched=hand.Grip;
 }
 private static void TryGrab(bool useLeft,ControllerTransport.Hand hand)
 {
  if(MotionShovel.OwnsGrip(useLeft)||PhysicalHeldItems.OwnsGrip(useLeft))return;
#if IKAG_DIAGNOSTICS
  if(Environment.GetEnvironmentVariable("IKAG_XR_OBJECT_GRAB_AUDIT")!=null)Plugin.Line("GRAB_TRY left="+useLeft+" targets="+targets.Count+" held="+owner.heldItems.Count);
#endif
  if(!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))return;
  if(owner.IsHoldingItem())
  {
   var item=owner.cachedTopItem;if(item==null)return;
   if(!MotionHeldItems.TryAnchor(owner,out var ip,out var iq,out var relative))return;
   var activeHand=LeftHand?ControllerTransport.Left:ControllerTransport.Right;
   if(!StereoTrial.TryTrackingPose(activeHand.Position,activeHand.Rotation,out var activeGrip,out _))return;
   bool close=(p-activeGrip).sqrMagnitude<=.12f*.12f;
   if(close&&held!=null&&left!=useLeft)
   {MotionHaptics.Pulse(left,.16f,.025f);Attach(item,useLeft,p,q,ip,iq*relative);}
   else if(close&&MotionInput.PlacementPreviewActive)Attach(item,useLeft,p,q,ip,iq*relative);
   else DistanceGrips.Queue(owner,useLeft,close?item:null);
   return;
  }
  Target selected=null;float best=float.PositiveInfinity;
  foreach(var t in targets.Values)
  {
   if(t.Holdable==null||!t.Holdable.IsSpawned||!Near(t,p,out var contact)||!t.Holdable.CanPickup())continue;
   float d=(contact-p).sqrMagnitude;if(d<best){best=d;selected=t;}
  }
  if(selected==null){DistanceGrips.Queue(owner,useLeft);return;}
#if IKAG_DIAGNOSTICS
  if(Environment.GetEnvironmentVariable("IKAG_XR_OBJECT_GRAB_AUDIT")!=null)Plugin.Line("GRAB_SELECTED "+selected.Item.name+" distance="+best);
#endif
  var pos=selected.Transform.position;var rot=selected.Transform.rotation;
  if(owner.TryPickupItem(selected.Holdable.Cast<IHoldable>())&&owner.heldItems.Contains(selected.Item))Attach(selected.Item,useLeft,p,q,pos,rot);
 }
 internal static void Regrip(InteractionManager manager,GameObject item,bool useLeft)
 {
  var hand=useLeft?ControllerTransport.Left:ControllerTransport.Right;
  if(!hand.Grip||!hand.Tracked||!manager.heldItems.Contains(item)||!MotionHeldItems.TryAnchor(manager,out var p,out var q,out var relative)||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var grip,out var rotation))return;
  owner=manager;Attach(item,useLeft,grip,rotation,p,q*relative);
 }
 internal static bool AdoptDistance(InteractionManager manager,GameObject item,bool useLeft,bool requireGrip=true)
 {
  var hand=useLeft?ControllerTransport.Left:ControllerTransport.Right;
  if((requireGrip&&!hand.Grip)||!hand.Tracked||!manager.heldItems.Contains(item)||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q)||!StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out _,out var aim))return false;
  owner=manager;var config=manager.GetHoldingConfiguration(item);
  Attach(item,useLeft,p,q,p,aim*Quaternion.Euler(config.rotation));latched=hand.Grip;return true;
 }
#if IKAG_DIAGNOSTICS
 internal static int Registered=>targets.Count;
 internal static bool Grabbed=>held!=null;
 internal static void Trace(GameObject fixture)
 {
  Plugin.Line("GRAB_STATE armed="+armedLeft+"/"+armedRight+" previous="+previousLeft+"/"+previousRight+" owner="+owner?.name+" latched="+latched+" held="+owner?.heldItems.Count+" UI="+ImmersiveHud.InputOwned+" cursor="+Cursor.lockState+" machine="+MachineInteractionBase.IsLocalPlayerInMachineMode+" acquiring="+KitchenHands.Acquiring);
  var h=ControllerTransport.Left;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out _);Plugin.Line("GRAB_HAND p="+p+" tracked="+h.Tracked+" grip="+h.Grip);
  foreach(var t in targets.Values)if(t.Item!=null&&fixture!=null&&t.Item.Pointer==fixture.Pointer){Plugin.Line("GRAB_TARGET can="+t.Holdable.CanPickup()+" active="+t.Item.activeInHierarchy+" spawned="+t.Holdable.IsSpawned+" near="+Near(t,p,out _)+" colliders="+t.Colliders.Length);foreach(var c in t.Colliders)Plugin.Line("GRAB_COLLIDER enabled="+c.enabled+" trigger="+c.isTrigger+" bounds="+c.bounds+" distance="+(c.ClosestPoint(p)-p).magnitude+" mesh="+(c.TryCast<MeshCollider>()!=null));}
 }
#endif
}
