using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.XR;

namespace IKnowAGuyVR.SubsystemProbe;

// Read the existing Unity XR input subsystem. No device enumeration, actions,
// controller input or pose conversion. Cache the HMD ID and native calls once.
internal sealed class HeadPoseReader : IDisposable
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct NativeSpan
    {
        [FieldOffset(0)] internal IntPtr Begin;
        [FieldOffset(8)] internal int Length;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong DeviceId(XRNode node);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte DeviceValid(ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Feature(ulong id, ref NativeSpan usage, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Position(XRNode node, out Vector3 value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Rotation(XRNode node, out Quaternion value);
    private readonly DeviceValid valid;
    private readonly DeviceId deviceId;
    private readonly Feature feature;
    private readonly Position position;
    private readonly Rotation rotation;
    private NativeSpan trackingUsage;
    private float retryAt;
    internal ulong Id { get; private set; }
    internal bool DeviceIsValid { get; private set; }
    internal bool HasTrackingState { get; private set; }

    internal HeadPoseReader()
    {
        EngineBinaryContract.ValidateBinary();
        deviceId = Bind<DeviceId>("UnityEngine.XR.InputTracking::GetDeviceIdAtXRNode");
        valid = Bind<DeviceValid>("UnityEngine.XR.InputDevices::IsDeviceValid");
        feature = Bind<Feature>("UnityEngine.XR.InputDevices::TryGetFeatureValue_UInt32_Injected");
        position = Bind<Position>("UnityEngine.XR.InputTracking::GetLocalPosition_Injected");
        rotation = Bind<Rotation>("UnityEngine.XR.InputTracking::GetLocalRotation_Injected");
        FindHead();
        // CommonUsages fields are stripped. Official XRInput.bindings.cs defines
        // this name; the target ManagedSpanWrapper ABI is pointer@0,length@8.
        // Bypass the measured broken ReadOnlySpan.GetPinnableReference wrapper.
        trackingUsage = new NativeSpan { Begin = Marshal.StringToHGlobalUni("TrackingState"), Length = 13 };
    }

    internal bool TryRead(out Vector3 value, out Quaternion orientation, out uint trackingState)
    {
        DeviceIsValid = valid(Id) != 0;
        if (!DeviceIsValid && Time.unscaledTime >= retryAt)
        {
            FindHead();
            retryAt = Time.unscaledTime + 1;
            DeviceIsValid = valid(Id) != 0;
        }
        if (!DeviceIsValid)
        { value = default; orientation = default; trackingState = 0; HasTrackingState = false; return false; }
        HasTrackingState = feature(Id, ref trackingUsage, out trackingState) != 0;
        position(XRNode.CenterEye, out value);
        rotation(XRNode.CenterEye, out orientation);
        return DeviceIsValid && HasTrackingState && (trackingState & 3) == 3;
    }

    private void FindHead()
    {
        Id = deviceId(XRNode.Head);
        if (valid(Id) == 0) Id = deviceId(XRNode.CenterEye);
    }

    public void Dispose()
    {
        if (trackingUsage.Begin != IntPtr.Zero) Marshal.FreeHGlobal(trackingUsage.Begin);
        trackingUsage.Begin = IntPtr.Zero;
    }
    private static T Bind<T>(string name) where T : Delegate
    {
        IntPtr code = IL2CPP.il2cpp_resolve_icall(name);
        if (code == IntPtr.Zero) throw new MissingMethodException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(code);
    }
}
