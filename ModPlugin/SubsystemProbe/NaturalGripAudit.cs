using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class NaturalGripAudit
{
 private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_NATURAL_GRIPS")=="1";
 private static bool done,captured;
 internal static void Tick()
 {
  if(!enabled||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<25)return;
  if(done){if(!captured&&Time.realtimeSinceStartup>30){captured=true;if(Environment.GetEnvironmentVariable("IKAG_XR_RENDERDOC")=="1")RenderDocCapture.Trigger();File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"shot depth-baseline\n");}return;}done=true;
  var seen=new HashSet<string>();
  foreach(var h in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
  {
   var hold=h.TryCast<IHoldable>();if(hold==null)continue;
   string key=h.GetIl2CppType().Name+"/"+h.name;if(!seen.Add(key))continue;
   Plugin.Line("NATURAL_ITEM type="+h.GetIl2CppType().Name+" name="+h.name+" scene="+h.gameObject.scene.name+" scale="+h.transform.lossyScale);
   try{var config=InteractionManager.LocalInstance.GetHoldingConfiguration(h.gameObject);Plugin.Line("NATURAL_CONFIG item="+key+" position="+config.position+" rotation="+config.rotation);}catch(Exception e){Plugin.Line("NATURAL_CONFIG_ERROR "+key+" "+e.Message);}
   if(h.TryCast<Shovel>()!=null||h.TryCast<Pizza>()!=null||h.TryCast<Hammer>()!=null)
   foreach(var t in h.GetComponentsInChildren<Transform>(true))
   {
    var f=t.GetComponent<MeshFilter>();Plugin.Line("NATURAL_NODE item="+key+" node="+t.name+" local="+h.transform.InverseTransformPoint(t.position)+" rot="+(Quaternion.Inverse(h.transform.rotation)*t.rotation)+" mesh="+f?.sharedMesh?.name+" bounds="+f?.sharedMesh?.bounds+" readable="+f?.sharedMesh?.isReadable);
   }
  }
  Plugin.Line("NATURAL_CENSUS_COMPLETE unique="+seen.Count);
  InventoryManager.LocalInstance.SetSelectedSlot(1);
  File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"hands reset\nhand l grip pos .18 1.30 -.25\ngrip l 1\n");
 }
}
