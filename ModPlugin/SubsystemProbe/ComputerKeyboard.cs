using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;
namespace IKnowAGuyVR.SubsystemProbe;

// SupermarketSimulatorVR's generated layout and direct-ray interaction, adapted
// to the existing UITK panel. See LICENSES/SupermarketSimulatorVR-keyboard.txt.
// No capture camera, render texture, collider, mesh or replacement text editor.
internal static class ComputerKeyboard
{
    private sealed class Key
    {
        internal string Normal,Shifted,Action;
        internal Label Label;
        internal float X0,Y0,X1,Y1;
    }
    private const float Height=700;
    private static float Width=>numeric?800:1600;
    private static float WorldWidth=>numeric?.55f:.90f; private static float WorldHeight=>numeric?.48125f:.394f;
    private static bool numeric,builtNumeric;
    private static CashRegisterMachine register;
    private static ForgeryStation forgery;
    private static VisualElement pointerDot;
    private static bool dotVisible;
    private static TypingMiniGame typing;
    private static VisualElement typingSurface;
    private static readonly Color Navy=new(.025f,.10f,.19f,.98f),Blue=new(.045f,.25f,.43f,1),Hover=new(.10f,.58f,.94f,1),White=new(.97f,.99f,1,1);
    private static readonly List<Key> keys=new(60);
    private static readonly EventCallback<FocusInEvent> focus=(Action<FocusInEvent>)Focused;
    private static readonly EventCallback<PointerDownEvent> pointer=(Action<PointerDownEvent>)Clicked;
    private static readonly EventCallback<GeometryChangedEvent> geometry=(Action<GeometryChangedEvent>)Geometry;
    private static MachineInteractionBase machine;
    private static UIDocument document;
    private static VisualElement boundRoot,root;
    private static TextElement target;
    private static HUDManager hud;
    private static UIDocument menuSource,menuOverlay;
    private static GameObject menuOverlayObject;
    private static PanelSettings menuSettings;
    internal static void BindMenu(UIDocument source){Unbind();menuSource=source;}
    private static Key hovered;
    private static bool open,placed,uppercase,pressedBefore,inputArmed,dragging,boundsReady,failed,failureLogged;
    private static float ux,uy,uw,uh;
    private static Vector3 center,right,up,normal,dragOrigin,dragOffset;
    private static Matrix4x4 headFromUi,previousProjection,previousGroup;
    private static RenderData data;
    private static RenderChainCommand push,pop,head,tail;
    private static DrawParams groupParams;
    private static bool inGroup,vrGroup;
    internal static bool IsOpen=>open;
    internal static void Tick()
    {
        try
        {
            if(failed){if(open)Close(false);return;}
            var active=MachineInteractionBase.IsLocalPlayerInMachineMode?MachineInteractionBase.CurrentActiveMachine:null;
            var arcade=ArcadeMiniGameManager.LocalInstance;
            var currentTyping=arcade!=null&&arcade.IsGameActive&&arcade.activeType==ArcadeMiniGameType.Typing?arcade.typingGame:null;
            if(menuSource!=null)document=menuSource;
            else if(currentTyping!=null)
            {
                if(typing==null||typing.Pointer!=currentTyping.Pointer){Unbind();typing=currentTyping;typingSurface=arcade.viewportElement;document=arcade.uiDocument;}
            }
            else if(active==null)
            {if(machine!=null||typing!=null||boundRoot!=null)Unbind();return;}
            else if(machine==null||active.Pointer!=machine.Pointer||typing!=null)
            {
                Unbind();machine=active;
                var kitchen=active.TryCast<KitchenOrderDisplay>();var dark=active.TryCast<DarkWebComputer>();register=active.TryCast<CashRegisterMachine>();
                forgery=active.TryCast<ForgeryStation>();
                numeric=register!=null;
                document=forgery!=null?ForgeryPrintUIManager.LocalInstance?.uiDocument:kitchen!=null?kitchen.supplierDeliveryUI?.uiDocument:register!=null?register.cashRegisterUI?.uiDocument:dark?.darkWebUIManager?.uiDocument;
            }
            var current=document==null?null:document.rootVisualElement;
            if(current!=null&&(boundRoot==null||current.Pointer!=boundRoot.Pointer))
            {
                Unregister();boundRoot=current;
                boundRoot.RegisterCallback(focus,TrickleDown.TrickleDown);
                boundRoot.RegisterCallback(pointer,TrickleDown.TrickleDown);
                EnsureVisuals();
                if(typing!=null)OpenSpecial(typing.sentenceLabel);
            }
            if(open&&((register!=null&&!RegisterReady)||(typing==null&&register==null&&(target==null||target.panel==null||target.isReadOnly||!target.enabledInHierarchy||!target.hasFocus))||(menuSource==null&&(hud==null||hud.IsEscapeMenuOpen||MachineInteractionBase.isLocalMachineTransitionInProgress))))Close(false);
        }
        catch(Exception e){Fail(e);}
    }
    private static void Unregister()
    {
        if(boundRoot!=null){boundRoot.UnregisterCallback(focus,TrickleDown.TrickleDown);boundRoot.UnregisterCallback(pointer,TrickleDown.TrickleDown);}
        boundRoot=null;
    }
    private static void Unbind()
    {
        Close(false);Unregister();machine=null;register=null;forgery=null;typing=null;typingSurface=null;numeric=false;document=null;menuSource=null;
        if(menuOverlayObject!=null)UnityEngine.Object.Destroy(menuOverlayObject);
        if(menuSettings!=null)UnityEngine.Object.Destroy(menuSettings);
        menuOverlay=null;menuOverlayObject=null;menuSettings=null;
    }
    private static bool RegisterReady=>register!=null&&register.isPaymentPending&&register.currentMode==CashRegisterMode.CreditCard&&!register.isProcessingPayment&&!register.IsPowerOut;
    private static void OpenSpecial(TextElement fontSource)
    {
        if(!MotionInput.Enabled)return;
        EnsureVisuals();if(root==null||open)return;
        open=true;placed=false;inputArmed=false;dragging=false;uppercase=false;
        if(fontSource!=null)root.style.unityFontDefinition=fontSource.resolvedStyle.unityFontDefinition;
        root.style.display=DisplayStyle.Flex;RefreshLabels();
    }
    private static bool Within(VisualElement node,VisualElement parent)
    {if(parent==null)return false;for(var v=node;v!=null;v=v.parent)if(v.Pointer==parent.Pointer)return true;return false;}
    private static TextElement Editable(VisualElement node)
    {
        if(boundRoot==null)return null;
        for(var v=node;v!=null&&v.Pointer!=boundRoot.Pointer;v=v.parent)
        {
            var e=v.TryCast<TextElement>();if(e!=null&&!e.isReadOnly)return e;
            var f=v.TryCast<TextField>();if(f!=null&&!f.isReadOnly)return f.textInputBase.textElement;
            if(v.ClassListContains("unity-base-text-field"))return Descendant(v);
        }
        return null;
    }
    private static TextElement Descendant(VisualElement node)
    {
        var e=node.TryCast<TextElement>();if(e!=null&&!e.isReadOnly)return e;
        for(int i=0;i<node.childCount;++i){var found=Descendant(node.ElementAt(i));if(found!=null)return found;}return null;
    }
    private static void Focused(FocusInEvent e){try{var editor=Editable(e.elementTarget);if(editor!=null)Open(editor);}catch(Exception error){Fail(error);}}
    private static void Clicked(PointerDownEvent e)
    {
        if(e.button!=0)return;
        try{
            if(register!=null){if(RegisterReady&&Within(e.elementTarget,register.cashRegisterUI.posInputDisplay.parent))OpenSpecial(register.cashRegisterUI.posInputDisplay);return;}
            if(typing!=null){if(Within(e.elementTarget,typingSurface))OpenSpecial(typing.sentenceLabel);return;}
            var editor=Editable(e.elementTarget);if(editor!=null)Open(editor);else if(open)Close(false);
        }catch(Exception error){Fail(error);}
    }
    private static void Open(TextElement editor)
    {
        if(!MotionInput.Enabled||failed||editor.isReadOnly||!editor.enabledInHierarchy)return;
        EnsureVisuals();if(root==null)return;
        if(target!=null&&target.Pointer==editor.Pointer&&open)return;
        target=editor;
        if(!open)
        {
            open=true;placed=false;inputArmed=false;dragging=false;uppercase=false;
            root.style.unityFontDefinition=editor.resolvedStyle.unityFontDefinition;
            root.style.display=DisplayStyle.Flex;RefreshLabels();
        }
    }
    private static void Close(bool blur)
    {
        HideDot();
        var old=target;target=null;open=placed=dragging=inputArmed=false;SetHover(null);
        if(root!=null)root.style.display=DisplayStyle.None;
        if(blur&&old!=null&&old.panel!=null)old.Blur();
    }
    internal static void Suspend(){ReleaseInput();if(open)Close(false);}
    internal static void ReleaseInput(){HideDot();dragging=inputArmed=false;SetHover(null);}
    private static void Fail(Exception e)
    {
        failed=true;
        if(!failureLogged){failureLogged=true;Plugin.Line("COMPUTER_KEYBOARD_DISABLED "+e);}
    }
    private static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    internal static bool Process(Vector3 origin,Vector3 direction,bool pressed,Vector3 headPosition,Quaternion headRotation,out Vector3 hit)
    {
        hit=default;bool owns=false;
        try
        {
            if(!open||failed||!boundsReady){HideDot();return false;}
            if(!placed)
            {
                var forward=headRotation*Vector3.forward;forward.y=0;
                if(forward.sqrMagnitude<1e-6f){HideDot();return false;}
                // The TV menu camera is tilted upward. Keep its keyboard's
                // initial offset in the viewing basis so all rows fit below
                // the TV, rather than below the scene's horizontal horizon.
                var q=menuSource!=null?headRotation:Quaternion.LookRotation(forward.normalized,Vector3.up);
                right=q*Vector3.right;up=q*Vector3.up;normal=q*Vector3.forward;
                center=headPosition+normal*1.35f-up*.55f;placed=true;
            }
            if(!inputArmed){inputArmed=!pressed;pressedBefore=pressed;}
            bool rising=inputArmed&&pressed&&!pressedBefore;pressedBefore=pressed;
            if(!pressed)dragging=false;
            var plane=dragging?dragOrigin:center;float denominator=Dot(direction,normal);
            if(denominator<=.0001f){HideDot();SetHover(null);return false;}
            float distance=Dot(plane-origin,normal)/denominator;
            if(distance<.02f||distance>4){HideDot();SetHover(null);return false;}
            hit=origin+direction*distance;
#if IKAG_DIAGNOSTICS
            KeyboardAudit.Ray(rising,origin,direction,hit,center,ux,uy,uw,uh);
#endif
            if(dragging){HideDot();center=hit+dragOffset;return true;}
            var local=hit-center;float x=(Dot(local,right)/WorldWidth+.5f)*uw+ux,y=(.5f-Dot(local,up)/WorldHeight)*uh+uy;
            if(x<ux||x>ux+uw||y<uy||y>uy+uh){HideDot();SetHover(null);return false;}
            owns=true;
            if(pointerDot!=null){pointerDot.style.translate=new Translate(x-ux-10,y-uy-10,0);if(!dotVisible){pointerDot.style.display=DisplayStyle.Flex;dotVisible=true;}}
            Key key=null;for(int i=0;i<keys.Count;++i){var k=keys[i];if(x>=k.X0&&x<=k.X1&&y>=k.Y0&&y<=k.Y1){key=k;break;}}
            SetHover(key);
            if(rising)
            {
#if IKAG_DIAGNOSTICS
                KeyboardAudit.Hit(key?.Action??key?.Normal,x,y);
#endif
                if(key!=null){Activate(key);MotionHaptics.Pulse(MotionPointer.PointerLeft,.12f,.02f);}
                else if(y<uy+uh*.1f){dragging=true;dragOrigin=center;dragOffset=center-hit;}
            }
            return true;
        }
        catch(Exception e){Fail(e);return owns;}
    }
    private static void HideDot(){if(dotVisible&&pointerDot!=null)pointerDot.style.display=DisplayStyle.None;dotVisible=false;}
    private static void SetHover(Key key)
    {
        if(hovered==key)return;if(hovered!=null)hovered.Label.style.backgroundColor=Blue;
        hovered=key;if(key!=null)key.Label.style.backgroundColor=Hover;
    }
    private static void NativeKey(char c,KeyCode code,EventModifiers modifiers=EventModifiers.None)
    {
        if(register!=null)
        {
            if(!RegisterReady)return;
            if(c>='0'&&c<='9')register.SimulatePOSKeyPress((POSKeyType)(c-'0'));
            else if(c=='.')register.SimulatePOSKeyPress(POSKeyType.Decimal);
            else if(code==KeyCode.Backspace)register.SimulatePOSKeyPress(POSKeyType.Backspace);
            else if(code==KeyCode.Return)register.SimulatePOSKeyPress(POSKeyType.Enter);
            return;
        }
        if(typing!=null){if(typing.IsRunning&&c>=' ')typing.OnTextInput(c);return;}
        var editor=target;if(editor==null||editor.isReadOnly||!editor.hasFocus)return;
        if(code==KeyCode.Return)
        {
#if IKAG_DIAGNOSTICS
            KeyboardAudit.EnterState("before",editor);
#endif
            var down=KeyDownEvent.GetPooled(c,code,modifiers);
            try{down.elementTarget=editor;editor.SendEvent(down);}finally{down.Dispose();}
#if IKAG_DIAGNOSTICS
            KeyboardAudit.EnterState("after",editor);
#endif
            return;
        }
        if(code==KeyCode.A&&(modifiers&EventModifiers.Control)!=0){editor.selection.SelectAll();return;}
        var edition=editor.edition;var editing=editor.editingManipulator.editingUtilities;
        bool changed;
        if(code==KeyCode.Backspace)changed=editing.Backspace();
        else
        {
            if(edition.AcceptCharacter!=null&&!edition.AcceptCharacter.Invoke(c))return;
            changed=editing.Insert(c);
        }
        if(!changed)return;
        string value=edition.CullString(editing.text);
        if(value!=editing.text)editing.SetTextWithoutNotify(value);
        edition.UpdateText(value);
        if(!edition.isDelayed)edition.UpdateValueFromText?.Invoke();
        edition.UpdateScrollOffset?.Invoke(false);
    }
    private static void Activate(Key key)
    {
        switch(key.Action)
        {
            case "shift":uppercase=!uppercase;RefreshLabels();break;
            case "backspace":NativeKey('\b',KeyCode.Backspace);break;
            case "space":NativeKey(' ',KeyCode.Space);break;
            case "clear":NativeKey('a',KeyCode.A,EventModifiers.Control);NativeKey('\b',KeyCode.Backspace);break;
            case "enter":NativeKey('\n',KeyCode.Return);Close(true);break;
            case "close":Close(true);break;
            default:var value=uppercase?key.Shifted:key.Normal;NativeKey(value[0],KeyCode.None,uppercase?EventModifiers.Shift:EventModifiers.None);break;
        }
    }
    private static void RefreshLabels(){foreach(var key in keys)key.Label.text=key.Action=="shift"?(uppercase?"SHIFT ON":"SHIFT"):key.Action==null?(uppercase?key.Shifted:key.Normal):key.Normal;}
    private static Key Pair(string normal,string shifted)=>new(){Normal=normal,Shifted=shifted};
    private static Key Action(string label,string action)=>new(){Normal=label,Shifted=label,Action=action};
    private static Key[] Letters(string letters){var result=new Key[letters.Length];for(int i=0;i<letters.Length;++i){var upper=letters[i].ToString();result[i]=Pair(upper.ToLowerInvariant(),upper);}return result;}
    private static Label Label(string text,float x,float y,float width,float height,Color background)
    {
        var label=new Label(text){pickingMode=PickingMode.Ignore,focusable=false};var s=label.style;
        s.position=Position.Absolute;s.left=x;s.top=y;s.width=width;s.height=height;s.backgroundColor=background;s.color=White;s.fontSize=31;s.unityTextAlign=TextAnchor.MiddleCenter;s.whiteSpace=WhiteSpace.NoWrap;
        s.borderTopLeftRadius=s.borderTopRightRadius=s.borderBottomLeftRadius=s.borderBottomRightRadius=8;
        root.Add(label);return label;
    }
    private static void AddRow(float bottom,float height,Key[] row,float left=45,float rightMargin=45)
    {
        float total=0;foreach(var k in row)total+=k.Action=="shift"||k.Action=="backspace"?1.6f:k.Action=="space"?5:k.Action=="close"||k.Action=="clear"?1.5f:k.Action=="enter"?2:1;
        float unit=(Width-left-rightMargin-12*(row.Length-1))/total,x=left;
        foreach(var k in row){float weight=k.Action=="shift"||k.Action=="backspace"?1.6f:k.Action=="space"?5:k.Action=="close"||k.Action=="clear"?1.5f:k.Action=="enter"?2:1;float w=unit*weight;k.Label=Label(k.Normal,x,Height-bottom-height,w,height,Blue);keys.Add(k);x+=w+12;}
    }
    private static void EnsureVisuals()
    {
        var current=HUDManager.LocalInstance;
        UIDocument doc;
        if(menuSource!=null)
        {
            if(menuOverlay==null)
            {
                if(menuSource.panelSettings==null)return;
                menuSettings=UnityEngine.Object.Instantiate(menuSource.panelSettings);
#if IKAG_DIAGNOSTICS
                Plugin.Line("MENU_KEYBOARD_PANEL sourceClear="+menuSettings.clearColor+" color="+menuSettings.colorClearValue);
#endif
                // The menu's TV texture clears its own background. An eye
                // overlay must preserve the scene color already rendered.
                menuSettings.targetTexture=null;menuSettings.clearColor=false;
                menuOverlayObject=new GameObject("IKAG_MenuKeyboard");
                menuOverlay=menuOverlayObject.AddComponent<UIDocument>();menuOverlay.panelSettings=menuSettings;menuOverlay.sortingOrder=100;
                menuOverlay.rootVisualElement.pickingMode=PickingMode.Ignore;
            }
            doc=menuOverlay;
        }
        else {if(current==null)return;doc=typing!=null||forgery!=null?document:current.GetComponent<UIDocument>();}
        if(doc==null||doc.rootVisualElement==null)return;
        if(root!=null&&root.panel!=null&&root.parent?.Pointer==doc.rootVisualElement.Pointer&&builtNumeric==numeric)
        {
            // Arcade overlays draw after the HUD; reuse their existing panel so
            // the keyboard is its final child and remains above the game.
            if(root.parent?.Pointer!=doc.rootVisualElement.Pointer){root.RemoveFromHierarchy();doc.rootVisualElement.Add(root);}
            return;
        }
        hud=current;
        if(root!=null)root.RemoveFromHierarchy();builtNumeric=numeric;
        keys.Clear();hovered=null;boundsReady=false;data=null;push=pop=head=tail=null;
        root=new VisualElement{name="IKAG_ComputerKeyboard",pickingMode=PickingMode.Ignore,focusable=false,usageHints=UsageHints.GroupTransform};
        var s=root.style;s.position=Position.Absolute;s.left=0;s.top=0;s.width=Width;s.height=Height;s.backgroundColor=Navy;s.display=DisplayStyle.None;s.overflow=Overflow.Visible;
        root.RegisterCallback(geometry);
        Label(numeric?"NUMBER PAD - DRAG HERE":"VR KEYBOARD - HOLD GRIP HERE TO MOVE",45,17,Width-190,48,Navy);
        var close=Action("X","close");close.Label=Label("X",Width-110,12,70,52,Blue);keys.Add(close);
        if(numeric)
        {
            AddRow(475,130,new[]{Pair("7","7"),Pair("8","8"),Pair("9","9")});
            AddRow(335,130,new[]{Pair("4","4"),Pair("5","5"),Pair("6","6")});
            AddRow(195,130,new[]{Pair("1","1"),Pair("2","2"),Pair("3","3")});
            AddRow(55,130,new[]{Pair("0","0"),Pair(".","."),Action("BACK","backspace"),Action("ENTER","enter")});
        }
        else
        {
        AddRow(515,92,new[]{Pair("1","!"),Pair("2","@"),Pair("3","#"),Pair("4","$"),Pair("5","%"),Pair("6","^"),Pair("7","&"),Pair("8","*"),Pair("9","("),Pair("0",")"),Pair("-","_"),Pair(".",">"),Pair(",","?"),Pair("'","\""),Pair(";",":"),Pair("/","\\")});
        AddRow(405,92,Letters("QWERTYUIOP"));AddRow(295,92,Letters("ASDFGHJKL"),95,95);
        AddRow(185,92,new[]{Action("SHIFT","shift"),Pair("z","Z"),Pair("x","X"),Pair("c","C"),Pair("v","V"),Pair("b","B"),Pair("n","N"),Pair("m","M"),Action("BACK","backspace")});
        AddRow(55,100,new[]{Action("CLOSE","close"),Action("SPACE","space"),Action("CLEAR","clear"),Action("ENTER","enter")});
        }
        // UI is drawn at overlay depth, over scene geometry including the beam.
        // Reuse the keyboard panel and its exact hit coordinates for the endpoint.
        pointerDot=new VisualElement{name="IKAG_KeyboardPointer",pickingMode=PickingMode.Ignore,usageHints=UsageHints.DynamicTransform};
        dotVisible=false;var ds=pointerDot.style;ds.position=Position.Absolute;ds.left=0;ds.top=0;ds.width=20;ds.height=20;ds.backgroundColor=Color.cyan;
        ds.borderTopLeftRadius=ds.borderTopRightRadius=ds.borderBottomLeftRadius=ds.borderBottomRightRadius=10;ds.display=DisplayStyle.None;
        root.Add(pointerDot);
        doc.rootVisualElement.Add(root);
    }
    private static void Geometry(GeometryChangedEvent e)
    {
        var r=root.worldBound;ux=r.x;uy=r.y;uw=r.width;uh=r.height;boundsReady=uw>0&&uh>0;
        foreach(var key in keys){var b=key.Label.worldBound;key.X0=b.x;key.Y0=b.y;key.X1=b.x+b.width;key.Y1=b.y+b.height;}
    }
    internal static void Prepare(Matrix4x4 headWorld)
    {
        if(!open||!placed||!boundsReady||failed)return;
        var m=Matrix4x4.zero;float sx=WorldWidth/uw,sy=-WorldHeight/uh;
        var x=right*sx;var y=up*sy;var t=center-x*(ux+uw*.5f)-y*(uy+uh*.5f);
        m.m00=x.x;m.m10=x.y;m.m20=x.z;m.m01=y.x;m.m11=y.y;m.m21=y.z;m.m03=t.x;m.m13=t.y;m.m23=t.z;m.m33=1;
        headFromUi=headWorld.inverse*m;
    }
    internal static void Tree(BaseRuntimePanel panel)
    {
        if(!open||root?.panel==null||root.panel.Pointer!=panel.Pointer)return;
        var current=root.renderData;if(current==null)return;
        if(data==null||data.Pointer!=current.Pointer){data=current;head=tail=push=pop=null;}
        if((data.firstHeadCommand?.Pointer??IntPtr.Zero)!=(head?.Pointer??IntPtr.Zero))
        {head=data.firstHeadCommand;push=null;for(var c=head;c!=null&&c.owner.Pointer==data.Pointer;c=c.next)if(c.type==CommandType.PushView){push=c;break;}}
        if((data.firstTailCommand?.Pointer??IntPtr.Zero)!=(tail?.Pointer??IntPtr.Zero))
        {tail=data.firstTailCommand;pop=null;for(var c=tail;c!=null&&c.owner.Pointer==data.Pointer;c=c.next)if(c.type==CommandType.PopView){pop=c;break;}}
    }
    internal static void Command(IntPtr command,IntPtr drawParams)
    {
        if(inGroup&&pop!=null&&command==pop.Pointer){EndDraw();return;}
        if(!open||push==null||command!=push.Pointer)return;
        previousProjection=Utility.GetUnityProjectionMatrix();groupParams=UiCommandBridge.Parameters(drawParams);inGroup=true;
        var bounds=root.worldBound;Matrix4x4 projection=default;
        vrGroup=placed&&boundsReady&&!failed&&EyeScreenUI.WorldPanelProjection(headFromUi,bounds,out projection);
        if(vrGroup)
        {
            previousGroup=EyeScreenUI.GroupProjection(projection);
            GL.LoadProjectionMatrix(EyeScreenUI.WithOverlayDepth(projection,bounds));Utility.DisableScissor();
        }
        else
        {
            // This is a VR-only group. Keep the monitor's native computer UI.
            var hidden=previousProjection;
            hidden.m00=-2*hidden.m30;hidden.m01=-2*hidden.m31;hidden.m02=-2*hidden.m32;hidden.m03=-2*hidden.m33;
            hidden.m10=hidden.m00;hidden.m11=hidden.m01;hidden.m12=hidden.m02;hidden.m13=hidden.m03;
            GL.LoadProjectionMatrix(hidden);
        }
    }
    internal static void EndDraw()
    {
        if(!inGroup)return;inGroup=false;GL.LoadProjectionMatrix(previousProjection);
        if(vrGroup){EyeScreenUI.GroupProjection(previousGroup);if(groupParams!=null)EyeScreenUI.RestoreScissor(groupParams.scissor.Peek());}
        groupParams=null;
    }
#if IKAG_DIAGNOSTICS
    internal static TextElement Target=>target;
    internal static Vector3 KeyPoint(string key){foreach(var k in keys)if(k.Normal==key||k.Action==key){float x=(k.X0+k.X1)*.5f,y=(k.Y0+k.Y1)*.5f;return center+right*((x-ux)/uw-.5f)*WorldWidth+up*(.5f-(y-uy)/uh)*WorldHeight;}throw new InvalidOperationException("Unknown keyboard key "+key);}
    internal static Vector3 TitlePoint=>center+up*(WorldHeight*.46f);
    internal static Vector3 Center=>center;
#endif
}

