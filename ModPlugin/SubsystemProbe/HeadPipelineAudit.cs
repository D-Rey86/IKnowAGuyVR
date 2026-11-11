using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Bounded read-only tracking probe; never primes the shared head cache.
// Runs before the original camera and immediately after native eye queries.
// No per-frame file reads. This is not a frame-time measurement. Excluded normally.
internal static class HeadPipelineAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_HEAD_PIPELINE")=="1";
    private static readonly BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
    private static readonly FieldInfo cacheFrame=typeof(StereoTrial).GetField("headFrame",flags),cacheRotation=typeof(StereoTrial).GetField("cachedHeadRotation",flags),cachePosition=typeof(StereoTrial).GetField("cachedHeadPosition",flags),cacheValid=typeof(StereoTrial).GetField("cachedHeadValid",flags);
    private static List<object> rows;
    private static HeadPoseReader reader;
    private static int beginFrame=-1,eyeFrame=-1,errors,beginCount,eyeCount,currentBegin,currentEyes;
    private static float start=-1,maxBeginAngle,maxEyeAngle;
    private static bool done;
    internal static void Install()
    {
        if(!enabled)return;
        new Harmony("ikag-head-pipeline-audit").Patch(typeof(NativeEyeResources).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic),postfix:new HarmonyMethod(typeof(HeadPipelineAudit).GetMethod(nameof(Eyes),flags)));
    }
    private static void Eyes(NativeEyeResources __instance,Camera camera)=>Sample(camera,true,__instance.Parameters[0].View);
    internal static void Sample(Camera camera)=>Sample(camera,false,default);
    private static void Sample(Camera camera,bool eyes,Matrix4x4 nativeView)
    {
        if(!enabled||done||!StereoTrial.GameplayActive||camera==null||camera.name!="MainCam")return;
        int frame=Time.frameCount;
        if(eyes){if(eyeFrame==frame)return;eyeFrame=frame;}else{if(beginFrame==frame)return;beginFrame=frame;}
        if(start<0){start=Time.realtimeSinceStartup+2;return;}
        if(Time.realtimeSinceStartup<start)return;
        try
        {
            if(reader==null)
            {
                reader=new HeadPoseReader();rows=new List<object>(370);
                string directory=Environment.GetEnvironmentVariable("XRSIM_DIR");
                // This command is only for a simulator run. A physical run has
                // no simulator state file and observes the actual headset.
                if(directory!=null&&File.Exists(Path.Combine(directory,"state.json")))File.WriteAllText(Path.Combine(directory,"command.txt"),"head orbit 30 3000\n");
            }
            bool valid=reader.TryRead(out var p,out var q,out var tracking);
            int cachedFrame=(int)cacheFrame.GetValue(null);
            var cq=(Quaternion)cacheRotation.GetValue(null);var cp=(Vector3)cachePosition.GetValue(null);bool cv=(bool)cacheValid.GetValue(null);
            float angle=Quaternion.Angle(cq,q);
            if(eyes)++eyeCount;else ++beginCount;
            if(cachedFrame==frame&&cv&&valid)
            {if(eyes){++currentEyes;maxEyeAngle=Math.Max(maxEyeAngle,angle);}else{++currentBegin;maxBeginAngle=Math.Max(maxBeginAngle,angle);}}
            float? eyeCenterAngle=null;float[] eyePosition=null;
            if(eyes)
            {
                var world=nativeView.inverse;
                var rotation=Quaternion.LookRotation(new Vector3(-world.m02,-world.m12,-world.m22),new Vector3(world.m01,world.m11,world.m21));
                eyeCenterAngle=Quaternion.Angle(rotation,camera.transform.rotation);eyePosition=new[]{world.m03,world.m13,world.m23};
            }
            var center=camera.transform;
            rows.Add(new{stage=eyes?"native-eyes-ready":"original-camera-begin",unityFrame=frame,cachedFrame,headId=reader.Id,valid,cachedValid=cv,tracking,cacheFreshAngle=angle,position=new[]{p.x,p.y,p.z},cachedPosition=new[]{cp.x,cp.y,cp.z},rotation=new[]{q.x,q.y,q.z,q.w},cachedRotation=new[]{cq.x,cq.y,cq.z,cq.w},cameraPosition=new[]{center.position.x,center.position.y,center.position.z},cameraRotation=new[]{center.rotation.x,center.rotation.y,center.rotation.z,center.rotation.w},eyeCenterAngle,eyePosition});
            if(eyeCount<180)return;
            File.WriteAllText(Path.Combine(BepInEx.Paths.GameRootPath,"HeadPipelineAudit.json"),JsonSerializer.Serialize(new{probeVersion=2,label=Environment.GetEnvironmentVariable("IKAG_XR_PIPELINE_LABEL"),beginCount,eyeCount,currentBegin,currentEyes,maxBeginAngle,maxEyeAngle,errors,rows},new JsonSerializerOptions{WriteIndented=true}));
            Plugin.Line($"HEAD_PIPELINE_AUDIT_COMPLETE version=2 begin={beginCount} eyes={eyeCount} beginFreshAngle={maxBeginAngle:R} eyeFreshAngle={maxEyeAngle:R} skipped={errors}");
            reader.Dispose();reader=null;done=true;
        }
        catch(Exception e)
        {if(++errors>20){Plugin.Line("HEAD_PIPELINE_AUDIT_FAILED "+e);reader?.Dispose();reader=null;done=true;}}
    }
}
