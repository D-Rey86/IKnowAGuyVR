using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ContextHudAudit
{
 private static bool installed,progress;
 private static void Progress(){if(progress&&InteractionPromptUITK.LocalInstance!=null)InteractionPromptUITK.LocalInstance.ShowProgressBar(.45f);}
 private static int phase,continued;private static float next=24,punch;private static GameObject fixture;private static Vector3 anchor,popup;
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 internal static void Tick(){if(Environment.GetEnvironmentVariable("IKAG_XR_CONTEXT_HUD_AUDIT")!="1"||!StereoTrial.GameplayActive||phase==99||Time.realtimeSinceStartup<next)return;
 try{if(!installed){new Harmony("ikag.context-progress-fixture").Patch(typeof(InteractionManager).GetMethod("HandleInteractionPrompts"),postfix:new HarmonyMethod(typeof(ContextHudAudit).GetMethod(nameof(Progress),BindingFlags.NonPublic|BindingFlags.Static)));installed=true;}var h=HUDManager.LocalInstance;float delay=.7f;switch(phase){
 case 0:var source=Resources.FindObjectsOfTypeAll<PressHandle>()[0].GetComponentInParent<MachineInteractionBase>(true);fixture=UnityEngine.Object.Instantiate(source.gameObject);var camera=InteractionManager.LocalInstance.interactionCamera;fixture.transform.position=camera.transform.position+camera.transform.forward*1.25f-Vector3.up*.6f;fixture.SetActive(true);fixture.GetComponent<NetworkObject>().Spawn();break;
 case 1:Physics.SyncTransforms();Send(MotionPointer.AimAtWorld(fixture.GetComponent<Collider>().bounds.center));break;
 case 2:ContextHud.Trace("object");Check(ContextHud.PromptVisible,"object prompt not visible");anchor=ContextHud.Anchor;progress=true;Send("shot context-object");break;
 case 3:Check(InteractionPromptUITK.LocalInstance.showingProgressBar,"native hold bar not visible");fixture.transform.position+=Vector3.up*.08f;Physics.SyncTransforms();Send("shot context-hold-progress\n"+MotionPointer.AimAtWorld(fixture.GetComponent<Collider>().bounds.center));break;
 case 4:progress=false;ContextHud.Trace("moved");Plugin.Line("CONTEXT_ANCHOR_DELTA "+(ContextHud.Anchor-anchor));Check((ContextHud.Anchor-anchor).magnitude>.04f,"prompt did not follow object");h.ShowWarningNotification("WARNING: diagnostic stock issue","warning");h.ShowPersistentDangerNotification("POLICE RISK: diagnostic warning");h.ShowNotificationDirect("Diagnostic stock notice");break;
 case 5:ContextHud.Trace("alerts");Send("shot context-alerts");break;
 case 6:Send("trigger r 1");break;
 case 7:punch=InteractionManager.LocalInstance.lastPunchTime;h.ShowTutorialPopup("VR TUTORIAL","Read this native tutorial, then choose Continue with RT.",new Il2CppSystem.Collections.Generic.List<Sprite>(),(Action)(()=>++continued));break;
 case 8:ContextHud.Trace("tutorial");Check(ContextHud.ModalActive,"tutorial not active");popup=ContextHud.PopupCenter;Send(MotionPointer.AimAtWorld(ContextHud.ContinuePoint));break;
 case 9:Check(continued==0,"held trigger dismissed tutorial");Send("trigger r 0\nshot context-tutorial");break;
 case 10:Send("head move .12 0 -.08");break;
 case 11:Check((ContextHud.PopupCenter-popup).sqrMagnitude<.000001f,"tutorial followed head");Send(MotionPointer.AimAtWorld(ContextHud.ContinuePoint));break;
 case 12:Send("trigger r pull 100");break;
 case 13:Check(continued==1,"RT did not invoke native continue exactly once");Check(InteractionManager.LocalInstance.lastPunchTime==punch,"tutorial click punched");h.HideTutorialPopup();Send("head move -.12 0 .08");delay=2;break;
 case 14:ContextHud.Trace("notice");Send("shot context-after-tutorial");break;
 case 15:Send(MotionPointer.AimAtWorld(fixture.transform.position+Vector3.up*10));break;
 case 16:Check(!ContextHud.PromptVisible,"object prompt remained on missed target");UnityEngine.Object.Destroy(fixture);Plugin.Line("CONTEXT_HUD_AUDIT_COMPLETE moving target, prompt disappearance, tutorial placement, trigger gate and native callback");phase=99;return;
 }phase++;next=Time.realtimeSinceStartup+delay;}catch(Exception e){ContextHud.Trace("failed");Plugin.Line("CONTEXT_HUD_AUDIT_FAILED phase="+phase+" "+e);Send("trigger r 0");phase=99;}}
}
