using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IKnowAGuyVR.SubsystemProbe;

// Read-only render inventory. The optional singleplayer command uses the game's
// own existing menu action, only in the isolated diagnostic copy with save backup.
public sealed class GameplayRenderAudit : MonoBehaviour
{
    private readonly HashSet<int> observed = new();
    private Il2CppSystem.Action<ScriptableRenderContext, Camera> callback;
    private string path;
    private bool started;
    private float startAt;
    private bool nativeAudit, surfacesObserved;
    private bool waitForMenu;
    private InventoryManager pendingInventory;
    private float inventoryReadAt;
    private bool motionObserved;
    private int newPhase;private UI.MainMenu.MainMenuManager newMenu;
    private Canvas openingFade;private bool openingFadeEnabled;
    private void OpeningCheck()
    {
        try
        {
            switch(newPhase)
            {
                case 0:
                    if(!StereoTrial.MenuRendered)return;
                    newMenu=UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();
                    UI.MainMenu.SingleplayerPageController.SetSingleplayerHostConfig();
                    newMenu.singleplayerController.HostSingleplayerAsync("IKAG isolated opening audit");break;
                case 1:
                    if(InteractionManager.LocalInstance==null||InteractionManager.LocalInstance.gameObject.scene.name!="Lobby")return;
                    Write("OPENING_READY camera="+InteractionManager.LocalInstance.interactionCamera.GetInstanceID());
                    UnityEngine.Object.FindObjectOfType<Gameplay.Lobby.LobbyDoor>().OnInteract();break;
                default:
                    var local=InteractionManager.LocalInstance;
                    Write("OPENING_STATE scene="+local?.gameObject.scene.name+" camera="+local?.interactionCamera?.GetInstanceID()+" stereo="+StereoTrial.GameplayActive);
                    foreach(var c in Camera.allCameras)Write("OPENING_CAMERA "+Hierarchy(c.transform)+" scene="+c.gameObject.scene.name+" id="+c.GetInstanceID());
                    var tutorial=TutorialManager.Instance;
                    if(newPhase==3&&tutorial!=null&&tutorial.introVideoPlayer!=null)
                        TrialReadback.Read(tutorial.introVideoPlayer.texture,"intro-source",Write);
                    if(newPhase==3&&Environment.GetEnvironmentVariable("IKAG_XR_INTRO_GPU")=="1")RenderDocCapture.Trigger();
                    if(tutorial!=null)Write("OPENING_TUTORIAL canvas="+tutorial.introVideoCanvas?.name+" texture="+tutorial.introRenderTexture?.name+" black="+tutorial.introScreenBlacked);
                    foreach(var c in UnityEngine.Object.FindObjectsOfType<Canvas>())Write("OPENING_CANVAS "+Hierarchy(c.transform)+" mode="+c.renderMode+" camera="+c.worldCamera?.name+" size="+c.GetComponent<RectTransform>()?.rect);
                    if(Environment.GetEnvironmentVariable("IKAG_XR_INTRO_BINOCULAR_AUDIT")=="1"&&newPhase==3)
                    {
                        var fade=ScreenFadeController._instance;
                        if(fade!=null&&fade.fadeImage!=null)
                        {
                            openingFade=fade.fadeImage.canvas;openingFadeEnabled=openingFade.enabled;
                            Write("INTRO_FADE_CONTROL color="+fade.fadeImage.color+" sort="+openingFade.sortingOrder+" mode="+openingFade.renderMode+" videoSort="+IntroScreen.Canvas?.sortingOrder);
                            openingFade.enabled=false;
                        }
                    }
                    if(newPhase==4&&openingFade!=null)openingFade.enabled=openingFadeEnabled;
                    if(newPhase==2&&Environment.GetEnvironmentVariable("IKAG_XR_INTRO_BINOCULAR_AUDIT")!="1")
                    {
                        for(int i=0;i<32;i++)Write("LAYER "+i+" "+LayerMask.LayerToName(i));
                        foreach(var b in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                        {
                            var joints=b.GetComponents<Joint>();
                            if(joints.Length==0&&!b.name.ToLowerInvariant().Contains("door"))continue;
                            Write("PHYSICS_BODY "+Hierarchy(b.transform)+" layer="+b.gameObject.layer+" kinematic="+b.isKinematic+" mass="+b.mass+" position="+b.position+" joints="+joints.Length);
                            foreach(var j in joints)Write("PHYSICS_JOINT "+j.GetIl2CppType().FullName+" axis="+j.axis+" anchor="+j.anchor+" connected="+j.connectedBody?.name);
                            foreach(var c in b.GetComponentsInChildren<Collider>(true))Write("PHYSICS_COLLIDER "+Hierarchy(c.transform)+" type="+c.GetIl2CppType().FullName+" layer="+c.gameObject.layer+" trigger="+c.isTrigger+" bounds="+c.bounds);
                        }
                    }
                    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"shot opening-"+newPhase+"\n");
                    if(newPhase>=5){enabled=false;Write("OPENING_CENSUS_COMPLETE");return;}break;
            }
            newPhase++;startAt=Time.realtimeSinceStartup+12;
        }
        catch(Exception e){Write("OPENING_FAILED "+e);enabled=false;}
    }
    private void NewGameCheck()
    {
        try
        {
            void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
            switch(newPhase)
            {
                case 0:newMenu=UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();newMenu.OnSingleplayerButtonClicked();break;
                case 1:newMenu.singleplayerController.saveBrowser.SelectCampaign(null);newMenu.singleplayerController.saveBrowser.FocusNameInput();break;
                case 2:if(!ComputerKeyboard.IsOpen)throw new Exception("New-save keyboard did not open");Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("clear")));break;
                case 3:Send("grip r 1");break;
                case 4:Send("grip r 0");break;
                case 5:Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("q")));break;
                case 6:Send("grip r 1");break;
                case 7:Send("grip r 0\nshot new-save-keyboard");if(newMenu.singleplayerController.saveBrowser.campaignNameInput.value!="q")throw new Exception("New-save keyboard native typing failed");Write("NEW_SAVE_KEYBOARD_NATIVE_PASS");break;
                case 8:Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("close")));break;
                case 9:Send("grip r 1");break;
                case 10:Send("grip r 0");if(ComputerKeyboard.IsOpen)throw new Exception("Menu keyboard close failed");UI.MainMenu.SingleplayerPageController.SetSingleplayerHostConfig();newMenu.singleplayerController.HostSingleplayerAsync("IKAG isolated new-game audit");startAt=Time.realtimeSinceStartup+8;newPhase++;return;
                case 11:if(!StereoTrial.GameplayActive||InteractionManager.LocalInstance==null||InteractionManager.LocalInstance.gameObject.scene.name!="Lobby")throw new Exception("Lobby stereo not active");Send("shot new-game-lobby\nhead move .1 0 0");break;
                case 12:Send("shot new-game-lobby-moved");Write("NEW_GAME_ACCEPTANCE_COMPLETE");enabled=false;return;
            }
            newPhase++;startAt=Time.realtimeSinceStartup+.6f;
        }
        catch(Exception e){Write("NEW_GAME_CHECK_FAILED "+e);enabled=false;}
    }

    public GameplayRenderAudit(IntPtr pointer) : base(pointer) { }

    private void Awake()
    {
        EngineBinaryContract.ValidateBinary();
        HeadPipelineAudit.Install();
        path = Path.Combine(BepInEx.Paths.GameRootPath, "GameplayRenderAudit.log");
        File.WriteAllText(path, "Read-only current-game camera rendering inventory\n");
        Write($"PROFILE company={Application.companyName} product={Application.productName} path={Application.persistentDataPath}");
        callback = (Action<ScriptableRenderContext, Camera>)OnCamera;
        RenderPipelineManager.add_beginCameraRendering(callback);
        startAt = Time.realtimeSinceStartup + 8f;
        nativeAudit = Environment.GetEnvironmentVariable("IKAG_XR_GAMEPLAY_NATIVE_AUDIT") == "1";
        waitForMenu = Environment.GetEnvironmentVariable("IKAG_XR_MENU_STEREO") == "1";
    }

    private void Update()
    {
        if(Environment.GetEnvironmentVariable("IKAG_XR_NEW_GAME_AUDIT")=="opening")
        {if(Time.realtimeSinceStartup>=startAt)OpeningCheck();return;}
        if(Environment.GetEnvironmentVariable("IKAG_XR_NEW_GAME_AUDIT")=="keyboard")
        {if(Time.realtimeSinceStartup>=startAt&&(newPhase>0||StereoTrial.MenuRendered))NewGameCheck();return;}
        if (pendingInventory != null && Time.realtimeSinceStartup >= inventoryReadAt)
        {
            DumpInventory(pendingInventory, "delayed");
            pendingInventory = null;
            enabled = false;
        }
        if (started || Time.realtimeSinceStartup < startAt) return;
        if (waitForMenu && !StereoTrial.MenuRendered) return;
        started = true;
        enabled = false;
        if (Environment.GetEnvironmentVariable("IKAG_XR_AUDIT_SINGLEPLAYER") != "1") return;
        try
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "gameplay-audit-backup.json")))
                throw new InvalidOperationException("Gameplay save-backup marker required");
            // One cold search; no repeated object search or input modification.
            var menu = UnityEngine.Object.FindObjectOfType<UI.MainMenu.MainMenuManager>();
            if (menu == null || menu.singleplayerController == null)
                throw new InvalidOperationException("Initialized singleplayer menu missing");
            if(Environment.GetEnvironmentVariable("IKAG_XR_NEW_GAME_AUDIT")=="1")
            {Write("START_NEW_SINGLEPLAYER");UI.MainMenu.SingleplayerPageController.SetSingleplayerHostConfig();menu.singleplayerController.HostSingleplayerAsync("IKAG isolated new-game audit");}
            else {Write("START_SINGLEPLAYER_EXISTING_SAVE index=0");menu.singleplayerController.HostSingleplayerSilently("IKAG rendering audit", 0);}
        }
        catch (Exception e) { Write("SESSION_FAILED " + e); }
    }

    private void OnCamera(ScriptableRenderContext context, Camera camera)
    {
        HeadPipelineAudit.Sample(camera);
        if (!motionObserved && camera != null && camera.gameObject.scene.name == "Live" && Time.realtimeSinceStartup > startAt + 12 && Environment.GetEnvironmentVariable("IKAG_XR_MOTION_AUDIT") == "1")
        {
            var local = InventoryManager.LocalInstance;
            if (local != null) { motionObserved = true; MotionRigAudit.Run(local); }
        }
        if (camera == null || !observed.Add(camera.GetInstanceID())) return;
        try
        {
            if(Environment.GetEnvironmentVariable("IKAG_XR_NEW_GAME_AUDIT")=="1")foreach(var doc in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())Write("NEW_GAME_DOC scene="+doc.gameObject.scene.name+" name="+doc.name+" mode="+doc.panelSettings?.renderMode+" target="+doc.panelSettings?.targetTexture?.name);
            Write($"CAMERA frame={Time.frameCount} scene={camera.gameObject.scene.name} path={Hierarchy(camera.transform)} tag={camera.tag} enabled={camera.enabled} depth={camera.depth:R} mask=0x{camera.cullingMask:X8} clear={camera.clearFlags} near={camera.nearClipPlane:R} far={camera.farClipPlane:R} fov={camera.fieldOfView:R} aspect={camera.aspect:R} rect={camera.rect} target={(camera.targetTexture == null ? "screen" : camera.targetTexture.name)}");
            Write("PROJECTION " + camera.projectionMatrix.ToString("R"));
            if (camera.gameObject.scene.name == "Live")
            {
                if (nativeAudit)
                {
                    var eyes = Probe.ReadNativeEyes(camera);
                    for (int eye = 0; eye < 2; ++eye)
                        Write($"NATIVE_EYE camera={camera.name} eye={eye} size={eyes.Targets[eye].width}x{eyes.Targets[eye].height} view={eyes.Parameters[eye].View.ToString("R")} projection={eyes.Parameters[eye].Projection.ToString("R")}");
                }
                if (!surfacesObserved)
                {
                    surfacesObserved = true;
                    DumpSurfaces();
                    foreach (var child in camera.GetComponentsInChildren<Transform>(true))
                        Write($"CAMERA_CHILD path={Hierarchy(child)} active={child.gameObject.activeInHierarchy} layer={child.gameObject.layer} localPosition={child.localPosition.ToString("R")}");
                    pendingInventory = camera.GetComponentInParent<InventoryManager>();
                    if (pendingInventory != null)
                    {
                        DumpInventory(pendingInventory, "first-camera");
                        inventoryReadAt = Time.realtimeSinceStartup + 2;
                        enabled = true;
                    }
                }
            }
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) { Write("NO_URP_DATA"); return; }
            Write($"URP type={data.renderType} aa={data.antialiasing} post={data.renderPostProcessing} shadows={data.renderShadows} clearDepth={data.clearDepth} requiresColor={data.requiresColorTexture} requiresDepth={data.requiresDepthTexture}");
            if (data.renderType == CameraRenderType.Base)
            {
                var stack = data.cameraStack;
                Write("STACK count=" + stack.Count);
                for (int i = 0; i < stack.Count; ++i)
                {
                    var overlay = stack[i];
                    Write($"STACK_ENTRY {i} path={(overlay == null ? "null" : Hierarchy(overlay.transform))} enabled={overlay != null && overlay.enabled}");
                }
            }
        }
        catch (Exception e) { Write("CAMERA_FAILED " + e); }
    }

    private static string Hierarchy(Transform current)
    {
        var names = new List<string>();
        for (int i = 0; current != null && i < 16; ++i, current = current.parent) names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private void DumpSurfaces()
    {
        // One scene inventory, never a per-frame hierarchy scan.
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            Write($"CANVAS path={Hierarchy(canvas.transform)} mode={canvas.renderMode} camera={(canvas.worldCamera == null ? "null" : canvas.worldCamera.name)} distance={canvas.planeDistance:R} sorting={canvas.sortingOrder}");
        foreach (var document in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
            Write($"UI_DOCUMENT path={Hierarchy(document.transform)} panel={(document.panelSettings == null ? "null" : document.panelSettings.name)} mode={(document.panelSettings == null ? "null" : document.panelSettings.renderMode.ToString())} worldSpace={document.isWorldSpace} target={(document.panelSettings == null || document.panelSettings.targetTexture == null ? "null" : document.panelSettings.targetTexture.name)}");
    }

    private void DumpInventory(InventoryManager manager, string phase)
    {
        if (phase == "delayed" && Environment.GetEnvironmentVariable("IKAG_XR_MOTION_AUDIT") == "1") MotionRigAudit.Run(manager);
        try
        {
            int count = manager.GetTotalSlots();
            Write($"INVENTORY phase={phase} count={count} selected={manager.GetCurrentSelectedSlot()}");
            for (int i = 0; i <= Math.Min(count, 16); ++i)
            {
                bool empty = manager.IsSlotEmpty(i);
                var item = empty ? null : manager.GetItemInSlot(i);
                Write($"INVENTORY_SLOT phase={phase} index={i} empty={empty} item={(item == null ? "null" : item.name)}");
            }
        }
        catch (Exception e) { Write("INVENTORY_READ_FAILED " + e); }
    }

    private void Write(string text) => File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + text + "\n");
    private void OnDestroy()
    {
        if (callback != null) RenderPipelineManager.remove_beginCameraRendering(callback);
    }
}
