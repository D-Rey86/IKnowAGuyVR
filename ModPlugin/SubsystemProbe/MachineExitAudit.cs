using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class MachineExitAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MACHINE_EXIT_AUDIT")=="1";
    private static int phase;
    private static float next=22;
    private static KitchenOrderDisplay kitchen;
    private static DarkWebComputer dark;
    internal static void Tick()
    {
        if(!enabled||phase>6||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;
            if(phase==0)
            {
                kitchen=UnityEngine.Object.FindObjectOfType<KitchenOrderDisplay>();dark=UnityEngine.Object.FindObjectOfType<DarkWebComputer>();
                if(kitchen==null||dark==null)throw new InvalidOperationException("Both existing computers required");
                kitchen.OnInteract();
            }
            if(phase==1||phase==4)
            {
                var expected=phase==1?(MachineInteractionBase)kitchen:dark;
                var active=MachineInteractionBase.CurrentActiveMachine;
                if(!MachineInteractionBase.IsLocalPlayerInMachineMode||active==null||active.Pointer!=expected.Pointer||MachineInteractionBase.isLocalMachineTransitionInProgress)throw new InvalidOperationException("Native computer entry incomplete");
                command="btn b down\n";
            }
            if(phase==2||phase==5)
            {
                if(MachineInteractionBase.IsLocalPlayerInMachineMode||MachineInteractionBase.CurrentActiveMachine!=null||MachineInteractionBase.isLocalMachineTransitionInProgress)throw new InvalidOperationException("B did not complete native computer exit");
                var player=InteractionManager.LocalInstance.movementController;
                if(!player.canLook||Cursor.lockState!=CursorLockMode.Locked)throw new InvalidOperationException("Native player/cursor ownership not restored");
                Plugin.Line($"MACHINE_B_EXIT_PASS computer={(phase==2?"regular":"dark-web")} canLook={player.canLook} cursor={Cursor.lockState}");
                command="btn b up\n";
            }
            if(phase==3)dark.OnInteract();
            if(phase==6)Plugin.Line("MACHINE_B_EXIT_AUDIT_COMPLETE");
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command);
            ++phase;next=Time.realtimeSinceStartup+1.5f;
        }
        catch(Exception e){Plugin.Line("MACHINE_B_EXIT_AUDIT_FAILED phase="+phase+" "+e);phase=7;}
    }
}
