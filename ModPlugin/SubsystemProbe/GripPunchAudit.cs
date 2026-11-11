using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class GripPunchAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_GRIP_PUNCH_AUDIT")=="1";
    private static readonly bool hitOnly=Environment.GetEnvironmentVariable("IKAG_XR_GRIP_PUNCH_HIT_AUDIT")=="1";
    private static int phase,leftStrikes,rightStrikes,volumes,hits,punchInputs,animations;
    private static float next=20;
    private static InteractionManager interaction;
    private static IntPtr excludedTarget;private static IPunchable target;private static Collider targetCollider;private static Transform targetTransform;private static Vector3 targetPosition;
    private static Vector3 rightBase,leftBase,rightAxis,leftAxis;
    private static readonly List<object> bodySamples=new(),restorations=new();
    private static Transform[] transforms;private static Vector3[] positions,scales;private static Quaternion[] rotations;
    private static int bodyPhase=-1;private static bool restorePending;
    internal static void BeforeBody(Animator a)
    {
        if(!enabled||bodyPhase==phase||phase>30)return;
        bodyPhase=phase;
        if(transforms==null){var all=a.GetComponentsInChildren<Transform>(true);transforms=new Transform[all.Length];positions=new Vector3[all.Length];rotations=new Quaternion[all.Length];scales=new Vector3[all.Length];for(int i=0;i<all.Length;++i)transforms[i]=all[i];}
        for(int i=0;i<transforms.Length;++i){positions[i]=transforms[i].localPosition;rotations[i]=transforms[i].localRotation;scales[i]=transforms[i].localScale;}restorePending=true;
    }
    internal static void Body(Animator a)
    {
        if(!restorePending)return;
        float Distance(bool left)
        {
            var wrist=a.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);float sum=0;
            for(int f=1;f<5;++f){int at=(int)(left?HumanBodyBones.LeftThumbProximal:HumanBodyBones.RightThumbProximal)+f*3;var tip=a.GetBoneTransform((HumanBodyBones)(at+2))??a.GetBoneTransform((HumanBodyBones)(at+1));if(tip.childCount>0)tip=tip.GetChild(0);sum+=(tip.position-wrist.position).magnitude;}
            return sum/4;
        }
        bodySamples.Add(new{phase,left=Distance(true),right=Distance(false),leftGrip=ControllerTransport.Left.GripValue,rightGrip=ControllerTransport.Right.GripValue});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"finger-metrics.json"),JsonSerializer.Serialize(bodySamples));
    }
    internal static void Restored()
    {
        if(!restorePending)return;restorePending=false;float p=0,q=0,s=0;
        for(int i=0;i<transforms.Length;++i){p=Math.Max(p,(transforms[i].localPosition-positions[i]).magnitude);s=Math.Max(s,(transforms[i].localScale-scales[i]).magnitude);q=Math.Max(q,Math.Abs(1-Math.Abs(Quaternion.Dot(transforms[i].localRotation,rotations[i]))));}
        restorations.Add(new{phase,transforms=transforms.Length,position=p,rotation=q,scale=s});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"finger-restoration.json"),JsonSerializer.Serialize(restorations));
    }
    private static Vector3 cameraPosition;private static Quaternion cameraRotation;
    internal static void BeforeStrike(InteractionManager owner)
    {if(enabled){cameraPosition=owner.interactionCamera.transform.localPosition;cameraRotation=owner.interactionCamera.transform.localRotation;}}
    internal static void Strike(bool left,Vector3 position,float speed)
    {if(!enabled)return;Check((interaction.interactionCamera.transform.localPosition-cameraPosition).sqrMagnitude<1e-12f&&Math.Abs(Quaternion.Dot(interaction.interactionCamera.transform.localRotation,cameraRotation))>.999999f,"Punch did not restore camera");if(left)++leftStrikes;else ++rightStrikes;Plugin.Line($"MOTION_PUNCH_STRIKE left={left} position={position:R} speed={speed:R}");}
    private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
    private static string To(bool left,Vector3 p,int ms)=>FormattableString.Invariant($"hand {(left?"l":"r")} to grip {p.x:R} {p.y:R} {-p.z:R} 0 -90 0 {ms}");
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static void Tick()
    {
        if(!enabled||phase>30||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            var inv=InventoryManager.LocalInstance;string command=null;float delay=.5f;
            if(phase==0)
            {
                interaction=InteractionManager.LocalInstance;inv.SetSelectedSlot(7);
                Check(ControllerTransport.HeadTracked,"Input HMD position unavailable");
                Plugin.Line($"GRIP_INPUT_HEAD p={ControllerTransport.HeadPosition:R}");
                var h=new Harmony("ikag-grip-punch-audit");
                HarmonyMethod Hook(string n)=>new(typeof(GripPunchAudit).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static));
                h.Patch(typeof(PunchCollider).GetMethod("Initialize"),postfix:Hook(nameof(ColliderInitialized)));
                h.Patch(typeof(InteractionManager).GetMethod("HandlePunch"),prefix:Hook(nameof(PunchInput)));
                h.Patch(typeof(InteractionManager).GetMethod("OnPunchHit"),postfix:Hook(nameof(Hit)));
                h.Patch(typeof(CharacterAnimatorBridge).GetMethod("TriggerPunch"),prefix:Hook(nameof(AnimationTriggered)));
                command="hands reset\ngrip l 0\ngrip r 0\nhand l follow off\nhand r follow off\nhand l grip pose -0.2 1.3 -0.35 0 -90 0\nhand r grip pose 0.2 1.3 -0.35 0 -90 0\nstick r 0 0";
            }
            else if(phase==1)command="stick r 0 1";
            else if(phase==2)Check(inv.currentSelectedSlot==(7+1)%inv.totalSlots,"Right-stick up did not advance once");
            else if(phase==3){Check(inv.currentSelectedSlot==(7+1)%inv.totalSlots,"Held up repeated hotbar");command="stick r 0 0";}
            else if(phase==4)command="stick r 0 -1";
            else if(phase==5){Check(inv.currentSelectedSlot==7,"Right-stick down did not select previous");command="stick r 0 0\ngrip l 1\ntrigger l 1";}
            else if(phase==6){Check(inv.currentSelectedSlot==7,"Left grip changed hotbar");command="shot left-fist\ngrip l 0\ntrigger l 0\ngrip r 1\ntrigger r 1";}
            else if(phase==7){Check(inv.currentSelectedSlot==7,"Right grip changed hotbar");command="shot right-fist\ngrip l 1\ntrigger l 1";}
            else if(phase==8)
            {
                Check(volumes==0,"Stationary grips punched");command="shot both-fists";
                rightBase=ControllerTransport.Right.Position;leftBase=ControllerTransport.Left.Position;
                Check(MotionBody.TryPunchGeometry(interaction,false,out var r,out _)&&MotionBody.TryPunchGeometry(interaction,true,out _,out _),"Fist geometry unavailable");
                MotionBody.TryPunchGeometry(interaction,true,out var l,out _);
                rightAxis=ControllerTransport.Right.Rotation*r;leftAxis=ControllerTransport.Left.Rotation*l;
                Plugin.Line($"PUNCH_AXES left={leftAxis:R} right={rightAxis:R}");
            }
            else if(phase==9){var side=Vector3.Cross(rightAxis,Vector3.up);if(side.sqrMagnitude<.1f)side=Vector3.Cross(rightAxis,Vector3.right);command=To(false,rightBase+side.normalized*.35f,120);}
            else if(phase==10){Check(volumes==0,"Lateral sweep punched");command=To(false,rightBase,800);delay=1;}
            else if(phase==11)command=To(false,rightBase-rightAxis*.35f,120);
            else if(phase==12){Check(volumes==0,"Retraction punched");command=To(false,rightBase,800);delay=1;}
            else if(phase==13){if(hitOnly)PlaceTarget(false);command=To(false,rightBase+rightAxis*.4f,150);}
            else if(phase==14){Check(rightStrikes==1&&leftStrikes==0&&volumes==1,"Right thrust did not produce exactly one native strike");command="shot right-punch";}
            else if(phase==15){if(hitOnly){Check(hits==1,"Right native hit missing");PlaceTarget(true);}command=To(true,leftBase+leftAxis*.4f,150);}
            else if(phase==16){Check(leftStrikes==1&&rightStrikes==1&&volumes==2,"Left thrust did not produce exactly one native strike");Check(punchInputs==0&&animations==0,"Motion called canned punch path");command="shot left-punch\ngrip l 0\ngrip r 0\ntrigger l 0\ntrigger r 0";}
            else if(phase==17)command=To(false,rightBase+rightAxis*.8f,150);
            else if(phase==18){if(hitOnly){Check(hits==2,"Left native hit missing");RestoreTarget();Plugin.Line("GRIP_PUNCH_HIT_AUDIT_COMPLETE bilateral native hits=2 cameras restored canned-path=0");phase=31;return;}Check(volumes==2,"Open hand punched");inv.SetSelectedSlot(1);command="hands reset\ntrigger l 1\nstick r 0 0";}
            else if(phase==19)command="trigger l 0\nstick r 0 1";
            else if(phase==20){Check(inv.currentSelectedSlot==1&&MotionInput.PhotoMode,"Zoom stick cycled hotbar");command="stick r 0 0\ntrigger l 1";}
            else if(phase==21){command="trigger l 0";inv.SetSelectedSlot(7);}
            else if(phase==22)command="hands reset\ngrip l 0\ngrip r 0\nhand l grip pose -0.15 1.45 -0.35 -66.8014 0 21.5\nhead rot 23.1986 -21.5 0";
            else if(phase==23){MotionInput.Trace();var h=ControllerTransport.Left;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var pp,out var qq);StereoTrial.TryHeadWorld(out var hp,out var hq);var d=hp-pp;Plugin.Line($"WHEEL_FIXTURE focus={Probe.Focused} cursor={Cursor.lockState} distance={d.magnitude:R} facing={Vector3.Dot(qq*Vector3.right,d.normalized):R} gaze={Vector3.Dot(hq*Vector3.forward,-d.normalized):R} head={hq:R}");Check(!MotionInput.WheelOpen,"Removed palm gesture opened wheel");command="stick r 0 0";}
            else if(phase==24){Check(inv.currentSelectedSlot==7&&!MotionInput.WheelOpen,"Palm gesture changed gameplay state");command="stick r 0 0\nhands reset\nhead rot 0 0 0";}
            else if(phase==25)Check(!MotionInput.WheelOpen,"Removed palm gesture opened wheel after reset");
            else if(phase==30){Check(volumes==2&&punchInputs==0&&animations==0,"Unexpected additional punch");Plugin.Line($"GRIP_PUNCH_AUDIT_COMPLETE left={leftStrikes} right={rightStrikes} volumes={volumes} hits={hits}");}
            Plugin.Line($"GRIP_PHASE {phase} slot={inv.currentSelectedSlot}/{inv.totalSlots} left={leftStrikes} right={rightStrikes} volumes={volumes} hits={hits}");
            if(command!=null)Send(command);++phase;next=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e){RestoreTarget();Plugin.Line("GRIP_PUNCH_AUDIT_FAILED "+e);phase=31;Send("grip l 0\ngrip r 0\nstick r 0 0\ntrigger l 0\ntrigger r 0");}
    }
    private static void PlaceTarget(bool left)
    {
        if(left&&target!=null){Plugin.Line("PUNCH_TARGET_AFTER_RIGHT punchable="+target.CanBePunched());RestoreTarget();excludedTarget=target.Pointer;target=null;targetTransform=null;}
        if(target==null)
        {
            float nearest=float.PositiveInfinity;
            foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                var p=m.TryCast<IPunchable>();if(p==null||p.Pointer==excludedTarget||!p.CanBePunched()||m.transform.IsChildOf(interaction.transform))continue;
                var t=p.GetTransform();var c=m.GetComponent<Collider>()??m.GetComponentInChildren<Collider>();
                if(c==null||!c.enabled||c.isTrigger||c.bounds.size.magnitude>6)continue;
                float d=(c.bounds.center-interaction.transform.position).sqrMagnitude;
                if(d<nearest){nearest=d;target=p;targetCollider=c;targetTransform=t;}
            }
            Check(target!=null,"No existing punchable collision fixture");targetPosition=targetTransform.position;
        }
        var hand=left?ControllerTransport.Left:ControllerTransport.Right;
        Check(MotionBody.TryPunchGeometry(interaction,left,out var axis,out var offset),"No fist geometry");
        Check(StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var grip,out var q),"No world fist pose");
        var desired=grip+q*offset+q*axis*.8f;
        targetTransform.position+=desired-targetCollider.bounds.center;Physics.SyncTransforms();
        Plugin.Line($"PUNCH_TARGET left={left} name={targetTransform.name} center={targetCollider.bounds.center:R}");
    }
    private static void RestoreTarget(){if(targetTransform!=null){targetTransform.position=targetPosition;Physics.SyncTransforms();}}
    private static void PunchInput(InteractionManager __instance){if(__instance.punchPressedThisFrame)++punchInputs;}
    private static void AnimationTriggered(){++animations;}
    private static void Hit(Vector3 hitPoint,IPunchable target){++hits;Plugin.Line($"MOTION_PUNCH_HIT target={target.GetTransform().name} p={hitPoint:R}");}
    private static void ColliderInitialized(PunchCollider __instance,Vector3 direction,float force,float range,float radius)
    {
        ++volumes;
        Plugin.Line($"PUNCH_COLLIDER center={__instance.transform.position:R} direction={direction:R} force={force:R} range={range:R} radius={radius:R}");
    }
}



