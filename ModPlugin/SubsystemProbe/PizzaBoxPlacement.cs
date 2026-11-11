using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class PizzaBoxPlacement
{
    private sealed class Target{internal PizzaBox Box;internal BoxCollider Volume;internal Transform Anchor;}
    private static readonly Dictionary<IntPtr,Target> targets=new();
    private static bool discovered;
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.pizza-box-release");
        h.Patch(typeof(PizzaBox).GetMethod("OnNetworkSpawn"),postfix:Hook(nameof(Spawned)));
        h.Patch(typeof(PizzaBox).GetMethod("OnNetworkDespawn"),prefix:Hook(nameof(Despawned)));
    }
    private static HarmonyMethod Hook(string n)=>new(typeof(PizzaBoxPlacement).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static));
    private static void Spawned(PizzaBox __instance)=>Register(__instance);
    private static void Despawned(PizzaBox __instance)=>targets.Remove(__instance.Pointer);
    private static void Register(PizzaBox b)
    {if(b!=null&&b.IsSpawned)targets[b.Pointer]=new Target{Box=b,Volume=b.col?.TryCast<BoxCollider>(),Anchor=b.pizzaPlacementPoint};}
    internal static void Reset(){targets.Clear();discovered=false;}
    private static bool Eligible(PizzaBox b,Pizza p)=>b!=null&&b.IsSpawned&&b.gameObject.activeInHierarchy&&b.CanPack(p)&&b.CanInteract();
    internal static bool TryPack(InteractionManager owner,GameObject item,out PizzaBox packed)
    {
        packed=null;
        if(owner==null||item==null||owner.cachedTopItem!=item)return false;
        var pizza=item.GetComponent<Pizza>();if(pizza==null)return false;
        if(!MotionHeldItems.TryAnchor(owner,out var p,out var q,out var relative))return false;
        int index=owner.heldItems.IndexOf(item);if(index<0)return false;
        p+=q*(Vector3.up*(index*owner.stackYOffset));
        // Geometry is resolved only on the opposite-grip press.
        if(!discovered){discovered=true;foreach(var b in UnityEngine.Object.FindObjectsOfType<PizzaBox>())Register(b);}
        Target selected=null;float best=float.PositiveInfinity;
        foreach(var t in targets.Values)
        {
            if(t.Box==null||t.Volume==null||t.Anchor==null||!t.Volume.enabled||!t.Volume.gameObject.activeInHierarchy)continue;
            var local=t.Volume.transform.InverseTransformPoint(p)-t.Volume.center;var half=t.Volume.size*.5f;
            // Inside the authored box footprint, within the existing 12 cm hand
            // reach of its packing plane. No new overlap/raycast is required.
            if(Mathf.Abs(local.x)>half.x||Mathf.Abs(local.z)>half.z)continue;
            float height=Vector3.Dot(p-t.Anchor.position,t.Anchor.up);
            if(height<-.12f||height>.12f||!Eligible(t.Box,pizza))continue;
            float d=(p-t.Anchor.position).sqrMagnitude;if(d<best){best=d;selected=t;}
        }
        PizzaBox target=selected?.Box;
        if(target==null)
        {
            // Reuse the native interaction result for the distance option.
            target=owner.currentInteractable?.TryCast<PizzaBox>();
            if(!Eligible(target,pizza))return false;
        }
        // Native packing removes the pizza and immediately attempts local pickup
        // of this box. Network authority and all recipe/occupancy checks stay native.
        target.OnInteract();packed=target;return true;
    }
}
