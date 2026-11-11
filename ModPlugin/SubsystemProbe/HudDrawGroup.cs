using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;
namespace IKnowAGuyVR.SubsystemProbe;

// Reuse the accepted keyboard's native UIR group projection and depth contract.
// Each group keeps its original widget, draw chain, callbacks and layout.
internal sealed class HudDrawGroup
{
#if IKAG_DIAGNOSTICS
 internal int Applied,Suppressed;
#endif
 internal readonly VisualElement Element;
 internal Rect Bounds,ProjectionBounds;internal bool HasProjectionBounds;
 internal Matrix4x4 HeadFromUi;
 internal bool Visible,HideOnMonitor;
 private RenderData data;private RenderChainCommand first,last,push,pop;
 private Matrix4x4 oldProjection,oldGroup;private DrawParams parameters;
 private bool drawing,world;
 internal HudDrawGroup(VisualElement element,bool custom=false)
 {
  Element=element;HideOnMonitor=custom;
  var parent=element.parent;int index=parent.IndexOf(element);
  element.RemoveFromHierarchy();element.usageHints|=UsageHints.GroupTransform;parent.Insert(index,element);
 }
 internal IntPtr Push=>push?.Pointer??IntPtr.Zero;
 internal IntPtr Pop=>pop?.Pointer??IntPtr.Zero;
 internal bool Tree(BaseRuntimePanel panel)
 {
  if(Element.panel==null||Element.panel.Pointer!=panel.Pointer)return false;
  Bounds=Element.worldBound;bool changed=false;var current=Element.renderData;if(current==null)return false;
  if(data==null||data.Pointer!=current.Pointer){data=current;first=last=push=pop=null;changed=true;}
  if((data.firstHeadCommand?.Pointer??IntPtr.Zero)!=(first?.Pointer??IntPtr.Zero))
  {changed=true;first=data.firstHeadCommand;push=null;for(var c=first;c!=null&&c.owner.Pointer==data.Pointer;c=c.next)if(c.type==CommandType.PushView){push=c;break;}}
  if((data.firstTailCommand?.Pointer??IntPtr.Zero)!=(last?.Pointer??IntPtr.Zero))
  {changed=true;last=data.firstTailCommand;pop=null;for(var c=last;c!=null&&c.owner.Pointer==data.Pointer;c=c.next)if(c.type==CommandType.PopView){pop=c;break;}}
  return changed;
 }
 internal bool Ends(RenderChainCommand command)=>drawing&&pop!=null&&command.Pointer==pop.Pointer;
 internal bool Starts(RenderChainCommand command)=>push!=null&&command.Pointer==push.Pointer;
 internal void Begin(DrawParams drawParams,bool immersive)
 {
  if(!immersive&&!HideOnMonitor)return;
  oldProjection=Utility.GetUnityProjectionMatrix();parameters=drawParams;drawing=true;
  var bounds=HasProjectionBounds?ProjectionBounds:Bounds;Matrix4x4 projection=default;world=immersive&&Visible&&EyeScreenUI.WorldPanelProjection(HeadFromUi,bounds,out projection);
#if IKAG_DIAGNOSTICS
  if(world)Applied++;else if(immersive)Suppressed++;
#endif
  if(world){oldGroup=EyeScreenUI.GroupProjection(projection);GL.LoadProjectionMatrix(EyeScreenUI.WithOverlayDepth(projection,bounds));Utility.DisableScissor();}
  else
  {
   var hidden=oldProjection;hidden.m00=-2*hidden.m30;hidden.m01=-2*hidden.m31;hidden.m02=-2*hidden.m32;hidden.m03=-2*hidden.m33;
   hidden.m10=hidden.m00;hidden.m11=hidden.m01;hidden.m12=hidden.m02;hidden.m13=hidden.m03;GL.LoadProjectionMatrix(hidden);
  }
 }
 internal void End()
 {if(!drawing)return;drawing=false;GL.LoadProjectionMatrix(oldProjection);if(world){EyeScreenUI.GroupProjection(oldGroup);if(parameters!=null)EyeScreenUI.RestoreScissor(parameters.scissor.Peek());}parameters=null;}
}
