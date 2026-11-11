using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ForgeryAudit
{
 private static int phase,index;private static float next=24;
 private static ForgeryStation desk;private static ForgeryPrintUIManager ui;private static Vector3 point;
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 internal static void Tick(){if(Environment.GetEnvironmentVariable("IKAG_XR_FORGERY_AUDIT")!="1"||!StereoTrial.GameplayActive||phase==99||Time.realtimeSinceStartup<next)return;
 try{float delay=.5f;switch(phase){
 case 0:desk=UnityEngine.Object.FindObjectOfType<ForgeryStation>();Check(desk!=null,"desk missing");desk.EnterMachineModeLocal();delay=2;break;
 case 1:
  if(index>=desk.trays.Count){phase=4;return;}
  var tray=desk.trays[index];var c=tray.GetComponentInChildren<Collider>();Check(c!=null,"tray collider missing");point=c.bounds.center;
  StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var z,out var q);var p=Quaternion.Inverse(q)*(point+Vector3.up*.35f-z);
  Send(FormattableString.Invariant($"hand r grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0"));break;
 case 2:Send(MotionPointer.AimAtWorld(point));break;
 case 3:Check(desk.hoveredTray!=null&&desk.hoveredTray.Pointer==desk.trays[index].Pointer,"tray hover mismatch "+index+" got="+desk.hoveredTray?.name);Plugin.Line("FORGERY_TRAY_PASS "+index);++index;phase=1;next=Time.realtimeSinceStartup+delay;return;
 case 4:Send("trigger r pull 100");delay=4;break;
 case 5:ui=ForgeryPrintUIManager.LocalInstance;Plugin.Line("FORGERY_PRINT_STATE "+desk.printMachine.State+" open="+ui.IsOpen);if(!ui.IsOpen){ui.Open(desk,desk.printMachine);delay=1;}break;
 case 6:Send(MotionPointer.AimAt(new Vector2(ui.nameButton.worldBound.center.x/ui.root.worldBound.width*Screen.width,ui.nameButton.worldBound.center.y/ui.root.worldBound.height*Screen.height)));phase=20;next=Time.realtimeSinceStartup+.5f;return;
 case 20:Send("trigger r pull 100");phase=7;next=Time.realtimeSinceStartup+1;return;
 case 7:Check(ComputerKeyboard.IsOpen,"keyboard not opened");Check(ComputerKeyboard.Target.Pointer==ui.nameInput.textInputBase.textElement.Pointer,"wrong editor");Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("clear")));break;
 case 8:Send("trigger r pull 100");break;
 case 9:Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("q")));break;
 case 10:Send("trigger r pull 100");break;
 case 11:Check(ui.nameInput.value=="q","native field not edited: "+ui.nameInput.value);Send("shot forgery-keyboard-dot");Plugin.Line("FORGERY_KEYBOARD_PASS "+ui.nameInput.value);break;
 case 12:Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("close")));break;
 case 13:Send("trigger r pull 100");break;
 case 14:Check(!ComputerKeyboard.IsOpen,"keyboard close failed");ui.CloseNamePopup();Send(MotionPointer.AimAt(new Vector2(ui.surnameButton.worldBound.center.x/ui.root.worldBound.width*Screen.width,ui.surnameButton.worldBound.center.y/ui.root.worldBound.height*Screen.height)));phase=21;next=Time.realtimeSinceStartup+.5f;return;
 case 21:Send("trigger r pull 100");break;
 case 22:Check(ComputerKeyboard.IsOpen,"surname keyboard missing");Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("w")));break;
 case 23:Send("trigger r pull 100");break;
 case 24:Check(ui.nameInput.value.Contains("w"),"surname edit failed");Send("shot forgery-surname-dot");Plugin.Line("FORGERY_SURNAME_PASS");break;
 case 25:ui.Close();desk.TriggerExit();phase=15;next=Time.realtimeSinceStartup+2;return;
 case 15:Check(!ForgeryMotion.Active,"desk context retained");Plugin.Line("FORGERY_AUDIT_COMPLETE");phase=99;return;
 }phase++;next=Time.realtimeSinceStartup+delay;
 }catch(Exception e){Plugin.Line("FORGERY_FAILED phase="+phase+" "+e);phase=99;}}
}
