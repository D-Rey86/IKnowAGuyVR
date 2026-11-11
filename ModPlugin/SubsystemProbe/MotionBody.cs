using UnityEngine;
using HarmonyLib;
using System;
using System.Reflection;
namespace IKnowAGuyVR.SubsystemProbe;

// A reversible render pose over the existing animated humanoid. No replacement
// avatar or animation replay. The game's locomotion continues to own the legs.
internal static class MotionBody
{
    private sealed class Arm
    {
        internal Transform Upper,Lower,Hand,Prop;
        internal Quaternion SavedUpper,SavedLower,SavedHand,HandFromFrame;
        internal Vector3 PropOffset;
        internal Vector3 StrikeAxis,StrikeOffset;
        internal Vector3 PalmCenter;
        internal float PalmLength,PalmWidth;
        internal Vector3 PalmSurface,PalmNormal,Pinch;internal bool SurfaceMeasured;
        internal Vector3 IndexTip,IndexRoot;
        private float supportLength,supportWidth;
        internal void SetSupportLimits(Vector3[] points){var inv=Quaternion.Inverse(Hand.rotation*Quaternion.Inverse(HandFromFrame));foreach(var v in points){var p=inv*(v-Prop.position)-PalmCenter;supportLength=Mathf.Max(supportLength,Vector3.Dot(p,StrikeAxis));supportWidth=Mathf.Max(supportWidth,Mathf.Abs(p.z));}}
        internal void MeasureSurface(SkinnedMeshRenderer skin,Vector3[] vertices)
        {
            var frame=Hand.rotation*Quaternion.Inverse(HandFromFrame);var inv=Quaternion.Inverse(frame);var outward=PalmNormal;
            float top=float.NegativeInfinity;
            foreach(var v in vertices)
            {
                var p=inv*(skin.transform.TransformPoint(v)-Prop.position)-PalmCenter;
                if(Mathf.Abs(Vector3.Dot(p,StrikeAxis))>PalmLength*.25f||Mathf.Abs(p.z)>PalmWidth*.25f||Mathf.Abs(p.x)>PalmWidth*.5f)continue;
                top=Mathf.Max(top,Vector3.Dot(p,outward));
            }
            if(float.IsFinite(top)&&top>0)
            {
                float pad=top;
                foreach(var v in vertices){var p=inv*(skin.transform.TransformPoint(v)-Prop.position)-PalmCenter;float along=Vector3.Dot(p,StrikeAxis);if(along< -PalmLength*.25f||along>supportLength+pad||Mathf.Abs(p.z)>supportWidth+pad||Mathf.Abs(p.x)>PalmWidth*.5f)continue;top=Mathf.Max(top,Vector3.Dot(p,outward));}
                if(!SurfaceMeasured||top>Vector3.Dot(PalmSurface-PalmCenter,outward))PalmSurface=PalmCenter+outward*top;SurfaceMeasured=true;
            }
#if IKAG_DIAGNOSTICS
            Plugin.Line("NATURAL_PALM left="+IsLeft+" measured="+SurfaceMeasured+" surface="+PalmSurface.ToString("R")+" normal="+PalmNormal+" forward="+StrikeAxis.ToString("R")+" width="+PalmWidth);
#endif
        }
        internal bool Saved;
        internal bool IsLeft,Attached;
        internal Vector3 SavedHandPosition;
        internal Arm(Animator animator,bool left)
        {
            IsLeft=left;
            Upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            Lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            Hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            var index=animator.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal);
            var pinky=animator.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal);
            var middle=animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal);
            Prop=Hand.Find(left?"LeftHandProp":"RightHandProp");
            if(Upper==null||Lower==null||Hand==null||index==null||pinky==null||middle==null||Prop==null) throw new System.InvalidOperationException("Measured humanoid hand landmarks missing");
            // OpenXR grip +Z in Unity runs from little finger toward thumb;
            // +X is out of the left palm and into the right palm. Derive these
            // axes from this avatar's measured landmarks, without Euler offsets.
            Vector3 forward=index.position-pinky.position;
            // Index/pinky ordering already accounts for mirrored anatomy. A
            // second handedness sign reverses the palm axis on BOTH hands.
            Vector3 normal=Vector3.Cross(index.position-Hand.position,pinky.position-Hand.position);
            var frame=Quaternion.LookRotation(forward,Vector3.Cross(forward,normal));
            HandFromFrame=Quaternion.Inverse(frame)*Hand.rotation;
            PropOffset=Hand.InverseTransformPoint(Prop.position);
            StrikeAxis=(Quaternion.Inverse(frame)*(middle.position-Hand.position)).normalized;
            StrikeOffset=Quaternion.Inverse(frame)*(middle.position-Prop.position);
            PalmCenter=Quaternion.Inverse(frame)*((middle.position+Hand.position)*.5f-Prop.position);
            PalmLength=Vector3.Distance(middle.position,Hand.position);
            PalmWidth=Vector3.Distance(index.position,pinky.position);
        }
        internal void Apply(ControllerTransport.Hand hand)
        {
            Saved=false;
            if(!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var grip,out var orientation))return;
            SavedHandPosition=Hand.localPosition;
            Attached=KitchenHands.Pose(IsLeft,out var attachedPosition,out var attachedRotation);
            if(!Attached)Attached=MotionShovel.HandPose(IsLeft,out attachedPosition,out attachedRotation);
            if(!Attached)Attached=PhysicalHeldItems.HandPose(IsLeft,out attachedPosition,out attachedRotation);
            if(Attached){grip=attachedPosition;orientation=attachedRotation;}
            SavedUpper=Upper.localRotation;SavedLower=Lower.localRotation;SavedHand=Hand.localRotation;Saved=true;
            var targetRotation=orientation*HandFromFrame;
            Vector3 target=grip-targetRotation*Vector3.Scale(PropOffset,Hand.lossyScale);
            Vector3 shoulder=Upper.position,elbow=Lower.position,wrist=Hand.position;
            float upper=(elbow-shoulder).magnitude,lower=(wrist-elbow).magnitude;
            Vector3 delta=target-shoulder;float length=delta.magnitude;
            if(upper<=.0001f||lower<=.0001f||length<=.0001f){Hand.SetPositionAndRotation(target,targetRotation);return;}
            Vector3 direction=delta/length;
            float reach=Mathf.Clamp(length,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
            // Preserve the animated bend plane, projected onto the target axis.
            Vector3 bend=Vector3.ProjectOnPlane(elbow-shoulder,direction);
            if(bend.sqrMagnitude<1e-8f) bend=Vector3.ProjectOnPlane(Upper.up,direction);
            if(bend.sqrMagnitude<1e-8f){Hand.SetPositionAndRotation(target,targetRotation);return;}
            float along=(upper*upper-lower*lower+reach*reach)/(2*reach);
            Vector3 desiredElbow=shoulder+direction*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            Upper.rotation=Quaternion.FromToRotation(elbow-shoulder,desiredElbow-shoulder)*Upper.rotation;
            Vector3 reachable=shoulder+direction*reach;
            Lower.rotation=Quaternion.FromToRotation(Hand.position-Lower.position,reachable-Lower.position)*Lower.rotation;
            // The hand contact must remain at the tracked grip even when the
            // avatar's finite arm chain cannot reach it. Otherwise props follow
            // the controller while the wrist stops short of the same contact.
            Hand.SetPositionAndRotation(target,targetRotation);
        }
        internal void Restore()
        {if(!Saved)return;Upper.localRotation=SavedUpper;Lower.localRotation=SavedLower;Hand.localRotation=SavedHand;Hand.localPosition=SavedHandPosition;Attached=false;Saved=false;}
    }
    private static InteractionManager owner;
    private static Animator animator;
    private static Arm left,right;
    private static Transform hips,head,hiddenHead;
    private static TrackedBodyAnchor anchor;
    private static GripFingers fingers;
#if IKAG_DIAGNOSTICS
    private static Transform neck;
#endif
    private static Vector3 headScale;
    private static bool torsoSaved;
    private sealed class Leg
    {
        internal Transform Upper,Lower,Foot;
        internal Quaternion UpperRotation,LowerRotation,FootRotation,FootWorldRotation;
        internal Vector3 FootPosition;
        private Vector3 solveStart,solveKnee,solveEnd;
        private float upperLength,lowerLength;
        internal Leg(Animator a,bool left)
        {Upper=a.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);Lower=a.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);Foot=a.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);}
        internal void Save()
        {UpperRotation=Upper.localRotation;LowerRotation=Lower.localRotation;FootRotation=Foot.localRotation;FootPosition=Foot.position;FootWorldRotation=Foot.rotation;}
        internal void MoveTarget(Vector3 oldHip,Vector3 newHip,Quaternion turn,float scale)
        {
            float height=FootPosition.y;
            FootPosition=newHip+turn*((FootPosition-oldHip)*scale);FootPosition.y=height;
            FootWorldRotation=turn*FootWorldRotation;
        }
        internal float MeasurePelvisDrop()
        {
            solveStart=Upper.position;solveKnee=Lower.position;solveEnd=Foot.position;
            upperLength=(solveKnee-solveStart).magnitude;lowerLength=(solveEnd-solveKnee).magnitude;
            var delta=solveStart-FootPosition;float reach=upperLength+lowerLength-.0001f;
            float vertical=Mathf.Sqrt(Mathf.Max(0,reach*reach-delta.x*delta.x-delta.z*delta.z));
            return Mathf.Max(0,delta.y-vertical);
        }
        internal void Solve(float drop)
        {
            var offset=Vector3.up*drop;
            var start=solveStart-offset;var elbow=solveKnee-offset;var end=solveEnd-offset;
            float a=upperLength,b=lowerLength;var d=FootPosition-start;float distance=d.magnitude;
            if(a<=.0001f||b<=.0001f||distance<=.0001f)return;
            var axis=d/distance;float reach=Mathf.Clamp(distance,Mathf.Abs(a-b)+.0001f,a+b-.0001f);
            var bend=Vector3.ProjectOnPlane(elbow-start,axis);if(bend.sqrMagnitude<1e-8f)return;
            float along=(a*a-b*b+reach*reach)/(2*reach);
            var knee=start+axis*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            Upper.rotation=Quaternion.FromToRotation(elbow-start,knee-start)*Upper.rotation;
            Lower.rotation=Quaternion.FromToRotation(Foot.position-Lower.position,start+axis*reach-Lower.position)*Lower.rotation;
            Foot.rotation=FootWorldRotation;
        }
        internal void Restore(){Upper.localRotation=UpperRotation;Lower.localRotation=LowerRotation;Foot.localRotation=FootRotation;}
    }
    private static Leg leftLeg,rightLeg;
    private static SkinnedMeshRenderer[] skins;
    private static bool[] savedSkinFlags;
    private static bool[] savedPhotoVisibility;
    private static bool photoHidden;
    private static bool skinSaved;
    internal static void Install()
    {
        new Harmony("iknowaguyvr.motion-body-lifecycle").Patch(typeof(CharacterAnimatorBridge).GetMethod("SetAnimator"),postfix:new HarmonyMethod(typeof(MotionBody).GetMethod(nameof(Invalidate),BindingFlags.NonPublic|BindingFlags.Static)));
    }
    private static void Invalidate(CharacterAnimatorBridge __instance)
    {if(__instance._IsOwner_k__BackingField)owner=null;}
    internal static void Begin()
    {
        if(!MotionInput.Enabled||!ControllerTransport.Ready||!StereoTrial.GameplayActive)return;
        var current=InteractionManager.LocalInstance;
        if(current==null||!current._IsOwner_k__BackingField)return;
        if(owner==null||owner.Pointer!=current.Pointer)
        {
            owner=current;
            left=right=null;
            var bridge=current.GetComponentInChildren<CharacterAnimatorBridge>(true);
            animator=bridge==null?null:bridge.animator;
            if(animator==null||!animator.isHuman)return;
            try
            {
                left=new Arm(animator,true);right=new Arm(animator,false);
                hips=animator.GetBoneTransform(HumanBodyBones.Hips);head=animator.GetBoneTransform(HumanBodyBones.Head);
#if IKAG_DIAGNOSTICS
                neck=animator.GetBoneTransform(HumanBodyBones.Neck);
#endif
                leftLeg=new Leg(animator,true);rightLeg=new Leg(animator,false);
                var renderers=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                skins=new SkinnedMeshRenderer[renderers.Length];savedSkinFlags=new bool[renderers.Length];savedPhotoVisibility=new bool[renderers.Length];
                for(int i=0;i<skins.Length;++i)skins[i]=renderers[i];
                fingers=null;
                try{fingers=new GripFingers(animator);left.PalmNormal=fingers.MeasurePalmarNormal(left.Hand,left.Prop,left.HandFromFrame,true);right.PalmNormal=fingers.MeasurePalmarNormal(right.Hand,right.Prop,right.HandFromFrame,false);left.Pinch=fingers.MeasurePinch(left.Hand,left.Prop,left.HandFromFrame,true);right.Pinch=fingers.MeasurePinch(right.Hand,right.Prop,right.HandFromFrame,false);}catch(Exception e){Plugin.Line("GRIP_FINGERS_UNAVAILABLE "+e);}
                if(fingers!=null){fingers.MeasureIndex(left.Hand,left.Prop,left.HandFromFrame,true,out left.IndexTip,out left.IndexRoot);fingers.MeasureIndex(right.Hand,right.Prop,right.HandFromFrame,false,out right.IndexTip,out right.IndexRoot);}
                var palmMesh=new Mesh();
                try{fingers?.MeasureSupport(()=>{left.SetSupportLimits(fingers.Landmarks(true));right.SetSupportLimits(fingers.Landmarks(false));foreach(var skin in skins){if(skin==null||!skin.enabled||!skin.gameObject.activeInHierarchy)continue;bool handSkin=false;foreach(var bone in skin.bones)if(bone==left.Hand||bone==right.Hand){handSkin=true;break;}if(!handSkin)continue;skin.BakeMesh(palmMesh);var vertices=palmMesh.vertices;left.MeasureSurface(skin,vertices);right.MeasureSurface(skin,vertices);}});}
                finally{UnityEngine.Object.Destroy(palmMesh);}
                NaturalItemGrip.AvatarChanged();
                anchor=new TrackedBodyAnchor(animator,skins,current.interactionCamera.transform);
            }
            catch(Exception e){left=right=null;Plugin.Line("MOTION_BODY_UNAVAILABLE "+e);return;}
            Plugin.Line("MOTION_BODY_BOUND existing humanoid and hand prop landmarks");
        }
        if(animator==null||left==null||right==null)return;
#if IKAG_DIAGNOSTICS
        BodyAlignmentAudit.Before(animator);
        ItemPoseAudit.Before(animator);
        BodyAlignmentAudit.Save(animator,current);
        GripPunchAudit.BeforeBody(animator);
        ShovelPlayAudit.Before(animator);
        PhysicalItemAudit.Before(animator);
        CarryCatalogueAudit.Before(animator);
        FingerControlAudit.Before(animator);
        WorldPressAudit.Before(animator);
#endif
        skinSaved=true;
        for(int i=0;i<skins.Length;++i)
            if(skins[i]!=null){savedSkinFlags[i]=skins[i].forceMatrixRecalculationPerRender;if(!savedSkinFlags[i])skins[i].forceMatrixRecalculationPerRender=true;}
        if(StereoTrial.TryHeadWorld(out var eye,out var orientation))
        {
            hiddenHead=head;
#if IKAG_DIAGNOSTICS
            if(DiagnosticHideNeck&&neck!=null)hiddenHead=neck;
#endif
            headScale=hiddenHead.localScale;leftLeg.Save();rightLeg.Save();torsoSaved=true;
            var oldHip=hips.position;var nativeYaw=anchor.NativeYaw;
            anchor.Apply(eye,orientation);
            var turn=anchor.Yaw*Quaternion.Inverse(nativeYaw);
            leftLeg.MoveTarget(oldHip,hips.position,turn,anchor.Scale);rightLeg.MoveTarget(oldHip,hips.position,turn,anchor.Scale);
            float drop=Mathf.Max(leftLeg.MeasurePelvisDrop(),rightLeg.MeasurePelvisDrop());
            anchor.GroundPelvis(drop);leftLeg.Solve(drop);rightLeg.Solve(drop);
            // The local head mesh lies below the authored flat camera and enters
            // the headset view when looking down. Hide only its bone subtree for
            // the stereo draw; desktop, animation and network poses are restored.
            hiddenHead.localScale=Vector3.zero;
        }
        ControllerTransport.Sample();left.Apply(ControllerTransport.Left);right.Apply(ControllerTransport.Right);
#if IKAG_DIAGNOSTICS
        ItemPoseAudit.ApplyStart();
#endif
        fingers?.Apply(ControllerTransport.Left,ControllerTransport.Right,current,left.Attached,right.Attached);
        if(torsoSaved&&ControllerTransport.HeadTracked&&ControllerTransport.Left.Tracked&&ControllerTransport.Right.Tracked&&Probe.Focused)
            IKnowAGuyVR.Networking.NetworkPoseRuntime.Capture(current,animator,hiddenHead,headScale);
#if IKAG_DIAGNOSTICS
        ItemPoseAudit.ApplyEnd();
        NaturalCatalogueAudit.Rendered();
        ShovelPlayAudit.Rendered();
        PhysicalItemAudit.Rendered();
        CarryCatalogueAudit.Rendered();
        FingerControlAudit.Rendered();
        WorldPressAudit.Rendered();
#endif
#if IKAG_DIAGNOSTICS
        MotionRegressionAudit.Body(animator);
        BodyAlignmentAudit.After(animator,anchor,leftLeg.FootPosition,rightLeg.FootPosition);
        GripPunchAudit.Body(animator);
#endif
    }
    internal static void End()
    {
        fingers?.Restore();left?.Restore();right?.Restore();
#if IKAG_DIAGNOSTICS
        ItemPoseAudit.Restored();
#endif
        if(torsoSaved){hiddenHead.localScale=headScale;anchor.Restore();leftLeg.Restore();rightLeg.Restore();torsoSaved=false;}
        if(skinSaved)
        {for(int i=0;i<skins.Length;++i)if(skins[i]!=null&&!savedSkinFlags[i])skins[i].forceMatrixRecalculationPerRender=false;skinSaved=false;}
#if IKAG_DIAGNOSTICS
        BodyAlignmentAudit.Restored();
        GripPunchAudit.Restored();
        ShovelPlayAudit.Restored();
        PhysicalItemAudit.Restored();
        CarryCatalogueAudit.Restored();
        FingerControlAudit.Restored();
        WorldPressAudit.Restored();
#endif
    }
    internal static bool TryCarryFrame(bool useLeft,out Vector3 surface,out Vector3 forward,out Vector3 normal,out Vector3 pinch,out float width)
    {
        surface=forward=normal=pinch=default;width=0;var arm=useLeft?left:right;
        if(arm==null||!arm.SurfaceMeasured||anchor==null)return false;
        surface=arm.PalmSurface*anchor.Scale;forward=arm.StrikeAxis;normal=arm.PalmNormal;pinch=arm.Pinch*anchor.Scale;width=arm.PalmWidth*anchor.Scale;return true;
    }
#if IKAG_DIAGNOSTICS
    internal static Vector3 RenderedContact(bool useLeft,bool support,bool pinch)
    {
        var arm=useLeft?left:right;var frame=arm.Hand.rotation*Quaternion.Inverse(arm.HandFromFrame);
        return arm.Prop.position+(support?frame*(arm.PalmSurface*anchor.Scale):pinch?frame*(arm.Pinch*anchor.Scale):Vector3.zero);
    }
#endif
    internal static bool TryPunchGeometry(InteractionManager current,bool useLeft,out Vector3 axis,out Vector3 offset)
    {
        axis=offset=default;
        if(owner==null||owner.Pointer!=current.Pointer||fingers==null||anchor==null)return false;
        var arm=useLeft?left:right;if(arm==null)return false;
        axis=arm.StrikeAxis;offset=arm.StrikeOffset*anchor.Scale;return true;
    }
    internal static bool TryIndexGeometry(InteractionManager current,bool useLeft,out Vector3 tip,out Vector3 root)
    {
        tip=root=default;if(owner==null||current==null||owner.Pointer!=current.Pointer||fingers==null||anchor==null)return false;
        var arm=useLeft?left:right;var hand=useLeft?ControllerTransport.Left:ControllerTransport.Right;if(arm==null||!fingers.TryIndexOffset(useLeft,hand.GripValue,hand.TriggerValue,out tip,out root))return false;tip*=anchor.Scale;root*=anchor.Scale;return (tip-root).sqrMagnitude>1e-6f;
    }
#if IKAG_DIAGNOSTICS
    internal static Vector3 RenderedIndex(bool useLeft)=>fingers.IndexWorld(useLeft);
    // The input fixture positions the future pointing pose before grip-down.
    // Use the cached native extended landmark, not the currently relaxed tip.
    internal static bool TryPointingGeometry(InteractionManager current,bool useLeft,out Vector3 tip,out Vector3 root)
    {
        tip=root=default;if(owner==null||current==null||owner.Pointer!=current.Pointer||anchor==null)return false;
        var arm=useLeft?left:right;if(arm==null)return false;tip=arm.IndexTip*anchor.Scale;root=arm.IndexRoot*anchor.Scale;return true;
    }
#endif
    internal static bool TryPalmGeometry(InteractionManager current,bool useLeft,out Vector3 center,out Vector3 axis,out float length,out float radius)
    {
        center=axis=default;length=radius=0;
        if(owner==null||owner.Pointer!=current.Pointer||anchor==null||animator==null)return false;
        var arm=useLeft?left:right;if(arm==null)return false;
        center=arm.PalmCenter*anchor.Scale;axis=arm.StrikeAxis;
        length=arm.PalmLength*anchor.Scale;radius=arm.PalmWidth*.5f*anchor.Scale;
        return length>0&&radius>0;
    }
    internal static void HideForPhoto()
    {
        if(skins==null||photoHidden)return;
        photoHidden=true;
        for(int i=0;i<skins.Length;++i)if(skins[i]!=null){savedPhotoVisibility[i]=skins[i].enabled;if(savedPhotoVisibility[i])skins[i].enabled=false;}
    }
    internal static void RestoreAfterPhoto()
    {
        if(!photoHidden)return;
        for(int i=0;i<skins.Length;++i)if(skins[i]!=null&&savedPhotoVisibility[i])skins[i].enabled=true;
        photoHidden=false;
    }
#if IKAG_DIAGNOSTICS
    internal static void RendererSnapshot()
    {
        Plugin.Line("MACHINE_BODY animator="+animator?.gameObject.activeInHierarchy+" enabled="+animator?.enabled);
        if(skins!=null)foreach(var s in skins)if(s!=null)Plugin.Line($"MACHINE_SKIN {s.name} enabled={s.enabled} forceOff={s.forceRenderingOff} active={s.gameObject.activeInHierarchy} layer={s.gameObject.layer} shadows={s.shadowCastingMode} visible={s.isVisible} bounds={s.bounds}");
        foreach(var c in UnityEngine.Object.FindObjectsOfType<Camera>())Plugin.Line($"MACHINE_CAMERA {c.name} mask={c.cullingMask:X8}");
        Begin();try {
            if(right!=null&&KitchenHands.Pose(false,out var p,out _))Plugin.Line("ATTACHED_PROP_ERROR "+(right.Prop.position-p).magnitude);

        }finally{End();}
    }
    internal static bool DiagnosticHideNeck;
    internal static void Trace()
    {
        if(right==null)return;
        var r=ControllerTransport.Right;
        if(StereoTrial.TryTrackingPose(r.Position,r.Rotation,out var p,out var q))
        {
            Begin();
            try {Plugin.Line($"BODY_CONTROL propError={(right.Prop.position-p).magnitude:R} prop={right.Prop.position:R} target={p:R} head={head.position:R} hips={hips.position:R}");}
            finally {End();}
        }
    }
#endif
}
