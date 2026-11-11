using System;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Rest-rig neck anchoring follows EnshroudedVR's body solver. Its 0.13/0.10 m
// eye-to-neck pivot was fitted to the user's headset motion there. This game's
// bind pose supplies every body offset; no camera/player/collision transform moves.
internal sealed unsafe class TrackedBodyAnchor
{
    private sealed class Joint
    {
        internal Transform Transform;
        internal Vector3 RestPosition,SavedPosition;
        internal Quaternion RestRotation,SavedRotation;
        internal Joint(Transform t){Transform=t;}
        internal void Save(){SavedPosition=Transform.localPosition;SavedRotation=Transform.localRotation;}
        internal void Restore()=>Transform.SetLocalPositionAndRotation(SavedPosition,SavedRotation);
    }
    private readonly Joint[] joints;
    private readonly Transform root;
    private readonly Vector3 neckToHead;
    private Vector3 savedHipScale;
    private Quaternion yaw;
    private bool saved;
    internal readonly float Scale;
    internal static readonly Vector3 EyeFromPivot=new(0,.13f,.10f);
    internal Quaternion Yaw=>yaw;
    internal Quaternion NativeYaw=>Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward,Vector3.up),Vector3.up);
    internal Vector3 Pivot {get;private set;}
    internal Vector3 DesiredHead {get;private set;}
    internal float PelvisDrop {get;private set;}

    internal TrackedBodyAnchor(Animator a,SkinnedMeshRenderer[] skins,Transform camera)
    {
        var names=new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Neck,HumanBodyBones.Head,HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder};
        joints=new Joint[names.Length];
        for(int i=0;i<names.Length;++i){var t=a.GetBoneTransform(names[i]);if(t==null)throw new InvalidOperationException("Body anchor bone missing: "+names[i]);joints[i]=new Joint(t);}
        root=joints[0].Transform.parent;
        if(root==null||Vector3.Dot(root.up,Vector3.up)<.999f)throw new InvalidOperationException("Body anchor requires the measured upright skeleton root");
        var rootScale=root.lossyScale;
        if(rootScale.x<=0||Math.Abs(rootScale.x-rootScale.y)>.001f||Math.Abs(rootScale.x-rootScale.z)>.001f)throw new InvalidOperationException("Nonuniform skeleton scale");
        bool bound=false;
        foreach(var skin in skins)
        {
            if(skin==null||skin.sharedMesh==null)continue;
            var bones=skin.bones;var mesh=skin.sharedMesh;
            if(bones==null||bones.Length!=mesh.bindposeCount)continue;
            int rootIndex=-1;var indices=new int[joints.Length];
            for(int j=0;j<indices.Length;++j)indices[j]=-1;
            for(int b=0;b<bones.Length;++b)
            {
                var bone=bones[b];if(bone==null)continue;
                if(bone.Pointer==root.Pointer)rootIndex=b;
                for(int j=0;j<joints.Length;++j)if(bone.Pointer==joints[j].Transform.Pointer)indices[j]=b;
            }
            bool complete=rootIndex>=0;for(int j=0;j<indices.Length;++j)complete&=indices[j]>=0;
            if(!complete)continue;
            // This native-array accessor is read-only and avoids the target's
            // reconstructed BlittableArrayWrapper array getter (unsafe here).
            var pointer=mesh.GetBindposesArray();if(pointer==IntPtr.Zero)continue;
            var bind=(Matrix4x4*)pointer;
            for(int j=0;j<joints.Length;++j)
            {
                var m=bind[rootIndex]*bind[indices[j]].inverse;
                var p=new Vector3(m.m03,m.m13,m.m23)*rootScale.y;
                var forward=new Vector3(m.m02,m.m12,m.m22);var up=new Vector3(m.m01,m.m11,m.m21);
                if(!Finite(p)||forward.sqrMagnitude<.5f||up.sqrMagnitude<.5f)throw new InvalidOperationException("Invalid measured body bind pose");
                joints[j].RestPosition=p;joints[j].RestRotation=Quaternion.LookRotation(forward,up);
            }
            bound=true;break;
        }
        if(!bound)throw new InvalidOperationException("No native skin binds the complete upper-body chain");
        float height=camera.position.y-root.position.y-EyeFromPivot.y;
        Scale=height/joints[3].RestPosition.y;
        if(!float.IsFinite(Scale)||Scale<.25f||Scale>4)throw new InvalidOperationException("Invalid camera/neck standing-height datum");
        neckToHead=(joints[4].RestPosition-joints[3].RestPosition)*Scale;
        yaw=NativeYaw;
        Plugin.Line($"MOTION_BODY_ANCHOR_BOUND scale={Scale:R} cameraAboveRoot={camera.position.y-root.position.y:R} restNeckHeight={joints[3].RestPosition.y:R} pivot={EyeFromPivot:R}");
    }
    private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    internal void Apply(Vector3 eye,Quaternion orientation)
    {
        if(saved)throw new InvalidOperationException("Nested body anchor pose");
        for(int i=0;i<joints.Length;++i)joints[i].Save();savedHipScale=joints[0].Transform.localScale;saved=true;
        // Match the established solver's right-axis heading: looking through
        // straight down must not reverse the torso by 180 degrees.
        var right=Vector3.ProjectOnPlane(orientation*Vector3.right,Vector3.up);
        if(right.sqrMagnitude>.09f)yaw=Quaternion.LookRotation(Vector3.Cross(right,Vector3.up),Vector3.up);
        else
        {
            var forward=Vector3.ProjectOnPlane(orientation*Vector3.forward,Vector3.up);
            if(forward.sqrMagnitude>1e-6f)yaw=Quaternion.LookRotation(forward,Vector3.up);
        }
        PelvisDrop=0;
        Pivot=eye-orientation*EyeFromPivot;
        var restNeck=joints[3].RestPosition;
        joints[0].Transform.localScale=savedHipScale*Scale;
        // Upright chest/shoulders are independent of head pitch/roll and of
        // animated forward sway. The head turns around the tracked neck pivot.
        for(int i=0;i<joints.Length;++i)
            joints[i].Transform.SetPositionAndRotation(Pivot+yaw*((joints[i].RestPosition-restNeck)*Scale),yaw*joints[i].RestRotation);
        DesiredHead=Pivot+orientation*neckToHead;
        var neckTurn=Quaternion.FromToRotation(yaw*neckToHead,orientation*neckToHead);
        joints[3].Transform.SetPositionAndRotation(Pivot,neckTurn*yaw*joints[3].RestRotation);
        joints[4].Transform.SetPositionAndRotation(DesiredHead,orientation*joints[4].RestRotation);
    }
    internal void GroundPelvis(float drop)
    {
        if(drop<=0)return;
        // The measured foot targets can be below the scaled legs' reach when
        // the neck rises. Lower only the pelvis by the analytic reach deficit;
        // compensate the first spine joint so chest, shoulders and head stay
        // on their tracked targets. Leg lengths and the floor do not move.
        var spine=joints[1].Transform.position;
        joints[0].Transform.position-=Vector3.up*drop;
        joints[1].Transform.position=spine;PelvisDrop=drop;
    }
    internal void Restore()
    {
        if(!saved)return;
        joints[0].Transform.localScale=savedHipScale;
        for(int i=0;i<joints.Length;++i)joints[i].Restore();saved=false;
    }
}
