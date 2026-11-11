using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

internal static unsafe class BodyAlignmentAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_BODY_ALIGNMENT_AUDIT")=="1";
    private static bool census;
    private static int phase,lastSample=-1;
    private static float next=22;
    private const int LastPhase=20;
    private static readonly List<object> samples=new();
    private static readonly List<object> restorations=new();
    private static Transform[] savedTransforms;
    private static Vector3[] savedPositions,savedScales;
    private static Quaternion[] savedRotations;
    private static Transform savedCamera;
    private static Vector3 savedCameraPosition;
    private static Quaternion savedCameraRotation;
    private static int savedPhase;
    private static readonly HumanBodyBones[] names={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Neck,HumanBodyBones.Head,HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftEye,HumanBodyBones.RightEye};
    private static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
    private static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
    internal static void Before(Animator a)
    {
        if(!enabled||census)return;census=true;
        try
        {
            Plugin.Line("BODY_ALIGNMENT_CENSUS_BEGIN bones");
            var bones=new List<object>();
            foreach(var name in names){var t=a.GetBoneTransform(name);if(t!=null)bones.Add(new{name=name.ToString(),path=t.name,parent=t.parent?.name,p=V(t.position),q=Q(t.rotation),scale=V(t.lossyScale)});}
            var meshes=new List<object>();
            Plugin.Line("BODY_ALIGNMENT_CENSUS_BEGIN meshes");
            foreach(var skin in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(skin.sharedMesh==null)continue;
                var mesh=skin.sharedMesh;Plugin.Line("BODY_ALIGNMENT_BIND_READ "+skin.name);
                // Unity's native-array accessor supplies engine-owned matrices;
                // avoid the reconstructed bindposes BlittableArrayWrapper getter.
                var count=mesh.bindposeCount;var pointer=mesh.GetBindposesArray();
                if(count<1||count>512||pointer==IntPtr.Zero)continue;
                var binding=(Matrix4x4*)pointer;var transforms=skin.bones;var rest=new List<object>();
                int root=-1;for(int i=0;i<transforms.Length;++i)if(transforms[i]!=null&&transforms[i].name=="Root"){root=i;break;}
                if(root<0||root>=count)continue;
                var bindToWorld=transforms[root].localToWorldMatrix*binding[root];
                for(int i=0;i<transforms.Length&&i<count;++i)
                {
                    var t=transforms[i];if(t==null)continue;
                    var world=bindToWorld*binding[i].inverse;
                    rest.Add(new{name=t.name,p=new[]{world.m03,world.m13,world.m23},matrix=world.ToString("R")});
                }
                meshes.Add(new{name=skin.name,mesh=mesh.name,readable=mesh.isReadable,vertices=mesh.vertexCount,scale=V(skin.transform.lossyScale),rest});
            }
            var eyes=new List<object>();var head=a.GetBoneTransform(HumanBodyBones.Head);
            foreach(var r in head.GetComponentsInChildren<Renderer>(true))
            {
                var f=r.GetComponent<MeshFilter>();var mesh=f==null?null:f.sharedMesh;
                Matrix4x4 local=Matrix4x4.identity;
                for(var t=r.transform;t!=null&&t.Pointer!=head.Pointer;t=t.parent)local=Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale)*local;
                var c=mesh==null?Vector3.zero:mesh.bounds.m_Center;
                var relative=new Vector3(local.m00*c.x+local.m01*c.y+local.m02*c.z+local.m03,local.m10*c.x+local.m11*c.y+local.m12*c.z+local.m13,local.m20*c.x+local.m21*c.y+local.m22*c.z+local.m23);
                eyes.Add(new{name=r.name,parent=r.transform.parent?.name,grandparent=r.transform.parent?.parent?.name,active=r.gameObject.activeInHierarchy,bounds=V(r.bounds.m_Center),meshCenter=mesh==null?null:V(r.transform.TransformPoint(mesh.bounds.m_Center)),localCenter=mesh==null?null:V(mesh.bounds.m_Center),headRelative=V(relative)});
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"body-census.json"),JsonSerializer.Serialize(new{bones,meshes,eyes},new JsonSerializerOptions{WriteIndented=true}));
            Plugin.Line("BODY_ALIGNMENT_CENSUS_COMPLETE");
        }
        catch(Exception e){Plugin.Line("BODY_ALIGNMENT_CENSUS_FAILED "+e);}
    }
    internal static void Save(Animator a,InteractionManager current)
    {
        if(!enabled||phase>LastPhase||lastSample==phase)return;
        var all=a.GetComponentsInChildren<Transform>(true);
        savedTransforms=new Transform[all.Length];savedPositions=new Vector3[all.Length];savedScales=new Vector3[all.Length];savedRotations=new Quaternion[all.Length];
        for(int i=0;i<all.Length;++i){var t=all[i];savedTransforms[i]=t;savedPositions[i]=t.localPosition;savedRotations[i]=t.localRotation;savedScales[i]=t.localScale;}
        savedCamera=current.interactionCamera.transform;savedCameraPosition=savedCamera.position;savedCameraRotation=savedCamera.rotation;savedPhase=phase;
    }
    private static float RotationDifference(Quaternion a,Quaternion b)=>1-Math.Abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w);
    internal static void Restored()
    {
        if(savedTransforms==null)return;
        float position=0,rotation=0,scale=0;
        for(int i=0;i<savedTransforms.Length;++i){var t=savedTransforms[i];position=Math.Max(position,(t.localPosition-savedPositions[i]).magnitude);rotation=Math.Max(rotation,RotationDifference(t.localRotation,savedRotations[i]));scale=Math.Max(scale,(t.localScale-savedScales[i]).magnitude);}
        restorations.Add(new{phase=savedPhase,transforms=savedTransforms.Length,position,rotation,scale,cameraPosition=(savedCamera.position-savedCameraPosition).magnitude,cameraRotation=RotationDifference(savedCamera.rotation,savedCameraRotation)});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"body-restoration.json"),JsonSerializer.Serialize(restorations,new JsonSerializerOptions{WriteIndented=true}));
        savedTransforms=null;
    }
    internal static void After(Animator a,TrackedBodyAnchor anchor,Vector3 leftFootTarget,Vector3 rightFootTarget)
    {
        if(!enabled||phase>LastPhase||lastSample==phase)return;lastSample=phase;
        if(!StereoTrial.TryHeadWorld(out var p,out var q))return;
        var bones=new List<object>();foreach(var name in names){var t=a.GetBoneTransform(name);if(t!=null)bones.Add(new{name=name.ToString(),p=V(t.position),q=Q(t.rotation)});}
        var lf=a.GetBoneTransform(HumanBodyBones.LeftFoot);var rf=a.GetBoneTransform(HumanBodyBones.RightFoot);
        var l=a.GetBoneTransform(HumanBodyBones.LeftHand).Find("LeftHandProp");var r=a.GetBoneTransform(HumanBodyBones.RightHand).Find("RightHandProp");
        StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var lp,out _);
        StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var rp,out _);
        samples.Add(new{phase,frame=Time.frameCount,headP=V(p),headQ=Q(q),scale=anchor.Scale,pivot=V(anchor.Pivot),yaw=Q(anchor.Yaw),pelvisDrop=anchor.PelvisDrop,neckError=(a.GetBoneTransform(HumanBodyBones.Neck).position-(p-q*TrackedBodyAnchor.EyeFromPivot)).magnitude,leftFootTarget=V(leftFootTarget),rightFootTarget=V(rightFootTarget),leftFootError=(lf.position-leftFootTarget).magnitude,rightFootError=(rf.position-rightFootTarget).magnitude,leftPropError=(l.position-lp).magnitude,rightPropError=(r.position-rp).magnitude,bones});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"body-samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Tick()
    {
        if(!enabled||phase>LastPhase||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        string command=phase switch {
            0=>"head rot 0 0 0\nhead pos 0 1.6 0\nhands reset",
            1=>"shot body-level",
            2=>"head rot 0 -70 0\nhead pos 0 1.5 -0.1",
            3=>"shot body-down",
            4=>"stick l 0 0.6",
            5=>"shot body-walking",
            6=>"stick l 0 0\nhead rot 0 -70 20",
            7=>"shot body-roll",
            8=>"head rot 110 -30 0\nhead pos 0.1 1.6 -0.1",
            9=>"shot body-turn",
            10=>Arc(0),
            11=>"shot body-arc-level",
            12=>Arc(85),
            13=>"shot body-arc-85",
            14=>Arc(90),
            15=>"shot body-arc-90",
            16=>Arc(95),
            17=>"shot body-arc-95",
            18=>Arc(0),
            19=>"shot body-arc-returned",
            _=>"head rot 0 0 0\nhead pos 0 1.6 0\nhands reset"};
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
        if(phase==LastPhase)Plugin.Line("BODY_ALIGNMENT_AUDIT_COMPLETE");
        ++phase;next=Time.realtimeSinceStartup+1.2f;
    }
    private static string Arc(float pitch)
    {
        // Fixed neck-pivot motion, expressed in the simulator's OpenXR space.
        var eye=new Vector3(0,1.47f,-.10f)+Quaternion.Euler(pitch,0,0)*TrackedBodyAnchor.EyeFromPivot;
        return FormattableString.Invariant($"head rot 0 {-pitch} 0\nhead pos {eye.x:R} {eye.y:R} {-eye.z:R}\nhands reset");
    }
}
