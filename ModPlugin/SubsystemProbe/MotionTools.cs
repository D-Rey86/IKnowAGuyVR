using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using N=System.Numerics.Vector3;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionTools
{
 private static readonly ToolMotion motion=new();private static readonly ToolSwing swing=new();
 private static readonly BatMotion batMotion=new();
 private static readonly Il2CppStructArray<RaycastHit> hammerHits=new(16);
 private static readonly Il2CppReferenceArray<Collider> barrelOverlaps=new(16);
 private static bool permit,dispatch,contact;private static int inputFrame=-1;
 private static bool left;private static Vector3 actionPosition;private static Quaternion actionRotation;
#if IKAG_DIAGNOSTICS
 internal static string DebugState=>"ready="+PhysicalHeldItems.Ready+" active="+MotionInput.GameplayReady+" selected="+PhysicalHeldItems.Selected+" cursor="+Cursor.lockState+" focus="+Probe.Focused+" block="+InteractionManager.IsHeldItemUseBlocked+" placement="+MotionInput.PlacementPreviewActive+" grip="+PhysicalHeldItems.Hand.Grip+" velocity="+motion.InstantVelocity+" travel="+motion.Travel+" started="+swing.Started+" permit="+permit;
#endif
 // Distal contact spheres measured from the shipping head/barrel meshes.
 internal static readonly Vector3 HammerHead=new(0,.333035f,0),BatHead=new(0,.825866f,0);
 private static HarmonyMethod Hook(string n)=>new(typeof(MotionTools).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static));
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.physical-tools");
  h.Patch(typeof(InteractionManager).GetMethod("HandleHammerSwing"),prefix:Hook(nameof(ComfortHammer)));
  h.Patch(typeof(MeleeWeapon).GetMethod("OnInstantUse"),prefix:Hook(nameof(ComfortMelee)));
  h.Patch(typeof(MeleeWeapon).GetMethod("PlayHitSound"),prefix:Hook(nameof(Hit)));
  h.Patch(typeof(MeleeWeapon).GetMethod("HitSoundRpc"),prefix:Hook(nameof(Hit)));
 }
 internal static void Reset(){motion.Reset();swing.Reset();batMotion.Reset();permit=dispatch=contact=false;inputFrame=-1;}
 internal static bool TryActionPose(out Vector3 p,out Quaternion q){p=actionPosition;q=actionRotation;return dispatch;}
 internal static void Input()
 {
  if(inputFrame==Time.frameCount)return;inputFrame=Time.frameCount;
  if((PhysicalHeldItems.Hammer==null&&PhysicalHeldItems.Melee==null&&PhysicalHeldItems.Dart==null)||!PhysicalHeldItems.Ready)
  {motion.Reset();swing.Reset();batMotion.Reset();permit=false;return;}
  var hand=PhysicalHeldItems.Hand;bool useLeft=PhysicalHeldItems.Left;
  if(left!=useLeft){motion.Reset();swing.Reset();batMotion.Reset();permit=false;left=useLeft;}
  var local=PhysicalHeldItems.Hammer!=null?HammerHead:PhysicalHeldItems.Melee!=null?BatHead:Vector3.zero;
  Vector3 localLow=default;float batRadius=0;
  if(PhysicalHeldItems.Melee!=null&&!PhysicalHeldItems.TryBatShape(out localLow,out local,out batRadius)){batMotion.Reset();return;}
  if(!PhysicalHeldItems.TryRoomPoint(local,out var point,out var room,out var basis,out var rootRotation)){motion.Reset();swing.Reset();batMotion.Reset();return;}
  double now=Time.realtimeSinceStartupAsDouble;
  bool isBat=PhysicalHeldItems.Melee!=null;
  Vector3 low=isBat?room+Quaternion.Inverse(basis)*rootRotation*Vector3.Scale(localLow-local,PhysicalHeldItems.Scale):default;
  bool valid=!isBat&&motion.Sample(ToN(room),now);
  if(PhysicalHeldItems.Dart!=null)return;
  if(!hand.Grip&&!isBat){swing.Reset();permit=false;return;}
  bool sweep=isBat?batMotion.Sample(ToN(low),ToN(room),ToN(hand.Position),ToN(ControllerTransport.HeadPosition),now):swing.Sample(motion,now,valid);
  var hammer=PhysicalHeldItems.Hammer;var melee=PhysicalHeldItems.Melee;
  if(!isBat&&swing.Started)
  {
   permit=Time.time-hammer.lastSwingTime>=hammer.swingCooldown;
   if(permit){hammer.lastSwingTime=Time.time;if(hammer.swingSound!=null)AudioSource.PlayClipAtPoint(hammer.swingSound,point);}
  }
  if(isBat&&(sweep||batMotion.Started)){permit=melee.CanInstantUse();if(permit&&batMotion.Started)melee.PlaySwingSound();}
  if(!sweep||!permit)return;
  var previous=ToV(isBat?batMotion.PreviousHigh:motion.Previous);
  var from=point+basis*(previous-room);var travel=point-from;float distance=travel.magnitude;
  if(hammer!=null)
  {
   if(distance<.001f)return;
   float radius=.06f*Mathf.Max(Mathf.Abs(PhysicalHeldItems.Scale.x),Mathf.Max(Mathf.Abs(PhysicalHeldItems.Scale.y),Mathf.Abs(PhysicalHeldItems.Scale.z)));
   int count=Physics.SphereCastNonAlloc(from,radius,travel/distance,hammerHits,distance,PhysicalHeldItems.Owner.interactionLayers.value&~0x404,QueryTriggerInteraction.Ignore);int index=-1;float nearest=float.PositiveInfinity;
   for(int i=0;i<count;i++){var hit=hammerHits[i];if(hit.collider==null||hit.collider.transform.IsChildOf(PhysicalHeldItems.Owner.transform)||hit.collider.transform.IsChildOf(PhysicalHeldItems.Item.transform))continue;if(hit.distance<nearest){index=i;nearest=hit.distance;}}
   if(index>=0){hammer.TryDamageObject(hammerHits[index],true);swing.Contact();MotionHaptics.Pulse(left,.45f,.05f);
#if IKAG_DIAGNOSTICS
    Plugin.Line("PHYSICAL_HAMMER_CONTACT target="+hammerHits[index].collider.name+" left="+left);
#endif
   }
  }
  else
  {
   // The complete authored bat collider, swept once; both previous endpoints use
   // today's tracking origin, so artificial locomotion cannot generate a hit.
   float radius=batRadius;
   var lo=point+basis*(low-room);var prevLo=point+basis*(ToV(batMotion.PreviousLow)-room);
   ToolCapsule.Sweep(ToN(prevLo),ToN(from),ToN(lo),ToN(point),radius,out var a,out var b,out var delta,out var r);
   var d=ToV(delta);float length=d.magnitude;Vector3 direction=distance>.001f?travel/distance:(length>.001f?d/length:Vector3.zero);
   if(direction==Vector3.zero)return;
   int mask=melee.hitMask.value&~0x404;Collider selected=null;Vector3 hitPoint=default,normal=default;float nearest=float.PositiveInfinity;
   if(length>.0001f){int count=Physics.CapsuleCastNonAlloc(ToV(a),ToV(b),r,d/length,hammerHits,length,mask,QueryTriggerInteraction.Ignore);
    for(int i=0;i<count;i++){var hit=hammerHits[i];if(!MeleeCollider(hit.collider)||hit.distance>=nearest)continue;selected=hit.collider;nearest=hit.distance;hitPoint=hit.point;normal=hit.normal;}}
   else{int count=Physics.OverlapCapsuleNonAlloc(ToV(a),ToV(b),r,barrelOverlaps,mask,QueryTriggerInteraction.Ignore);
    for(int i=0;i<count;i++){var c=barrelOverlaps[i];if(!MeleeCollider(c))continue;var p=c.ClosestPoint((lo+point)*.5f);float sq=(p-(lo+point)*.5f).sqrMagnitude;if(sq>=nearest)continue;nearest=sq;selected=c;hitPoint=p;normal=-direction;}}
   if(selected==null)return;
   if(nearest<=0){hitPoint=selected.ClosestPoint((lo+point)*.5f);normal=-direction;}
   if(normal.sqrMagnitude<1e-8f)normal=-direction;else normal.Normalize();
   var target=selected.GetComponentInParent<IMeleeTarget>();bool damaged=false;
   if(target!=null&&target.CanTakeMeleeHit()){target.OnMeleeHit(new MeleeHitInfo(hitPoint,direction,normal,melee.hitForce,melee.WeaponId));damaged=true;}
   else{var punch=selected.GetComponentInParent<IPunchable>();if(punch!=null&&punch.CanBePunched()){punch.OnPunched(direction,melee.hitForce);damaged=true;}}
   // Native recovery starts at solid contact, not during a backswing. The
   // nearest solid still blocks targets behind it; resting contact cannot repeat.
   SetCooldown(melee);if(damaged){melee.PlayHitSound(hitPoint);MotionHaptics.Pulse(left,.5f,.05f);
#if IKAG_DIAGNOSTICS
    Plugin.Line("PHYSICAL_MELEE_CONTACT left="+left+" target="+selected.name+" fullCollider=True");
#endif
   }
  }
 }
 private static N ToN(Vector3 p)=>new(p.x,p.y,p.z);
 private static Vector3 ToV(N p)=>new(p.X,p.Y,p.Z);
 private static bool MeleeCollider(Collider c)
 {
  if(c==null||c.transform.IsChildOf(PhysicalHeldItems.Item.transform)||c.transform.IsChildOf(PhysicalHeldItems.Owner.transform))return false;
  var network=c.GetComponentInParent<INetworkHoldable>();return network==null||!network.IsBeingHeld();
 }
 private static void SetCooldown(MeleeWeapon weapon)=>weapon.swingReadyTime=Time.time+weapon.windupSeconds+weapon.strikeSeconds+weapon.recoverSeconds+weapon.extraCooldown;
 private static void Resolve(MeleeWeapon weapon,Vector3 p,Vector3 direction,float range,float radius)
 {
  var camera=PhysicalHeldItems.Owner.interactionCamera;if(camera==null)return;var t=camera.transform;var savedP=t.localPosition;var savedQ=t.localRotation;float oldRange=weapon.hitRange,oldRadius=weapon.hitRadius;
  contact=false;dispatch=true;actionPosition=p;actionRotation=Quaternion.LookRotation(direction,Mathf.Abs(Vector3.Dot(direction,Vector3.up))>.99f?Vector3.forward:Vector3.up);
#if IKAG_DIAGNOSTICS
  PhysicalItemAudit.BeforeAction();
#endif
  try{weapon.hitRange=range;weapon.hitRadius=radius;t.SetPositionAndRotation(p,actionRotation);weapon.ResolveHit(camera);}
  finally{if(t!=null)t.SetLocalPositionAndRotation(savedP,savedQ);if(weapon!=null){weapon.hitRange=oldRange;weapon.hitRadius=oldRadius;}dispatch=false;
#if IKAG_DIAGNOSTICS
   PhysicalItemAudit.AfterAction();
#endif
  }
 }
 private static void Hit(MeleeWeapon __instance)
 {if(dispatch&&!contact&&__instance==PhysicalHeldItems.Melee){contact=true;MotionHaptics.Pulse(PhysicalHeldItems.Left,.5f,.05f);
#if IKAG_DIAGNOSTICS
  Plugin.Line("PHYSICAL_MELEE_CONTACT left="+PhysicalHeldItems.Left);
#endif
 }}
 private static bool ComfortHammer(InteractionManager __instance)
 {
  if(!MotionInput.Enabled||!StereoTrial.GameplayActive||!__instance._IsOwner_k__BackingField)return true;
  if(!__instance.primaryPressedThisFrame)return true;
  PhysicalHeldItems.EnsureSelection();var hammer=PhysicalHeldItems.Hammer;
  if(hammer==null||PhysicalHeldItems.Owner!=__instance)return true;
  if(PhysicalHeldItems.Ready&&__instance.primaryPressedThisFrame&&Time.time-hammer.lastSwingTime>=hammer.swingCooldown)
  {hammer.lastSwingTime=Time.time;hammer.TryDamageObject(__instance.rayHit,__instance.hasHit);if(hammer.swingSound!=null)AudioSource.PlayClipAtPoint(hammer.swingSound,PhysicalHeldItems.Item.transform.position);if(__instance.hasHit)MotionHaptics.Pulse(PhysicalHeldItems.Left,.3f,.04f);}
  return false;
 }
 private static bool ComfortMelee(MeleeWeapon __instance)
 {
  if(!MotionInput.Enabled||!StereoTrial.GameplayActive)return true;
  PhysicalHeldItems.EnsureSelection();if(__instance!=PhysicalHeldItems.Melee)return true;
  if(PhysicalHeldItems.Ready&&__instance.CanInstantUse()&&MotionHeldItems.TryUsePose(PhysicalHeldItems.Owner,out var p,out var q))
  {SetCooldown(__instance);__instance.PlaySwingSound();Resolve(__instance,p,q*Vector3.forward,__instance.hitRange,__instance.hitRadius);}
  return false;
 }
 internal static bool TryReleaseDart(InteractionManager owner,GameObject item)
 {
  PhysicalHeldItems.EnsureSelection();
  if(item!=PhysicalHeldItems.Item||PhysicalHeldItems.Dart==null||owner!=PhysicalHeldItems.Owner||!PhysicalHeldItems.Ready||!motion.TryThrow(Time.realtimeSinceStartupAsDouble,out var velocity)||!PhysicalHeldItems.TryRoot(out var p,out _))return false;
  var h=PhysicalHeldItems.Hand;if(!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var q))return false;
  var v=new Vector3(velocity.X,velocity.Y,velocity.Z);var direction=q*Quaternion.Inverse(h.Rotation)*v.normalized;
  actionPosition=p;actionRotation=Quaternion.LookRotation(direction,Mathf.Abs(Vector3.Dot(direction,Vector3.up))>.99f?Vector3.forward:Vector3.up);bool hit=owner.hasHit;
  MotionHeldItems.SnapForRelease(owner);dispatch=true;
#if IKAG_DIAGNOSTICS
  PhysicalItemAudit.BeforeAction();
#endif
  try{owner.hasHit=false;owner.primaryPressedThisFrame=true;owner.HandleDartThrow();}
  finally{if(owner!=null){owner.hasHit=hit;owner.primaryPressedThisFrame=false;}dispatch=false;
#if IKAG_DIAGNOSTICS
   PhysicalItemAudit.AfterAction();
#endif
  }
  bool launched=!owner.heldItems.Contains(item);if(launched){MotionHaptics.Pulse(PhysicalHeldItems.Left,.22f,.035f);
#if IKAG_DIAGNOSTICS
   Plugin.Line("PHYSICAL_DART_THROW left="+PhysicalHeldItems.Left+" direction="+direction.ToString("R")+" nativeSpeed="+PhysicalHeldItems.Dart.velocity.magnitude);
#endif
  }motion.Reset();return launched;
 }
}
