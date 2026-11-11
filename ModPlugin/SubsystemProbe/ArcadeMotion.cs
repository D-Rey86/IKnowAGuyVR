using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ArcadeMotion
{
    private static IntPtr owner;
    private static bool armed;
    internal static bool Active { get {var a=ArcadeMiniGameManager.LocalInstance;return a!=null&&a.IsGameActive;} }
    internal static void Install()
    {
        var harmony=new Harmony("iknowaguyvr.arcade-input");
        harmony.Patch(typeof(RacingMiniGame).GetMethod("StartGame"),prefix:new HarmonyMethod(typeof(ArcadeMotion).GetMethod(nameof(Reset),BindingFlags.Static|BindingFlags.NonPublic)));
        // UpdateInput is inlined into UpdateRunning in the current IL2CPP player.
        harmony.Patch(typeof(RacingMiniGame).GetMethod("UpdateRunning"),prefix:new HarmonyMethod(typeof(ArcadeMotion).GetMethod(nameof(Race),BindingFlags.Static|BindingFlags.NonPublic)));
    }

    private static void Reset(){owner=IntPtr.Zero;armed=false;}
    private static void Race(RacingMiniGame __instance)
    {
        var a=ArcadeMiniGameManager.LocalInstance;
        var hud=HUDManager.LocalInstance;
        if(!MotionInput.Enabled||!Probe.Focused||a==null||!a.IsGameActive||a.racingGame==null||a.racingGame.Pointer!=__instance.Pointer||!__instance.IsRunning||(hud!=null&&hud.IsEscapeMenuOpen)) {armed=false;owner=IntPtr.Zero;return;}
        ControllerTransport.Sample();var hand=VrControls.Move;
        if(!hand.Tracked){armed=false;return;}
        if(owner!=__instance.Pointer){owner=__instance.Pointer;armed=false;}
        float x=hand.Stick.x;
        if(Math.Abs(x)<=.25f){armed=true;return;}
        if(!armed||Math.Abs(x)<.65f)return;
        armed=false;
        if(__instance.laneSwitching)return;
        int lane=__instance.playerLane+(x>0?1:-1);
        if(__instance.laneCentersX!=null&&lane>=0&&lane<__instance.laneCentersX.Length)__instance.BeginLaneSwitch(lane);
    }
}
