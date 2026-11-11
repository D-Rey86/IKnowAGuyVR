using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// Cold scene/prefab census only; no target, state or camera writes.
internal static class WorldPressSurvey
{
 private static bool done;
 private static readonly string[] names={"ShopShutterController","BackroomDoorController","CourierOrderButton","LightSwitch","BeverageButton","IncineratorButton","WashingMachineButton","RegisterBell","POSKeyButton","RadioController","PizzaBoxDispenser","BeerMugDispenser","SwitchArm"};
 private static object V(Vector3 v)=>new[]{v.x,v.y,v.z};
 private static string PathOf(Transform t){string p=t.name;for(t=t.parent;t!=null;t=t.parent)p=t.name+"/"+p;return p;}
 internal static void Tick()
 {
  if(done||Environment.GetEnvironmentVariable("IKAG_XR_WORLD_PRESS_SURVEY")!="1"||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<25)return;done=true;
  try{
   foreach(string name in names)UiInputAudit.Dump("Assembly-CSharp.dll",name=="LightSwitch"||name=="RadioController"?"ProjectOne.Gameplay":"",name,"OnInteract","CanInteract","AnimateButtonPress","Press","PressKey","PressButton","RingBell","GetTargetSlot","SetButtonState","Awake");
   UiInputAudit.DumpNested("Assembly-CSharp.dll","","CourierOrderButton","<PressAnimation>d__19","MoveNext");
   var rows=new List<object>();var prefabs=new HashSet<string>();
   foreach(var c in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
   {
    string type=c.GetIl2CppType().Name;if(Array.IndexOf(names,type)<0)continue;bool scene=c.gameObject.scene.IsValid();if(!scene&&!prefabs.Add(type+"/"+c.name))continue;
    var meshes=new List<object>();foreach(var m in c.GetComponentsInChildren<MeshFilter>(true)){var mesh=m.sharedMesh;if(mesh==null)continue;meshes.Add(new{path=PathOf(m.transform),mesh=mesh.name,center=V(mesh.bounds.center),size=V(mesh.bounds.size),position=V(m.transform.position),rotation=V(m.transform.eulerAngles),scale=V(m.transform.lossyScale),readable=mesh.isReadable});}
    var cols=new List<object>();foreach(var x in c.GetComponentsInChildren<Collider>(true)){var b=x.TryCast<BoxCollider>();cols.Add(new{path=PathOf(x.transform),kind=x.GetIl2CppType().Name,enabled=x.enabled,trigger=x.isTrigger,center=V(x.bounds.center),size=V(x.bounds.size),localCenter=b==null?null:V(b.center),localSize=b==null?null:V(b.size)});}
    var i=c.TryCast<IInteractable>();bool can=false;if(scene&&i!=null)can=i.CanInteract();object details=null;
    var shutter=c.TryCast<ShopShutterController>();if(shutter!=null)details=new{renderer=shutter.buttonRenderer==null?null:PathOf(shutter.buttonRenderer.transform),rest=V(shutter.buttonOriginalLocalPosition),depth=shutter.buttonPressDistance,open=scene&&shutter.IsShopOpen(),locked=shutter.interactionLocked};
    var courier=c.TryCast<CourierOrderButton>();if(courier!=null)details=new{renderer=courier.buttonRenderer==null?null:PathOf(courier.buttonRenderer.transform),rest=V(courier.originalLocalPosition),depth=courier.pressDepth,kind=courier.ButtonType.ToString(),active=courier.IsActive};
    var incinerator=c.TryCast<IncineratorButton>();if(incinerator!=null)details=new{renderer=incinerator.buttonRenderer==null?null:PathOf(incinerator.buttonRenderer.transform),off=V(incinerator.offPosition),on=V(incinerator.onPosition)};
    var washer=c.TryCast<WashingMachineButton>();if(washer!=null)details=new{renderer=washer.buttonRenderer==null?null:PathOf(washer.buttonRenderer.transform),off=V(washer.offRotation),on=V(washer.onRotation)};
    var light=c.TryCast<ProjectOne.Gameplay.LightSwitch>();if(light!=null)details=new{lever=light.switchTransform==null?null:PathOf(light.switchTransform),on=light.onAngleX,off=light.offAngleX,state=light.isLightsOn};
    var bell=c.TryCast<RegisterBell>();if(bell!=null)details=new{cap=bell.bellButton==null?null:PathOf(bell.bellButton),rest=V(bell.originalButtonPosition),depth=bell.pressDepth};
    rows.Add(new{type,name=c.name,path=PathOf(c.transform),scene,active=c.gameObject.activeInHierarchy,can,position=V(c.transform.position),rotation=V(c.transform.eulerAngles),scale=V(c.transform.lossyScale),meshes,colliders=cols,details});
   }
   File.WriteAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"world-controls.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Plugin.Line("WORLD_PRESS_SURVEY_COMPLETE controls="+rows.Count);
  }catch(Exception e){Plugin.Line("WORLD_PRESS_SURVEY_FAILED "+e);}
 }
}
