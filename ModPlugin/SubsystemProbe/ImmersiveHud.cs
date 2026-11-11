using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class ImmersiveHud
{
 internal sealed class Surface
 {
  internal Vector3 Center,Right,Up,Normal;internal float Width,Height;
  internal Matrix4x4 Matrix(Rect source)
  {var x=Right*(Width/source.width);var y=Up*(-Height/source.height);var t=Center-x*source.center.x-y*source.center.y;var m=Matrix4x4.zero;m.m00=x.x;m.m10=x.y;m.m20=x.z;m.m01=y.x;m.m11=y.y;m.m21=y.z;m.m03=t.x;m.m13=t.y;m.m23=t.z;m.m33=1;return m;}
  internal bool Hit(Ray ray,out Vector2 point,out Vector3 world,out float distance)
  {point=default;world=default;distance=0;float d=Vector3.Dot(ray.direction,Normal);if(d<=.0001f)return false;distance=Vector3.Dot(Center-ray.origin,Normal)/d;if(distance<=.02f||distance>4)return false;world=ray.GetPoint(distance);var v=world-Center;point=new Vector2((Vector3.Dot(v,Right)/Width+.5f)*1000,(.5f-Vector3.Dot(v,Up)/Height)*800);return point.x>=0&&point.x<=1000&&point.y>=0&&point.y<=800;}
 }
 private static ConfigEntry<bool> setting;
 private static HUDManager hud;private static BaseRuntimePanel panel;
 private static HudDrawGroup left,time,economy,money,debt,reputation,orders,inventory,hints,wristFrame,boardFrame;
 private static readonly List<HudDrawGroup> groups=new(12),stack=new(12);
 private static readonly Surface wrist=new(),board=new(),hotbar=new();
 private static readonly Dictionary<IntPtr,List<HudDrawGroup>> panelGroups=new();
 private static readonly Dictionary<IntPtr,HudDrawGroup> starts=new(16),ends=new(16);
 private static Label boardLabel;
 private static int tickFrame=-1,slot=-1;private static float wristSince=-1,hotbarUntil;
 private static bool wristOpen,boardPlaced,boardPinned=true,previousTrigger,previousInputLeft,inputArmed,latched,dragging,failed;
 private static Vector3 dragCenter,dragOffset;
 private static bool pointer,nativePointer;private static Vector2 pointerPosition;private static Vector3 pointerWorld;
 private static bool boardPointer,boardLatched,boardStickLatched;
 private static float pointerDistance;
 private static readonly Rect Whole=new(0,0,1000,800),LeftSlot=new(35,210,440,480),OrderSlot=new(525,270,440,420);
 private static readonly Rect MapButtonBounds=new(780,20,190,70),RecallButtonBounds=new(350,710,300,65);
 private static bool MapOpen=>UI.NavigationManager.LocalInstance?.IsMapOpen??false;
 internal static bool Enabled=>MotionInput.Enabled&&setting!=null&&setting.Value&&!failed;
 internal static bool Tracked=>ControllerTransport.Left.Tracked&&ControllerTransport.Right.Tracked;
 internal static bool InputOwned{get{Tick();return pointer||latched||ContextHud.ModalActive;}}
 internal static bool PointerOwned=>pointer||latched;
 internal static bool PointerLatched=>latched||dragging;
 internal static bool BoardInputOwned{get{Tick();return (boardPointer||boardLatched||dragging)&&(!pointer||boardPointer);}}
 internal static bool BoardOwnsStick{get{Tick();return dragging||boardStickLatched;}}
 internal static bool WristOpen=>wristOpen;
 internal static void Initialize(ConfigFile config)=>setting=config.Bind("Interface","ImmersiveHUD",true,"Wrist dashboard, journal and movable world orders board. Classic HUD remains available in the pause menu.");
 private static Label Label(VisualElement parent,string text,float x,float y,float width,float height,int size=32)
 {var l=new Label(text){pickingMode=PickingMode.Ignore};var s=l.style;s.position=Position.Absolute;s.left=x;s.top=y;s.width=width;s.height=height;s.fontSize=size;s.color=new Color(.93f,.97f,1);s.unityTextAlign=TextAnchor.MiddleCenter;parent.Add(l);return l;}
 private static VisualElement Frame(string name)
 {var root=new VisualElement{name=name,pickingMode=PickingMode.Ignore};var s=root.style;s.position=Position.Absolute;s.left=0;s.top=0;s.width=1000;s.height=800;s.backgroundColor=new Color(.025f,.06f,.10f,.96f);s.overflow=Overflow.Visible;s.borderTopLeftRadius=s.borderTopRightRadius=s.borderBottomLeftRadius=s.borderBottomRightRadius=24;s.unityFontDefinition=hud.currentTimeLabel.resolvedStyle.unityFontDefinition;hud.root.Insert(0,root);return root;}
 private static void Bind(HUDManager current)
 {
  EndDraw();if(wristFrame!=null)wristFrame.Element.RemoveFromHierarchy();if(boardFrame!=null)boardFrame.Element.RemoveFromHierarchy();
  hud=current;ContextHud.Unbind();groups.Clear();panelGroups.Clear();stack.Clear();starts.Clear();ends.Clear();panel=hud.root.panel?.TryCast<BaseRuntimePanel>();
  left=new HudDrawGroup(hud.leftColumn);time=new HudDrawGroup(hud.currentTimeLabel.parent);economy=new HudDrawGroup(hud.currentMoneyLabel.parent.parent.parent);money=new HudDrawGroup(hud.currentMoneyLabel.parent);debt=new HudDrawGroup(hud.debtRow);reputation=new HudDrawGroup(hud.reputationMeter);orders=new HudDrawGroup(hud.ordersPanel);inventory=new HudDrawGroup(hud.cachedInventoryPanel);hints=new HudDrawGroup(hud.cachedEmoteHintPanel);
  var wf=Frame("IKAG_WristDashboard");Label(wf,"JOURNAL / ORDERS",30,15,700,80,38);
  var mapButton=Label(wf,"MAP",MapButtonBounds.x,MapButtonBounds.y,MapButtonBounds.width,MapButtonBounds.height);
  mapButton.style.backgroundColor=new Color(.08f,.16f,.23f,.96f);
  var recallButton=Label(wf,"RECALL ORDERS",RecallButtonBounds.x,RecallButtonBounds.y,RecallButtonBounds.width,RecallButtonBounds.height,28);
  recallButton.style.backgroundColor=new Color(.08f,.16f,.23f,.96f);
  wristFrame=new HudDrawGroup(wf,true);
  var bf=Frame("IKAG_OrdersBoard");boardLabel=Label(bf,"ORDERS - HOLD GRIP HERE TO MOVE",25,15,790,85,30);Label(bf,"X",865,15,110,85,38);boardFrame=new HudDrawGroup(bf,true);
  AddGroup(left);AddGroup(time);AddGroup(economy);AddGroup(money);AddGroup(debt);AddGroup(reputation);AddGroup(orders);AddGroup(inventory);AddGroup(hints);AddGroup(wristFrame);AddGroup(boardFrame);
  wristOpen=boardPlaced=dragging=latched=inputArmed=boardPointer=boardLatched=boardStickLatched=false;slot=-1;wristSince=-1;boardPinned=true;
  ContextHud.Bind(hud);
  VrPrompts.Bind(hud);
  Plugin.Line("IMMERSIVE_HUD_BOUND native status, quests, orders and inventory");
 }
 internal static bool ConfiguredImmersive=>setting!=null&&setting.Value;
 internal static void SetEnabled(bool value){setting.Value=value;wristOpen=dragging=latched=false;wristSince=-1;}
 internal static bool Back()
 {
  // The native map polls keyboard close input instead of OnCancelPressed.
  // Keep B's existing Back route and let native methods restore cursor/ownership.
  var map=UI.NavigationManager.LocalInstance;
  if(map!=null&&map.IsMapOpen&&hud!=null&&!hud.IsEscapeMenuOpen)
  {if(!map.CloseResetConfirmIfOpen())map.CloseMap();return true;}
  if(ContextHud.ModalActive||!InputOwned)return false;wristOpen=false;wristSince=-1;dragging=false;return true;
 }
 internal static void Tick()
 {
  if(tickFrame==Time.frameCount)return;tickFrame=Time.frameCount;pointer=nativePointer=boardPointer=false;
  if(VrControls.Turn.Tracked&&VrControls.Turn.Stick.sqrMagnitude<=.04f)boardStickLatched=false;
  PhysicalBelt.Hide();
  try
  {
   var current=HUDManager.LocalInstance;
   if(current==null||!StereoTrial.GameplayActive){
    BeltModels.Clear();
    if(groups.Count>0){EndDraw();ContextHud.Unbind();groups.Clear();panelGroups.Clear();starts.Clear();ends.Clear();panel=null;hud=null;if(wristFrame!=null)wristFrame.Element.style.display=DisplayStyle.None;if(boardFrame!=null)boardFrame.Element.style.display=DisplayStyle.None;}
    wristOpen=dragging=latched=boardLatched=boardStickLatched=false;return;}
   if(current.root==null||current.leftColumn==null||current.currentTimeLabel==null||current.currentMoneyLabel==null||current.debtRow==null||current.reputationMeter==null||current.ordersPanel==null||current.cachedInventoryPanel==null||current.cachedEmoteHintPanel==null||current.escapeMenuButtons==null)return;
   if(hud==null||current.Pointer!=hud.Pointer||wristFrame?.Element.panel==null)Bind(current);
   ContextHud.Tick();
   bool live=Enabled&&Tracked&&Probe.Focused&&!hud.IsEscapeMenuOpen&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&!ArcadeMotion.Active&&!SpyMotion.Active&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive;
   ControllerTransport.Sample();var l=VrControls.Off;var r=VrControls.MainInput;bool click=MotionPointer.PrimaryHeld;
   bool inputLeft=MotionPointer.PointerLeft;if(inputLeft!=previousInputLeft){previousTrigger=false;previousInputLeft=inputLeft;}
   if(!live||MapOpen||ContextHud.ModalActive||!MotionPointer.PointerTracked||!StereoTrial.TryHeadWorld(out var hp,out var hq)){wristOpen=dragging=latched=inputArmed=boardLatched=false;wristSince=-1;previousTrigger=click;return;}
   if(!boardPlaced)PlaceBoard(hp,hq);
   bool gesture=false;
   if(l.Tracked&&StereoTrial.TryTrackingPose(l.Position,l.Rotation,out var p,out var q))
   {
    var toHead=hp-p;float distance=toHead.magnitude;var back=q*(VrControls.LeftHanded?Vector3.right:Vector3.left);
    gesture=distance>.18f&&distance<.85f&&Vector3.Dot(back,toHead.normalized)>(wristOpen?.45f:.7f)&&Vector3.Dot(hq*Vector3.forward,-toHead.normalized)>(wristOpen?.6f:.8f);
    wrist.Normal=-back;
    // Keep the attachment on the anatomical wrist, but read text relative to
    // the viewer. Hand-up reverses when the same back-of-wrist face is raised
    // with the fingers pointing the other way.
    var readingUp=Vector3.ProjectOnPlane(hq*Vector3.up,wrist.Normal);
    if(readingUp.sqrMagnitude<.000001f)gesture=false;
    else{wrist.Up=readingUp.normalized;wrist.Right=Vector3.Cross(wrist.Up,wrist.Normal).normalized;}
    float wristScale=VrSettings.WristScale.Value*WristDisplaySettings.ScalePerPercent;
    wrist.Center=p-(q*Vector3.up)*.08f+back*.025f;wrist.Width=.60f*wristScale;wrist.Height=wrist.Width*.8f;wrist.Center+=wrist.Right*((wrist.Width-.38f*wristScale)*.5f*(VrControls.LeftHanded?-1:1))+wrist.Up*((wrist.Height-.304f*wristScale)*.5f);
   }
   if(gesture){if(wristSince<0)wristSince=Time.unscaledTime;wristOpen=Time.unscaledTime-wristSince>=.2f;}else{wristSince=-1;wristOpen=false;}
   var inv=InventoryManager.LocalInstance;int selected=inv==null?-1:inv.currentSelectedSlot;if(selected!=slot){slot=selected;hotbarUntil=Time.unscaledTime+1.6f;}
   if(PhysicalBelt.Enabled)PhysicalBelt.Update(inv,hp,hq);
   else if(StereoTrial.TryTrackingPose(VrControls.Main.Position,VrControls.Main.Rotation,out var rp,out _))
   {hotbar.Center=rp+hq*new Vector3(0,.12f,.16f);hotbar.Normal=hq*Vector3.forward;hotbar.Up=hq*Vector3.up;hotbar.Right=hq*Vector3.right;hotbar.Width=.42f;hotbar.Height=.046f;}
   if(!inputArmed){inputArmed=!click;previousTrigger=click;}
   if(wristOpen)dragging=false;
   if(!click){latched=false;dragging=false;boardLatched=false;}
   if(MotionPointer.TryControllerRay(out var ray))
   {
    Surface selectedSurface=null;Vector2 logical=default;pointerDistance=float.PositiveInfinity;
    if(wristOpen&&wrist.Hit(ray,out var wp,out var wh,out var wd)){selectedSurface=wrist;logical=wp;pointerWorld=wh;pointerDistance=wd;}
    if(boardPinned&&!(wristOpen)&&board.Hit(ray,out var bp,out var bh,out var bd)&&bd<pointerDistance){selectedSurface=board;logical=bp;pointerWorld=bh;pointerDistance=bd;boardPointer=true;}
    if(dragging)
    {
     var stick=MotionInput.ControlsArmed&&UnityEngine.Cursor.lockState==CursorLockMode.Locked?MotionInput.Deadzone(r.Stick):Vector2.zero;
     if(stick.sqrMagnitude>0)
     {
      boardStickLatched=true;
      var yaw=Quaternion.AngleAxis(stick.x*60*Time.unscaledDeltaTime,Vector3.up);
      board.Normal=yaw*board.Normal;board.Right=yaw*board.Right;dragOffset=yaw*dragOffset;
     }
     float d=Vector3.Dot(ray.direction,board.Normal);
     if(d>.0001f)
     {
      float travel=Vector3.Dot(dragCenter-ray.origin,board.Normal)/d;
      if(stick.y!=0)travel=Mathf.Clamp(travel+stick.y*.6f*Time.unscaledDeltaTime,.45f,3.5f);
      if(travel>0&&travel<4)
      {
       pointerWorld=ray.GetPoint(travel);board.Center=pointerWorld+dragOffset;pointer=boardPointer=true;
       // Keep the existing grab plane at the adjusted depth. Release leaves
       // this same surface pinned at its final position and orientation.
       dragCenter=pointerWorld;
      }
     }
    }
    else if(selectedSurface!=null)
    {
     pointer=true;bool rising=inputArmed&&click&&!previousTrigger;
     if(selectedSurface==wrist)
     {
      if(MapButtonBounds.Contains(logical)){if(rising){MotionPointer.ConsumePrimaryUntilRelease();UI.NavigationManager.LocalInstance?.OpenMap();}}
      else if(RecallButtonBounds.Contains(logical)){if(rising){boardPinned=true;PlaceBoard(hp,hq);}}
      else{if(logical.x<500)NativePoint(left,LeftSlot,wrist,logical);else NativePoint(orders,OrderSlot,wrist,logical);}
     }
     else if(logical.y<110)
     {if(rising){if(logical.x>850)boardPinned=false;else{dragging=true;dragCenter=board.Center;dragOffset=board.Center-pointerWorld;}}}
     else NativePoint(orders,new Rect(35,120,930,640),board,logical);
     if(rising&&nativePointer&&WristSections.Click(hud,selectedSurface==wrist&&logical.x<500,pointerPosition))nativePointer=false;
     if(rising){latched=true;boardLatched=selectedSurface==board;if(!nativePointer)MotionHaptics.Pulse(MotionPointer.PointerLeft,.12f,.02f);}
    }
   }
   previousTrigger=click;
  }
  catch(Exception e){failed=true;wristOpen=pointer=latched=false;Plugin.Line("IMMERSIVE_HUD_DISABLED "+e);}
 }
 private static void PlaceBoard(Vector3 hp,Quaternion hq)
 {var forward=hq*Vector3.forward;forward.y=0;if(forward.sqrMagnitude<.0001f)forward=Vector3.forward;forward.Normalize();board.Normal=forward;board.Up=Vector3.up;board.Right=Vector3.Cross(board.Up,forward);board.Center=hp+forward*1.15f+board.Right*.8f-Vector3.up*.25f;board.Width=.75f;board.Height=.60f;boardPlaced=true;}
 private static Rect Fit(Rect source,Rect slot)
 {float scale=Math.Min(slot.width/Math.Max(1,source.width),slot.height/Math.Max(1,source.height));var size=source.size*scale;return new Rect(slot.center-size*.5f,size);}
 private static void NativePoint(HudDrawGroup group,Rect slot,Surface surface,Vector2 logical)
 {var b=group.Element.worldBound;var fitted=Fit(b,slot);if(!fitted.Contains(logical))return;pointerPosition=b.position+Vector2.Scale((logical-fitted.position),new Vector2(b.width/fitted.width,b.height/fitted.height));nativePointer=true;}
 internal static bool Pointer(out BaseRuntimePanel target,out Vector2 point,out Vector3 hit,out bool native)
 {Tick();target=panel;point=pointerPosition;hit=pointerWorld;native=nativePointer;return pointer&&panel!=null&&!panel.disposed;}
 internal static bool PointerWorld(out Vector3 point){Tick();point=pointerWorld;return pointer;}
 internal static void Prepare(Matrix4x4 head)
 {
  Tick();if(hud==null)return;ContextHud.Prepare(head);var inverse=head.inverse;bool live=Enabled&&Tracked&&!MapOpen&&!ContextHud.ModalActive&&StereoTrial.GameplayActive&&Probe.Focused&&!hud.IsEscapeMenuOpen&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&!ArcadeMotion.Active&&!SpyMotion.Active&&!MotionInput.ConversationActive;
  Configure(wristFrame,wrist,Whole,live&&wristOpen,inverse);Configure(boardFrame,board,Whole,live&&boardPinned&&boardPlaced&&!(wristOpen),inverse);
  // Keep native formatting, debt visibility and reputation updates. Suppress the
  // old wrapper while its existing child widgets share the compact status strip.
  economy.Visible=false;
  Configure(time,wrist,new Rect(40,115,160,60),live&&wristOpen,inverse);
  Configure(money,wrist,new Rect(225,110,390,65),live&&wristOpen,inverse);
  Configure(debt,wrist,new Rect(650,110,310,65),live&&wristOpen,inverse);
  Configure(reputation,wrist,new Rect(40,180,920,75),live&&wristOpen,inverse);
  Configure(left,wrist,LeftSlot,live&&wristOpen,inverse);
  bool useBoard=boardPinned&&!(wristOpen);
  Configure(orders,useBoard?board:wrist,useBoard?new Rect(35,120,930,640):OrderSlot,live&&(useBoard||wristOpen),inverse);
  hints.Visible=false;
  if(PhysicalBelt.Enabled)inventory.Visible=false;
  else{var ib=inventory.Element.worldBound;inventory.Visible=live&&ib.width>0&&ib.height>0&&Time.unscaledTime<hotbarUntil;if(inventory.Visible)inventory.HeadFromUi=inverse*hotbar.Matrix(ib);}
 }
 private static void Configure(HudDrawGroup group,Surface surface,Rect slot,bool visible,Matrix4x4 inverse)
 {if(group==null)return;var source=group.Element.worldBound;group.Visible=visible&&source.width>0&&source.height>0;if(!group.Visible)return;var fit=Fit(source,slot);var frame=surface.Matrix(Whole);var map=Matrix4x4.TRS(new Vector3(fit.x-source.x*fit.width/source.width,fit.y-source.y*fit.height/source.height,0),Quaternion.identity,new Vector3(fit.width/source.width,fit.height/source.height,1));group.HeadFromUi=inverse*frame*map;}
 private static void AddGroup(HudDrawGroup group)
 {groups.Add(group);var key=group.Element.panel?.Pointer??IntPtr.Zero;if(!panelGroups.TryGetValue(key,out var batch)){batch=new List<HudDrawGroup>(12);panelGroups.Add(key,batch);}batch.Add(group);}
 internal static HudDrawGroup RegisterGroup(VisualElement element)
 {if(element==null||element.parent==null||element.panel==null)return null;foreach(var g in groups)if(g.Element.Pointer==element.Pointer)return g;var added=new HudDrawGroup(element);AddGroup(added);return added;}
 internal static void Tree(BaseRuntimePanel target)
 {if(hud==null||!panelGroups.TryGetValue(target.Pointer,out var batch))return;bool changed=false;foreach(var g in batch)changed|=g.Tree(target);if(!changed)return;starts.Clear();ends.Clear();foreach(var g in groups){if(g.Push!=IntPtr.Zero)starts[g.Push]=g;if(g.Pop!=IntPtr.Zero)ends[g.Pop]=g;}}
    internal static void Command(IntPtr command,IntPtr parameters)
 {
  var key=command;
  if(ends.TryGetValue(key,out var ending)){ending.End();for(int i=stack.Count-1;i>=0;i--)if(stack[i]==ending){stack.RemoveAt(i);break;}}
  if(starts.TryGetValue(key,out var starting)){bool immersive=Enabled&&Tracked&&EyeScreenUI.InEye&&StereoTrial.GameplayActive;starting.Begin(UiCommandBridge.Parameters(parameters),immersive);stack.Add(starting);}
 }
 internal static void EndDraw(){for(int i=stack.Count-1;i>=0;i--)stack[i].End();stack.Clear();}
#if IKAG_DIAGNOSTICS
 internal static int InventoryDraws=>inventory?.Applied??0;
 internal static float WristUprightDot=>StereoTrial.TryHeadWorld(out _,out var rotation)?Vector3.Dot(wrist.Up,rotation*Vector3.up):0;
 internal static Vector3 WristCenter=>wrist.Center;
 internal static Vector3 BoardCenter=>board.Center;
 internal static Vector3 WristRecallButton=>wrist.Matrix(Whole).MultiplyPoint3x4(RecallButtonBounds.center);
 internal static Vector3 WristMapButton=>wrist.Matrix(Whole).MultiplyPoint3x4(MapButtonBounds.center);
 internal static Vector3 BoardTitle=>board.Center+board.Up*(.42f*board.Height);
 internal static Vector3 BoardClose=>BoardTitle+board.Right*(.42f*board.Width);
 internal static bool DashboardVisible=>wristOpen&&time.Visible&&money.Visible&&reputation.Visible&&left.Visible&&orders.Visible;
 internal static int BoundGroups=>groups.Count;
 internal static bool UsingImmersive=>Enabled&&Tracked;
 internal static bool BoardPinned=>boardPinned;
 internal static void DiagnosticToggle()=>SetEnabled(!Enabled);
#endif
}
