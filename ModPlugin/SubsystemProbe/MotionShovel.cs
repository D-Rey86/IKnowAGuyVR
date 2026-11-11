using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using N=System.Numerics.Vector3;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class MotionShovel
{
 // Shipped mesh landmarks: D-handle centre, blade cutting edge and straight shaft.
 internal static readonly Vector3 Handle=new(0,1.259918f,-.06762494f);
 internal static readonly Vector3 Blade=new(0,-.0009493373f,.002861014f);
 private const float ShaftLow=.506f,ShaftHigh=.944f,ShaftZ=-.067f,GrabRadius=.12f;
 private sealed class Site{internal Shoveable Target;internal BoxCollider Box;internal Transform Transform;internal Vector3 Centre,LocalCentre,Size,LocalNormal;internal int A,B;internal float Radius;}
 private static readonly Dictionary<IntPtr,Site> sites=new();
 private static readonly Dictionary<IntPtr,Site> colliders=new();
 private static InteractionManager owner;private static GameObject item;private static Shovel shovel;
 private static bool discovered,support,primaryLeft,wasOther,otherArmed,wasComplete,triggerOwned,soilContact,triggerNeedsRelease;
 private static int inputFrame=-1,digFrame=-1;
 private static Vector3 supportPoint;private static Quaternion supportFromTool,primaryFromTool;
 private static Site active,gestureSite;private static float until,nextStroke;
 private static readonly ShovelStroke gesture=new();
#if IKAG_DIAGNOSTICS
 private static Vector3 debugTip,debugRoom;private static float debugHeight;
 internal static string DebugState=>"ready="+MotionInput.GameplayReady+" focus="+Probe.Focused+" block="+InteractionManager.IsHeldItemUseBlocked+" wheel="+MotionInput.WheelOpen+" hud="+ImmersiveHud.InputOwned+" cursor="+Cursor.lockState+" left="+primaryLeft+" grip="+(primaryLeft?ControllerTransport.Left:ControllerTransport.Right).Grip+" site="+gestureSite?.Target?.siteId+" height="+debugHeight+" armed="+gesture.Armed+" tip="+debugTip.ToString("R")+" room="+debugRoom.ToString("R")+" apex="+gesture.Apex;
#endif
 internal static int Revision{get;private set;}
 internal static bool Supporting=>support&&shovel!=null&&owner!=null&&owner.cachedTopItem==item&&owner.IsHoldingItem();
 internal static bool OwnsGrip(bool left)=>Supporting&&left!=primaryLeft;
 private static HarmonyMethod Hook(string name)=>new(typeof(MotionShovel).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.physical-shovel");
  h.Patch(typeof(InteractionManager).GetMethod("HandleDigging"),prefix:Hook(nameof(Dig)));
  h.Patch(typeof(Shovel).GetMethod("UpdateDigPose"),prefix:Hook(nameof(KeepTrackedPose)));
  h.Patch(typeof(Shoveable).GetMethod("OnNetworkSpawn"),postfix:Hook(nameof(Spawned)));
  h.Patch(typeof(Shoveable).GetMethod("OnNetworkDespawn"),prefix:Hook(nameof(Despawned)));
 }
 private static void Spawned(Shoveable __instance)=>Register(__instance);
 private static void Despawned(Shoveable __instance)
 {
  if(sites.TryGetValue(__instance.Pointer,out var s)){if(active==s)Stop();if(gestureSite==s){gestureSite=null;gesture.Reset();}if(s.Box!=null)colliders.Remove(s.Box.Pointer);sites.Remove(__instance.Pointer);}
 }
 private static void Register(Shoveable target)
 {
  if(target==null||sites.ContainsKey(target.Pointer))return;
  var box=target.GetComponent<BoxCollider>();if(box==null)return;
  var transform=box.transform;var size=box.size;var dimensions=Vector3.Scale(size,transform.lossyScale);int thin=0;
  if(Mathf.Abs(dimensions.y)<Mathf.Abs(dimensions[thin]))thin=1;if(Mathf.Abs(dimensions.z)<Mathf.Abs(dimensions[thin]))thin=2;
  var normal=Vector3.zero;normal[thin]=1;
  if(Vector3.Dot(transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal),Vector3.up)<0)normal=-normal;
  var site=new Site{Target=target,Box=box,Transform=transform,LocalCentre=box.center,Size=size,LocalNormal=normal,A=(thin+1)%3,B=(thin+2)%3,Centre=transform.TransformPoint(box.center),Radius=dimensions.magnitude*.5f+target.digDepth};
  sites[target.Pointer]=site;colliders[box.Pointer]=site;
 }
 internal static void Reset()
 {Stop();ReleaseSupport();gesture.Reset();gestureSite=null;owner=null;item=null;shovel=null;discovered=false;sites.Clear();colliders.Clear();inputFrame=digFrame=-1;wasOther=otherArmed=soilContact=triggerNeedsRelease=false;until=nextStroke=0;}
 private static bool Allowed(InteractionManager o)=>MotionInput.Enabled&&MotionInput.GameplayReady&&StereoTrial.GameplayActive&&Probe.Focused&&o!=null&&o._IsOwner_k__BackingField&&o.IsInteractionEnabled&&!o.skatingSuppressed&&!InteractionManager.IsHeldItemUseBlocked&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive&&!ImmersiveHud.InputOwned&&Cursor.lockState==CursorLockMode.Locked;
 internal static void Input()
 {
  if(inputFrame==Time.frameCount)return;inputFrame=Time.frameCount;
  var current=InteractionManager.LocalInstance;
  var next=current==null?null:current.cachedTopItem;
  if(owner!=current||item!=next)
  {
   Stop();ReleaseSupport();gesture.Reset();gestureSite=null;owner=current;item=next;shovel=next==null?null:next.GetComponent<Shovel>();otherArmed=false;
  }
  if(shovel==null){Stop();ReleaseSupport();gesture.Reset();return;}
  if(!ControllerTransport.HeadTracked)triggerNeedsRelease=true;
  if(!VrControls.Main.Trigger)triggerNeedsRelease=false;
  bool left=ObjectGrabs.LeftHand;
  if(primaryLeft!=left){Stop();ReleaseSupport();gesture.Reset();primaryLeft=left;otherArmed=false;}
  var hand=primaryLeft?ControllerTransport.Left:ControllerTransport.Right;var other=primaryLeft?ControllerTransport.Right:ControllerTransport.Left;
  if(!Allowed(owner)||!hand.Tracked||!other.Tracked||!ControllerTransport.HeadTracked){Stop();ReleaseSupport();gesture.Reset();wasOther=other.Grip;otherArmed=false;return;}
  if(!other.Grip){otherArmed=true;ReleaseSupport();}
  if(!discovered){discovered=true;var registry=Shoveable.siteRegistry;if(registry!=null)foreach(var s in registry)Register(s);}
  if(other.Grip&&!wasOther&&otherArmed&&!support&&MotionHeldItems.TryAnchor(owner,out var p,out var q,out var relative)&&StereoTrial.TryTrackingPose(other.Position,other.Rotation,out var op,out var oq))
  {
   var root=q*relative;var local=Quaternion.Inverse(root)*(op-p);var scale=item.transform.lossyScale;
   local=new Vector3(local.x/scale.x,local.y/scale.y,local.z/scale.z);
   var point=new Vector3(0,Mathf.Clamp(local.y,ShaftLow,ShaftHigh),ShaftZ);
   if((p+root*Vector3.Scale(point,scale)-op).sqrMagnitude<=GrabRadius*GrabRadius&&StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out _,out var hq))
   {support=true;supportPoint=point;supportFromTool=Quaternion.Inverse(root)*oq;primaryFromTool=Quaternion.Inverse(root)*hq;otherArmed=false;DistanceGrips.Reset();Revision++;MotionHaptics.Pulse(!primaryLeft);}
  }
  wasOther=other.Grip;
 }
 private static void ReleaseSupport(){if(!support)return;support=false;Revision++;}
 internal static void Rotate(GameObject target,Vector3 grip,ref Quaternion root)
 {
  if(!Supporting||target!=item)return;
  var h=primaryLeft?ControllerTransport.Right:ControllerTransport.Left;
  if(!h.Grip||!h.Tracked||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var p,out _))return;
  var axis=root*Vector3.Scale(supportPoint-Handle,item.transform.lossyScale);var desired=p-grip;
  if(desired.sqrMagnitude<.15f*.15f)return;
  root=Quaternion.FromToRotation(axis,desired)*root;
 }
 internal static bool HandPose(bool left,out Vector3 p,out Quaternion q)
 {
  p=default;q=Quaternion.identity;
  if(!Supporting||!Probe.Focused||!MotionHeldItems.TryAnchor(owner,out var origin,out var basis,out var relative))return false;
  var root=basis*relative;
  p=origin+root*Vector3.Scale(left==primaryLeft?Handle:supportPoint,item.transform.lossyScale);
  q=root*(left==primaryLeft?primaryFromTool:supportFromTool);return true;
 }
 private static bool KeepTrackedPose(Shovel __instance)=>!(MotionInput.Enabled&&StereoTrial.GameplayActive&&owner!=null&&owner._IsOwner_k__BackingField&&shovel!=null&&shovel.Pointer==__instance.Pointer);
 private static bool Dig(InteractionManager __instance)
 {
  if(!MotionInput.Enabled||!__instance._IsOwner_k__BackingField||!StereoTrial.GameplayActive)return true;
  Input();
  if(shovel==null||owner!=__instance)return true;
  if(digFrame==Time.frameCount)return false;digFrame=Time.frameCount;
  var h=primaryLeft?ControllerTransport.Left:ControllerTransport.Right;
  if(!Allowed(owner)||!h.Tracked||!ControllerTransport.HeadTracked||!MotionHeldItems.TryAnchor(owner,out var p,out var q,out var relative))
  {Stop();gesture.Reset();gestureSite=null;return false;}
  // Use the game's clock for native digging credit/cadence; tracking intent
  // separately uses a monotonic real clock to reject tracking discontinuities.
  float now=Time.time;var root=q*relative;var scale=item.transform.lossyScale;
  var tip=p+root*Vector3.Scale(Blade,scale);
  StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var gripRotation);
  var worldFromRoom=gripRotation*Quaternion.Inverse(h.Rotation);
  var roomTip=h.Position+(Quaternion.Inverse(worldFromRoom)*root)*Vector3.Scale(Blade-Handle,scale);
#if IKAG_DIAGNOSTICS
  debugTip=tip;debugRoom=roomTip;
#endif
  Site touched=null;float height=0;Vector3 normal=Vector3.up;float best=float.PositiveInfinity;
  foreach(var s in sites.Values)
  {
   if(s.Target==null||s.Box==null||!s.Box.enabled||!s.Target.gameObject.activeInHierarchy||(tip-s.Centre).sqrMagnitude>(s.Radius+shovel.strokeDepth*2)*(s.Radius+shovel.strokeDepth*2))continue;
   // The patch sinks while digging. Read its existing transform once near it;
   // derive the plane and footprint from the cached BoxCollider, with no query.
   var matrix=s.Transform.localToWorldMatrix;var inverse=matrix.inverse;var local=inverse.MultiplyPoint3x4(tip);
   var c=s.LocalCentre;var size=s.Size;
   if(Mathf.Abs(local[s.A]-c[s.A])>size[s.A]*.5f||Mathf.Abs(local[s.B]-c[s.B])>size[s.B]*.5f)continue;
   var n=inverse.transpose.MultiplyVector(s.LocalNormal).normalized;
   var surface=matrix.MultiplyPoint3x4(c+Vector3.Scale(s.LocalNormal,size)*.5f);float d=Vector3.Dot(tip-surface,n);
   if(d< -shovel.strokeDepth||d>shovel.strokeDepth*2||Mathf.Abs(d)>=best||!s.Target.CanBeDug())continue;
   touched=s;height=d;normal=n;best=Mathf.Abs(d);
  }
  if(touched!=gestureSite){gesture.Reset();gestureSite=touched;soilContact=false;}
#if IKAG_DIAGNOSTICS
  debugHeight=touched==null?float.NaN:height;
#endif
  bool accepted=false;
  if(touched!=null&&h.Grip&&!VrControls.Main.Trigger&&Vector3.Dot(root*Vector3.down,normal)<-Mathf.Cos((shovel.bladeLean+shovel.strokeTilt)*Mathf.Deg2Rad))
  {
   var n=Quaternion.Inverse(worldFromRoom)*normal;
   if(gesture.Sample(new N(roomTip.x,roomTip.y,roomTip.z),height,new N(n.x,n.y,n.z),shovel.strokeDepth*.5f,Time.realtimeSinceStartupAsDouble)&&now>=nextStroke)
   {
    accepted=true;until=nextStroke=now+1/Mathf.Max(.1f,shovel.strokesPerSecond);Start(touched);triggerOwned=false;MotionHaptics.Pulse(primaryLeft,.35f,.045f);if(support)MotionHaptics.Pulse(!primaryLeft,.18f,.035f);
#if IKAG_DIAGNOSTICS
    Plugin.Line("SHOVEL_STROKE site="+touched.Target.siteId+" left="+primaryLeft+" progress="+touched.Target.Progress);
#endif
   }
  }
  else gesture.Reset();
  bool contact=touched!=null&&height<=0&&h.Grip;
  if(contact&&!soilContact&&!accepted)MotionHaptics.Pulse(primaryLeft,.14f,.025f);
  soilContact=contact;
  if(active!=null&&active.Target!=null&&active.Target.IsFullyDug&&!wasComplete){wasComplete=true;MotionHaptics.Pulse(primaryLeft,.65f,.09f);if(support)MotionHaptics.Pulse(!primaryLeft,.35f,.06f);}
  // Comfort digging reuses the already-completed native interaction ray.
  Site trigger=null;
  bool comfort=MotionInput.PrimaryHeld&&!triggerNeedsRelease;
  if(comfort&&owner.hasHit&&owner.rayHit.collider!=null)colliders.TryGetValue(owner.rayHit.collider.Pointer,out trigger);
  if(trigger!=null&&trigger.Target!=null&&trigger.Target.CanBeDug()){until=now+.1f;Start(trigger);triggerOwned=true;}
  else if(triggerOwned){Stop();triggerOwned=false;}
  if(!h.Grip&&!comfort)Stop();
  if(active!=null)
  {
   if(now>=until||active.Target==null||!active.Target.CanBeDug())Stop();
  }
  return false;
 }
 private static void Start(Site site)
 {
  if(active==site)return;Stop();
  if(owner.currentDiggingPatch!=null)owner.ExitDiggingMode(shovel);
  active=site;wasComplete=site.Target.IsFullyDug;owner.currentDiggingPatch=site.Target;site.Target.SetLocalDigging(true);
 }
 private static void Stop()
 {
  if(active==null)return;
  if(active.Target!=null&&active.Target.IsSpawned)active.Target.SetLocalDigging(false);
  if(owner!=null&&owner.currentDiggingPatch==active.Target)owner.currentDiggingPatch=null;
  active=null;
 }
#if IKAG_DIAGNOSTICS
 internal static void Refresh(Shoveable target){if(sites.TryGetValue(target.Pointer,out var s)){s.Centre=s.Box.bounds.center;s.Radius=s.Box.bounds.extents.magnitude+target.digDepth;}else Register(target);}
 internal static Vector3 SupportPoint=>supportPoint;
 internal static Shoveable ActiveSite=>active?.Target;
#endif
}
