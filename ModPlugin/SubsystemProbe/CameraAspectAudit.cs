using System;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

internal static class CameraAspectAudit
{
    internal static void Run()
    {
        EngineBinaryContract.ValidateBinary();
        using var log = new StreamWriter(Path.Combine(BepInEx.Paths.GameRootPath, "CameraAspectAudit.log"), false);
        long imageBase = 0;
        foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if (module.ModuleName == "UnityPlayer.dll") imageBase = module.BaseAddress.ToInt64();
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No camera");
        IntPtr native = UnityEngine.Object.MarshalledUnityObject.Marshal(camera);
        log.WriteLine($"CAMERA native=0x{native.ToInt64():X} aspect={camera.aspect:R} screen={Screen.width}x{Screen.height} pixelRect={camera.pixelRect} projectionMode={camera.projectionMatrixMode}");
        byte[] state = new byte[4096];
        Marshal.Copy(native, state, 0, state.Length);
        File.WriteAllBytes(Path.Combine(BepInEx.Paths.GameRootPath, "CameraAspectAudit.bin"), state);
        foreach (string name in new[] { "get_aspect_Injected", "set_aspect_Injected", "ResetAspect_Injected", "set_targetTexture_Injected", "get_pixelRect_Injected", "ResetProjectionMatrix_Injected", "get_stereoTargetEyeInternal_Injected", "set_stereoTargetEyeInternal_Injected", "get_projectionMatrix_Injected", "set_projectionMatrix_Injected", "get_fieldOfView_Injected", "set_fieldOfView_Injected" })
        {
            IntPtr address = IL2CPP.il2cpp_resolve_icall("UnityEngine.Camera::" + name);
            if (address == IntPtr.Zero) { log.WriteLine(name + " absent"); continue; }
            byte[] bytes = new byte[192];
            Marshal.Copy(address, bytes, 0, bytes.Length);
            log.WriteLine($"{name} RVA=0x{address.ToInt64() - imageBase:X} bytes={Convert.ToHexString(bytes)}");
        }
    }
}
