using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Bindings;
namespace IKnowAGuyVR.SubsystemProbe;

internal sealed unsafe class GripFingers
{
    private readonly Transform[] bones=new Transform[30];
    private readonly Quaternion[] closed=new Quaternion[30],opened=new Quaternion[30],relaxed=new Quaternion[30],pointed=new Quaternion[30],flatOpened=new Quaternion[30],saved=new Quaternion[30],itemPose=new Quaternion[30];
    private GameObject fittedItem;private int fittedRevision=-1;private bool fittedLeft,hasFit;
    private int profileRevision=-1;
    private int shovelRevision=-1,physicalRevision=-1;private bool supportFit;
    private GameObject geometryItem;private Collider[] itemColliders;
    private bool leftSaved,rightSaved;
    private readonly Transform[] indexTips=new Transform[2];
    private sealed class IndexPose
    {
        internal readonly int[] Slots=new int[3];internal readonly Vector3[] Segments=new Vector3[3];
        internal readonly Quaternion[] Parents=new Quaternion[3],InverseParents=new Quaternion[3],InverseOpen=new Quaternion[3];
        internal int Count;internal Vector3 Root,OpenTip,LastTip;internal float LastGrip=-1,LastTrigger=-1;internal bool Uniform=true;
    }
    private readonly IndexPose[] indexes=new IndexPose[2];
    internal GripFingers(Animator animator)
    {
        for(int i=0;i<30;++i)
        {
            bones[i]=animator.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal+i));
            if(bones[i]==null&&i%3==0)throw new InvalidOperationException("Humanoid finger base missing");
        }
        var all=animator.GetComponentsInChildren<Transform>(true);
        var positions=new Vector3[all.Length];var rotations=new Quaternion[all.Length];var scales=new Vector3[all.Length];
        for(int i=0;i<all.Length;++i){positions[i]=all[i].localPosition;rotations[i]=all[i].localRotation;scales[i]=all[i].localScale;}
        void RestoreRig(){for(int i=0;i<all.Length;++i){all[i].SetLocalPositionAndRotation(positions[i],rotations[i]);all[i].localScale=scales[i];}}
        var names=HumanTrait.MuscleName;var muscles=new float[HumanTrait.MuscleCount];
        var candidates=new List<Transform>();
        for(var t=animator.GetBoneTransform(HumanBodyBones.Hips).parent;t!=null;t=t.parent)
        {candidates.Add(t);if(t.Pointer==animator.transform.Pointer)break;}
        var low=new Quaternion[30];var high=new Quaternion[30];
        bool found=false;
        foreach(var root in candidates)
        {
            IntPtr handler=HumanPoseHandler.Internal_CreateFromRoot(animator.avatar,root);
            if(handler==IntPtr.Zero)continue;
            float lowDistance=0,highDistance=0;
            try
            {
                for(int endpoint=0;endpoint<2;++endpoint)
                {
                    int selected=0;
                    for(int i=0;i<muscles.Length;++i)
                        if((names[i].StartsWith("Left ",StringComparison.Ordinal)||names[i].StartsWith("Right ",StringComparison.Ordinal))&&names[i].Contains("Stretched")){muscles[i]=endpoint==0?-1:1;++selected;}
                    if(selected!=30)throw new InvalidOperationException("Unexpected humanoid finger muscle mapping");
                    var body=Vector3.zero;var q=Quaternion.identity;
                    fixed(float* p=muscles){var span=new ManagedSpanWrapper(p,muscles.Length);HumanPoseHandler.SetHumanPose_Injected(handler,ref body,ref q,ref span);}
                    float distance=0;
                    for(int i=0;i<30;++i)
                    {
                        if(bones[i]==null)continue;
                        var value=bones[i].localRotation;
                        if(!float.IsFinite(value.x)||!float.IsFinite(value.y)||!float.IsFinite(value.z)||!float.IsFinite(value.w)||Math.Abs(Quaternion.Dot(value,value)-1)>.001f)throw new InvalidOperationException("Invalid native finger pose");
                        (endpoint==0?low:high)[i]=value;
                        bool terminal=i%3==2||(i%3==1&&bones[i+1]==null);
                        if(terminal&&i%15>=3)
                        {var wrist=animator.GetBoneTransform(i<15?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var tip=bones[i].childCount>0?bones[i].GetChild(0):bones[i];distance+=(tip.position-wrist.position).magnitude;}
                    }
                    if(endpoint==0)lowDistance=distance;else highDistance=distance;
                    RestoreRig();
                }
                float effect=0;for(int i=0;i<30;++i)if(bones[i]!=null)effect=Math.Max(effect,1-Math.Abs(Quaternion.Dot(low[i],high[i])));
                if(effect<.01f)continue;
                if(Math.Abs(lowDistance-highDistance)<.01f)throw new InvalidOperationException("Native finger limits do not distinguish an open and closed pose");
                Array.Copy(lowDistance<highDistance?low:high,closed,30);Array.Copy(lowDistance<highDistance?high:low,opened,30);found=true;
                for(int i=0;i<muscles.Length;i++)if(names[i].Contains("Stretched")&&(names[i].StartsWith("Left ")||names[i].StartsWith("Right ")))muscles[i]=lowDistance<highDistance?1:-1;
                void ApplyMuscles(){var body=Vector3.zero;var q=Quaternion.identity;fixed(float* p=muscles){var span=new ManagedSpanWrapper(p,muscles.Length);HumanPoseHandler.SetHumanPose_Injected(handler,ref body,ref q,ref span);}}
                // Sample the avatar's authored neutral muscle pose for a relaxed
                // free hand. Keep the extreme endpoints for pointing, fists,
                // item fitting and the measured flat support pose.
                for(int i=0;i<muscles.Length;i++)if(names[i].Contains("Stretched")&&(names[i].StartsWith("Left ")||names[i].StartsWith("Right ")))muscles[i]=0;
                ApplyMuscles();for(int i=0;i<30;i++)if(bones[i]!=null)relaxed[i]=bones[i].localRotation;RestoreRig();
                for(int i=0;i<muscles.Length;i++)if(names[i].Contains("Stretched")&&(names[i].StartsWith("Left ")||names[i].StartsWith("Right ")))muscles[i]=lowDistance<highDistance?1:-1;
                for(int side=0;side<2;side++)
                {
                    int spread=-1;for(int i=0;i<names.Length;i++)if(names[i].StartsWith(side==0?"Left ":"Right ")&&names[i].Contains("Thumb")&&names[i].Contains("Spread")){spread=i;break;}
                    if(spread<0)continue;
                    var wrist=animator.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);int at=side*15;
                    Vector3 Tip(int f){var tip=bones[at+f*3+2]??bones[at+f*3+1]??bones[at+f*3];if(tip.childCount>0)tip=tip.GetChild(0);return tip.position;}
                    float Score(float value){muscles[spread]=value;ApplyMuscles();var normal=Vector3.Cross(bones[at+3].position-wrist.position,bones[at+12].position-wrist.position).normalized;float target=0;for(int f=1;f<5;f++)target+=Vector3.Dot(Tip(f)-wrist.position,normal)*.25f;return Vector3.Dot(Tip(0)-wrist.position,normal)-target;}
                    float choice=0,best=float.PositiveInfinity,previous=Score(-1);
                    for(int k=0;k<=8;k++){float value=-1+k*.25f;float score=Score(value);if(Mathf.Abs(score)<best){best=Mathf.Abs(score);choice=value;}if(k>0&&previous*score<=0){float lo=value-.25f,hi=value;bool positive=previous>0;for(int n=0;n<10;n++){float mid=(lo+hi)*.5f;if((Score(mid)>0)==positive)lo=mid;else hi=mid;}choice=(lo+hi)*.5f;break;}previous=score;}
                    muscles[spread]=choice;
                }
                ApplyMuscles();for(int i=0;i<30;i++)if(bones[i]!=null)flatOpened[i]=bones[i].localRotation;RestoreRig();
                Plugin.Line($"GRIP_FINGERS_BOUND root={root.name} negativeDistance={lowDistance:R} positiveDistance={highDistance:R} endpoint={(lowDistance<highDistance?-1:1)}");
                break;
            }
            finally{RestoreRig();HumanPoseHandler.Internal_Destroy(handler);}
        }
        if(!found)throw new InvalidOperationException("No avatar root maps native finger muscles");
        for(int side=0;side<2;side++){var tip=bones[side*15+5]??bones[side*15+4]??bones[side*15+3];indexTips[side]=tip.childCount>0?tip.GetChild(0):tip;}
        Array.Copy(opened,pointed,30);
        // Follow the authored metacarpal direction, rather than the avatar's
        // maximum backward-extension limit. Align each actual phalanx in turn.
        for(int side=0;side<2;side++)
        {
            int start=side*15+3;var wrist=animator.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            for(int i=start;i<start+3;i++)if(bones[i]!=null){saved[i]=bones[i].localRotation;bones[i].localRotation=opened[i];}
            try{
                var direction=(bones[start].position-wrist.position).normalized;
                for(int i=start;i<start+3;i++)if(bones[i]!=null){var end=i+1<start+3&&bones[i+1]!=null?bones[i+1]:indexTips[side];var segment=end.position-bones[i].position;if(segment.sqrMagnitude<1e-8f)continue;bones[i].rotation=Quaternion.FromToRotation(segment,direction)*bones[i].rotation;pointed[i]=bones[i].localRotation;}
            }finally{for(int i=start;i<start+3;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
        }
    }
    internal void MeasureIndex(Transform hand,Transform prop,Quaternion handFromFrame,bool left,out Vector3 tip,out Vector3 root)
    {
        int start=(left?0:15)+3;var inverse=Quaternion.Inverse(hand.rotation*Quaternion.Inverse(handFromFrame));
        for(int i=start;i<start+3;i++)if(bones[i]!=null){saved[i]=bones[i].localRotation;bones[i].localRotation=pointed[i];}
        try{
            tip=inverse*(indexTips[left?0:1].position-prop.position);root=inverse*(bones[start].position-prop.position);var pose=new IndexPose{Root=root,OpenTip=tip};
            for(int i=start;i<start+3;i++)if(bones[i]!=null)pose.Slots[pose.Count++]=i;
            for(int i=0;i<pose.Count;i++){var b=bones[pose.Slots[i]];var end=i+1<pose.Count?bones[pose.Slots[i+1]]:indexTips[left?0:1];pose.Segments[i]=inverse*(end.position-b.position);pose.Parents[i]=inverse*b.parent.rotation;pose.InverseParents[i]=Quaternion.Inverse(pose.Parents[i]);pose.InverseOpen[i]=Quaternion.Inverse(pointed[pose.Slots[i]]);var s=b.parent.lossyScale;pose.Uniform&=s.x>0&&Mathf.Abs(s.x-s.y)<.0001f&&Mathf.Abs(s.x-s.z)<.0001f;}
            indexes[left?0:1]=pose;
        }
        finally{for(int i=start;i<start+3;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
    }
    // Compose the same native local-quaternion curls as ApplyHand, in the
    // measured controller frame. Input contact never needs to pose the rig.
    internal bool TryIndexOffset(bool left,float grip,float trigger,out Vector3 tip,out Vector3 root)
    {
        tip=root=default;var pose=indexes[left?0:1];if(pose==null)return false;root=pose.Root;if(grip>=1&&trigger<=0){tip=pose.OpenTip;return true;}if(!pose.Uniform)return false;
        if(grip==pose.LastGrip&&trigger==pose.LastTrigger){tip=pose.LastTip;return true;}var rotation=Quaternion.identity;tip=root;
        for(int i=0;i<pose.Count;i++){int slot=pose.Slots[i];var q=FreePose(slot,grip,trigger);rotation=rotation*(pose.Parents[i]*(q*pose.InverseOpen[i])*pose.InverseParents[i]);tip+=rotation*pose.Segments[i];}
        pose.LastGrip=grip;pose.LastTrigger=trigger;pose.LastTip=tip;return true;
    }
#if IKAG_DIAGNOSTICS
    internal Vector3 IndexWorld(bool left)=>indexTips[left?0:1].position;
#endif
    internal void Apply(ControllerTransport.Hand left,ControllerTransport.Hand right,InteractionManager owner,bool leftAttached,bool rightAttached)
    {
        var item=owner.cachedTopItem;bool occupied=item!=null&&owner.IsHoldingItem(),useLeft=ObjectGrabs.LeftHand;
        if(item!=fittedItem||useLeft!=fittedLeft||(!NaturalItemGrip.SemanticReady&&fittedRevision!=ObjectGrabs.PoseRevision)||profileRevision!=NaturalItemGrip.Revision||shovelRevision!=MotionShovel.Revision||physicalRevision!=PhysicalHeldItems.Revision)
        {
            profileRevision=NaturalItemGrip.Revision;
            shovelRevision=MotionShovel.Revision;physicalRevision=PhysicalHeldItems.Revision;supportFit=false;
            fittedItem=item;fittedLeft=useLeft;fittedRevision=ObjectGrabs.PoseRevision;hasFit=false;
            if(occupied){if(NaturalItemGrip.FlatHand){Array.Copy(flatOpened,itemPose,30);hasFit=true;}else hasFit=Fit(item,useLeft?0:15);}
            if(occupied&&(MotionShovel.Supporting||PhysicalHeldItems.Attached(!useLeft)))supportFit=Fit(item,useLeft?15:0);
        }
        leftSaved=ApplyHand(0,left.GripValue,left.TriggerValue,left.Tracked,occupied&&(useLeft||MotionShovel.Supporting||PhysicalHeldItems.Attached(true)),useLeft?hasFit:supportFit,leftAttached);
        rightSaved=ApplyHand(15,right.GripValue,right.TriggerValue,right.Tracked,occupied&&(!useLeft||MotionShovel.Supporting||PhysicalHeldItems.Attached(false)),useLeft?supportFit:hasFit,rightAttached);
    }
    internal Vector3 MeasurePalmarNormal(Transform hand,Transform prop,Quaternion handFromFrame,bool left)
    {
        int start=left?0:15;var inv=Quaternion.Inverse(hand.rotation*Quaternion.Inverse(handFromFrame));
        float Tips(){float sum=0;for(int f=1;f<5;f++){int at=start+f*3;var tip=bones[at+2]??bones[at+1]??bones[at];if(tip.childCount>0)tip=tip.GetChild(0);sum+=(inv*(tip.position-prop.position)).x;}return sum;}
        for(int i=start;i<start+15;i++)if(bones[i]!=null){saved[i]=bones[i].localRotation;bones[i].localRotation=opened[i];}
        float a=Tips();
        try{for(int i=start;i<start+15;i++)if(bones[i]!=null)bones[i].localRotation=closed[i];float delta=Tips()-a;if(Mathf.Abs(delta)<.0001f)throw new InvalidOperationException("No measured palm-facing curl direction");return Vector3.right*Mathf.Sign(delta);}
        finally{for(int i=start;i<start+15;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
    }
    internal void MeasureSupport(Action sample)
    {
        for(int i=0;i<30;i++)if(bones[i]!=null){saved[i]=bones[i].localRotation;bones[i].localRotation=flatOpened[i];}
        try{sample();}finally{for(int i=0;i<30;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
    }
    internal Vector3[] Landmarks(bool left)
    {
        var points=new List<Vector3>();int start=left?0:15;for(int i=start;i<start+15;i++)if(bones[i]!=null){points.Add(bones[i].position);if(i%3==2&&bones[i].childCount>0)points.Add(bones[i].GetChild(0).position);}return points.ToArray();
    }
    internal Vector3 MeasurePinch(Transform hand,Transform prop,Quaternion handFromFrame,bool left)
    {
        int start=left?0:15;var inv=Quaternion.Inverse(hand.rotation*Quaternion.Inverse(handFromFrame));
        for(int i=start;i<start+15;i++)if(bones[i]!=null){saved[i]=bones[i].localRotation;bones[i].localRotation=closed[i];}
        Vector3 Tip(int at){var t=bones[at+2]??bones[at+1]??bones[at];if(t.childCount>0)t=t.GetChild(0);return t.position;}
        try{return inv*((Tip(start)+Tip(start+3))*.5f-prop.position);}
        finally{for(int i=start;i<start+15;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
    }
    private Quaternion FreePose(int slot,float grip,float trigger)
    {
        bool index=slot%15>=3&&slot%15<6;
        var rest=index?(grip<=0?relaxed[slot]:grip>=1?pointed[slot]:Quaternion.Slerp(relaxed[slot],pointed[slot],grip)):relaxed[slot];
        float curl=index?trigger:grip;
        return curl<=0?rest:curl>=1?closed[slot]:Quaternion.Slerp(rest,closed[slot],curl);
    }
    private bool ApplyHand(int start,float grip,float trigger,bool tracked,bool occupied,bool fitted,bool attached)
    {
        if(!tracked&&!occupied)return false;
        if(occupied&&!fitted)return false;
        if(!occupied&&attached&&grip<=0)return false;
        for(int i=start;i<start+15;++i)if(bones[i]!=null)
        {
            saved[i]=bones[i].localRotation;
            if(occupied){bones[i].localRotation=itemPose[i];continue;}
            if(attached){bones[i].localRotation=Quaternion.Slerp(saved[i],closed[i],grip);continue;}
            // Rest naturally; grip also extends the index for pointing, and
            // trigger still closes that index independently into the same fist.
            bones[i].localRotation=FreePose(i,grip,trigger);
        }
        return true;
    }
    private bool Fit(GameObject item,int start)
    {
#if IKAG_DIAGNOSTICS
        long fitClock=CarryCatalogueAudit.BeginFit();
#endif
        if(geometryItem!=item){geometryItem=item;var colliders=item.GetComponentsInChildren<Collider>(true);var selected=new List<Collider>();foreach(var c in colliders){if(c.isTrigger)continue;var containing=c.GetComponentInParent<HoldableBase>();if(containing==null||containing.gameObject==item)selected.Add(c);}itemColliders=selected.ToArray();}
        var shapes=new List<GripShape>();var enabledForFit=new List<Collider>();
#if IKAG_DIAGNOSTICS
        float fixedAt=Time.fixedTime;
#endif
        try
        {
        foreach(var c in itemColliders)
        {
            if(c==null||!c.gameObject.activeInHierarchy)continue;
            var mesh=c.TryCast<MeshCollider>();if(mesh!=null&&mesh.convex&&!c.enabled){enabledForFit.Add(c);c.enabled=true;}
            if(GripShape.TryCreate(c,out var shape))shapes.Add(shape);
#if IKAG_DIAGNOSTICS
            else ItemPoseAudit.Unsupported(c);
#endif
        }
        if(shapes.Count==0)return false;
        for(int i=start;i<start+15;i++)if(bones[i]!=null)saved[i]=bones[i].localRotation;
        try
        {
            for(int finger=0;finger<5;finger++)
            {
                int at=start+finger*3;var tip=bones[at+2]??bones[at+1]??bones[at];
                if(tip.childCount>0)tip=tip.GetChild(0);
                void Curl(float value){for(int j=at;j<at+3;j++)if(bones[j]!=null)bones[j].localRotation=Quaternion.Slerp(opened[j],closed[j],value);}
                float Distance(){float d=float.PositiveInfinity;foreach(var s in shapes)d=Mathf.Min(d,s.Distance(tip.position));return d;}
                Curl(0);float previous=Distance(),best=Mathf.Abs(previous),choice=0;
                // Sweep the avatar's measured open-to-closed arc. Refine its first
                // surface crossing, rather than inventing item-specific Euler angles.
                for(int k=1;k<=32;k++)
                {
                    float value=k/32f;Curl(value);float d=Distance();
                    if(Mathf.Abs(d)<best){best=Mathf.Abs(d);choice=value;}
                    if((previous>0&&d<=0)||(previous<0&&d>=0))
                    {
                        float lo=(k-1)/32f,hi=value;bool outside=previous>0;
                        for(int n=0;n<12;n++){float mid=(lo+hi)*.5f;Curl(mid);if((Distance()>0)==outside)lo=mid;else hi=mid;}
                        choice=(lo+hi)*.5f;break;
                    }
                    previous=d;
                }
                Curl(choice);for(int j=at;j<at+3;j++)if(bones[j]!=null)itemPose[j]=bones[j].localRotation;
            }
#if IKAG_DIAGNOSTICS
            ItemPoseAudit.Fitted(item,start==0,itemPose,opened,closed,bones);
#endif
            return true;
        }
        finally{for(int i=start;i<start+15;i++)if(bones[i]!=null)bones[i].localRotation=saved[i];}
        }
        finally{foreach(var c in enabledForFit)if(c!=null)c.enabled=false;
#if IKAG_DIAGNOSTICS
        CarryCatalogueAudit.EndFit(fitClock);
        if(Time.fixedTime!=fixedAt)throw new InvalidOperationException("Physics advanced during finger fit");
#endif
        }
    }
    internal void Restore()
    {
        for(int i=0;i<30;++i)if(bones[i]!=null&&(i<15?leftSaved:rightSaved))bones[i].localRotation=saved[i];
        leftSaved=rightSaved=false;
    }
}

