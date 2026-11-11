using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using InputManager=Core.Input.InputManager;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class MotionInput
{
    private struct HeldUseScope { internal PizzaBox PackedBox;internal bool Left; }
#if IKAG_DIAGNOSTICS
    internal static readonly bool BaseEnabled=Probe.AutomaticGameplay?Environment.GetEnvironmentVariable("IKAG_XR_MOTION_CONTROLS")!="0":Environment.GetEnvironmentVariable("IKAG_XR_MOTION_CONTROLS")=="1";
#else
    internal static readonly bool BaseEnabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_CONTROLS")!="0";
#endif
    internal static bool Enabled=>BaseEnabled&&!(VrSettings.Gamepad?.Value??false);
    internal static void ModeChanged(){var input=InputManager.Instance;if(input!=null)Release(input);active=armed=false;lastClick=-100;VrLocomotion.Reset();HandPhysics.Stop();MotionPointer.Suspend();}
    private static ControllerTransport.Hand previousLeft,previousRight;
    private static bool active,primaryOwned,secondaryOwned,yLong,bLong;
    private static bool interactEdge;
    private static bool interactOwned,yOwned,bOwned;
    private static int heldUseFrame=-1;
    private static GameObject triggerItem;
    private static bool armed,sprintOwned;
    private static bool buildArmed;
    private static bool hotbarArmed;
    private static BuildingManager building;
    private static UI.MainMenu.MainMenuManager menu;
    private static bool previousMenuB,previousMenuClick,menuArmed;
    private static float yAt,bAt,lastClick=-100;
    private static int routedFrame=-1;
    private static InteractionManager interaction;
    private static GameObject item;
    private static PhotoCamera photo;
    private static CargoBox cargo;
    private static bool yCancelledPlacement;
    private static EmoteManager emotes;
    private static System.IntPtr emoteOwner;
    internal static bool WheelOpen { get; private set; }
    internal static bool GameplayReady=>active&&armed;
    internal static bool ControlsArmed=>armed;
    internal static bool PrimaryHeld=>active&&primaryOwned&&VrControls.MainInput.Trigger;
    internal static bool PhotoMode=>photo!=null&&photo.isInPhotoMode;
    internal static bool PlacementPreviewActive=>(building!=null&&building.HasActivePreview)||(interaction!=null&&(interaction.ingredientPlacementArmed||(interaction.currentPreviewObject!=null&&interaction.currentPreviewObject.activeInHierarchy)));
    internal static bool ConstructionPlacementActive=>(building!=null&&building.HasActivePreview)||(cargo!=null&&cargo.isInPlacementMode)||(interaction!=null&&interaction.ingredientPlacementArmed);
    internal static bool InteractHeld=>active&&interactOwned&&VrControls.MainInput.Trigger;
    internal static bool SellHeld=>active&&bOwned&&bLong&&VrControls.MainInput.Secondary;
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.motion-input");
        h.Patch(typeof(InputManager).GetMethod("Update"),postfix:Hook(nameof(Route)));
        var heldUse=Hook(nameof(HeldUse));heldUse.priority=Priority.First;
        h.Patch(typeof(InteractionManager).GetMethod("HandleInteraction"),prefix:heldUse,postfix:Hook(nameof(HeldUsed)));
        h.Patch(typeof(PlayerMovementController).GetMethod("CacheInput"),postfix:Hook(nameof(Movement)));
        foreach(string name in new[]{"IsInteractHeld","IsSkipHeld","IsPrimaryHeld","IsSecondaryHeld","IsSprintHeld","IsSellHeld","IsBuildModeHeld"})
            h.Patch(typeof(InputManager).GetProperty(name).GetMethod,postfix:Hook(nameof(Held)));
        h.Patch(typeof(InputManager).GetMethod("WasInteractPressedRaw"),postfix:Hook(nameof(RawInteract)));
        h.Patch(typeof(PhotoCamera).GetMethod("HandleZoomInput"),postfix:Hook(nameof(Zoom)));
        h.Patch(typeof(EmoteManager).GetMethod("UpdateWheelSelection"),prefix:Hook(nameof(WheelSelection)),finalizer:Hook(nameof(RestoreWheelPointer)));
        h.Patch(typeof(UI.MainMenu.MainMenuManager).GetMethod("OnEnable"),postfix:Hook(nameof(MenuEnabled)));
    }
    private static HarmonyMethod Hook(string name)=>new(typeof(MotionInput).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
    internal static void BindMenu(UI.MainMenu.MainMenuManager instance)
    {
        menu=instance;menuArmed=false;
        if(instance!=null)
        {
            // The outgoing InputManager may stop updating before scene teardown.
            // Menu input must not inherit its wheel or held-action latches.
            WheelOpen=false;emotes=null;emoteOwner=IntPtr.Zero;active=armed=false;lastClick=-100;
            ObjectGrabs.Reset();MotionHeldItems.Reset();
            primaryOwned=secondaryOwned=interactOwned=interactEdge=yOwned=bOwned=sprintOwned=false;
            buildArmed=yLong=bLong=hotbarArmed=false;previousLeft=previousRight=default;
        }
        MotionPointer.BindMenu(instance==null?null:instance.uiDocument);
        ComputerKeyboard.BindMenu(instance==null?null:instance.uiDocument);
    }
    private static void MenuEnabled(UI.MainMenu.MainMenuManager __instance)
    {if(__instance.gameObject.scene.name=="MainMenu")BindMenu(__instance);}
    internal static void MenuTick()
    {
        var r=VrControls.MainInput;
        if(!StereoTrial.MenuActive||!Probe.Focused||!r.Tracked||menu==null){menuArmed=false;previousMenuB=r.Secondary;previousMenuClick=r.Click;return;}
        if(!menuArmed){menuArmed=!(r.Secondary||r.Click);previousMenuB=r.Secondary;previousMenuClick=r.Click;return;}
        if(r.Secondary&&!previousMenuB)menu.HandleBackNavigation();
        if(r.Click&&!previousMenuClick)MotionPointer.ToggleCursor();
        previousMenuB=r.Secondary;previousMenuClick=r.Click;
    }
    private static void Route(InputManager __instance)
    {
        if(routedFrame==Time.frameCount) return;routedFrame=Time.frameCount;
        ControllerTransport.Sample();var l=VrControls.OffInput;var r=VrControls.MainInput;
        VrGamepadInput.Tick();
        DistanceGrips.Input();
        MotionShovel.Input();
        PhysicalHeldItems.Input();
        WorldPress.Tick();
        bool live=Enabled&&Probe.Focused&&l.Tracked&&r.Tracked;
        if(!live) {armed=false;lastClick=-100;}
        else if(!armed)
        {
            Release(__instance);active=false;previousLeft=l;previousRight=r;
            armed=!(l.Primary||l.Secondary||l.Trigger||l.Grip||l.Click||r.Primary||r.Secondary||r.Trigger||r.Grip||r.Click);
            return;
        }
        bool dialog=ConversationActive;
        bool gripPressed=(l.Grip&&!previousLeft.Grip)||(r.Grip&&!previousRight.Grip);
        if(live&&dialog&&gripPressed)
        {var ui=UIManager.LocalInstance;if(ui.isWaiting)ui.skipWaitingRequested=true;else __instance.OnSkipPressed?.Invoke();}
        if(live&&dialog&&r.Primary&&!previousRight.Primary)
        {
            // Native UIManager.Update sets this same flag on Space while waiting.
            var ui=UIManager.LocalInstance;if(ui.isWaiting)ui.skipWaitingRequested=true;
        }
        KitchenHands.Tick();
        // Observe native/manual wheel access; palm orientation never opens it.
        WheelOpen=emotes!=null&&emotes.isWheelOpen;
        bool machine=MachineInteractionBase.IsLocalPlayerInMachineMode;
        bool gameplay=live&&!ImmersiveHud.InputOwned&&StereoTrial.GameplayActive&&!machine&&!ArcadeMotion.Active&&!SpyMotion.Active&&(UnityEngine.Cursor.lockState==CursorLockMode.Locked||WheelOpen);
        if(live&&r.Click&&!previousRight.Click)MotionPointer.ToggleCursor();
#if IKAG_DIAGNOSTICS
        PizzaBoxAudit.RecipeInput(l.Primary,previousLeft.Primary,live,gameplay);
#endif
        if(!gameplay)
        {
            // Opening recipes releases the cursor and enters this modal path.
            // X must still reach the existing native toggle while it is open.
            if(live&&l.Primary&&!previousLeft.Primary)
            {var hud=HUDManager.LocalInstance;if(hud!=null&&hud.IsRecipeBookOpen)__instance.OnRecipeBookPressed?.Invoke();}
            ObjectGrabs.Suspend();
            Release(__instance);active=false;yLong=bLong=false;yAt=bAt=Time.unscaledTime;
            if(WheelOpen&&emotes!=null) {emotes.CloseWheel();WheelOpen=false;}
            if(live&&!StereoTrial.MenuActive&&r.Secondary&&!previousRight.Secondary)
            {
                var hud=HUDManager.LocalInstance;
                var currentMachine=machine?MachineInteractionBase.CurrentActiveMachine:null;
                // Machine.Update polls the physical Escape key, not OnCancelPressed.
                // Reuse the same native exit method, including camera/RPC cleanup.
                if(ImmersiveHud.Back()){}
                else if(SpyMotion.Active&&(hud==null||!hud.IsEscapeMenuOpen))SpyMiniGameManager.LocalInstance.AbortActiveSession();
                else if(ArcadeMotion.Active&&(hud==null||!hud.IsEscapeMenuOpen))ArcadeMiniGameManager.LocalInstance.AbortActiveGame();
                else if(currentMachine!=null&&!MachineInteractionBase.isLocalMachineTransitionInProgress&&(hud==null||!hud.IsEscapeMenuOpen))currentMachine.TriggerExit();
                else __instance.OnCancelPressed?.Invoke();
            }
            // Pause must remain reachable while its menu owns gameplay focus.
            Pause(__instance,l.Click&&!previousLeft.Click);
            previousLeft=l;previousRight=r;return;
        }
        active=true;
        interactEdge=false;
        if(!r.Trigger)interactOwned=false;
        if(interaction==null) {interaction=InteractionManager.LocalInstance;emoteOwner=IntPtr.Zero;item=null;photo=null;cargo=null;emotes=null;building=null;}
        if(interaction!=null&&emoteOwner!=interaction.Pointer) {emoteOwner=interaction.Pointer;emotes=interaction.GetComponent<EmoteManager>();}
        if(building==null)building=BuildingManager.Instance;
        bool placementAtStart=building!=null&&building.HasActivePreview;
        if(building!=null&&building.isReplacingMode)buildArmed=false;
        if(WheelOpen)
        { ObjectGrabs.Suspend();Release(__instance);Pause(__instance,l.Click&&!previousLeft.Click);previousLeft=l;previousRight=r;return; }
        var top=interaction==null?null:interaction.cachedTopItem;
        if(top!=item) { item=top;photo=item==null?null:item.GetComponent<PhotoCamera>();cargo=item==null?null:item.GetComponent<CargoBox>(); }
        ObjectGrabs.Tick(interaction,!PhotoMode&&!dialog,placementAtStart,__instance);
        if(l.Primary&&!previousLeft.Primary)
        {
            __instance.OnRecipeBookPressed?.Invoke();
            // The native toggle can release the cursor immediately. Do not
            // dispatch another gameplay action on the opening frame.
            var hud=HUDManager.LocalInstance;
            if(hud!=null&&hud.IsRecipeBookOpen)
            {ObjectGrabs.Suspend();Release(__instance);active=false;previousLeft=l;previousRight=r;return;}
        }
        if(!dialog&&r.Primary&&!previousRight.Primary) __instance.OnJumpPressed?.Invoke();
        if(l.Click&&!previousLeft.Click)sprintOwned=true;
        if(!l.Click)sprintOwned=false;
        if(l.Secondary&&!previousLeft.Secondary) { yAt=Time.unscaledTime;yLong=false;yOwned=true;yCancelledPlacement=CancelPlacement(); }
        if(l.Secondary&&yOwned&&!yCancelledPlacement&&!yLong&&Time.unscaledTime-yAt>=.55f) { yLong=true;__instance.OnDropPressed?.Invoke(); }
        if(!l.Secondary&&previousLeft.Secondary&&yOwned) {if(!yCancelledPlacement&&!yLong) DropItem();yOwned=false;}
        if(r.Secondary&&!previousRight.Secondary) { bAt=Time.unscaledTime;bLong=false;bOwned=true; }
        if(r.Secondary&&bOwned&&!bLong&&Time.unscaledTime-bAt>=.55f) {bLong=true;buildArmed=false;}
        if(!r.Secondary&&previousRight.Secondary&&bOwned)
        {
            if(!bLong)
            {
                if(ConfirmPlacement(__instance))buildArmed=false;else buildArmed=!buildArmed;
            }
            bOwned=false;
        }
        bool placementNow=building!=null&&building.HasActivePreview;
        if(placementAtStart||placementNow)
        {
            interactOwned=false;heldUseFrame=-1;triggerItem=null;
            if(primaryOwned){primaryOwned=false;__instance.OnPrimaryReleased?.Invoke();}
            if(secondaryOwned){secondaryOwned=false;__instance.OnSecondaryReleased?.Invoke();}
            float turn=(r.Trigger?1:0)-(l.Trigger?1:0);
            if(placementNow&&turn!=0)building.RotatePreview(turn*90*Time.deltaTime);
        }
        else
        {
            if(l.Trigger&&!previousLeft.Trigger)
            {
                if(photo!=null) photo.TogglePhotoMode();
                else {secondaryOwned=true;__instance.OnSecondaryPressed?.Invoke();}
            }
            if(!l.Trigger&&secondaryOwned) {secondaryOwned=false;__instance.OnSecondaryReleased?.Invoke();}
            if(r.Trigger&&!previousRight.Trigger)
            {
                if(photo!=null) { if(photo.isInPhotoMode) photo.TakePhoto(); }
                else if(interaction!=null&&interaction.cachedSkateboard!=null) __instance.OnRidePressed?.Invoke();
                else if(interaction!=null&&interaction.IsHoldingItem())
                {
                    // Reuse X's native interaction edge/hold/raw-input funnel.
                    // Choose the primary-use fallback after this frame's query.
                    interactEdge=interactOwned=true;triggerItem=interaction.cachedTopItem;heldUseFrame=Time.frameCount;
                }
            }
            if(!r.Trigger&&primaryOwned) {primaryOwned=false;__instance.OnPrimaryReleased?.Invoke();}
        }
        var inventory=InventoryManager.LocalInstance;
        if(inventory==null||!inventory.inventoryEnabled||PhotoMode||placementAtStart||placementNow)hotbarArmed=false;
        else
        {
            float y=r.Stick.y;
            if(Math.Abs(y)<=.25f)hotbarArmed=true;
            else if(hotbarArmed&&Math.Abs(y)>=.65f&&Math.Abs(y)>Math.Abs(r.Stick.x))
            {
                hotbarArmed=false;int delta=y>0?1:-1;
                inventory.SetSelectedSlot((inventory.currentSelectedSlot+delta+inventory.totalSlots)%inventory.totalSlots);
            }
        }
        if(l.Click!=previousLeft.Click) __instance.OnSprintChanged?.Invoke(l.Click);
        MotionPunch.Tick(interaction,placementAtStart||placementNow);
        Pause(__instance,l.Click&&!previousLeft.Click);
        previousLeft=l;previousRight=r;
    }
    private static void HeldUse(InteractionManager __instance,out HeldUseScope __state)
    {
        __state=default;
        if(heldUseFrame!=Time.frameCount||interaction==null||__instance.Pointer!=interaction.Pointer)return;
        heldUseFrame=-1;
        if(!GameplayReady||!__instance._IsOwner_k__BackingField||!__instance.IsInteractionEnabled||
            __instance.skatingSuppressed||InteractionManager.IsHeldItemUseBlocked||!__instance.IsHoldingItem()||
            __instance.cachedTopItem!=triggerItem)return;
        // Native Update has completed raycast/sphere/target selection here.
        // Never issue both a receiving interaction and primary use for one press.
        bool receiving=!__instance.ingredientPlacementArmed&&
            ((__instance.currentInteractable!=null&&__instance.currentInteractable.CanInteract())||
             (__instance.currentHoldable!=null&&__instance.currentHoldable.CanPickup()));
        if(receiving)
        {
            __state.PackedBox=__instance.currentInteractable?.TryCast<PizzaBox>();__state.Left=ObjectGrabs.LeftHand;
            InputManager.Instance?.OnInteractPressed?.Invoke();return;
        }
        interactOwned=interactEdge=false;
        primaryOwned=true;InputManager.Instance?.OnPrimaryPressed?.Invoke();
    }
    private static void HeldUsed(InteractionManager __instance,HeldUseScope __state)
    {
        var packed=__state.PackedBox;
        if(packed!=null&&packed.HasWholePizza()&&__instance.heldItems.Contains(packed.gameObject))
            ObjectGrabs.AdoptPacked(__instance,packed.gameObject,__state.Left);
    }
    private static void Pause(InputManager input,bool pressed)
    {
        if(!pressed||!Probe.Focused) return;
        float now=Time.unscaledTime;
        if(now-lastClick<=.32f)
        {
            lastClick=-100;
            var hud=HUDManager.LocalInstance;
            if(hud!=null&&!hud.disableEscapeMenu)
            {if(hud.IsEscapeMenuOpen)hud.HideEscapeMenu();else hud.ShowEscapeMenu();}
        }
        else lastClick=now;
    }
    private static bool CancelPlacement()
    {
        if(building!=null&&building.isReplacingMode){building.CancelReplacement();return true;}
        if(cargo!=null&&cargo.isInPlacementMode){cargo.ExitPlacementMode();return true;}
        return false;
    }
    private static bool ConfirmPlacement(InputManager input)
    {
        if(building!=null&&building.isReplacingMode){building.PlaceReplacedBuildable();return true;}
        if(cargo!=null&&(cargo.IsMachineBox()||cargo.IsDecorationBox()))
        {
            if(cargo.isInPlacementMode)cargo.TryPlaceMachine();else cargo.EnterPlacementMode();
            return true;
        }
        if(interaction==null||!interaction.IsHoldingItem())return false;
        if(interaction.ingredientPlacementArmed){input.OnPrimaryPressed?.Invoke();return true;}
        if(interaction.currentPreviewObject!=null&&interaction.lastPlacementPointForPreview!=null){input.OnInteractPressed?.Invoke();return true;}
        return false;
    }
    internal static bool ReleasePreview(InputManager input)
    {
        // The native preview owns validation. Even an invalid construction
        // preview consumes a release, keeping its item instead of dropping it.
        if(building!=null&&building.HasActivePreview)
        {
            if(building.isValidPlacement)
            {
                if(building.isReplacingMode)building.PlaceReplacedBuildable();
                else if(cargo!=null&&cargo.isInPlacementMode)cargo.TryPlaceMachine();
            }
            return true;
        }
        if(interaction==null||!interaction.IsHoldingItem())return false;
        if(interaction.ingredientPlacementArmed){input.OnPrimaryPressed?.Invoke();return true;}
        // Shelf and other generic previews record their native interactable,
        // without filling the dedicated pizza placement-point field.
        var previewTarget=interaction.lastInteractableForPreview;
        if(previewTarget?.TryCast<PizzaBox>()!=null)return false; // Opposite grip confirms packing.
        bool nativeTarget=previewTarget!=null&&interaction.currentInteractable!=null&&previewTarget.Pointer==interaction.currentInteractable.Pointer&&previewTarget.CanInteract();
        if(interaction.currentPreviewObject!=null&&interaction.currentPreviewObject.activeInHierarchy&&(interaction.lastPlacementPointForPreview!=null||nativeTarget))
        {interactEdge=true;input.OnInteractPressed?.Invoke();return true;}
        return false;
    }
    private static void DropItem()
    {
        if(interaction==null||!interaction.IsHoldingItem())return;
        MotionHeldItems.SnapForRelease(interaction);int before=interaction.heldItems.Count;bool leftHand=ObjectGrabs.LeftHand;
        // Native DropLastItem preserves ownership, layers and inventory cleanup.
        // Its measured impulse is forward*dropForce + up*dropUpwardForce.
        float force=interaction.dropForce,up=interaction.dropUpwardForce;
        try {interaction.dropForce=0;interaction.dropUpwardForce=0;interaction.DropLastItem();}
        finally {interaction.dropForce=force;interaction.dropUpwardForce=up;}
        if(interaction.heldItems.Count<before)MotionHaptics.Pulse(leftHand,.16f,.025f);
    }
    private static void Release(InputManager input)
    {
        MotionPunch.Reset();
        if(primaryOwned) input.OnPrimaryReleased?.Invoke();
        if(secondaryOwned) input.OnSecondaryReleased?.Invoke();
        if(active&&previousLeft.Click) input.OnSprintChanged?.Invoke(false);
        primaryOwned=secondaryOwned=false;
        heldUseFrame=-1;triggerItem=null;
        yOwned=bOwned=interactOwned=interactEdge=false;
        sprintOwned=false;
        buildArmed=false;
        hotbarArmed=false;
    }
    private static void Held(MethodBase __originalMethod,ref bool __result)
    {
        if(!active) return;
        __result|=__originalMethod.Name switch {
            "get_IsInteractHeld"=>InteractHeld||DistanceGrips.InteractHeld,
            "get_IsPrimaryHeld"=>primaryOwned,"get_IsSecondaryHeld"=>secondaryOwned,
            "get_IsSprintHeld"=>sprintOwned&&VrControls.OffInput.Click,"get_IsSellHeld"=>SellHeld,
            "get_IsBuildModeHeld"=>buildArmed&&!VrControls.MainInput.Secondary,_=>false};
    }
    private static void RawInteract(ref bool __result) { if(active) __result|=interactEdge; }
    internal static bool ConversationActive { get { var ui=UIManager.LocalInstance;return ui!=null&&ui.isLocalPlayer&&ui.isDialogActive; } }
    private static void Movement(PlayerMovementController __instance)
    {
        if(!BaseEnabled||!Probe.Focused||!__instance._IsOwner_k__BackingField) return;
        if(VrSettings.Gamepad.Value){VrLocomotion.GamepadMovement(__instance);return;}
        ControllerTransport.Sample();var l=VrControls.OffInput;var r=VrControls.MainInput;
        if(!l.Tracked||!r.Tracked) return;
        bool boardMovement=armed&&StereoTrial.GameplayActive&&ImmersiveHud.BoardInputOwned&&!ContextHud.ModalActive&&
            !MachineInteractionBase.IsLocalPlayerInMachineMode&&!ArcadeMotion.Active&&!SpyMotion.Active&&Cursor.lockState==CursorLockMode.Locked;
        if((!active&&!boardMovement)||WheelOpen) {__instance.moveInput=Vector2.zero;VrLocomotion.Reset();return;}
        __instance.moveInput=Deadzone(l.Stick);
        __instance.jumpPressed|=r.PrimaryPressed&&!ConversationActive;
        // Native yaw consumes pointer-delta * mouseSensitivity without deltaTime.
        // Convert a 90 degree/second stick rate into that measured input funnel.
        VrLocomotion.Apply(__instance,l.Stick,r.Stick,false,!ImmersiveHud.BoardOwnsStick);
    }
    internal static Vector2 Deadzone(Vector2 v)
    { float m=v.magnitude;return m<=.2f?Vector2.zero:v/Math.Max(m,1e-6f)*Math.Min(1,(m-.2f)/.8f); }
    private static void Zoom(PhotoCamera __instance)
    {
        if(!active||!__instance.isInPhotoMode||photo==null||photo.Pointer!=__instance.Pointer||WheelOpen) return;
        float y=Deadzone(VrControls.MainInput.Stick).y;
        __instance.targetZoom=Mathf.Clamp(__instance.targetZoom+y*__instance.zoomStep*Time.unscaledDeltaTime*5,__instance.minZoom,__instance.maxZoom);
    }
    private static void WheelSelection(EmoteManager __instance,out Vector2 __state)
    {
        var input=InputManager.Instance;__state=input==null?default:input.mousePosition;
        if(!WheelOpen||input==null||emotes==null||emotes.Pointer!=__instance.Pointer)return;
        // Native selection reads InputManager.mousePosition against Screen center.
        var v=Deadzone(VrControls.MainInput.Stick);
        input.mousePosition=new Vector2(Screen.width*.5f+v.x*Screen.height*.3f,Screen.height*.5f+v.y*Screen.height*.3f);
    }
    private static void RestoreWheelPointer(Vector2 __state)
    {if(WheelOpen&&InputManager.Instance!=null) InputManager.Instance.mousePosition=__state;}
#if IKAG_DIAGNOSTICS
    internal static void Trace()
    {Plugin.Line($"GESTURE_CONTROL active={active} interact={InteractHeld} sell={SellHeld} primary={primaryOwned} secondary={secondaryOwned} wheel={WheelOpen} nativeWheel={(emotes!=null&&emotes.isWheelOpen)} hovered={(emotes==null?-100:emotes.hoveredSlot)} emotes={(emotes==null?"none":emotes.name)}");}
#endif
}
