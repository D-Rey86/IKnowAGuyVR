using System;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering;

namespace IKnowAGuyVR.SubsystemProbe;

// One-shot read-only audit of the actual native targets and remaining renderer code.
internal static unsafe class RendererAudit
{
#if IKAG_DIAGNOSTICS
    private static Il2CppSystem.Action<ScriptableRenderContext, Il2CppSystem.Collections.Generic.List<Camera>> contextCallback;
    private static int contextObservations;
    private static string contextLog;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParameterCount(ref XRDisplaySubsystem.XRRenderPass pass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Parameter(ref XRDisplaySubsystem.XRRenderPass pass, IntPtr camera, int index, ref NativeParameter parameter);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PassCount(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte GetPass(IntPtr display, int index, out XRDisplaySubsystem.XRRenderPass pass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetTexture(IntPtr display, int index);
#endif
    // Shipping UnityPlayer GetRenderParameter_Injected at RVA 0x282CD0 reads and
    // writes precisely these offsets. Mesh slots carry native IL2CPP GC handles.
    [StructLayout(LayoutKind.Explicit, Size = 232)]
    internal struct NativeParameter
    {
        [FieldOffset(0)] public Matrix4x4 View;
        [FieldOffset(64)] public Matrix4x4 Projection;
        [FieldOffset(128)] public Rect Viewport;
        [FieldOffset(144)] public IntPtr OcclusionMeshHandle;
        [FieldOffset(152)] public IntPtr VisibleMeshHandle;
        [FieldOffset(160)] public int Slice;
        [FieldOffset(164)] public Matrix4x4 PreviousView;
        [FieldOffset(228)] public byte PreviousValid;
    }
#if IKAG_DIAGNOSTICS
    private static T Native<T>(string name) where T : Delegate
    {
        IntPtr address = IL2CPP.il2cpp_resolve_icall(name);
        if (address == IntPtr.Zero) throw new MissingMethodException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    internal static void Run(IntPtr displayObject)
    {
        string path = Path.Combine(BepInEx.Paths.GameRootPath, "RendererAudit.log");
        using var log = new StreamWriter(path, false);
        IntPtr imageBase = System.Diagnostics.Process.GetCurrentProcess().MainModule.BaseAddress;
        foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if (module.ModuleName == "GameAssembly.dll") { imageBase = module.BaseAddress; break; }
        foreach (var item in new[] {
            ("Unity.RenderPipelines.Core.Runtime.dll", "UnityEngine.Experimental.Rendering", "XRPass"),
            ("Unity.RenderPipelines.Core.Runtime.dll", "UnityEngine.Experimental.Rendering", "XRLayout"),
            ("Unity.RenderPipelines.Universal.Runtime.dll", "UnityEngine.Rendering.Universal", "UniversalRenderPipeline"),
            ("Unity.RenderPipelines.Universal.Runtime.dll", "UnityEngine.Rendering.Universal", "UniversalCameraData"),
            ("Unity.RenderPipelines.Universal.Runtime.dll", "UnityEngine.Rendering.Universal", "XRPassUniversal") })
        {
            IntPtr klass = IL2CPP.GetIl2CppClass(item.Item1, item.Item2, item.Item3);
            log.WriteLine($"CLASS {item.Item1} {item.Item3} present={klass != IntPtr.Zero}");
            if (klass == IntPtr.Zero) continue;
            IntPtr iterator = IntPtr.Zero, method;
            while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iterator)) != IntPtr.Zero)
            {
                string name = IL2CPP.il2cpp_method_get_name_(method);
                if (!(name.Contains("Render") || name.Contains("Camera") || name.Contains("View") || name.Contains("enabled") || name.Contains("Culling") || name.Contains("Create") || name.Contains("Pass") || name.Contains("Update"))) continue;
                IntPtr address = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer;
                if (address == IntPtr.Zero) continue;
                byte[] code = new byte[32];
                Marshal.Copy(address, code, 0, code.Length);
                log.WriteLine($"METHOD {item.Item3}.{name} argc={IL2CPP.il2cpp_method_get_param_count(method)} RVA=0x{address.ToInt64() - imageBase.ToInt64():X} bytes={Convert.ToHexString(code)}");
            }
        }
        log.Flush();
        // This wraps the registry's existing IL2CPP object, never a native subsystem handle.
        IntPtr baseClass = IL2CPP.GetIl2CppClass("UnityEngine.SubsystemsModule.dll", "UnityEngine", "IntegratedSubsystem");
        IntPtr nativeDisplay = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(displayObject, IL2CPP.il2cpp_class_get_field_from_name(baseClass, "m_Ptr"), &nativeDisplay);
        var passCountFn = Native<PassCount>("UnityEngine.XR.XRDisplaySubsystem::GetRenderPassCount_Injected");
        var getPass = Native<GetPass>("UnityEngine.XR.XRDisplaySubsystem::Internal_TryGetRenderPass_Injected");
        var getTexture = Native<GetTexture>("UnityEngine.XR.XRDisplaySubsystem::GetRenderTextureForRenderPass_Injected");
        var countParameters = Native<ParameterCount>("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameterCount");
        var getParameter = Native<Parameter>("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameter_Injected");
        IntPtr getParameterAddress = IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameter_Injected");
        foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if (module.ModuleName == "UnityPlayer.dll") log.WriteLine($"GET_RENDER_PARAMETER_UNITYPLAYER_RVA=0x{getParameterAddress.ToInt64() - module.BaseAddress.ToInt64():X}");
        int passCount = passCountFn(nativeDisplay);
        log.WriteLine("NATIVE_RENDER_PASS_COUNT " + passCount);
        Camera camera = Camera.main;
        if (camera == null) { log.WriteLine("NO_MAIN_CAMERA"); return; }
        log.WriteLine($"CAMERA {camera.name} pos={camera.transform.position} rot={camera.transform.rotation} near={camera.nearClipPlane} far={camera.farClipPlane} projectionMode={camera.projectionMatrixMode}");
        for (int i = 0; i < passCount; ++i)
        {
            if (getPass(nativeDisplay, i, out var pass) == 0) throw new InvalidOperationException("Native pass not available");
            IntPtr textureHandle = getTexture(nativeDisplay, i);
            var target = textureHandle == IntPtr.Zero ? null : new Il2CppSystem.Runtime.InteropServices.GCHandle { handle = textureHandle }.Target;
            RenderTexture texture = target == null ? null : new RenderTexture(target.Pointer);
            int parameterCount = countParameters(ref pass);
            log.WriteLine($"PASS {i} desc={pass.renderTargetDesc.width}x{pass.renderTargetDesc.height} samples={pass.renderTargetDesc.msaaSamples} dimension={pass.renderTargetDesc.dimension} depth={pass.renderTargetDesc.volumeDepth} cull={pass.cullingPassIndex} paramCount={parameterCount} targetExists={texture != null}");
            if (texture != null) log.WriteLine($"TEXTURE {i} id={texture.GetInstanceID()} size={texture.width}x{texture.height} created={texture.IsCreated()} samples={texture.antiAliasing} dimension={texture.dimension}");
            for (int j = 0; j < parameterCount; ++j)
            {
                NativeParameter parameter = default;
                getParameter(ref pass, UnityEngine.Object.MarshalledUnityObject.Marshal(camera), j, ref parameter);
                log.WriteLine($"PARAM {i}/{j} viewport={parameter.Viewport} slice={parameter.Slice} meshHandles={parameter.OcclusionMeshHandle}/{parameter.VisibleMeshHandle}");
                log.WriteLine("VIEW " + parameter.View.ToString("R"));
                log.WriteLine("PROJECTION " + parameter.Projection.ToString("R"));
            }
        }
        contextLog = Path.Combine(BepInEx.Paths.GameRootPath, "RenderContextAudit.log");
        File.WriteAllText(contextLog, "Read-only endContextRendering observations\n");
        contextCallback = (Action<ScriptableRenderContext, Il2CppSystem.Collections.Generic.List<Camera>>)OnContext;
        RenderPipelineManager.add_endContextRendering(contextCallback);
    }

    private static void OnContext(ScriptableRenderContext context, Il2CppSystem.Collections.Generic.List<Camera> cameras)
    {
        try
        {
            File.AppendAllText(contextLog, $"frame={Time.frameCount} cameras={cameras.Count} contextPresent={*(IntPtr*)(&context) != IntPtr.Zero}\n");
            if (++contextObservations == 3)
            {
                RenderPipelineManager.remove_endContextRendering(contextCallback);
                contextCallback = null;
                File.AppendAllText(contextLog, "CALLBACK_REMOVED\n");
            }
        }
        catch (Exception e)
        {
            try { RenderPipelineManager.remove_endContextRendering(contextCallback); } catch { }
            Plugin.Line("CONTEXT_AUDIT_FAILED " + e);
        }
    }
#endif
}
