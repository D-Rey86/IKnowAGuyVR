using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class DayTransitionAudit
{
    private static int phase,day,cycle;private static float next=25,started;private static BedInteraction bed;
    private static void Snapshot(string label)
    {
        var list=new List<object>();foreach(var t in Resources.FindObjectsOfTypeAll<RenderTexture>())list.Add(new{id=t.GetInstanceID(),name=t.name,width=t.width,height=t.height,depth=t.volumeDepth,created=t.IsCreated()});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),label+"-textures.json"),JsonSerializer.Serialize(list));
    }
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    internal static void Tick()
    {
        if(Environment.GetEnvironmentVariable("IKAG_XR_RUNTIME_ISSUE_AUDIT")!="day"||phase==99||(!StereoTrial.GameplayActive&&phase<3)||Time.realtimeSinceStartup<next)return;
        try{switch(phase)
        {
            case 0:
                foreach(var b in UnityEngine.Object.FindObjectsOfType<BedInteraction>())if(b.IsSpawned&&b.gameObject.activeInHierarchy){bed=b;break;}
                if(bed==null)throw new Exception("No spawned native bed");
                Plugin.Line($"DAY_BED name={bed.name} canInteract={bed.CanInteract()} tutorialBlocked={BedInteraction.IsSleepBlockedByTutorial()} duration={bed.blackScreenDuration}");
                UiInputAudit.Dump("UnityEngine.CoreModule.dll","UnityEngine","RenderTexture","Release","DiscardContents","Create","IsCreated");
                break;
            case 1:
                Snapshot("day-before-"+cycle);
                day=TimeManager.Instance.GetCurrentDay();TimeManager.Instance.SetTime(22);
                if(!bed.CanInteract())throw new Exception("Native bed preconditions not satisfied");
                Plugin.Line($"DAY_SLEEP_START cycle={cycle} day={day} time={TimeManager.Instance.GetCurrentTime()} camera={InteractionManager.LocalInstance.interactionCamera.GetInstanceID()}");
                started=Time.realtimeSinceStartup;bed.OnInteract();next=Time.realtimeSinceStartup+1;phase=2;return;
            case 2:
                var fade=ScreenFadeController._instance;float alpha=fade!=null&&fade.fadeImage!=null?fade.fadeImage.color.a:-1;
                Plugin.Line($"DAY_SLEEP_PROGRESS cycle={cycle} day={TimeManager.Instance.GetCurrentDay()} executing={bed.isExecutingSleep} localSleeping={bed.isLocalPlayerSleeping} fade={alpha} time={TimeManager.Instance.GetCurrentTime()}");
                if(bed.isExecutingSleep||bed.isLocalPlayerSleeping){if(Time.realtimeSinceStartup-started>60)throw new Exception("Sleep did not finish within 60s");next=Time.realtimeSinceStartup+2;return;}
                if(TimeManager.Instance.GetCurrentDay()<=day)throw new Exception("Sleep ended without advancing the day");
                if(alpha>.001f)throw new Exception("Fade remained opaque after waking");
                Plugin.Line($"DAY_TRANSITION_PASS cycle={cycle} before={day} after={TimeManager.Instance.GetCurrentDay()} frame={Time.frameCount} textures={Resources.FindObjectsOfTypeAll<RenderTexture>().Length}");Send("shot day-after-"+cycle);
                Snapshot("day-after-"+cycle);
                if(++cycle<3){phase=1;next=Time.realtimeSinceStartup+8;return;}
                Plugin.Line("DAY_TRANSITION_AUDIT_COMPLETE");phase=3;next=Time.realtimeSinceStartup+8;return;
            case 3:HUDManager.LocalInstance.ExitToMainMenu();next=Time.realtimeSinceStartup+5;break;
            case 4:
                if(!StereoTrial.MenuActive)return;
                Snapshot("returned-menu");Send("shot day-returned-menu");
                var menu=UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();
                menu.singleplayerController.HostSingleplayerSilently("IKAG day re-entry audit",0);next=Time.realtimeSinceStartup+15;break;
            case 5:
                if(!StereoTrial.GameplayActive)return;
                Snapshot("reentered-world");Send("shot day-reentered-world");Plugin.Line("DAY_REENTRY_PASS");phase=99;return;
        }phase++;next=Time.realtimeSinceStartup+1;
        }catch(Exception e){Plugin.Line("DAY_TRANSITION_AUDIT_FAILED "+e);phase=99;}
    }
}
