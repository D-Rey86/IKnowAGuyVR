using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

// Normal startup enables stereo/tracking and motion input. Explicit probe modes
// retain the separate bounded research controls.
[BepInPlugin("iknowaguyvr.subsystemprobe", "I Know a Guy VR", Plugin.Version)]
public sealed class Plugin : BasePlugin
{
    internal const string Version="1.0.1";
    internal static string LogPath;
    public override void Load()
    {
        LogPath = Path.Combine(Paths.GameRootPath, "SubsystemProbe.log");
        File.WriteAllText(LogPath, $"SubsystemProbe {DateTime.UtcNow:O}\n");
        ClassInjector.RegisterTypeInIl2Cpp<ProbeBehaviour>();
        var go = new GameObject("IKnowAGuyVR_SubsystemProbe");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeBehaviour>();
        if (Probe.GameplayStereo) HeadsetOnlyPitch.Install();
        if (Probe.GameplayStereo) HeadInteractionAim.Install();
        VrSettings.Initialize(Config);
        VrPrompts.Install();
        IKnowAGuyVR.Networking.NetworkPoseRuntime.Initialize(true,Line);
        ImmersiveHud.Initialize(Config);
        PhysicalBelt.Initialize(Config);
        VrSettingsMenu.Install();
        VrGamepadInput.Install();
        if(MotionInput.BaseEnabled)BeltModels.Install();
        MotionHaptics.Initialize(Config);
        if (MotionInput.BaseEnabled) { MotionInput.Install(); ArcadeMotion.Install(); MotionPointer.Install(); CashMotion.Install(); ForgeryMotion.Install(); KitchenHands.Install(); ObjectGrabs.Install(); DistanceGrips.Install(); ReleasePlacement.Install(); ContextHud.Install(); SpyMotion.Install(); RepairMotion.Install(); MotionHeldItems.Install(); MotionShovel.Install(); MotionTools.Install(); MotionChest.Install(); NaturalItemGrip.Install(); MotionBody.Install(); MotionPhotography.Install(); WorldPress.Install(); }
#if IKAG_DIAGNOSTICS
        if(Environment.GetEnvironmentVariable("IKAG_XR_INTRO_GPU")=="1")Environment.SetEnvironmentVariable("IKAG_XR_RENDERDOC","0");
        ShadyUiAudit.Init();
        if (Environment.GetEnvironmentVariable("IKAG_XR_HEAD_AIM_AUDIT") == "1") HeadAimAudit.Install();
        if (Environment.GetEnvironmentVariable("IKAG_XR_PITCH_AUDIT") == "1") PitchInputControl.Begin();
        if (Environment.GetEnvironmentVariable("IKAG_XR_UI_INPUT_AUDIT") == "1") UiInputAudit.Begin();
        if (Environment.GetEnvironmentVariable("IKAG_XR_GAMEPLAY_AUDIT") == "1")
        {
            ClassInjector.RegisterTypeInIl2Cpp<GameplayRenderAudit>();
            go.AddComponent<GameplayRenderAudit>();
        }
#endif
    }
    internal static void Line(string text) => File.AppendAllText(LogPath, $"{DateTime.UtcNow:O} {text}\n");
}

public sealed class ProbeBehaviour : MonoBehaviour
{
    private bool attempted;
    public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

    private void LateUpdate(){if(MotionInput.Enabled)HandPhysics.Sample();}
    private void FixedUpdate(){if(MotionInput.Enabled)HandPhysics.FixedStep();}
    private void OnDisable(){HandPhysics.Stop();}
    private void OnApplicationQuit(){VrSettings.Flush();}

    private void Update()
    {
        if (attempted)
        {
            if (Probe.Active) Probe.Tick();
            if (!Probe.Active) enabled = false;
            return;
        }
        attempted = true;
        try { Probe.Run(); }
        catch (Exception e) { Plugin.Line("FAILED: " + e); }
        finally { if (!Probe.Active) enabled = false; }
    }
}

internal static unsafe class Probe
{
#if IKAG_DIAGNOSTICS
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string module);
#endif
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeEvent(int evt, ulong payload);
    private static readonly NativeEvent callback = OnNativeEvent;
    [DllImport("UnityOpenXR", EntryPoint = "main_LoadOpenXRLibrary")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool LoadLibrary(byte[] path);
    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_GetProcAddressPtr")]
    private static extern IntPtr GetProcAddress([MarshalAs(UnmanagedType.I1)] bool loaderDefault);
    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_SetProcAddressPtrAndLoadStage1")]
    private static extern void LoadStage1(IntPtr procAddress);
    [DllImport("UnityOpenXR", EntryPoint = "session_InitializeSession")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool InitializeSession();
    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_SetCallbacks")]
    private static extern void SetCallbacks(NativeEvent value);
    [DllImport("UnityOpenXR", EntryPoint = "session_SetSuccessfullyInitialized")]
    private static extern void SetInitialized([MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport("UnityOpenXR", EntryPoint = "session_CreateSessionIfNeeded")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CreateSessionIfNeeded();
    [DllImport("UnityOpenXR", EntryPoint = "session_BeginSession")]
    private static extern void BeginSession();
    [DllImport("UnityOpenXR", EntryPoint = "messagepump_PumpMessageLoop")]
    private static extern void Pump();
#if IKAG_DIAGNOSTICS
    [DllImport("UnityOpenXR", EntryPoint = "session_GetSessionState")]
    private static extern void GetSessionState(out int oldState, out int newState);
#endif
    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_IsSessionFocused")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsFocused();
    private static IntPtr displayObject, inputObject, baseClass;
    private static bool ready, started, stereoArmed;
#if IKAG_DIAGNOSTICS
    private static int ticks;
#endif
    private static int lastNativeEvent = -1;
    private static ulong lastNativePayload;
    internal static bool Active { get; private set; }
    internal static bool Focused => started && IsFocused();
#if IKAG_DIAGNOSTICS
    private static readonly bool MotionTransportEnabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_TRANSPORT")=="1";
    internal static readonly bool AutomaticGameplay = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IKAG_XR_PROBE_MODE"));
    internal static readonly bool GameplayStereo = AutomaticGameplay || Environment.GetEnvironmentVariable("IKAG_XR_GAMEPLAY_STEREO") == "1";
    internal static readonly bool ScreenUI = Environment.GetEnvironmentVariable("IKAG_XR_DISABLE_SCREEN_UI") != "1";
#else
    private static readonly bool MotionTransportEnabled = false;
    internal static readonly bool GameplayStereo = true;
    internal static readonly bool ScreenUI = true;
#endif

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateSubsystem(IntPtr nativeDescriptor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StartSubsystem(IntPtr nativeSubsystem);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte IsSubsystemRunning(IntPtr nativeSubsystem);
    private static StartSubsystem startSubsystem;
    private static IsSubsystemRunning isSubsystemRunning;

    private sealed class Descriptor
    {
        internal readonly string Id;
        internal readonly IntPtr Object;
        internal readonly IntPtr Native;
        internal Descriptor(string id, IntPtr obj, IntPtr native)
        { Id = id; Object = obj; Native = native; }
    }

    internal static void Run()
    {
        EngineBinaryContract.ValidateBinary();
#if IKAG_DIAGNOSTICS
        string mode = AutomaticGameplay ? "loader-order" : Environment.GetEnvironmentVariable("IKAG_XR_PROBE_MODE");
        if (mode != "inventory" && mode != "create" && mode != "start" && mode != "loader-order")
            throw new InvalidOperationException("Unknown diagnostic mode: " + mode);
#endif
        Plugin.Line($"Unity={Application.unityVersion} OpenXR startup");

        IntPtr descriptorBase = Class("UnityEngine", "IntegratedSubsystemDescriptor");
        IntPtr subsystemBase = Class("UnityEngine", "IntegratedSubsystem");
        IntPtr store = Class("UnityEngine.SubsystemsImplementation", "SubsystemDescriptorStore");
        IntPtr manager = Class("UnityEngine", "SubsystemManager");
        startSubsystem = Resolve<StartSubsystem>("UnityEngine.IntegratedSubsystem::Start_Injected");
        isSubsystemRunning = Resolve<IsSubsystemRunning>("UnityEngine.IntegratedSubsystem::IsRunning_Injected");
        var descriptors = new List<Descriptor>();
        IntPtr list = StaticObject(store, "s_IntegratedDescriptors");
        int count = Count(list);
        Plugin.Line("Integrated descriptor count=" + count);
        for (int i = 0; i < count; ++i)
        {
            IntPtr obj = Item(list, i);
            IntPtr native = PointerField(obj, descriptorBase, "m_Ptr");
            string id = IL2CPP.Il2CppStringToManaged(Invoke(Method(descriptorBase, "get_id", 0), obj));
#if IKAG_DIAGNOSTICS
            Plugin.Line($"descriptor[{i}] id={id} class={ClassName(obj)} object=0x{obj.ToInt64():X} native=0x{native.ToInt64():X}");
#endif
            descriptors.Add(new Descriptor(id, obj, native));
        }
#if IKAG_DIAGNOSTICS
        DumpInstances(manager, subsystemBase);
        Plugin.Line("INVENTORY_COMPLETE");
        if (mode == "inventory") return;
        if (mode == "loader-order")
#endif
        {
            Plugin.Line("BEFORE_LOAD_LIBRARY");
            // Match OpenXRLoader.LoadOpenXRSymbols: explicitly reload/reset the pre-init context.
            string loader = Path.Combine(Paths.GameRootPath, "I Know a Guy_Data", "Plugins", "x86_64", "openxr_loader.dll");
            if (!LoadLibrary(System.Text.Encoding.Unicode.GetBytes(loader + "\0")))
                throw new InvalidOperationException("LoadOpenXRLibrary returned false");
            IntPtr procAddress = GetProcAddress(true);
            if (procAddress == IntPtr.Zero) throw new InvalidOperationException("Loader proc address is null");
#if IKAG_DIAGNOSTICS
            procAddress=OpenXRFrameAudit.Wrap(procAddress);
#endif
            LoadStage1(XrViewLimits.Wrap(procAddress));
            Plugin.Line("STAGE1_LOADED");
            Plugin.Line("BEFORE_INITIALIZE_SESSION");
            SetInitialized(false);
            if (!InitializeSession()) throw new InvalidOperationException("InitializeSession returned false");
            SetCallbacks(callback);
            Plugin.Line("SESSION_INITIALIZED");
        }

        // Same native handle and registry lookup contract as Unity's descriptor Create().
        // Never pass an Il2CppObjectBase.Pointer as the native descriptor handle.
        IntPtr createAddress = IL2CPP.il2cpp_resolve_icall("UnityEngine.SubsystemDescriptorBindings::Create");
        if (createAddress == IntPtr.Zero) throw new InvalidOperationException("Create icall absent");
        var create = Marshal.GetDelegateForFunctionPointer<CreateSubsystem>(createAddress);
        IntPtr lookup = Method(manager, "GetIntegratedSubsystemByPtr", 1);
        IntPtr* args = stackalloc IntPtr[1];
        foreach (string wanted in new[] { "OpenXR Display", "OpenXR Input" })
        {
            Descriptor descriptor = descriptors.Find(d => d.Id == wanted);
            if (descriptor == null || descriptor.Native == IntPtr.Zero)
                throw new InvalidOperationException("Missing native descriptor: " + wanted);
            Plugin.Line($"BEFORE_CREATE {wanted} native=0x{descriptor.Native.ToInt64():X}");
            IntPtr nativeSubsystem = create(descriptor.Native);
            Plugin.Line($"AFTER_CREATE {wanted} native=0x{nativeSubsystem.ToInt64():X}");
            if (nativeSubsystem == IntPtr.Zero) throw new InvalidOperationException("Create returned null: " + wanted);
            args[0] = (IntPtr)(&nativeSubsystem);
            IntPtr subsystem = Invoke(lookup, IntPtr.Zero, args);
            if (subsystem == IntPtr.Zero) throw new InvalidOperationException("Registry lookup returned null: " + wanted);
            if (PointerField(subsystem, subsystemBase, "m_Ptr") != nativeSubsystem)
                throw new InvalidOperationException("Registry native handle mismatch");
            IntPtr field = Field(subsystemBase, "m_SubsystemDescriptor");
            IntPtr target = IntPtr.Add(subsystem, checked((int)IL2CPP.il2cpp_field_get_offset(field)));
            IL2CPP.il2cpp_gc_wbarrier_set_field(subsystem, target, descriptor.Object);
            Plugin.Line($"REGISTERED {wanted} class={ClassName(subsystem)}");
            if (wanted == "OpenXR Display") displayObject = subsystem;
            else inputObject = subsystem;
#if IKAG_DIAGNOSTICS
            if (mode == "start")
            {
                Plugin.Line("BEFORE_START " + wanted);
                Start(subsystem, subsystemBase);
                Plugin.Line("AFTER_START " + wanted + " running=" + Running(subsystem, subsystemBase));
            }
#endif
        }
#if IKAG_DIAGNOSTICS
        DumpInstances(manager, subsystemBase);
        Plugin.Line("PROBE_COMPLETE");
        if (mode == "loader-order")
#endif
        {
            baseClass = subsystemBase;
            SetInitialized(true);
            Plugin.Line("CREATE_SESSION " + CreateSessionIfNeeded());
            Active = true;
        }
    }

    private static void OnNativeEvent(int evt, ulong payload)
    {
        // Never throw through the reverse P/Invoke boundary.
        try
        {
            if (evt == 11) ready = true; // OpenXRFeature.NativeEvent.XrReady in package 1.17.1.
            if (evt != lastNativeEvent || payload != lastNativePayload)
            {
                lastNativeEvent = evt; lastNativePayload = payload;
                Plugin.Line($"NATIVE_EVENT {evt} payload={payload}");
            }
        }
        catch { }
    }

    internal static void Tick()
    {
        try
        {
            Pump();
            OverlayCursor.Update();
            VrSettings.Tick();
#if IKAG_DIAGNOSTICS
            UiScenarioControl.Tick();
            HandContactAudit.Tick();
            PizzaBoxAudit.Tick();
            ControlRemapAudit.Tick();
            JournalBeltAudit.Tick();
            BoxIdentifierAudit.Tick();
            RuntimeIssueAudit.Tick();
            SauceDistanceAudit.Tick();
            DayTransitionAudit.Tick();
            VrSettingsAudit.Tick();
        OvenCycleAudit.Tick();
        OrderSwitchAudit.Tick();
        HandoffAudit.Tick();
        ItemPoseAudit.Tick();
        NaturalGripAudit.Tick();
        NaturalCatalogueAudit.Tick();
        ShovelAudit.Tick();
        ShovelPlayAudit.Tick();
        ShovelPerfAudit.Tick();
        ToolNativeAudit.Tick();
        ToolControlAudit.Tick();
        PhysicalItemAudit.Tick();
        CarryCatalogueAudit.Tick();
        MeleeShapeAudit.Tick();
        FingerControlAudit.Tick();
        WorldPressSurvey.Tick();
        WorldPressAudit.Tick();
            MotionTransportAudit.Tick();
            MotionAcceptanceAudit.Tick();
            FramePerformanceAudit.Tick();
            MotionWorldAudit.Tick();
            MotionMenuAudit.Tick();
            MotionActionAudit.Tick();
            MotionFocusedAudit.Tick();
            MotionBodyAudit.Tick();
            MotionPlacementAudit.Tick();
            MotionRegressionAudit.Tick();
            MachineExitAudit.Tick();
            BodyAlignmentAudit.Tick();
            PlacementReticleAudit.Tick();
            GripPunchAudit.Tick();
            ShadyUiAudit.Tick();
            GripInteractionAudit.Tick(); DistanceGripAudit.Tick(); PizzaShelfAudit.Tick(); ObjectGrabAudit.Tick(); ContextHudAudit.Tick(); WristOrientationAudit.Tick(); ImmersiveHudAudit.Tick(); KeyboardAudit.Tick(); RegisterArcadeAudit.Tick(); CashAudit.Tick(); PolishAudit.Tick(); ForgeryAudit.Tick(); InteractionAudit.Tick(); HandleAssetAudit.Tick();
#endif
            if (ready && !started)
            {
                started = true;
                if (MotionInput.BaseEnabled || MotionTransportEnabled)
                {
                    try { ControllerTransport.Register(); ControllerTransport.Initialize(); }
                    catch(Exception e) { Plugin.Line("MOTION_INPUT_UNAVAILABLE " + e); }
                }
#if IKAG_DIAGNOSTICS
                if (Environment.GetEnvironmentVariable("IKAG_XR_TRIAL") == "1" || Environment.GetEnvironmentVariable("IKAG_XR_GAMEPLAY_NATIVE_AUDIT") == "1" || GameplayStereo)
#endif
                    SrpDisplayConfig.BeforeStart(displayObject, baseClass);
#if IKAG_DIAGNOSTICS
                OpenXRFrameAudit.Configure();
#endif
                Plugin.Line("BEFORE_START OpenXR Display");
                Start(displayObject, baseClass);
                Plugin.Line("AFTER_START OpenXR Display running=" + Running(displayObject, baseClass));
                VrGraphics.Attach(displayObject);
                BeginSession();
                Plugin.Line("BEGIN_SESSION_RETURNED");
                Start(inputObject, baseClass);
                Plugin.Line("AFTER_START OpenXR Input running=" + Running(inputObject, baseClass));
            }
            if (started && (MotionInput.BaseEnabled || MotionTransportEnabled)) ControllerTransport.Sample();
#if IKAG_DIAGNOSTICS
            FingerControlAudit.SamplePrediction();
#endif
            if (started && MotionInput.Enabled) {ImmersiveHud.Tick();MotionShovel.Input();PhysicalHeldItems.Input();WorldPress.Tick();KitchenHands.Tick();MotionInput.MenuTick();ComputerKeyboard.Tick();MotionPointer.Tick();}
            // Readiness owns startup. A slow runtime must not lose its pump at an
            // arbitrary frame count. Register the camera observer exactly once.
            if (started && GameplayStereo && !stereoArmed)
            {
                GameplayStereoArmer.Begin(displayObject);
                stereoArmed = true;
            }
#if IKAG_DIAGNOSTICS
            if (++ticks == 120)
            {
                GetSessionState(out int oldState, out int newState);
                Plugin.Line($"BOUNDED_OBSERVATION oldState={oldState} newState={newState} focused={IsFocused()} started={started}");
                DumpRenderPath();
                if (Environment.GetEnvironmentVariable("IKAG_XR_AUDIT") == "1") RendererAudit.Run(displayObject);
                if (Environment.GetEnvironmentVariable("IKAG_XR_TRIAL") == "1") StereoTrial.Begin(displayObject);
                if (Environment.GetEnvironmentVariable("IKAG_XR_CAMERA_ABI") == "1") CameraAspectAudit.Run();
                // The normal before-render registration helper is stripped here.
                // Reuse this existing once-per-frame Update pump, including frames
                // without cameras. No duplicate callback or late pose sampling.
                if (!GameplayStereo) Active = false;
            }
#endif
        }
        catch (Exception e)
        {
            Plugin.Line("TICK_FAILED " + e);
            Active = false;
        }
    }

#if IKAG_DIAGNOSTICS
    internal static NativeEyeResources ReadNativeEyes(Camera camera)
    {
        if (!started || displayObject == IntPtr.Zero) throw new InvalidOperationException("Native display not started");
        var eyes = new NativeEyeResources(displayObject);
        if (!eyes.TryRefresh(camera)) throw new InvalidOperationException("Native eye targets temporarily unavailable");
        return eyes;
    }

    internal static string SessionSnapshot()
    {
        GetSessionState(out int previous, out int current);
        return $"previous={previous} current={current} focused={IsFocused()}";
    }

    private static void DumpRenderPath()
    {
        Plugin.Line("RENDER_PATH batchMode=" + Application.isBatchMode);
        var cameras = Camera.allCameras;
        foreach (var camera in cameras)
            Plugin.Line($"CAMERA {camera.name} enabled={camera.enabled} stereo={camera.stereoEnabled} target={(camera.targetTexture == null ? "screen" : camera.targetTexture.name)}");
        IntPtr xrSystem = IL2CPP.GetIl2CppClass("Unity.RenderPipelines.Core.Runtime.dll", "UnityEngine.Experimental.Rendering", "XRSystem");
        if (xrSystem == IntPtr.Zero) throw new InvalidOperationException("XRSystem missing");
        Plugin.Line("URP_XRSYSTEM displayField=" + (IL2CPP.il2cpp_class_get_field_from_name(xrSystem, "s_Display") != IntPtr.Zero));
        foreach (string methodName in new[] { "get_displayActive", "RefreshDeviceInfo", "CreateDefaultLayout", "ReconfigurePass" })
        {
            IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(xrSystem, methodName, -1);
            if (method == IntPtr.Zero) { Plugin.Line("URP_METHOD_ABSENT " + methodName); continue; }
            var nativeMethod = Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap((Il2CppInterop.Runtime.Runtime.Il2CppMethodInfo*)method);
            IntPtr address = nativeMethod.MethodPointer;
            byte[] bytes = new byte[16];
            Marshal.Copy(address, bytes, 0, bytes.Length);
            IntPtr imageBase = GetModuleHandle("GameAssembly.dll");
            if (imageBase == IntPtr.Zero) throw new InvalidOperationException("GameAssembly module absent");
            Plugin.Line($"URP_METHOD {methodName} RVA=0x{address.ToInt64() - imageBase.ToInt64():X} bytes={Convert.ToHexString(bytes)}");
            if (methodName == "get_displayActive")
            {
                IntPtr boxed = Invoke(method, IntPtr.Zero);
                Plugin.Line("URP_DISPLAY_ACTIVE=" + (*(byte*)IL2CPP.il2cpp_object_unbox(boxed) != 0));
            }
        }
    }

    private static void DumpInstances(IntPtr manager, IntPtr subsystemBase)
    {
        IntPtr list = StaticObject(manager, "s_IntegratedSubsystems");
        int count = Count(list);
        Plugin.Line("Integrated instance count=" + count);
        for (int i = 0; i < count; ++i)
        {
            IntPtr obj = Item(list, i);
            Plugin.Line($"instance[{i}] class={ClassName(obj)} native=0x{PointerField(obj, subsystemBase, "m_Ptr").ToInt64():X} running={Running(obj, subsystemBase)}");
        }
    }

#endif

    private static IntPtr Class(string ns, string name)
    {
        Plugin.Line("CLASS " + ns + "." + name);
        IntPtr klass = IL2CPP.GetIl2CppClass("UnityEngine.SubsystemsModule.dll", ns, name);
        if (klass == IntPtr.Zero) throw new InvalidOperationException("Class absent: " + name);
        IL2CPP.il2cpp_runtime_class_init(klass);
        return klass;
    }
    private static IntPtr Field(IntPtr klass, string name)
    {
        IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, name);
        if (field == IntPtr.Zero) throw new MissingFieldException(name);
        return field;
    }
    private static IntPtr Method(IntPtr klass, string name, int argc)
    {
        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
        if (method == IntPtr.Zero) throw new MissingMethodException(name);
        return method;
    }
    private static IntPtr Invoke(IntPtr method, IntPtr obj, IntPtr* args = null)
    {
        IntPtr exception = IntPtr.Zero;
        IntPtr result = IL2CPP.il2cpp_runtime_invoke(method, obj, (void**)args, ref exception);
        Il2CppException.RaiseExceptionIfNecessary(exception);
        return result;
    }
    private static IntPtr StaticObject(IntPtr klass, string name)
    {
        Plugin.Line("STATIC_FIELD " + name);
        IntPtr result = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(Field(klass, name), &result);
        if (result == IntPtr.Zero) throw new InvalidOperationException("Null list: " + name);
        return result;
    }
    private static IntPtr PointerField(IntPtr obj, IntPtr klass, string name)
    {
        IntPtr result = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(obj, Field(klass, name), &result);
        return result;
    }
    private static int Count(IntPtr list)
    {
        IntPtr result = Invoke(Method(IL2CPP.il2cpp_object_get_class(list), "get_Count", 0), list);
        if (result == IntPtr.Zero) throw new InvalidOperationException("Null count result");
        int count = *(int*)IL2CPP.il2cpp_object_unbox(result);
        if (count < 0 || count > 32) throw new InvalidOperationException("Unexpected subsystem count=" + count);
        return count;
    }
    private static IntPtr Item(IntPtr list, int index)
    {
        IntPtr* args = stackalloc IntPtr[1];
        args[0] = (IntPtr)(&index);
        IntPtr item = Invoke(Method(IL2CPP.il2cpp_object_get_class(list), "get_Item", 1), list, args);
        if (item == IntPtr.Zero) throw new InvalidOperationException("Null subsystem item");
        return item;
    }
    private static T Resolve<T>(string name) where T : Delegate
    {
        IntPtr address = IL2CPP.il2cpp_resolve_icall(name);
        if (address == IntPtr.Zero) throw new MissingMethodException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static bool Running(IntPtr obj, IntPtr klass)
    {
        IntPtr native = PointerField(obj, klass, "m_Ptr");
        return native != IntPtr.Zero && isSubsystemRunning(native) != 0;
    }
    private static void Start(IntPtr obj, IntPtr klass)
    {
        IntPtr native = PointerField(obj, klass, "m_Ptr");
        if (native == IntPtr.Zero) throw new InvalidOperationException("Null native subsystem at Start");
        startSubsystem(native);
    }
    private static string ClassName(IntPtr obj) => IL2CPP.il2cpp_class_get_name_(IL2CPP.il2cpp_object_get_class(obj));
}
