using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OccaSoftware.Buto.Runtime;
using UnityEngine;
using HistoryMap=Il2CppSystem.Collections.Generic.Dictionary<UnityEngine.Camera,OccaSoftware.Buto.Runtime.RenderFogPass.RTData>;
namespace IKnowAGuyVR.SubsystemProbe;

// Buto keys temporal volumes by Camera. Our existing camera renders a monitor
// and two runtime eye views, so each view needs its own native history entry.
// Keep it selected until the whole graph has executed, then restore the native
// monitor entry. No additional camera, fog pass, or copy is introduced.
internal static class ButoEyeHistory
{
    private sealed class Bank
    {
        internal RenderFogPass Pass;internal Camera Camera;
        internal HistoryMap Map;
        internal RenderFogPass.RTData Left,Right,Monitor;
        internal RenderTexture PreviousLight,PreviousMedia;
        internal bool Selected,HadMonitor;
    }
    private static readonly Dictionary<(IntPtr,IntPtr),Bank> banks=new();
    private static readonly List<Bank> selected=new(2);
    private static readonly List<(IntPtr,IntPtr)> removed=new(2);
    private static readonly List<Camera> retiredCameras=new(2);
    private static bool installed;private static int eye=-1;
    internal static void Install()
    {
        if(installed)return;
        var h=new Harmony("iknowaguyvr.buto-eye-history");
        h.Patch(typeof(RenderFogPass).GetMethod("SetupMaterials"),prefix:new HarmonyMethod(typeof(ButoEyeHistory).GetMethod(nameof(Select),BindingFlags.NonPublic|BindingFlags.Static)),postfix:new HarmonyMethod(typeof(ButoEyeHistory).GetMethod(nameof(AfterSetup),BindingFlags.NonPublic|BindingFlags.Static)));
        h.Patch(typeof(RenderFogPass).GetMethod("Dispose"),prefix:new HarmonyMethod(typeof(ButoEyeHistory).GetMethod(nameof(DisposePass),BindingFlags.NonPublic|BindingFlags.Static)));
        installed=true;
    }
    internal static void BeginEye(int index){eye=index;}
    private static void Select(RenderFogPass __instance,Camera camera,out Bank __state)
    {
        __state=null;if(camera==null)return;
        var key=(__instance.Pointer,camera.Pointer);
        if(!banks.TryGetValue(key,out var b))
        {
            if(eye<0)return;
            var histories=__instance.kvps;
            RetireDestroyedCameras(histories);
            b=new Bank{Pass=__instance,Camera=camera,Map=histories};banks.Add(key,b);
        }
        var map=b.Map;
        if(eye>=0&&!b.Selected)
        {
            b.HadMonitor=map.TryGetValue(camera,out var monitor);b.Monitor=monitor;
            var history=eye==0?b.Left:b.Right;
            if(history==null)map.Remove(camera);else map[camera]=history;
            b.Selected=true;selected.Add(b);
        }
        if(map.TryGetValue(camera,out var before)){b.PreviousLight=before.lightingDataRtPrev;b.PreviousMedia=before.mediaDataRtPrev;}
        else {b.PreviousLight=null;b.PreviousMedia=null;}
        __state=b;
    }
    private static void AfterSetup(Bank __state)
    {
        var b=__state;if(b==null)return;
        if(b.Map.TryGetValue(b.Camera,out var after))
        {
            RetireReplacement(b.Camera,b.PreviousLight,after.lightingDataRtPrev);
            RetireReplacement(b.Camera,b.PreviousMedia,after.mediaDataRtPrev);
        }
        b.PreviousLight=null;b.PreviousMedia=null;
    }
    private static void RetireReplacement(Camera camera,RenderTexture previous,RenderTexture current)
    {
        if(previous==null||current==null||previous.Pointer==current.Pointer)return;
        // Native SetupMaterials releases the old GPU backing but omits Destroy
        // when a descriptor changes (e.g. native save-thumbnail capture). Retire
        // that exact replaced object; Unity defers destruction until frame end.
#if IKAG_DIAGNOSTICS
        RuntimeIssueAudit.Replaced(camera,previous,current);
#endif
        UnityEngine.Object.Destroy(previous);
    }
    internal static void EndEye()
    {
        for(int i=0;i<selected.Count;++i)
        {
            var b=selected[i];var map=b.Map;map.TryGetValue(b.Camera,out var history);
            if(eye==0)b.Left=history;else b.Right=history;
            if(b.HadMonitor)map[b.Camera]=b.Monitor;else map.Remove(b.Camera);
            b.Selected=false;
        }
        selected.Clear();eye=-1;
    }
    private static void DisposePass(RenderFogPass __instance)
    {
        // Renderer/quality replacement invalidates the native dictionary even
        // when the gameplay camera survives. Dispose only our detached banks.
        foreach(var entry in banks)if(entry.Key.Item1==__instance.Pointer)
        {Destroy(entry.Value.Left);Destroy(entry.Value.Right);removed.Add(entry.Key);}
        for(int i=0;i<removed.Count;++i)banks.Remove(removed[i]);removed.Clear();
    }
    private static void Destroy(RenderFogPass.RTData history)
    {
        if(history==null)return;
        var light=history.lightingDataRtPrev;var media=history.mediaDataRtPrev;
        if(light!=null){light.Release();UnityEngine.Object.Destroy(light);}
        if(media!=null){media.Release();UnityEngine.Object.Destroy(media);}
        history.lightingDataRtPrev=null;history.mediaDataRtPrev=null;
    }
    private static void RetireDestroyedCameras(HistoryMap histories)
    {
        // A new pass/camera binding is the invalidation boundary. Buto retains
        // native monitor histories for cameras destroyed by scene transitions.
        // Sweep that cache here, never on the healthy per-frame selection path.
        // Unity's null comparison detects a destroyed camera; retain its wrapper
        // as the dictionary key until removal. Live camera histories stay owned
        // by Buto, including a camera whose stereo binding is merely replaced.
        try
        {
            foreach(var entry in histories)if(entry.Key==null)retiredCameras.Add(entry.Key);
            foreach(var camera in retiredCameras)
                if(histories.TryGetValue(camera,out var history)&&histories.Remove(camera))Destroy(history);
        }
        finally { retiredCameras.Clear(); }
    }
    internal static void Clear()
    {
        EndEye();
        foreach(var b in banks.Values){Destroy(b.Left);Destroy(b.Right);}
        banks.Clear();
    }
}
