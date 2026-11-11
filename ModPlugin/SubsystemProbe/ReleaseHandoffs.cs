using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;

// Release can consume an item only through a known native receiving action.
// Physical delivery uses cached authored colliders; distance delivery reuses
// the existing interaction query. No added raycast or overlap query.
internal static class ReleaseHandoffs
{
 private sealed class Target
 {
  internal NetworkBehaviour Native;internal Collider[] Volumes;
  internal TrashBin Trash;internal Dustbin Dustbin;internal BuildableTrashBin Buildable;
  internal CustomerNPC Customer;internal Courier Courier;
 }
 private static readonly Dictionary<IntPtr,Target> targets=new();
 private static bool discovered;
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.release-handoffs");
  foreach(var type in new[]{typeof(TrashBin),typeof(Dustbin),typeof(BuildableTrashBin),typeof(CustomerNPC),typeof(Courier)})
  {
   h.Patch(type.GetMethod(type==typeof(BuildableTrashBin)?"Start":"OnNetworkSpawn"),postfix:Hook(nameof(Spawned)));
   var despawn=type.GetMethod("OnNetworkDespawn",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
   if(despawn!=null)h.Patch(despawn,prefix:Hook(nameof(Despawned)));
  }
 }
 private static HarmonyMethod Hook(string name)=>new(typeof(ReleaseHandoffs).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));
 private static void Spawned(NetworkBehaviour __instance)=>Register(__instance);
 private static void Despawned(NetworkBehaviour __instance)=>targets.Remove(__instance.Pointer);
 private static void Register(NetworkBehaviour n)
 {
  if(n==null||!n.IsSpawned||targets.ContainsKey(n.Pointer))return;
  var t=new Target{Native=n,Trash=n.TryCast<TrashBin>(),Dustbin=n.TryCast<Dustbin>(),Buildable=n.TryCast<BuildableTrashBin>(),Customer=n.TryCast<CustomerNPC>(),Courier=n.TryCast<Courier>()};
  if(t.Trash==null&&t.Dustbin==null&&t.Buildable==null&&t.Customer==null&&t.Courier==null)return;
  var colliders=n.GetComponentsInChildren<Collider>(true);var volumes=new List<Collider>(colliders.Length);
  foreach(var c in colliders)
  {
   var mesh=c.TryCast<MeshCollider>();if(c.isTrigger||(mesh!=null&&!mesh.convex))continue;
   // Do not include colliders from another network object carried by this NPC.
   var root=c.GetComponentInParent<NetworkBehaviour>();if(root!=null&&root.gameObject!=n.gameObject)continue;
   volumes.Add(c);
  }
  t.Volumes=volumes.ToArray();targets.Add(n.Pointer,t);
 }
 internal static void Reset(){targets.Clear();discovered=false;}
 private static bool Eligible(Target t,PizzaBox pizza,DrinkingCup drink)
 {
  if(t.Native==null||!t.Native.IsSpawned||!t.Native.gameObject.activeInHierarchy)return false;
  if(t.Trash!=null)return t.Trash.CanInteract();
  if(t.Dustbin!=null)return t.Dustbin.CanInteract();
  if(t.Buildable!=null)return !t.Buildable.IsBeingRelocated()&&t.Buildable.CanInteract();
  if(t.Customer!=null)
  {
   var c=t.Customer;
   // OnInteract otherwise falls through to dialogue. Match its delivery gates
   // and call only the dedicated receiving operation below.
   return !c.IsOpenWorldDeliveryNPC&&c.networkHasOrdered.Value&&!c.networkOrderFulfilled.Value&&c.CanInteract()&&
    ((pizza!=null&&!pizza.IsEmpty()&&c.WouldAcceptPizza(pizza))||(drink!=null&&drink.IsFilled()&&c.WouldAcceptBeverage(drink)));
  }
  if(t.Courier!=null&&pizza!=null&&!pizza.IsEmpty()&&t.Courier.CanInteract())
  {var order=t.Courier.GetActiveOrder();return order!=null&&t.Courier.IsPizzaAcceptableForOrder(pizza,order);}
  return false;
 }
 internal static bool TryRelease(InteractionManager owner,GameObject item)
 {
  if(owner==null||item==null||owner.cachedTopItem!=item||!owner.heldItems.Contains(item))return false;
  if(!discovered)
  {
   discovered=true;
   foreach(var n in UnityEngine.Object.FindObjectsOfType<TrashBin>())Register(n);
   foreach(var n in UnityEngine.Object.FindObjectsOfType<Dustbin>())Register(n);
   foreach(var n in UnityEngine.Object.FindObjectsOfType<BuildableTrashBin>())Register(n);
   foreach(var n in UnityEngine.Object.FindObjectsOfType<CustomerNPC>())Register(n);
   foreach(var n in UnityEngine.Object.FindObjectsOfType<Courier>())Register(n);
  }
  var pizza=item.GetComponent<PizzaBox>();var drink=item.GetComponent<DrinkingCup>();
  Target selected=null;float best=.12f*.12f;
  if(MotionHeldItems.TryAnchor(owner,out var p,out var q,out _))
  {
   p+=q*(Vector3.up*(owner.heldItems.IndexOf(item)*owner.stackYOffset));
   foreach(var t in targets.Values)
   {
    if(t.Native==null||!t.Native.gameObject.activeInHierarchy)continue;
    foreach(var c in t.Volumes)
    {
     if(c==null||!c.enabled||!c.gameObject.activeInHierarchy||c.bounds.SqrDistance(p)>best)continue;
     float distance=(c.ClosestPoint(p)-p).sqrMagnitude;
     if(distance<=best&&Eligible(t,pizza,drink)){best=distance;selected=t;break;}
    }
   }
  }
  if(selected==null&&owner.currentInteractable!=null&&targets.TryGetValue(owner.currentInteractable.Pointer,out var aimed)&&Eligible(aimed,pizza,drink))selected=aimed;
  if(selected==null)return false;
  if(selected.Trash!=null)selected.Trash.OnInteract();
  else if(selected.Dustbin!=null)selected.Dustbin.OnInteract();
  else if(selected.Buildable!=null)selected.Buildable.OnInteract();
  else if(selected.Customer!=null)
  {if(pizza!=null)selected.Customer.TryDeliverPizzaDirectly();else selected.Customer.TryDeliverBeverageDirectly();}
  else selected.Courier.OnInteract();
  // Native RPC owns completion. Never issue a second drop on this release.
  return true;
 }
}
