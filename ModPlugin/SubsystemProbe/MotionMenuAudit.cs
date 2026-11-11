using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UI.MainMenu;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionMenuAudit
{
    private static bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_MENU_AUDIT")=="1";
    internal static void AfterGameplay(){enabled=true;phase=0;next=Time.realtimeSinceStartup+1;}
    private static int phase;private static float next;
    private static MainMenuManager menu;private static RenderTextureScreenInput surface;
    private static ScrollView scroll;private static float before;
    private static bool Grip=>Environment.GetEnvironmentVariable("IKAG_XR_MENU_GRIP_AUDIT")=="1";
    private static string Point(VisualElement element)
    {
        var size=menu.uiDocument.rootVisualElement.layout;var uv=element.worldBound.center;
        var rect=surface.screenRect;
        var origin=new Vector3(Mathf.Lerp(rect.x,rect.z,uv.x/size.width),Mathf.Lerp(rect.y,rect.w,1-uv.y/size.height),-1);
        float nearest=float.PositiveInfinity;var vertices=surface.vertices;var triangles=surface.triangles;
        for(int i=0;i<triangles.Length;i+=3)
            if(RenderTextureScreenInput.RayTriangle(origin,Vector3.forward,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],out var hit)&&hit<nearest)nearest=hit;
        if(!float.IsFinite(nearest))throw new InvalidOperationException("Exact UI coordinate misses native TV triangles");
        return MotionPointer.AimAtWorld(surface.screenMesh.transform.TransformPoint(origin+Vector3.forward*nearest),Grip);
    }
    private static void FindScroll(VisualElement element)
    {
        if(element.resolvedStyle.display==DisplayStyle.None||element.resolvedStyle.visibility!=Visibility.Visible)return;
        var s=element.TryCast<ScrollView>();
        if(s!=null&&s.worldBound.width>10&&s.worldBound.height>10&&s.verticalScroller.highValue>0&&s.verticalScroller.highValue<1e7&&(scroll==null||s.verticalScroller.highValue>scroll.verticalScroller.highValue))scroll=s;
        for(int i=0;i<element.childCount;++i)FindScroll(element.ElementAt(i));
    }
    internal static void Tick()
    {
        if(!enabled||phase>10||!StereoTrial.MenuRendered||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;
            if(phase==0)
            {
                menu=UnityEngine.Object.FindObjectOfType<MainMenuManager>();
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<RenderTextureScreenInput>(true))
                    if(candidate.uiDocument!=null&&candidate.uiDocument.Pointer==menu.uiDocument.Pointer){surface=candidate;break;}
                if(surface==null)throw new InvalidOperationException("Native menu TV surface not found");
                Plugin.Line($"MENU_DOCUMENT_CONTROL mode={menu.uiDocument.panelSettings.renderMode} target={menu.uiDocument.panelSettings.targetTexture.name} surface={surface.name}");
                command=Point(menu.btnSettings);
            }
            else if(phase==1)command=Grip?"grip l 1":"trigger r 1";
            else if(phase==2)command=Grip?"grip l 0":"trigger r 0";
            else if(phase==3)
            {
                if(menu.activePage.Pointer!=menu.settingsPage.Pointer)throw new InvalidOperationException("Laser Settings click was not accepted");
                FindScroll(menu.settingsPage);
                if(scroll!=null){before=scroll.scrollOffset.y;command=Point(scroll.contentViewport);}
                else Plugin.Line("MENU_SCROLL_CONTROL no scrollable settings view; computer ScrollViews remain the relevant fixture");
            }
            else if(phase==4)command=scroll==null?null:"stick r 0 -0.8";
            else if(phase==5)
            {
                if(scroll!=null)Plugin.Line($"MENU_SCROLL_CONTROL before={before:R} after={scroll.scrollOffset.y:R} max={scroll.verticalScroller.highValue:R}");
                command="stick r 0 0\nbtn b down";
            }
            else if(phase==6)command="btn b up";
            else if(phase==7)
            {
                if(menu.activePage.Pointer!=menu.mainPage.Pointer)throw new InvalidOperationException("B back did not restore main page");
                Plugin.Line("MAIN_MENU_CONTINUE_COMMAND "+Point(menu.btnContinue));command="shot motion-main-menu-final";
            }
            else if(phase==10)Plugin.Line("MOTION_MAIN_MENU_COMPLETE");
            Plugin.Line($"MAIN_MENU_CONTROL phase={phase} main={menu.activePage.Pointer==menu.mainPage.Pointer} settings={menu.activePage.Pointer==menu.settingsPage.Pointer}");
            MotionPointer.Trace();
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+1.2f;
        }
        catch(Exception e){Plugin.Line("MOTION_MAIN_MENU_FAILED "+e);phase=11;}
    }
}
