using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace IKnowAGuyVR.SubsystemProbe;

// Optional one-shot measurement. Reuses existing GPU textures, allocates CPU data only.
// WaitForCompletion deliberately stalls this diagnostic frame; never a release path.
internal static class TrialReadback
{
    internal static void Capture(NativeEyeResources eyes, Action<string> log)
    {
        byte[] control = Read(Texture2D.whiteTexture, "white-control", log);
        foreach (byte value in control)
            if (value != 255) throw new InvalidOperationException("White texture readback control failed");
        log("READBACK_WHITE_CONTROL_PASS");
        for (int eye = 0; eye < 2; ++eye) Read(eyes.Targets[eye], "eye-" + eye, log);
    }

    internal static byte[] Read(Texture texture, string tag, Action<string> log)
    {
        log($"READBACK_RESOURCE {tag} id={texture.GetInstanceID()} size={texture.width}x{texture.height} format={texture.graphicsFormat} native=0x{texture.GetNativeTexturePtr().ToInt64():X}");
        var request = AsyncGPUReadback.Request(texture, 0, null);
        request.WaitForCompletion();
        log($"READBACK_REQUEST {tag} done={request.done} error={request.hasError} layers={request.layerCount}");
        if (!request.done || request.hasError || request.layerCount != 1)
            throw new InvalidOperationException("GPU readback failed: " + tag);
        int size = request.layerDataSize;
        if (size != checked(texture.width * texture.height * 4))
            throw new InvalidOperationException("Unexpected readback byte layout: " + tag);
        var bytes = new byte[size];
        Marshal.Copy(request.GetDataRaw(0), bytes, 0, size);
        File.WriteAllBytes(Path.Combine(BepInEx.Paths.GameRootPath, "TrialReadback-" + tag + ".rgba"), bytes);
        int nonblack = 0;
        for (int i = 0; i < size; i += 4) if ((bytes[i] | bytes[i + 1] | bytes[i + 2]) != 0) ++nonblack;
        log($"READBACK {tag} size={texture.width}x{texture.height} format={texture.graphicsFormat} native=0x{texture.GetNativeTexturePtr().ToInt64():X} nonblack={nonblack}/{size / 4}");
        return bytes;
    }
}
