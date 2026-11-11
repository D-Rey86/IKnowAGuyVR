using System;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in cold census; the separate control mode exercises native digging
// only inside the isolated, profile-backed-up diagnostic session.
internal static class ShovelAudit
{
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_SHOVEL_AUDIT");
 private static readonly bool enabled=mode!=null;
 private static bool done;
 private static Shoveable target;private static int phase;private static float next,before,stopped;
 internal static void Tick()
 {
  if(!enabled||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<25)return;
  if(done){if(mode=="control")Control();return;}
  done=true;
  UiInputAudit.Dump("Assembly-CSharp.dll","","InteractionManager","HandleDigging","ExitDiggingMode");
  UiInputAudit.Dump("Assembly-CSharp.dll","","Shovel","UpdateDigPose","GetRigidbody");
  UiInputAudit.Dump("Assembly-CSharp.dll","","Shoveable","Update","CanBeDug","SetLocalDigging","SetDiggingRpc","ApplyProgressVisual","RefreshBuriedItem");
  UiInputAudit.Dump("Assembly-CSharp.dll","","DigsitePrize","CanInteract","CollectRpc");
  int count=0;
  var sites=Shoveable.siteRegistry;
  if(sites!=null)foreach(var site in sites)
  {
   if(site==null)continue;count++;
   Plugin.Line("SHOVEL_SITE id="+site.siteId+" position="+site.transform.position.ToString("R")+" canDig="+site.CanBeDug()+" progress="+site.Progress+" seconds="+site.digSeconds+" reveal="+site.revealProgress+" refill="+site.refillPerSecond+" rebury="+site.reburying+" chest="+site.linkedChest?.name+" prize="+site.linkedPrize?.name);
   foreach(var collider in site.GetComponentsInChildren<Collider>(true)){Plugin.Line("SHOVEL_SITE_COLLIDER site="+site.siteId+" node="+collider.name+" type="+collider.GetIl2CppType().Name+" enabled="+collider.enabled+" trigger="+collider.isTrigger+" bounds="+collider.bounds);var b=collider.TryCast<BoxCollider>();if(b!=null)Plugin.Line("SHOVEL_SITE_BOX size="+b.size.ToString("R")+" center="+b.center.ToString("R")+" rotation="+b.transform.rotation.ToString("R")+" scale="+b.transform.lossyScale.ToString("R")+" depth="+site.digDepth);}
  }
  foreach(var shovel in Resources.FindObjectsOfTypeAll<Shovel>())
   Plugin.Line("SHOVEL_TOOL name="+shovel.name+" scene="+shovel.gameObject.scene.name+" scale="+shovel.transform.lossyScale.ToString("R")+" strokes="+shovel.strokesPerSecond+" depth="+shovel.strokeDepth+" reach="+shovel.strokeReach+" lean="+shovel.bladeLean+" tilt="+shovel.strokeTilt);
  Plugin.Line("SHOVEL_OBSERVATION_COMPLETE sites="+count);
  if(mode=="control")foreach(var site in sites)if(site!=null&&site.CanBeDug()&&site.linkedPrize!=null){target=site;break;}
 }
 private static void Control()
 {
  if(target==null||phase==99||Time.realtimeSinceStartup<next)return;
  try{
   if(phase==0){before=target.Progress;target.SetLocalDigging(true);Plugin.Line("SHOVEL_CONTROL_START before="+before+" pos="+target.transform.position+" diggers="+target.anyoneDigging.Value);next=Time.realtimeSinceStartup+.8f;}
   else if(phase==1){stopped=target.Progress;target.SetLocalDigging(false);Plugin.Line("SHOVEL_CONTROL_STOP delta="+(stopped-before)+" pos="+target.transform.position+" bounds="+target.GetComponent<BoxCollider>().bounds+" diggers="+target.anyoneDigging.Value);if(stopped<=before)throw new Exception("Native digging did not advance");next=Time.realtimeSinceStartup+.8f;}
   else if(phase==2){if(Math.Abs(target.Progress-stopped)>.001f)throw new Exception("Progress continued after stop");target.SetLocalDigging(true);next=Time.realtimeSinceStartup+target.digSeconds;}
   else{target.SetLocalDigging(false);if(!target.IsFullyDug)throw new Exception("Native completion failed");Plugin.Line("SHOVEL_CONTROL_COMPLETE progress="+target.Progress+" prizeUnlocked="+target.linkedPrize.IsDugOut+" collected="+target.linkedPrize.IsCollected+" diggers="+target.anyoneDigging.Value+" pos="+target.transform.position);phase=99;return;}
   phase++;
  }catch(Exception e){target.SetLocalDigging(false);phase=99;Plugin.Line("SHOVEL_CONTROL_FAILED "+e);}
 }
}
