using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionTransportAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_TRANSPORT_AUDIT")=="1";
    private static int phase; private static float next;
    private static readonly string[] commands={"btn x down\nbtn a down", "btn x up\nbtn a up\nbtn y down\nbtn b down", "btn y up\nbtn b up\ntrigger l 1\ntrigger r 1", "trigger l 0\ntrigger r 0\ngrip l 1\ngrip r 1", "grip l 0\ngrip r 0\nclick l down\nclick r down", "click l up\nclick r up\nstick l 0.5 -0.7\nstick r -0.6 0.8", "stick l 0 0\nstick r 0 0\nhand r grip pos 0.4 1.4 0.5\nhand r aim rot 25 -30 10", "hands reset"};
    internal static void Tick()
    {
        if(!enabled || phase>commands.Length || !StereoTrial.GameplayActive || Time.realtimeSinceStartup<next) return;
        ControllerTransport.Sample(); var l=ControllerTransport.Left;var r=ControllerTransport.Right;
        bool pass=l.Tracked&&r.Tracked;
        if(phase==1) pass&=l.Primary&&r.Primary;
        if(phase==2) pass&=!l.Primary&&!r.Primary&&l.Secondary&&r.Secondary;
        if(phase==3) pass&=l.Trigger&&r.Trigger&&!l.Secondary&&!r.Secondary;
        if(phase==4) pass&=l.Grip&&r.Grip&&!l.Trigger&&!r.Trigger;
        if(phase==5) pass&=l.Click&&r.Click&&!l.Grip&&!r.Grip;
        if(phase==6) pass&=Math.Abs(l.Stick.x-.5f)<.001f&&Math.Abs(r.Stick.y-.8f)<.001f;
        Plugin.Line($"MOTION_TRANSPORT_CONTROL phase={phase} PASS={pass} left={l.Tracked}/{l.Primary}/{l.Secondary}/{l.Trigger}/{l.Grip}/{l.Click}/{l.Stick:R} right={r.Tracked}/{r.Primary}/{r.Secondary}/{r.Trigger}/{r.Grip}/{r.Click}/{r.Stick:R} grip={r.Position:R}/{r.Rotation:R} aim={r.AimPosition:R}/{r.AimRotation:R}");
        if(phase<commands.Length) File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),commands[phase]+"\n");
        else Plugin.Line("MOTION_TRANSPORT_CONTROLS_COMPLETE");
        ++phase;next=Time.realtimeSinceStartup+1;
    }
}
