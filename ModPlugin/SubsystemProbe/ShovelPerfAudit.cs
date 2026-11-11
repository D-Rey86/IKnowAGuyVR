using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
// Identical cold fixture injected into baseline/candidate diagnostic snapshots.
internal static class ShovelPerfAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_SHOVEL_PERFORMANCE")=="1";
 private static int stage;private static float next=25;private static InteractionManager owner;private static Shovel shovel;private static BoxCollider ground;
 internal static bool Ready{get;private set;}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static string Pose(bool left,Vector3 p,Quaternion q)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var wp,out var wq);var basis=wq*Quaternion.Inverse(h.Rotation);
  var raw=h.Position+Quaternion.Inverse(basis)*(p-wp);var a=(Quaternion.Inverse(basis)*q).eulerAngles;
  return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {raw.x:R} {raw.y:R} {-raw.z:R} {-a.y:R} {-a.x:R} {-a.z:R}");
 }
 internal static void Tick()
 {
  if(!enabled||stage==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{
   switch(stage)
   {
    case 0:
     owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
     foreach(var s in Shoveable.siteRegistry)if(s.CanBeDug()&&s.linkedPrize!=null){ground=s.GetComponent<BoxCollider>();break;}
     if(ground==null)throw new Exception("No performance site");
     Shovel template=null;foreach(var s in Resources.FindObjectsOfTypeAll<Shovel>())if(!s.gameObject.scene.IsValid()){template=s;break;}
     var obj=UnityEngine.Object.Instantiate(template.gameObject);obj.transform.position=owner.transform.position+Vector3.up*3;obj.SetActive(true);obj.GetComponent<NetworkObject>().Spawn();shovel=obj.GetComponent<Shovel>();
     owner.movementController.Teleport(ground.bounds.center-owner.movementController.transform.forward*.6f);Send("input clear\nhands reset\nhead rot 0 0 0");break;
    case 1:if(!owner.TryPickupItem(shovel.Cast<IHoldable>()))throw new Exception("Performance pickup failed");break;
    case 2:
     var b=ground.bounds;if(!ground.Raycast(new Ray(b.center+Vector3.up*(b.extents.y+1),Vector3.down),out var hit,b.size.y+2))throw new Exception("No performance surface");
     var blade=new Vector3(0,-.0009493373f,.002861014f);var handle=new Vector3(0,1.259918f,-.06762494f);var scale=shovel.transform.lossyScale;var tip=hit.point+Vector3.up*.3f;
     var r=ControllerTransport.Right;var authored=Quaternion.Euler(owner.GetHoldingConfiguration(shovel.gameObject).rotation);var offset=Quaternion.Inverse(r.Rotation)*r.AimRotation;var gripRotation=Quaternion.Inverse(authored)*Quaternion.Inverse(offset);
     StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out _,out var lq);
     Send(Pose(false,tip+Vector3.Scale(handle-blade,scale),gripRotation)+"\n"+Pose(true,tip+Vector3.Scale(new Vector3(0,.725f,-.067f)-blade,scale),lq)+"\ngrip r 1");break;
    case 3:ObjectGrabs.AdoptDistance(owner,shovel.gameObject,false);Send("grip l 1");break;
    case 4:if(!owner.heldItems.Contains(shovel.gameObject))throw new Exception("Performance tool was lost");Ready=true;stage=99;Plugin.Line("SHOVEL_PERFORMANCE_READY");return;
   }
   stage++;next=Time.realtimeSinceStartup+.65f;
  }catch(Exception e){Plugin.Line("SHOVEL_PERFORMANCE_FAILED "+e);stage=99;}
 }
}
