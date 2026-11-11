using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionHeldItems
{
    private struct Visual
    {
        internal GameObject Item;internal Transform Transform;internal Vector3 Position;internal Quaternion Rotation;internal bool Saved;
        internal MeshRenderer[] Meshes;internal bool[] GpuFlags;
        internal PizzaBox Box;internal GameObject Content;internal Pizza ContentPizza;internal Transform ContentTransform;
        internal MeshRenderer[] ContentMeshes;internal bool[] ContentGpuFlags;
        internal Vector3 ContentPosition;internal Quaternion ContentRotation;internal bool ContentSaved;
        private void ClearContent()
        {
            if(ContentMeshes!=null)for(int i=0;i<ContentMeshes.Length;i++)if(ContentMeshes[i]!=null)ContentMeshes[i].allowGPUDrivenRendering=ContentGpuFlags[i];
            Content=null;ContentPizza=null;ContentTransform=null;ContentMeshes=null;ContentGpuFlags=null;
        }
        internal void RefreshContent()
        {
            var next=Box==null?null:Box.wholePizzaInBox?.pizzaObject;
            if(Content==next)return;
            ClearContent();Content=next;if(next==null)return;
            ContentTransform=next.transform;ContentPizza=next.GetComponent<Pizza>();
            var meshes=next.GetComponentsInChildren<MeshRenderer>(true);
            ContentMeshes=new MeshRenderer[meshes.Length];ContentGpuFlags=new bool[meshes.Length];
            for(int i=0;i<meshes.Length;i++){ContentMeshes[i]=meshes[i];ContentGpuFlags[i]=meshes[i].allowGPUDrivenRendering;meshes[i].allowGPUDrivenRendering=false;}
        }
        internal void Clear()
        {
            ClearContent();
            if(Meshes!=null)for(int i=0;i<Meshes.Length;i++)if(Meshes[i]!=null)Meshes[i].allowGPUDrivenRendering=GpuFlags[i];
            this=default;
        }
        internal void Bind(GameObject item)
        {
            Clear();Item=item;if(item==null)return;Transform=item.transform;Box=item.GetComponent<PizzaBox>();
            var meshes=item.GetComponentsInChildren<MeshRenderer>(true);Meshes=new MeshRenderer[meshes.Length];GpuFlags=new bool[meshes.Length];
            for(int i=0;i<meshes.Length;i++){Meshes[i]=meshes[i];GpuFlags[i]=meshes[i].allowGPUDrivenRendering;meshes[i].allowGPUDrivenRendering=false;}
        }
    }
    private static Visual[] visuals=System.Array.Empty<Visual>();private static int visibleCount;
    private static int preparedCount;
    internal static void Reset(){EndRender();for(int i=0;i<preparedCount;i++)visuals[i].Clear();preparedCount=0;}
    private static void Prepare(InteractionManager owner)
    {
        int count=owner.heldItems==null?0:owner.heldItems.Count;
        bool changed=count!=preparedCount;
        if(!changed)for(int i=0;i<count;i++)
        {var item=owner.heldItems[i];if(item==null?visuals[i].Item!=null:visuals[i].Item==null||visuals[i].Item.Pointer!=item.Pointer){changed=true;break;}}
        if(!changed){for(int i=0;i<count;i++)visuals[i].RefreshContent();return;}
        // Restore the whole previous group before binding any new indices. A
        // stack can reorder its roots; otherwise one slot could cache another
        // slot's temporary false flag and restore the wrong value on release.
        for(int i=0;i<preparedCount;i++)visuals[i].Clear();
        if(visuals.Length<count)System.Array.Resize(ref visuals,System.Math.Max(count,4));
        for(int i=0;i<count;i++){visuals[i].Bind(owner.heldItems[i]);visuals[i].RefreshContent();}
        preparedCount=count;
    }
    internal struct Scope { internal Transform Anchor;internal Vector3 Position,ItemOffset;internal Quaternion Rotation,ItemRotation;internal InteractionManager Owner; }
    internal static void Install()
    {
        MotionCheeseGun.Install();
        var h=new Harmony("iknowaguyvr.motion-held-items");
        foreach(string method in new[]{"FixedUpdate","LateUpdate"})
            h.Patch(typeof(InteractionManager).GetMethod(method),prefix:Hook(nameof(Begin)),finalizer:Hook(nameof(End)));
    }
    private static HarmonyMethod Hook(string n)=>new(typeof(MotionHeldItems).GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic));
    private static void Begin(InteractionManager __instance,out Scope __state)
    {
        __state=default;
        if(!__instance._IsOwner_k__BackingField||__instance.holdPosition==null||!StereoTrial.GameplayActive) return;
        // GPU Resident Drawer uploads ordinary MeshRenderer instance transforms
        // before our render-only rigid pose. Opt out only the selected held group,
        // ahead of that upload; restore each renderer's actual flag on deselection.
        // Skinned renderers keep their native path. Cache inactive pooled toppings
        // too, so ingredient activation needs no per-frame hierarchy traversal.
        Prepare(__instance);
        ControllerTransport.Sample();
        if(!TryAnchor(__instance,out var p,out var q,out var relative))return;
        var t=__instance.holdPosition;
        // Native position offsets place models in a flat camera view. Attach the
        // authored item origin directly to grip instead. Preserve its authored
        // forward-axis correction and use the runtime aim orientation, keeping
        // the model's pointing direction aligned with interaction/photography.
        __state=new Scope{Anchor=t,Position=t.localPosition,Rotation=t.localRotation,Owner=__instance,ItemOffset=__instance.targetHoldingPosition,ItemRotation=__instance.targetHoldingRotation};
        __instance.targetHoldingPosition=Vector3.zero;__instance.targetHoldingRotation=relative;
        t.SetPositionAndRotation(p,q);
    }
    private static void End(Scope __state)
    {
        if(__state.Anchor!=null)__state.Anchor.SetLocalPositionAndRotation(__state.Position,__state.Rotation);
        if(__state.Owner!=null){__state.Owner.targetHoldingPosition=__state.ItemOffset;__state.Owner.targetHoldingRotation=__state.ItemRotation;}
    }
    internal static bool TryAnchor(InteractionManager owner,out Vector3 p,out Quaternion q,out Quaternion relative)
    {
        if(NaturalItemGrip.TryPose(owner,out p,out q,out relative))return true;
        if(ObjectGrabs.TryAnchor(owner,out p,out q,out relative))return true;
        var r=VrControls.Main;relative=owner.targetHoldingRotation;
        return r.Tracked&&StereoTrial.TryTrackingPose(r.Position,r.Rotation,out p,out _)&&StereoTrial.TryTrackingPose(r.AimPosition,r.AimRotation,out _,out q);
    }
    internal static void SnapForRelease(InteractionManager owner)
    {
        if(!TryAnchor(owner,out var p,out var q,out var relative))return;
        for(int i=0;i<owner.heldItems.Count;i++)
        {
            var item=owner.heldItems[i];if(item==null)continue;var position=p+q*(Vector3.up*(i*owner.stackYOffset));var rotation=q*relative;
            var rb=i<owner.heldRigidbodies.Count?owner.heldRigidbodies[i]:null;
            if(rb!=null){if(!rb.isKinematic){rb.linearVelocity=Vector3.zero;rb.angularVelocity=Vector3.zero;}rb.position=position;rb.rotation=rotation;}
            item.transform.SetPositionAndRotation(position,rotation);
        }
    }
    internal static bool TryUsePose(InteractionManager owner,out Vector3 position,out Quaternion orientation)
    {
        if(ObjectGrabs.TryAnchor(owner,out position,out var basis,out var relative))
        {orientation=basis*relative*Quaternion.Inverse(owner.targetHoldingRotation);return true;}
        var hand=VrControls.Main;orientation=Quaternion.identity;
        return hand.Tracked&&StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out position,out orientation);
    }
    // Physics interpolation can move the native root again after LateUpdate.
    // Present a rigid pose for the stereo pair, then restore simulation exactly.
    internal static void BeginRender()
    {
        if(!MotionInput.Enabled||!StereoTrial.GameplayActive)return;
        var owner=InteractionManager.LocalInstance;
        if(owner==null||!owner._IsOwner_k__BackingField)return;
        Prepare(owner);
        if(owner.heldItems==null||owner.heldItems.Count==0)return;
        if(!TryAnchor(owner,out var p,out var q,out var relative))return;
        int count=owner.heldItems.Count;visibleCount=count;
#if IKAG_DIAGNOSTICS
        PizzaShelfAudit.Attachment(false);
#endif
        for(int i=0;i<count;i++)
        {
            ref var v=ref visuals[i];var item=owner.heldItems[i];v.Saved=false;
            if(item==null)continue;
            if(!v.Transform.gameObject.activeInHierarchy)continue;
            v.Position=v.Transform.localPosition;v.Rotation=v.Transform.localRotation;v.Saved=true;
            v.Transform.SetPositionAndRotation(p+q*(Vector3.up*(i*owner.stackYOffset)),q*relative);
            // Native Pizza.LateUpdate copies this anchor to a separate network
            // root. It already ran before our render-only box pose. Apply the
            // same authored position/rotation for both eyes, then restore it.
            v.ContentSaved=false;
            if(v.ContentTransform!=null&&v.ContentPizza!=null&&v.ContentPizza.continuousFollow&&v.ContentPizza.followTarget==v.Box.pizzaPlacementPoint)
            {
                v.ContentPosition=v.ContentTransform.localPosition;v.ContentRotation=v.ContentTransform.localRotation;v.ContentSaved=true;
                var target=v.Box.pizzaPlacementPoint;v.ContentTransform.SetPositionAndRotation(target.position,target.rotation);
            }
        }
#if IKAG_DIAGNOSTICS
        ObjectGrabAudit.Locked();
        DistanceGripAudit.Locked();
        PizzaShelfAudit.Attachment(true);
        PizzaBoxAudit.Rendered();
#endif
    }
    internal static void EndRender()
    {
        for(int i=0;i<visibleCount;i++){ref var v=ref visuals[i];
        if(v.ContentSaved&&v.ContentTransform!=null)v.ContentTransform.SetLocalPositionAndRotation(v.ContentPosition,v.ContentRotation);v.ContentSaved=false;
        if(v.Saved&&v.Transform!=null){v.Transform.SetLocalPositionAndRotation(v.Position,v.Rotation);
#if IKAG_DIAGNOSTICS
            ObjectGrabAudit.Restored((v.Transform.localPosition-v.Position).magnitude,Mathf.Abs(1-Mathf.Abs(Quaternion.Dot(v.Transform.localRotation,v.Rotation))));
            DistanceGripAudit.Restored((v.Transform.localPosition-v.Position).magnitude,Mathf.Abs(1-Mathf.Abs(Quaternion.Dot(v.Transform.localRotation,v.Rotation))));
#endif
        }v.Saved=false;}
        visibleCount=0;
    }
}
