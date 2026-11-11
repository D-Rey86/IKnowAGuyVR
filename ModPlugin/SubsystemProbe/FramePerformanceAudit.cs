using System;
using System.IO;
using System.Text.Json;
using UnityEngine;
using Unity.Profiling;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class FramePerformanceAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_PERFORMANCE")=="1";
    private static readonly float[] times=enabled?new float[1200]:null;
    private static int warm,count;
    private static long bytes;private static int gen0,gen1,gen2;
    private static ProfilerRecorder cpu,gc;
    private static bool cpuAvailable,gcAvailable,timingAvailable;
    private static float[] cpuTimes=enabled?new float[1200]:null,gpuTimes=enabled?new float[1200]:null;
    private static int cpuCount,gpuCount;private static long nativeGcBytes;
    private static Il2CppStructArray<FrameTiming> timing;
    private static float[] renderTimes=enabled?new float[1200]:null;private static int renderCount;
    internal static long BeginRender()=>enabled&&warm>=300&&count<1200?System.Diagnostics.Stopwatch.GetTimestamp():0;
    internal static void EndRender(long start)
    {if(start!=0&&renderCount<1200)renderTimes[renderCount++]=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency);}
    private static object Stats(float[] values,int length)
    {
        if(length==0)return null;
        Array.Sort(values,0,length);double sum=0;for(int i=0;i<length;++i)sum+=values[i];
        return new{samples=length,meanMs=sum/length,p50Ms=values[length/2],p95Ms=values[Math.Min(length-1,(int)(length*.95))],p99Ms=values[Math.Min(length-1,(int)(length*.99))]};
    }
    internal static void Tick()
    {
        if(!enabled||count==times.Length||!StereoTrial.GameplayActive)return;
        if(Environment.GetEnvironmentVariable("IKAG_XR_SHOVEL_PERFORMANCE")=="1"&&!ShovelPerfAudit.Ready)return;
        if(Environment.GetEnvironmentVariable("IKAG_XR_PIZZA_SHELF_AUDIT")=="performance"&&!PizzaShelfAudit.PerformanceReady)return;
        if(++warm==1)
        {
            try
            {
                cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread");gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
                cpuAvailable=cpu.Valid&&cpu.IsRunning&&cpu.UnitType.ToString()=="TimeNanoseconds";
                gcAvailable=gc.Valid&&gc.IsRunning&&gc.UnitType.ToString()=="Bytes";
                timingAvailable=FrameTimingManager.IsFeatureEnabled();if(timingAvailable)timing=new Il2CppStructArray<FrameTiming>(1);
                Plugin.Line($"PERFORMANCE_NATIVE_INSTRUMENT cpu={cpuAvailable}/{cpu.UnitType} gc={gcAvailable}/{gc.UnitType} frameTiming={timingAvailable}");
            }
            catch(Exception e){Plugin.Line("PERFORMANCE_NATIVE_UNAVAILABLE "+e);}
        }
        if(timingAvailable)FrameTimingManager.CaptureFrameTimings();
        if(warm<300)return;
        if(count==0){bytes=GC.GetAllocatedBytesForCurrentThread();gen0=GC.CollectionCount(0);gen1=GC.CollectionCount(1);gen2=GC.CollectionCount(2);}
        times[count++]=Time.unscaledDeltaTime*1000;
        if(cpuAvailable){long value=cpu.LastValue;if(value>0)cpuTimes[cpuCount++]=value/1e6f;}
        if(gcAvailable)nativeGcBytes+=gc.LastValue;
        if(timingAvailable&&FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpuTimes[gpuCount++]=(float)timing[0].gpuFrameTime;
        if(count!=times.Length)return;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
        double sum=0;foreach(float t in times)sum+=t;
        double mean=sum/times.Length,variance=0;foreach(float t in times)variance+=(t-mean)*(t-mean);
        Array.Sort(times);
        var result=new {label=Environment.GetEnvironmentVariable("IKAG_XR_PERF_LABEL"),frames=count,warmupFrames=300,meanMs=mean,p50Ms=times[600],p95Ms=times[1140],p99Ms=times[1188],maxMs=times[1199],stdDevMs=Math.Sqrt(variance/times.Length),managedBytes=allocated,managedBytesPerFrame=(double)allocated/count,nativeCpu=Stats(cpuTimes,cpuCount),nativeGpu=Stats(gpuTimes,gpuCount),nativeGcBytesPerFrame=gcAvailable?(double?)nativeGcBytes/count:null,collections=new[]{GC.CollectionCount(0)-gen0,GC.CollectionCount(1)-gen1,GC.CollectionCount(2)-gen2},limits="Simulator, one NVIDIA D3D11 system, same save/stationary view. Native profiler and frame timing reported only when available; shared audit instrumentation is included. No physical-compositor timing."};
        File.WriteAllText(Path.Combine(BepInEx.Paths.GameRootPath,"PerformanceAudit.json"),JsonSerializer.Serialize(new{frameMetrics=result,stereoCallbackCpu=Stats(renderTimes,renderCount),renderTimingScope="Elapsed CPU across body pose, both eye renders and restoration; excludes other game/update work."},new JsonSerializerOptions{WriteIndented=true}));
        Plugin.Line("PERFORMANCE_AUDIT_COMPLETE");
        if(cpu.Valid)cpu.Dispose();if(gc.Valid)gc.Dispose();
    }
}
