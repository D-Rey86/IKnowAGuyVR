using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using PointerType=UnityEngine.UIElements.PointerType;
namespace IKnowAGuyVR.SubsystemProbe;
internal static unsafe class MotionPointer
{
    // StateEvent header (20), format (4), aligned MouseState (32).
    [StructLayout(LayoutKind.Explicit,Size=56)] private struct MouseEvent
    {
        [FieldOffset(0)] internal uint Type; [FieldOffset(4)] internal ushort Size;[FieldOffset(6)] internal ushort Device;
        [FieldOffset(8)] internal double Time;[FieldOffset(16)] internal uint Id;[FieldOffset(20)] internal uint Format;
        [FieldOffset(24)] internal Vector2 Position;[FieldOffset(32)] internal Vector2 Delta;[FieldOffset(40)] internal Vector2 Scroll;
        [FieldOffset(48)] internal ushort Buttons;
    }
    private static InputDevice mouse;
    private static bool wasPressed,wasSecondary;
    private static bool leftCursorVisible=true,rightCursorVisible=true;
    private static int cursorToggleFrame=-1;
    internal static bool CursorVisible(bool useLeft)=>useLeft?leftCursorVisible:rightCursorVisible;
    internal static void ToggleCursor()
    {
        if(cursorToggleFrame==Time.frameCount)return;cursorToggleFrame=Time.frameCount;
        bool useLeft=StereoTrial.MenuActive||UnityEngine.Cursor.visible||ImmersiveHud.PointerOwned||ArcadeMotion.Active?PointerLeft:DistanceGrips.AimLeft;
        if(useLeft)leftCursorVisible=!leftCursorVisible;else rightCursorVisible=!rightCursorVisible;
        if(laser!=null)laser.enabled=false;
    }
    private static Vector2 lastPoint;
    private static Ray ray;
    private static Ray rightRay;
    private static int handFrame=-1;private static bool pointerLeft,previousLeftGrip,previousRightGrip,previousPointerTrigger;
    internal static bool PointerLeft{get{SelectHand();return pointerLeft;}}
    internal static bool PointerTracked=>(PointerLeft?ControllerTransport.Left:ControllerTransport.Right).Tracked;
    internal static bool PrimaryHeld{get{SelectHand();var h=pointerLeft?ControllerTransport.Left:ControllerTransport.Right;return (pointerLeft==VrControls.LeftHanded&&h.Trigger)||(h.Grip&&!WorldPress.OwnsGrip(pointerLeft));}}
    internal static void ResetPreferredHand()
    {
        Suspend();pointerLeft=VrControls.LeftHanded;previousLeftGrip=ControllerTransport.Left.Grip;previousRightGrip=ControllerTransport.Right.Grip;
        previousPointerTrigger=VrControls.Main.Trigger;handFrame=rayFrame=leftRayFrame=-1;meshFrame=-1;surfaceFrame=-100;
        blockedUntilRelease=true;
    }
    private static void SelectHand()
    {
        if(handFrame==Time.frameCount)return;handFrame=Time.frameCount;ControllerTransport.Sample();
        var l=ControllerTransport.Left;var r=ControllerTransport.Right;
        bool locked=wasPressed||ImmersiveHud.PointerLatched||(keyboardOwned&&((pointerLeft?l.Grip:r.Grip)||(pointerLeft==VrControls.LeftHanded&&VrControls.Main.Trigger)));
        bool next=pointerLeft;
        if(!locked)
        {
            if(VrControls.Main.Tracked&&VrControls.Main.Trigger&&!previousPointerTrigger)next=VrControls.LeftHanded;
            else if(l.Tracked&&l.Grip&&!previousLeftGrip)next=true;
            else if(r.Tracked&&r.Grip&&!previousRightGrip)next=false;
            else if(pointerLeft&&!l.Tracked)next=false;
            else if(!pointerLeft&&!r.Tracked&&l.Tracked)next=true;
        }
        if(next!=pointerLeft){pointerLeft=next;meshFrame=-1;surfaceFrame=-100;if(previousRay)blockedUntilRelease=false;}
        previousLeftGrip=l.Grip;previousRightGrip=r.Grip;previousPointerTrigger=VrControls.Main.Trigger;
    }
    private static bool validRay;
    private static int rayFrame=-1;
    private static bool rayTracked,cashSecondaryArmed;
    internal static bool TryControllerRay(out Ray result)
    =>TryControllerRay(PointerLeft,out result);
    private static Ray leftRay;private static int leftRayFrame=-1;private static bool leftRayTracked;
    internal static bool TryControllerRay(bool useLeft,out Ray result)
    {
        if(useLeft)
        {
            if(leftRayFrame!=Time.frameCount)
            {
                leftRayFrame=Time.frameCount;ControllerTransport.Sample();var hand=ControllerTransport.Left;
                leftRayTracked=MotionInput.Enabled&&Probe.Focused&&hand.Tracked;
                if(leftRayTracked&&StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out var p,out var q))leftRay=new Ray(p,q*Vector3.forward);
                else leftRayTracked=false;
            }
            result=leftRay;return leftRayTracked;
        }
        if(rayFrame!=Time.frameCount)
        {
            rayFrame=Time.frameCount;ControllerTransport.Sample();var hand=ControllerTransport.Right;
            rayTracked=MotionInput.Enabled&&Probe.Focused&&hand.Tracked;
            if(rayTracked&&StereoTrial.TryTrackingPose(hand.AimPosition,hand.AimRotation,out var p,out var q))rightRay=new Ray(p,q*Vector3.forward);
            else rayTracked=false;
        }
        result=rightRay;return rayTracked;
    }
    internal static void RecordMachineHit(Vector3 point){surfaceFrame=Time.frameCount;surfacePoint=point;}

    private static bool previousTutorial;
    private static bool keyboardOwned;private static BaseRuntimePanel hudPanel;
    private static bool previousRay,blockedUntilRelease;
    // A wrist action opens a different panel while the same trigger is held.
    // Consume that press so it cannot also click a control on the new panel.
    internal static void ConsumePrimaryUntilRelease()=>blockedUntilRelease=true;
    private static int surfaceFrame=-100;
    private static Vector3 surfacePoint;
    private static LineRenderer laser;
    private static Material laserMaterial;private static Color laserColor;
    private static bool shaderTried;
    private static UIDocument menuDocument;
    private static BaseRuntimePanel menuPanel;
    private static UIDocument arcadeDocument;
    private static BaseRuntimePanel arcadePanel;
    private static Vector2 menuPoint,previousMenuPoint;
    private static bool menuHit;
    private static PointerMoveEvent lastMenuMove;
    private static PointerDownEvent lastMenuDown;
    private static PointerUpEvent lastMenuUp;
    private static WheelEvent lastMenuWheel;
    private static float scrollSteps;
    private static int meshFrame=-1;
    private static IntPtr meshOwner;
    private static bool meshHit;
    private static Vector3 meshPoint;
#if IKAG_DIAGNOSTICS
    private static int worldRayCalls,worldRayOverrides,worldPickCalls,positionEvents,rayEvents;
    private static int meshCalls,meshHits;
    private static Ray lastNativeRay;
#endif
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.motion-pointer");
        h.Patch(typeof(RenderTextureScreenInput).GetMethod("TryHitSurface"),prefix:Hook(nameof(ScreenRay)),postfix:Hook(nameof(SurfaceHit)));
        h.Patch(typeof(WorldDocumentRaycaster).GetMethod("GetWorldRay"),postfix:Hook(nameof(WorldPanelRay)));
        h.Patch(typeof(PhysicsDocumentPicker).GetMethod("TryPickWithCapture"),postfix:Hook(nameof(WorldPanelHit)));
#if IKAG_DIAGNOSTICS
        // Optional native output pointers cannot pass through a counter trampoline.
        // Keep default event dispatch unmodified in diagnostic builds too.
#endif
    }
    private static HarmonyMethod Hook(string n)=>new(typeof(MotionPointer).GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic));
    internal static void BindMenu(UIDocument document){menuDocument=document;menuPanel=null;meshFrame=-1;surfaceFrame=-100;previousRay=false;shaderTried=false;}
    internal static void Suspend()
    {
        validRay=menuHit=false;
        if(hudPanel!=null)SendMenu(false,hudPanel);
        else if(wasPressed||wasSecondary)Send(new Vector2(-10000,-10000),false);
        hudPanel=null;wasPressed=wasSecondary=previousRay=keyboardOwned=cashSecondaryArmed=false;scrollSteps=0;
        ComputerKeyboard.Suspend();if(laser!=null)laser.enabled=false;
    }
    internal static void Tick()
    {
        ControllerTransport.Sample();SelectHand();var right=VrControls.Turn;bool primary=PrimaryHeld;
        bool hudHit=ImmersiveHud.Pointer(out var currentHudPanel,out var hudPoint,out var hudWorld,out var nativeHud);
        bool tutorialMode=ContextHud.PointerMode;bool tutorialHit=ContextHud.Pointer(out var tutorialPanel,out var tutorialPoint,out var tutorialWorld);
        if(tutorialHit){hudHit=true;currentHudPanel=tutorialPanel;hudPoint=tutorialPoint;hudWorld=tutorialWorld;nativeHud=true;}
        validRay=MotionInput.Enabled&&Probe.Focused&&PointerTracked&&(UnityEngine.Cursor.visible||ArcadeMotion.Active||hudHit)&&!MotionInput.WheelOpen;
        if(hudPanel!=null&&!hudHit){bool wasValid=validRay;validRay=false;menuHit=false;SendMenu(false,hudPanel);hudPanel=null;validRay=wasValid;}
        if(validRay){if(!previousRay||(tutorialMode&&!previousTutorial))blockedUntilRelease=primary;previousRay=true;if(!primary)blockedUntilRelease=false;}else previousRay=false;
        previousTutorial=tutorialMode;
        if(tutorialMode&&!tutorialHit)
        {
            var popup=ContextHud.PopupPanel;menuHit=false;if(popup!=null)SendMenu(false,popup);keyboardOwned=false;ComputerKeyboard.ReleaseInput();
            if(TryControllerRay(out var offRay))UpdateLaser(offRay.origin,offRay.GetPoint(1.25f));else if(laser!=null)laser.enabled=false;return;
        }
        if(hudHit&&validRay&&TryControllerRay(out ray))
        {
            hudPanel=currentHudPanel;menuHit=nativeHud;menuPoint=hudPoint;float a=right.Stick.y;scrollSteps=ImmersiveHud.BoardOwnsStick||Math.Abs(a)<=.2f?0:Math.Sign(a)*(Math.Min(1,Math.Abs(a))-.2f)/.8f*10*Time.unscaledDeltaTime;
            SendMenu(nativeHud&&primary&&!blockedUntilRelease,hudPanel);UpdateLaser(ray.origin,hudWorld);return;
        }
        if(!validRay) {cashSecondaryArmed=false;keyboardOwned=false;ComputerKeyboard.ReleaseInput();previousRay=false;if(wasPressed||wasSecondary) Send(new Vector2(-10000,-10000),false);GameplayCursor();return;}
        if(!TryControllerRay(out ray))return;
        var p=ray.origin;
        float axis=right.Stick.y;scrollSteps=Math.Abs(axis)<=.2f?0:Math.Sign(axis)*(Math.Min(1,Math.Abs(axis))-.2f)/.8f*10*Time.unscaledDeltaTime;
        // The existing overlay is fitted at two metres in tracked center-head space.
        if(!StereoTrial.TryHeadWorld(out var hp,out var hq)) {validRay=false;return;}
        keyboardOwned=ComputerKeyboard.Process(p,ray.direction,primary,hp,hq,out var keyboardHit);
        if(keyboardOwned)
        {
            if(wasPressed){menuHit=false;Send(new Vector2(-10000,-10000),false);}
            if(primary)blockedUntilRelease=true;
            UpdateLaser(p,keyboardHit);return;
        }
        var inverse=Quaternion.Inverse(hq);
        bool hit=EyeScreenUI.RayToScreen(inverse*(p-hp),inverse*ray.direction,out var point,out var distance);
        lastPoint=hit?point:new Vector2(-10000,-10000);
        // Query the TV's existing native mesh/UV mapping once per frame. Feed
        // that position to its panel dispatcher; later native calls reuse it.
        menuHit=false;
        if(StereoTrial.MenuActive&&menuDocument!=null)
        {
            if(menuPanel==null||menuPanel.disposed)menuPanel=menuDocument.rootVisualElement?.panel?.TryCast<BaseRuntimePanel>();
            lastPoint=new Vector2(-10000,-10000);
            if(menuPanel!=null)
            {
                var mapped=menuPanel.ScreenToPanel(new Vector2(Screen.width*.5f,Screen.height*.5f));
                var layout=menuPanel.visualTree.layout;
                if(float.IsFinite(mapped.x)&&float.IsFinite(mapped.y)&&mapped.x>=0&&mapped.y>=0&&mapped.x<=layout.width&&mapped.y<=layout.height)
                {
                    menuHit=true;menuPoint=new Vector2(mapped.x,mapped.y);
                    lastPoint=new Vector2(mapped.x/layout.width*Screen.width,(1-mapped.y/layout.height)*Screen.height);
                }
            }
        }
        // Deliver to the existing arcade panel: native default mouse dispatch
        // can remain inactive after leaving a world-space machine document.
        var arcade=ArcadeMiniGameManager.LocalInstance;
        if(arcade!=null&&arcade.IsGameActive)
        {
            if(arcadeDocument==null||arcadeDocument.Pointer!=arcade.uiDocument.Pointer)
            {arcadeDocument=arcade.uiDocument;arcadePanel=null;blockedUntilRelease=primary;}
            if(arcadePanel==null||arcadePanel.disposed)arcadePanel=arcadeDocument.rootVisualElement?.panel?.TryCast<BaseRuntimePanel>();
            if(arcadePanel!=null)
            {
                var bounds=arcadePanel.visualTree.layout;
                menuHit=hit;menuPoint=new Vector2(point.x/Screen.width*bounds.width,(1-point.y/Screen.height)*bounds.height);
                SendMenu(hit&&primary&&!blockedUntilRelease,arcadePanel);
                UpdateLaser(p,p+ray.direction*(hit?distance:2));return;
            }
        }
        else {arcadeDocument=null;arcadePanel=null;}
        bool screenHit=Time.frameCount-surfaceFrame<=1;
        Send(lastPoint,(hit||screenHit||StereoTrial.MenuActive||CashMotion.PointerActive||ForgeryMotion.Active)&&primary&&!blockedUntilRelease);
        UpdateLaser(p,screenHit?surfacePoint:p+ray.direction*(hit?distance:2));
    }
    private static AnimationCurve laserWidth;
    private static void GameplayCursor()
    {
        if(laser!=null)laser.enabled=false;
        if(VrLocomotion.Aiming){DrawTeleport();return;}
        bool useLeft=DistanceGrips.AimLeft;
        if(!CursorVisible(useLeft)||!MotionInput.GameplayReady||!StereoTrial.GameplayActive||MotionInput.WheelOpen||RepairMotion.Active||
            MachineInteractionBase.IsLocalPlayerInMachineMode||!TryControllerRay(useLeft,out var aim))return;
        var owner=InteractionManager.LocalInstance;if(owner==null)return;
        // Reuse the native query for this hand. Never cast a second visual ray.
        var end=aim.GetPoint(owner.rayDistance);
        if(DistanceGrips.QueryMatches(useLeft)&&InteractionReticle.TryRawHit(out _,out var hit))end=hit;
        if(KitchenHands.DistancePoint(out var grabbed))end=grabbed;
        UpdateLaser(aim.origin,end,useLeft);
    }
    private static void UpdateLaser(Vector3 start,Vector3 end)=>UpdateLaser(start,end,PointerLeft);
    internal static void UpdateTeleportVisual(){if(VrLocomotion.Aiming)DrawTeleport();else if(laser!=null)laser.enabled=false;}
    internal static void DrawTeleport()=>UpdateLaser(VrLocomotion.AimRay.origin,VrLocomotion.AimEnd,!(VrSettings.Gamepad?.Value??false));
    private static void UpdateLaser(Vector3 start,Vector3 end,bool useLeft)
    {
        if(!CursorVisible(useLeft)&&!VrLocomotion.Aiming){if(laser!=null)laser.enabled=false;return;}
        if(laser==null)
        {
            if(shaderTried)return;shaderTried=true;
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null) return;
            var go=new GameObject("IKAG_RightPointer");UnityEngine.Object.DontDestroyOnLoad(go);
            laser=go.AddComponent<LineRenderer>();laser.positionCount=3;laser.useWorldSpace=true;
            laserWidth=new AnimationCurve(new Keyframe(0,.002f),new Keyframe(.99f,.002f),new Keyframe(1,.012f));
            laser.widthMultiplier=1;laser.numCapVertices=8;laserMaterial=new Material(shader);laser.material=laserMaterial;laserColor=Color.cyan;laserMaterial.color=laserColor;
            laser.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;laser.receiveShadows=false;
        }
        // Reuse the beam mesh: its final short segment and round cap form the endpoint dot.
        var colour=VrLocomotion.Aiming?(VrLocomotion.TargetValid?Color.green:Color.red):Color.cyan;
        if(colour!=laserColor){laserColor=colour;laserMaterial.color=colour;}
        float length=Vector3.Distance(start,end);float neck=Mathf.Clamp01(1-.006f/Mathf.Max(length,.006f));
        laserWidth.MoveKey(1,new Keyframe(Mathf.Clamp(neck,.001f,.9999f),.002f));laser.widthCurve=laserWidth;
        laser.enabled=true;laser.SetPosition(0,start);laser.SetPosition(1,Vector3.Lerp(start,end,neck));laser.SetPosition(2,end);
    }
    private static bool ScreenRay(RenderTextureScreenInput __instance,ref Ray worldRay,ref Vector3 hitOS,ref bool __result)
    {
#if IKAG_DIAGNOSTICS
        ++meshCalls;
#endif
        if(keyboardOwned){__result=false;return false;}
        if(!validRay)return true;
        worldRay=ray;
        if(meshFrame!=Time.frameCount||meshOwner!=__instance.Pointer)return true;
        hitOS=meshPoint;__result=meshHit;return false;
    }
    private static void SurfaceHit(RenderTextureScreenInput __instance,Vector3 hitOS,bool __result)
    {
        if(!validRay)return;
#if IKAG_DIAGNOSTICS
        if(__result)++meshHits;
#endif
        meshFrame=Time.frameCount;meshOwner=__instance.Pointer;meshPoint=hitOS;meshHit=__result;
        if(__result&&__instance.screenMesh!=null){surfaceFrame=Time.frameCount;surfacePoint=__instance.screenMesh.transform.TransformPoint(hitOS);}
    }
    private static void WorldPanelRay(ref Ray worldRay,ref bool __result)
    {
#if IKAG_DIAGNOSTICS
        ++worldRayCalls;
#endif
        if(keyboardOwned){__result=false;return;}
        if(!__result||!validRay||!MachineInteractionBase.IsLocalPlayerInMachineMode)return;
        var hud=HUDManager.LocalInstance;if(hud!=null&&hud.IsEscapeMenuOpen)return;
#if IKAG_DIAGNOSTICS
        ++worldRayOverrides;
#endif
        worldRay=ray;
    }
    private static void WorldPanelHit(Ray worldRay,UIDocument document,float distance,bool __result)
    {
#if IKAG_DIAGNOSTICS
        ++worldPickCalls;
        lastNativeRay=worldRay;
#endif
        if(validRay&&__result&&document!=null){surfaceFrame=Time.frameCount;surfacePoint=worldRay.GetPoint(distance);}
    }
    private static void Send(Vector2 position,bool pressed)
    {
        if(StereoTrial.MenuActive&&menuPanel!=null){SendMenu(pressed,menuPanel);return;}
        if(arcadePanel!=null&&!arcadePanel.disposed&&ArcadeMotion.Active){SendMenu(pressed,arcadePanel);return;}
        if(pressed&&!wasPressed&&validRay&&position.x>=0&&position.y>=0)MotionHaptics.Pulse(PointerLeft,.12f,.02f);
        if(mouse==null) mouse=InputSystem.AddDevice("Mouse","IKAG_VR_Pointer",null);
        // The game's background policy disables mice when its window loses focus.
        // Only this virtual device follows the separately validated XR focus gate.
        if(!mouse.enabled)InputSystem.EnableDevice(mouse);
        MouseEvent value=default;value.Type=0x53544154;value.Size=(ushort)sizeof(MouseEvent);value.Device=checked((ushort)mouse.deviceId);
        value.Time=InputRuntime.s_Instance.currentTime;value.Format=0x4D4F5553;value.Position=position;value.Buttons=(ushort)(pressed?1:0);
        bool cash=validRay&&CashMotion.Active;
        if(!cash||!VrControls.Off.Tracked)cashSecondaryArmed=false;
        else if(!VrControls.Off.Trigger)cashSecondaryArmed=true;
        if(cash&&cashSecondaryArmed&&VrControls.Off.Tracked&&VrControls.Off.Trigger)value.Buttons|=2;
        value.Scroll=new Vector2(0,validRay?scrollSteps*InputSystem.scrollWheelDeltaPerTick:0);
        InputEventPtr pointer=default;*(IntPtr*)&pointer=(IntPtr)(&value);
        InputSystem.QueueEvent(pointer);wasPressed=pressed;wasSecondary=(value.Buttons&2)!=0;
    }
    private static void SendMenu(bool pressed,BaseRuntimePanel panel)
    {
        // Deliver the already measured texture-panel position to that existing
        // panel. Its native dispatcher retains hover, widget capture and click
        // callbacks; no direct invocation of a menu button/action is involved.
        var point=validRay&&menuHit?menuPoint:new Vector2(-10000,-10000);
        var target=panel.Pick(point)??panel.visualTree;
        PointerDeviceState.SavePointerPosition(PointerId.mousePointerId,point,validRay&&menuHit?panel.Cast<IPanel>():null,ContextType.Player);
        lastMenuMove=PointerMoveEvent.GetPooled();
        try
        {
            lastMenuMove.pointerId=PointerId.mousePointerId;lastMenuMove.pointerType=PointerType.mouse;lastMenuMove.isPrimary=true;
            lastMenuMove.position=point;lastMenuMove.deltaPosition=point-previousMenuPoint;lastMenuMove.button=-1;lastMenuMove.pressedButtons=wasPressed?1:0;
            lastMenuMove.elementTarget=target;
            panel.SetTopElementUnderPointer(PointerId.mousePointerId,target,lastMenuMove);
            panel.SendEvent(lastMenuMove,DispatchMode.Immediate);
        }
        finally{lastMenuMove.Dispose();}
        if(pressed!=wasPressed)
        {
            if(pressed)
            {
                if(validRay&&menuHit&&target.enabledInHierarchy&&target.Pointer!=panel.visualTree.Pointer)MotionHaptics.Pulse(PointerLeft,.12f,.02f);
                PointerDeviceState.PressButton(PointerId.mousePointerId,0);
                lastMenuDown=PointerDownEvent.GetPooled();
                try
                {
                    lastMenuDown.pointerId=PointerId.mousePointerId;lastMenuDown.pointerType=PointerType.mouse;lastMenuDown.isPrimary=true;
                    lastMenuDown.position=point;lastMenuDown.button=0;lastMenuDown.pressedButtons=1;lastMenuDown.clickCount=1;lastMenuDown.elementTarget=target;
                    panel.SendEvent(lastMenuDown,DispatchMode.Immediate);
                }
                finally{lastMenuDown.Dispose();}
            }
            else
            {
                PointerDeviceState.ReleaseButton(PointerId.mousePointerId,0);
                lastMenuUp=PointerUpEvent.GetPooled();
                try
                {
                    lastMenuUp.pointerId=PointerId.mousePointerId;lastMenuUp.pointerType=PointerType.mouse;lastMenuUp.isPrimary=true;
                    lastMenuUp.position=point;lastMenuUp.button=0;lastMenuUp.pressedButtons=0;lastMenuUp.clickCount=1;lastMenuUp.elementTarget=target;
                    panel.SendEvent(lastMenuUp,DispatchMode.Immediate);
                }
                finally{lastMenuUp.Dispose();}
            }
        }
        if(validRay&&menuHit&&scrollSteps!=0)
        {
            lastMenuWheel=WheelEvent.GetPooled(new Vector3(0,-scrollSteps*WheelEvent.scrollDeltaPerTick,0),point,EventModifiers.None);
            try{lastMenuWheel.elementTarget=target;panel.SendEvent(lastMenuWheel,DispatchMode.Immediate);}
            finally{lastMenuWheel.Dispose();}
        }
        previousMenuPoint=point;wasPressed=pressed;
    }
#if IKAG_DIAGNOSTICS
    private static void PositionRoute(){++positionEvents;}
    private static void RayRoute(){++rayEvents;}
    internal static bool DiagnosticPressed=>wasPressed;
    internal static bool DiagnosticBeam=>laser!=null&&laser.enabled;
    internal static Ray MenuRay=>ray;
    internal static Vector2 MenuPoint=>menuPoint;
    internal static bool MenuHit=>menuHit;
    internal static void Trace()
    {
        Plugin.Line($"POINTER_CONTROL ray={validRay} pointer={lastPoint:R} pressed={wasPressed} virtual={(mouse==null?"none":mouse.name)} nativeMouse={(Mouse.current==null?"none":Mouse.current.name)} pos={(Mouse.current==null?Vector2.zero:Mouse.current.position.ReadValue())} runtimeTime={InputRuntime.s_Instance.currentTime:R} gameTime={Time.realtimeSinceStartupAsDouble:R} offset={InputRuntime.s_CurrentTimeOffsetToRealtimeSinceStartup:R} mouseSize={sizeof(MouseState)} enabled={(mouse!=null&&mouse.enabled)} appFocused={Application.isFocused}");
        Plugin.Line($"MENU_EVENT_ROUTE meshCalls={meshCalls} meshHits={meshHits}");
    }
    internal static string AimAt(Vector2 uiPoint)
    {
        var r=ControllerTransport.Right;
        if(!StereoTrial.TryHeadWorld(out var hp,out var hq)||!StereoTrial.TryTrackingPose(r.Position,r.Rotation,out var rp,out var rq))throw new InvalidOperationException("Aim control requires tracking");
        var target=hp+hq*EyeScreenUI.ScreenTarget(uiPoint);
        return AimAtWorld(target);
    }
    internal static string AimAtWorld(Vector3 target)
    =>AimAtWorld(target,false);
    internal static string AimAtWorld(Vector3 target,bool useLeft)
    {
        var r=useLeft?ControllerTransport.Left:ControllerTransport.Right;
        if(!StereoTrial.TryTrackingPose(r.Position,r.Rotation,out var rp,out var rq))throw new InvalidOperationException("Aim control requires tracking");
        var direction=Quaternion.Inverse(rq)*(target-rp);
        float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
        float pitch=Mathf.Atan2(direction.y,Mathf.Sqrt(direction.x*direction.x+direction.z*direction.z))*Mathf.Rad2Deg;
        return FormattableString.Invariant($"hand {(useLeft?"l":"r")} aimtrim {pitch:R} {-yaw:R}");
    }
    internal static void TraceWorldPick()
    {
        var system=UIElementsRuntimeUtility.defaultEventSystem;
        system.m_WorldSpacePicker.Pick(ray,system.worldSpaceMaxDistance,system.worldSpaceLayers,out var collider,out var document,out var element,out var distance);
        Plugin.Line($"WORLD_PICK_CONTROL rayCalls={worldRayCalls} overrides={worldRayOverrides} picks={worldPickCalls} positionEvents={positionEvents} rayEvents={rayEvents} forUI={system.m_UseInputForUI}/{system.m_IsInputForUIActive} useDefault={UIElementsRuntimeUtility.useDefaultEventSystem} nativeRay={lastNativeRay.origin:R}/{lastNativeRay.direction:R} directDoc={(document==null?"none":document.name)} directElement={(element==null?"none":element.name)} collider={(collider==null?"none":collider.name)} distance={distance:R} origin={ray.origin:R} direction={ray.direction:R} maxDistance={system.worldSpaceMaxDistance:R} mask={system.worldSpaceLayers:X}");
    }
#endif
}
