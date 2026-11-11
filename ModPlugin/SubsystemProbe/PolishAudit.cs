using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class PolishAudit
{
    private static int phase;private static float next=24;
    private static UIManager ui;private static bool oldActive,oldWaiting;
    private static Il2CppSystem.Collections.IEnumerator wait;
    internal static void Tick()
    {
        if(Environment.GetEnvironmentVariable("IKAG_XR_POLISH_AUDIT")!="1"||!StereoTrial.GameplayActive||phase>7||Time.realtimeSinceStartup<next)return;
        try {
            string command=null;
            if(phase==0){ui=UIManager.LocalInstance;if(ui==null)throw new Exception("no UI");oldActive=ui.isDialogActive;oldWaiting=ui.isWaiting;
                var owner=UnityEngine.Object.FindObjectOfType<CustomerDialogueSystem>();if(owner==null)throw new Exception("no dialogue owner");
                ui.isDialogActive=true;ui.isWaiting=true;ui.skipWaitingRequested=false;wait=owner.WaitForSpaceInput();
                if(!wait.MoveNext())throw new Exception("wait did not yield");command="btn a down";}
            else if(phase==1){if(!ui.skipWaitingRequested)throw new Exception("A did not set native Space flag");if(wait.MoveNext())throw new Exception("native conversation did not advance");
                Plugin.Line("POLISH_DIALOG_PASS native coroutine completed on A");ui.skipWaitingRequested=false;}
            else if(phase==2){if(ui.skipWaitingRequested)throw new Exception("held A repeated");command="btn a up";ui.isDialogActive=oldActive;ui.isWaiting=oldWaiting;}
            else if(phase==3){HUDManager.LocalInstance.ShowEscapeMenu();command="hands reset";}
            else if(phase==4)command="shot polish-laser";
            else if(phase==5){HUDManager.LocalInstance.HideEscapeMenu();}
            else if(phase==7)Plugin.Line("POLISH_AUDIT_COMPLETE");
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+.7f;
        }catch(Exception e){Plugin.Line("POLISH_FAILED "+e);if(ui!=null){ui.isDialogActive=oldActive;ui.isWaiting=oldWaiting;}phase=8;}
    }
}
