using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ImmersiveHudAudit
{
 internal static bool Complete=>stageDone;
 private static bool stageDone;
 private static int phase;private static float next=24,punch,deadline;private static Vector3 center;
 private static void Check(bool v,string s){if(!v)throw new Exception(s);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 internal static void Tick(){if(Environment.GetEnvironmentVariable("IKAG_XR_IMMERSIVE_HUD_AUDIT")!="1"||(phase<28&&!StereoTrial.GameplayActive)||Time.realtimeSinceStartup<next||phase==99)return;
 try{float delay=.7f;switch(phase){
 case 0:Check(ImmersiveHud.Enabled,"disabled on startup");Send("shot hud-clear");break;
 case 1:var hp=ControllerTransport.HeadPosition;Send(FormattableString.Invariant($"hand l grip pose {hp.x-.15f:R} {hp.y-.16f:R} {-hp.z-.42f:R} 90 0 0"));delay=1;break;
 case 2:Plugin.Line("WRIST_TRACE open="+ImmersiveHud.WristOpen+" position="+ImmersiveHud.WristCenter+" rotation="+ControllerTransport.Left.Rotation.eulerAngles);Check(ImmersiveHud.WristOpen,"back wrist did not open");Send("shot hud-wrist");break;
 case 3:punch=InteractionManager.LocalInstance.lastPunchTime;Send(MotionPointer.AimAtWorld(ImmersiveHud.WristRecallButton));break;
 case 4:Send("trigger r pull 100");break;
 case 5:Check(ImmersiveHud.DashboardVisible,"combined dashboard missing");Check(InteractionManager.LocalInstance.lastPunchTime==punch,"UI click punched");Send("shot hud-journal");break;
 case 6:center=ImmersiveHud.BoardCenter;Send("hand l grip rot 0 0 0\n"+MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle));break;
 case 7:Send("trigger r 1");break;
 case 8:Send(MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle+Vector3.up*.15f+Vector3.right*.25f));break;
 case 9:Send("trigger r 0");break;
 case 10:Plugin.Line("BOARD_DRAG_DELTA "+(ImmersiveHud.BoardCenter-center));Check((ImmersiveHud.BoardCenter-center).magnitude>.15f,"board did not drag");Send("shot hud-board-moved");break;
 case 11:Send(MotionPointer.AimAtWorld(ImmersiveHud.BoardClose));break;
 case 12:Send("trigger r pull 100");break;
 case 13:Check(!ImmersiveHud.BoardPinned,"board did not close");Send("hand l grip rot 90 0 0");delay=.5f;break;
 case 14:Send("shot hud-journal-orders");break;
 case 15:HUDManager.LocalInstance.ShowEscapeMenu();delay=1;break;
 // The VR menu suite owns controller acceptance for the moved radios.
 case 16:ImmersiveHud.DiagnosticToggle();break;
 case 17:break;
 case 18:Check(!ImmersiveHud.Enabled,"pause classic toggle failed");Send("shot hud-pause-classic");break;
 case 19:ImmersiveHud.DiagnosticToggle();break;
 case 20:Check(ImmersiveHud.Enabled,"pause immersive toggle failed");HUDManager.LocalInstance.HideEscapeMenu();Send("hand l grip pose -.2 1.3 -.3 0 0 0");break;
 case 21:Check(!ImmersiveHud.WristOpen,"wrist failed to close");center=ImmersiveHud.BoardCenter;Send("head move .1 0 -.1");break;
 case 22:Check((ImmersiveHud.BoardCenter-center).sqrMagnitude<.000001f,"board followed head");Send("head move -.1 0 .1\nhand l valid off");break;
 case 23:Check(!ImmersiveHud.UsingImmersive,"tracking loss did not restore classic HUD");Send("shot hud-tracking-fallback\nhand l valid on");break;
 case 24:Check(ImmersiveHud.UsingImmersive,"tracking return failed");var h=ControllerTransport.HeadPosition;Send(FormattableString.Invariant($"hand l grip pose {h.x-.15f:R} {h.y-.16f:R} {-h.z-.42f:R} -90 0 0"));delay=1;break;
 case 25:Check(!MotionInput.WheelOpen&&!ImmersiveHud.WristOpen,"removed palm gesture opened UI");Send("hand l grip rot 0 0 0");break;
 case 26:Check(!MotionInput.WheelOpen,"removed palm gesture opened wheel");stageDone=true;if(Environment.GetEnvironmentVariable("IKAG_XR_HUD_KEYBOARD_REGRESSION")!="1"){Plugin.Line("IMMERSIVE_HUD_AUDIT_COMPLETE");phase=99;return;}break;
 case 27:if(!KeyboardAudit.Complete)return;deadline=Time.realtimeSinceStartup+15;HUDManager.LocalInstance.ExitToMainMenu();delay=3;break;
 case 28:if(!StereoTrial.MenuActive){Check(Time.realtimeSinceStartup<deadline,"main menu did not start");return;}Check(ImmersiveHud.BoundGroups==0,"HUD draw groups survived scene exit");Send("shot hud-menu-return");MotionMenuAudit.AfterGameplay();Plugin.Line("IMMERSIVE_HUD_AUDIT_COMPLETE menus, keyboards, tracking fallback, removed palm gesture, drag and click ownership");phase=99;return;

 }phase++;next=Time.realtimeSinceStartup+delay;
 }catch(Exception e){Plugin.Line("IMMERSIVE_HUD_AUDIT_FAILED phase="+phase+" "+e);Send("trigger r 0");phase=99;}}
}
