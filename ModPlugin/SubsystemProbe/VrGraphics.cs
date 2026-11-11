using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine.XR;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

// Scale the provider's existing engine-owned targets; no intermediate eye RTs.
internal static unsafe class VrGraphics
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void SetScale(IntPtr display,float scale);
    private static IntPtr display;private static SetScale setScale;private static int applied=100;
    internal static int RecommendedWidth{get;private set;}
    internal static int RecommendedHeight{get;private set;}
    internal static int Width{get;private set;}
    internal static int Height{get;private set;}
    internal static int Revision{get;private set;}
    private static int maxWidth,maxHeight,hardwareMaximum=int.MaxValue;private static bool limits;
    internal static int MaximumPercent
    {
        get
        {
            if(!limits)return 200;
            int size=hardwareMaximum;
            float factor=Math.Min((Math.Min(size,maxWidth)&~1)/(float)RecommendedWidth,(Math.Min(size,maxHeight)&~1)/(float)RecommendedHeight);
            return Math.Min(200,Math.Max(50,(int)Math.Floor(factor*100)));
        }
    }
    internal static int ClampPercent(int percent)=>Math.Min(percent,MaximumPercent);
    internal static void RecordLimits(int width,int height,int maximumWidth,int maximumHeight)
    {if(limits&&RecommendedWidth==width&&RecommendedHeight==height&&maxWidth==maximumWidth&&maxHeight==maximumHeight)return;RecommendedWidth=width;RecommendedHeight=height;maxWidth=maximumWidth;maxHeight=maximumHeight;limits=true;Revision++;}
    internal static void Attach(IntPtr pointer)
    {
        var klass=IL2CPP.GetIl2CppClass("UnityEngine.SubsystemsModule.dll","UnityEngine","IntegratedSubsystem");
        var field=IL2CPP.il2cpp_class_get_field_from_name(klass,"m_Ptr");IntPtr native=IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(pointer,field,&native);display=native;
        var address=IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem::set_scaleOfAllRenderTargets_Injected");
        if(native==IntPtr.Zero||address==IntPtr.Zero)throw new InvalidOperationException("Native XR target scaling unavailable");
        setScale=Marshal.GetDelegateForFunctionPointer<SetScale>(address);applied=100;
        hardwareMaximum=SystemInfo.maxTextureSize;Revision++;
    }
    internal static void Observe(int width,int height)
    {
        if(RecommendedWidth==0){RecommendedWidth=width;RecommendedHeight=height;Revision++;}
        if(Width==width&&Height==height)return;Width=width;Height=height;Revision++;
    }
    internal static void Tick()
    {
        if(display==IntPtr.Zero||RecommendedWidth==0||applied==VrSettings.Resolution.Value)return;
        if(VrSettings.Resolution.Value>MaximumPercent)VrSettings.Resolution.Value=MaximumPercent;
        setScale(display,VrSettings.Resolution.Value/100f);
        applied=VrSettings.Resolution.Value;
    }
    internal static int ScaledWidth(int percent)=>(int)Math.Ceiling(RecommendedWidth*(ClampPercent(percent)/100f)/2)*2;
    internal static int ScaledHeight(int percent)=>(int)Math.Ceiling(RecommendedHeight*(ClampPercent(percent)/100f)/2)*2;
}
