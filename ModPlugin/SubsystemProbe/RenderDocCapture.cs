using System;
using System.Runtime.InteropServices;

namespace IKnowAGuyVR.SubsystemProbe;

// Optional diagnostic, uses the installed RenderDoc app API 1.6 header contract.
// Its capture hook is instrumentation only, never a mod rendering dependency.
internal static class RenderDocCapture
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetApi(int version, out IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TriggerCapture();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    internal static void Trigger()
    {
        IntPtr module = GetModuleHandle("renderdoc.dll");
        if (module == IntPtr.Zero) throw new InvalidOperationException("RenderDoc capture requested without injected instrumentation");
        var get = Marshal.GetDelegateForFunctionPointer<GetApi>(NativeLibrary.GetExport(module, "RENDERDOC_GetAPI"));
        if (get(10600, out var api) != 1 || api == IntPtr.Zero) throw new InvalidOperationException("RenderDoc API 1.6 unavailable");
        string path = Environment.GetEnvironmentVariable("IKAG_XR_CAPTURE_PATH");
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Capture evidence path required");
        Marshal.GetDelegateForFunctionPointer<SetPath>(Marshal.ReadIntPtr(api, 11 * IntPtr.Size))(path);
        // Slot 15 is TriggerCapture in the installed renderdoc_app.h, with unions
        // occupying one pointer each. Capture completes at Present on the render thread.
        Marshal.GetDelegateForFunctionPointer<TriggerCapture>(Marshal.ReadIntPtr(api, 15 * IntPtr.Size))();
    }
}
