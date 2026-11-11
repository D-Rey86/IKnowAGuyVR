using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class KeyboardAudit
{
    internal static bool Complete {get;private set;}
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_KEYBOARD_AUDIT")=="1";
    private static readonly bool grips=Environment.GetEnvironmentVariable("IKAG_XR_KEYBOARD_GRIP_AUDIT")=="1";
    private static bool UseLeft=>grips&&computer==0;
    private static string Button=>grips?"grip "+(UseLeft?"l":"r"):"trigger r";
    private static void Press()=>Send(Button+" 1");
    private static void Release()=>Send(Button+" 0");
    private static readonly string[] actions={"clear","q","shift","w","1","backspace","space","shift","e","x","e","backspace","e"};
    private static readonly string[] expected={"","q","q","qW","qW!","qW","qW ","qW ","qW e","xW e","xW e","xW ","xW e"};
    private static int phase,computer,key,step,downs,baselineDowns,originalMaxLength;
    private static float next=0;
    private static KitchenOrderDisplay kitchen;private static DarkWebComputer dark;
    private static TextField field;private static string original;
    private static VisualElement watched;
    private static Vector3 before,delta;
    private static readonly List<object> results=new();
    private static readonly EventCallback<PointerDownEvent> click=(Action<PointerDownEvent>)(_=>++downs);
    private static string Directory=>Environment.GetEnvironmentVariable("XRSIM_DIR");
    private static void Send(string command)=>File.WriteAllText(Path.Combine(Directory,"command.txt"),command+"\n");
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Aim(string name)=>Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint(name),UseLeft));
    internal static void Ray(bool rising,Vector3 origin,Vector3 direction,Vector3 hit,Vector3 center,float x,float y,float w,float h)
    {if(enabled&&rising)Plugin.Line($"KEYBOARD_RAY origin={origin:R} dir={direction:R} hit={hit:R} center={center:R} bounds={x:R}/{y:R}/{w:R}/{h:R}");}
    internal static void Hit(string key,float x,float y){if(enabled)Plugin.Line($"KEYBOARD_HIT key={key} xy={x:R}/{y:R}");}
    internal static void Edit(string stage,TextElement editor,char c,KeyCode code){if(enabled)Plugin.Line($"KEYBOARD_EDIT {stage} code={code} c={(int)c} focused={editor.hasFocus} value={editor.value}");}
    internal static void EnterState(string stage,TextElement editor)
    {if(enabled)Plugin.Line($"KEYBOARD_ENTER {stage} visibleText={editor.text} backend={editor.editingManipulator.editingUtilities.text} field={field.value} focus={editor.hasFocus}");}
    internal static void Tick()
    {
        if(Environment.GetEnvironmentVariable("IKAG_XR_HUD_KEYBOARD_REGRESSION")=="1"&&!ImmersiveHudAudit.Complete)return;
        if((Environment.GetEnvironmentVariable("IKAG_XR_REGISTER_ARCADE_AUDIT")=="1"&&!RegisterArcadeAudit.Complete)||!enabled||phase>20||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            float delay=.2f;
            if(phase==0)
            {
                kitchen=UnityEngine.Object.FindObjectOfType<KitchenOrderDisplay>();dark=UnityEngine.Object.FindObjectOfType<DarkWebComputer>();
                Check(kitchen!=null&&dark!=null,"Both computers required");
                UiInputAudit.Dump("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements","KeyboardTextEditorEventHandler","OnKeyDown","HandleEventBubbleUp");
                UiInputAudit.Dump("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements","TextEditingManipulator","HandleEventBubbleUp");
                UiInputAudit.Dump("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements","TextElement","HandleEventBubbleUp","get_hasFocus","get_isReadOnly","get_isSelectable");
                kitchen.OnInteract();delay=1.5f;
            }
            else if(phase==1)
            {
                if(computer==0){kitchen.supplierDeliveryUI.OpenRenamePopup();field=kitchen.supplierDeliveryUI.shopNameInput;watched=kitchen.supplierDeliveryUI.uiDocument.rootVisualElement;}
                else
                {
                    var ui=dark.darkWebUIManager;var tabs=new List<string>();foreach(var entry in ui.tabAddresses)tabs.Add(entry.Key);
                    Plugin.Line("KEYBOARD_DARK_TABS "+string.Join(",",tabs));ui.SwitchToTab("market");field=ui.clSearchField;watched=ui.uiDocument.rootVisualElement;
                }
                Check(field!=null,"Native editable field missing");original=field.value;originalMaxLength=field.maxLength;watched.RegisterCallback(click,TrickleDown.TrickleDown);field.Focus();delay=.6f;
            }
            else if(phase==2)
            {
                Check(ComputerKeyboard.IsOpen,"Textbox focus did not open keyboard");Check(ComputerKeyboard.Target.Pointer==field.textInputBase.textElement.Pointer,"Wrong native input target");
                baselineDowns=downs;Send("shot keyboard-"+computer+"-opened");ScreenCapture.CaptureScreenshot(Path.Combine(Directory,"keyboard-"+computer+"-monitor.png"));key=step=0;
            }
            else if(phase==3)
            {
                if(key>=actions.Length){phase=4;return;}
                if(step==0){if(key==10){field.maxLength=4;field.textInputBase.textElement.cursorIndex=4;field.textInputBase.textElement.selectIndex=4;}if(key==9){field.textInputBase.textElement.cursorIndex=0;field.textInputBase.textElement.selectIndex=1;}Aim(actions[key]);step=1;}
                else if(step==1){Press();step=2;delay=.25f;}
                else
                {
                    Release();Check(field.value==expected[key],$"Native key {actions[key]} mismatch: expected '{expected[key]}', got '{field.value}', open={ComputerKeyboard.IsOpen}");
                    Check(ComputerKeyboard.IsOpen&&ComputerKeyboard.Target.hasFocus,"Key stole text focus");Check(downs==baselineDowns,"Keyboard click reached computer behind it");
                    results.Add(new{computer,key=actions[key],value=field.value,focus=ComputerKeyboard.Target.hasFocus,clickThrough=downs-baselineDowns});
                    Plugin.Line($"KEYBOARD_NATIVE_PASS computer={computer} key={actions[key]} value={field.value}");++key;step=0;
                }
                next=Time.realtimeSinceStartup+delay;return;
            }
            else if(phase==4){before=ComputerKeyboard.Center;delta=(ComputerKeyboard.KeyPoint("0")-ComputerKeyboard.KeyPoint("1")).normalized*.12f+Vector3.up*.05f;Send(MotionPointer.AimAtWorld(ComputerKeyboard.TitlePoint,UseLeft));}
            else if(phase==5)Press();
            else if(phase==6)Send(MotionPointer.AimAtWorld(ComputerKeyboard.TitlePoint+delta,UseLeft));
            else if(phase==7)Release();
            else if(phase==8)
            {
                float error=(ComputerKeyboard.Center-before-delta).magnitude;Check(error<.01f,"Title drag failed: "+error);Check(field.value==expected[9]&&downs==baselineDowns,"Drag typed/clicked through");
                results.Add(new{computer,dragError=error});Send("shot keyboard-"+computer+"-moved");
            }
            else if(phase==9)Aim("close");
            else if(phase==10)Press();
            else if(phase==11)
            {
                Release();Check(!ComputerKeyboard.IsOpen,"Close key did not hide keyboard");Check(field.value==expected[9],"Close changed text");
                var editor=field.textInputBase.textElement;var e=PointerDownEvent.GetPooled();
                try{e.pointerId=PointerId.mousePointerId;e.pointerType=UnityEngine.UIElements.PointerType.mouse;e.button=0;e.pressedButtons=1;e.clickCount=1;e.position=field.worldBound.center;e.elementTarget=editor;editor.SendEvent(e);}finally{e.Dispose();}
                field.Focus();delay=.4f;
            }
            else if(phase==12){Check(ComputerKeyboard.IsOpen,"Clicking textbox did not reopen keyboard");baselineDowns=downs;Aim("enter");}
            else if(phase==13)Press();
            else if(phase==14)
            {
                Release();Check(!ComputerKeyboard.IsOpen,"Enter did not close keyboard");Check(field.value==expected[9],"Enter changed text");Check(downs==baselineDowns,"Enter clicked through");
                if(computer==1)Check(DarkWebUIManager.SearchFold(dark.darkWebUIManager.searchQuery)==DarkWebUIManager.SearchFold(field.value),"Native computer search did not receive changes");
                field.maxLength=originalMaxLength;field.value=original;watched.UnregisterCallback(click,TrickleDown.TrickleDown);watched=null;
                if(computer==0)kitchen.TriggerExit();else dark.TriggerExit();delay=1.5f;
            }
            else if(phase==15)
            {
                Check(!ComputerKeyboard.IsOpen&&!MachineInteractionBase.IsLocalPlayerInMachineMode,"Computer exit did not clean up keyboard");
                if(computer==0){computer=1;dark.OnInteract();phase=1;next=Time.realtimeSinceStartup+1.5f;return;}
                File.WriteAllText(Path.Combine(Directory,"keyboard-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
                Complete=true;Plugin.Line("KEYBOARD_AUDIT_COMPLETE both native fields, selection, length limit, characters, drag, close/reopen, Enter, no click-through and exits");phase=21;return;
            }
            Plugin.Line($"KEYBOARD_PHASE computer={computer} phase={phase} open={ComputerKeyboard.IsOpen} target={ComputerKeyboard.Target?.name} native={field?.value}");++phase;next=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e)
        {
            Plugin.Line("KEYBOARD_AUDIT_FAILED "+e);Send("trigger r 0");if(field!=null){field.maxLength=originalMaxLength;field.value=original;}phase=21;
        }
    }
}
