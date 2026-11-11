using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem;
using UnityEngine;
using InputManager=Core.Input.InputManager;
namespace IKnowAGuyVR.SubsystemProbe;

// Add controller paths to the native actions. No synthetic keys or per-frame
// action searches; bindings are resolved only at initialization/mode changes.
internal static class VrGamepadInput
{
    private readonly struct Binding
    {
        internal readonly InputAction Action;internal readonly int Index;internal readonly string Path;
        internal Binding(InputAction action,int index,string path){Action=action;Index=index;Path=path;}
    }
    private static InputManager owner;private static InputAction previous,next,pause,cancel;private static readonly List<Binding> bindings=new();
    internal static void Install()=>new Harmony("iknowaguyvr.gamepad-bindings").Patch(typeof(InputManager).GetMethod("InitializeInputActions"),postfix:new HarmonyMethod(typeof(VrGamepadInput).GetMethod(nameof(Bind),BindingFlags.NonPublic|BindingFlags.Static)));
    private static void Add(InputAction action,string path)
    {if(action==null)return;int index=action.bindings.Count;action.AddBinding("",groups:"Keyboard&Mouse");bindings.Add(new Binding(action,index,"<Gamepad>/"+path));}
    private static void Bind(InputManager __instance)
    {
        if(owner!=null&&owner.Pointer==__instance.Pointer)return;
        owner=__instance;cancel=owner.CancelAction;bindings.Clear();previous=next=pause=null;var enabled=Suspend();
        Add(owner.MoveAction,"leftStick");Add(owner.JumpAction,"buttonSouth");Add(owner.SprintAction,"leftStickPress");Add(owner.CrouchAction,"rightStickPress");
        Add(owner.InteractAction,"buttonWest");Add(owner.DropAction,"buttonEast");Add(owner.PrimaryAction,"rightTrigger");Add(owner.SecondaryAction,"leftTrigger");
        Add(owner.RecipeBookAction,"buttonNorth");Add(owner.BuildModeAction,"select");pause=owner.uiMap.AddAction("VRPause",InputActionType.Button);Add(pause,"start");
        Add(owner.uiMap.FindAction("Navigate",false),"leftStick");Add(owner.uiMap.FindAction("Navigate",false),"dpad");
        Add(owner.RideAction,"rightTrigger");
        Add(owner.SubmitAction,"buttonSouth");Add(owner.CancelAction,"buttonEast");Add(owner.SkipAction,"buttonSouth");
        var slots=owner.SlotActions;var paths=new[]{"dpad/up","dpad/right","dpad/down","dpad/left"};if(slots!=null)for(int i=0;i<Math.Min(4,slots.Length);++i)Add(slots[i],paths[i]);
        previous=owner.playerMap.FindAction("Previous",false);next=owner.playerMap.FindAction("Next",false);
        Add(previous,"leftShoulder");Add(next,"rightShoulder");
        SetPaths();Resume(enabled);
    }
    private static List<InputAction> Suspend()
    {
        var enabled=new List<InputAction>();var maps=owner.inputActionsAsset.actionMaps;
        for(int i=0;i<maps.Count;++i){var actions=maps[i].actions;for(int j=0;j<actions.Count;++j)if(actions[j].enabled&&actions[j].Pointer!=previous?.Pointer&&actions[j].Pointer!=next?.Pointer&&actions[j].Pointer!=pause?.Pointer)enabled.Add(actions[j]);maps[i].Disable();}
        return enabled;
    }
    private static void Resume(List<InputAction> enabled)
    {var maps=owner.inputActionsAsset.actionMaps;for(int i=0;i<maps.Count;++i)maps[i].ResolveBindingsIfNecessary();foreach(var action in enabled)action.Enable();if(VrSettings.Gamepad.Value){previous?.Enable();next?.Enable();pause?.Enable();}}
    internal static void Tick()
    {if(!VrSettings.Gamepad.Value||!Probe.Focused)return;if(previous!=null&&previous.WasPressedThisFrame())Cycle(-1);if(next!=null&&next.WasPressedThisFrame())Cycle(1);
        if(cancel!=null&&cancel.WasPressedThisFrame())ExitMachine();
        if(pause!=null&&pause.WasPressedThisFrame()&&StereoTrial.GameplayActive&&!ExitMachine()){var hud=HUDManager.LocalInstance;if(hud!=null&&!hud.disableEscapeMenu){if(hud.IsEscapeMenuOpen)hud.HideEscapeMenu();else hud.ShowEscapeMenu();}}}
    private static bool ExitMachine()
    {
        var hud=HUDManager.LocalInstance;if(!StereoTrial.GameplayActive||(hud!=null&&hud.IsEscapeMenuOpen))return false;
        if(SpyMotion.Active){SpyMiniGameManager.LocalInstance.AbortActiveSession();return true;}
        if(ArcadeMotion.Active){ArcadeMiniGameManager.LocalInstance.AbortActiveGame();return true;}
        var machine=MachineInteractionBase.CurrentActiveMachine;
        if(machine==null||!MachineInteractionBase.IsLocalPlayerInMachineMode||MachineInteractionBase.isLocalMachineTransitionInProgress)return false;
        // Native machine Update polls physical Escape. Reuse the native exit
        // funnel already used by the motion-controller back button.
        machine.TriggerExit();return true;
    }
    private static void Cycle(int direction)
    {
#if IKAG_DIAGNOSTICS
        Plugin.Line($"VR_GAMEPAD_CYCLE direction={direction} focus={Probe.Focused} cursor={Cursor.lockState} preview={MotionInput.PlacementPreviewActive} inventory={InventoryManager.LocalInstance?.inventoryEnabled} selected={InventoryManager.LocalInstance?.currentSelectedSlot}");
#endif
        if(!VrSettings.Gamepad.Value||!Probe.Focused||!StereoTrial.GameplayActive||Cursor.lockState!=CursorLockMode.Locked||MachineInteractionBase.IsLocalPlayerInMachineMode||ArcadeMotion.Active||SpyMotion.Active||MotionInput.PlacementPreviewActive)return;
        var inventory=InventoryManager.LocalInstance;var hud=HUDManager.LocalInstance;
        if(inventory==null||!inventory.inventoryEnabled||inventory.totalSlots<1||(hud!=null&&hud.IsEscapeMenuOpen))return;
        inventory.SetSelectedSlot((inventory.currentSelectedSlot+direction+inventory.totalSlots)%inventory.totalSlots);
    }
    private static void SetPaths(){foreach(var b in bindings)b.Action.ChangeBinding(b.Index).WithPath(VrSettings.Gamepad.Value?b.Path:"");}
    internal static void ModeChanged()
    {
        if(owner==null)return;var enabled=Suspend();SetPaths();Resume(enabled);
    }
}
