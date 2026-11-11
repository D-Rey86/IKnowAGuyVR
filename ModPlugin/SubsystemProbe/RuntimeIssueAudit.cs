using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Profiling;
using HarmonyLib;
using System.Reflection;
using OccaSoftware.Buto.Runtime;
namespace IKnowAGuyVR.SubsystemProbe;

// Bounded read-only resource census. No shipping references or per-frame scans.
internal static class RuntimeIssueAudit
{
    private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_RUNTIME_ISSUE_AUDIT");
    private static int sample;private static float next=25;
    private static bool installed;private static int traces;
    internal static void Replaced(Camera camera,RenderTexture previous,RenderTexture current)
    {
        if(mode!="day"||traces++>=16)return;
        Plugin.Line($"FOG_REPLACEMENT camera={camera.name}:{camera.GetInstanceID()} target={RT(camera.targetTexture)} previous={RT(previous)} current={RT(current)}");
    }
    private sealed class Before { internal RenderTexture light,media;internal IntPtr data; }
    private static void Prefix(RenderFogPass __instance,Camera camera,out Before __state)
    {
        __state=null;if(traces>=24||camera==null||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<22)return;
        if(__instance.kvps.TryGetValue(camera,out var data))__state=new Before{light=data.lightingDataRtPrev,media=data.mediaDataRtPrev,data=data.Pointer};
        else __state=new Before();
    }
    private static string RT(RenderTexture t)=>t==null?"null":$"{t.GetInstanceID()}:{t.name}:{t.width}x{t.height}x{t.volumeDepth}:{t.dimension}:created={t.IsCreated()}";
    private static void Postfix(RenderFogPass __instance,Camera camera,Before __state)
    {
        if(__state==null)return;
        __instance.kvps.TryGetValue(camera,out var d);
        Plugin.Line($"FOG_HISTORY {traces++} frame={Time.frameCount} camera={camera.name}:{camera.GetInstanceID()} target={RT(camera.targetTexture)} data={__state.data}->{d?.Pointer} light={RT(__state.light)}->{RT(d?.lightingDataRtPrev)} media={RT(__state.media)}->{RT(d?.mediaDataRtPrev)}");
    }
    internal static void Tick()
    {
        if(mode=="resources"&&!installed){installed=true;var h=new Harmony("ikag.runtime-issue-audit");h.Patch(typeof(RenderFogPass).GetMethod("SetupMaterials"),new HarmonyMethod(typeof(RuntimeIssueAudit).GetMethod(nameof(Prefix),BindingFlags.Static|BindingFlags.NonPublic)),new HarmonyMethod(typeof(RuntimeIssueAudit).GetMethod(nameof(Postfix),BindingFlags.Static|BindingFlags.NonPublic)));UiInputAudit.Dump("OccaSoftware.Buto.Runtime.dll","OccaSoftware.Buto.Runtime","RenderFogPass","ConfigurePass","CleanupDictionary","Dispose","SetupData","RecordRenderGraph","ExecutePass","SetupMaterials","RegisterStackComponent");}
        if(mode!="resources"||sample>=7||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            var textures=Resources.FindObjectsOfTypeAll<RenderTexture>();var names=new Dictionary<string,int>();
            foreach(var t in textures){string key=t.name+"/"+t.width+"x"+t.height;names.TryGetValue(key,out var n);names[key]=n+1;}
            var data=new{sample,time=Time.realtimeSinceStartup,frame=Time.frameCount,renderTextures=textures.Length,
                materials=Resources.FindObjectsOfTypeAll<Material>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,
                textures2D=Resources.FindObjectsOfTypeAll<Texture2D>().Length,driverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver(),
                textureNames=names.OrderByDescending(x=>x.Value).Take(16).ToArray()};
            var json=JsonSerializer.Serialize(data);Plugin.Line("RESOURCE_CENSUS "+json);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"resources-"+sample+".json"),json);
            if(++sample==7)Plugin.Line("RESOURCE_CENSUS_COMPLETE");next=Time.realtimeSinceStartup+10;
        }
        catch(Exception e){Plugin.Line("RESOURCE_CENSUS_FAILED "+e);sample=7;}
    }
}
