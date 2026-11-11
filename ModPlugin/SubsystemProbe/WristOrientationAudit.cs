using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class WristOrientationAudit
{
 private static int phase;private static float next=24,punch;
 private static bool Baseline=>Environment.GetEnvironmentVariable("IKAG_XR_WRIST_BASELINE")=="1";
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static string Pose(bool flipped){var p=ControllerTransport.HeadPosition;return FormattableString.Invariant($"hand l grip pose {p.x-.15f:R} {p.y-.16f:R} {-p.z-.42f:R} 90 {(flipped?180:0)} 0");}
 internal static void Tick(){if(Environment.GetEnvironmentVariable("IKAG_XR_WRIST_ORIENTATION_AUDIT")!="1"||!StereoTrial.GameplayActive||phase==99||Time.realtimeSinceStartup<next)return;
 try{switch(phase){
 case 0:Send(Pose(true));break;
 case 1:Check(ImmersiveHud.WristOpen,"natural wrist did not open");Plugin.Line("WRIST_ORIENTATION natural upDot="+ImmersiveHud.WristUprightDot);Send("shot wrist-natural");if(Baseline){Check(ImmersiveHud.WristUprightDot<-.9f,"baseline did not reproduce upside-down view");Plugin.Line("WRIST_BASELINE_REPRODUCED");phase=99;return;}Check(ImmersiveHud.WristUprightDot>.7f,"natural wrist upside down");break;
 case 2:punch=InteractionManager.LocalInstance.lastPunchTime;Send(MotionPointer.AimAtWorld(ImmersiveHud.WristRecallButton));break;
 case 3:Send("trigger r pull 100");break;
 case 4:Check(ImmersiveHud.DashboardVisible,"flipped-posture combined dashboard missing");Check(InteractionManager.LocalInstance.lastPunchTime==punch,"UI click punched");Check(ImmersiveHud.WristUprightDot>.7f,"journal upside down");Send("shot wrist-natural-journal");break;
 case 5:Send("head rot 0 0 25");break;
 case 6:Check(ImmersiveHud.WristUprightDot>.7f,"head roll made journal upside down");Send("shot wrist-head-roll");break;
 case 7:Send("head rot 0 0 0\n"+Pose(false));break;
 case 8:Check(ImmersiveHud.WristOpen&&ImmersiveHud.WristUprightDot>.7f,"preceding wrist posture regressed");Send("shot wrist-preceding-posture");break;
 case 9:Send(MotionPointer.AimAtWorld(ImmersiveHud.WristRecallButton));break;
 case 10:Send("trigger r pull 100");break;
 case 11:Check(ImmersiveHud.DashboardVisible,"combined dashboard missing in preceding posture");Check(InteractionManager.LocalInstance.lastPunchTime==punch,"UI click punched");Plugin.Line("WRIST_ORIENTATION_AUDIT_COMPLETE both postures, head roll and laser controls");phase=99;return;
 }phase++;next=Time.realtimeSinceStartup+.7f;}catch(Exception e){Plugin.Line("WRIST_ORIENTATION_AUDIT_FAILED phase="+phase+" "+e);Send("trigger r 0");phase=99;}}
}
