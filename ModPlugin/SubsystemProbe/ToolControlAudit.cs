using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Il2CppInterop.Runtime;
namespace IKnowAGuyVR.SubsystemProbe;
// Native positive controls under an isolated profile backup, before adapters.
internal static class ToolControlAudit
{
 private static int phase;private static float next=25;private static InteractionManager owner;
 private static BreakableProp meleeTarget;private static int meleeBefore;
 private static GameObject clone;private static DartHoldable dart;private static ChestItem chest;
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static T Spawn<T>() where T:MonoBehaviour
 {
  T template=null;foreach(var t in Resources.FindObjectsOfTypeAll<T>())if(!t.gameObject.scene.IsValid()){template=t;break;}
  Check(template!=null,"No prefab "+typeof(T).Name);clone=UnityEngine.Object.Instantiate(template.gameObject);clone.transform.position=owner.transform.position+Vector3.up*3;clone.SetActive(true);clone.GetComponent<NetworkObject>().Spawn();return clone.GetComponent<T>();
 }
 private static void Clear(){if(owner.IsHoldingItem())owner.ForceDropItem();if(clone!=null){var n=clone.GetComponent<NetworkObject>();if(n.IsSpawned)n.Despawn();}clone=null;}
 private static void Offsets(Type type)
 {foreach(var f in type.GetFields(BindingFlags.NonPublic|BindingFlags.Static))if(f.Name.StartsWith("NativeFieldInfoPtr_")){var p=(IntPtr)f.GetValue(null);Plugin.Line("TOOL_FIELD "+type.Name+"."+f.Name.Substring(19)+" offset="+IL2CPP.il2cpp_field_get_offset(p));}}
 internal static void Tick()
 {
  if(Environment.GetEnvironmentVariable("IKAG_XR_TOOL_CONTROL")!="1"||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{switch(phase){
   case 0:
    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
    UiInputAudit.Dump("Assembly-CSharp.dll","","BuildingDamageHandler","CanBeDamaged","TakeDamage");
    UiInputAudit.Dump("Assembly-CSharp.dll","","BreakableProp","CanTakeMeleeHit","OnMeleeHit","AcceptsWeapon");
    Offsets(typeof(MeleeWeapon));Offsets(typeof(InteractionManager));Offsets(typeof(Hammer));
    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"input clear\nhands reset\n");break;
   case 1:
    var hammer=Spawn<Hammer>();var damageObject=GameObject.CreatePrimitive(PrimitiveType.Cube);damageObject.name="NativeHammerControl";damageObject.transform.position=owner.transform.position+Vector3.up*5;
    var target=damageObject.AddComponent<BuildingDamageHandler>();target.maxHits=3;var hitCollider=damageObject.GetComponent<Collider>();
    Physics.SyncTransforms();Plugin.Line("TOOL_HAMMER_FIXTURE can="+target.CanBeDamaged()+" bounds="+hitCollider.bounds+" origin="+damageObject.transform.position);RaycastHit hit=default;Check(target.CanBeDamaged()&&hitCollider.Raycast(new Ray(damageObject.transform.position+Vector3.up,Vector3.down),out hit,2),"Native hammer contact fixture");int before=target.GetCurrentHits();hammer.TryDamageObject(hit,true);Check(target.GetCurrentHits()==before+1,"Native hammer consumer did not damage");Plugin.Line("TOOL_HAMMER_CONTROL_PASS target="+target.name+" collider="+hitCollider.name);UnityEngine.Object.Destroy(damageObject);Clear();break;
   case 2:
    var melee=Spawn<MeleeWeapon>();BreakableProp prop=null;Collider collider=null;
    foreach(var t in UnityEngine.Object.FindObjectsOfType<BreakableProp>())if(t.CanTakeMeleeHit()&&t.AcceptsWeapon(melee.WeaponId)&&t.HitsRemaining>1){foreach(var c in t.GetComponentsInChildren<Collider>())if(c.enabled&&!c.isTrigger){prop=t;collider=c;break;}if(prop!=null)break;}
    Check(prop!=null,"No native melee target");var targetClone=UnityEngine.Object.Instantiate(prop.gameObject);targetClone.transform.position=owner.transform.position+Vector3.up*20;targetClone.SetActive(true);targetClone.GetComponent<NetworkObject>().Spawn();prop=targetClone.GetComponent<BreakableProp>();collider=targetClone.GetComponent<Collider>();if(collider==null)collider=targetClone.GetComponentInChildren<Collider>();Physics.SyncTransforms();var camera=owner.interactionCamera;var tr=camera.transform;var p=tr.localPosition;var q=tr.localRotation;before=prop.networkHits.Value;
    try{tr.SetPositionAndRotation(collider.bounds.center+Vector3.up*(collider.bounds.extents.y+.5f),Quaternion.LookRotation(Vector3.down,Vector3.forward));melee.ResolveHit(camera);}finally{tr.SetLocalPositionAndRotation(p,q);}
    meleeTarget=prop;meleeBefore=before;Plugin.Line("TOOL_MELEE_CONTROL_SENT target="+prop.name+" col="+collider.name+" layer="+collider.gameObject.layer+" id="+melee.WeaponId+" before="+before+" after="+prop.networkHits.Value);
    foreach(var result in MeleeWeapon.hitBuffer)if(result.collider!=null)Plugin.Line("TOOL_MELEE_CONTROL_HIT name="+result.collider.name+" root="+result.collider.transform.root.name+" distance="+result.distance);break;
   case 3:Check(meleeTarget.networkHits.Value==meleeBefore+1,"Native melee consumer did not damage after network tick");Plugin.Line("TOOL_MELEE_CONTROL_PASS target="+meleeTarget.name);meleeTarget.GetComponent<NetworkObject>().Despawn();Clear();dart=Spawn<DartHoldable>();Check(owner.TryPickupItem(dart.Cast<IHoldable>()),"Dart pickup");break;
   case 4:
    Check(owner.cachedIsDart,"Native dart cache missing");owner.primaryPressedThisFrame=true;try{owner.HandleDartThrow();}finally{owner.primaryPressedThisFrame=false;}
    Check(!owner.heldItems.Contains(dart.gameObject)&&dart.IsFlying(),"Native dart release contract");Plugin.Line("TOOL_DART_CONTROL_PASS speed="+dart.velocity.magnitude+" held="+dart.isHeldNetwork.Value+" kinematic="+dart.rb.isKinematic+" collider="+dart.col.enabled);break;
   case 5:Clear();var instrument=Spawn<MusicalInstrument>();float at=instrument.lastSoundTime;instrument.TryPlaySound();Check(instrument.lastSoundTime>at,"Native sound did not play");Plugin.Line("TOOL_INSTRUMENT_CONTROL_PASS index="+instrument.lastSoundIndex);Clear();break;
   case 6:chest=Spawn<ChestItem>();Check(owner.TryPickupItem(chest.Cast<IHoldable>()),"Chest pickup");break;
   case 7:chest.RequestLidOpen(true);Check(chest.isLidOpenNetwork.Value,"Native lid open rejected");Plugin.Line("TOOL_CHEST_CONTROL_PASS angle="+chest.openAngleX+" buried="+chest.IsBuried);break;
   case 8:Plugin.Line("TOOL_CHEST_DEFAULT_REST angle="+chest.currentAngle+" open="+chest.isLidOpenNetwork.Value);chest.RequestLidOpen(false);Clear();Plugin.Line("TOOL_NATIVE_CONTROLS_COMPLETE");phase=99;return;
  }phase++;next=Time.realtimeSinceStartup+.7f;}catch(Exception e){Plugin.Line("TOOL_NATIVE_CONTROL_FAILED phase="+phase+" "+e);Clear();phase=99;}
 }
}
