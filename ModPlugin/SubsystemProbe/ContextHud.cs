using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;

// Move the original prompt, progress, notifications and tutorial widgets.
// Native content, queues, fade lifetimes, voting and callbacks stay authoritative.
internal static class ContextHud
{
 private static HUDManager hud;private static InteractionPromptUITK prompt;
 private static HudDrawGroup promptGroup,alerts,notices,tutorialMask,tutorialCard,heldHint,machineHint;
 private static VisualElement tutorialStack;private static readonly List<VisualElement> labels=new(8);
 private static readonly ImmersiveHud.Surface objectSurface=new(),alertSurface=new(),noticeSurface=new(),popupSurface=new(),handSurface=new(),machineSurface=new();
 private static Transform target;private static Vector3 localAnchor,worldAnchor;private static bool anchorValid,placed,modal;
 private static Rect popupBounds;private static int frame=-1;
 internal static bool PointerMode=>ModalActive&&hud!=null&&!hud.IsEscapeMenuOpen;
 internal static BaseRuntimePanel PopupPanel=>tutorialStack?.panel?.TryCast<BaseRuntimePanel>();
 internal static bool ModalActive=>modal&&ImmersiveHud.Enabled&&ImmersiveHud.Tracked;
 internal static void Install()=>new Harmony("iknowaguyvr.context-hud").Patch(typeof(HUDManager).GetMethod("SpawnNotificationLabelNow"),postfix:new HarmonyMethod(typeof(ContextHud).GetMethod(nameof(NoticeAdded),BindingFlags.NonPublic|BindingFlags.Static)));
 internal static void Bind(HUDManager current)
 {hud=current;alerts=ImmersiveHud.RegisterGroup(hud.reputationNotifications);notices=ImmersiveHud.RegisterGroup(hud.notificationContainer);heldHint=ImmersiveHud.RegisterGroup(hud.cachedDropHintPanel);machineHint=ImmersiveHud.RegisterGroup(hud.cachedMachineControlsPanel);EnsureWidgets();CacheLabels();}
 internal static void Unbind(){hud=null;prompt=null;target=null;promptGroup=alerts=notices=tutorialMask=tutorialCard=heldHint=machineHint=null;tutorialStack=null;labels.Clear();anchorValid=placed=modal=false;frame=-1;}
 private static void EnsureWidgets()
 {
  if(alerts==null)alerts=ImmersiveHud.RegisterGroup(hud.reputationNotifications);
  if(notices==null)notices=ImmersiveHud.RegisterGroup(hud.notificationContainer);
  if(heldHint==null)heldHint=ImmersiveHud.RegisterGroup(hud.cachedDropHintPanel);
  if(machineHint==null)machineHint=ImmersiveHud.RegisterGroup(hud.cachedMachineControlsPanel);
  if(promptGroup==null){prompt=InteractionPromptUITK.LocalInstance;if(prompt!=null)promptGroup=ImmersiveHud.RegisterGroup(prompt.promptContainer);}
  if(tutorialCard==null&&hud.tutorialPopupImages?.parent?.parent!=null)
  {tutorialStack=hud.tutorialPopupImages.parent.parent;tutorialMask=ImmersiveHud.RegisterGroup(hud.tutorialPopupOverlay);tutorialCard=ImmersiveHud.RegisterGroup(tutorialStack);}
 }
 private static void NoticeAdded(HUDManager __instance){if(hud!=null&&hud.Pointer==__instance.Pointer)CacheLabels();}
 private static void CacheLabels()
 {labels.Clear();var container=hud?.notificationContainer;if(container==null)return;for(int i=0;i<container.childCount;i++)labels.Add(container.ElementAt(i));}
 internal static void Tick()
 {
  if(frame==Time.frameCount||hud==null)return;frame=Time.frameCount;EnsureWidgets();
  bool visible=tutorialStack!=null&&hud.tutorialPopupOverlay.resolvedStyle.display!=DisplayStyle.None&&tutorialStack.resolvedStyle.display!=DisplayStyle.None;
  modal=visible; if(!visible){placed=false;return;}
  popupBounds=tutorialStack.worldBound;
  var host=hud.popupHostContinueButton;
  if(hud.popupHostContinueShown&&host!=null&&host.worldBound.width>0){var b=host.worldBound;popupBounds=Rect.MinMaxRect(Math.Min(popupBounds.xMin,b.xMin),Math.Min(popupBounds.yMin,b.yMin),Math.Max(popupBounds.xMax,b.xMax),Math.Max(popupBounds.yMax,b.yMax));}
  if(!placed&&popupBounds.width>0&&popupBounds.height>0&&StereoTrial.TryHeadWorld(out var hp,out var hq))
  {popupSurface.Normal=hq*Vector3.forward;popupSurface.Up=hq*Vector3.up;popupSurface.Right=hq*Vector3.right;popupSurface.Center=hp+popupSurface.Normal*1.25f-popupSurface.Up*.12f;placed=true;}
  if(popupBounds.width>0&&popupBounds.height>0){float scale=Math.Min(.85f/popupBounds.width,.70f/popupBounds.height);popupSurface.Width=popupBounds.width*scale;popupSurface.Height=popupBounds.height*scale;}
 }
 internal static bool Pointer(out BaseRuntimePanel panel,out Vector2 point,out Vector3 world)
 {
  panel=null;point=default;world=default;
  if(!ModalActive||!placed||hud==null||hud.IsEscapeMenuOpen||!Probe.Focused||popupBounds.width<=0||!MotionPointer.TryControllerRay(out var ray)||!popupSurface.Hit(ray,out var uv,out world,out _))return false;
  panel=tutorialStack.panel?.TryCast<BaseRuntimePanel>();point=popupBounds.position+new Vector2(uv.x/1000*popupBounds.width,uv.y/800*popupBounds.height);return panel!=null;
 }
 private static void Face(ImmersiveHud.Surface surface,Vector3 point,Vector3 head,Quaternion rotation)
 {surface.Normal=(point-head).normalized;var up=Vector3.ProjectOnPlane(rotation*Vector3.up,surface.Normal);surface.Up=up.sqrMagnitude>.000001f?up.normalized:rotation*Vector3.up;surface.Right=Vector3.Cross(surface.Up,surface.Normal).normalized;}
 private static void Place(HudDrawGroup group,ImmersiveHud.Surface surface,Rect source,bool visible,Matrix4x4 inverse)
 {if(group==null)return;group.Visible=visible&&source.width>0&&source.height>0;group.HasProjectionBounds=true;group.ProjectionBounds=source;if(group.Visible)group.HeadFromUi=inverse*surface.Matrix(source);}
 private static Rect ContentBounds()
 {var b=prompt.promptText.worldBound;if(prompt.showingProgressBar&&prompt.progressBar!=null){var p=prompt.progressBar.worldBound;if(p.width>0&&p.height>0)b=Rect.MinMaxRect(Math.Min(b.xMin,p.xMin),Math.Min(b.yMin,p.yMin),Math.Max(b.xMax,p.xMax),Math.Max(b.yMax,p.yMax));}return b;}
 private static bool ResolveAnchor()
 {
  var manager=InteractionManager.LocalInstance;var next=manager!=null&&manager.activeHoldInteractable!=null?manager.activeHoldAnchor:null;if(next==null)next=manager?.closestInteractable;
  if(next==null){target=null;anchorValid=false;return false;}
  if(target==null||target.Pointer!=next.Pointer)
  {
   target=next;Vector3 point;
   if(InteractionReticle.TryRawHit(out var hit,out var raw)&&(hit.transform.Pointer==target.Pointer||hit.transform.IsChildOf(target)||target.IsChildOf(hit.transform)))point=raw;
   else {var collider=target.GetComponent<Collider>();if(collider!=null)point=collider.bounds.center;else{var renderer=target.GetComponentInChildren<Renderer>();point=renderer!=null?renderer.bounds.center:target.position;}}
   localAnchor=target.InverseTransformPoint(point);anchorValid=true;
  }
  worldAnchor=target.TransformPoint(localAnchor);return anchorValid;
 }
 internal static void Prepare(Matrix4x4 headWorld)
 {
  if(hud==null)return;Tick();var inverse=headWorld.inverse;var hp=(Vector3)headWorld.GetColumn(3);var hq=headWorld.rotation;
  bool live=ImmersiveHud.Enabled&&ImmersiveHud.Tracked&&Probe.Focused&&!hud.IsEscapeMenuOpen;
  if(tutorialMask!=null)tutorialMask.Visible=false;
  Place(tutorialCard,popupSurface,popupBounds,live&&modal&&placed,inverse);
  bool free=live&&!UnityEngine.Cursor.visible&&!modal&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&!ImmersiveHud.PointerOwned&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive;
  bool show=free&&prompt!=null&&prompt.promptText!=null&&(prompt.isShowing||prompt.showingProgressBar)&&ResolveAnchor();
  if(promptGroup!=null)
  {
   Rect source=prompt?.promptText==null?default:ContentBounds();float distance=(worldAnchor-hp).magnitude;
   if(show&&distance>.12f&&Vector3.Dot(worldAnchor-hp,hq*Vector3.forward)>0)
   {Face(objectSurface,worldAnchor,hp,hq);float scale=Math.Min(.0007f*distance,.60f/Math.Max(1,source.width));objectSurface.Width=source.width*scale;objectSurface.Height=source.height*scale;float side=Vector3.Dot(worldAnchor-hp,hq*Vector3.right)>=0?-1:1;objectSurface.Center=worldAnchor+objectSurface.Right*(side*(objectSurface.Width*.5f+.025f))+objectSurface.Up*(objectSurface.Height*.5f+.025f);}
   else show=false;
   Place(promptGroup,objectSurface,source,show,inverse);
  }
  if(alerts!=null){var b=alerts.Element.worldBound;alertSurface.Normal=hq*Vector3.forward;alertSurface.Up=hq*Vector3.up;alertSurface.Right=hq*Vector3.right;alertSurface.Center=hp+alertSurface.Normal*1.6f-alertSurface.Right*.78f+alertSurface.Up*.42f;float scale=Math.Min(.60f/Math.Max(1,b.width),.42f/Math.Max(1,b.height));alertSurface.Width=b.width*scale;alertSurface.Height=b.height*scale;Place(alerts,alertSurface,b,live,inverse);}
  if(notices!=null)
  {
   float width=0,height=0;for(int i=labels.Count-1;i>=0;i--){var l=labels[i];if(l.parent==null){labels.RemoveAt(i);continue;}width=Math.Max(width,l.layout.width);height=Math.Max(height,l.layout.height);}
   var center=notices.Element.worldBound.center;var b=new Rect(center.x-width*.5f,center.y-height*.5f,width,height);
   noticeSurface.Normal=hq*Vector3.forward;noticeSurface.Up=hq*Vector3.up;noticeSurface.Right=hq*Vector3.right;noticeSurface.Center=hp+noticeSurface.Normal*1.6f-noticeSurface.Right*.78f+noticeSurface.Up*.12f;
   noticeSurface.Width=.52f;noticeSurface.Height=width>0?.52f*height/width:0;Place(notices,noticeSurface,b,live&&labels.Count>0,inverse);
  }
  Vector3 rp=default;bool hand=live&&!modal&&StereoTrial.TryTrackingPose(VrControls.Main.Position,VrControls.Main.Rotation,out rp,out _);
  if(hand){handSurface.Normal=hq*Vector3.forward;handSurface.Up=hq*Vector3.up;handSurface.Right=hq*Vector3.right;handSurface.Center=rp+handSurface.Normal*.18f-handSurface.Up*.08f;}
  if(heldHint!=null){var b=heldHint.Element.worldBound;float scale=Math.Min(.00065f,.48f/Math.Max(1,b.width));handSurface.Width=b.width*scale;handSurface.Height=b.height*scale;Place(heldHint,handSurface,b,hand,inverse);}
  if(machineHint!=null)
  {
   var b=machineHint.Element.worldBound;bool attached=KitchenHands.DistancePoint(out var point)||KitchenHands.Pose(false,out point,out _)||KitchenHands.Pose(true,out point,out _);
   if(attached){Face(machineSurface,point,hp,hq);machineSurface.Width=Math.Min(.48f,(point-hp).magnitude*.30f);machineSurface.Height=b.width>0?machineSurface.Width*b.height/b.width:0;machineSurface.Center=point+machineSurface.Up*.12f;}else{machineSurface.Center=handSurface.Center;machineSurface.Normal=handSurface.Normal;machineSurface.Up=handSurface.Up;machineSurface.Right=handSurface.Right;machineSurface.Width=.42f;machineSurface.Height=b.width>0?.42f*b.height/b.width:0;}
   Place(machineHint,machineSurface,b,hand&&MachineInteractionBase.IsLocalPlayerInMachineMode,inverse);
  }
 }
#if IKAG_DIAGNOSTICS
 internal static void Trace(string tag){foreach(var g in new[]{promptGroup,alerts,notices,tutorialMask,tutorialCard})if(g!=null)Plugin.Line("CONTEXT_GROUP "+tag+" "+g.Element.name+" visible="+g.Visible+" applied="+g.Applied+" hidden="+g.Suppressed+" push="+g.Push+" bounds="+g.ProjectionBounds);Plugin.Line("CONTEXT_STATE "+tag+" target="+target?.name+" anchor="+worldAnchor+" caption="+objectSurface.Center+" labels="+labels.Count+" children="+hud?.notificationContainer?.childCount+" modal="+modal+" groups="+ImmersiveHud.BoundGroups);}
 internal static Vector3 Anchor=>worldAnchor;
 internal static Vector3 PromptCenter=>objectSurface.Center;
 internal static Vector3 PopupCenter=>popupSurface.Center;
 internal static bool PromptVisible=>promptGroup!=null&&promptGroup.Visible;
 internal static Vector3 ContinuePoint{get{var uv=hud.tutorialPopupContinue.worldBound.center-popupBounds.position;return popupSurface.Center+popupSurface.Right*((uv.x/popupBounds.width)-.5f)*popupSurface.Width+popupSurface.Up*(.5f-uv.y/popupBounds.height)*popupSurface.Height;}}
#endif
}
