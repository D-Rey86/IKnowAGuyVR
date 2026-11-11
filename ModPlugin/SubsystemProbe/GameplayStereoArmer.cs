using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IKnowAGuyVR.SubsystemProbe;

// Wait for the actual spawned gameplay camera, without a per-frame global search.
internal static class GameplayStereoArmer
{
    private static IntPtr display;
    private static readonly HashSet<int> observed = new();
#if IKAG_DIAGNOSTICS
    private static Camera waitingCamera;
#endif
    private static Camera activeCamera;
#if IKAG_DIAGNOSTICS
    private static InventoryManager waitingInventory;
    private static bool waitForSavedItem;
    private static bool includeMenu;
    private static float waitDeadline;
#endif
    private static Il2CppSystem.Action<ScriptableRenderContext, Camera> callback;
    internal static void Begin(IntPtr displayObject)
    {
        display = displayObject;
        observed.Clear();
#if IKAG_DIAGNOSTICS
        waitingCamera = null; waitingInventory = null;
#endif
        activeCamera = null;
#if IKAG_DIAGNOSTICS
        includeMenu = Probe.AutomaticGameplay || Environment.GetEnvironmentVariable("IKAG_XR_MENU_STEREO") == "1";
        waitForSavedItem = Environment.GetEnvironmentVariable("IKAG_XR_EQUIP_CONTROL") == "1";
#endif
        SceneManager.sceneLoaded += (Action<Scene, LoadSceneMode>)((scene, mode) => observed.Clear());
        callback = (Action<ScriptableRenderContext, Camera>)Observe;
        RenderPipelineManager.add_endCameraRendering(callback);
    }
    private static void Observe(ScriptableRenderContext context, Camera camera)
    {
        try { ObserveCore(camera); }
        catch (Exception e)
        {
            RenderPipelineManager.remove_endCameraRendering(callback);
            callback = null;
            Plugin.Line("GAMEPLAY_ARM_FAILED " + e);
        }
    }
    private static void ObserveCore(Camera camera)
    {
        if (camera == null || (activeCamera != null && camera.Pointer == activeCamera.Pointer)) return;
#if IKAG_DIAGNOSTICS
        if (waitingCamera != null)
        {
            if (Time.realtimeSinceStartup > waitDeadline) throw new TimeoutException("Expected saved visual-control item was not restored");
            if (camera == null || camera.Pointer != waitingCamera.Pointer || waitingInventory.IsSlotEmpty(0)) return;
            Arm(waitingCamera);
            return;
        }
#endif
        if (!observed.Add(camera.GetInstanceID())) return;
        bool gameplay = camera.name == "MainCam" && (camera.gameObject.scene.name == "Live" || camera.gameObject.scene.name == "Lobby");
#if IKAG_DIAGNOSTICS
        bool menu = includeMenu && camera.name == "Setup2CAM" && camera.gameObject.scene.name == "MainMenu";
#else
        bool menu = camera.name == "Setup2CAM" && camera.gameObject.scene.name == "MainMenu";
#endif
        if (!gameplay && !menu) return;
#if IKAG_DIAGNOSTICS
        if (waitForSavedItem && gameplay)
        {
            waitingInventory = camera.GetComponentInParent<InventoryManager>();
            if (waitingInventory == null) throw new InvalidOperationException("Expected local player inventory");
            waitingCamera = camera;
            waitDeadline = Time.realtimeSinceStartup + 30;
            if (waitingInventory.IsSlotEmpty(0)) return;
        }
#endif
        Arm(camera);
    }
    private static void Arm(Camera camera)
    {
        if(MotionInput.Enabled)MotionInput.BindMenu(camera.gameObject.scene.name=="MainMenu"?UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>():null);
        activeCamera = camera;
#if IKAG_DIAGNOSTICS
        waitingCamera = null; waitingInventory = null;
#endif
        Plugin.Line("STEREO_CAMERA_SELECTED scene=" + camera.gameObject.scene.name + " camera=" + camera.name);
        StereoTrial.Begin(display, camera);
    }
}
