using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// One selection cache and controller snapshot shared by the physical item verbs.
internal static class PhysicalHeldItems
{
 internal static InteractionManager Owner{get;private set;}
 internal static GameObject Item{get;private set;}
 internal static Hammer Hammer{get;private set;}
 internal static MeleeWeapon Melee{get;private set;}
 internal static DartHoldable Dart{get;private set;}
 internal static MusicalInstrument Instrument{get;private set;}
 internal static ChestItem Chest{get;private set;}
 private static HoldableBase holdable;
 private static Collider carryCollider;private static int carryKind,carryAxis;private static float carryRadius,carryHeight;
 internal static bool Carryable{get;private set;}
 internal static Bounds Bounds{get;private set;}
 internal static Vector3 Scale{get;private set;}
 private static int frame=-1;
 internal static bool Left=>ObjectGrabs.LeftHand;
 internal static ControllerTransport.Hand Hand=>Left?ControllerTransport.Left:ControllerTransport.Right;
 internal static ControllerTransport.Hand Other=>Left?ControllerTransport.Right:ControllerTransport.Left;
 internal static bool Selected=>Item!=null&&Owner!=null&&Owner.cachedTopItem==Item&&Owner.IsHoldingItem()&&Owner.heldItems.Contains(Item);
 internal static bool Ready=>Selected&&holdable!=null&&holdable._IsOwner_k__BackingField&&!holdable.IsStored()&&MotionInput.Enabled&&MotionInput.GameplayReady&&StereoTrial.GameplayActive&&Probe.Focused&&ControllerTransport.HeadTracked&&Hand.Tracked&&Other.Tracked&&Owner._IsOwner_k__BackingField&&Owner.IsInteractionEnabled&&!Owner.skatingSuppressed&&!InteractionManager.IsHeldItemUseBlocked&&!MotionInput.ConstructionPlacementActive&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&!MotionInput.ConversationActive&&!MotionInput.WheelOpen&&!ImmersiveHud.InputOwned&&Cursor.lockState==CursorLockMode.Locked;
 internal static int Revision=>MotionCarry.Revision+MotionChest.Revision;
 internal static bool OwnsGrip(bool left)=>MotionCarry.OwnsGrip(left)||MotionChest.OwnsGrip(left)||MotionInstrument.OwnsGrip(left);
 internal static bool Attached(bool left)=>MotionCarry.Supporting||MotionChest.OwnsGrip(left);
 internal static bool HandPose(bool left,out Vector3 p,out Quaternion q)=>MotionChest.HandPose(left,out p,out q)||MotionCarry.HandPose(left,out p,out q);
 internal static void Reset()
 {MotionTools.Reset();MotionCarry.Reset();MotionChest.Reset();MotionInstrument.Reset();Owner=null;Item=null;holdable=null;Hammer=null;Melee=null;Dart=null;Instrument=null;Chest=null;Carryable=false;frame=-1;}
 internal static void EnsureSelection()
 {var current=InteractionManager.LocalInstance;var selected=current==null?null:current.cachedTopItem;if(current!=Owner||selected!=Item){frame=-1;Input();}}
 internal static void Input()
 {
  if(frame==Time.frameCount)return;frame=Time.frameCount;
  var owner=InteractionManager.LocalInstance;var item=owner==null?null:owner.cachedTopItem;
  if(owner!=Owner||item!=Item)
  {
   MotionTools.Reset();MotionCarry.Reset();MotionChest.Reset();MotionInstrument.Reset();Owner=owner;Item=item;
   holdable=item==null?null:item.GetComponent<HoldableBase>();
   Hammer=item==null?null:item.GetComponent<Hammer>();Melee=item==null?null:item.GetComponent<MeleeWeapon>();Dart=item==null?null:item.GetComponent<DartHoldable>();Instrument=item==null?null:item.GetComponent<MusicalInstrument>();Chest=item==null?null:item.GetComponent<ChestItem>();
   Carryable=item!=null&&holdable!=null;Scale=item==null?Vector3.one:item.transform.lossyScale;Bounds=default;carryCollider=null;carryKind=0;
   if(Carryable)
   {
    var box=item.GetComponent<BoxCollider>();var sphere=item.GetComponent<SphereCollider>();var capsule=item.GetComponent<CapsuleCollider>();var mesh=item.GetComponent<MeshCollider>();
    if(box!=null){carryCollider=box;Bounds=new Bounds(box.center,box.size);}
    else if(sphere!=null){carryCollider=sphere;carryKind=1;carryRadius=sphere.radius;Bounds=new Bounds(sphere.center,Vector3.one*sphere.radius*2);}
    else if(capsule!=null){carryCollider=capsule;carryKind=2;carryRadius=capsule.radius;carryHeight=capsule.height;carryAxis=capsule.direction;var size=Vector3.one*carryRadius*2;size[carryAxis]=carryHeight;Bounds=new Bounds(capsule.center,size);}
    else if(mesh!=null&&mesh.convex&&mesh.sharedMesh!=null){carryCollider=mesh;carryKind=3;Bounds=mesh.sharedMesh.bounds;}
    else Carryable=false;
    if(carryCollider!=null&&carryCollider.isTrigger)Carryable=false;
   }
  }
  MotionChest.Input();MotionCarry.Input();MotionInstrument.Input();MotionTools.Input();
 }
 internal static bool TryBatShape(out Vector3 low,out Vector3 high,out float radius)
 {
  low=high=default;radius=0;
  if(Melee==null||carryCollider==null||carryKind!=2)return false;
  float axisScale=Mathf.Abs(Scale[carryAxis]);if(axisScale<.0001f)return false;
  radius=carryRadius*Mathf.Max(Mathf.Abs(Scale[(carryAxis+1)%3]),Mathf.Abs(Scale[(carryAxis+2)%3]));
  var extent=Vector3.zero;extent[carryAxis]=Mathf.Max(0,carryHeight*axisScale*.5f-radius)/axisScale;
  low=Bounds.center-extent;high=Bounds.center+extent;return true;
 }
 internal static bool TryCarryPoint(Vector3 palm,Vector3 root,Quaternion rotation,out Vector3 point)
 {
  point=default;if(!Carryable||carryCollider==null||Mathf.Abs(Scale.x)<.0001f||Mathf.Abs(Scale.y)<.0001f||Mathf.Abs(Scale.z)<.0001f)return false;
  var unscaled=Quaternion.Inverse(rotation)*(palm-root);var local=new Vector3(unscaled.x/Scale.x,unscaled.y/Scale.y,unscaled.z/Scale.z);
  if(carryKind==0){point=MotionCarry.BoxSurface(Bounds,local,Scale);return true;}
  if(carryKind==1||carryKind==2)
  {
   var center=Vector3.Scale(Bounds.center,Scale);var v=unscaled-center;var axis=Vector3.zero;
   float radius=carryRadius*(carryKind==1?Mathf.Max(Mathf.Abs(Scale.x),Mathf.Max(Mathf.Abs(Scale.y),Mathf.Abs(Scale.z))):Mathf.Max(Mathf.Abs(Scale[(carryAxis+1)%3]),Mathf.Abs(Scale[(carryAxis+2)%3])));
   if(carryKind==2)axis[carryAxis]=Mathf.Clamp(v[carryAxis],-Mathf.Max(0,carryHeight*Mathf.Abs(Scale[carryAxis])*.5f-radius),Mathf.Max(0,carryHeight*Mathf.Abs(Scale[carryAxis])*.5f-radius));
   var radial=v-axis;if(radial.sqrMagnitude<1e-10f)radial=carryKind==2?(carryAxis==0?Vector3.up:Vector3.right):Vector3.down;var surface=center+axis+radial.normalized*radius;
   point=new Vector3(surface.x/Scale.x,surface.y/Scale.y,surface.z/Scale.z);return true;
  }
  // Held convex colliders are disabled. Reuse their cooked geometry at the
  // explicit rendered pose, enabling only across these synchronous grab-edge
  // queries and restoring the exact flag before any physics step can run.
  bool enabled=carryCollider.enabled;
#if IKAG_DIAGNOSTICS
  float fixedAt=Time.fixedTime;
#endif
  try
  {
   carryCollider.enabled=true;var surface=Physics.ClosestPoint(palm,carryCollider,root,rotation);
   if((surface-palm).sqrMagnitude<1e-10f)
   {
    var center=root+rotation*Vector3.Scale(Bounds.center,Scale);var direction=palm-center;if(direction.sqrMagnitude<1e-10f)direction=rotation*Vector3.down;
    var outside=center+direction.normalized*(Vector3.Scale(Bounds.extents,Scale).magnitude+.25f);surface=Physics.ClosestPoint(outside,carryCollider,root,rotation);
    if((surface-outside).sqrMagnitude<1e-10f)return false;
   }
   var p=Quaternion.Inverse(rotation)*(surface-root);point=new Vector3(p.x/Scale.x,p.y/Scale.y,p.z/Scale.z);return true;
  }
  finally{if(carryCollider!=null)carryCollider.enabled=enabled;
#if IKAG_DIAGNOSTICS
   if(Time.fixedTime!=fixedAt)throw new System.InvalidOperationException("Physics advanced during carry contact query");
#endif
  }
 }
 internal static bool TryRoot(out Vector3 p,out Quaternion q)
 {p=default;q=Quaternion.identity;if(!Selected||!MotionHeldItems.TryAnchor(Owner,out p,out var basis,out var relative))return false;q=basis*relative;return true;}
 internal static bool TryRoomPoint(Vector3 local,out Vector3 world,out Vector3 room,out Quaternion worldFromRoom,out Quaternion rootRotation)
 {
  world=room=default;worldFromRoom=rootRotation=Quaternion.identity;var h=Hand;
  if(!TryRoot(out var p,out var q)||!StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var grip,out var rotation))return false;
  rootRotation=q;worldFromRoom=rotation*Quaternion.Inverse(h.Rotation);world=p+q*Vector3.Scale(local,Scale);room=h.Position+Quaternion.Inverse(worldFromRoom)*(world-grip);return true;
 }
}
