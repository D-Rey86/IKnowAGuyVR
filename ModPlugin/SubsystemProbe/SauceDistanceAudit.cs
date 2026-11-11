using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;

// Isolated mechanical fixture: native ownership/input and authored lever arc.
// Empty fixture is enabled explicitly; this does not claim recipe acceptance.
internal static class SauceDistanceAudit
{
    private static int phase,index;private static float next=25,peak;private static SauceHandle sauce;private static GameObject clone;
    private static Vector3 contact,destination,normal,origin,grabLocal;private static float startValue,expected;
    private static bool Extended=>Environment.GetEnvironmentVariable("IKAG_XR_SAUCE_EXTENDED")=="1";
    private static int PerHand=>Extended?15:3;private static bool Left=>index>=PerHand;
    private static readonly float[] offsets={0,.15f,.6f,-.15f,-.6f};
    private static float Offset=>offsets[index%(Extended?5:3)];
    private static void Aim(float fraction)
    {
        var t=sauce.transform;expected=Mathf.Lerp(startValue,sauce.endRotationX,fraction);
        destination=t.parent.TransformPoint(t.localPosition+Quaternion.AngleAxis(expected-startValue,Vector3.right)*(grabLocal-t.localPosition));
        Send(MotionPointer.AimAtWorld(destination,Left));
    }
    private static void VerifyTarget()
    {
        float error=Mathf.Abs(sauce.targetRotationX-expected);
        Plugin.Line($"SAUCE_PARTIAL index={index} expected={expected:R} target={sauce.targetRotationX:R} error={error:R}");
        Check(error<.1f,"partial/reverse target inaccurate "+error);
    }
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void Check(bool b,string s){if(!b)throw new Exception(s);}
    private static string Hand(Vector3 p){StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var z,out var q);p=Quaternion.Inverse(q)*(p-z);return FormattableString.Invariant($"hand {(Left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");}
    internal static void Tick()
    {
        if(Environment.GetEnvironmentVariable("IKAG_XR_RUNTIME_ISSUE_AUDIT")!="sauce"||phase==99||!StereoTrial.GameplayActive)return;
        if(sauce!=null){sauce.isEnabled=true;if(phase==10)peak=Mathf.Max(peak,sauce.GetProgress());}
        if(Time.realtimeSinceStartup<next)return;
        try{float delay=.7f;switch(phase)
        {
            case 0:
                var im=InteractionManager.LocalInstance;im.movementController.Teleport(im.transform.position+Vector3.up*20);im.movementController.enabled=false;
                var source=Resources.FindObjectsOfTypeAll<SauceHandle>()[0].GetComponentInParent<MachineInteractionBase>(true);
                clone=UnityEngine.Object.Instantiate(source.gameObject);clone.SetActive(true);var net=clone.GetComponent<NetworkObject>();if(!net.IsSpawned)net.Spawn();
                sauce=clone.GetComponentInChildren<SauceHandle>(true);var cam=im.interactionCamera;
                var machine=clone.GetComponent<MachineInteractionBase>();clone.transform.rotation=Quaternion.FromToRotation(Vector3.ProjectOnPlane(machine.CameraViewPoint.forward,Vector3.up),Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up))*clone.transform.rotation;
                Physics.SyncTransforms();clone.transform.position+=cam.transform.position+cam.transform.forward*1.5f-Vector3.up*.25f-sauce.GetComponentInChildren<Collider>().bounds.center;delay=2;break;
            case 1:
                sauce.ResetHandle();Physics.SyncTransforms();contact=sauce.GetComponentInChildren<Collider>().bounds.center;
                normal=sauce.transform.parent.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.right).normalized;
                var front=Vector3.ProjectOnPlane(InteractionManager.LocalInstance.interactionCamera.transform.position-contact,normal).normalized;
                if(Extended)front=Quaternion.AngleAxis(((index%PerHand)/5-1)*30,normal)*front;
                origin=contact+front*1.2f+normal*Offset;Send("input clear\n"+Hand(origin));break;
            case 2:Send(MotionPointer.AimAtWorld(contact,Left));break;
            case 3:
                var hit=InteractionManager.LocalInstance;MotionPointer.TryControllerRay(Left,out var ray);
                Plugin.Line($"SAUCE_BASELINE_ACQUIRE index={index} left={Left} offset={Offset} hit={hit.rayHit.collider?.name} desired={contact} rayOrigin={ray.origin} dot={Vector3.Dot(normal,ray.direction):R}");Send(Left?"grip l 1":"grip r 1");break;
            case 4:
                Check(KitchenHands.DistancePoint(out var actual),"distance acquisition failed");Check(KitchenHands.RemoteLeft==Left,"wrong hand");
                grabLocal=sauce.transform.parent.InverseTransformPoint(actual);startValue=sauce.currentRotationX;
                Plugin.Line($"SAUCE_TRAVEL_START index={index} start={startValue} end={sauce.endRotationX} contact={actual} threshold={sauce.sauceThreshold} full={sauce.requireFullRotation}");
                if(!Extended){Aim(1);phase=10;peak=0;next=Time.realtimeSinceStartup+2;return;}
                Aim(.25f);break;
            case 5:
                VerifyTarget();Aim(.65f);break;
            case 6:VerifyTarget();Aim(.35f);break;
            case 7:VerifyTarget();delay=1;break;
            case 8:VerifyTarget();Aim(1);peak=0;phase=10;next=Time.realtimeSinceStartup+2;return;
            case 10:
                MotionPointer.TryControllerRay(Left,out var endRay);Plugin.Line($"SAUCE_TRAVEL_RESULT index={index} offset={Offset} left={Left} peak={peak:R} progress={sauce.GetProgress():R} current={sauce.currentRotationX:R} target={sauce.targetRotationX:R} dot={Vector3.Dot(normal,endRay.direction):R}");
                Check(peak>.99f,"full travel incomplete");Send("grip l 0\ngrip r 0");break;
            case 11:
                Check(!MachineInteractionBase.IsLocalPlayerInMachineMode,"ownership not released");if(++index<PerHand*2){phase=1;next=Time.realtimeSinceStartup+1;return;}
                UnityEngine.Object.Destroy(clone);Plugin.Line("SAUCE_DISTANCE_AUDIT_COMPLETE");phase=99;return;
        }phase++;next=Time.realtimeSinceStartup+delay;
        }catch(Exception e){Plugin.Line("SAUCE_DISTANCE_AUDIT_FAILED phase="+phase+" index="+index+" "+e);Send("grip l 0\ngrip r 0");phase=99;}
    }
}
