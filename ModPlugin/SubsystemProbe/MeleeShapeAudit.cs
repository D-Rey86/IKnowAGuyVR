using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace IKnowAGuyVR.SubsystemProbe;
// Discriminating API/consumer oracle; not part of the physical input adapter.
internal static class MeleeShapeAudit
{
 private static int phase;private static float next=25;private static GameObject target;private static BreakableProp prop;private static int hits;
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 internal static void Tick()
 {
  if(Environment.GetEnvironmentVariable("IKAG_XR_MELEE_SHAPE")!="1"||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try{
   if(phase==0)
   {
    var owner=InteractionManager.LocalInstance;MeleeWeapon weapon=null;foreach(var m in Resources.FindObjectsOfTypeAll<MeleeWeapon>())if(!m.gameObject.scene.IsValid()){weapon=m;break;}Check(weapon!=null,"No bat settings");
    var camera=owner.interactionCamera;var tr=camera.transform;var savedP=tr.localPosition;var savedQ=tr.localRotation;
    try{tr.SetPositionAndRotation(owner.transform.position+Vector3.up*10000,Quaternion.LookRotation(Vector3.up,Vector3.forward));weapon.ResolveHit(camera);}finally{tr.SetLocalPositionAndRotation(savedP,savedQ);}
    IntPtr module=IntPtr.Zero;foreach(System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)if(m.ModuleName=="GameAssembly.dll"){module=m.BaseAddress;break;}Check(module!=IntPtr.Zero,"No module");
    foreach(int rva in new[]{0x53134B8,0x53116C8}){var type=Marshal.ReadIntPtr(module+rva);Plugin.Line("MELEE_NATIVE_INTERFACE rva="+rva.ToString("X")+" name="+Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(type)));}
    BreakableProp source=null;foreach(var b in UnityEngine.Object.FindObjectsOfType<BreakableProp>())if(b.CanTakeMeleeHit()&&b.AcceptsWeapon(weapon.WeaponId)&&b.HitsRemaining>1){source=b;break;}Check(source!=null,"No eligible prop");
    target=UnityEngine.Object.Instantiate(source.gameObject);target.transform.position=owner.transform.position+Vector3.up*20;target.SetActive(true);target.GetComponent<NetworkObject>().Spawn();prop=target.GetComponent<BreakableProp>();Physics.SyncTransforms();var bounds=target.GetComponent<Collider>().bounds;var center=bounds.center;
    var buffer=new Il2CppStructArray<RaycastHit>(16);var a=center-Vector3.right*(bounds.extents.x+.5f)-Vector3.up*.2f;var bpoint=a+Vector3.up*.4f;
    int count=Physics.CapsuleCastNonAlloc(a,bpoint,.033713f,Vector3.right,buffer,bounds.size.x+1,-1&~0x404,QueryTriggerInteraction.Ignore);Check(count>0,"Capsule oracle missed");
    RaycastHit selected=default;float nearest=float.PositiveInfinity;
    for(int i=0;i<count;i++)if(buffer[i].collider!=null&&buffer[i].distance<nearest){selected=buffer[i];nearest=selected.distance;}
    var consumer=selected.collider.GetComponentInParent<IMeleeTarget>();Check(consumer!=null&&consumer.CanTakeMeleeHit(),"Native interface lookup/gate failed");hits=prop.networkHits.Value;
    var overlaps=new Il2CppReferenceArray<Collider>(16);Check(Physics.OverlapCapsuleNonAlloc(center-Vector3.up*.2f,center+Vector3.up*.2f,.033713f,overlaps,-1&~0x404,QueryTriggerInteraction.Ignore)>0,"Rotational capsule overlap unavailable");
    consumer.OnMeleeHit(new MeleeHitInfo(selected.point,Vector3.right,selected.normal,weapon.hitForce,weapon.WeaponId));
    Plugin.Line("MELEE_CAPSULE_SENT count="+count+" collider="+selected.collider.name+" before="+hits+" after="+prop.networkHits.Value+" distance="+selected.distance);
   }
   else{Check(prop.networkHits.Value==hits+1,"Direct native interface did not preserve damage");target.GetComponent<NetworkObject>().Despawn();Plugin.Line("MELEE_SHAPE_AUDIT_COMPLETE");phase=99;return;}
   phase++;next=Time.realtimeSinceStartup+.7f;
  }catch(Exception e){Plugin.Line("MELEE_SHAPE_AUDIT_FAILED "+e);phase=99;}
 }
}
