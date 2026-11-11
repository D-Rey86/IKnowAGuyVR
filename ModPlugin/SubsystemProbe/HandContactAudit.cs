using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class HandContactAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_HAND_CONTACT_AUDIT")=="1";
    private static int phase;private static float next=25;
    private static InteractionManager owner;private static PlayerMovementController player;
    private static HingeJoint hinge;private static Rigidbody door;private static BoxCollider box;
    private static Vector3 origin,normal,contact,startPosition;private static Quaternion startRotation;
    private static Quaternion beforePush;
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void Check(bool b,string s){if(!b)throw new Exception(s);}
    private static string Hand(bool left,Vector3 center,bool move=false)
    {
        var h=left?ControllerTransport.Left:ControllerTransport.Right;
        Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q),"No hand world pose");
        Check(MotionBody.TryPalmGeometry(owner,left,out var offset,out var axis,out float len,out float radius),"No palm geometry");
        var local=h.Position+Quaternion.Inverse(q*Quaternion.Inverse(h.Rotation))*(center-q*offset-p);
        if(move)return FormattableString.Invariant($"hand {(left?"l":"r")} to grip {local.x:R} {local.y:R} {-local.z:R} 0 0 0 1000");
        return FormattableString.Invariant($"hand {(left?"l":"r")} grip pos {local.x:R} {local.y:R} {-local.z:R}");
    }
    internal static void Tick()
    {
        if(!enabled||phase>18||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;player=owner.GetComponent<PlayerMovementController>();origin=player.transform.position;
                    InventoryManager.LocalInstance.SetSelectedSlot(7);
                    foreach(var j in UnityEngine.Object.FindObjectsOfType<HingeJoint>())if(j.name=="Toilet_Door (3)"){hinge=j;break;}
                    Check(hinge!=null,"Native stall hinge not found");door=hinge.GetComponent<Rigidbody>();box=hinge.GetComponent<BoxCollider>();
                    startPosition=door.position;startRotation=door.rotation;contact=box.bounds.center;
                    var size=box.size;normal=box.transform.TransformDirection(size.z<size.x&&size.z<size.y?Vector3.forward:size.x<size.y?Vector3.right:Vector3.up).normalized;
                    var near=contact+normal*1.2f;
                    Check(Physics.Raycast(near+Vector3.up*2,Vector3.down,out var floor,6,1<<0,QueryTriggerInteraction.Ignore),"No measured floor");
                    player.Teleport(floor.point+Vector3.up*.05f);
                    Plugin.Line("HAND_CONTACT_NATIVE door="+door.name+" mass="+door.mass+" layers="+owner.interactionLayers.value+" normal="+normal+" contact="+contact+" floor="+floor.point);
                    Send("hands reset\ninput clear");break;
                case 1:
                    Check(HandPhysics.Body(false)!=null&&HandPhysics.Body(true)!=null,"Palm proxies missing");
                    MotionBody.TryPalmGeometry(owner,false,out var center,out var axis,out float length,out float radius);
                    Plugin.Line("HAND_CONTACT_GEOMETRY center="+center+" axis="+axis+" length="+length+" radius="+radius);
                    Check((owner.interactionLayers.value&(1<<HandPhysics.Body(false).gameObject.layer))==0,"Native interaction mask includes palm layer");
                    Send(Hand(false,contact+normal*.35f));break;
                case 2:beforePush=door.rotation;Check(HandPhysics.ContactEnabled(false),"Right contact disabled");Send(Hand(false,contact-normal*.5f,true));break;
                case 3:
                    Plugin.Line("HAND_CONTACT_RIGHT rotationDelta="+Quaternion.Angle(beforePush,door.rotation)+" speed="+door.angularVelocity.magnitude);
                    Check(Quaternion.Angle(beforePush,door.rotation)>5,"Right hand did not push native door");
                    Send("hands reset");break;
                case 4:door.position=startPosition;door.rotation=startRotation;door.linearVelocity=door.angularVelocity=Vector3.zero;Physics.SyncTransforms();break;
                case 5:Send(Hand(true,contact+normal*.35f));break;
                case 6:beforePush=door.rotation;Check(HandPhysics.ContactEnabled(true),"Left contact disabled");Send(Hand(true,contact-normal*.5f,true));break;
                case 7:
                    Plugin.Line("HAND_CONTACT_LEFT rotationDelta="+Quaternion.Angle(beforePush,door.rotation)+" speed="+door.angularVelocity.magnitude);
                    Check(Quaternion.Angle(beforePush,door.rotation)>5,"Left hand did not push native door");Send("hands reset\nfocus lose");break;
                case 8:Check(!HandPhysics.ContactEnabled(true)&&!HandPhysics.ContactEnabled(false),"Focus loss left contacts enabled");Send("focus regain");break;
                case 9:InventoryManager.LocalInstance.SetSelectedSlot(0);break;
                case 10:
                    Check(owner.IsHoldingItem(),"Held-item exclusion fixture missing");Check(!HandPhysics.ContactEnabled(false),"Occupied palm contact enabled");
                    Check(HandPhysics.ContactEnabled(true),"Free palm disabled");
                    var colliders=owner.cachedTopItem.GetComponentsInChildren<Collider>(true);
                    foreach(var c in colliders)Check(Physics.GetIgnoreCollision(HandPhysics.Body(true).GetComponent<Collider>(),c),"Held item not excluded");
                    InventoryManager.LocalInstance.SetSelectedSlot(7);break;
                case 11:Check(HandPhysics.ContactEnabled(false),"Empty right hand did not recover");Send("shot hands-contact-complete");StereoTrial.DiagnosticMetrics();Plugin.Line("HAND_CONTACT_ACCEPTANCE_COMPLETE");phase=19;return;
            }
            phase++;next=Time.realtimeSinceStartup+1.5f;
        }
        catch(Exception e){Plugin.Line("HAND_CONTACT_FAILED "+e);phase=19;Send("hands reset\ninput clear\nfocus regain");}
    }
}

