using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class InteractionAudit
{
 private static int phase;private static float next=24,before;private static OvenHandle oven;private static MachineInteractionBase machine;private static Vector3 contact,start;private static bool useLeft;
 private static SpyMiniGameManager spy;private static ForgeryStation desk;private static ForgerySignatureUIManager signature;private static RepairFocusMinigame repair;
 private static Vector2 ScreenPoint(UnityEngine.UIElements.VisualElement element,float x,float y,UnityEngine.UIElements.VisualElement root)
 {var b=element.worldBound;return new Vector2((b.x+b.width*x)/root.worldBound.width*Screen.width,(b.y+b.height*y)/root.worldBound.height*Screen.height);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void Check(bool value,string s){if(!value)throw new Exception(s);}
 private static string Hand(Vector3 world,bool left)
 {StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var z,out var q);var p=Quaternion.Inverse(q)*(world-z);return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");}
 internal static void Tick(){if(Environment.GetEnvironmentVariable("IKAG_XR_INTERACTION_AUDIT")!="1"||!StereoTrial.GameplayActive||phase==99||Time.realtimeSinceStartup<next)return;
 try{float delay=.5f;switch(phase){
 case 0:
  Plugin.Line("HANDLE_ASSETS press="+Resources.FindObjectsOfTypeAll<PressHandle>().Length+" slicer="+Resources.FindObjectsOfTypeAll<SlicerHandle>().Length+" sauce="+Resources.FindObjectsOfTypeAll<SauceHandle>().Length+" oven2="+Resources.FindObjectsOfTypeAll<OvenLevel2Handle>().Length);
  oven=UnityEngine.Object.FindObjectOfType<OvenHandle>();Check(oven!=null,"no oven");machine=oven.GetComponentInParent<MachineInteractionBase>();Check(machine!=null,"no owning machine");delay=2;break;
 case 1:contact=oven.GetComponentInChildren<Collider>().bounds.center;start=contact;before=oven.currentLocalRotationY;Send(Hand(contact,useLeft));break;
 case 2:Send(useLeft?"grip l 1":"grip r 1");break;
 case 3:Check(KitchenHands.Pose(useLeft,out _,out _),"grip did not attach");var axis=oven.slideAxis==NeedleRotationAxis.X?Vector3.right:oven.slideAxis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;var delta=(oven.slideClosedValue-oven.slideOpenValue)*(1-Mathf.InverseLerp(oven.minLocalRotationY,oven.maxLocalRotationY,before));Send(Hand(start+oven.transform.parent.TransformVector(axis*delta),useLeft));delay=1;break;
 case 4:Check(Mathf.Abs(oven.currentLocalRotationY-oven.maxLocalRotationY)<.01f,"oven did not follow: "+oven.currentLocalRotationY);Check(KitchenHands.Pose(useLeft,out var attached,out _),"attachment lost");Plugin.Line("KITCHEN_HAND_PASS left="+useLeft+" value="+oven.currentLocalRotationY+" pose="+attached);Send(useLeft?"grip l 0":"grip r 0");break;
 case 5:Check(!KitchenHands.Pose(useLeft,out _,out _),"release retained hand");if(!useLeft){useLeft=true;oven.targetLocalRotationY=oven.minLocalRotationY;phase=1;next=Time.realtimeSinceStartup+1;return;}machine.TriggerExit();delay=2;break;
 case 6:UiInputAudit.Dump("Assembly-CSharp.dll","","MachineInteractionBase","ForceExitImmediate","OnDisable","OnEnterMachineMode","OnExitMachineMode");UiInputAudit.Dump("Unity.InputSystem.dll","UnityEngine.InputSystem","InputAction","IsPressed","WasPerformedThisFrame");spy=SpyMiniGameManager.LocalInstance;Check(spy!=null,"spy missing");Plugin.Line("SPY_ENUM "+string.Join(",",Enum.GetNames(typeof(SpyMiniGameStage))));Check(spy.StartStandaloneStage((SpyMiniGameStage)1),"start spy failed");delay=3;break;
 case 7:Plugin.Line("SPY_RUNNING stage="+spy.activeStage?.GetType().Name+" tune="+spy.frequencyTuneGame.running+" lock="+spy.signalLockGame.IsRunning);before=spy.frequencyTuneGame.tunerPos;Send("stick l 1 0");delay=1;break;
 case 8:Plugin.Line("SPY_TUNE before="+before+" after="+spy.frequencyTuneGame.tunerPos);Check(spy.frequencyTuneGame.tunerPos>before,"tuner did not move");Send("stick l 0 0\nbtn b press 100");delay=2;break;
 case 9:Check(!SpyMotion.Active,"B did not abort");Check(spy.StartStandaloneStage((SpyMiniGameStage)0),"signal start failed");delay=3;break;
 case 10:spy.signalLockGame.phase=SignalLockMiniGame.Phase.Sweeping;spy.signalLockGame.sweepSpeedDeg=0;spy.signalLockGame.travelledDeg=spy.signalLockGame.leadDeg+spy.signalLockGame.bandDeg*.5f;before=spy.signalLockGame.locks;Send("trigger r pull 100");break;
 case 11:Check(spy.signalLockGame.locks>before,"RT signal did not lock");Plugin.Line("SPY_CONTROLS_PASS");Send("btn b press 100");delay=2;break;
 case 12:desk=UnityEngine.Object.FindObjectOfType<ForgeryStation>();desk.EnterMachineModeLocal();delay=2;break;
 case 13:var tray=desk.trays[0];desk.printMachine.RequestLoadBlank(tray.DocumentType,tray.blankSpawnAnchor.position,tray.blankSpawnAnchor.rotation);delay=4;break;
 case 14:var doc=desk.printMachine.GetDocumentInMachine();Check(doc!=null,"no blank document");ForgeryPrintUIManager.LocalInstance.Close();signature=ForgerySignatureUIManager.LocalInstance;signature.Open(desk,desk.printMachine,doc);delay=3;break;
 case 15:Check(signature.IsOpen,"signature UI closed");Send(MotionPointer.AimAt(ScreenPoint(signature.pad,.25f,.5f,signature.root)));break;
 case 16:Send("trigger r 1");break;
 case 17:Send(MotionPointer.AimAt(ScreenPoint(signature.pad,.5f,.5f,signature.root)));break;
 case 18:Send(MotionPointer.AimAt(ScreenPoint(signature.pad,.75f,.6f,signature.root)));break;
 case 19:Send("trigger r 0\nshot signature-drag");break;
 case 20:Check(signature.pad.HasInk&&signature.pad.playerStrokes.Count>0&&signature.pad.playerStrokes[0].Count>=3,"signature stroke failed");Plugin.Line("SIGNATURE_PASS points="+signature.pad.playerStrokes[0].Count);signature.Close();desk.TriggerExit();delay=2;break;
 case 21:var target=UnityEngine.Object.FindObjectOfType<SabotageTarget>();Check(target!=null,"no repair target");repair=RepairFocusMinigame.Begin(target);Check(repair!=null&&repair.rings.Count>0,"no repair rings");Send(MotionPointer.AimAtWorld(repair.rings[0].worldPosition));break;
 case 22:Check(repair.IsAimedAt(repair.rings[0],repair.viewerCamera.transform),"repair controller aim failed");Send(MotionPointer.AimAtWorld(repair.rings[0].worldPosition+Vector3.up*10));break;
 case 23:Check(!repair.IsAimedAt(repair.rings[0],repair.viewerCamera.transform),"repair aim stayed locked");repair.End();Plugin.Line("REPAIR_AIM_PASS");Plugin.Line("INTERACTION_AUDIT_COMPLETE");phase=99;return;
 }phase++;next=Time.realtimeSinceStartup+delay;
 }catch(Exception e){Plugin.Line("INTERACTION_FAILED phase="+phase+" "+e);phase=99;}}
}
