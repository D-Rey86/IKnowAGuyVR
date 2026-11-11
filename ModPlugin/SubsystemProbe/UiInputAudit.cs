using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific;
using UnityEngine.Rendering.Universal;

namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in cold diagnostic. No rendering or input behavior is changed.
internal static unsafe class UiInputAudit
{
    private static readonly HashSet<(int, int, bool, bool)> observed = new();
    private static string path;
    private static bool inputObserved;
    internal static void Begin()
    {
        EngineBinaryContract.ValidateBinary();
        path = Path.Combine(BepInEx.Paths.GameRootPath, "UiInputAudit.log");
        File.WriteAllText(path, "Read-only overlay/input audit\n");
        if (Environment.GetEnvironmentVariable("IKAG_XR_MOTION_AUDIT") == "1")
        {
            Dump("Assembly-CSharp.dll", "", "EmoteManager", "UpdateWheelSelection", "OnWheelPressed", "OnWheelReleased", "OpenWheel", "CloseWheel");
            Dump("Assembly-CSharp.dll", "", "InteractionManager", "DropLastItem", "HandleIngredientPlacement", "HandlePrimaryPressed", "HandleDropPressed", "HandleInteraction", "GetHoldingConfiguration");
            Dump("Assembly-CSharp.dll", "", "PhotoCamera", "HandleZoomInput", "CaptureScreenshot", "TakePhoto", "FindCaptureCamera", "UpdateLiveSubjectDetection");
            Dump("Assembly-CSharp.dll", "", "InteractionManager", "SpawnPunchCollider", "HandleDartThrow", "HandleHammerSwing", "HandleDirtCleaning", "HandleDigging");
            Dump("Assembly-CSharp.dll", "", "BuildingManager", "Update", "UpdatePreviewPosition", "HandleReplacementMode");
            Dump("Assembly-CSharp.dll", "Core.Input", "InputManager", "get_IsBuildModeHeld");
            Dump("Assembly-CSharp.dll", "", "WorldSpacePanelInput", "Install");
            Dump("UnityEngine.UIElementsModule.dll", "UnityEngine.UIElements", "CameraScreenRaycaster", "MakeRay");
        }
        Dump("Assembly-CSharp.dll", "", "InteractionManager", "Update", "LateUpdate", "FixedUpdate", "PerformRaycast", "DetectObjectsInSphere", "FindClosestInteractable", "IsStillFacingHoldTarget", "GetInteractionCamera", "HandleDrop", "HandleInteraction");
        Dump("Assembly-CSharp.dll", "", "PlayerMovementController", "CacheInput", "HandleCameraRotation", "HandleBodyRotation");
        Dump("Assembly-CSharp.dll", "Core.Input", "InputManager", "get_LookInput", "Update", "SubscribeToActions");
        Dump("Unity.RenderPipelines.Universal.Runtime.dll", "UnityEngine.Rendering.Universal", "UniversalCameraData", "get_rendersOverlayUI", "get_resolveToScreen");
        Dump("Unity.RenderPipelines.Universal.Runtime.dll", "UnityEngine.Rendering.Universal", "UniversalRenderer", "OnAfterRendering");
        var harmony = new Harmony("iknowaguyvr.read-only-ui-input-audit");
        harmony.Patch(typeof(UniversalCameraData).GetProperty("rendersOverlayUI").GetMethod,
            postfix: new HarmonyMethod(typeof(UiInputAudit).GetMethod(nameof(Overlay), BindingFlags.Static | BindingFlags.NonPublic)));
        harmony.Patch(typeof(PlayerMovementController).GetMethod("CacheInput"),
            postfix: new HarmonyMethod(typeof(UiInputAudit).GetMethod(nameof(Input), BindingFlags.Static | BindingFlags.NonPublic)));
        harmony.Patch(typeof(DrawScreenSpaceUIPass).GetMethod("RenderOverlay"),
            prefix: new HarmonyMethod(typeof(UiInputAudit).GetMethod(nameof(RenderOverlay), BindingFlags.Static | BindingFlags.NonPublic)));
        harmony.Patch(typeof(UniversalRenderer).GetMethod("OnAfterRendering"),
            prefix: new HarmonyMethod(typeof(UiInputAudit).GetMethod(nameof(After), BindingFlags.Static | BindingFlags.NonPublic)));
    }
    private static void Overlay(UniversalCameraData __instance, bool __result)
    {
        var camera = __instance.camera;
        if (camera == null) return;
        int target = __instance.targetTexture == null ? 0 : __instance.targetTexture.GetInstanceID();
        var key = (camera.GetInstanceID(), target, __instance.resolveFinalTarget, __instance.isLastBaseCamera);
        if (!observed.Add(key)) return;
        File.AppendAllText(path, $"OVERLAY scene={camera.gameObject.scene.name} camera={camera.name} target={target} resolveFinal={__instance.resolveFinalTarget} lastBase={__instance.isLastBaseCamera} eyeMatch={NativeEyeFinalBlit.IsEyeTarget(__instance.targetTexture)} result={__result} pixel={__instance.pixelRect}\n");
    }
    private static bool overlayPassObserved;
    private static readonly HashSet<(int, int, bool)> afterObserved = new();
    private static void After(UniversalRenderer __instance)
    {
        try
        {
            var data = __instance.frameData.Get<UniversalCameraData>();
            var resources = __instance.frameData.Get<UniversalResourceData>();
            int target = data.targetTexture == null ? 0 : data.targetTexture.GetInstanceID();
            if (!afterObserved.Add((data.camera.GetInstanceID(), target, data.resolveFinalTarget))) return;
            File.AppendAllText(path, $"AFTER camera={data.camera.name} target={target} final={data.resolveFinalTarget} lastBase={data.isLastBaseCamera} overlay={data.rendersOverlayUI} HDR={data.isHDROutputActive} eyeMatch={NativeEyeFinalBlit.IsEyeTarget(data.targetTexture)} colorValid={resources.backBufferColor.IsValid()} depthValid={resources.backBufferDepth.IsValid()}\n");
        }
        catch (Exception e) { File.AppendAllText(path, "AFTER_AUDIT_FAILED " + e + "\n"); }
    }
    private static void RenderOverlay()
    {
        if (overlayPassObserved) return;
        overlayPassObserved = true;
        File.AppendAllText(path, "NATIVE_OVERLAY_PASS_CALLED\n");
    }
    private static void Input(PlayerMovementController __instance)
    {
        if (inputObserved) return;
        var manager = Core.Input.InputManager.Instance;
        if (manager == null) return;
        inputObserved = true;
        File.AppendAllText(path, $"INPUT local={__instance.IsOwner} scheme={manager.CurrentControlScheme} cached={__instance.lookInput} source={manager.LookInput}\n");
        var action = manager.LookAction;
        if (action == null) return;
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; ++i)
            File.AppendAllText(path, $"BINDING index={i} path={bindings[i].effectivePath} processors={bindings[i].effectiveProcessors}\n");
    }
    internal static void Dump(string assembly, string ns, string type, params string[] names)
    {
        var klass=IL2CPP.GetIl2CppClass(assembly,ns,type);
        DumpClass(klass,type,names);
    }
    internal static void DumpNested(string assembly,string ns,string type,string nested,params string[] names)
    {
        var klass=IL2CPP.GetIl2CppNestedType(IL2CPP.GetIl2CppClass(assembly,ns,type),nested);
        DumpClass(klass,type+"/"+nested,names);
    }
    private static void DumpClass(IntPtr klass,string type,string[] names)
    {
        IntPtr imageBase = IntPtr.Zero;
        foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if (module.ModuleName == "GameAssembly.dll") imageBase = module.BaseAddress;
        IL2CPP.il2cpp_runtime_class_init(klass);
        IntPtr iterator = IntPtr.Zero;
        for (IntPtr method; (method = IL2CPP.il2cpp_class_get_methods(klass, ref iterator)) != IntPtr.Zero;)
        {
            string name = IL2CPP.il2cpp_method_get_name_(method);
            if (Array.IndexOf(names, name) < 0) continue;
            IntPtr address = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer;
            byte[] bytes = new byte[32]; Marshal.Copy(address, bytes, 0, bytes.Length);
            File.AppendAllText(path, $"METHOD {type}.{name} RVA=0x{address.ToInt64()-imageBase.ToInt64():X} bytes={Convert.ToHexString(bytes)}\n");
        }
    }
}
