using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Physical shelf placement uses the authored slot volume and native transaction.
// Other green previews retain the game's existing interaction/placement route.
internal static class ReleasePlacement
{
 private sealed class Slot {internal CargoBoxShelfSlot Native;internal CargoBoxShelf Shelf;internal Collider Volume;internal Transform Anchor;}
 private static readonly Dictionary<IntPtr,Slot> slots=new();
 private static bool discovered;
 private static GameObject cachedItem;private static CargoBox box;private static BoxCollider boxCollider;
 internal static void Install()
 {
  PizzaBoxPlacement.Install();
  ReleaseHandoffs.Install();
  var h=new Harmony("iknowaguyvr.release-placement");
  h.Patch(typeof(CargoBoxShelfSlot).GetMethod("Initialize"),postfix:Hook(nameof(Initialized)));
  h.Patch(typeof(CargoBoxShelf).GetMethod("OnNetworkSpawn"),postfix:Hook(nameof(Spawned)));
  h.Patch(typeof(CargoBoxShelf).GetMethod("OnNetworkDespawn"),prefix:Hook(nameof(Despawned)));
 }
 private static HarmonyMethod Hook(string name)=>new(typeof(ReleasePlacement).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));
 private static void Initialized(CargoBoxShelfSlot __instance)=>Register(__instance);
 private static void Spawned(CargoBoxShelf __instance)
 {if(__instance.slots!=null)foreach(var s in __instance.slots)Register(s);}
 private static void Despawned(CargoBoxShelf __instance)
 {if(__instance.slots!=null)foreach(var s in __instance.slots)if(s!=null)slots.Remove(s.Pointer);}
 private static void Register(CargoBoxShelfSlot s)
 {
  if(s==null||s.parentShelf==null||!s.parentShelf.IsSpawned)return;
  slots[s.Pointer]=new Slot{Native=s,Shelf=s.parentShelf,Volume=s.slotCollider,Anchor=s.parentShelf.GetSlotTransform(s.slotIndex)};
 }
 internal static void Reset(){slots.Clear();discovered=false;cachedItem=null;box=null;boxCollider=null;PizzaBoxPlacement.Reset();ReleaseHandoffs.Reset();}
 internal static bool TryShelf(InteractionManager owner,GameObject item)
 {
  if(item==null)return false;
  if(cachedItem==null||cachedItem.Pointer!=item.Pointer)
  {cachedItem=item;box=item.GetComponent<CargoBox>();boxCollider=box==null||box.col==null?null:box.col.TryCast<BoxCollider>();}
  if(box==null||boxCollider==null)return false;
  int heldIndex=owner.heldItems.IndexOf(item);if(heldIndex<0)return false;
  if(!discovered){discovered=true;foreach(var s in UnityEngine.Object.FindObjectsOfType<CargoBoxShelfSlot>(true))Register(s);}
  if(!MotionHeldItems.TryAnchor(owner,out var p,out var q,out var relative))return false;
  p+=q*(Vector3.up*(heldIndex*owner.stackYOffset));
  // Held colliders are disabled. Their bounds/ClosestPoint are unusable, but the
  // authored center can still be transformed into the visible root's pose.
  var root=item.transform;var center=root.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center));
  var visibleCenter=p+(q*relative)*Vector3.Scale(root.lossyScale,center);
  Slot selected=null;float best=float.PositiveInfinity;
  foreach(var s in slots.Values)
  {
   if(s.Native==null||s.Shelf==null||!s.Shelf.IsSpawned||s.Shelf.IsBeingRelocated()||s.Volume==null||!s.Volume.enabled||!s.Volume.gameObject.activeInHierarchy||s.Anchor==null)continue;
   if(s.Volume.bounds.SqrDistance(visibleCenter)>0||(s.Volume.ClosestPoint(visibleCenter)-visibleCenter).sqrMagnitude>1e-8f||!s.Native.CanInteract())continue;
   float d=(p-s.Anchor.position).sqrMagnitude;if(d<best){best=d;selected=s;}
  }
  if(selected==null)return false;
  selected.Shelf.PlaceCargoBoxInSlot(selected.Native.slotIndex,box,owner);
  // A valid native request consumes this release even if an RPC/cooldown delays
  // completion. Never follow a placement request with DropLastItem or retry it.
  return true;
 }
}
