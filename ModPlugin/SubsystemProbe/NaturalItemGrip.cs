using System.Collections.Generic;
using System;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
namespace IKnowAGuyVR.SubsystemProbe;
// Model-space contact anchors. Poses are resolved on item changes; draw/update
// paths only compose the cached anchor with the shared controller pose.
internal static class NaturalItemGrip
{
 private sealed class Profile{internal Vector3 Contact;internal bool Support,Pinch,MirrorX,Unsupported;internal Quaternion AuthoredRotation;internal GameObject Source;}
 private static readonly Dictionary<IntPtr,Profile> profiles=new();
 private static GameObject item;private static Profile current;private static bool ready;
 internal static int Revision{get;private set;}
 internal static bool FlatHand=>ready&&current!=null&&current.Support;
 internal static bool PinchingHand=>ready&&current!=null&&current.Pinch;
 internal static bool SemanticReady=>ready&&current!=null&&!current.Unsupported;
 internal static Vector3 ContactPoint=>current==null?Vector3.zero:current.Contact;
 internal static bool Supports(GameObject target)=>ready&&item==target&&current!=null&&current.Support;
#if IKAG_DIAGNOSTICS
 internal static Vector3 RenderedContact(bool left){var contact=current.Contact;if(left&&current.MirrorX)contact.x=-contact.x;return item.transform.TransformPoint(contact);}
#endif
 internal static void AvatarChanged(){ready=false;item=null;profiles.Clear();Revision++;}
 internal static void Install(){new Harmony("iknowaguyvr.natural-item-grips").Patch(typeof(Pizza).GetMethod("OnNetworkDoughStateChanged"),postfix:new HarmonyMethod(typeof(NaturalItemGrip).GetMethod(nameof(DoughChanged),BindingFlags.Static|BindingFlags.NonPublic)));}
 private static void DoughChanged(Pizza __instance){if(__instance==null)return;var obj=__instance.gameObject;profiles.Remove(obj.Pointer);if(item==obj){item=null;ready=false;Revision++;}}
 private static Profile Measure(GameObject obj,float palmWidth)
 {
  var box=obj.GetComponent<BoxCollider>();var sphere=obj.GetComponent<SphereCollider>();var capsule=obj.GetComponent<CapsuleCollider>();var mesh=obj.GetComponent<MeshCollider>();
  Bounds b;
  if(box!=null)b=new Bounds(box.center,box.size);
  else if(sphere!=null)b=new Bounds(sphere.center,Vector3.one*sphere.radius*2);
  else if(capsule!=null){var size=Vector3.one*capsule.radius*2;size[capsule.direction]=capsule.height;b=new Bounds(capsule.center,size);}
  else if(mesh!=null&&mesh.sharedMesh!=null)b=mesh.sharedMesh.bounds;
  else return new Profile{Unsupported=true};
  // Tool contacts are measured from the shipping meshes rather than their
  // blade/head or model origins.
  // Centre of the D-handle's upper crossbar, measured from its opposing
  // centreline mesh vertices. Keep the existing authored tool orientation.
  if(obj.GetComponent<Shovel>()!=null)return new Profile{Contact=MotionShovel.Handle};
  if(obj.GetComponent<Hammer>()!=null)return new Profile{Contact=new Vector3(0,.149955f,0)};
  if(obj.GetComponent<MeleeWeapon>()!=null)return new Profile{Contact=new Vector3(0,.149955f,0)};
  if(obj.GetComponent<CheeseGun>()!=null)return new Profile{Contact=new Vector3(0,.0375f,.03f)};
  if(obj.GetComponent<MusicalInstrument>()!=null&&mesh!=null&&mesh.sharedMesh!=null)
  {return new Profile{Contact=mesh.sharedMesh.name.Contains("Guitar")?new Vector3(0,.0872f,-.725f):new Vector3(0,.11f,0)};}
  if(obj.GetComponent<Photograph>()!=null||obj.GetComponent<ForgeryDocument>()!=null||obj.GetComponent<PaintingItem>()!=null)
  {var edge=b.center;int axis=b.size.x>b.size.z?0:2;if(b.size.y>b.size[axis])axis=1;edge[axis]=b.min[axis];return new Profile{Contact=edge,Pinch=true};}
  if(obj.GetComponent<PhotoCamera>()!=null)
  {var point=b.center;point.x=b.min.x+palmWidth*.5f;return new Profile{Contact=point,MirrorX=true};}
  if(obj.GetComponent<Skateboard>()!=null)
  {var point=b.center;point.x=b.min.x;return new Profile{Contact=point,MirrorX=true};}
  bool support=obj.GetComponent<Pizza>()!=null||obj.GetComponent<PizzaBox>()!=null||obj.GetComponent<CargoBox>()!=null||obj.GetComponent<ChestItem>()!=null;
  // An object wider than the measured palm in every cross-section cannot
  // fit inside a closed hand. Support it from its authored bottom instead.
  var s=Vector3.Scale(b.size,obj.transform.lossyScale);float[] dimensions={Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z)};Array.Sort(dimensions);
  support|=dimensions[1]>palmWidth;
  if(obj.GetComponent<PhotoCamera>()!=null||obj.GetComponent<FlashlightHoldable>()!=null||obj.GetComponent<CheeseGun>()!=null||obj.GetComponent<MeleeWeapon>()!=null||obj.GetComponent<DartHoldable>()!=null||obj.GetComponent<MusicalInstrument>()!=null||obj.GetComponent<Skateboard>()!=null)support=false;
  var contact=b.center;if(support)contact.y=b.min.y;
  var pizza=obj.GetComponent<Pizza>();
  if(support&&pizza!=null&&pizza.skinnedMeshRenderer!=null)
  {
   var baked=new Mesh();try{pizza.skinnedMeshRenderer.BakeMesh(baked);var toRoot=obj.transform.worldToLocalMatrix*pizza.skinnedMeshRenderer.transform.localToWorldMatrix;float bottom=float.PositiveInfinity;foreach(var v in baked.vertices)bottom=Mathf.Min(bottom,toRoot.MultiplyPoint3x4(v).y);if(float.IsFinite(bottom))contact.y=bottom;}finally{UnityEngine.Object.Destroy(baked);}
  }
  if(support&&mesh!=null&&mesh.convex)
  {
   bool enabled=mesh.enabled;try{mesh.enabled=true;var t=mesh.transform;var outside=t.TransformPoint(contact-Vector3.up*b.size.y);var point=Physics.ClosestPoint(outside,mesh,t.position,t.rotation);if((point-outside).sqrMagnitude>0)contact=t.InverseTransformPoint(point);}finally{mesh.enabled=enabled;}
  }
  return new Profile{Contact=contact,Support=support,Pinch=!support&&(obj.GetComponent<Ingredient>()!=null||dimensions[2]<=palmWidth)};
 }
 internal static bool TryPose(InteractionManager owner,out Vector3 p,out Quaternion q,out Quaternion relative)
 {
  p=default;q=relative=Quaternion.identity;
  var next=owner.cachedTopItem;bool left=ObjectGrabs.LeftHand;
  if(next==null||!owner.IsHoldingItem())return false;
  if(!MotionBody.TryCarryFrame(left,out var surface,out var forward,out var normal,out var pinch,out var width))return false;
  if(item!=next){item=next;if(!profiles.TryGetValue(next.Pointer,out current)||(current!=null&&(current.Source==null||current.Source!=next))){current=Measure(next,width);if(current!=null){current.Source=next;current.AuthoredRotation=Quaternion.Euler(owner.GetHoldingConfiguration(next).rotation);}profiles[next.Pointer]=current;}ready=false;Revision++;}
  if(current==null||current.Unsupported)return false;
  var h=left?ControllerTransport.Left:ControllerTransport.Right;
  if(!h.Tracked||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var grip,out q))return false;
  if(!ready){ready=true;Revision++;}
  if(current.Support)relative=Quaternion.LookRotation(forward,normal);
  else
  {
   if(!StereoTrial.TryTrackingPose(h.AimPosition,h.AimRotation,out _,out var aim))return false;
   relative=Quaternion.Inverse(q)*aim*current.AuthoredRotation;
  }
  if(MotionShovel.Supporting){var rotation=q*relative;MotionShovel.Rotate(next,grip,ref rotation);relative=Quaternion.Inverse(q)*rotation;}
  if(MotionCarry.Supporting){var rotation=q*relative;MotionCarry.Rotate(next,grip+(current.Support?q*surface:Vector3.zero),ref rotation);relative=Quaternion.Inverse(q)*rotation;}
  var contact=current.Contact;if(left&&current.MirrorX)contact.x=-contact.x;
  p=grip+(current.Support?q*surface:current.Pinch?q*pinch:Vector3.zero)-(q*relative)*Vector3.Scale(contact,next.transform.lossyScale);
  return true;
 }
}
