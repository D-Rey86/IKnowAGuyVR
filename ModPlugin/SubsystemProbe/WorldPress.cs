using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using N=System.Numerics.Vector3;
namespace IKnowAGuyVR.SubsystemProbe;
// Input-time fingertip interaction over cached native button surfaces. No rig or
// camera writes here; native actions retain all gameplay/RPC authority.
internal static class WorldPress
{
 internal sealed class Face
 {
  internal Transform Transform,Parent;internal Bounds Bounds;internal Vector3 RestPosition,Scale;internal Quaternion Rotation;internal bool LiveRotation;
  internal int Axis;internal float Sign;internal Matrix4x4 Matrix,Inverse,LatchedInverse;
  internal void Refresh(){Matrix=(Parent==null?Matrix4x4.identity:Parent.localToWorldMatrix)*Matrix4x4.TRS(RestPosition,LiveRotation?Transform.localRotation:Rotation,Scale);Inverse=Matrix.inverse;}
  internal Vector3 Normal{get{var axis=Vector3.zero;axis[Axis]=Sign;return Inverse.transpose.MultiplyVector(axis).normalized;}}
  internal Vector3 Margin(float metres,Matrix4x4 inverse)=>new(new Vector3(inverse.m00,inverse.m01,inverse.m02).magnitude*metres,new Vector3(inverse.m10,inverse.m11,inverse.m12).magnitude*metres,new Vector3(inverse.m20,inverse.m21,inverse.m22).magnitude*metres);
 }
 internal sealed class Target
 {
  internal MonoBehaviour Component;internal GameObject Object;internal Transform Transform;internal IntPtr Pointer;internal IInteractable Action;internal Face[] Faces;internal bool Latched;internal float Release;
 }
 private sealed class Hand
 {
  internal readonly ToolMotion Motion=new();internal bool Valid,Continuous,Reserved;internal Vector3 Tip,Root,Previous,Velocity;internal Quaternion Basis;
  internal void Reset(){Motion.Reset();Valid=Continuous=Reserved=false;}
 }
 private static readonly Hand left=new(),right=new();private static readonly List<Target> targets=new();private static readonly Dictionary<IntPtr,Target> byPointer=new();
 private static InteractionManager owner;private static bool discovered,hadContact;private static int frame=-1;
 private static readonly Type[] types={typeof(ShopShutterController),typeof(BackroomDoorController),typeof(CourierOrderButton),typeof(ProjectOne.Gameplay.LightSwitch),typeof(RegisterBell),typeof(BeverageButton),typeof(IncineratorButton),typeof(WashingMachineButton)};
 internal static void Install()
 {
  var h=new Harmony("iknowaguyvr.world-press");var hook=new HarmonyMethod(typeof(WorldPress).GetMethod(nameof(Spawned),BindingFlags.NonPublic|BindingFlags.Static));
  foreach(var type in types){var m=type.GetMethod("Start",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)??type.GetMethod("Awake",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)??type.GetMethod("OnNetworkSpawn",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);if(m!=null)h.Patch(m,postfix:hook);}
 }
 private static void Spawned(MonoBehaviour __instance)=>Register(__instance);
 internal static void Reset(){owner=null;discovered=hadContact=false;frame=-1;targets.Clear();byPointer.Clear();left.Reset();right.Reset();}
 private static N Vec(Vector3 p)=>new(p.x,p.y,p.z);
 private static void FaceFor(List<Face> list,MeshFilter mesh,Vector3 normalParent,Vector3? rest=null,bool rotate=false,bool meshNormal=false)
 {
  if(mesh==null||mesh.sharedMesh==null)return;var t=mesh.transform;var normal=meshNormal?normalParent:Quaternion.Inverse(t.localRotation)*normalParent;
  int axis=Mathf.Abs(normal.x)>Mathf.Abs(normal.y)?0:1;if(Mathf.Abs(normal.z)>Mathf.Abs(normal[axis]))axis=2;if(Mathf.Abs(normal[axis])<.0001f)return;
  list.Add(new Face{Transform=t,Parent=t.parent,Bounds=mesh.sharedMesh.bounds,RestPosition=rest??t.localPosition,Rotation=t.localRotation,Scale=t.localScale,LiveRotation=rotate,Axis=axis,Sign=Mathf.Sign(normal[axis])});
 }
 private static void Register(MonoBehaviour c)
 {
  if(c==null||!c.gameObject.scene.IsValid()||!c.gameObject.activeInHierarchy||byPointer.ContainsKey(c.Pointer))return;
  var faces=new List<Face>();float depth=0;
  var shutter=c.TryCast<ShopShutterController>();if(shutter!=null){depth=Mathf.Abs(shutter.buttonPressDistance);if(shutter.buttonRenderer!=null)FaceFor(faces,shutter.buttonRenderer.GetComponent<MeshFilter>(),-Vector3.up*shutter.buttonPressDistance,shutter.buttonOriginalLocalPosition);}
  var back=c.TryCast<BackroomDoorController>();if(back!=null){foreach(var m in c.GetComponentsInChildren<MeshFilter>(true)){var n=m.name;if(n=="#"||n=="*"||(n.Length==1&&n[0]>='0'&&n[0]<='9'))FaceFor(faces,m,Vector3.forward,meshNormal:true);}}
  var courier=c.TryCast<CourierOrderButton>();if(courier!=null){depth=Mathf.Abs(courier.pressDepth);if(courier.buttonRenderer!=null)FaceFor(faces,courier.buttonRenderer.GetComponent<MeshFilter>(),Vector3.forward*courier.pressDepth,courier.originalLocalPosition);}
  var light=c.TryCast<ProjectOne.Gameplay.LightSwitch>();if(light!=null&&light.switchTransform!=null)FaceFor(faces,light.switchTransform.GetComponent<MeshFilter>(),Vector3.forward,rotate:true,meshNormal:true);
  var bell=c.TryCast<RegisterBell>();if(bell!=null&&bell.bellButton!=null){depth=Mathf.Abs(bell.pressDepth);FaceFor(faces,bell.bellButton.GetComponent<MeshFilter>(),Vector3.up,bell.originalButtonPosition);}
  var drink=c.TryCast<BeverageButton>();if(drink!=null)FaceFor(faces,c.GetComponent<MeshFilter>(),Vector3.back,meshNormal:true);
  var burner=c.TryCast<IncineratorButton>();if(burner!=null&&burner.buttonRenderer!=null){depth=Vector3.Distance(burner.onPosition,burner.offPosition);FaceFor(faces,burner.buttonRenderer.GetComponent<MeshFilter>(),burner.offPosition-burner.onPosition,burner.offPosition);}
  var washer=c.TryCast<WashingMachineButton>();if(washer!=null&&washer.buttonRenderer!=null)FaceFor(faces,washer.buttonRenderer.GetComponent<MeshFilter>(),Vector3.forward,rotate:true,meshNormal:true);
  if(faces.Count==0)return;var t=new Target{Component=c,Object=c.gameObject,Transform=c.transform,Pointer=c.Pointer,Action=c.Cast<IInteractable>(),Faces=faces.ToArray(),Release=Mathf.Clamp(depth*.5f,.006f,.02f)};targets.Add(t);byPointer.Add(c.Pointer,t);
 }
 private static void Sample(Hand h,bool useLeft,bool live)
 {
  var input=useLeft?ControllerTransport.Left:ControllerTransport.Right;h.Reserved=false;
  if(!live||!input.Tracked||!input.Grip||input.TriggerValue>.1f||(owner.IsHoldingItem()&&ObjectGrabs.LeftHand==useLeft)||PhysicalHeldItems.Attached(useLeft)||MotionShovel.OwnsGrip(useLeft)||KitchenHands.Pose(useLeft,out _,out _)||!MotionBody.TryIndexGeometry(owner,useLeft,out var tip,out var root)||!StereoTrial.TryTrackingPose(input.Position,input.Rotation,out var grip,out var q))
  {h.Reset();return;}
  h.Basis=q*Quaternion.Inverse(input.Rotation);var room=input.Position+input.Rotation*tip;h.Tip=grip+q*tip;h.Root=grip+q*root;h.Continuous=h.Motion.Sample(Vec(room),Time.realtimeSinceStartupAsDouble);h.Previous=h.Tip+h.Basis*(new Vector3(h.Motion.Previous.X,h.Motion.Previous.Y,h.Motion.Previous.Z)-room);var v=h.Motion.Velocity;h.Velocity=h.Basis*new Vector3(v.X,v.Y,v.Z);h.Valid=true;
 }
 private static bool Near(Face f,Hand h,float distance,Matrix4x4 inverse,bool keepBehind=false)=>h.Valid&&PressSurface.Near(Vec(inverse.MultiplyPoint3x4(h.Tip)),Vec(f.Bounds.min),Vec(f.Bounds.max),f.Axis,f.Sign,Vec(f.Margin(distance,inverse)),keepBehind);
 private static void Latch(Target t){t.Latched=true;hadContact=true;foreach(var f in t.Faces)f.LatchedInverse=f.Inverse;}
 private static bool TryPress(Target t,Face f,Hand hand,bool useLeft)
 {
  if(!hand.Valid)return false;var now=f.Inverse.MultiplyPoint3x4(hand.Tip);var old=f.Inverse.MultiplyPoint3x4(hand.Previous);
  if(!PressSurface.Cross(Vec(old),Vec(now),Vec(f.Bounds.min),Vec(f.Bounds.max),f.Axis,f.Sign,out var hit))return false;
  Latch(t);if(!hand.Continuous||hand.Motion.Travel<.001f||Vector3.Dot(hand.Velocity,-f.Normal)<.01f){
#if IKAG_DIAGNOSTICS
   Plugin.Line("WORLD_PRESS_REJECT reason=room-intent target="+t.Component.name);
#endif
   return true;
  }
  if(!t.Action.CanInteract()){
#if IKAG_DIAGNOSTICS
   Plugin.Line("WORLD_PRESS_REJECT reason=native-gate target="+t.Component.name);
#endif
   return true;
  }
  var point=f.Matrix.MultiplyPoint3x4(new Vector3(hit.X,hit.Y,hit.Z));var ray=point-hand.Root;float length=ray.magnitude;if(length<.001f)return true;
  // The existing controller aim ray does not cover this fingertip segment.
  // Query only on a new contact, using an unpatched overload and native mask.
  if(Physics.Raycast(new Ray(hand.Root,ray/length),out var obstacle,length+.001f,owner.interactionLayers.value&~0x404,QueryTriggerInteraction.Ignore)&&obstacle.collider!=null&&!obstacle.collider.transform.IsChildOf(t.Component.transform)){
#if IKAG_DIAGNOSTICS
   Plugin.Line("WORLD_PRESS_REJECT reason=occlusion target="+t.Component.name+" blocker="+obstacle.collider.name);
#endif
   return true;
  }
  t.Action.OnInteract();MotionHaptics.Pulse(useLeft,.16f,.025f);
#if IKAG_DIAGNOSTICS
  WorldPressAudit.Activated(t.Component,useLeft);Plugin.Line("WORLD_PRESS_ACTIVATED type="+t.Component.GetIl2CppType().Name+" name="+t.Component.name+" left="+useLeft);
#endif
  return true;
 }
 internal static bool OwnsGrip(bool useLeft){Tick();return (useLeft?left:right).Reserved;}
 internal static void Tick()
 {
  var current=PhysicalHeldItems.Owner;if(frame==Time.frameCount&&current==owner)return;frame=Time.frameCount;
  if(!MotionInput.Enabled||!StereoTrial.GameplayActive){if(discovered)Reset();return;}
  if(current==null){left.Reset();right.Reset();return;}
  if(owner!=current){owner=current;discovered=false;left.Reset();right.Reset();targets.Clear();byPointer.Clear();}
  if(!discovered){discovered=true;foreach(var c in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true)){var k=c.GetIl2CppType().Name;if(k=="ShopShutterController"||k=="BackroomDoorController"||k=="CourierOrderButton"||k=="LightSwitch"||k=="RegisterBell"||k=="BeverageButton"||k=="IncineratorButton"||k=="WashingMachineButton")Register(c);}}
  if(!ControllerTransport.Left.Grip&&!ControllerTransport.Right.Grip){left.Reset();right.Reset();if(hadContact){foreach(var t in targets)t.Latched=false;hadContact=false;}return;}
  // Read already-latched UI ownership; calling InputOwned here would tick the
  // HUD recursively through its grip query before proximity has been resolved.
  bool live=Probe.Focused&&ControllerTransport.HeadTracked&&MotionInput.ControlsArmed&&owner.IsInteractionEnabled&&!owner.skatingSuppressed&&!InteractionManager.IsHeldItemUseBlocked&&!MotionInput.ConstructionPlacementActive&&!MotionInput.PhotoMode&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive&&!ArcadeMotion.Active&&!SpyMotion.Active&&!RepairMotion.Active&&!ImmersiveHud.PointerLatched&&!ContextHud.ModalActive&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&Cursor.lockState==CursorLockMode.Locked;
  Sample(left,true,live);Sample(right,false,live);
  if(!left.Valid&&!right.Valid){if(hadContact){foreach(var t in targets)t.Latched=false;hadContact=false;}return;}
  for(int i=targets.Count-1;i>=0;i--)
  {
   var t=targets[i];if(t.Component==null){byPointer.Remove(t.Pointer);targets.RemoveAt(i);continue;}if(!t.Object.activeInHierarchy){t.Latched=false;continue;}
   // Native transform work is restricted to controls within fingertip reach.
   var center=t.Transform.position;if((!left.Valid||(left.Tip-center).sqrMagnitude>1)&&(!right.Valid||(right.Tip-center).sqrMagnitude>1)){t.Latched=false;continue;}
   foreach(var f in t.Faces)f.Refresh();
   bool keep=false;if(t.Latched){foreach(var f in t.Faces)if(Near(f,left,t.Release,f.LatchedInverse,true)||Near(f,right,t.Release,f.LatchedInverse,true)){keep=true;break;}if(!keep)t.Latched=false;}
   foreach(var f in t.Faces)
   {
    if(Near(f,left,.12f,f.Inverse))left.Reserved=true;if(Near(f,right,.12f,f.Inverse))right.Reserved=true;
    if(t.Latched)continue;
    if(TryPress(t,f,left,true)||TryPress(t,f,right,false))continue;
    // Starting/reappearing inside a button cannot count as a new press.
    if((left.Valid&&!left.Continuous&&Near(f,left,t.Release,f.Inverse))||(right.Valid&&!right.Continuous&&Near(f,right,t.Release,f.Inverse)))Latch(t);
   }
   if(owner==null||!StereoTrial.GameplayActive)return;
  }
 }
#if IKAG_DIAGNOSTICS
 internal static IReadOnlyList<Target> Targets=>targets;
 internal static bool Tip(bool useLeft,out Vector3 p){var h=useLeft?left:right;p=h.Tip;return h.Valid;}
#endif
}
