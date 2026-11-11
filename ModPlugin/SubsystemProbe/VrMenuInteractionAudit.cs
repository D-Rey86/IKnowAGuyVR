using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UI.MainMenu;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputEvent=UnityEngine.InputSystem.LowLevel.InputEvent;
namespace IKnowAGuyVR.SubsystemProbe;

internal static unsafe class VrMenuInteractionAudit
{
    private static int phase;private static float next=12,beforeScroll;private static MainMenuManager menu;private static RenderTextureScreenInput surface;private static VrSettingsMenu.Page page;private static VisualElement slider,track,thumb;
    private static Gamepad gamepad;
    private static void Pad(uint buttons){if(!gamepad.enabled)InputSystem.EnableDevice(gamepad);byte* bytes=stackalloc byte[52];*(InputEvent*)bytes=new InputEvent(StateEvent.Type,52,gamepad.deviceId,-1);*(UnityEngine.InputSystem.Utilities.FourCC*)(bytes+20)=GamepadState.Format;*(GamepadState*)(bytes+24)=new GamepadState{buttons=buttons};InputSystem.QueueEvent(new InputEventPtr((InputEvent*)bytes));}
    private static Vector3 aimedTarget,cameraAtAim;private static Quaternion rotationAtAim;
    private static void Check(bool b,string s){if(!b)throw new Exception(s);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static VisualElement E(string name){var e=VrSettingsMenu.Element(page.Root,name);Check(e!=null,"Missing control "+name);return e;}
    private static string Point(Vector2 point)
    {
        var size=menu.uiDocument.rootVisualElement.layout;var rect=surface.screenRect;
        var viewLocal=surface.screenMesh.transform.InverseTransformPoint(Camera.main.transform.position);float side=Mathf.Sign(viewLocal.z);
        var origin=new Vector3(Mathf.Lerp(rect.x,rect.z,point.x/size.width),Mathf.Lerp(rect.y,rect.w,1-point.y/size.height),side);var direction=Vector3.back*side;
        float distance=float.PositiveInfinity;var vertices=surface.vertices;var triangles=surface.triangles;
        for(int i=0;i<triangles.Length;i+=3)if(RenderTextureScreenInput.RayTriangle(origin,direction,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],out var d)&&d<distance)distance=d;
        Check(float.IsFinite(distance),"Control does not map to native TV triangles");
        aimedTarget=surface.screenMesh.transform.TransformPoint(origin+direction*distance);cameraAtAim=Camera.main.transform.position;rotationAtAim=Camera.main.transform.rotation;return MotionPointer.AimAtWorld(aimedTarget);
    }
    private static void Aim(VisualElement element)=>Send(Point(element.worldBound.center));
    private static void Click()=>Send("trigger r pull 150");
    internal static void Tick()
    {
        if(phase==99||!StereoTrial.MenuRendered||Time.realtimeSinceStartup<next)return;
        try{float delay=.5f;switch(phase)
        {
            case 0:
                menu=UnityEngine.Object.FindObjectOfType<MainMenuManager>();page=VrSettingsMenu.Find(menu.settingsController);Check(page!=null,"VR tab not bound");
                foreach(var s in UnityEngine.Object.FindObjectsOfType<RenderTextureScreenInput>(true))if(s.uiDocument!=null&&s.uiDocument.Pointer==menu.uiDocument.Pointer){surface=s;break;}
                Check(surface!=null,"Native TV surface missing");menu.OnSettingsButtonClicked();Send("input clear\nhands reset\nhand r offset 0 0 0");break;
            case 1:Aim(page.Tab);break;
            case 2:surface.TryHitSurface(default,out var localHit);Plugin.Line($"VR_UI_GEOMETRY expectedLocal={surface.screenMesh.transform.InverseTransformPoint(aimedTarget)} nativeLocal={localHit} rect={surface.screenRect} texture={menu.uiDocument.panelSettings.targetTexture.width}x{menu.uiDocument.panelSettings.targetTexture.height} panelScale={menu.uiDocument.panelSettings.scale} worldHit={surface.screenMesh.transform.TransformPoint(localHit)} usedRay={MotionPointer.MenuRay}");MotionPointer.Trace();MotionPointer.TryControllerRay(false,out var ray);Plugin.Line($"VR_UI_TARGET rect={page.Tab.worldBound} point={MotionPointer.MenuPoint} hit={MotionPointer.MenuHit} desiredWorld={aimedTarget} ray={ray} angleError={Vector3.Angle(ray.direction,aimedTarget-ray.origin)} cameraDelta={Vector3.Distance(cameraAtAim,Camera.main.transform.position)} rotationDelta={Quaternion.Angle(rotationAtAim,Camera.main.transform.rotation)} rootLayout={menu.uiDocument.rootVisualElement.layout}");Send("shot vr-tab-aim\ntrigger r pull 150");break;
            case 3:MotionPointer.Trace();Check(page.Open,"Controller click did not open VR tab");Send("shot vr-menu-controller-open");Aim(page.Presets[0]);break;
            case 4:Click();break;
            case 5:
                Check(VrSettings.Resolution.Value==65,"Low preset click not accepted");Check(VrGraphics.Width==VrGraphics.ScaledWidth(65)&&VrGraphics.Height==VrGraphics.ScaledHeight(65),"Low preset allocation mismatch");
                slider=E("IKAG_VR_ResolutionScale");track=VrSettingsMenu.Element(slider,"unity-drag-container");thumb=VrSettingsMenu.Element(slider,"unity-dragger");Check(track!=null&&thumb!=null,"Slider geometry missing");Aim(thumb);break;
            case 6:Send("trigger r 1");break;
            case 7:
                var r=track.worldBound;float f=(150-50)/150f;Send(Point(new Vector2(r.xMin+thumb.worldBound.width*.5f+(r.width-thumb.worldBound.width)*f,thumb.worldBound.center.y)));break;
            case 8:Send("trigger r 0");break;
            case 9:
                Plugin.Line("VR_UI_SLIDER_DRAG percent="+VrSettings.Resolution.Value+" actual="+VrGraphics.Width+"x"+VrGraphics.Height);
                Check(VrSettings.Resolution.Value>=140&&VrSettings.Resolution.Value<=160,"Controller drag did not change resolution");
                page.Scroll.ScrollTo(E("IKAG_VR_Turning1"));break;
            case 10:Aim(E("IKAG_VR_Turning1"));break;
            case 11:Click();break;
            case 12:
                Check(VrSettings.SnapTurn.Value&&page.SnapRow.resolvedStyle.display==DisplayStyle.Flex&&page.SmoothRow.resolvedStyle.display==DisplayStyle.None,"Snap radio/conditional controls failed");page.Scroll.ScrollTo(E("IKAG_VR_SnapAngle2"));phase=119;break;
            case 120:Aim(E("IKAG_VR_SnapAngle2"));phase=12;break;
            case 13:Click();break;
            case 14:Check(VrSettings.SnapAngle.Value==45,"45 degree radio failed");Aim(E("IKAG_VR_Turning0"));break;
            case 15:Click();break;
            case 16:
                Check(!VrSettings.SnapTurn.Value&&page.SmoothRow.resolvedStyle.display==DisplayStyle.Flex&&page.SnapRow.resolvedStyle.display==DisplayStyle.None,"Smooth conditional controls failed");
                beforeScroll=page.Scroll.scrollOffset.y;Aim(page.Scroll.contentViewport);break;
            case 17:Send("stick r 0 -0.85");delay=1;break;
            case 18:Check(page.Scroll.scrollOffset.y>beforeScroll,"Controller stick did not scroll VR settings");Send("stick r 0 0");page.Scroll.ScrollTo(E("IKAG_VR_UI1"));break;
            case 19:Aim(E("IKAG_VR_UI1"));break;
            case 20:Click();break;
            case 21:Check(!ImmersiveHud.ConfiguredImmersive,"Classic radio not accepted");Aim(E("IKAG_VR_UI0"));break;
            case 22:Click();break;
            case 23:Check(ImmersiveHud.ConfiguredImmersive,"Immersive radio not restored");Send("shot vr-menu-gameplay");page.Scroll.scrollOffset=Vector2.zero;break;
            case 24:Check(page.SeparationRow.resolvedStyle.display==DisplayStyle.Flex,"Stereo separation is hidden");VrSettings.Resolution.Value=100;VrSettings.SnapAngle.Value=30;VrSettings.Flush();menu.HandleBackNavigation();phase=28;break;
            case 29:menu.OnSettingsButtonClicked();break;
            case 30:
                Check(menu.settingsController.gameplayTab.resolvedStyle.display==DisplayStyle.Flex,"Native settings did not reopen");Aim(page.Tab);break;
            case 31:Click();break;
            case 32:Check(page.Open,"VR tab did not reopen");Send("shot vr-menu-final");page.Scroll.ScrollTo(E("IKAG_VR_GamepadMode1"));break;
            case 33:Aim(E("IKAG_VR_GamepadMode1"));break;
            case 34:Click();break;
            case 35:Check(VrSettings.Gamepad.Value,"Motion click did not enable gamepad mode");Check(!MotionPointer.DiagnosticPressed&&!MotionPointer.DiagnosticBeam,"Gamepad mode retained pointer press/beam");Check(!ComputerKeyboard.IsOpen,"Gamepad mode retained motion keyboard");gamepad=InputSystem.AddDevice("Gamepad").Cast<Gamepad>();Pad(0);E("IKAG_VR_GamepadMode0").Focus();break;
            case 36:Pad(1u<<(int)GamepadButton.South);break;
            case 37:Check(!VrSettings.Gamepad.Value,"Gamepad could not restore motion mode in main-menu settings");Pad(0);InputSystem.RemoveDevice(gamepad);gamepad=null;Send("input clear");break;
            case 38:Check(MotionInput.Enabled,"Motion controls did not recover");Aim(E("IKAG_VR_GamepadMode0"));break;
            case 39:Send("trigger r 1");break;
            case 40:Check(MotionPointer.DiagnosticPressed,"Held-pointer fixture did not latch");VrSettings.Gamepad.Value=true;break;
            case 41:Check(!MotionPointer.DiagnosticPressed&&!MotionPointer.DiagnosticBeam,"Forced mode switch did not release held pointer");VrSettings.Gamepad.Value=false;Send("input clear");break;
            case 42:Check(MotionInput.Enabled,"Motion controls did not recover");Plugin.Line("VR_MENU_MODE_HANDOFF_PASS");Plugin.Line("VR_MENU_INTERACTION_AUDIT_COMPLETE");phase=99;return;
        }phase++;next=Time.realtimeSinceStartup+delay;
        }catch(Exception e){Plugin.Line("VR_MENU_INTERACTION_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
    }
}
