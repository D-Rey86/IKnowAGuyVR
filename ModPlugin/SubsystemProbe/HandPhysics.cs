using System.Collections.Generic;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

// Passive palm contacts, separate from render-only bones and native punch damage.
// LateUpdate caches the existing tracked poses; FixedUpdate only moves kinematic
// proxies. Neither path changes the camera, input sampling, or held-item physics.
internal static class HandPhysics
{
    private sealed class Palm
    {
        internal GameObject Object;
        internal Rigidbody Body;
        internal CapsuleCollider Collider;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal bool Valid,Placed;
        internal void Disable(){if(Collider!=null)Collider.enabled=false;Valid=Placed=false;}
    }
    private sealed class Held
    {internal GameObject Item;internal Collider[] Colliders;}
    private static readonly Palm left=new(),right=new();
    private static readonly List<Held> held=new();
    private static InteractionManager owner;
    private static float sampledAt;
    private static int scene;

    internal static void Sample()
    {
        var current=InteractionManager.LocalInstance;
        bool live=MotionInput.Enabled&&Probe.Focused&&StereoTrial.GameplayActive&&current!=null&&current._IsOwner_k__BackingField;
        if(!live){Disable();if(owner!=null||left.Object!=null)Reset();return;}
        if(owner==null||owner.Pointer!=current.Pointer){Reset();owner=current;}
        int currentScene=current.gameObject.scene.handle;
        if(scene!=currentScene){Disable();scene=currentScene;}
        if(Cursor.lockState!=CursorLockMode.Locked||MachineInteractionBase.IsLocalPlayerInMachineMode||ImmersiveHud.InputOwned||MotionInput.WheelOpen||MotionInput.PhotoMode)
        {Disable();return;}
        if(left.Object==null&&!Create(left,true))return;
        if(right.Object==null&&!Create(right,false))return;
        RefreshHeld();
        bool holding=current.IsHoldingItem();
        Cache(left,ControllerTransport.Left,true,holding&&ObjectGrabs.LeftHand);
        Cache(right,ControllerTransport.Right,false,holding&&!ObjectGrabs.LeftHand);
        sampledAt=Time.unscaledTime;
    }
    private static bool Create(Palm palm,bool useLeft)
    {
        if(!MotionBody.TryPalmGeometry(owner,useLeft,out _,out _,out _,out _))return false;
        palm.Object=new GameObject(useLeft?"IKAG_LeftPalmContact":"IKAG_RightPalmContact");
        Object.DontDestroyOnLoad(palm.Object);
        // The measured native interaction mask includes Ignore Raycast (2),
        // but excludes HeldItem (10). Reuse that exclusion; no global layer edits.
        palm.Object.layer=10;
        palm.Body=palm.Object.AddComponent<Rigidbody>();
        palm.Body.isKinematic=true;palm.Body.useGravity=false;
        palm.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        palm.Collider=palm.Object.AddComponent<CapsuleCollider>();
        palm.Collider.enabled=false;
        // Existing dynamic props and hinged doors. Exclude player/ragdoll,
        // held-item, preview, traffic and specialized workstation layers.
        int contactLayers=(1<<0)|(1<<6)|(1<<7)|(1<<22);
        palm.Collider.excludeLayers=~contactLayers;
        palm.Collider.includeLayers=contactLayers;
        return true;
    }
    private static void Cache(Palm palm,ControllerTransport.Hand hand,bool useLeft,bool occupied)
    {
        if(occupied||!hand.Tracked||PhysicalHeldItems.Attached(useLeft)||MotionShovel.OwnsGrip(useLeft)||KitchenHands.Pose(useLeft,out _,out _)||
           !MotionBody.TryPalmGeometry(owner,useLeft,out var center,out var axis,out float length,out float radius)||
           !StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q))
        {palm.Disable();return;}
        // Actual avatar wrist/knuckle landmarks, in the same grip frame as IK.
        if(palm.Collider.radius!=radius)palm.Collider.radius=radius;
        float height=length+2*radius;if(palm.Collider.height!=height)palm.Collider.height=height;
        palm.Position=p+q*center;
        palm.Rotation=q*Quaternion.FromToRotation(Vector3.up,axis);
        palm.Valid=true;
    }
    private static void RefreshHeld()
    {
        var items=owner.heldItems;
        for(int i=held.Count-1;i>=0;--i)
            if(held[i].Item==null||items==null||!items.Contains(held[i].Item))
            {Ignore(held[i],false);held.RemoveAt(i);}
        if(items==null)return;
        for(int i=0;i<items.Count;++i)
        {
            var item=items[i];if(item==null)continue;
            bool found=false;for(int j=0;j<held.Count;++j)if(held[j].Item==item){found=true;break;}
            if(found)continue;
            var colliders=item.GetComponentsInChildren<Collider>(true);
            var entry=new Held{Item=item,Colliders=new Collider[colliders.Length]};
            for(int j=0;j<colliders.Length;++j)entry.Colliders[j]=colliders[j];
            held.Add(entry);Ignore(entry,true);
        }
    }
    private static void Ignore(Held item,bool value)
    {
        foreach(var c in item.Colliders)if(c!=null)
        {if(left.Collider!=null)Physics.IgnoreCollision(left.Collider,c,value);if(right.Collider!=null)Physics.IgnoreCollision(right.Collider,c,value);}
    }
    internal static void FixedStep()
    {
        if(!MotionInput.Enabled||!Probe.Focused||!StereoTrial.GameplayActive||Time.unscaledTime-sampledAt>.15f)
        {Disable();return;}
        Move(left);Move(right);
    }
    private static void Move(Palm palm)
    {
        if(!palm.Valid||palm.Body==null){palm.Disable();return;}
        var delta=palm.Position-palm.Body.position;
        // Reacquisition/teleport is a non-contact placement, never a fast sweep
        // through the room. Ordinary contact speed is bounded to avoid launches.
        if(!palm.Placed||delta.sqrMagnitude>.09f||Quaternion.Angle(palm.Body.rotation,palm.Rotation)>120)
        {
            palm.Collider.enabled=false;palm.Body.position=palm.Position;palm.Body.rotation=palm.Rotation;
            palm.Placed=true;return;
        }
        if(!palm.Collider.enabled)
        {
            palm.Collider.enabled=true;
            foreach(var item in held)foreach(var c in item.Colliders)if(c!=null)Physics.IgnoreCollision(palm.Collider,c,true);
        }
        palm.Body.MovePosition(palm.Body.position+Vector3.ClampMagnitude(delta,4*Time.fixedDeltaTime));
        palm.Body.MoveRotation(Quaternion.RotateTowards(palm.Body.rotation,palm.Rotation,720*Time.fixedDeltaTime));
    }
    private static void Disable(){left.Disable();right.Disable();}
    internal static void Stop()=>Reset();
    private static void Reset()
    {
        Disable();held.Clear();
        if(left.Object!=null)Object.Destroy(left.Object);if(right.Object!=null)Object.Destroy(right.Object);
        left.Object=right.Object=null;left.Body=right.Body=null;left.Collider=right.Collider=null;owner=null;
    }
#if IKAG_DIAGNOSTICS
    internal static Rigidbody Body(bool useLeft)=>(useLeft?left:right).Body;
    internal static bool ContactEnabled(bool useLeft)=>(useLeft?left:right).Collider?.enabled==true;
#endif
}
