using System;
using BepInEx.Configuration;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class VrSettings
{
    internal static ConfigEntry<int> Resolution,WorldScale,Separation,SnapAngle,TurnSpeed,WristScale;
    internal static ConfigEntry<bool> SnapTurn,Teleport,Gamepad,Mirror,LeftHanded,SwapSticks,Seated;
    internal static ConfigEntry<int> SeatedHeight;
    internal static readonly int[] Presets={65,80,100,150,200};
    internal static readonly string[] PresetNames={"Low","Medium","High","Ultra","Max"};
    private static ConfigFile file;private static bool dirty;private static float saveAt;
    private static bool gamepad;
    internal static event Action Changed;
    internal static void Initialize(ConfigFile config)
    {
        file=config;file.SaveOnConfigSet=false;
        Resolution=Number("Picture","ResolutionScale",100,50,200,"Percentage of the runtime-recommended per-eye width and height. Above 100% supersamples.");
        Mirror=config.Bind("Picture","DesktopMirror",true,"Reuse a VR eye for the desktop view instead of rendering an independent view. Disable for the original desktop view and mouse alignment. HDR and off-screen captures retain native rendering.");
        WorldScale=Number("Picture","WorldScale",100,50,150,"Perceived world size percentage. Tracking and eye offsets scale together; the current head center is preserved.");
        Separation=Number("Picture","StereoSeparation",100,0,200,"Eye separation percentage relative to the active runtime. Uses the active runtime eye poses.");
        SnapTurn=config.Bind("Gameplay","SnapTurning",false,"Use discrete turns instead of smooth turning.");
        SnapAngle=Number("Gameplay","SnapAngle",30,15,90,"Snap angle: 15, 30, 45 or 90 degrees.");
        TurnSpeed=Number("Gameplay","SmoothTurnSpeed",90,30,180,"Smooth turning speed in degrees per second.");
        Teleport=config.Bind("Gameplay","TeleportMovement",false,"Hold the left stick forward to aim with the left controller; release to teleport to a valid surface.");
        Gamepad=config.Bind("Gameplay","GamepadMode",false,"Use native gamepad gameplay with headset tracking instead of motion-controller gameplay.");
        LeftHanded=config.Bind("Gameplay","LeftHanded",false,"Use the left hand for primary actions and the right wrist for the dashboard. Physical hands and independent grip ownership are preserved.");
        SwapSticks=config.Bind("Gameplay","SwapMotionSticks",false,"Move/sprint/pause on the right stick; turn/scroll/cursor on the left stick. Independent of dominant hand; regular gamepads are unchanged.");
        Seated=config.Bind("Gameplay","SeatedPlay",false,"Calibrate your seated head position to normal game eye height. Use Calibrate Seated Height after changing posture.");
        SeatedHeight=Number("Gameplay","SeatedHeightAdjustment",0,-50,70,"Additional eye and hand height in centimetres while seated play is enabled. Does not alter world scale or controller reach.");
        WristScale=WristDisplaySettings.Bind(config,out bool wristMigrated);
        if(wristMigrated){dirty=true;saveAt=Time.realtimeSinceStartup+.35f;}
        if(SnapAngle.Value!=15&&SnapAngle.Value!=30&&SnapAngle.Value!=45&&SnapAngle.Value!=90)SnapAngle.Value=30;
        file.SettingChanged+=(_,_)=>{dirty=true;saveAt=Time.realtimeSinceStartup+.35f;Changed?.Invoke();};
        Changed+=VrLocomotion.Reset;
    }
    private static ConfigEntry<int> Number(string section,string key,int value,int min,int max,string description)
        =>file.Bind(section,key,value,new ConfigDescription(description,new AcceptableValueRange<int>(min,max)));
    internal static void Tick()
    {
        VrControls.Tick();StereoTrial.UpdateSeatedPlay();
        if(gamepad!=Gamepad.Value){gamepad=Gamepad.Value;MotionInput.ModeChanged();VrGamepadInput.ModeChanged();}
        VrGamepadDevice.Tick();
        StereoTrial.ApplyTrackingScale(100f/WorldScale.Value);
        VrGraphics.Tick();
        VrSettingsMenu.Tick();
        if(dirty&&Time.realtimeSinceStartup>=saveAt)Flush();
    }
    internal static void Flush(){if(file==null)return;file.Save();dirty=false;}
}
