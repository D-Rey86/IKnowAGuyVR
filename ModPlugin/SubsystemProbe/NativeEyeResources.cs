using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using UnityEngine.XR;

namespace IKnowAGuyVR.SubsystemProbe;

// The same measured x64 binding contract as RendererAudit. No native graphics hook.
internal sealed unsafe class NativeEyeResources
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PassCount(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte GetPass(IntPtr display, int index, out XRDisplaySubsystem.XRRenderPass pass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetTexture(IntPtr display, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ParameterCount(ref XRDisplaySubsystem.XRRenderPass pass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetParameter(ref XRDisplaySubsystem.XRRenderPass pass, IntPtr camera, int index, ref RendererAudit.NativeParameter parameter);
    private readonly IntPtr display;
    private readonly PassCount passCount;
    private readonly GetPass getPass;
    private readonly GetTexture getTexture;
    private readonly ParameterCount parameterCount;
    private readonly GetParameter getParameter;
    internal readonly RendererAudit.NativeParameter[] Parameters = new RendererAudit.NativeParameter[2];
    internal readonly RenderTexture[] Targets = new RenderTexture[2];
    private readonly XRDisplaySubsystem.XRRenderPass[] passes = new XRDisplaySubsystem.XRRenderPass[2];
    internal readonly UnityEngine.Rendering.RenderTargetIdentifier[] RenderTargets = new UnityEngine.Rendering.RenderTargetIdentifier[2];

    internal NativeEyeResources(IntPtr displayObject)
    {
        IntPtr klass = IL2CPP.GetIl2CppClass("UnityEngine.SubsystemsModule.dll", "UnityEngine", "IntegratedSubsystem");
        IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, "m_Ptr");
        if (field == IntPtr.Zero) throw new MissingFieldException("IntegratedSubsystem.m_Ptr");
        IntPtr native = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(displayObject, field, &native);
        if (native == IntPtr.Zero) throw new InvalidOperationException("Null native display");
        display = native;
        passCount = Bind<PassCount>("UnityEngine.XR.XRDisplaySubsystem::GetRenderPassCount_Injected");
        getPass = Bind<GetPass>("UnityEngine.XR.XRDisplaySubsystem::Internal_TryGetRenderPass_Injected");
        getTexture = Bind<GetTexture>("UnityEngine.XR.XRDisplaySubsystem::GetRenderTextureForRenderPass_Injected");
        parameterCount = Bind<ParameterCount>("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameterCount");
        getParameter = Bind<GetParameter>("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameter_Injected");
    }

    // Fetch BOTH parameters before moving the scene camera: the native result is camera-relative.
    internal bool TryRefresh(Camera camera)
    {
        int count = passCount(display);
        // Unity can expose no passes for a frame while tracking is unavailable.
        // Do not destroy the binding; no parameter/target is used until BOTH return.
        if (count == 0) return false;
        if (count != 2) throw new InvalidOperationException("Trial requires two native multipass targets");
        IntPtr nativeCamera = UnityEngine.Object.MarshalledUnityObject.Marshal(camera);
        for (int eye = 0; eye < 2; ++eye)
        {
            if (getPass(display, eye, out var pass) == 0 || parameterCount(ref pass) != 1)
                throw new InvalidOperationException("Trial requires one parameter per eye pass");
            if (pass.renderTargetDesc.dimension != UnityEngine.Rendering.TextureDimension.Tex2D || pass.renderTargetDesc.volumeDepth != 1)
                throw new InvalidOperationException("Unsupported target layout in bounded trial");
            RenderTargets[eye] = pass.renderTarget;
            passes[eye] = pass;
            // Unity's matching UnmarshalUnityObject reads this engine-owned GCHandle.Target.
            // Refresh object identity because the XR display may rotate its native resources.
            IntPtr handle = getTexture(display, eye);
            var target = handle == IntPtr.Zero ? null : new Il2CppSystem.Runtime.InteropServices.GCHandle { handle = handle }.Target;
            if (target == null) throw new InvalidOperationException("Missing native eye target");
            if (Targets[eye] == null || Targets[eye].Pointer != target.Pointer)
                Targets[eye] = Il2CppObjectPool.Get<RenderTexture>(target.Pointer);
            if (!Targets[eye].IsCreated() || Targets[eye].width != pass.renderTargetDesc.width || Targets[eye].height != pass.renderTargetDesc.height)
                throw new InvalidOperationException("Native target descriptor mismatch");
            Parameters[eye] = default;
            getParameter(ref pass, nativeCamera, 0, ref Parameters[eye]);
            if (Parameters[eye].Slice != 0) throw new InvalidOperationException("Unexpected Tex2D slice");
        }
        if (Targets[0].Pointer == Targets[1].Pointer) throw new InvalidOperationException("Eye targets alias");
        VrGraphics.Observe(Targets[0].width,Targets[0].height);
        return true;
    }

    // Share the already-queried physical targets/passes with another camera in
    // the same stack; its view and near/far projection still need native queries.
    internal void RefreshParameters(Camera camera, RendererAudit.NativeParameter[] parameters)
    {
        IntPtr nativeCamera = UnityEngine.Object.MarshalledUnityObject.Marshal(camera);
        for (int eye = 0; eye < 2; ++eye)
        {
            parameters[eye] = default;
            getParameter(ref passes[eye], nativeCamera, 0, ref parameters[eye]);
            if (parameters[eye].Slice != 0) throw new InvalidOperationException("Unexpected overlay eye slice");
        }
    }

    private static T Bind<T>(string name) where T : Delegate
    {
        IntPtr address = IL2CPP.il2cpp_resolve_icall(name);
        if (address == IntPtr.Zero) throw new MissingMethodException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
}
