using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using InputManager=Core.Input.InputManager;
namespace IKnowAGuyVR.SubsystemProbe;

// Adapt display text only. Native action bindings and localized action/body
// text remain authoritative; stable actions and static HUD legends are cached.
internal static class VrPrompts
{
    private sealed class Key
    {
        internal readonly string Raw,Display;internal readonly bool Interact;
        internal Key(string raw,bool interact=false){Raw=raw;Display="["+raw+"]";Interact=interact;}
    }
    private sealed class Legend
    {
        internal Label Label;internal string Original,Motion;
        internal VisualElement HiddenRow;internal StyleEnum<DisplayStyle> OriginalDisplay;
    }
    private static readonly Dictionary<IntPtr,Key> keys=new();
    private static readonly List<Legend> legends=new(20);
    private static InputManager input;private static HUDManager hud;private static bool lastMode,lastLeft,lastSwap;
    private static Label machineExit,machineHandle,forgeExit,recipeKey;
    private static string holdBuild,tapBuild,holdSell,vrHoldSell,lastPrompt,adaptedPrompt;
    private static IntPtr heldOwner;private static bool held;
    internal static bool Active=>MotionInput.Enabled;
    private static HarmonyMethod Hook(string name)=>new(typeof(VrPrompts).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.vr-prompts");WristSections.Install(h);
        h.Patch(typeof(InputManager).GetMethod("InitializeInputActions"),postfix:Hook(nameof(Actions)));
        foreach(var name in new[]{"GetKeyDisplay","GetKeyDisplayShort"})h.Patch(typeof(InputManager).GetMethod(name),prefix:Hook(nameof(Display)));
        foreach(var name in new[]{"GetKeyRaw","GetKeyRawShort"})h.Patch(typeof(InputManager).GetMethod(name),prefix:Hook(nameof(Raw)));
        h.Patch(typeof(HUDManager).GetMethod("RefreshKeyBindings"),postfix:Hook(nameof(Refreshed)));
        h.Patch(typeof(HUDManager).GetMethod("ShowMachineControls"),postfix:Hook(nameof(Machine)));
        h.Patch(typeof(HUDManager).GetMethod("ShowSkateBoostHint"),postfix:Hook(nameof(Skate)));
        foreach(var name in new[]{"ShowForgeryStepGuide","SetForgeryStep"})h.Patch(typeof(HUDManager).GetMethod(name),postfix:Hook(nameof(Forge)));
        h.Patch(typeof(HUDManager).GetMethod("RefreshRecipeBookHint"),postfix:Hook(nameof(Recipe)));
        h.Patch(typeof(HUDManager).GetMethod("ShowItemActionHint"),prefix:Hook(nameof(ItemHint)));
        h.Patch(typeof(HUDManager).GetMethod("SetPoiOfferText"),prefix:Hook(nameof(MapNudge)));
        h.Patch(typeof(InteractionPromptUITK).GetMethod("ShowPrompt"),prefix:Hook(nameof(ObjectPrompt)));
        h.Patch(typeof(HUDManager).GetMethod("OnLocaleChanged"),postfix:Hook(nameof(LanguageChanged)));
        h.Patch(typeof(InteractionManager).GetMethod("RefreshHeldItemCache"),postfix:Hook(nameof(HeldChanged)));
        lastMode=Active;lastLeft=VrControls.LeftHanded;lastSwap=VrControls.SwapSticks;HeldLabels();VrSettings.Changed+=ModeChanged;
    }
    private static void Add(InputAction action,string raw,bool interact=false){if(action!=null)keys[action.Pointer]=new Key(raw,interact);}
    private static void Actions(InputManager __instance)
    {
        input=__instance;keys.Clear();
        Add(input.MoveAction,VrControls.MoveStick);Add(input.LookAction,"Head / "+VrControls.TurnStick);
        Add(input.JumpAction,VrControls.MainPrimary);Add(input.SprintAction,VrControls.MoveStick+" click");
        Add(input.InteractAction,"Grip",true);Add(input.DropAction,VrControls.OffSecondary);
        Add(input.PrimaryAction,VrControls.MainTrigger);Add(input.SecondaryAction,VrControls.OffTrigger);
        Add(input.BuildModeAction,VrControls.MainSecondary);Add(input.SellAction,VrControls.MainSecondary);
        Add(input.PhotoModeAction,VrControls.OffTrigger);Add(input.RecipeBookAction,VrControls.OffPrimary);
        Add(input.RideAction,VrControls.MainTrigger);Add(input.CancelAction,VrControls.MainSecondary);
        Add(input.SubmitAction,"Trigger / grip");Add(input.SkipAction,VrControls.MainPrimary+" / grip");
        Add(input.ClickAction,"Trigger / grip");Add(input.QuickPauseAction,"Double "+VrControls.MoveStick+" click");
        Add(input.ScrollAction,VrControls.TurnStick);Add(input.PunchAction,"Swing fist");
        for(int i=0;i<5;i++)Add(input.GetHudDrawerAction(i),"Click");
    }
    private static bool Resolve(InputManager owner,InputAction action,bool display,out string result)
    {
        result=null;if(!Active||action==null)return false;
        if(input==null||input.Pointer!=owner.Pointer)Actions(owner);
        if(!keys.TryGetValue(action.Pointer,out var key))return false;
        if(key.Interact&&InteractionManager.LocalInstance?.cachedTopItem!=null)result=display?heldDisplay:heldRaw;
        else result=display?key.Display:key.Raw;
        return true;
    }
    private static bool Display(InputManager __instance,InputAction action,ref string __result){if(!Resolve(__instance,action,true,out var text))return true;__result=text;return false;}
    private static bool Raw(InputManager __instance,InputAction action,ref string __result){if(!Resolve(__instance,action,false,out var text))return true;__result=text;return false;}
    internal static void Bind(HUDManager current)
    {
        if(current==null||current.root==null||hud!=null&&hud.Pointer==current.Pointer)return;
        hud=current;legends.Clear();machineExit=machineHandle=forgeExit=recipeKey=null;
        // Native static legend tree is read once per HUD, never in Update/render.
        Capture(hud.root,null);Apply();WristSections.Bind(hud);PromptTokens();
        hud.RefreshKeyBindings();hud.RefreshHudDrawerKeys();hud.RefreshRecipeBookHint();hud.RefreshTownJobsHint();
    }
    private static void Capture(VisualElement element,string context)
    {
        string name=element.name;
        if(name=="building-controls-panel"||name=="throwing-controls-panel"||name=="machine-controls-panel"||name=="cash-register-credit-controls"||name=="cash-register-cash-controls"||name=="emote-hint-panel"||name=="forgery-step-exit"||name=="skate-boost-hint"||name=="item-action-hint")context=name;
        var label=element.TryCast<Label>();
        if(label!=null&&context!=null)
        {
            string text=label.text,motion=null;bool found=false,hide=false;
            if(context=="machine-controls-panel")
            {
                if(name=="key-machine-exit")machineExit=label;
                else if(text=="[LMB]"){machineHandle=label;motion="[Grip]";found=true;}
            }
            else if(context=="building-controls-panel")
            {
                if(text=="[LMB]"){motion="[R B]";found=true;}
                else if(text=="[RMB]"){motion="[L secondary]";found=true;}
                else if(text=="[Scroll]"){motion="[L / R trigger]";found=true;}
                else if(text=="[Shift]"||text=="[Alt]"){found=hide=true;}
            }
            else if(context=="cash-register-credit-controls"||context=="cash-register-cash-controls")
            {if(text=="[LMB]"){motion="[R trigger]";found=true;}else if(text=="[RMB]"){motion="[L trigger]";found=true;}}
            else if(context=="emote-hint-panel"&&text=="[TAB]"){motion="[Wrist Map]";found=true;}
            else if(context=="emote-hint-panel"&&name=="key-recipe-book"){recipeKey=label;motion="[L primary]";found=true;}
            else if(context=="forgery-step-exit"&&text=="[E]"){forgeExit=label;motion="[R B]";found=true;}
            else if(context=="skate-boost-hint"&&name=="skate-boost-key"){motion="[L stick click]";found=true;}
            else if(context=="item-action-hint"&&name=="item-action-key"){motion="[R trigger]";found=true;}
            else if(context=="throwing-controls-panel"&&(text=="[LMB]"||text=="[Scroll]")){found=hide=true;}
            if(found)legends.Add(new Legend{Label=label,Original=text,Motion=motion,HiddenRow=hide?label.parent:null,OriginalDisplay=label.parent.style.display});
        }
        for(int i=0;i<element.childCount;i++)Capture(element.ElementAt(i),context);
    }
    private static void Apply()
    {
        foreach(var legend in legends)
        {
            if(legend.HiddenRow!=null)legend.HiddenRow.style.display=Active?new StyleEnum<DisplayStyle>(DisplayStyle.None):legend.OriginalDisplay;
            else Set(legend.Label,Active?MotionLabel(legend.Motion):legend.Original);
        }
        if(Active){Set(machineExit,VrControls.MainSecondaryTag);Set(machineHandle,"[Grip]");}
    }
    private static string heldRaw,heldDisplay;
    private static void HeldLabels(){heldRaw="Grip / "+VrControls.MainTrigger;heldDisplay="["+heldRaw+"]";}
    private static string MotionLabel(string original)=>original switch
    {
        "[R B]"=>VrControls.MainSecondaryTag,
        "[L secondary]"=>VrControls.LeftHanded?"[R B]":"[L secondary]",
        "[R trigger]"=>VrControls.MainTriggerTag,
        "[L trigger]"=>VrControls.LeftHanded?"[R trigger]":"[L trigger]",
        "[L primary]"=>VrControls.OffPrimaryTag,
        "[L stick click]"=>VrControls.MoveClickTag,
        _=>original
    };
    private static void Set(Label label,string text){if(label!=null&&label.text!=text)label.text=text;}
    private static void Refreshed(HUDManager __instance){if(hud!=null&&hud.Pointer==__instance.Pointer)Apply();}
    private static void Machine(HUDManager __instance){if(Active&&hud!=null&&hud.Pointer==__instance.Pointer){Set(machineExit,VrControls.MainSecondaryTag);Set(machineHandle,"[Grip]");}}
    private static void Skate(HUDManager __instance){if(Active)Set(__instance.cachedSkateBoostKeyLabel,VrControls.MoveClickTag);}
    private static void Forge(HUDManager __instance){if(Active&&hud!=null&&hud.Pointer==__instance.Pointer)Set(forgeExit,VrControls.MainSecondaryTag);}
    private static void Recipe(HUDManager __instance){if(Active&&hud!=null&&hud.Pointer==__instance.Pointer)Set(recipeKey,VrControls.OffPrimaryTag);}
    private static void ItemHint(ref string keyLabel){if(Active&&(string.IsNullOrEmpty(keyLabel)||keyLabel=="[LMB]"))keyLabel=VrControls.MainTriggerTag;}
    private static void MapNudge(ref string nudge){if(Active&&nudge!=null&&nudge.Contains("[TAB]",StringComparison.Ordinal))nudge=nudge.Replace("[TAB]","[Wrist Map]",StringComparison.Ordinal);}
    private static void PromptTokens()
    {
        // Native relocation uses the cached build action, but its hold wrapper
        // and literal X sell key bypass InputManager's display lookup.
        string format=LocalizationHelper.GetLocalized("L_prompt_hold_key");
        if(string.IsNullOrEmpty(format)||!format.Contains("{0}",StringComparison.Ordinal))format="[Hold {0}]";
        string relocate=LocalizationHelper.GetLocalized("L_hold_b_relocate");
        holdBuild=string.Format(format,VrControls.MainSecondary)+" "+relocate;tapBuild=VrControls.MainSecondaryTag+" "+relocate;
        holdSell=string.Format(format,"X");vrHoldSell=string.Format(format,VrControls.MainSecondary);
        lastPrompt=adaptedPrompt=null;
    }
    private static void LanguageChanged(HUDManager __instance){if(hud!=null&&hud.Pointer==__instance.Pointer){PromptTokens();Apply();hud.RefreshHudDrawerKeys();}}
    private static void HeldChanged(InteractionManager __instance)
    {
        if(!Active||!__instance._IsOwner_k__BackingField)return;
        bool next=__instance.cachedTopItem!=null;
        if(heldOwner==__instance.Pointer&&held==next)return;
        heldOwner=__instance.Pointer;held=next;
        // Native object prompts cache their interact label independently of
        // InputManager. Update on empty/holding transitions, not every frame.
        __instance.cachedInteractKeyDisplay=next?heldDisplay:"[Grip]";
    }
    private static void ObjectPrompt(ref string prompt)
    {
        if(!Active||prompt==null||holdBuild==null)return;
        if(prompt!=lastPrompt)
        {
            lastPrompt=prompt;
            // Preserve native localized action text; rewrite only measured tags.
            adaptedPrompt=prompt.Replace(holdBuild,tapBuild,StringComparison.Ordinal).Replace(holdSell,vrHoldSell,StringComparison.Ordinal);
        }
        prompt=adaptedPrompt;
    }
    private static void ModeChanged()
    {
        bool mode=Active;if(mode==lastMode&&lastLeft==VrControls.LeftHanded&&lastSwap==VrControls.SwapSticks)return;lastMode=mode;lastLeft=VrControls.LeftHanded;lastSwap=VrControls.SwapSticks;HeldLabels();if(input!=null)Actions(input);if(hud!=null)PromptTokens();
        InteractionManager.LocalInstance?.RefreshKeyDisplayCache();
        if(hud==null)return;Apply();hud.RefreshKeyBindings();hud.RefreshHudDrawerKeys();hud.RefreshRecipeBookHint();hud.RefreshTownJobsHint();
    }
}
