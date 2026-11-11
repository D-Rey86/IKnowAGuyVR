using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using UI.MainMenu;
namespace IKnowAGuyVR.SubsystemProbe;

// Uses the game's existing settings page, USS classes, scroll view and dispatcher.
internal static class VrSettingsMenu
{
    internal sealed class Page
    {
        internal SettingsPageController Controller;internal Button Tab;internal VisualElement Root,Content,SnapRow,SmoothRow,SeparationRow;
        internal VisualElement[] NativePanels;internal Button[] NativeTabs;internal bool Pause;internal string Prefix=>Pause?"esc-":"";internal string ActiveClass=>Pause?"esc-tab-active":SettingsPageController.TAB_ACTIVE_CLASS;
        internal ScrollView Scroll;internal Label Resolution;
        internal readonly List<Action> Updates=new();internal readonly Button[] Presets=new Button[5];internal bool Open;
        internal void Refresh()
        {
            SupportReport.Tick();
            foreach(var update in Updates)update();
            SnapRow.style.display=VrSettings.SnapTurn.Value?DisplayStyle.Flex:DisplayStyle.None;
            SmoothRow.style.display=VrSettings.SnapTurn.Value?DisplayStyle.None:DisplayStyle.Flex;
            Resolution.text=VrGraphics.Width>0?$"{VrGraphics.Width} Ã— {VrGraphics.Height} per eye":"Waiting for headset";
            for(int i=0;i<5;++i){int p=VrSettings.Presets[i];Presets[i].text=VrSettings.PresetNames[i]+"\n"+(VrGraphics.RecommendedWidth>0?$"{VrGraphics.ScaledWidth(p)} Ã— {VrGraphics.ScaledHeight(p)}":"â€”");Presets[i].EnableInClassList(ActiveClass,VrSettings.Resolution.Value==VrGraphics.ClampPercent(p)&&(i==4||VrGraphics.ClampPercent(VrSettings.Presets[i+1])!=VrGraphics.ClampPercent(p)));}
        }
        internal void Show()
        {
            if(Controller!=null)Controller.ShowTab(SettingsTab.Visual);
            foreach(var element in NativePanels)element.style.display=DisplayStyle.None;
            foreach(var button in NativeTabs)button.RemoveFromClassList(ActiveClass);
            Tab.AddToClassList(ActiveClass);Root.style.display=DisplayStyle.Flex;Open=true;Refresh();
        }
    }
    private static readonly Dictionary<IntPtr,Page> pages=new();private static int revision=-1;
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.settings-menu");
        h.Patch(typeof(SettingsPageController).GetMethod("Initialize"),postfix:Hook(nameof(Bind)));
        h.Patch(typeof(SettingsPageController).GetMethod("ShowTab"),postfix:Hook(nameof(NativeTab)));
        h.Patch(typeof(SettingsPageController).GetMethod("Hide"),postfix:Hook(nameof(Hide)));
        h.Patch(typeof(SettingsPageController).GetMethod("Show"),postfix:Hook(nameof(Show)));
        h.Patch(typeof(SettingsPageController).GetMethod("Cleanup"),prefix:Hook(nameof(Cleanup)));
        h.Patch(typeof(HUDManager).GetMethod("OpenSettingsPanel"),postfix:Hook(nameof(BindPause)));
        h.Patch(typeof(HUDManager).GetMethod("CloseSettingsPanel"),postfix:Hook(nameof(ClosePause)));
        VrSettings.Changed+=RefreshOpen;
    }
    private static HarmonyMethod Hook(string name)=>new(typeof(VrSettingsMenu).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
    private static void Click(Button button,Action action)
    {
        button.RegisterCallback<ClickEvent>((Action<ClickEvent>)(_=>action()));
        button.RegisterCallback<NavigationSubmitEvent>((Action<NavigationSubmitEvent>)(e=>{action();e.StopImmediatePropagation();}));
    }
    internal static Page Find(SettingsPageController controller)=>controller!=null&&pages.TryGetValue(controller.Pointer,out var p)?p:null;
    internal static Page ForPanel(VisualElement root){foreach(var p in pages.Values)if(p.Root.panel!=null&&root.panel!=null&&p.Root.panel.Pointer==root.panel.Pointer)return p;return null;}
    private static void Bind(SettingsPageController __instance)
    {
        if(__instance.btnTabVisual==null||__instance.visualTab==null||pages.ContainsKey(__instance.Pointer))return;
        var p=new Page{Controller=__instance,NativePanels=new[]{__instance.gameplayTab,__instance.visualTab,__instance.audioTab,__instance.controlsTab},NativeTabs=new[]{__instance.btnTabGameplay,__instance.btnTabVisual,__instance.btnTabAudio,__instance.btnTabControls}};pages.Add(__instance.Pointer,p);Build(p);
    }
    private static void BindPause(HUDManager __instance)
    {
        if(pages.TryGetValue(__instance.Pointer,out var existing)){existing.Open=false;existing.Root.style.display=DisplayStyle.None;existing.Tab.RemoveFromClassList(existing.ActiveClass);return;}
        var root=__instance.escapeSettingsPanel;if(root==null)return;
        var panelNames=new[]{"EscGameplayTab","EscVisualTab","EscAudioTab","EscControlsTab"};var buttonNames=new[]{"BtnEscTabGameplay","BtnEscTabVisual","BtnEscTabAudio","BtnEscTabControls"};
        var p=new Page{Pause=true,NativePanels=new VisualElement[4],NativeTabs=new Button[4]};
        for(int i=0;i<4;++i){p.NativePanels[i]=Element(root,panelNames[i]);p.NativeTabs[i]=Element(root,buttonNames[i])?.TryCast<Button>();if(p.NativePanels[i]==null||p.NativeTabs[i]==null)return;}
        var key=__instance.Pointer;pages.Add(key,p);Build(p);
        p.Root.RegisterCallback<DetachFromPanelEvent>((Action<DetachFromPanelEvent>)(_=>{p.Open=false;pages.Remove(key);}));
        foreach(var button in p.NativeTabs)button.RegisterCallback<ClickEvent>((Action<ClickEvent>)(_=>Close(p)));
    }
    private static void Close(Page p){p.Open=false;p.Root.style.display=DisplayStyle.None;p.Tab.RemoveFromClassList(p.ActiveClass);}
    private static void ClosePause(HUDManager __instance){if(pages.TryGetValue(__instance.Pointer,out var p))Close(p);VrSettings.Flush();}
    internal static VisualElement Element(VisualElement root,string name){if(root.name==name)return root;for(int i=0;i<root.childCount;++i){var found=Element(root.ElementAt(i),name);if(found!=null)return found;}return null;}
    private static void Build(Page p)
    {
        p.Tab=new Button{name="IKAG_VR_Settings_Tab",text="VR"};p.Tab.AddToClassList(p.Prefix+"tab");Click(p.Tab,()=>p.Show());p.NativeTabs[1].parent.Add(p.Tab);
        p.Root=new VisualElement{name="IKAG_VR_Settings"};p.Root.AddToClassList(p.Prefix+"settings-tab");p.Root.style.display=DisplayStyle.None;p.NativePanels[1].parent.Add(p.Root);
        p.Scroll=new ScrollView(ScrollViewMode.Vertical){name="IKAG_VR_Settings_Scroll"};p.Scroll.AddToClassList(p.Pause?"esc-settings-scroll":"settings-tab-scroll");p.Root.Add(p.Scroll);
        p.Content=new VisualElement();p.Content.AddToClassList(p.Prefix+"settings-tab-content");p.Scroll.Add(p.Content);
        Heading(p,"PICTURE");
        var row=Row(p,"Current Resolution");p.Resolution=new Label();p.Resolution.AddToClassList(p.Prefix+"setting-label");p.Resolution.style.unityTextAlign=TextAnchor.MiddleRight;row.Add(p.Resolution);
        Slider(p,"Resolution Scale",50,200,()=>VrSettings.Resolution.Value,v=>VrSettings.Resolution.Value=Mathf.RoundToInt(v),v=>$"{v:0}%");
        row=Row(p,"VR Resolution Presets");row.style.flexDirection=FlexDirection.Column;row.style.height=116;row.style.minHeight=116;row.style.flexShrink=0;
        var presets=new VisualElement();presets.style.flexDirection=FlexDirection.Row;presets.style.width=new Length(100,LengthUnit.Percent);row.Add(presets);
        for(int i=0;i<5;++i){int index=i;var b=new Button{name="IKAG_VR_Preset_"+VrSettings.PresetNames[i]};b.AddToClassList(p.Prefix+"tab");b.style.whiteSpace=WhiteSpace.Normal;b.style.flexGrow=1;b.style.flexBasis=0;b.style.height=70;b.style.minHeight=70;b.style.fontSize=p.Pause?16:13;Click(b,()=>VrSettings.Resolution.Value=VrGraphics.ClampPercent(VrSettings.Presets[index]));presets.Add(b);p.Presets[i]=b;}
        Slider(p,"World Scale",50,150,()=>VrSettings.WorldScale.Value,v=>VrSettings.WorldScale.Value=Mathf.RoundToInt(v),v=>$"{v:0}%");
        p.SeparationRow=Slider(p,"Stereo Separation",0,200,()=>VrSettings.Separation.Value,v=>VrSettings.Separation.Value=Mathf.RoundToInt(v),v=>$"{v:0}%");
        Choices(p,"Desktop View",new[]{"VR Mirror","Independent"},()=>VrSettings.Mirror.Value?0:1,i=>VrSettings.Mirror.Value=i==0);
        Heading(p,"GAMEPLAY");
        Choices(p,"Turning",new[]{"Smooth","Snap"},()=>VrSettings.SnapTurn.Value?1:0,i=>VrSettings.SnapTurn.Value=i==1);
        p.SnapRow=Choices(p,"Snap Angle",new[]{"15Â°","30Â°","45Â°","90Â°"},()=>Array.IndexOf(new[]{15,30,45,90},VrSettings.SnapAngle.Value),i=>VrSettings.SnapAngle.Value=new[]{15,30,45,90}[i]);
        p.SmoothRow=Slider(p,"Smooth Turn Sensitivity",30,180,()=>VrSettings.TurnSpeed.Value,v=>VrSettings.TurnSpeed.Value=Mathf.RoundToInt(v),v=>$"{v:0}Â°/s");
        Choices(p,"Movement",new[]{"Smooth","Teleport"},()=>VrSettings.Teleport.Value?1:0,i=>VrSettings.Teleport.Value=i==1);
        Choices(p,"Gamepad Mode",new[]{"Disable","Enable"},()=>VrSettings.Gamepad.Value?1:0,i=>VrSettings.Gamepad.Value=i==1);
        Choices(p,"Dominant Hand",new[]{"Right","Left"},()=>VrSettings.LeftHanded.Value?1:0,i=>VrSettings.LeftHanded.Value=i==1);
        Choices(p,"Swap Motion Sticks",new[]{"Off","On"},()=>VrSettings.SwapSticks.Value?1:0,i=>VrSettings.SwapSticks.Value=i==1);
        Choices(p,"Play Position",new[]{"Standing","Seated"},()=>VrSettings.Seated.Value?1:0,i=>VrSettings.Seated.Value=i==1);
        var seatHeight=Slider(p,"Seated Height Adjustment",-50,70,()=>VrSettings.SeatedHeight.Value,v=>VrSettings.SeatedHeight.Value=Mathf.RoundToInt(v),v=>$"{v:+0;-0;0} cm");
        var seatRow=Row(p,"Seated Calibration");var calibrate=new Button{name="IKAG_VR_CalibrateSeatedHeight",text="Calibrate Seated Height"};calibrate.AddToClassList(p.Prefix+"tab");Click(calibrate,StereoTrial.CalibrateSeatedHeight);calibrate.SetEnabled(p.Pause);seatRow.Add(calibrate);
        var seatHint=new Label("Seated height calibrates when you enter the game. In the pause menu, sit comfortably and recalibrate after changing posture. Adjust eye height here; Belt Height and Belt Distance adjust seated reach.");seatHint.AddToClassList(p.Prefix+"setting-label");seatHint.style.whiteSpace=WhiteSpace.Normal;p.Content.Add(seatHint);
        p.Updates.Add(()=>{var display=VrSettings.Seated.Value?DisplayStyle.Flex:DisplayStyle.None;seatHeight.style.display=seatRow.style.display=seatHint.style.display=display;});
        Choices(p,"UI",new[]{"Immersive","Classic"},()=>ImmersiveHud.ConfiguredImmersive?0:1,i=>ImmersiveHud.SetEnabled(i==0));
        Slider(p,"Wrist Display Size",WristDisplaySettings.Minimum,WristDisplaySettings.Maximum,()=>VrSettings.WristScale.Value,v=>VrSettings.WristScale.Value=Mathf.RoundToInt(v),v=>$"{v:0}%");
        Slider(p,"Belt Height",.22f,.65f,()=>.87f-PhysicalBelt.Height,v=>PhysicalBelt.Height=.87f-v,v=>$"{(.87f-v)*100:0} cm below eyes");
        Slider(p,"Belt Distance",.10f,.52f,()=>PhysicalBelt.Distance,v=>PhysicalBelt.Distance=v,v=>$"{v*100:0} cm");
        var hint=new Label("Teleport: hold the movement stick forward, aim with your off hand, then release. Gamepads use left stick and head aim.");hint.AddToClassList(p.Prefix+"setting-label");hint.style.whiteSpace=WhiteSpace.Normal;p.Content.Add(hint);
        var padHint=new Label("Gamepad: A jump/confirm · B drop/back · X interact · Y recipes · RT use · LT secondary · LB/RB inventory · L3 sprint · R3 crouch · Start pause · View build.");padHint.AddToClassList(p.Prefix+"setting-label");padHint.style.whiteSpace=WhiteSpace.Normal;p.Content.Add(padHint);
        VrControlReference.Add(p);
        SupportReportMenu.Add(p);
        p.Refresh();
    }
    private static void Heading(Page p,string text){var label=new Label(text);label.AddToClassList(p.Prefix+"section-title");label.style.fontSize=p.Pause?18:13;label.style.color=new Color(1,.843f,.176f);p.Content.Add(label);}
    private static VisualElement Row(Page p,string label)
    {var row=new VisualElement();row.AddToClassList(p.Prefix+"setting-row");var l=new Label(label);l.AddToClassList(p.Prefix+"setting-label");row.Add(l);p.Content.Add(row);return row;}
    private static VisualElement Slider(Page p,string name,float min,float max,Func<float> get,Action<float> set,Func<float,string> format)
    {
        var row=Row(p,name);var value=new Label();value.AddToClassList(p.Prefix+"setting-label");value.style.minWidth=110;value.style.flexGrow=0;value.style.unityTextAlign=TextAnchor.MiddleRight;
        var slider=new UnityEngine.UIElements.Slider(min,max){name="IKAG_VR_"+name.Replace(" ","")};slider.AddToClassList(p.Prefix+"setting-slider");slider.style.minWidth=220;
        slider.RegisterCallback<ChangeEvent<float>>((Action<ChangeEvent<float>>)(e=>set(e.newValue)));row.Add(slider);row.Add(value);
        p.Updates.Add(()=>{if(name=="Resolution Scale")slider.highValue=VrGraphics.MaximumPercent;slider.SetValueWithoutNotify(get());value.text=format(get());});return row;
    }
    private static VisualElement Choices(Page p,string name,string[] names,Func<int> get,Action<int> set)
    {
        var row=Row(p,name);var group=new GroupBox();group.style.flexDirection=FlexDirection.Row;group.style.flexGrow=0;row.Add(group);var buttons=new RadioButton[names.Length];
        for(int i=0;i<names.Length;++i){int index=i;var r=new RadioButton(names[i]){name="IKAG_VR_"+name.Replace(" ","")+i};r.style.color=new Color(1,1,1,.85f);r.style.marginLeft=18;r.style.fontSize=p.Pause?18:15;r.labelElement.style.minWidth=0;r.labelElement.style.width=StyleKeyword.Auto;r.labelElement.style.marginRight=8;r.labelElement.style.fontSize=p.Pause?18:15;
            var mark=Element(r,"unity-checkmark");if(mark!=null){var ring=mark.parent;ring.style.backgroundColor=new Color(.078f,.098f,.137f);ring.style.borderTopWidth=ring.style.borderBottomWidth=ring.style.borderLeftWidth=ring.style.borderRightWidth=2;ring.style.borderTopColor=ring.style.borderBottomColor=ring.style.borderLeftColor=ring.style.borderRightColor=new Color(1,.843f,.176f);mark.style.backgroundColor=new Color(1,.843f,.176f);}
            r.RegisterCallback<ChangeEvent<bool>>((Action<ChangeEvent<bool>>)(e=>{if(e.newValue)set(index);}));group.Add(r);buttons[i]=r;}
        p.Updates.Add(()=>{for(int i=0;i<buttons.Length;++i)buttons[i].SetValueWithoutNotify(i==get());});return row;
    }
    private static void NativeTab(SettingsPageController __instance){var p=Find(__instance);if(p==null)return;p.Open=false;p.Root.style.display=DisplayStyle.None;p.Tab.RemoveFromClassList(SettingsPageController.TAB_ACTIVE_CLASS);}
    private static void Hide(SettingsPageController __instance){var p=Find(__instance);if(p!=null)p.Open=false;VrSettings.Flush();}
    private static void Show(SettingsPageController __instance){var p=Find(__instance);if(p!=null&&p.Root.style.display.value==DisplayStyle.Flex){p.Open=true;p.Refresh();}}
    private static void Cleanup(SettingsPageController __instance){var p=Find(__instance);if(p==null)return;p.Root.RemoveFromHierarchy();p.Tab.RemoveFromHierarchy();pages.Remove(__instance.Pointer);VrSettings.Flush();}
    private static void RefreshOpen(){foreach(var p in pages.Values)if(p.Open)p.Refresh();}
    internal static void Tick(){if(revision==VrGraphics.Revision)return;revision=VrGraphics.Revision;RefreshOpen();}
}
