using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ShovelPlayAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_SHOVEL_PLAY")=="1";
 private static int phase,renderPhase=-1,repeat;
 private static float next=26,height,previous,stopped,moveAt;
 private static InteractionManager owner;private static Shovel tool;private static Shoveable site,second,locked;
 private static BoxCollider box;private static Quaternion authored;private static bool move,moveBoth,left,comfort;
 private static RaycastHit hit;
 private static Vector3 playerStart;
 private static float tracedAt;
 private static int pulsesLeft,pulsesRight,completions;
 private static Transform[] bones;private static Vector3[] positions,scales;private static Quaternion[] rotations;private static int savedPhase=-1,restoreCount;private static bool pendingRestore;private static float maxPosition,maxRotation,maxScale;
 internal static void Before(Animator animator)
 {
  if(!enabled||savedPhase==phase)return;savedPhase=phase;
  if(bones==null){var all=animator.GetComponentsInChildren<Transform>(true);bones=new Transform[all.Length];positions=new Vector3[all.Length];rotations=new Quaternion[all.Length];scales=new Vector3[all.Length];for(int i=0;i<all.Length;i++)bones[i]=all[i];}
  for(int i=0;i<bones.Length;i++){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;scales[i]=bones[i].localScale;}pendingRestore=true;
 }
 internal static void Restored()
 {
  if(!pendingRestore)return;pendingRestore=false;restoreCount++;
  for(int i=0;i<bones.Length;i++){maxPosition=Mathf.Max(maxPosition,Vector3.Distance(positions[i],bones[i].localPosition));maxScale=Mathf.Max(maxScale,Vector3.Distance(scales[i],bones[i].localScale));maxRotation=Mathf.Max(maxRotation,Mathf.Abs(1-Mathf.Abs(Quaternion.Dot(rotations[i],bones[i].localRotation))));}
  Check(maxPosition<1e-6f&&maxScale<1e-6f&&maxRotation<1e-6f,"Rig restoration drift");
 }
 internal static void Haptic(bool left,float strength,bool sent){if(!enabled||!sent)return;if(left)pulsesLeft++;else pulsesRight++;if(strength>=.6f)completions++;}
 private static void Check(bool v,string why){if(!v)throw new Exception(why);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static Vector3 Surface(){var b=box.bounds;Check(box.Raycast(new Ray(b.center+Vector3.up*(b.extents.y+1),Vector3.down),out var contact,b.size.y+2),"Independent native surface oracle");return contact.point;}
 private static string Pose(bool useLeft,Vector3 position,Quaternion rotation)
 {
  var h=useLeft?ControllerTransport.Left:ControllerTransport.Right;
  Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out var q),"No tracked frame");
  var basis=q*Quaternion.Inverse(h.Rotation);var raw=h.Position+Quaternion.Inverse(basis)*(position-p);var angles=(Quaternion.Inverse(basis)*rotation).eulerAngles;
  // xr-sim's quat_from_ypr rolls about -Z; Unity's roll is about +Z.
  return FormattableString.Invariant($"hand {(useLeft?"l":"r")} grip pose {raw.x:R} {raw.y:R} {-raw.z:R} {-angles.y:R} {-angles.x:R} {-angles.z:R}");
 }
 private static void ToolPose(float above,bool both=false,string prefix=null)
 {
  var h=left?ControllerTransport.Left:ControllerTransport.Right;
  Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var q),"No primary pose");
  var aimOffset=Quaternion.Inverse(h.Rotation)*h.AimRotation;
  var basis=q*Quaternion.Inverse(h.Rotation);
  var desiredGrip=Quaternion.Inverse(authored)*Quaternion.Inverse(aimOffset);
  var tip=Surface()+Vector3.up*above;var scale=tool.transform.lossyScale;
  var grip=tip+Vector3.Scale(MotionShovel.Handle-MotionShovel.Blade,scale);
  string command=Pose(left,grip,desiredGrip);
  if(both){var local=MotionShovel.SupportPoint;var point=tip+Vector3.Scale(local-MotionShovel.Blade,scale);var other=left?ControllerTransport.Right:ControllerTransport.Left;StereoTrial.TryTrackingPose(other.Position,other.Rotation,out _,out var oq);command+="\n"+Pose(!left,point,oq);}
  Send(prefix==null?command:prefix+"\n"+command);
 }
 private static void BeginMove(bool both=false){height=.3f;moveAt=Time.realtimeSinceStartup;move= true;moveBoth=both;ToolPose(height,both);}
 internal static void Rendered()
 {
  if(!enabled||tool==null||owner==null||renderPhase==phase||!owner.IsHoldingItem())return;
  renderPhase=phase;float a=0,b=0;
  if(MotionShovel.Supporting)
  {
   bool primary=MotionShovel.HandPose(left,out var p,out _),secondary=MotionShovel.HandPose(!left,out var op,out _);Check(primary&&secondary,"Missing bilateral render pose");
   a=Vector3.Distance(p,MotionBody.RenderedContact(left,false,false));b=Vector3.Distance(op,MotionBody.RenderedContact(!left,false,false));
   Check(a<.001f&&b<.001f,"Hand contact leaked away from shovel");
  }
  Plugin.Line("SHOVEL_RENDER phase="+phase+" support="+MotionShovel.Supporting+" primaryError="+a+" secondaryError="+b);
 }
 private static void Inject(InteractionManager __instance){if(enabled&&comfort&&__instance==owner){__instance.hasHit=true;__instance.rayHit=hit;}}
 internal static void Tick()
 {
  if(!enabled||phase==99||(!StereoTrial.GameplayActive&&phase<45))return;
  try{
   if(move)
   {
    if(Time.realtimeSinceStartup>=tracedAt){tracedAt=Time.realtimeSinceStartup+.1f;Plugin.Line("SHOVEL_MOVE_TRACE phase="+phase+" "+MotionShovel.DebugState);}
    float elapsed=Time.realtimeSinceStartup-moveAt;height=.3f-.7f*elapsed;
    ToolPose(Mathf.Max(-.04f,height),moveBoth);
    if(height>-.04f)return;move=false;next=Time.realtimeSinceStartup+1.1f;return;
   }
   if(Time.realtimeSinceStartup<next)return;
   switch(phase)
   {
    case 0:
     owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
     foreach(var s in Shoveable.siteRegistry){if(s.CanBeDug()&&s.linkedPrize!=null){if(site==null)site=s;else if(second==null)second=s;}else if(locked==null)locked=s;}
     Check(site!=null&&second!=null&&locked!=null,"No eligible/locked sites");box=site.GetComponent<BoxCollider>();playerStart=owner.movementController.transform.position;
     owner.movementController.Teleport(Surface()-owner.movementController.transform.forward*.6f);MotionShovel.Refresh(site);
     Shovel source=null;foreach(var s in Resources.FindObjectsOfTypeAll<Shovel>())if(!s.gameObject.scene.IsValid()){source=s;break;}
     Check(source!=null,"No shovel prefab");var obj=UnityEngine.Object.Instantiate(source.gameObject);obj.transform.position=owner.transform.position+Vector3.up*3;obj.SetActive(true);obj.GetComponent<NetworkObject>().Spawn();tool=obj.GetComponent<Shovel>();
     new Harmony("iknowaguyvr.shovel-fixture").Patch(typeof(InteractionManager).GetMethod("PerformRaycast"),postfix:new HarmonyMethod(typeof(ShovelPlayAudit).GetMethod(nameof(Inject),BindingFlags.NonPublic|BindingFlags.Static)));
     Send("input clear\nhands reset\nhead rot 0 -45 0");break;
    case 1:Check(owner.TryPickupItem(tool.Cast<IHoldable>()),"Native shovel pickup");authored=Quaternion.Euler(owner.GetHoldingConfiguration(tool.gameObject).rotation);break;
    case 2:ToolPose(.3f);break;
    case 3:
     Check(MotionHeldItems.TryAnchor(owner,out var p,out var q,out var rel),"No shovel pose");var tip=p+(q*rel)*Vector3.Scale(MotionShovel.Blade,tool.transform.lossyScale);
     Plugin.Line("SHOVEL_FIXTURE_POSE tip="+tip.ToString("R")+" wanted="+(Surface()+Vector3.up*.3f).ToString("R")+" root="+(q*rel).ToString("R")+" authored="+authored);
     Check(Vector3.Distance(tip,Surface()+Vector3.up*.3f)<.003f&&Quaternion.Angle(q*rel,Quaternion.identity)<.1f,"Fixture tracking-to-tool conversion");Send("grip r 1");break;
    case 4:ObjectGrabs.AdoptDistance(owner,tool.gameObject,false);previous=site.Progress;break;
    case 5:Check(site.Progress==previous&&MotionShovel.ActiveSite==null,"Stationary tool dug");Plugin.Line("SHOVEL_STATIONARY_PASS");Send("stick l 0 .5");next=Time.realtimeSinceStartup+1;phase++;return;
    case 6:Check(site.Progress==previous,"Walking dug");Plugin.Line("SHOVEL_WALK_PASS");owner.movementController.Teleport(Surface()-owner.movementController.transform.forward*.6f);ToolPose(.3f,prefix:"stick l 0 0");break;
    case 7:BeginMove();phase++;return;
    case 8:Check(site.Progress>previous,"Physical insertion did not dig");stopped=site.Progress;Plugin.Line("SHOVEL_INSERTION_PASS progress="+stopped);Send("shot shovel-one-hand");break;
    case 9:Check(Math.Abs(site.Progress-stopped)<.005f,"Stationary contact repeated progress");Plugin.Line("SHOVEL_RECOVERY_REQUIRED_PASS");ToolPose(.3f);break;
    case 10:
     Check(MotionHeldItems.TryAnchor(owner,out p,out q,out rel),"No shaft pose");var contact=p+q*rel*Vector3.Scale(new Vector3(0,.725f,-.067f),tool.transform.lossyScale);
     StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out _,out var lq);Send(Pose(true,contact,lq));break;
    case 11:Send("grip l 1");break;
    case 12:Check(MotionShovel.Supporting&&MotionShovel.OwnsGrip(true)&&!ObjectGrabs.LeftHand&&!DistanceGrips.Pending&&!KitchenHands.Acquiring,"Secondary shaft grip conflict");Plugin.Line("SHOVEL_SUPPORT_PASS");Send("shot shovel-two-hands");phase=120;next=Time.realtimeSinceStartup+.65f;return;
    case 120:
     StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out var supportGrip,out var supportRotation);Send(Pose(true,supportGrip+Vector3.right*.15f,supportRotation));phase=121;next=Time.realtimeSinceStartup+.65f;return;
    case 121:
     Check(MotionHeldItems.TryAnchor(owner,out p,out q,out rel),"No coupled rotation");StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var primaryGrip,out _);StereoTrial.TryTrackingPose(ControllerTransport.Left.Position,ControllerTransport.Left.Rotation,out supportGrip,out _);
     Check(Vector3.Dot((q*rel*Vector3.Scale(MotionShovel.SupportPoint-MotionShovel.Handle,tool.transform.lossyScale)).normalized,(supportGrip-primaryGrip).normalized)>.9999f,"Two-hand orientation coupling");Plugin.Line("SHOVEL_COUPLED_ROTATION_PASS");ToolPose(.3f,true);phase=122;next=Time.realtimeSinceStartup+.65f;return;
    case 122:BeginMove(true);phase=13;return;
    case 13:Check(site.Progress>stopped,"Two-hand insertion did not dig");Plugin.Line("SHOVEL_TWO_HAND_STROKE_PASS");Send("grip l 0");break;
    case 14:Check(!MotionShovel.Supporting&&owner.heldItems.Contains(tool.gameObject)&&!ObjectGrabs.LeftHand,"Support release transferred or dropped tool");Plugin.Line("SHOVEL_SUPPORT_RELEASE_PASS");Send("grip r 0");break;
    case 15:Check(!owner.heldItems.Contains(tool.gameObject)&&!MotionShovel.Supporting&&MotionShovel.ActiveSite==null,"Primary release did not drop and stop");Check(owner.TryPickupItem(tool.Cast<IHoldable>()),"Left native pickup");Send("grip l 1");left=true;break;
    case 16:ObjectGrabs.AdoptDistance(owner,tool.gameObject,true);ToolPose(.3f);previous=site.Progress;break;
    case 17:BeginMove();phase++;return;
    case 18:Check(site.Progress>previous&&ObjectGrabs.LeftHand,"Left-hand insertion did not dig");Plugin.Line("SHOVEL_LEFT_STROKE_PASS");
     Check(box.Raycast(new Ray(Surface()+Vector3.up,Vector3.down),out hit,2),"Comfort hit control");comfort=true;previous=site.Progress;Send("trigger r 1");break;
    case 19:Check(site.Progress>previous,"Comfort RT did not dig");Send("trigger r 0");break;
    case 20:stopped=site.Progress;comfort=false;break;
    case 21:Check(Math.Abs(site.Progress-stopped)<.005f,"Comfort release left digging active");Plugin.Line("SHOVEL_TRIGGER_PASS");
     if(!site.IsFullyDug){BeginMove();phase=22;return;}phase=23;next=Time.realtimeSinceStartup;return;
    case 22:if(!site.IsFullyDug&&repeat++<8){BeginMove();return;}Check(site.IsFullyDug,"Physical cycles did not complete native site");break;
    case 23:Check(site.linkedPrize.IsDugOut&&!site.linkedPrize.IsCollected&&MotionShovel.ActiveSite==null,"Native reward completion/cleanup");Plugin.Line("SHOVEL_COMPLETION_PASS");
     comfort=true;var lockedBox=locked.GetComponent<BoxCollider>();Check(lockedBox.Raycast(new Ray(lockedBox.bounds.center+Vector3.up,Vector3.down),out hit,2),"Locked hit control");previous=locked.Progress;Send("trigger r 1");break;
    case 24:Check(locked.Progress==previous&&MotionShovel.ActiveSite==null,"Locked quest site was excavated");Plugin.Line("SHOVEL_LOCKED_SITE_PASS");comfort=false;Send("trigger r 0");InventoryManager.LocalInstance.SetSelectedSlot(0);break;
    case 25:
     Check(!MotionShovel.Supporting&&MotionShovel.ActiveSite==null,"Deselect cleanup");
     site=second;box=site.GetComponent<BoxCollider>();owner.movementController.Teleport(Surface()-owner.movementController.transform.forward*.6f);MotionShovel.Refresh(site);InventoryManager.LocalInstance.SetSelectedSlot(7);Send("input clear");break;
    case 26:Check(owner.heldItems.Contains(tool.gameObject)||owner.TryPickupItem(tool.Cast<IHoldable>()),"Second-site pickup");Send("grip l 1");break;
    case 27:ObjectGrabs.AdoptDistance(owner,tool.gameObject,true);ToolPose(.3f);break;
    case 28:Check(box.Raycast(new Ray(Surface()+Vector3.up,Vector3.down),out hit,2),"Second-site ray");comfort=true;Send("trigger r 1");break;
    case 29:Check(site.Progress>0&&MotionShovel.ActiveSite==site,"Second site did not start");Send("hand l valid off");break;
    case 30:stopped=site.Progress;Check(MotionShovel.ActiveSite==null&&!MotionShovel.Supporting,"Tracking loss left digging active");break;
    case 31:Check(Math.Abs(site.Progress-stopped)<.005f,"Tracking-loss progress continued");Plugin.Line("SHOVEL_TRACKING_LOSS_PASS");Send("hand l valid on\ninput clear");comfort=false;break;
    case 32:ToolPose(.3f,prefix:"grip l 1");break;
    case 33:ObjectGrabs.AdoptDistance(owner,tool.gameObject,true);Check(box.Raycast(new Ray(Surface()+Vector3.up,Vector3.down),out hit,2),"Deselect ray");comfort=true;Send("trigger r 1");break;
    case 34:Check(MotionShovel.ActiveSite==site,"No active digging for deselect");InventoryManager.LocalInstance.SetSelectedSlot(0);break;
    case 35:stopped=site.Progress;Check(MotionShovel.ActiveSite==null&&!MotionShovel.Supporting,"Active deselection cleanup");comfort=false;Send("input clear");break;
    case 36:Check(Math.Abs(site.Progress-stopped)<.005f,"Deselected shovel continued digging");Check(pulsesLeft>0&&pulsesRight>0&&completions>0,"Haptic dispatch incomplete");Plugin.Line("SHOVEL_HAPTICS_PASS left="+pulsesLeft+" right="+pulsesRight+" completion="+completions);InventoryManager.LocalInstance.SetSelectedSlot(7);Send("input clear");break;
    case 37:Check(owner.heldItems.Contains(tool.gameObject)||owner.TryPickupItem(tool.Cast<IHoldable>()),"Focus fixture pickup");Send("grip l 1");break;
    case 38:ObjectGrabs.AdoptDistance(owner,tool.gameObject,true);ToolPose(.3f);Check(box.Raycast(new Ray(Surface()+Vector3.up,Vector3.down),out hit,2),"Focus ray");comfort=true;Send("trigger r 1");break;
    case 39:Check(MotionShovel.ActiveSite==site,"Focus fixture did not start");Send("focus lose 1000");break;
    case 40:stopped=site.Progress;Check(MotionShovel.ActiveSite==null,"Focus loss left digging active");break;
    case 41:Check(Math.Abs(site.Progress-stopped)<.005f,"Focus recovery resumed held trigger");Plugin.Line("SHOVEL_FOCUS_LOSS_PASS");comfort=false;Send("input clear");break;
    case 42:Plugin.Line("SHOVEL_RESTORATION_PASS samples="+restoreCount+" bones="+bones.Length+" position="+maxPosition+" rotation="+maxRotation+" scale="+maxScale);ToolPose(.3f,prefix:"grip l 1");break;
    case 43:Check(box.Raycast(new Ray(Surface()+Vector3.up,Vector3.down),out hit,2),"Exit ray");comfort=true;Send("trigger r 1");break;
    case 44:Check(MotionShovel.ActiveSite==site,"Exit fixture did not start");comfort=false;StereoTrial.DiagnosticMetrics();HUDManager.LocalInstance.ExitToMainMenu();break;
    case 45:if(!StereoTrial.MenuActive){next=Time.realtimeSinceStartup+.2f;return;}Check(MotionShovel.ActiveSite==null&&!MotionShovel.Supporting,"Menu retained shovel state");Plugin.Line("SHOVEL_SCENE_EXIT_PASS");Plugin.Line("SHOVEL_PLAY_COMPLETE");phase=99;Send("input clear\nhands reset");return;
   }
   phase++;next=Time.realtimeSinceStartup+.65f;
  }catch(Exception e){Plugin.Line("SHOVEL_FAILURE_STATE "+MotionShovel.DebugState);MotionInput.Trace();move=comfort=false;MotionShovel.Reset();Send("input clear\nhands reset");Plugin.Line("SHOVEL_PLAY_FAILED phase="+phase+" "+e);phase=99;}
 }
}
