using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// Cold read-only census. No native item action or pose is changed.
internal static class ToolNativeAudit
{
 private static bool done;
 internal static void Tick()
 {
  if(done||Environment.GetEnvironmentVariable("IKAG_XR_TOOL_NATIVE")!="1"||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<25)return;
  done=true;
  UiInputAudit.Dump("Assembly-CSharp.dll","","Hammer","TrySwing","PlaySwingAnimation","TryDamageObject");
  UiInputAudit.Dump("Assembly-CSharp.dll","","MeleeWeapon","OnInstantUse","SwingRoutine","ResolveHit","PlaySwingSound","PlayHitSound","CanInstantUse","CancelSwing");
  UiInputAudit.Dump("Assembly-CSharp.dll","","DartHoldable","LaunchDart","LaunchRpc","LaunchClientRpc","FixedUpdate","OnDropSideEffect");
  UiInputAudit.Dump("Assembly-CSharp.dll","","InteractionManager","HandleDartThrow","HandleHammerSwing","HandlePrimaryPressed","ConsumeHeldItem","ReleaseItemSilently","DropLastItem");
  UiInputAudit.Dump("Assembly-CSharp.dll","","MusicalInstrument","Update","TryPlaySound","OnHeldChanged");
  UiInputAudit.Dump("Assembly-CSharp.dll","","ChestItem","Update","PollPeekInput","RequestLidOpen","AnimateLid","ApplyLidAngle","SetLidOpenRpc","CanInteract","OnInteract");
  int count=0;var seen=new HashSet<string>();var dir=Environment.GetEnvironmentVariable("XRSIM_DIR");
  foreach(var c in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
  {
   if(c.gameObject.scene.IsValid())continue;var type=c.GetIl2CppType().Name;
   if(Array.IndexOf(new[]{"Hammer","MeleeWeapon","DartHoldable","MusicalInstrument","ChestItem","CargoBox","PizzaBox"},type)<0||!seen.Add(type+"/"+c.name))continue;
   var root=c.transform;Plugin.Line("TOOL_PREFAB type="+type+" name="+c.name+" scale="+root.lossyScale.ToString("R"));count++;
   var hammer=c.TryCast<Hammer>();if(hammer!=null)Plugin.Line("TOOL_HAMMER cooldown="+hammer.swingCooldown);
   var melee=c.TryCast<MeleeWeapon>();if(melee!=null)Plugin.Line("TOOL_MELEE length="+melee.barrelLength+" range="+melee.hitRange+" radius="+melee.hitRadius+" force="+melee.hitForce+" mask="+melee.hitMask.value+" cooldown="+melee.extraCooldown+" hold="+melee.UseHoldDuration);
   var dart=c.TryCast<DartHoldable>();if(dart!=null)Plugin.Line("TOOL_DART speed="+dart.flySpeed+" gravity="+dart.gravity+" time="+dart.maxFlyTime);
   var instrument=c.TryCast<MusicalInstrument>();if(instrument!=null)Plugin.Line("TOOL_INSTRUMENT cooldown="+instrument.soundCooldown+" sounds="+instrument.instrumentSounds?.Length);
   var chest=c.TryCast<ChestItem>();if(chest!=null)Plugin.Line("TOOL_CHEST lid="+chest.lid?.name+" angle="+chest.openAngleX+" speed="+chest.lidSpeed+" closed="+chest.closedLocalRotation.ToString("R"));
   foreach(var t in root.GetComponentsInChildren<Transform>(true))
   {
    var mf=t.GetComponent<MeshFilter>();var mesh=mf==null?null:mf.sharedMesh;var col=t.GetComponent<Collider>();
    Plugin.Line("TOOL_NODE type="+type+" root="+c.name+" node="+t.name+" p="+root.InverseTransformPoint(t.position).ToString("R")+" q="+(Quaternion.Inverse(root.rotation)*t.rotation).ToString("R")+" scale="+t.lossyScale.ToString("R")+" mesh="+mesh?.name+" bounds="+mesh?.bounds+" collider="+col?.GetIl2CppType().Name);
    if(mesh!=null&&mesh.isReadable)
    {
     var label=(type+"-"+c.name+"-"+t.name);foreach(char invalid in Path.GetInvalidFileNameChars())label=label.Replace(invalid,'_');
     using var file=new StreamWriter(Path.Combine(dir,label+".obj"));var matrix=root.worldToLocalMatrix*t.localToWorldMatrix;
     foreach(var v in mesh.vertices){var p=matrix.MultiplyPoint3x4(v);file.WriteLine(FormattableString.Invariant($"v {p.x:R} {p.y:R} {p.z:R}"));}
     var triangles=mesh.triangles;for(int i=0;i<triangles.Length;i+=3)file.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");
    }
   }
  }
  Plugin.Line("TOOL_NATIVE_CENSUS_COMPLETE count="+count);
 }
}
