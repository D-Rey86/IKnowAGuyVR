using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

internal static unsafe class SrpDisplayConfig
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte GetBool(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBool(IntPtr display, byte value);
    internal static void BeforeStart(IntPtr displayObject, IntPtr baseClass)
    {
        EngineBinaryContract.ValidateBinary();
        IntPtr native = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(displayObject, IL2CPP.il2cpp_class_get_field_from_name(baseClass, "m_Ptr"), &native);
        IntPtr getter = IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem::get_disableLegacyRenderer_Injected");
        IntPtr setter = IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem::set_disableLegacyRenderer_Injected");
        if (native == IntPtr.Zero || getter == IntPtr.Zero || setter == IntPtr.Zero)
            throw new InvalidOperationException("Native SRP display configuration unavailable");
        var get = Marshal.GetDelegateForFunctionPointer<GetBool>(getter);
        var set = Marshal.GetDelegateForFunctionPointer<SetBool>(setter);
        Plugin.Line("SRP_DISPLAY disableLegacyRenderer before=" + get(native));
        set(native, 1);
        Plugin.Line("SRP_DISPLAY disableLegacyRenderer after=" + get(native));
        getter = IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem::get_sRGB_Injected");
        setter = IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem::set_sRGB_Injected");
        if (getter == IntPtr.Zero || setter == IntPtr.Zero) throw new InvalidOperationException("Native display sRGB configuration unavailable");
        get = Marshal.GetDelegateForFunctionPointer<GetBool>(getter);
        set = Marshal.GetDelegateForFunctionPointer<SetBool>(setter);
        byte expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? (byte)1 : (byte)0;
        Plugin.Line($"SRP_DISPLAY sRGB before={get(native)} expected={expected} colorSpace={QualitySettings.activeColorSpace}");
        set(native, expected);
        Plugin.Line("SRP_DISPLAY sRGB after=" + get(native));
    }
}
