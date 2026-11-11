using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Native shots and their local projectile spawn must see the same held pose
// as the stereo draw. The simulation/interpolation pose is restored afterward.
internal static class MotionCheeseGun
{
    internal struct Scope { internal Transform Root;internal Vector3 Position;internal Quaternion Rotation; }
    internal static void Install()
    {
        var harmony=new Harmony("iknowaguyvr.cheese-gun-pose");
        foreach(var method in new[]{"Fire","SpawnProjectileLocal"})
            harmony.Patch(typeof(CheeseGun).GetMethod(method),
                prefix:new HarmonyMethod(typeof(MotionCheeseGun).GetMethod(nameof(Begin),BindingFlags.Static|BindingFlags.NonPublic)),
                finalizer:new HarmonyMethod(typeof(MotionCheeseGun).GetMethod(nameof(End),BindingFlags.Static|BindingFlags.NonPublic)));
    }
    private static void Begin(CheeseGun __instance,out Scope __state)
    {
        __state=default;
        if(!MotionInput.Enabled||!StereoTrial.GameplayActive)return;
        var owner=InteractionManager.LocalInstance;
        if(owner==null||!owner._IsOwner_k__BackingField||owner.heldItems==null)return;
        int index=owner.heldItems.IndexOf(__instance.gameObject);
        if(index<0||!(ObjectGrabs.LeftHand?ControllerTransport.Left:ControllerTransport.Right).Tracked||
            !MotionHeldItems.TryAnchor(owner,out var position,out var basis,out var relative))return;
        var root=__instance.transform;
        __state=new Scope{Root=root,Position=root.localPosition,Rotation=root.localRotation};
        root.SetPositionAndRotation(position+basis*(Vector3.up*(index*owner.stackYOffset)),basis*relative);
    }
    private static void End(Scope __state)
    {if(__state.Root!=null)__state.Root.SetLocalPositionAndRotation(__state.Position,__state.Rotation);}
}
