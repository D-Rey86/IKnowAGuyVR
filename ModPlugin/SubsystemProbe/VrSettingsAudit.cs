using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using UI.MainMenu;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class VrSettingsAudit
{
    private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_SETTINGS_AUDIT");
    private static bool installed;private static SettingsPageController controller;private static int phase;private static float next=12;
    private static int scaleIndex;private static readonly int[] scales={50,65,80,101,125,150,200,100};
    private static void Initialized(SettingsPageController __instance){controller=__instance;}
    internal static void Dump(VisualElement root)
    {
        var lines=new List<string>();Walk(root,0,lines);File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"settings-tree.txt"),lines);
    }
    private static void Walk(VisualElement e,int depth,List<string> lines)
    {
        var classes=new List<string>();var native=e.GetClasses().TryCast<Il2CppSystem.Collections.Generic.List<string>>();if(native!=null)for(int j=0;j<native.Count;++j)classes.Add(native[j]);
        var s=e.resolvedStyle;var text=e.TryCast<TextElement>()?.text;
        lines.Add(new string(' ',depth)+e.GetIl2CppType().Name+" #"+e.name+" ."+string.Join(".",classes)+" text="+text+" rect="+e.worldBound+" display="+s.display+" color="+s.color+" bg="+s.backgroundColor+" font="+s.fontSize);
        for(int i=0;i<e.childCount;++i)Walk(e.ElementAt(i),depth+1,lines);
    }
    internal static void Tick()
    {
        if(mode==null||phase==99)return;
        if(mode=="resources"){VrResolutionAudit.Tick();return;}
        if(mode=="gameplay"){VrGameplayAudit.Tick();return;}
        if(mode=="interaction"){VrMenuInteractionAudit.Tick();return;}
        if(!installed){installed=true;new Harmony("ikag.settings-inspection").Patch(typeof(SettingsPageController).GetMethod("Initialize"),postfix:new HarmonyMethod(typeof(VrSettingsAudit).GetMethod(nameof(Initialized),BindingFlags.Static|BindingFlags.NonPublic)));}
        if(!StereoTrial.MenuActive||Time.realtimeSinceStartup<next)return;
        if(controller==null){Plugin.Line("VR_SETTINGS_FIND_MENU");controller=UnityEngine.Object.FindObjectOfType<MainMenuManager>()?.settingsController;Plugin.Line("VR_SETTINGS_CONTROLLER "+(controller==null?"null":controller.Pointer.ToString()));}
        if(controller==null)return;
        try{Plugin.Line("VR_SETTINGS_PHASE_BEGIN "+phase);switch(phase)
        {
            case 0:var menu=UnityEngine.Object.FindObjectOfType<MainMenuManager>();menu.OnSettingsButtonClicked();break;
            case 1:if(mode=="menu")VrSettingsMenu.Find(controller).Show();else controller.ShowTab(SettingsTab.Visual);break;
            case 2:Dump(controller.settingsPage);Send("shot settings-native");if(mode!="menu"){Plugin.Line("VR_SETTINGS_INSPECTION_COMPLETE");phase=99;return;}break;
            case 3:VrSettings.Resolution.Value=scales[scaleIndex];break;
            case 4:ResolutionCheck(scales[scaleIndex]);if(++scaleIndex<scales.Length){phase=3;next=Time.realtimeSinceStartup+1;return;}phase=6;next=Time.realtimeSinceStartup+1;return;
            case 5:ResolutionCheck(150);Send("shot settings-supersampled");VrSettings.Resolution.Value=100;break;
            case 6:ResolutionCheck(100);var m=UnityEngine.Object.FindObjectOfType<MainMenuManager>();m.HandleBackNavigation();break;
            case 7:UnityEngine.Object.FindObjectOfType<MainMenuManager>().OnSettingsButtonClicked();break;
            case 8:Plugin.Line("VR_SETTINGS_REOPEN nativeVisual="+controller.visualTab.resolvedStyle.display+" vr="+VrSettingsMenu.Find(controller).Root.resolvedStyle.display);Send("shot settings-reopened");Plugin.Line("VR_SETTINGS_MENU_AUDIT_COMPLETE");phase=99;return;
        }Plugin.Line("VR_SETTINGS_PHASE_END "+phase);phase++;next=Time.realtimeSinceStartup+1;
        }catch(Exception e){Plugin.Line("VR_SETTINGS_AUDIT_FAILED "+e);phase=99;}
    }
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void ResolutionCheck(int percent)
    {
        Plugin.Line($"VR_SETTINGS_RESOLUTION percent={percent} actual={VrGraphics.Width}x{VrGraphics.Height} expected={VrGraphics.ScaledWidth(percent)}x{VrGraphics.ScaledHeight(percent)}");
        if(VrGraphics.Width!=VrGraphics.ScaledWidth(percent)||VrGraphics.Height!=VrGraphics.ScaledHeight(percent))throw new Exception("Native XR allocation does not match preset label");
    }
}

internal static class VrResolutionAudit
{
    private static readonly int[] scales={50,65,80,101,125,150,200,100};
    private static int phase,index,cycle,firstCount,firstHistories,histories;private static float next=25;
    private static int Snapshot(string label)
    {
        histories=0;var values=new List<object>();foreach(var t in Resources.FindObjectsOfTypeAll<RenderTexture>()){if(t.name.StartsWith("LightPrevRT_")||t.name.StartsWith("MediaPrevRT_"))histories++;values.Add(new{id=t.GetInstanceID(),name=t.name,width=t.width,height=t.height,depth=t.volumeDepth,created=t.IsCreated()});}
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),label+"-textures.json"),System.Text.Json.JsonSerializer.Serialize(values));return values.Count;
    }
    internal static void Tick()
    {
        if(phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try{switch(phase)
        {
            case 0:Snapshot("resolution-before");phase=1;break;
            case 1:VrSettings.Resolution.Value=VrGraphics.ClampPercent(scales[index]);phase=2;break;
            case 2:
                if(VrGraphics.Width!=VrGraphics.ScaledWidth(scales[index])||VrGraphics.Height!=VrGraphics.ScaledHeight(scales[index]))throw new Exception("Actual native resolution differs from label");
                int count=Snapshot($"resolution-{cycle}-{scales[index]}");Plugin.Line($"VR_RESOLUTION_RUNTIME cycle={cycle} percent={VrSettings.Resolution.Value} dimensions={VrGraphics.Width}x{VrGraphics.Height} textures={count} fogHistories={histories}");
                if(scales[index]==50||scales[index]==200)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),$"shot resolution-{cycle}-{scales[index]}\n");
                if(++index<scales.Length){phase=1;break;}
                index=0;if(cycle++==0){firstCount=count;firstHistories=histories;phase=1;break;}
                if(histories!=firstHistories||Math.Abs(count-firstCount)>3)throw new Exception($"Repeated scale cycle did not plateau: {firstCount} to {count}");
                StereoTrial.DiagnosticMetrics();Plugin.Line("VR_RESOLUTION_RESOURCE_AUDIT_COMPLETE");phase=99;break;
        }next=Time.realtimeSinceStartup+1;
        }catch(Exception e){Plugin.Line("VR_RESOLUTION_AUDIT_FAILED "+e);phase=99;}
    }
}
