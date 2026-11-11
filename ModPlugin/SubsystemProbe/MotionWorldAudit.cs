using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionWorldAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_WORLD_AUDIT")=="1";
    private static int phase;private static float next=25;
    private static KitchenOrderDisplay kitchen;private static DarkWebComputer dark;
    private static Button tab;private static int clicks;
    private static ScrollView scroll;private static float scrollBefore;
    private static void FindScroll(VisualElement element)
    {
        if(element==null)return;
        if(element.resolvedStyle.display==DisplayStyle.None||element.resolvedStyle.visibility!=Visibility.Visible)return;
        var s=element.TryCast<ScrollView>();
        if(s!=null&&s.verticalScroller!=null&&s.worldBound.width>0&&s.worldBound.height>0&&s.verticalScroller.highValue>0&&s.verticalScroller.highValue<1e7&&(scroll==null||s.verticalScroller.highValue>scroll.verticalScroller.highValue))scroll=s;
        for(int i=0;i<element.childCount;++i)FindScroll(element.ElementAt(i));
    }
    private static void Click(Clickable __instance)
    {++clicks;Plugin.Line("WORLD_UI_CLICK_ACCEPTED target="+__instance.target.name);}
    internal static void Tick()
    {
        if(!enabled||phase>22||Time.realtimeSinceStartup<next||!StereoTrial.GameplayActive)return;
        try
        {
            string command=null;
            if(phase==0)
            {
                new Harmony("iknowaguyvr.world-ui-audit").Patch(typeof(Clickable).GetMethod("Invoke",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(EventBase)},null),prefix:new HarmonyMethod(typeof(MotionWorldAudit).GetMethod(nameof(Click),BindingFlags.Static|BindingFlags.NonPublic)));
                kitchen=UnityEngine.Object.FindObjectOfType<KitchenOrderDisplay>();dark=UnityEngine.Object.FindObjectOfType<DarkWebComputer>();
                Plugin.Line($"WORLD_UI_CENSUS kitchen={(kitchen==null?"none":kitchen.name)} dark={(dark==null?"none":dark.name)}");
                if(kitchen!=null){Plugin.Line("KITCHEN_CAN_INTERACT="+kitchen.CanInteract());kitchen.OnInteract();}
            }
            else if(phase==1)command=Point(kitchen==null?null:kitchen.supplierDeliveryUI.uiDocument,"kitchen");
            else if(phase==2){MotionPointer.TraceWorldPick();Plugin.Line("KITCHEN_HOVER_CONTROL="+(tab!=null&&(tab.pseudoStates&PseudoStates.Hover)!=0));command="trigger r 1";}
            else if(phase==3)command="trigger r 0\nshot motion-kitchen-computer";
            else if(phase==4){Plugin.Line("KITCHEN_CLICK_RESULT count="+clicks+" tab="+kitchen?.supplierDeliveryUI.currentTab);kitchen?.TriggerExit();}
            else if(phase==5){if(dark!=null){Plugin.Line("DARK_CAN_INTERACT="+dark.CanInteract());dark.OnInteract();}}
            else if(phase==6)command=Point(dark==null?null:dark.darkWebUIManager.uiDocument,"dark");
            else if(phase==7){MotionPointer.TraceWorldPick();Plugin.Line("DARK_HOVER_CONTROL="+(tab!=null&&(tab.pseudoStates&PseudoStates.Hover)!=0));command="trigger r 1";}
            else if(phase==8)command="trigger r 0\nshot motion-dark-computer";
            else if(phase==9)
            {
                Plugin.Line("DARK_CLICK_RESULT total="+clicks+" tab="+dark?.darkWebUIManager.currentTab);
                scroll=null;FindScroll(dark.darkWebUIManager.uiDocument.rootVisualElement);
                if(scroll!=null)
                {
                    scrollBefore=scroll.scrollOffset.y;var doc=dark.darkWebUIManager.uiDocument;var viewport=scroll.contentViewport;
                    var p=doc.transform.TransformPoint(viewport.worldTransform.MultiplyPoint3x4(viewport.rect.center));command=MotionPointer.AimAtWorld(p);
                }
                else Plugin.Line("DARK_SCROLL_CONTROL no visible overflowing ScrollView");
            }
            else if(phase==10)command=scroll==null?null:"stick r 0 -0.8";
            else if(phase==11)
            {
                if(scroll!=null)Plugin.Line($"DARK_SCROLL_CONTROL before={scrollBefore:R} after={scroll.scrollOffset.y:R} max={scroll.verticalScroller.highValue:R}");
                command="stick r 0 0";
            }
            else if(phase==12)dark?.TriggerExit();
            else if(phase==13)command="hands reset\nhead rot 0 0 0\nclick r down";
            else if(phase==14)command="click r up";
            else if(phase==15)
            {
                scroll=null;
                foreach(var doc in UnityEngine.Object.FindObjectsOfType<UIDocument>())
                    if(doc.isActiveAndEnabled&&doc.panelSettings!=null&&doc.panelSettings.renderMode==PanelRenderMode.ScreenSpaceOverlay&&doc.panelSettings.targetTexture==null)FindScroll(doc.rootVisualElement);
                if(scroll!=null){scrollBefore=scroll.scrollOffset.y;command=MotionPointer.AimAt(scroll.contentViewport.worldBound.center);}
                else Plugin.Line("RECIPE_SCROLL_CONTROL no visible overflowing ScrollView");
            }
            else if(phase==16)command=scroll==null?null:"stick r 0 -0.8";
            else if(phase==17)
            {
                if(scroll!=null)Plugin.Line($"RECIPE_SCROLL_CONTROL before={scrollBefore:R} after={scroll.scrollOffset.y:R} max={scroll.verticalScroller.highValue:R}");
                command="stick r 0 0\nbtn b press 100";
            }
            else if(phase==18)command="hands reset";
            else if(phase==22)Plugin.Line("MOTION_WORLD_AUDIT_COMPLETE");
            MotionPointer.Trace();
            Plugin.Line($"WORLD_UI_PHASE {phase} machine={MachineInteractionBase.IsLocalPlayerInMachineMode} cursor={Cursor.lockState} selected={(tab==null?"none":tab.name)}");
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+2;
        }
        catch(Exception e){Plugin.Line("WORLD_AUDIT_FAILED "+e);phase=23;}
    }
    private static string Point(UIDocument doc,string label)
    {
        tab=null;clicks=0;
        if(doc==null){Plugin.Line("WORLD_UI_DOCUMENT_MISSING "+label);return null;}
        Scan(doc.rootVisualElement,0);
        if(tab==null){Plugin.Line("WORLD_UI_SAFE_TAB_MISSING "+label);return null;}
        // World-space panel matrices are in document-local metres; element rect
        // is local to the element, whereas localBound already includes layout.
        var p=doc.transform.TransformPoint(tab.worldTransform.MultiplyPoint3x4(tab.rect.center));
        Plugin.Line($"WORLD_UI_AIM {label} target={tab.name}/{tab.text} local={tab.localBound} world={p:R} transform={tab.worldTransform.ToString("R")} doc={doc.transform.localToWorldMatrix.ToString("R")}");
        return MotionPointer.AimAtWorld(p);
    }
    private static void Scan(VisualElement element,int depth)
    {
        if(element==null||depth>25)return;
        var b=element.TryCast<Button>();
        if(b!=null&&b.resolvedStyle.display!=DisplayStyle.None)
        {
            Plugin.Line($"WORLD_UI_BUTTON name={b.name} text={b.text} bound={b.localBound} world={b.worldTransform.ToString("R")}");
            string text=b.text?.Trim().ToLowerInvariant();
            if(tab==null&&(text=="machines"||text=="upgrades"||text=="orders"||text=="shop"||text=="home"||text=="marketplace"||b.name=="tab-machines"))tab=b;
        }
        for(int i=0;i<element.childCount;++i)Scan(element.ElementAt(i),depth+1);
    }
}
