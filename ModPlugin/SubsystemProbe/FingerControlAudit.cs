using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// Isolated native-input/avatar check; excluded from normal builds.
internal static class FingerControlAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_FINGER_CONTROL_AUDIT")=="1";
 private static int phase,renderFrame=-1,restores,punchInputs,strikes;private static float next=25;
 private static Animator avatar;private static Transform[] all;private static Transform[] fingers=new Transform[30];
 private static readonly Transform[] wrists=new Transform[2],indexTips=new Transform[2];private static readonly Vector3[] indexDirections=new Vector3[2];
 private static readonly Vector3[] predictedIndex=new Vector3[2];private static int predictedFrame=-1;private static float maxIndexError;
 internal static void SamplePrediction()
 {
  if(!enabled||phase==99||owner==null||owner.IsHoldingItem())return;predictedFrame=-1;bool valid=true;
  for(int i=0;i<2;i++){var h=i==0?ControllerTransport.Left:ControllerTransport.Right;if(!MotionBody.TryIndexGeometry(owner,i==0,out var tip,out _)||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q)){valid=false;break;}predictedIndex[i]=p+q*tip;}if(valid)predictedFrame=Time.frameCount;
 }
 private static Vector3[] positions,scales;private static Quaternion[] rotations;
 private static readonly Quaternion[] seen=new Quaternion[30],relaxed=new Quaternion[30],pointed=new Quaternion[30],closed=new Quaternion[30],held=new Quaternion[15];
 private static bool saved;private static float maxP,maxQ,maxS;private static InteractionManager owner;private static GameObject item;
 private static void Check(bool value,string why){if(!value)throw new Exception(why);}
 private static void Send(string command)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
 private static void Fail(Exception e){phase=99;Plugin.Line("FINGER_CONTROL_FAILED "+e);Send("input clear\nhands reset");}
 private static void Punch(){punchInputs++;}
 private static void Strike(){strikes++;}
 internal static void Before(Animator animator)
 {
  if(!enabled||phase==99||owner==null)return;
  if(avatar==null){avatar=animator;var nodes=animator.GetComponentsInChildren<Transform>(true);all=new Transform[nodes.Length];positions=new Vector3[nodes.Length];scales=new Vector3[nodes.Length];rotations=new Quaternion[nodes.Length];for(int i=0;i<all.Length;i++)all[i]=nodes[i];for(int i=0;i<30;i++)fingers[i]=animator.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal+i));for(int i=0;i<2;i++){wrists[i]=animator.GetBoneTransform(i==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);indexTips[i]=fingers[i*15+5]??fingers[i*15+4];if(indexTips[i].childCount>0)indexTips[i]=indexTips[i].GetChild(0);}}
  for(int i=0;i<all.Length;i++){positions[i]=all[i].localPosition;scales[i]=all[i].localScale;rotations[i]=all[i].localRotation;}saved=true;
 }
 internal static void Rendered()
 {if(!enabled||!saved)return;try{for(int i=0;i<30;i++)if(fingers[i]!=null)seen[i]=fingers[i].localRotation;for(int i=0;i<2;i++){indexDirections[i]=(indexTips[i].position-wrists[i].position).normalized;if(predictedFrame==Time.frameCount){float error=Vector3.Distance(predictedIndex[i],indexTips[i].position);maxIndexError=Math.Max(maxIndexError,error);Check(error<.001f,"Curled index FK mismatch "+error);}}renderFrame=Time.frameCount;}catch(Exception e){Fail(e);}}
 private static string FaceFingers(bool left)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var q)&&StereoTrial.TryHeadWorld(out _,out _),"Missing view pose");StereoTrial.TryHeadWorld(out var head,out var headQ);MotionBody.TryCarryFrame(left,out _,out _,out var normal,out _,out _);
  var forward=Vector3.ProjectOnPlane(headQ*Vector3.forward,Vector3.up).normalized;var right=Vector3.Cross(Vector3.up,forward);var p=head+forward*.55f-Vector3.up*.15f+right*(left?-.18f:.18f);
  var localIndex=Quaternion.Inverse(q)*indexDirections[left?0:1];var rotation=Quaternion.LookRotation(Vector3.up,head-p)*Quaternion.Inverse(Quaternion.LookRotation(localIndex,normal));
  return PhysicalItemAudit.Pose(left,p,rotation);
 }
 internal static void Restored()
 {
  if(!saved)return;saved=false;
  try{for(int i=0;i<all.Length;i++){maxP=Math.Max(maxP,Vector3.Distance(positions[i],all[i].localPosition));maxS=Math.Max(maxS,Vector3.Distance(scales[i],all[i].localScale));maxQ=Math.Max(maxQ,1-Mathf.Abs(Quaternion.Dot(rotations[i],all[i].localRotation)));}restores++;Check(maxP<1e-6&&maxQ<1e-6&&maxS<1e-6,"Avatar restoration leaked");}catch(Exception e){Fail(e);}
 }
 private static float[][] PoseRows(Quaternion[] pose){var rows=new float[pose.Length][];for(int i=0;i<pose.Length;i++)rows[i]=new[]{pose[i].x,pose[i].y,pose[i].z,pose[i].w};return rows;}
 private static void Match(float leftGrip,float leftTrigger,float rightGrip,float rightTrigger)
 {
  Check(renderFrame>=Time.frameCount-2,"No current rendered pose");var l=ControllerTransport.Left;var r=ControllerTransport.Right;
  Check(Mathf.Abs(l.GripValue-leftGrip)<.01f&&Mathf.Abs(l.TriggerValue-leftTrigger)<.01f&&Mathf.Abs(r.GripValue-rightGrip)<.01f&&Mathf.Abs(r.TriggerValue-rightTrigger)<.01f,"Runtime actions not acknowledged");
  for(int i=0;i<30;i++)if(fingers[i]!=null){bool index=i%15>=3&&i%15<6;float value=i<15?(index?leftTrigger:leftGrip):(index?rightTrigger:rightGrip);float grip=i<15?leftGrip:rightGrip;var expected=closed[i];if(value<1){var rest=index?Quaternion.Slerp(relaxed[i],pointed[i],grip):relaxed[i];expected=Quaternion.Slerp(rest,closed[i],value);}Check(1-Mathf.Abs(Quaternion.Dot(expected,seen[i]))<1e-6,"Finger mapping mismatch at "+i);}
 }
 internal static void Tick()
 {
  if(!enabled||phase==99||!StereoTrial.GameplayActive)return;
  if(Time.realtimeSinceStartup<next)return;
  try{switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
    var h=new Harmony("ikag-finger-control-audit");h.Patch(typeof(InteractionManager).GetMethod("HandlePunchPressed"),prefix:new HarmonyMethod(typeof(FingerControlAudit).GetMethod(nameof(Punch),BindingFlags.NonPublic|BindingFlags.Static)));h.Patch(typeof(InteractionManager).GetMethod("SpawnPunchCollider"),prefix:new HarmonyMethod(typeof(FingerControlAudit).GetMethod(nameof(Strike),BindingFlags.NonPublic|BindingFlags.Static)));
    Send("input clear\nhands reset\nhand l grip pose -.2 1.3 -.4 0 -90 0\nhand r grip pose .2 1.3 -.4 0 -90 0\nhead rot 0 -35 0");break;
   case 1:Check(!owner.IsHoldingItem()&&avatar!=null,"Empty-hand fixture missing");Array.Copy(seen,relaxed,30);Send(FaceFingers(true)+"\n"+FaceFingers(false));break;
   case 2:Send("shot finger-relaxed\ngrip l 1\ngrip r 1\ntrigger l 1\ntrigger r 1");break;
   case 3:
    Array.Copy(seen,closed,30);foreach(int at in new[]{0,3,6,9,12,15,18,21,24,27})Check(1-Mathf.Abs(Quaternion.Dot(relaxed[at],closed[at]))>.005f,"Native digit did not close: "+at);
    Match(1,1,1,1);Check(punchInputs==0&&strikes==0,"Stationary fist/RT invoked a punch");Send("shot finger-fists\ntrigger l 0\ntrigger r 0");break;
   case 4:Array.Copy(seen,pointed,30);Match(1,0,1,0);Send("shot finger-pointing\ngrip l 0\ngrip r 0\ntrigger l 1\ntrigger r 1");break;
   case 5:Match(0,1,0,1);Send("shot finger-index-only\ngrip l 1\ntrigger l 0");break;
   case 6:Match(1,0,0,1);Send("shot finger-independent\ngrip r 1\ntrigger r 0");break;
   case 7:Match(1,0,1,0);Send("hand r to grip .2 1.3 -.95 0 -90 0 150");break;
   case 8:Check(strikes==0,"Pointing thrust punched");Send("hands reset\ngrip l .5\ngrip r .5\ntrigger l .5\ntrigger r .5");break;
   case 9:Match(.5f,.5f,.5f,.5f);Send("input clear\nhands reset");break;
   case 10:
    Match(0,0,0,0);Hammer source=null;foreach(var x in Resources.FindObjectsOfTypeAll<Hammer>())if(!x.gameObject.scene.IsValid()){source=x;break;}Check(source!=null,"No held-item fixture");item=UnityEngine.Object.Instantiate(source.gameObject);item.transform.position=owner.transform.position+Vector3.up*3;item.SetActive(true);item.GetComponent<Unity.Netcode.NetworkObject>().Spawn();Check(owner.TryPickupItem(item.GetComponent<Hammer>().Cast<IHoldable>()),"Held fixture pickup failed");Send("grip r 1");break;
   case 11:ObjectGrabs.AdoptDistance(owner,item,false);break;
   case 12:Array.Copy(seen,15,held,0,15);Send("grip l 1\ntrigger l 1\ntrigger r 1");break;
   case 13:
    Check(owner.heldItems.Contains(item),"Trigger released held item");for(int i=15;i<30;i++)if(fingers[i]!=null)Check(1-Mathf.Abs(Quaternion.Dot(held[i-15],seen[i]))<1e-6,"Held grip changed with trigger");Check(punchInputs==0&&strikes==0,"Finger controls invoked punch path");
    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"finger-poses.json"),System.Text.Json.JsonSerializer.Serialize(new {relaxed=PoseRows(relaxed),pointed=PoseRows(pointed),fist=PoseRows(closed)},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    Check(restores>100,"Restoration coverage too short");Plugin.Line("FINGER_CONTROL_COMPLETE restores="+restores+" bones="+all.Length+" position="+maxP+" rotation="+maxQ+" scale="+maxS+" pointingStrikes="+strikes+" buttonPunches="+punchInputs+" indexError="+maxIndexError);Send("input clear\nhands reset");phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+.8f;}catch(Exception e){Fail(e);}
 }
}
