using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class PhysicalItemAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_PHYSICAL_ITEMS")=="1";
 private static readonly bool edge=Environment.GetEnvironmentVariable("IKAG_XR_PHYSICAL_EDGE")=="1",reverse=Environment.GetEnvironmentVariable("IKAG_XR_PHYSICAL_REVERSE")=="1";
 private static readonly string[] cases=Environment.GetEnvironmentVariable("IKAG_XR_PHYSICAL_BAT_ONLY")=="1"?new[]{"bat","bat"}:edge?new[]{"slow-dart","dart-stack","comfort-hammer","comfort-bat","comfort-chest","lifecycle-carry","lifecycle-lid","lifecycle-tools","guitar-reserve"}:new[]{"hammer","bat","dart0","dart1","dart2","dart3","chest0","chest1","chest2","chest3","chest4","carry0","carry1","carry2","guitar","trumpet"};
 private static InteractionManager owner;private static int index,step;private static float next=25;private static bool left,moving,release;
 private static float moveAt,moveDuration;private static Vector3 moveStart,moveEnd;private static Quaternion moveRotation;private static bool moveLeft;
 private static string moveAfter;
 private static float tracedAt;
 private static GameObject item,target;private static BuildingDamageHandler damage;private static BreakableProp prop;private static DartHoldable dart;private static ChestItem chest;private static MusicalInstrument instrument;
 private static Vector3 startPosition,rootP,headP;private static Quaternion rootQ;private static bool movementEnabled;private static float soundAt;private static int hits;
 private static Vector3 savedCamera;private static Quaternion savedCameraRotation;private static bool cameraPending;private static int cameraActions,scaleIndex=-1;
 internal static void BeforeAction(){if(!enabled||owner==null)return;var t=owner.interactionCamera.transform;savedCamera=t.localPosition;savedCameraRotation=t.localRotation;cameraPending=true;}
 internal static void AfterAction(){if(!cameraPending)return;cameraPending=false;try{var t=owner.interactionCamera.transform;Check(Vector3.Distance(t.localPosition,savedCamera)<1e-6&&1-Mathf.Abs(Quaternion.Dot(t.localRotation,savedCameraRotation))<1e-6,"Native action leaked camera transform");cameraActions++;}catch(Exception e){Fail(e);}}
 private static Transform[] bones;private static Vector3[] positions,scales;private static Quaternion[] rotations;private static bool restorePending;private static int restoreCount;private static float maxPosition,maxRotation,maxScale;
 private static int leftHaptics,rightHaptics;
 internal static void Haptic(bool left,bool sent){if(!enabled||!sent)return;if(left)leftHaptics++;else rightHaptics++;}
 private static void Check(bool b,string why){if(!b)throw new Exception(why);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 internal static string Pose(bool useLeft,Vector3 p,Quaternion q)
 {
  var h=useLeft?ControllerTransport.Left:ControllerTransport.Right;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out var wp,out var wq);var basis=wq*Quaternion.Inverse(h.Rotation);var raw=h.Position+Quaternion.Inverse(basis)*(p-wp);var e=(Quaternion.Inverse(basis)*q).eulerAngles;
  return FormattableString.Invariant($"hand {(useLeft?"l":"r")} grip pose {raw.x:R} {raw.y:R} {-raw.z:R} {-e.y:R} {-e.x:R} {-e.z:R}");
 }
 private static T Spawn<T>(int which=0) where T:MonoBehaviour
 {
  var sources=new List<T>();foreach(var t in Resources.FindObjectsOfTypeAll<T>())if(!t.gameObject.scene.IsValid())sources.Add(t);
  Check(which<sources.Count,"Missing prefab "+typeof(T).Name+"/"+which);item=UnityEngine.Object.Instantiate(sources[which].gameObject);item.transform.position=owner.transform.position+Vector3.up*3;item.SetActive(true);item.GetComponent<NetworkObject>().Spawn();return item.GetComponent<T>();
 }
 private static void Pick(){Check(owner.TryPickupItem(item.GetComponent<HoldableBase>().Cast<IHoldable>()),"Native pickup failed");Send(edge&&cases[index].StartsWith("comfort")?"input clear":"grip "+(left?"l":"r")+" 1");}
 private static void PutRoot(Vector3 p,Quaternion q)
 {
  Check(PhysicalHeldItems.TryRoot(out _,out var current),"No physical root");var h=PhysicalHeldItems.Hand;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var handQ),"No primary pose");var relative=Quaternion.Inverse(handQ)*current;var desired=q*Quaternion.Inverse(relative);Vector3 surface=Vector3.zero;
  if(NaturalItemGrip.FlatHand)Check(MotionBody.TryCarryFrame(left,out surface,out _,out _,out _,out _),"No palm surface");
  else if(NaturalItemGrip.PinchingHand)Check(MotionBody.TryCarryFrame(left,out _,out _,out _,out surface,out _),"No pinch surface");
  var grip=p+q*Vector3.Scale(NaturalItemGrip.ContactPoint,PhysicalHeldItems.Scale)-desired*surface;Send(Pose(left,grip,desired));rootP=p;rootQ=q;
 }
 private static void Move(bool useLeft,Vector3 delta,float duration=.25f,bool letGo=false)
 {var h=useLeft?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(h.Position,h.Rotation,out moveStart,out moveRotation),"No moving hand");moveEnd=moveStart+delta;moveLeft=useLeft;moveAt=Time.realtimeSinceStartup;moveDuration=duration;release=letGo;moveAfter=null;moving=true;}
 private static void EndCase()
 {
  if(owner!=null&&owner.IsHoldingItem())owner.ForceDropItem();
  foreach(var obj in new[]{item,target})if(obj!=null){var net=obj.GetComponent<NetworkObject>();if(net!=null&&net.IsSpawned)net.Despawn();else UnityEngine.Object.Destroy(obj);}
  item=target=null;damage=null;prop=null;dart=null;chest=null;instrument=null;Send("input clear\nhands reset\nhead pos 0 1.6 0");index++;step=0;next=Time.realtimeSinceStartup+.8f;
 }
 private static void Fail(Exception e)
 {moving=false;Plugin.Line("PHYSICAL_ITEMS_FAILED case="+(index<cases.Length?cases[index]:"end")+" step="+step+" "+e);Send("input clear\nhands reset");if(owner!=null){owner.movementController.enabled=movementEnabled;owner.movementController.Teleport(startPosition);}step=99;}
 internal static void Before(Animator animator)
 {
  if(!enabled||step==99||owner==null)return;
  if(bones==null){var all=animator.GetComponentsInChildren<Transform>(true);bones=new Transform[all.Length];positions=new Vector3[all.Length];rotations=new Quaternion[all.Length];scales=new Vector3[all.Length];for(int i=0;i<all.Length;i++)bones[i]=all[i];}
  for(int i=0;i<bones.Length;i++){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;scales[i]=bones[i].localScale;}restorePending=true;
 }
 internal static void Rendered()
 {
  if(!enabled||step==99||owner==null)return;
  try{if(item!=null&&PhysicalHeldItems.Selected&&scaleIndex!=index){scaleIndex=index;Plugin.Line("PHYSICAL_MODEL_SCALE case="+cases[index]+" cached="+PhysicalHeldItems.Scale.ToString("R")+" rendered="+item.transform.lossyScale.ToString("R"));Check(Vector3.Distance(PhysicalHeldItems.Scale,item.transform.lossyScale)<.0001f,"Simulation/render model scale mismatch");}foreach(bool side in new[]{false,true})if(PhysicalHeldItems.Attached(side))
   {Check(PhysicalHeldItems.HandPose(side,out var p,out _),"Missing attached hand");if(MotionCarry.Supporting){MotionBody.TryCarryFrame(side,out var surface,out _,out _,out _,out _);PhysicalHeldItems.HandPose(side,out p,out var q);Check(Vector3.Distance(p+q*surface,MotionBody.RenderedContact(side,true,false))<.001f,"Carry palm contact leaked");}else Check(Vector3.Distance(p,MotionBody.RenderedContact(side,false,false))<.001f,"Lid hand contact leaked");}
  }catch(Exception e){Fail(e);}
 }
 internal static void Restored()
 {
  if(!restorePending)return;restorePending=false;
  try{for(int i=0;i<bones.Length;i++){maxPosition=Math.Max(maxPosition,Vector3.Distance(positions[i],bones[i].localPosition));maxScale=Math.Max(maxScale,Vector3.Distance(scales[i],bones[i].localScale));maxRotation=Math.Max(maxRotation,1-Mathf.Abs(Quaternion.Dot(rotations[i],bones[i].localRotation)));}restoreCount++;Check(maxPosition<1e-6&&maxScale<1e-6&&maxRotation<1e-6,"Avatar restoration leaked");}catch(Exception e){Fail(e);}
 }
 internal static void Tick()
 {
  if(!enabled||step==99||!StereoTrial.GameplayActive)return;
  try{
   if(owner!=null&&cameraPending){var t=owner.interactionCamera.transform;Check(Vector3.Distance(t.localPosition,savedCamera)<1e-5&&1-Mathf.Abs(Quaternion.Dot(t.localRotation,savedCameraRotation))<1e-6,"Tool action leaked camera pose");cameraPending=false;}
   if(moving)
   {if(Time.realtimeSinceStartup>=tracedAt){tracedAt=Time.realtimeSinceStartup+.05f;PhysicalHeldItems.TryRoot(out var rp,out var rq);Plugin.Line("PHYSICAL_MOVE_TRACE case="+cases[index]+" "+MotionTools.DebugState+" root="+rp.ToString("R")+" rotation="+rq.ToString("R")+" target="+target?.transform.position.ToString("R"));}float t=Mathf.Clamp01((Time.realtimeSinceStartup-moveAt)/moveDuration);var command=Pose(moveLeft,Vector3.Lerp(moveStart,moveEnd,t),moveRotation);if(t>=1){moving=false;if(release)command+="\ngrip "+(moveLeft?"l":"r")+" 0";if(moveAfter!=null)command+="\n"+moveAfter;next=Time.realtimeSinceStartup+.65f;}Send(command);return;}
   if(Time.realtimeSinceStartup<next)return;
   if(owner==null){owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);startPosition=owner.movementController.transform.position;movementEnabled=owner.movementController.enabled;owner.movementController.Teleport(startPosition+Vector3.up*20);owner.movementController.enabled=false;Send("input clear\nhands reset\nhead rot 0 -25 0");next=Time.realtimeSinceStartup+1;return;}
   if(index>=cases.Length){owner.movementController.Teleport(startPosition);owner.movementController.enabled=movementEnabled;Check(restoreCount>100&&leftHaptics>0&&rightHaptics>0,"Restoration/haptic coverage incomplete");Plugin.Line("PHYSICAL_ITEMS_RESTORATION samples="+restoreCount+" bones="+bones.Length+" position="+maxPosition+" rotation="+maxRotation+" scale="+maxScale+" cameraActions="+cameraActions+" haptics="+leftHaptics+"/"+rightHaptics);StereoTrial.DiagnosticMetrics();Plugin.Line("PHYSICAL_ITEMS_COMPLETE");step=99;return;}
   string kind=cases[index];left=((index%2)==1)^reverse;if(kind.StartsWith("comfort"))left=false;
   if(step==0)
   {
    if(kind=="hammer"||kind=="comfort-hammer")Spawn<Hammer>();else if(kind=="bat"||kind=="comfort-bat")Spawn<MeleeWeapon>();else if(kind=="slow-dart"||kind=="dart-stack"||kind=="lifecycle-tools")dart=Spawn<DartHoldable>();else if(kind=="comfort-chest"||kind=="lifecycle-lid")chest=Spawn<ChestItem>();else if(kind=="lifecycle-carry")Spawn<CargoBox>();else if(kind.StartsWith("dart"))dart=Spawn<DartHoldable>(int.Parse(kind.Substring(4)));else if(kind.StartsWith("chest"))chest=Spawn<ChestItem>(int.Parse(kind.Substring(5)));else if(kind.StartsWith("carry")){int n=int.Parse(kind.Substring(5));if(n==0)Spawn<CargoBox>();else if(n==1)Spawn<PizzaBox>();else chest=Spawn<ChestItem>();}else instrument=Spawn<MusicalInstrument>(kind=="guitar"||kind=="guitar-reserve"?0:1);
    Pick();step++;next=Time.realtimeSinceStartup+.65f;return;
   }
   if(step==1)
   {
    if(!kind.StartsWith("comfort"))ObjectGrabs.AdoptDistance(owner,item,left);Check(PhysicalHeldItems.Item==item,"Selection cache mismatch");StereoTrial.TryHeadWorld(out var hp,out var hq);var forward=Vector3.ProjectOnPlane(hq*Vector3.forward,Vector3.up).normalized;
    rootQ=Quaternion.LookRotation(forward,Vector3.up);rootP=hp+forward*(kind=="guitar"?1.1f:.8f)-Vector3.up*.65f;
    if(kind=="hammer"||kind=="bat"){rootQ=Quaternion.identity;headP=hp+forward*.8f-Vector3.up*.2f;rootP=headP-Vector3.Scale(kind=="hammer"?MotionTools.HammerHead:MotionTools.BatHead,PhysicalHeldItems.Scale);}
    PutRoot(rootP,rootQ);step++;next=Time.realtimeSinceStartup+.8f;return;
   }
   if(edge)TestEdge(kind);
   else if(kind=="hammer"||kind=="bat")TestSwing(kind);
   else if(kind.StartsWith("dart"))TestDart();
   else if(kind.StartsWith("chest"))TestChest();
   else if(kind.StartsWith("carry"))TestCarry();
   else TestInstrument(kind=="guitar");
   if(step!=99){step++;next=Time.realtimeSinceStartup+.65f;}
  }catch(Exception e){Fail(e);}
 }
 private static void TestSwing(string kind)
 {
  switch(step){
   case 2:
    var center=headP+Vector3.right*.35f-(kind=="bat"?Vector3.up*.13f:Vector3.zero);
    if(kind=="hammer"){target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.transform.position=center;target.transform.localScale=Vector3.one*.15f;damage=target.AddComponent<BuildingDamageHandler>();damage.maxHits=3;}
    else{BreakableProp source=null;foreach(var b in UnityEngine.Object.FindObjectsOfType<BreakableProp>())if(b.CanTakeMeleeHit()&&b.AcceptsWeapon(PhysicalHeldItems.Melee.WeaponId)&&b.HitsRemaining>1){source=b;break;}Check(source!=null,"No native melee fixture");target=UnityEngine.Object.Instantiate(source.gameObject);target.SetActive(true);target.GetComponent<NetworkObject>().Spawn();prop=target.GetComponent<BreakableProp>();var c=target.GetComponent<Collider>();target.transform.position+=center-c.bounds.center;}
    if(kind=="bat"){var box=target.GetComponent<BoxCollider>();Check(box!=null,"Missing narrow barrel fixture");var size=box.size;size.y=.06f/Mathf.Abs(target.transform.lossyScale.y);box.size=size;}
    Physics.SyncTransforms();hits=kind=="hammer"?damage.GetCurrentHits():prop.networkHits.Value;break;
   case 3:Check((kind=="hammer"?damage.GetCurrentHits():prop.networkHits.Value)==hits,"Stationary tool damaged target");Send("shot physical-"+kind+"-before");owner.movementController.Teleport(owner.movementController.transform.position+Vector3.right*.1f);break;
   case 4:Check((kind=="hammer"?damage.GetCurrentHits():prop.networkHits.Value)==hits,"Artificial movement swung tool");owner.movementController.Teleport(owner.movementController.transform.position-Vector3.right*.1f);break;
   case 5:Move(left,Vector3.right*.6f,.3f);break;
   case 6:Check((kind=="hammer"?damage.GetCurrentHits():prop.networkHits.Value)==hits+1,"Physical swing did not produce one native hit");Send("shot physical-"+kind);break;
   case 7:Check((kind=="hammer"?damage.GetCurrentHits():prop.networkHits.Value)==hits+1,"Stationary contact repeated damage");Plugin.Line("PHYSICAL_SWING_PASS kind="+kind+" left="+left);EndCase();step=-1;break;
  }
 }
 private static void TestDart()
 {
  switch(step){
   case 2:Check(!dart.IsFlying(),"Stationary dart launched");break;
   case 3:Move(left,rootQ*Vector3.forward*.5f,.22f,true);break;
   case 4:Check(!owner.heldItems.Contains(item)&&dart.IsFlying(),"Grip release did not launch dart");Plugin.Line("PHYSICAL_DART_PASS name="+item.name+" left="+left);EndCase();step=-1;break;
  }
 }
 private static Vector3 LidPoint(float angle)
 {
  Check(PhysicalHeldItems.TryRoot(out var p,out var q),"No chest pose");var lid=chest.lid;var mesh=lid.GetComponent<MeshFilter>().sharedMesh;var bounds=mesh.bounds;var local=new Vector3(bounds.center.x,bounds.max.y,bounds.min.z+.05f);
  var closed=chest.transform.worldToLocalMatrix*lid.parent.localToWorldMatrix*Matrix4x4.TRS(lid.localPosition,chest.closedLocalRotation,lid.localScale);
  return (Matrix4x4.TRS(p,q,PhysicalHeldItems.Scale)*closed).MultiplyPoint3x4(Quaternion.AngleAxis(angle,Vector3.right)*local);
 }
 private static void TestChest()
 {
  switch(step){
   case 2:StereoTrial.TryTrackingPose(PhysicalHeldItems.Other.Position,PhysicalHeldItems.Other.Rotation,out _,out var oq);Send(Pose(!left,LidPoint(0),oq)+"\nhead move 0 0 .8");break;
   case 3:Send("grip "+(!left?"l":"r")+" 1");break;
   case 4:Check(MotionChest.OwnsGrip(!left)&&!MotionCarry.Supporting&&!DistanceGrips.Pending&&!KitchenHands.Acquiring,"Lid grip priority failed");Move(!left,LidPoint(45)-LidPoint(0),.5f);break;
   case 5:Check(chest.currentAngle>40&&chest.isLidOpenNetwork.Value,"Lid did not open with hand");Send("shot physical-chest-"+index+"\ngrip "+(!left?"l":"r")+" 0");break;
   case 6:Check(owner.heldItems.Contains(item)&&chest.isLidOpenNetwork.Value&&chest.currentAngle>40,"Lid release dropped chest or auto-closed");Send("grip "+(!left?"l":"r")+" 1");break;
   case 7:Check(MotionChest.OwnsGrip(!left),"Lid could not be grabbed twice");Move(!left,LidPoint(0)-LidPoint(45),.5f);break;
   case 8:Check(chest.currentAngle<5&&!chest.isLidOpenNetwork.Value,"Lid did not close with hand");Send("grip "+(!left?"l":"r")+" 0");break;
   case 9:Plugin.Line("PHYSICAL_CHEST_PASS name="+item.name+" left="+left);EndCase();step=-1;break;
  }
 }
 private static void TestCarry()
 {
  switch(step){
   case 2:
    Check(PhysicalHeldItems.Carryable&&NaturalItemGrip.FlatHand,"No carry geometry");var b=PhysicalHeldItems.Bounds;var local=new Vector3(b.min.x+.07f,b.min.y,b.center.z);var h=PhysicalHeldItems.Hand;StereoTrial.TryTrackingPose(h.Position,h.Rotation,out _,out var handQ);MotionBody.TryCarryFrame(!left,out var palm,out _,out _,out _,out _);PhysicalHeldItems.TryRoot(out var p,out var q);Send(Pose(!left,p+q*Vector3.Scale(local,PhysicalHeldItems.Scale)-handQ*palm,handQ)+(chest!=null?"\nhead move 0 0 .8":""));break;
   case 3:Send("grip "+(!left?"l":"r")+" 1");break;
   case 4:Check(MotionCarry.Supporting&&MotionCarry.OwnsGrip(!left)&&!DistanceGrips.Pending&&!KitchenHands.Acquiring,"Carry support did not own grip");Move(!left,Vector3.up*.15f,.5f);break;
   case 5:Check(MotionCarry.Supporting&&owner.heldItems.Contains(item)&&ObjectGrabs.LeftHand==left,"Support moved ownership");Send("shot physical-carry-"+index+"\ngrip "+(!left?"l":"r")+" 0");break;
   case 6:Check(!MotionCarry.Supporting&&owner.heldItems.Contains(item),"Support release dropped item");Send("grip "+(left?"l":"r")+" 0");break;
   case 7:Check(!owner.heldItems.Contains(item)&&!MotionCarry.Supporting,"Primary release did not drop");Plugin.Line("PHYSICAL_CARRY_PASS name="+item.name+" left="+left);EndCase();step=-1;break;
  }
 }
 private static void TestInstrument(bool guitar)
 {
  switch(step){
   case 2:
    Check(instrument.name.Contains(guitar?"Guitar":"Trumpet"),"Instrument ordering changed");PhysicalHeldItems.TryRoot(out var p,out var q);StereoTrial.TryTrackingPose(PhysicalHeldItems.Other.Position,PhysicalHeldItems.Other.Rotation,out _,out var handQ);MotionBody.TryCarryFrame(!left,out _,out _,out _,out var pinch,out _);
    var local=guitar?new Vector3(.12f,.09f,-.04317f):new Vector3(-.027466f,.34f,-.04f);Send(Pose(!left,p+q*Vector3.Scale(local,PhysicalHeldItems.Scale)-handQ*pinch,handQ));soundAt=instrument.lastSoundTime;break;
   case 3:Check(instrument.lastSoundTime==soundAt,"Stationary instrument played");Move(!left,rootQ*(guitar?Vector3.left*.24f:Vector3.down*.11f),.3f);break;
   case 4:Check(instrument.lastSoundTime>soundAt,"Physical instrument gesture did not play");soundAt=instrument.lastSoundTime;Send("shot physical-"+(guitar?"guitar":"trumpet"));break;
   case 5:Check(instrument.lastSoundTime==soundAt,"Stationary instrument repeated sound");Plugin.Line("PHYSICAL_INSTRUMENT_PASS guitar="+guitar+" left="+left);EndCase();step=-1;break;
  }
 }
 private static string Primary=>left?"l":"r";
 private static string Secondary=>left?"r":"l";
 private static void SupportPoint()
 {
  var b=PhysicalHeldItems.Bounds;var local=new Vector3(b.min.x+.07f,b.min.y,b.center.z);StereoTrial.TryTrackingPose(PhysicalHeldItems.Hand.Position,PhysicalHeldItems.Hand.Rotation,out _,out var handQ);MotionBody.TryCarryFrame(!left,out var palm,out _,out _,out _,out _);PhysicalHeldItems.TryRoot(out var p,out var q);Send(Pose(!left,p+q*Vector3.Scale(local,PhysicalHeldItems.Scale)-handQ*palm,handQ));
 }
 private static void PlaceLidHand()
 {StereoTrial.TryTrackingPose(PhysicalHeldItems.Other.Position,PhysicalHeldItems.Other.Rotation,out _,out var q);Send(Pose(!left,LidPoint(chest.currentAngle),q));}
 private static void ComfortTarget(bool bat)
 {
  StereoTrial.TryHeadWorld(out var hp,out var hq);var center=hp+hq*Vector3.forward;
  if(!bat){target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.transform.position=center;target.transform.localScale=Vector3.one*.15f;damage=target.AddComponent<BuildingDamageHandler>();damage.maxHits=3;hits=damage.GetCurrentHits();}
  else{BreakableProp source=null;foreach(var b in UnityEngine.Object.FindObjectsOfType<BreakableProp>())if(b.CanTakeMeleeHit()&&b.AcceptsWeapon(PhysicalHeldItems.Melee.WeaponId)&&b.HitsRemaining>1){source=b;break;}Check(source!=null,"No comfort target");target=UnityEngine.Object.Instantiate(source.gameObject);target.SetActive(true);target.GetComponent<NetworkObject>().Spawn();prop=target.GetComponent<BreakableProp>();target.transform.position+=center-target.GetComponent<Collider>().bounds.center;hits=prop.networkHits.Value;}
  Physics.SyncTransforms();Send(MotionPointer.AimAtWorld(center));
 }
 private static void TestEdge(string kind)
 {
  if(kind=="slow-dart")
  {
   if(step==2)Send("grip "+Primary+" 0");
   else if(step==3){Check(!owner.heldItems.Contains(item)&&!dart.IsFlying(),"Slow release threw instead of dropping");Plugin.Line("PHYSICAL_SLOW_DROP_PASS");EndCase();step=-1;}
  }
  else if(kind=="dart-stack")
  {
   if(step==2)
   {
    DartHoldable source=null;foreach(var t in Resources.FindObjectsOfTypeAll<DartHoldable>())if(!t.gameObject.scene.IsValid()){source=t;break;}
    target=UnityEngine.Object.Instantiate(source.gameObject);target.transform.position=owner.transform.position+Vector3.up*3;target.SetActive(true);target.GetComponent<NetworkObject>().Spawn();dart=target.GetComponent<DartHoldable>();
    if(!owner.TryPickupItem(dart.Cast<IHoldable>())){Plugin.Line("PHYSICAL_DART_STACK_NATIVE_REJECT max="+owner.GetEffectiveMaxStackSize());EndCase();step=-1;return;}
   }
   else if(step==3){Check(owner.heldItems.Count==2&&owner.cachedTopItem==target,"Dart stack fixture");Move(left,rootQ*Vector3.forward*.5f,.22f,true);}
   else if(step==4){Check(!owner.heldItems.Contains(target)&&dart.IsFlying()&&owner.heldItems.Contains(item)&&owner.heldItems.Count==1,"Stack grip release did not launch exactly the top dart");Plugin.Line("PHYSICAL_DART_STACK_PASS");EndCase();step=-1;}
  }
  else if(kind=="comfort-hammer"||kind=="comfort-bat")
  {
   bool bat=kind=="comfort-bat";
   if(step==2)ComfortTarget(bat);
   else if(step==3){Plugin.Line("PHYSICAL_COMFORT_RAY bat="+bat+" ray="+owner.interactionRay+" hit="+owner.hasHit+"/"+owner.rayHit.collider?.name+" targetBounds="+target.GetComponent<Collider>().bounds);if(!bat)Check(owner.hasHit&&owner.rayHit.collider!=null&&owner.rayHit.collider.transform.IsChildOf(target.transform),"Comfort controller ray missed native target");Send("trigger r 1");}
   else if(step==4){Check((bat?prop.networkHits.Value:damage.GetCurrentHits())==hits+1,"RT comfort hit failed");if(bat)Check(PhysicalHeldItems.Melee.swingRoutine==null,"Comfort bat ran canned swing");Send("trigger r 0");}
   else if(step==5){Plugin.Line("PHYSICAL_COMFORT_PASS kind="+kind);EndCase();step=-1;}
  }
  else if(kind=="comfort-chest")
  {
   if(step==2)Send("trigger r 1");
   else if(step==3)Check(chest.currentAngle>40&&chest.isLidOpenNetwork.Value,"RT chest did not open");
   else if(step==4)Send("trigger r 0");
   else if(step==5){Check(chest.currentAngle<5&&!chest.isLidOpenNetwork.Value,"RT chest release did not close");Plugin.Line("PHYSICAL_COMFORT_CHEST_PASS");EndCase();step=-1;}
  }
  else if(kind=="lifecycle-carry")
  {
   if(step==2)SupportPoint();
   else if(step==3)Send("grip "+Secondary+" 1");
   else if(step==4){Check(MotionCarry.Supporting,"No carry for focus loss");Check(!HandPhysics.ContactEnabled(!left),"Supporting palm physics enabled");Send("focus lose 1000");}
   else if(step==5)Check(!MotionCarry.Supporting&&owner.heldItems.Contains(item),"Focus loss retained support or dropped item");
   else if(step==6)Send("input clear");
   else if(step==7){Check(PhysicalHeldItems.Ready,"Focus did not recover");Send("grip "+Primary+" 1");}
   else if(step==8){ObjectGrabs.AdoptDistance(owner,item,left);SupportPoint();}
   else if(step==9)Send("grip "+Secondary+" 1");
   else if(step==10){Check(MotionCarry.Supporting,"Carry could not reattach");Send("hand "+Secondary+" valid off");}
   else if(step==11){Check(!MotionCarry.Supporting&&owner.heldItems.Contains(item),"Tracking loss retained support or dropped item");Send("hand "+Secondary+" valid on\ninput clear");}
   else if(step==12){Check(PhysicalHeldItems.Ready&&!MotionCarry.Supporting,"Tracking return resumed stale grip");Plugin.Line("PHYSICAL_CARRY_LIFECYCLE_PASS");EndCase();step=-1;}
  }
  else if(kind=="lifecycle-lid")
  {
   if(step==2)PlaceLidHand();
   else if(step==3)Send("grip "+Secondary+" 1");
   else if(step==4){Check(MotionChest.OwnsGrip(!left),"No lid for loss test");Check(!HandPhysics.ContactEnabled(!left),"Lid palm physics enabled");Send("hand "+Secondary+" valid off");}
   else if(step==5){Check(!MotionChest.OwnsGrip(!left)&&owner.heldItems.Contains(item),"Tracking loss retained lid or dropped chest");Send("hand "+Secondary+" valid on\ninput clear");}
   else if(step==6){Check(PhysicalHeldItems.Ready,"Lid tracking return failed");Send("grip "+Primary+" 1");}
   else if(step==7){ObjectGrabs.AdoptDistance(owner,item,left);PlaceLidHand();}
   else if(step==8)Send("grip "+Secondary+" 1");
   else if(step==9){Check(MotionChest.OwnsGrip(!left),"Lid could not reattach");Send("focus lose 1000");}
   else if(step==10)Check(!MotionChest.OwnsGrip(!left)&&owner.heldItems.Contains(item),"Focus loss retained lid or dropped chest");
   else if(step==11)Send("input clear");
   else if(step==12){Check(PhysicalHeldItems.Ready,"Lid focus return failed");Plugin.Line("PHYSICAL_LID_LIFECYCLE_PASS");EndCase();step=-1;}
  }
  else if(kind=="lifecycle-tools")
  {
   if(step==2){Move(left,rootQ*Vector3.forward*.4f,.2f);moveAfter="focus lose 1000";}
   else if(step==3)Send("grip "+Primary+" 0");
   else if(step==4){Check(owner.heldItems.Contains(item)&&!dart.IsFlying(),"Focus loss emitted stale dart throw");Send("input clear");}
   else if(step==5){Check(PhysicalHeldItems.Ready,"Dart focus recovery failed");Send("grip "+Primary+" 1");}
   else if(step==6)ObjectGrabs.AdoptDistance(owner,item,left);
   else if(step==7){Move(left,rootQ*Vector3.forward*.4f,.2f);moveAfter="hand "+Primary+" valid off";}
   else if(step==8)Send("grip "+Primary+" 0");
   else if(step==9){Check(owner.heldItems.Contains(item)&&!dart.IsFlying(),"Tracking loss emitted stale dart throw");Send("hand "+Primary+" valid on\ninput clear");}
   else if(step==10){Check(PhysicalHeldItems.Ready,"Dart tracking recovery failed");Plugin.Line("PHYSICAL_TOOLS_LIFECYCLE_PASS");EndCase();step=-1;}
  }
  else if(kind=="guitar-reserve")
  {
   if(step==2)TestInstrument(true);
   else if(step==3)Send("grip "+Secondary+" 1");
   else if(step==4){Check(PhysicalHeldItems.OwnsGrip(!left)&&!DistanceGrips.Pending&&!KitchenHands.Acquiring,"Strum grip started another action");Move(!left,rootQ*Vector3.left*.24f,.3f);}
   else if(step==5){Check(instrument.lastSoundTime>soundAt,"Closed-hand strum failed");Plugin.Line("PHYSICAL_INSTRUMENT_RESERVATION_PASS");EndCase();step=-1;}
  }
 }
}
