using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;
namespace IKnowAGuyVR.SubsystemProbe;

// Reuse the native dot and native interaction query. Only that dot's existing
// draw group receives a world-target projection; the rest of the HUD is untouched.
internal static class InteractionReticle
{
    private static HUDManager hud;
    private static VisualElement dot;
    private static RenderData data;
    private static RenderChainCommand push,pop,head,tail;
    private static IntPtr panelPointer;
    private static Vector3 point,headPoint,rawHitPoint;
    private static Collider hitCollider;private static int hitFrame=-10;
    private static bool sampled,active,inGroup,hideIdle;
    private static Matrix4x4 previousProjection;
    internal static void Record(InteractionManager source,bool controllerAim)
    {
        rawHitPoint=source.rayHit.point;sampled=controllerAim;hitFrame=Time.frameCount;hitCollider=controllerAim&&source.hasHit?source.rayHit.collider:null;
        if(controllerAim)point=source.hasHit?source.rayHit.point:source.interactionRay.GetPoint(source.rayDistance);
    }
    internal static bool TryHit(out Collider collider,out Vector3 position)
    {collider=hitCollider;position=point;return sampled&&Time.frameCount-hitFrame<=1&&collider!=null;}
    // Context captions need the native contact, independently of a displayed
    // dot temporarily redirected to a wrist control or grabbed handle.
    internal static bool TryRawHit(out Collider collider,out Vector3 position)
    {collider=hitCollider;position=rawHitPoint;return sampled&&Time.frameCount-hitFrame<=1&&collider!=null;}
    internal static void Prepare(Matrix4x4 headWorld)
    {
        hideIdle=MotionInput.Enabled&&StereoTrial.GameplayActive;
        if(!hideIdle){active=sampled=false;return;}
        bool repairPointer=RepairMotion.Pointer(out var repairPoint);if(repairPointer)point=repairPoint;
        bool hudPointer=ImmersiveHud.PointerWorld(out var hudPoint);
        bool distanceGrab=KitchenHands.DistancePoint(out var grabbedPoint);if(distanceGrab)point=grabbedPoint;if(hudPointer)point=hudPoint;
        bool tracked=hudPointer?MotionPointer.PointerTracked:(distanceGrab?(KitchenHands.RemoteLeft?ControllerTransport.Left:ControllerTransport.Right):(DistanceGrips.AimLeft?ControllerTransport.Left:ControllerTransport.Right)).Tracked;
        active=(repairPointer||distanceGrab||hudPointer||(sampled&&(MotionInput.PhotoMode||MotionInput.PlacementPreviewActive)))&&Probe.Focused&&tracked&&!UnityEngine.Cursor.visible&&!MotionInput.WheelOpen&&(!MachineInteractionBase.IsLocalPlayerInMachineMode||distanceGrab);
        bool useLeft=hudPointer?MotionPointer.PointerLeft:distanceGrab?KitchenHands.RemoteLeft:DistanceGrips.AimLeft;
        active&=MotionPointer.CursorVisible(useLeft);
        if(active){headPoint=headWorld.inverse.MultiplyPoint3x4(point);if(repairPointer&&headPoint.z<=.01f)active=false;}
        var current=HUDManager.LocalInstance;
        if(current==null){hud=null;dot=null;data=null;push=pop=head=tail=null;sampled=false;return;}
        if(hud!=null&&hud.Pointer==current.Pointer&&dot!=null)return;
        var candidate=current.crosshairDot;
        if(candidate==null||candidate.parent==null)return;
        hud=current;dot=candidate;data=null;push=pop=head=tail=null;panelPointer=dot.panel?.Pointer??IntPtr.Zero;
        // Usage hints are immutable while attached. Preserve the exact element,
        // parent and sibling index; add no container, mesh, material or UI surface.
        var parent=dot.parent;int index=parent.IndexOf(dot);
        dot.RemoveFromHierarchy();dot.usageHints|=UsageHints.GroupTransform;parent.Insert(index,dot);
        Plugin.Line("INTERACTION_RETICLE_BOUND native dot, controller ray result");
    }
    internal static void Tree(BaseRuntimePanel panel)
    {
        if(dot==null||panelPointer!=panel.Pointer)return;
        var current=dot.renderData;
        if(current==null)return;
        if(data==null||current.Pointer!=data.Pointer){data=current;head=tail=push=pop=null;}
        var first=data.firstHeadCommand;var last=data.firstTailCommand;
        if((first?.Pointer??IntPtr.Zero)!=(head?.Pointer??IntPtr.Zero))
        {
            head=first;push=null;
            for(var command=first;command!=null&&command.owner.Pointer==data.Pointer;command=command.next)
                if(command.type==CommandType.PushView){push=command;break;}
        }
        if((last?.Pointer??IntPtr.Zero)!=(tail?.Pointer??IntPtr.Zero))
        {
            tail=last;pop=null;
            for(var command=last;command!=null&&command.owner.Pointer==data.Pointer;command=command.next)
                if(command.type==CommandType.PopView){pop=command;break;}
        }
    }
    internal static void Command(IntPtr command)
    {
        if(pop!=null&&command==pop.Pointer&&inGroup){GL.LoadProjectionMatrix(previousProjection);inGroup=false;return;}
        if(push==null||command!=push.Pointer||(!active&&!hideIdle))return;
        previousProjection=Utility.GetUnityProjectionMatrix();
        Matrix4x4 projection=default;
        if(active&&EyeScreenUI.ReticleProjection(headPoint,dot.worldBound,out projection))
        {GL.LoadProjectionMatrix(EyeScreenUI.WithOverlayDepth(projection,dot.worldBound));Utility.DisableScissor();}
        else
        {
            // Same clip-space suppression used by HudDrawGroup. Change only the
            // existing dot's eye draw; monitor widgets/styles remain untouched.
            var hidden=previousProjection;hidden.m00=-2*hidden.m30;hidden.m01=-2*hidden.m31;hidden.m02=-2*hidden.m32;hidden.m03=-2*hidden.m33;
            hidden.m10=hidden.m00;hidden.m11=hidden.m01;hidden.m12=hidden.m02;hidden.m13=hidden.m03;GL.LoadProjectionMatrix(hidden);
        }
        inGroup=true;
#if IKAG_DIAGNOSTICS
        if(active)PlacementReticleAudit.Projected(point,headPoint,dot.worldBound.center,projection);
#endif
    }
    internal static void EndDraw(){if(inGroup){GL.LoadProjectionMatrix(previousProjection);inGroup=false;}}
#if IKAG_DIAGNOSTICS
    internal static bool Visible=>active;
#endif
}
