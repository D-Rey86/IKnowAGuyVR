using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace IKnowAGuyVR.SubsystemProbe;

// Render runtime eye views through the existing URP stack and restore its state.
internal static class StereoTrial
{
#if IKAG_DIAGNOSTICS
    private const int FrameLimit = 120;
#endif
    private static Camera camera;
    private static GameObject cameraObject;
    private static Transform cameraTransform;
    private static Camera overlayCamera;
    private static UniversalAdditionalCameraData overlayData;
    private static Transform overlayTransform;
    private static RendererAudit.NativeParameter[] overlayParameters;
    private static bool gameplay;
    private static bool screenUI;
    private static bool fitUI;
    private static bool continuous;
    private static NativeEyeResources eyes;
    private static Il2CppSystem.Action<ScriptableRenderContext, Camera> callback;
    private static string logPath;
#if IKAG_DIAGNOSTICS
    private static string commandPath;
#endif
    private static int rendered, lastFrame = -1, sceneHandle;
    private static bool rendering;
    private static bool initializedLog;
#if IKAG_DIAGNOSTICS
    private static bool readback;
    private static bool renderDoc;
    private static bool clearControl;
    private static bool headMotionControl;
    private static bool overlayToggleControl, originalOverlayEnabled;
    private static bool equipControl;
    private static bool focusControl;
    private static bool trackingLossControl;
    private static bool equipSelected;
    private static InventoryManager inventory;
    private static int originalSlot;
#endif
    private static HeadPoseReader poseReader;
    private static bool headTracking;
    private static bool referenceAligned;
    internal static float TrackingScale{get;private set;}=1;
    private static bool seatedApplied,seatedCalibrate;
    private static float standingReferenceY;
    private static Vector3 SeatedOffset=>gameplay&&(VrSettings.Seated?.Value??false)?Vector3.up*(VrSettings.SeatedHeight.Value*.01f):Vector3.zero;
    internal static void CalibrateSeatedHeight(){seatedCalibrate=true;}
    internal static void UpdateSeatedPlay()
    {
        if(!gameplay||!referenceAligned||rendering||poseReader==null)return;
        bool seated=VrSettings.Seated.Value;
        if(seated==seatedApplied&&!seatedCalibrate)return;
        if(!ReadHead(out var head,out var rotation,out _)||!ValidPose(head,rotation))return;
        if(seated)
        {
            if(!seatedApplied)standingReferenceY=referenceHeadPosition.y;
            referenceHeadPosition.y=head.y;
        }
        else if(seatedApplied)referenceHeadPosition.y=standingReferenceY;
        seatedApplied=seated;seatedCalibrate=false;
    }
    internal static void ApplyTrackingScale(float value)
    {
        if(value==TrackingScale)return;
        if(referenceAligned&&callback!=null&&!rendering&&ReadHead(out var head,out _,out _))
        {
            float ratio=TrackingScale/value;
            referenceHeadPosition=head-(head-referenceHeadPosition)*ratio;
            if(seatedApplied)standingReferenceY=head.y-(head.y-standingReferenceY)*ratio;
        }
        TrackingScale=value;
    }
    private static void ScaleEye(ref RendererAudit.NativeParameter parameter,Vector3 center)
    {
        float factor=TrackingScale*(VrSettings.Separation.Value/100f);
        if(factor==1)return;
        var world=parameter.View.inverse;
        world.m03=center.x+(world.m03-center.x)*factor;
        world.m13=center.y+(world.m13-center.y)*factor;
        world.m23=center.z+(world.m23-center.z)*factor;
        parameter.View=world.inverse;
    }
    private static int headFrame = -1;
    private static Vector3 cachedHeadPosition;
    private static Quaternion cachedHeadRotation;
    private static uint cachedHeadState;
    private static bool cachedHeadValid;
    private static Vector3 referenceHeadPosition;
    private static Quaternion referenceHeadRotationInverse;
#if IKAG_DIAGNOSTICS
    private static float maximumViewError, maximumCullError, maximumRestoreError;
    private static float maximumPositionError, maximumBasisError;
#endif
    internal static bool GameplayActive => gameplay && callback != null;
    internal static bool IsGameplayCamera(Camera target)=>GameplayActive&&target!=null&&camera!=null&&target.Pointer==camera.Pointer;
    internal static Camera GameplayOverlay=>GameplayActive?overlayCamera:null;
    internal static bool MenuRendered => !gameplay && rendered >= 90;
    internal static bool MenuActive=>!gameplay&&callback!=null;
    // Binding survives temporary unavailability; this flag describes drawing readiness.
    internal static bool WaitingForFrame { get; private set; }

    internal static void Begin(IntPtr displayObject, Camera gameplayCamera = null)
    {
        logPath = Path.Combine(BepInEx.Paths.GameRootPath, "StereoTrial.log");
        if (callback != null || poseReader != null) Finish("CAMERA_CHANGED");
        camera = gameplayCamera == null ? Camera.main : gameplayCamera;
        gameplay = camera != null && (camera.gameObject.scene.name == "Live" || camera.gameObject.scene.name == "Lobby");
        overlayCamera = null; overlayData = null; overlayTransform = null; overlayParameters = null;
#if IKAG_DIAGNOSTICS
        inventory = null; equipSelected = false;
#endif
        rendered = 0; lastFrame = -1; rendering = false;
        referenceAligned = false;
        WaitingForFrame = true;
        headFrame = -1;
        screenUI = fitUI = Probe.ScreenUI;
#if IKAG_DIAGNOSTICS
        maximumViewError = maximumCullError = maximumRestoreError = maximumPositionError = maximumBasisError = 0;
        continuous = Probe.AutomaticGameplay || Environment.GetEnvironmentVariable("IKAG_XR_CONTINUOUS") == "1";
#else
        continuous = true;
#endif
        if (!initializedLog) { File.WriteAllText(logPath, "Native Unity XR rendering\n"); initializedLog = true; }
        Write("BEGIN " + (continuous ? "continuous" : "bounded") + " URP " + (gameplay ? "gameplay stack" : "MainMenu") + "; native matrices and existing targets");
        try
        {
            EngineBinaryContract.ValidateBinary();
            ValidateMatrixLayout();
#if IKAG_DIAGNOSTICS
            if(Environment.GetEnvironmentVariable("IKAG_XR_MATRIX_AUDIT")=="1")ValidateMatrixAccess();
#endif
#if IKAG_DIAGNOSTICS
            readback = Environment.GetEnvironmentVariable("IKAG_XR_READBACK") == "1";
            renderDoc = Environment.GetEnvironmentVariable("IKAG_XR_RENDERDOC") == "1";
            clearControl = Environment.GetEnvironmentVariable("IKAG_XR_CLEAR_CONTROL") == "1";
            headMotionControl = Environment.GetEnvironmentVariable("IKAG_XR_HEAD_MOTION_CONTROL") == "1";
            overlayToggleControl = Environment.GetEnvironmentVariable("IKAG_XR_OVERLAY_TOGGLE_CONTROL") == "1";
            equipControl = Environment.GetEnvironmentVariable("IKAG_XR_EQUIP_CONTROL") == "1";
            focusControl = Environment.GetEnvironmentVariable("IKAG_XR_FOCUS_CONTROL") == "1";
            trackingLossControl = Environment.GetEnvironmentVariable("IKAG_XR_TRACKING_LOSS_CONTROL") == "1";
            headTracking = (gameplay || Probe.GameplayStereo) && Environment.GetEnvironmentVariable("IKAG_XR_DISABLE_HEAD_TRACKING") != "1";
            if (headTracking || headMotionControl) poseReader = new HeadPoseReader();
#else
            headTracking = true;
            poseReader = new HeadPoseReader();
#endif
            if (headTracking)
            {
                if (!poseReader.TryRead(out referenceHeadPosition, out var referenceRotation, out _) ||
                    !ValidPose(referenceHeadPosition, referenceRotation) || !AlignReference(referenceHeadPosition, referenceRotation))
                    Write("HEAD_ALIGNMENT_DEFERRED waiting for a tracked head with a measurable horizontal direction");
#if IKAG_DIAGNOSTICS
                PoseStabilityControl.Alignment(camera, referenceRotation);
#endif
            }
            if (camera == null || (gameplay ? camera.name != "MainCam" || (camera.gameObject.scene.name != "Live" && camera.gameObject.scene.name != "Lobby") : camera.name != "Setup2CAM" || camera.gameObject.scene.name != "MainMenu"))
                throw new InvalidOperationException("Unexpected trial camera or scene");
            cameraObject = camera.gameObject;
            sceneHandle = cameraObject.scene.handle;
            cameraTransform = camera.transform;
            var additional = camera.GetComponent<UniversalAdditionalCameraData>();
            if (additional == null || additional.renderType != CameraRenderType.Base)
                throw new InvalidOperationException("Expected URP base camera");
            if (additional.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                throw new InvalidOperationException("Temporal camera is outside this rendering trial");
            if (gameplay)
            {
                if (additional.cameraStack.Count != 1) throw new InvalidOperationException("Expected measured single held-item overlay");
                overlayCamera = additional.cameraStack[0];
                if (overlayCamera == null || overlayCamera.name != "HeldItemCamera" || !overlayCamera.isActiveAndEnabled)
                    throw new InvalidOperationException("Measured held-item overlay missing");
                overlayData = overlayCamera.GetComponent<UniversalAdditionalCameraData>();
                if (overlayData == null || overlayData.renderType != CameraRenderType.Overlay || overlayData.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                    throw new InvalidOperationException("Unexpected held-item render configuration");
                overlayTransform = overlayCamera.transform;
#if IKAG_DIAGNOSTICS
                originalOverlayEnabled = overlayCamera.enabled;
                if (equipControl)
                {
                    inventory = camera.GetComponentInParent<InventoryManager>();
                    if (inventory == null) throw new InvalidOperationException("Player inventory missing");
                    originalSlot = inventory.GetCurrentSelectedSlot();
                    if (inventory.IsSlotEmpty(0)) throw new InvalidOperationException("Existing item in slot 0 required for visual control");
                    var item = inventory.GetItemInSlot(0);
                    Write($"EQUIP_CONTROL_READY originalSlot={originalSlot} item={(item == null ? "null" : item.name)}");
                }
#endif
                overlayParameters = new RendererAudit.NativeParameter[2];
                Write($"OVERLAY camera={overlayCamera.name} near={overlayCamera.nearClipPlane:R} far={overlayCamera.farClipPlane:R} mask=0x{overlayCamera.cullingMask:X8}");
            }
            else if (additional.cameraStack.Count != 0) throw new InvalidOperationException("Unexpected menu overlay stack");
            Write($"ARMED scene={camera.gameObject.scene.name} camera={camera.name} aa={additional.antialiasing} post={additional.renderPostProcessing} projection={camera.projectionMatrixMode} flatTarget={(camera.targetTexture == null ? "screen" : camera.targetTexture.name)}");
            eyes = new NativeEyeResources(displayObject);
            Write($"QUERY_BEFORE fov={camera.fieldOfView:R} aspect={camera.aspect:R} projection={camera.projectionMatrix.ToString("R")}");
            eyes.TryRefresh(camera);
            Write($"QUERY_AFTER fov={camera.fieldOfView:R} aspect={camera.aspect:R} projection={camera.projectionMatrix.ToString("R")}");
#if IKAG_DIAGNOSTICS
            string runtimeDirectory = Environment.GetEnvironmentVariable("XRSIM_DIR");
            commandPath = string.IsNullOrEmpty(runtimeDirectory) ? null : Path.Combine(runtimeDirectory, "command.txt");
            if (commandPath == null && (headMotionControl || focusControl || overlayToggleControl || trackingLossControl))
                throw new InvalidOperationException("Synthetic controls require the simulator directory");
#endif
            NativeEyeFinalBlit.Install();
            DesktopMirror.Install();
            ButoEyeHistory.Install();
            // Run before URP's RenderGraph.EndFrame. RenderSingleCamera does not emit
            // Begin/EndCameraRendering; the re-entry guard also rejects nested callbacks.
            callback = (Action<ScriptableRenderContext, Camera>)OnCamera;
            RenderPipelineManager.add_endCameraRendering(callback);
#if IKAG_DIAGNOSTICS
            if (commandPath != null) File.WriteAllText(commandPath, gameplay ? "shot stereo-first\n" : "shot menu-first\n");
#endif
        }
        catch (Exception e) { Finish("ARM_FAILED " + e); }
    }

    internal static bool TryMirrorStack(ScriptableRenderContext context,Camera current)
    {
        if(!VrSettings.Mirror.Value||rendering||MotionPhotography.Capturing||callback==null||camera==null||current==null||
            current.Pointer!=camera.Pointer||current.targetTexture!=null||current.targetDisplay!=0||
            current.rect!=new Rect(0,0,1,1)||!DesktopMirror.Supported)return false;
        int previous=rendered;
        var trigger=gameplay&&overlayCamera!=null&&overlayCamera.isActiveAndEnabled?overlayCamera:camera;
        OnCamera(context,trigger);
        if(callback==null||rendered==previous)return false;
        DesktopMirror.Present(context,eyes.Targets[0],eyes.Parameters[0].Viewport);
        return true;
    }

    private static void OnCamera(ScriptableRenderContext context, Camera current)
    {
        if (rendering || MotionPhotography.Capturing || current == null) return;
        if (camera == null || (gameplay && overlayCamera == null)) { Finish("CAMERA_DESTROYED"); return; }
        // Preserve the game's active stack: when it disables the held-item
        // overlay, the base camera itself becomes the final camera of the stack.
        var trigger = gameplay && overlayCamera.isActiveAndEnabled ? overlayCamera : camera;
        if (current.Pointer != trigger.Pointer) return;
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
#if IKAG_DIAGNOSTICS
        long performanceStart=FramePerformanceAudit.BeginRender();
#endif
        try
        {
            if (cameraObject.scene.handle != sceneHandle)
            {
                // Netcode migrates the SAME local player/camera stack out of
                // Lobby and into Live. Keep its tracking reference and targets;
                // object lifetime, not scene membership, owns this render binding.
                if (!gameplay) throw new InvalidOperationException("Trial camera scene changed");
                sceneHandle = cameraObject.scene.handle;
                Write("CAMERA_MIGRATED scene=" + cameraObject.scene.name);
            }
            // Input/interaction code can cache an HMD pose in FixedUpdate or
            // Update. Unity refreshes XR tracking again before rendering; use
            // that render-phase pose once for the body and BOTH eye draws.
            headFrame=-1;
#if IKAG_DIAGNOSTICS
            ObjectGrabAudit.BeforeRender();
#endif
            MotionHeldItems.BeginRender();
            MotionBody.Begin();
            BeltModels.BeginRender();
            rendering = true;
            var saved = new CameraState(camera, cameraTransform);
            var savedOverlay = gameplay ? new CameraState(overlayCamera, overlayTransform) : default;
            bool savedOverlayEnabled = gameplay && overlayCamera.enabled;
            bool emptyOverlay = false;
            try
            {
                if(gameplay&&MotionInput.Enabled)
                {
                    // Held props share the world/hand depth buffer in VR. The
                    // native overlay clears depth and otherwise draws over both.
                    const int heldLayer=1<<10;
                    camera.cullingMask|=heldLayer;overlayCamera.cullingMask&=~heldLayer;
                    // Preserve the native final copy/UI without culling a camera
                    // that no longer draws geometry. Other overlay paths stay native.
                    if (screenUI && savedOverlayEnabled && overlayCamera.cullingMask == 0 &&
                        overlayData != null && !overlayData.renderPostProcessing)
                    {
                        overlayCamera.enabled = false;
                        emptyOverlay = true;
                    }
                }
                Vector3 headPosition = default;
                Quaternion headRotation = Quaternion.identity;
                bool tracked = false;
#if IKAG_DIAGNOSTICS
                bool traceHead = headMotionControl && (rendered == 29 || rendered == 59 || rendered == 89);
                uint headState = 0;
                if (headTracking || traceHead)
                    tracked = ReadHead(out headPosition, out headRotation, out headState);
                if (traceHead) Write($"HEAD_CONTROL pair={rendered} ORIGINAL_CAMERA_WORLD " + cameraTransform.localToWorldMatrix.ToString("R"));
#else
                if (headTracking)
                    tracked = ReadHead(out headPosition, out headRotation, out _);
#endif
                if (headTracking)
                {
                    if (!tracked || !ValidPose(headPosition, headRotation)) { WaitingForFrame = true; return; }
                    if (!referenceAligned && !AlignReference(headPosition, headRotation)) { WaitingForFrame = true; return; }
                    Vector3 deltaPosition = (referenceHeadRotationInverse * (headPosition - referenceHeadPosition))*TrackingScale;
                    Quaternion deltaRotation = referenceHeadRotationInverse * headRotation;
                    // Capture both anchors before changing their parent/child hierarchy.
                    Vector3 basePosition = cameraTransform.position;
                    Quaternion baseRotation = cameraTransform.rotation;
                    Vector3 overlayPosition = gameplay ? overlayTransform.position : default;
                    Quaternion overlayRotation = gameplay ? overlayTransform.rotation : Quaternion.identity;
                    cameraTransform.SetPositionAndRotation(basePosition + baseRotation * deltaPosition + SeatedOffset, baseRotation * deltaRotation);
                    if (gameplay) overlayTransform.SetPositionAndRotation(overlayPosition + overlayRotation * deltaPosition + SeatedOffset, overlayRotation * deltaRotation);
#if IKAG_DIAGNOSTICS
                    if (traceHead) Write($"HEAD_CONTROL pair={rendered} TRACKED_CENTER_WORLD " + cameraTransform.localToWorldMatrix.ToString("R"));
#endif
#if IKAG_DIAGNOSTICS
                    if (rendered == 0) PoseStabilityControl.TrackedCenter(cameraTransform);
#endif
                }
                if (!eyes.TryRefresh(camera)) { WaitingForFrame = true; return; }
                if (rendered == 0) Write("STAGE refresh");
                if (gameplay) eyes.RefreshParameters(overlayCamera, overlayParameters);
                for(int e=0;e<2;++e){ScaleEye(ref eyes.Parameters[e],cameraTransform.position);if(gameplay)ScaleEye(ref overlayParameters[e],overlayTransform.position);}
#if IKAG_DIAGNOSTICS
                VrGameplayAudit.Views(eyes.Parameters);
#endif
                if (fitUI) EyeScreenUI.Prepare(eyes.Parameters, Matrix4x4.TRS(cameraTransform.position, cameraTransform.rotation, Vector3.one));
#if IKAG_DIAGNOSTICS
                if (focusControl && (rendered == 29 || rendered == 59 || rendered == 89))
                {
                    Write($"FOCUS_CONTROL pair={rendered} " + Probe.SessionSnapshot());
                    File.Copy(Path.Combine(Path.GetDirectoryName(commandPath), "state.json"), Path.Combine(BepInEx.Paths.GameRootPath, "FocusControl-" + rendered + ".json"), true);
                }
                if (traceHead)
                {
                    Write($"HEAD_CONTROL pair={rendered} NATIVE_LEFT_VIEW " + eyes.Parameters[0].View.ToString("R"));
                    Write($"HEAD_INPUT pair={rendered} id={poseReader.Id} valid={poseReader.DeviceIsValid} hasState={poseReader.HasTrackingState} state={headState} tracked={tracked} pos={headPosition.ToString("R")} rotation={headRotation.ToString("R")}");
                }
                if (rendered == 0) Write("STAGE refresh-complete");
#endif
                // Native ResetAspect selects the XR eye viewport while stereoTargetEye
                // is nonzero, even after targetTexture is restored to the screen.
                // Keep this standalone mono URP render outside that camera XR branch;
                // restore its previous mask only after restoring the screen target.
                // The reconstructed public setter first enters an unrelated managed
                // pipeline-warning path. Use its verified native binding directly.
                camera.stereoTargetEyeInternal = StereoTargetEyeMask.None;
                if (gameplay) overlayCamera.stereoTargetEyeInternal = StereoTargetEyeMask.None;
                if (rendered == 0) Write("STAGE mono-mask-set");
                for (int eye = 0; eye < 2; ++eye)
                {
                    ref var parameter = ref eyes.Parameters[eye];
                    Matrix4x4 world = parameter.View.inverse;
                    Vector3 position = new Vector3(world.m03, world.m13, world.m23);
                    Vector3 forward = new Vector3(-world.m02, -world.m12, -world.m22);
                    Vector3 up = new Vector3(world.m01, world.m11, world.m21);
                    cameraTransform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
                    camera.targetTexture = eyes.Targets[eye];
                    camera.rect = parameter.Viewport;
                    camera.projectionMatrix = parameter.Projection;
                    var actualView = camera.worldToCameraMatrix;
                    var actualCulling = camera.cullingMatrix;
                    float viewError = Difference(actualView, parameter.View);
                    float cullError = Difference(actualCulling, parameter.Projection * parameter.View);
#if IKAG_DIAGNOSTICS
                    maximumViewError = Math.Max(maximumViewError, viewError);
                    maximumCullError = Math.Max(maximumCullError, cullError);
#endif
                    if (!PoseMatches(actualView, cameraTransform, parameter.View, world) || !CullingMatches(actualCulling, parameter.Projection, actualView))
                    {
                        Write("VIEW_MISMATCH_NATIVE " + parameter.View.ToString("R"));
                        Write("VIEW_MISMATCH_ACTUAL " + camera.worldToCameraMatrix.ToString("R"));
                        Write("VIEW_MISMATCH_INVERSE " + world.ToString("R"));
                        Write("VIEW_MISMATCH_CAMERA_WORLD " + cameraTransform.localToWorldMatrix.ToString("R"));
                        Write("VIEW_MISMATCH_PARENT " + cameraTransform.parent.localToWorldMatrix.ToString("R"));
                        throw new InvalidOperationException($"Automatic camera matrices do not match native eye: view={viewError:R} cull={cullError:R}");
                    }
#if IKAG_DIAGNOSTICS
                    if (rendered == 0) PoseStabilityControl.CullingControls(camera);
#endif
                    if (rendered == 0)
                    {
                        bool canCull = camera.TryGetCullingParameters(false, out var cull);
                        Write($"CULL {eye} valid={canCull} mask={camera.cullingMask} near={camera.nearClipPlane:R} far={camera.farClipPlane:R}");
                        var id = eyes.RenderTargets[eye];
                        Write($"TARGET_ID {eye} type={id.m_Type} instance={id.m_InstanceID} name={id.m_NameID} buffer=0x{id.m_BufferPointer.ToInt64():X} mip={id.m_MipLevel} slice={id.m_DepthSlice}");
                        if (!canCull) throw new InvalidOperationException("Native mono culling parameters unavailable");
                        Write($"EYE {eye} frame={lastFrame} target={eyes.Targets[eye].GetInstanceID()} size={eyes.Targets[eye].width}x{eyes.Targets[eye].height} viewError={viewError:R} cullError={cullError:R}");
                        Write("VIEW " + parameter.View.ToString("R"));
                        Write("PROJECTION " + parameter.Projection.ToString("R"));
                    }
                    if (gameplay)
                    {
                        ref var overlayParameter = ref overlayParameters[eye];
                        var overlayWorld = overlayParameter.View.inverse;
                        overlayTransform.SetPositionAndRotation(
                            new Vector3(overlayWorld.m03, overlayWorld.m13, overlayWorld.m23),
                            Quaternion.LookRotation(new Vector3(-overlayWorld.m02, -overlayWorld.m12, -overlayWorld.m22), new Vector3(overlayWorld.m01, overlayWorld.m11, overlayWorld.m21)));
                        // Match the existing base destination/viewport so URP does not
                        // apply its flat-screen overlay aspect correction.
                        overlayCamera.targetTexture = eyes.Targets[eye];
                        overlayCamera.rect = overlayParameter.Viewport;
                        overlayCamera.projectionMatrix = overlayParameter.Projection;
                        var actualOverlayView = overlayCamera.worldToCameraMatrix;
                        var actualOverlayCulling = overlayCamera.cullingMatrix;
                        float overlayViewError = Difference(actualOverlayView, overlayParameter.View);
                        float overlayCullError = Difference(actualOverlayCulling, overlayParameter.Projection * overlayParameter.View);
#if IKAG_DIAGNOSTICS
                        maximumViewError = Math.Max(maximumViewError, overlayViewError);
                        maximumCullError = Math.Max(maximumCullError, overlayCullError);
#endif
                        if (!PoseMatches(actualOverlayView, overlayTransform, overlayParameter.View, overlayWorld) || !CullingMatches(actualOverlayCulling, overlayParameter.Projection, actualOverlayView))
                            throw new InvalidOperationException($"Overlay native matrix mismatch: view={overlayViewError:R} cull={overlayCullError:R}");
                        if (rendered == 0) Write($"OVERLAY_EYE {eye} viewError={overlayViewError:R} cullError={overlayCullError:R} projection={overlayParameter.Projection.ToString("R")}");
                    }
                    int previousFlips = NativeEyeFinalBlit.Applied;
                    int previousUI = NativeEyeFinalBlit.UIApplied;
                    NativeEyeFinalBlit.Begin(eyes.RenderTargets[eye].m_InstanceID, emptyOverlay);
                    ButoEyeHistory.BeginEye(eye);
                    if (fitUI) EyeScreenUI.BeginEye(eye);
                    try
                    {
                        if (gameplay) UniversalRenderPipeline.RenderCameraStack(context, camera, true);
                        else UniversalRenderPipeline.RenderSingleCamera(context, camera);
                    }
                    finally { ButoEyeHistory.EndEye();NativeEyeFinalBlit.End(); }
                    if(UiEvaluationBridge.Failure!=null)throw new InvalidOperationException("UI evaluation bridge failed: "+UiEvaluationBridge.Failure);
                    if (UiCommandBridge.Failure!=null)throw new InvalidOperationException("UI command bridge failed: "+UiCommandBridge.Failure);
                    if (NativeEyeFinalBlit.Failure != null || NativeEyeFinalBlit.Applied != previousFlips + 1)
                        throw new InvalidOperationException($"Native-eye final-copy origin was not applied exactly once: calls={NativeEyeFinalBlit.Calls} applied={NativeEyeFinalBlit.Applied} failure={NativeEyeFinalBlit.Failure}");
                    if (screenUI && NativeEyeFinalBlit.UIApplied != previousUI + 1)
                        throw new InvalidOperationException("Native eye UI pass was not appended exactly once");
                    if (Time.frameCount != lastFrame) throw new InvalidOperationException("Simulation frame advanced inside render call");
                }
#if IKAG_DIAGNOSTICS
                if (readback && rendered == 14) TrialReadback.Capture(eyes, Write);
                if (clearControl && rendered >= 14 && rendered < 18)
                {
                    var commands = CommandBufferPool.Get("Stereo target pixel control");
                    try
                    {
                        for (int eye = 0; eye < 2; ++eye)
                        {
                            commands.SetRenderTarget(eyes.RenderTargets[eye]);
                            commands.ClearRenderTarget(true, true, eye == 0 ? Color.red : Color.green);
                        }
                        context.ExecuteCommandBuffer(commands);
                        context.Submit();
                    }
                    finally { CommandBufferPool.Release(commands); }
                    if (rendered == 14) Write($"DIRECT_TARGET_CLEAR_CONTROL native0=0x{eyes.Targets[0].GetNativeTexturePtr().ToInt64():X} native1=0x{eyes.Targets[1].GetNativeTexturePtr().ToInt64():X}");
                }
#endif
            }
            finally
            {
                if (emptyOverlay) overlayCamera.enabled = savedOverlayEnabled;
                saved.Restore(camera, cameraTransform);
                if (gameplay) savedOverlay.Restore(overlayCamera, overlayTransform);
                float error = saved.Verify(camera, cameraTransform);
                if (gameplay) error = Math.Max(error, savedOverlay.Verify(overlayCamera, overlayTransform));
#if IKAG_DIAGNOSTICS
                maximumRestoreError = Math.Max(maximumRestoreError, error);
#endif
                if (error > 0.0002f) throw new InvalidOperationException("Camera restoration mismatch: " + error.ToString("R"));
            }
            ++rendered;
            WaitingForFrame = false;
            if (continuous && rendered == 240) Write("CONTINUOUS_RUNNING pairs=240");
#if IKAG_DIAGNOSTICS
            if (trackingLossControl && rendered == 240) File.WriteAllText(commandPath, "head valid off\n");
            if (focusControl && rendered == 30) File.WriteAllText(commandPath, "focus lose\n");
            if (focusControl && rendered == 60) File.WriteAllText(commandPath, "focus regain\n");
            if (equipControl && rendered == 8)
            {
                originalSlot = inventory.GetCurrentSelectedSlot();
                equipSelected = true;
                inventory.SetSelectedSlot(0);
                Write("EQUIP_CONTROL_SELECTED_SLOT_0 previousSlot=" + originalSlot);
            }
            if (equipControl && rendered == 12)
            {
                var item = inventory.GetItemInSlot(0);
                if (item == null) throw new InvalidOperationException("Equipped item missing");
                Write($"EQUIP_CONTROL_ITEM name={item.name} parent={(item.transform.parent == null ? "null" : item.transform.parent.name)} cameraChild={item.transform.IsChildOf(cameraTransform)} world={item.transform.position.ToString("R")}");
                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                    Write($"EQUIP_RENDERER name={renderer.name} active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} layer={renderer.gameObject.layer} center={renderer.bounds.center.ToString("R")}");
            }
            if (overlayToggleControl && gameplay)
            {
                if (rendered == 30) { overlayCamera.enabled = false; Write("OVERLAY_CONTROL_DISABLED"); }
                if (rendered == 60) File.WriteAllText(commandPath, "shot stereo-no-overlay\n");
                if (rendered == 90) { overlayCamera.enabled = originalOverlayEnabled; Write("OVERLAY_CONTROL_RESTORED"); }
            }
            if (headMotionControl && rendered == 30) File.WriteAllText(commandPath, "head rot 20 0 0\n");
            if (headMotionControl && rendered == 60) File.WriteAllText(commandPath, "head pos 0.1 1.6 0\n");
            PoseStabilityControl.Frame(gameplay, rendered, commandPath);
            if (renderDoc && rendered == 30) { RenderDocCapture.Trigger(); Write("RENDERDOC_NEXT_FRAME_REQUESTED"); }
            // Two bounded screenshots, no capture/logging in a release render path.
            if (commandPath != null && (rendered == 15 || rendered == 90))
                File.WriteAllText(commandPath, "shot " + (gameplay ? "stereo-trial-" : "menu-trial-") + rendered + "\n");
            if (!continuous && rendered == FrameLimit) Finish("COMPLETE");
#endif
        }
        catch (Exception e) { Finish("FAILED " + e); }
        finally
        {
            try{BeltModels.EndRender();MotionBody.End();}finally{MotionHeldItems.EndRender();rendering=false;}
#if IKAG_DIAGNOSTICS
            OpenXRFrameAudit.Rendered(Time.frameCount,gameplay,cachedHeadPosition,cachedHeadRotation,cameraTransform);
            FramePerformanceAudit.EndRender(performanceStart);
#endif
        }
    }

    private static void Finish(string result)
    {
        WaitingForFrame = false;
        IntroScreen.Restore();
        if (callback != null)
        {
            RenderPipelineManager.remove_endCameraRendering(callback);
            callback = null;
        }
        NativeEyeFinalBlit.Remove();
        ButoEyeHistory.Clear();
        MotionHeldItems.Reset();
#if IKAG_DIAGNOSTICS
        if (overlayToggleControl && overlayCamera != null) overlayCamera.enabled = originalOverlayEnabled;
        if (equipSelected && inventory != null)
        {
            inventory.SetSelectedSlot(originalSlot);
            equipSelected = false;
            Write("EQUIP_CONTROL_SLOT_RESTORED expected=" + originalSlot + " actual=" + inventory.GetCurrentSelectedSlot());
        }
#endif
        poseReader?.Dispose(); poseReader = null;
#if IKAG_DIAGNOSTICS
        Write($"{result} pairs={rendered} maxViewError={maximumViewError:R} maxCullError={maximumCullError:R} maxPositionError={maximumPositionError:R} maxBasisError={maximumBasisError:R} maxRestoreError={maximumRestoreError:R} uiDraws={EyeScreenUI.Draws} uiScissors={EyeScreenUI.Scissors} CALLBACK_REMOVED");
#else
        Write($"{result} pairs={rendered} CALLBACK_REMOVED");
#endif
    }

    private static void Write(string message) => File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " " + message + "\n");

    private static bool ValidPose(Vector3 position, Quaternion rotation)
    {
        float norm = Quaternion.Dot(rotation, rotation);
        return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z)
            && float.IsFinite(norm) && Math.Abs(norm - 1) < 0.001f;
    }

    private static bool ReadHead(out Vector3 position, out Quaternion rotation, out uint state
#if IKAG_DIAGNOSTICS
        ,[System.Runtime.CompilerServices.CallerMemberName]string caller=null
#endif
    )
    {
        if (headFrame != Time.frameCount)
        {
            headFrame = Time.frameCount;
            cachedHeadValid = poseReader.TryRead(out cachedHeadPosition, out cachedHeadRotation, out cachedHeadState);
#if IKAG_DIAGNOSTICS
            OpenXRFrameAudit.FirstRead(caller);
#endif
        }
        position = cachedHeadPosition; rotation = cachedHeadRotation; state = cachedHeadState;
        return cachedHeadValid;
    }

    internal static bool TryInteractionPose(Camera target, out Transform transform, out Vector3 position, out Quaternion rotation)
    {
        transform = null; position = default; rotation = Quaternion.identity;
        if (!gameplay || callback == null || rendering || !headTracking || !referenceAligned ||
            target == null || camera == null || target.Pointer != camera.Pointer || poseReader == null) return false;
        if (!ReadHead(out var headPosition, out var headRotation, out _) || !ValidPose(headPosition, headRotation)) return false;
        transform = cameraTransform;
        var anchorRotation = transform.rotation;
        position = transform.position + anchorRotation * (referenceHeadRotationInverse * (headPosition - referenceHeadPosition))*TrackingScale + SeatedOffset;
        rotation = anchorRotation * (referenceHeadRotationInverse * headRotation);
        return true;
    }

    internal static bool TryTrackingPose(Vector3 localPosition, Quaternion localRotation, out Vector3 position, out Quaternion rotation)
    {
        position=default;rotation=Quaternion.identity;
        if(callback==null||rendering||!referenceAligned||cameraTransform==null||poseReader==null||!ReadHead(out var h,out var q,out _)||!ValidPose(h,q)||!ValidPose(localPosition,localRotation)) return false;
        var anchor=cameraTransform.rotation;
        position=cameraTransform.position+anchor*(referenceHeadRotationInverse*(localPosition-referenceHeadPosition))*TrackingScale+SeatedOffset;
        rotation=anchor*(referenceHeadRotationInverse*localRotation);return true;
    }
    internal static bool TryHeadWorld(out Vector3 position,out Quaternion rotation)
    {
        position=default;rotation=Quaternion.identity;
        return poseReader!=null&&ReadHead(out var p,out var q,out _)&&TryTrackingPose(p,q,out position,out rotation);
    }
    internal static bool TryBodyDelta(out Vector3 position,out Quaternion rotation)
    {
        if(TryHeadWorld(out position,out rotation))
        {position-=cameraTransform.position;rotation*=Quaternion.Inverse(cameraTransform.rotation);return true;}
        return false;
    }

    private static bool AlignReference(Vector3 position, Quaternion rotation)
    {
        var reference = rotation;
        if (gameplay)
        {
            // Gravity fixes pitch and roll. Only the headset's heading is a
            // recenter reference; the live pose retains all three rotation axes.
            var forward = rotation * Vector3.forward;
            forward.y = 0;
            // A head within about ten degrees of vertical has no stable heading.
            // Defer this one-time alignment rather than inventing a yaw.
            if (forward.sqrMagnitude < 0.03f) return false;
            reference = Quaternion.LookRotation(forward, Vector3.up);
        }
        referenceHeadPosition = position;
        standingReferenceY=position.y;seatedApplied=gameplay&&(VrSettings.Seated?.Value??false);seatedCalibrate=false;
        referenceHeadRotationInverse = Quaternion.Inverse(reference);
        referenceAligned = true;
        Write($"HEAD_REFERENCE position={position.ToString("R")} rotation={rotation.ToString("R")} reference={reference.ToString("R")} axes={(gameplay ? "yaw" : "authored-menu")}");
        return true;
    }

#if IKAG_DIAGNOSTICS
    internal static void DiagnosticMetrics() => Write($"POSE_METRICS pairs={rendered} maxViewError={maximumViewError:R} maxCullError={maximumCullError:R} maxPositionError={maximumPositionError:R} maxBasisError={maximumBasisError:R} maxRestoreError={maximumRestoreError:R}");
#endif

    private static bool PoseMatches(Matrix4x4 actualView, Transform transform, Matrix4x4 nativeView, Matrix4x4 requestedWorld)
    {
        // View translation is -R*p: rounding of R is amplified at large world
        // coordinates. Validate the actual pose instead of an origin-sized fixed
        // threshold on that derived translation. Keep the full matrix error logged.
        var position = transform.position;
        float basisError = 0;
        for (int row = 0; row < 3; ++row)
            for (int column = 0; column < 3; ++column)
                basisError = Math.Max(basisError, Math.Abs(ReadComponent(ref actualView,row+4*column) - ReadComponent(ref nativeView,row+4*column)));
        float xError = Math.Abs(position.x - requestedWorld.m03);
        float yError = Math.Abs(position.y - requestedWorld.m13);
        float zError = Math.Abs(position.z - requestedWorld.m23);
#if IKAG_DIAGNOSTICS
        maximumPositionError = Math.Max(maximumPositionError, Math.Max(xError, Math.Max(yError, zError)));
        maximumBasisError = Math.Max(maximumBasisError, basisError);
#endif
        // Ragdoll reparents the cameras under the animated neck. Composing and
        // inverting that longer float transform chain introduces tiny basis
        // stretch as well as rotation error (measured 2.50e-6 in native impacts).
        // A 1e-5 component budget bounds each unit-axis error to sqrt(3)*1e-5,
        // approximately 0.001 degrees. This is a numerical validation tolerance,
        // not a change to the rendered pose. Position and culling guards remain.
        const float basisRoundoffBudget = 0.00001f;
        return basisError <= basisRoundoffBudget && xError <= PositionBudget(requestedWorld.m03)
            && yError <= PositionBudget(requestedWorld.m13) && zError <= PositionBudget(requestedWorld.m23);

    }

    private static float PositionBudget(float value)
    {
        value = Math.Abs(value);
        return Math.Max(0.00002f, 4 * (MathF.BitIncrement(value) - value));
    }

    internal static bool CullingMatches(Matrix4x4 culling, Matrix4x4 projection, Matrix4x4 view)
    {
        // PoseMatches independently checks the physical eye against the native
        // pose. Check culling against THAT camera view, not the native view's
        // slightly different floating-point basis: -R*p magnifies basis rounding
        // at world coordinates, and a fixed absolute cutoff stops valid rendering.
        // A four-term float dot product takes four multiplies and three adds.
        // Bound two evaluations by 2*gamma(7)*sum(abs(products)), evaluated in
        // double here so cancellation does not turn this into a relative test.
        const double u = 1.0 / 16777216.0;
        const double errorFactor = 2 * (7 * u / (1 - 7 * u));
        for (int row = 0; row < 4; ++row)
            for (int column = 0; column < 4; ++column)
            {
                double expected = 0, magnitude = 0;
                for (int k = 0; k < 4; ++k)
                {
                    double term = (double)ReadComponent(ref projection,row+4*k) * ReadComponent(ref view,k+4*column);
                    expected += term; magnitude += Math.Abs(term);
                }
                double actual = ReadComponent(ref culling,row+4*column);
                if (!double.IsFinite(expected) || !double.IsFinite(actual) ||
                    Math.Abs(actual - expected) > errorFactor * magnitude + 8 * (double)float.Epsilon) return false;
            }
        return true;

    }

    private static float Difference(Matrix4x4 a, Matrix4x4 b)
    {
        float result = 0;
        for (int i = 0; i < 16; ++i)
        {
            float error = Math.Abs(ReadComponent(ref a,i) - ReadComponent(ref b,i));
            if (!float.IsFinite(error)) throw new InvalidOperationException("Nonfinite camera matrix");
            result = Math.Max(result, error);
        }
        return result;

    }

    private static bool matrixLayoutChecked;
    private static void ValidateMatrixLayout()
    {
        if(matrixLayoutChecked)return;
        if(System.Runtime.InteropServices.Marshal.SizeOf<Matrix4x4>()!=64)throw new InvalidOperationException("Unsupported matrix layout size");
        for(int column=0;column<4;column++)for(int row=0;row<4;row++)
            if(System.Runtime.InteropServices.Marshal.OffsetOf<Matrix4x4>("m"+row+column).ToInt32()!=4*(row+4*column))throw new InvalidOperationException("Unsupported matrix component offset");
        matrixLayoutChecked=true;
    }
    private static unsafe float ReadComponent(ref Matrix4x4 matrix,int index)
    {fixed(Matrix4x4* pointer=&matrix)return ((float*)pointer)[index];}
#if IKAG_DIAGNOSTICS
    private static unsafe void ValidateMatrixAccess()
    {
        var edges=new[]{0,int.MinValue,1,unchecked((int)0x80000001),0x007fffff,0x00800000,0x7f7fffff,unchecked((int)0xff7fffff),0x7f800000,unchecked((int)0xff800000),0x7fc00000,0x7f800001,unchecked((int)0xffc0a123),unchecked((int)0xff800001),0x3f800000,unchecked((int)0xbf800000)};
        var random=new System.Random(60114);int reads=0;
        for(int n=0;n<256;n++){Matrix4x4 matrix=default;float* values=(float*)&matrix;for(int i=0;i<16;i++)values[i]=BitConverter.Int32BitsToSingle(n==0?edges[i]:random.Next()^(random.Next(2)==0?int.MinValue:0));for(int row=0;row<4;row++)for(int column=0;column<4;column++){int i=row+4*column;int bits=BitConverter.SingleToInt32Bits(ReadComponent(ref matrix,i));if(bits!=BitConverter.SingleToInt32Bits(matrix[i])||bits!=BitConverter.SingleToInt32Bits(matrix[row,column]))throw new InvalidOperationException("Matrix component mismatch");reads+=2;}}
        Plugin.Line("MATRIX_COMPONENT_EQUIVALENCE_PASS reads="+reads);
    }
#endif

    private readonly struct CameraState
    {
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly Matrix4x4 projection, nonJittered, view, culling;
        private readonly Camera.ProjectionMatrixMode projectionMode;
        private readonly StereoTargetEyeMask stereoTargetEye;
        private readonly RenderTexture target;
        private readonly Rect rect;
        private readonly float fov, aspect;
        private readonly int cullingMask;
        internal CameraState(Camera camera, Transform transform)
        {
            localPosition = transform.localPosition; localRotation = transform.localRotation;
            projection = camera.projectionMatrix; nonJittered = camera.nonJitteredProjectionMatrix;
            view = camera.worldToCameraMatrix; culling = camera.cullingMatrix;
            projectionMode = camera.projectionMatrixMode; target = camera.targetTexture;
            stereoTargetEye = camera.stereoTargetEye;
            rect = camera.rect; fov = camera.fieldOfView; aspect = camera.aspect;
            cullingMask=camera.cullingMask;
        }
        internal void Restore(Camera camera, Transform transform)
        {
            // Never write view/culling matrices or change their implicit/override flags.
            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            camera.targetTexture = target;
            camera.cullingMask=cullingMask;
            camera.rect = rect;
            if (projectionMode == Camera.ProjectionMatrixMode.Explicit) camera.projectionMatrix = projection;
            else camera.ResetProjectionMatrix();
            if (Difference(camera.nonJitteredProjectionMatrix, nonJittered) != 0)
                camera.nonJitteredProjectionMatrix = nonJittered;
            camera.stereoTargetEyeInternal = stereoTargetEye;
        }
        internal float Verify(Camera camera, Transform transform)
        {
            if (camera.cullingMask!=cullingMask||camera.projectionMatrixMode != projectionMode || camera.targetTexture != target || camera.rect != rect || camera.fieldOfView != fov || camera.aspect != aspect || camera.stereoTargetEye != stereoTargetEye)
                throw new InvalidOperationException($"Camera restoration fields: mode={projectionMode}->{camera.projectionMatrixMode} target={(target == null ? 0 : target.Pointer)}->{(camera.targetTexture == null ? 0 : camera.targetTexture.Pointer)} rect={rect}->{camera.rect} fov={fov:R}->{camera.fieldOfView:R} aspect={aspect:R}->{camera.aspect:R} stereoMask={stereoTargetEye}->{camera.stereoTargetEye}");
            float projectionError = Difference(camera.projectionMatrix, projection);
            float nonJitteredError = Difference(camera.nonJitteredProjectionMatrix, nonJittered);
            float viewError = Difference(camera.worldToCameraMatrix, view);
            float cullingError = Difference(camera.cullingMatrix, culling);
            if (Math.Max(Math.Max(projectionError, nonJitteredError), Math.Max(viewError, cullingError)) > 0.0002f)
            {
                Write($"RESTORE_ERRORS projection={projectionError:R} nonJittered={nonJitteredError:R} view={viewError:R} culling={cullingError:R}");
                Write("SAVED_PROJECTION " + projection.ToString("R") + "ACTUAL_PROJECTION " + camera.projectionMatrix.ToString("R"));
                Write("SAVED_VIEW " + view.ToString("R") + "ACTUAL_VIEW " + camera.worldToCameraMatrix.ToString("R"));
                Write("SAVED_CULLING " + culling.ToString("R") + "ACTUAL_CULLING " + camera.cullingMatrix.ToString("R"));
            }
            float error = Math.Max(Math.Max(projectionError, nonJitteredError), Math.Max(viewError, cullingError));
            error = Math.Max(error, (transform.localPosition - localPosition).magnitude);
            // Dot(q,q) can differ from 1 even when every stored component is
            // identical. Compare components, allowing the equivalent -q form.
            var rotation = transform.localRotation;
            float same = Math.Max(Math.Max(Math.Abs(rotation.x - localRotation.x), Math.Abs(rotation.y - localRotation.y)), Math.Max(Math.Abs(rotation.z - localRotation.z), Math.Abs(rotation.w - localRotation.w)));
            float negated = Math.Max(Math.Max(Math.Abs(rotation.x + localRotation.x), Math.Abs(rotation.y + localRotation.y)), Math.Max(Math.Abs(rotation.z + localRotation.z), Math.Abs(rotation.w + localRotation.w)));
            error = Math.Max(error, Math.Min(same, negated));
            return error;
        }
    }
}
