using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionAcceptanceAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_ACCEPTANCE")=="1";
    private static int phase;private static float next;
    private static Vector3 start;private static float yaw;
    private static readonly string[] commands={"head rot 0 0 0\nstick l 0 0.7", "stick l 0 0\nstick r 0.7 0", "stick r 0 0\nhand r grip pose 0.2 1.3 -0.35 10 15 20\nhand r aimtrim -10 25", "hand r grip pose 0.25 1.45 -0.3 -10 -20 30", "hands reset\nclick l press 80", "click l press 80", "click l press 80", "head rot 0 -65 0", "shot motion-body-final", "head rot 0 0 0"};
    internal static void Tick()
    {
        if(!enabled||phase>29||Time.realtimeSinceStartup<next) return;
        if(!StereoTrial.GameplayActive) {if(phase==0)MotionPointer.Trace();next=Time.realtimeSinceStartup+2;return;}
        var interaction=InteractionManager.LocalInstance;
        if(interaction==null||interaction.movementController==null)return;
        var player=interaction.movementController;
        if(phase==0) {start=player.transform.position;yaw=player.targetHorizontalRotation;}
        var l=ControllerTransport.Left;var r=ControllerTransport.Right;var hud=HUDManager.LocalInstance;
        Plugin.Line($"MOTION_ACCEPT phase={phase} position={player.transform.position:R} moved={(player.transform.position-start).magnitude:R} yawDelta={player.targetHorizontalRotation-yaw:R} move={player.moveInput:R} look={player.lookInput:R} cursor={Cursor.lockState} held={interaction.heldItems.Count} rawR={r.Position:R}/{r.Rotation:R} click={l.Click} menu={(hud!=null&&hud.IsEscapeMenuOpen)} quickPause={(hud!=null&&hud.quickPaused)} timeScale={Time.timeScale:R}");
        MotionPointer.Trace();MotionBody.Trace();MotionInput.Trace();
        if(r.Tracked&&StereoTrial.TryTrackingPose(r.AimPosition,r.AimRotation,out var rp,out var rq))
            Plugin.Line($"INTERACTION_RAY_CONTROL phase={phase} originError={(interaction.interactionRay.origin-rp).magnitude:R} directionError={(interaction.interactionRay.direction-rq*Vector3.forward).magnitude:R}");
        if(phase<commands.Length) File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),commands[phase]+"\n");
        else
        {
            string command=null;
            var inv=InventoryManager.LocalInstance;
            switch(phase)
            {
                case 10:
                    var center=hud.btnContinue.worldBound.center;
                    command=MotionPointer.AimAt(center);
                    Plugin.Line($"LASER_BUTTON_CONTROL uiTarget={center} button={hud.btnContinue.worldBound} command={command}");break;
                case 11:command="trigger r 1";break;
                case 12:command="trigger r 0";break;
                case 13:
                    Plugin.Line("LASER_CONTINUE_RESULT menuClosed="+!hud.IsEscapeMenuOpen);
                    if(hud.IsEscapeMenuOpen)hud.HideEscapeMenu();
                    inv.SetSelectedSlot(1);command="hands reset";break;
                case 14:command="hand r grip pose 0.15 1.45 -0.35 20 15 -10";break;
                case 15:command="trigger l 1";break;
                case 16:command="trigger l 0\nstick r 0 0.8";break;
                case 17:command="stick r 0 0\ntrigger l 1";break;
                case 18:command="trigger l 0\ngrip r 1";break;
                case 19:command="grip r 0\ngrip l 1";break;
                case 20:command="grip l 0\nhead rot 0 -65 0";break;
                case 21:command="shot motion-held-final";break;
                case 22:command="hands reset\nhead rot 0 0 0";break;
                case 23:inv.SetSelectedSlot(2);command="hand l grip pose -0.15 1.45 -0.35 -66.8014 0 21.5\nhead rot 23.1986 -21.5 0";break;
                case 24:command="stick r 1 0";break;
                case 25:command="shot motion-palm-no-wheel\nstick r -1 0";break;
                case 26:command="hands reset\nhead rot 0 0 0\nstick r 0 0";break;
                case 27:command="head pos 0.1 1.7 -0.05\nhead rot 15 -15 10";break;
                case 28:command="shot motion-body-lean\nhead pos 0 1.6 0\nhead rot 0 0 0";break;
                case 29:inv.SetSelectedSlot(2);Plugin.Line("MOTION_ACCEPTANCE_COMPLETE");break;
            }
            if(inv!=null)
            {
                var selected=inv.GetItemInSlot(inv.currentSelectedSlot);var pc=selected==null?null:selected.GetComponent<PhotoCamera>();
                Plugin.Line($"ITEM_CONTROL phase={phase} slot={inv.currentSelectedSlot} item={(selected==null?"none":selected.name)} position={(selected==null?Vector3.zero:selected.transform.position):R} rotation={(selected==null?Quaternion.identity:selected.transform.rotation):R} photo={(pc!=null&&pc.isInPhotoMode)} zoom={(pc==null?0:pc.targetZoom):R}");
            }
            if(command!=null) File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
        }
        ++phase;next=Time.realtimeSinceStartup+(phase==6||phase==7?.18f:1.2f);
    }
}
