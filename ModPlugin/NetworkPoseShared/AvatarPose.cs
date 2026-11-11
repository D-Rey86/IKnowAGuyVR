using System;
using UnityEngine;
using Unity.Netcode;
using NVector=System.Numerics.Vector3;
using NQuaternion=System.Numerics.Quaternion;

namespace IKnowAGuyVR.Networking;

internal sealed class AvatarPose
{
    internal readonly Transform[] Bones=new Transform[PoseProtocol.BoneCount];
    internal readonly Animator Animator;
    internal readonly NetworkObject Player;
    internal readonly InteractionManager Interaction;
    internal readonly ulong Mask,Rig;
    private readonly BonePose[] saved=new BonePose[PoseProtocol.BoneCount];
    private readonly SkinnedMeshRenderer[] skins;private readonly bool[] skinFlags;
    private readonly MeshRenderer[] rigid;private readonly bool[] gpuFlags;
    private bool applied,streaming;
    internal AvatarPose(NetworkObject player,Animator animator,InteractionManager interaction)
    {
        Player=player;Animator=animator;Interaction=interaction;
        ulong mask=0,hash=14695981039346656037UL;
        for(int i=0;i<Bones.Length;i++)
        {
            var bone=animator.GetBoneTransform((HumanBodyBones)i);Bones[i]=bone;if(bone==null)continue;
            mask|=1UL<<i;hash=(hash^(uint)i)*1099511628211UL;
            // Actual hierarchy identity, cached once. Different rigs fall back
            // to native animation instead of applying guessed bone transforms.
            for(var t=bone;t!=null&&t!=animator.transform;t=t.parent)
            {foreach(char c in t.name)hash=(hash^c)*1099511628211UL;hash=(hash^255)*1099511628211UL;}
        }
        Mask=mask;Rig=hash;
        var native=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);skins=new SkinnedMeshRenderer[native.Length];skinFlags=new bool[native.Length];
        for(int i=0;i<skins.Length;i++)skins[i]=native[i];
        var meshes=animator.GetComponentsInChildren<MeshRenderer>(true);rigid=new MeshRenderer[meshes.Length];gpuFlags=new bool[meshes.Length];
        for(int i=0;i<rigid.Length;i++)rigid[i]=meshes[i];
    }
    // Facial features are rigid meshes parented to the animated skeleton.
    // Opt them out before the instance upload, just like streamed held props.
    // Otherwise their GPU transforms remain at the native, pre-VR head pose.
    internal void SetStreaming(bool value)
    {
        if(streaming==value)return;
        if(value){for(int i=0;i<rigid.Length;i++)if(rigid[i]!=null){gpuFlags[i]=rigid[i].allowGPUDrivenRendering;rigid[i].allowGPUDrivenRendering=false;}}
        else{Restore();for(int i=0;i<rigid.Length;i++)if(rigid[i]!=null)rigid[i].allowGPUDrivenRendering=gpuFlags[i];}
        streaming=value;
    }
    internal bool Valid=>Player!=null&&Player.IsSpawned&&Animator!=null&&Animator.enabled&&Animator.isHuman&&Animator.gameObject.activeInHierarchy;
    internal bool Capture(PoseFrame frame,Transform hiddenHead,Vector3 visibleHeadScale)
    {
        if(!Valid)return false;frame.Object=Player.NetworkObjectId;frame.Rig=Rig;frame.Mask=Mask;
        // First-person head hiding is also performed by the native game, so
        // even the pre-VR saved head scale can be zero. Head scale is not a
        // tracked pose: use a neutral wire value and preserve the receiver's
        // own native head scale below. Never hide another player's head.
        for(int i=0;i<Bones.Length;i++)if(Bones[i]!=null){var pose=Read(Bones[i]);if(i==(int)HumanBodyBones.Head)pose.Scale=NVector.One;if(!PoseProtocol.Valid(pose))return false;frame.Bones[i]=pose;}
        return true;
    }
    internal void Apply(PoseFrame a,PoseFrame b,float t)
    {
        if(applied||!Valid||a.Rig!=Rig||b.Rig!=Rig||a.Mask!=Mask||b.Mask!=Mask)return;
        for(int i=0;i<Bones.Length;i++)if(Bones[i]!=null)saved[i]=Read(Bones[i]);
        for(int i=0;i<skins.Length;i++)if(skins[i]!=null)skinFlags[i]=skins[i].forceMatrixRecalculationPerRender;
        applied=true;
        for(int i=0;i<skins.Length;i++)if(skins[i]!=null)skins[i].forceMatrixRecalculationPerRender=true;
        for(int i=0;i<Bones.Length;i++)if(Bones[i]!=null)
        {var pose=BonePose.Blend(a.Bones[i],b.Bones[i],t);if(i==(int)HumanBodyBones.Head)pose.Scale=saved[i].Scale;Write(Bones[i],pose);}
    }
    internal void Restore()
    {
        if(!applied)return;
        for(int i=0;i<Bones.Length;i++)if(Bones[i]!=null)Write(Bones[i],saved[i]);
        for(int i=0;i<skins.Length;i++)if(skins[i]!=null)skins[i].forceMatrixRecalculationPerRender=skinFlags[i];
        applied=false;
    }
    internal static BonePose Read(Transform t)=>new(){Position=ToN(t.localPosition),Rotation=ToN(t.localRotation),Scale=ToN(t.localScale)};
    internal static void Write(Transform t,in BonePose pose){t.SetLocalPositionAndRotation(ToU(pose.Position),ToU(pose.Rotation));t.localScale=ToU(pose.Scale);}
    internal static NVector ToN(Vector3 v)=>new(v.x,v.y,v.z);
    internal static NQuaternion ToN(Quaternion q)=>new(q.x,q.y,q.z,q.w);
    internal static Vector3 ToU(NVector v)=>new(v.X,v.Y,v.Z);
    internal static Quaternion ToU(NQuaternion q)=>new(q.X,q.Y,q.Z,q.W);
}

internal sealed class RemoteHeldPose
{
    internal readonly ulong Id;
    internal readonly GameObject Item;
    internal readonly Transform Transform;
    internal readonly NetworkObject Network;
    internal readonly HoldableBase Holdable;
    internal readonly Pizza Pizza;
    internal readonly PizzaBox Box;
    private readonly MeshRenderer[] meshes;private readonly bool[] gpu;
    private BonePose saved;private bool applied,enabled;
    internal RemoteHeldPose(NetworkObject net)
    {
        Network=net;Id=net.NetworkObjectId;Item=net.gameObject;Transform=net.transform;
        Holdable=Item.GetComponent<HoldableBase>();Pizza=Item.GetComponent<Pizza>();Box=Item.GetComponent<PizzaBox>();
        var native=Item.GetComponentsInChildren<MeshRenderer>(true);meshes=new MeshRenderer[native.Length];gpu=new bool[native.Length];
        for(int i=0;i<meshes.Length;i++)meshes[i]=native[i];
    }
    // Called in Update, before GPU Resident Drawer builds instance data.
    internal void Enable()
    {if(enabled)return;for(int i=0;i<meshes.Length;i++)if(meshes[i]!=null){gpu[i]=meshes[i].allowGPUDrivenRendering;meshes[i].allowGPUDrivenRendering=false;}enabled=true;}
    internal void Disable()
    {Restore();if(!enabled)return;for(int i=0;i<meshes.Length;i++)if(meshes[i]!=null)meshes[i].allowGPUDrivenRendering=gpu[i];enabled=false;}
    internal void Apply(Transform player,in BonePose pose)
    {
        if(applied||!enabled||Transform==null||!Item.activeInHierarchy)return;
        saved=AvatarPose.Read(Transform);applied=true;
        Transform.SetPositionAndRotation(player.TransformPoint(AvatarPose.ToU(pose.Position)),player.rotation*AvatarPose.ToU(pose.Rotation));
    }
    internal void Restore(){if(!applied)return;if(Transform!=null)AvatarPose.Write(Transform,saved);applied=false;}
}
