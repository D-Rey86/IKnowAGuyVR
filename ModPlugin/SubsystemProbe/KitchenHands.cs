using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class KitchenHands
{
    private sealed class Handle
    {
        internal MachineInteractionBase Machine;internal MonoBehaviour Component;internal Transform Transform,Parent;internal Collider[] Colliders,MachineColliders;
        internal PressHandle Press;internal SlicerHandle Slicer;internal SauceHandle Sauce;internal OvenHandle Oven;internal OvenLevel2Handle Oven2;
        internal bool Enabled=>Press!=null?Press.isEnabled:Slicer!=null?Slicer.isEnabled:Sauce!=null?Sauce.isEnabled:Oven!=null?Oven.isEnabled:Oven2!=null&&Oven2.isEnabled;
        internal float Value=>Press!=null?Press.currentY:Slicer!=null?Slicer.currentY:Sauce!=null?Sauce.currentRotationX:Oven!=null?Oven.currentLocalRotationY:Oven2.currentX;
        internal Vector3 Axis=>Oven2!=null?Vector3.right:Sauce!=null?Vector3.right:Oven!=null?AxisOf(Rotary?Oven.rotationAxis:Oven.slideAxis):Vector3.up;
        internal bool Rotary=>Sauce!=null||(Oven!=null&&Oven.handleMotion==HandleMotion.Rotate);
        internal bool Busy=>Press!=null?(Press.isAutoCompleting||Press.isResetting):Sauce!=null?Sauce.isAutoCompleting:Oven!=null?Oven.isResetting:Oven2!=null&&Oven2.isResetting;
        internal void Target(float value)
        {
            if(Press!=null)Press.targetY=Mathf.Clamp(value,Mathf.Min(Press.topLocalY,Press.bottomLocalY),Mathf.Max(Press.topLocalY,Press.bottomLocalY));
            else if(Slicer!=null)Slicer.targetY=Mathf.Clamp(value,Mathf.Min(Slicer.topLocalY,Slicer.bottomLocalY),Mathf.Max(Slicer.topLocalY,Slicer.bottomLocalY));
            else if(Sauce!=null)Sauce.targetRotationX=Mathf.Clamp(value,Mathf.Min(Sauce.startRotationX,Sauce.endRotationX),Mathf.Max(Sauce.startRotationX,Sauce.endRotationX));
            else if(Oven2!=null)Oven2.targetX=Mathf.Clamp(value,Mathf.Min(Oven2.openLocalX,Oven2.closedLocalX),Mathf.Max(Oven2.openLocalX,Oven2.closedLocalX));
            else if(Oven!=null){float a=Oven.minLocalRotationY,b=Oven.maxLocalRotationY;Oven.targetLocalRotationY=Mathf.Clamp(value,Mathf.Min(a,b),Mathf.Max(a,b));}
        }
        internal void Grab()
        {
            if(Press!=null){Press.isDragging=true;Press.hasTriggeredPress=false;Press.targetY=Press.currentY;Press.UpdateMaterial();Press.OnPressStarted?.Invoke();}
            else if(Slicer!=null){Slicer.isDragging=true;Slicer.hasTriggeredSlice=false;Slicer.targetY=Slicer.currentY;Slicer.UpdateMaterial();Slicer.OnSliceStarted?.Invoke();}
            else if(Sauce!=null){Sauce.isDragging=true;Sauce.hasTriggeredSauce=false;Sauce.targetRotationX=Sauce.currentRotationX;Sauce.UpdateMaterial();Sauce.OnRotationStarted?.Invoke();}
            else if(Oven!=null){Oven.isDragging=true;Oven.UpdateMaterial();Oven.OnHandleGrabbed?.Invoke();}
            else {Oven2.isDragging=true;Oven2.UpdateMaterial();Oven2.OnHandleGrabbed?.Invoke();}
        }
        internal void Release(){if(Component==null)return;if(Press!=null)Press.StopDragging();else if(Slicer!=null)Slicer.StopDragging();else if(Sauce!=null)Sauce.StopDragging();else if(Oven!=null)Oven.StopDragging();else Oven2.StopDragging();}
        internal void NativeUpdate()
        {
            if(Press!=null){Press.UpdateHandlePosition();Press.UpdatePressProgress();}
            else if(Sauce!=null){Sauce.UpdateHandleRotation();Sauce.UpdateSauceProgress();}
            else if(Slicer!=null){if(Slicer.isEnabled){Slicer.UpdateHandlePosition();Slicer.UpdateSliceProgress();}}
            // Match the native Update gate: external network progress remains
            // latched after a cycle and does not prevent a new local drag.
            // Only the native return-to-start coroutine owns motion while resetting.
            else if(Oven!=null){if(Oven.isEnabled&&!Oven.isResetting){Oven.UpdateHandleRotation();Oven.UpdateTrayPosition();Oven.UpdateCookingState();}}
            else if(Oven2.isEnabled&&!Oven2.isResetting){Oven2.UpdateHandlePosition();Oven2.UpdateTrayPositions();Oven2.UpdateCookingState();}
        }
    }
    private sealed class Grab
    {internal Handle Handle;internal bool WasGrip,Armed,Remote;internal Plane DragPlane;internal Vector3 DragOrigin,DragAxis;internal Vector3 LocalPoint,StartController;internal Quaternion LocalRotation;internal float StartValue;}
    private static readonly Grab left=new(),right=new();
    private static readonly Dictionary<IntPtr,Handle> handles=new();
    private static MachineInteractionBase owner;
    private static int frame=-1;
    private static MachineInteractionBase direct,pending;
    private static float pendingAt;
    private static Handle requestedHandle;private static bool requestedRemote,requestedLeft;private static Vector3 requestedLocalPoint;
    private static readonly Dictionary<IntPtr,Handle> available=new();
    private static bool discovered;
    private static HUDManager remoteHud;private static bool previousCrosshair;
    private static bool Supported(MachineInteractionBase m)=>m!=null&&(m.TryCast<DoughPressMachine>()!=null||m.TryCast<DoughPressMachineL2>()!=null||m.TryCast<ComboPressSauceMachine>()!=null||HasRegistered(m));
    private static bool HasRegistered(MachineInteractionBase m){foreach(var h in available.Values)if(h.Machine!=null&&h.Machine.Pointer==m.Pointer)return true;return false;}
    private static void Register(MonoBehaviour component)
    {
        if(component==null||available.ContainsKey(component.Pointer))return;
        var machine=component.GetComponentInParent<MachineInteractionBase>(true);if(machine==null)return;
        var h=new Handle{Machine=machine,Component=component,Press=component.TryCast<PressHandle>(),Slicer=component.TryCast<SlicerHandle>(),Sauce=component.TryCast<SauceHandle>(),Oven=component.TryCast<OvenHandle>(),Oven2=component.TryCast<OvenLevel2Handle>()};
        h.Transform=component.transform;h.Parent=h.Transform.parent;var c=component.GetComponentsInChildren<Collider>(true);h.Colliders=new Collider[c.Length];for(int i=0;i<c.Length;i++)h.Colliders[i]=c[i];var body=machine.GetComponents<Collider>();h.MachineColliders=new Collider[body.Length];for(int i=0;i<body.Length;i++)h.MachineColliders[i]=body[i];available.Add(component.Pointer,h);
    }
    private static void Started(MonoBehaviour __instance)=>Register(__instance);
    private static bool Enter(MachineInteractionBase __instance)
    {
        if(!MotionInput.Enabled||!StereoTrial.GameplayActive||!Supported(__instance))return true;
        // Retain the native ownership and machine callbacks, but never detach,
        // move, rotate or hide the gameplay camera/player for a physical handle.
        if(__instance.playerController==null||!__instance.playerController.IsOwner)__instance.FindLocalPlayerController();if(__instance.playerController==null)return true;
        #if IKAG_DIAGNOSTICS
        Plugin.Line("DIRECT_ENTER "+__instance.name);
#endif
        direct=__instance;__instance.mainCamera=__instance.playerController.PlayerCamera;
        // Keep native emergency teardown valid even if destruction/disconnect
        // bypasses the regular exit RPC. Normal entry/exit never writes these.
        var camera=__instance.mainCamera;var t=camera.transform;
        __instance.originalCameraParent=t.parent;__instance.originalCameraPosition=t.position;__instance.originalCameraRotation=t.rotation;
        __instance.originalCameraLocalPosition=t.localPosition;__instance.originalCameraLocalRotation=t.localRotation;__instance.originalCameraCullingMask=camera.cullingMask;
        __instance.isInUseLocally=true;MachineInteractionBase.IsLocalPlayerInMachineMode=true;MachineInteractionBase.CurrentActiveMachine=__instance;
        __instance.isTransitioning=false;MachineInteractionBase.isLocalMachineTransitionInProgress=false;
        __instance.PlaySound(__instance.interactSound);__instance.OnEnterMachineMode();return false;
    }
    private static bool Exit(MachineInteractionBase __instance)
    {
        if(direct==null||direct.Pointer!=__instance.Pointer)return true;
        Release(left);Release(right);pending=null;direct=null;
        __instance.isInUseLocally=false;MachineInteractionBase.IsLocalPlayerInMachineMode=false;MachineInteractionBase.CurrentActiveMachine=null;
        __instance.isTransitioning=false;MachineInteractionBase.isLocalMachineTransitionInProgress=false;
        __instance.OnExitMachineMode();__instance.PlaySound(__instance.exitSound);return false;
    }
    private static void Approach(Grab g,ControllerTransport.Hand hand)
    {
        if(MotionShovel.OwnsGrip(g==left)||PhysicalHeldItems.OwnsGrip(g==left)||WorldPress.OwnsGrip(g==left)){g.WasGrip=hand.Grip;return;}
        if(!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out _)){g.Armed=false;g.WasGrip=hand.Grip;return;}
        if(!hand.Grip)g.Armed=true;
        if(hand.Grip&&!g.WasGrip&&g.Armed&&pending==null)
        {
            Handle nearest=null;float best=.12f*.12f;
            foreach(var h in available.Values){if(h.Component==null||h.Machine==null||!h.Component.gameObject.activeInHierarchy||h.Machine.IsBeingRelocated())continue;
                foreach(var c in h.Colliders){if(c==null||!c.enabled||!c.gameObject.activeInHierarchy)continue;float distance=(c.ClosestPoint(p)-p).sqrMagnitude;if(distance<best){nearest=h;best=distance;}}}
            #if IKAG_DIAGNOSTICS
            Plugin.Line("DIRECT_APPROACH count="+available.Count+" nearest="+nearest?.Machine?.name+" allowed="+(nearest!=null&&nearest.Machine.CanInteract()));
#endif
            if(nearest!=null&&nearest.Machine.CanInteract())Request(nearest,g==left,false,default);
        }
        // Leave the edge pending until the native request grants local ownership.
        if(pending==null)g.WasGrip=hand.Grip;
    }

    private static void Request(Handle handle,bool useLeft,bool remote,Vector3 point)
    {requestedHandle=handle;requestedRemote=remote;requestedLeft=useLeft;if(remote)requestedLocalPoint=handle.Transform.InverseTransformPoint(point);pending=handle.Machine;pendingAt=Time.unscaledTime;pending.OnInteract();}
    internal static bool AcquiringRemote=>pending!=null&&requestedRemote;
    internal static bool RemoteLeft=>requestedLeft;
    internal static bool TryDistance(bool useLeft)
    {
        if(pending!=null||MachineInteractionBase.IsLocalPlayerInMachineMode)return false;
        Handle nearest=null;bool remote=false;Vector3 remotePoint=default;
        if(InteractionReticle.TryRawHit(out var hit,out remotePoint))
        {
            foreach(var h in available.Values){if(h.Component==null||h.Machine==null||!h.Component.gameObject.activeInHierarchy||h.Machine.IsBeingRelocated()||h.Busy)continue;
                foreach(var c in h.Colliders)if(c!=null&&c.enabled&&c.Pointer==hit.Pointer){nearest=h;remote=true;break;}
                if(nearest!=null)break;}
            if(nearest==null)
            {
                // Some native machine body colliders enclose their handle.
                // Reuse that exact hit only when it identifies one handle.
                Handle candidate=null;int count=0;
                foreach(var h in available.Values){if(h.Component==null||h.Machine==null||!h.Component.gameObject.activeInHierarchy||h.Machine.IsBeingRelocated())continue;
                    foreach(var c in h.MachineColliders)if(c!=null&&c.Pointer==hit.Pointer){candidate=h;++count;break;}}
                if(count==1&&!candidate.Busy){float bestDistance=float.PositiveInfinity;Vector3 contact=remotePoint;
                    foreach(var c in candidate.Colliders){if(c==null||!c.enabled)continue;var pt=c.ClosestPoint(remotePoint);float d=(pt-remotePoint).sqrMagnitude;if(d<bestDistance){bestDistance=d;contact=pt;}}
                    if(float.IsFinite(bestDistance)){nearest=candidate;remote=true;remotePoint=contact;}}
            }
        }
        if(nearest==null||!remote||!nearest.Machine.CanInteract())return false;
        Request(nearest,useLeft,true,remotePoint);return true;
    }

    private static Vector3 AxisOf(NeedleRotationAxis axis)=>axis==NeedleRotationAxis.X?Vector3.right:axis==NeedleRotationAxis.Y?Vector3.up:Vector3.forward;
    internal static bool Active=>owner!=null&&handles.Count>0&&MachineInteractionBase.IsLocalPlayerInMachineMode;
    internal static bool Acquiring=>pending!=null||OrderSwitchHands.Active;
    internal static void Install()
    {
        var h=new Harmony("iknowaguyvr.kitchen-hands");
        foreach(var type in new[]{typeof(PressHandle),typeof(SlicerHandle),typeof(SauceHandle),typeof(OvenHandle),typeof(OvenLevel2Handle)})
            {
            h.Patch(type.GetMethod("Start"),postfix:new HarmonyMethod(typeof(KitchenHands).GetMethod(nameof(Started),BindingFlags.NonPublic|BindingFlags.Static)));
            h.Patch(type.GetMethod("Update"),prefix:new HarmonyMethod(typeof(KitchenHands).GetMethod(nameof(Update),BindingFlags.NonPublic|BindingFlags.Static)));
        }
        h.Patch(typeof(MachineInteractionBase).GetMethod("EnterMachineModeLocal"),prefix:new HarmonyMethod(typeof(KitchenHands).GetMethod(nameof(Enter),BindingFlags.NonPublic|BindingFlags.Static)));
        h.Patch(typeof(MachineInteractionBase).GetMethod("ExitMachineModeLocal"),prefix:new HarmonyMethod(typeof(KitchenHands).GetMethod(nameof(Exit),BindingFlags.NonPublic|BindingFlags.Static)));
    }
    private static bool Update(MonoBehaviour __instance)
    {
        Tick();if(!MotionInput.Enabled||!handles.TryGetValue(__instance.Pointer,out var handle))return true;
        // Native Update bodies differ and some inline HandleMouseInput. Preserve
        // their motion/progress/event work, replacing only desktop mouse input.
        handle.NativeUpdate();return false;
    }
    internal static void Tick()
    {
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        if(direct!=null&&!direct.isInUseLocally)direct=null;
        if(!discovered&&StereoTrial.GameplayActive){discovered=true;foreach(var c in UnityEngine.Object.FindObjectsOfType<PressHandle>())Register(c);foreach(var c in UnityEngine.Object.FindObjectsOfType<SlicerHandle>())Register(c);foreach(var c in UnityEngine.Object.FindObjectsOfType<SauceHandle>())Register(c);foreach(var c in UnityEngine.Object.FindObjectsOfType<OvenHandle>())Register(c);foreach(var c in UnityEngine.Object.FindObjectsOfType<OvenLevel2Handle>())Register(c);}
        if(!StereoTrial.GameplayActive){discovered=false;available.Clear();}
        ControllerTransport.Sample();
        OrderSwitchHands.Tick();
        if(pending!=null&&Time.unscaledTime-pendingAt>2){pending=null;left.WasGrip=ControllerTransport.Left.Grip;right.WasGrip=ControllerTransport.Right.Grip;}
        if(!OrderSwitchHands.Active&&MotionInput.Enabled&&StereoTrial.GameplayActive&&Probe.Focused&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&UnityEngine.Cursor.lockState==CursorLockMode.Locked&&!ImmersiveHud.InputOwned){Approach(left,ControllerTransport.Left);Approach(right,ControllerTransport.Right);}
        var current=MotionInput.Enabled&&MachineInteractionBase.IsLocalPlayerInMachineMode?MachineInteractionBase.CurrentActiveMachine:null;
        if((current==null)!=(owner==null)||(current!=null&&current.Pointer!=owner.Pointer))
        {
            bool grant=pending!=null&&current!=null&&pending.Pointer==current.Pointer;bool la=left.Armed,ra=right.Armed;Release(left);Release(right);if(grant){left.Armed=la;right.Armed=ra;pending=null;}handles.Clear();owner=current;
            if(owner!=null)
            {
                foreach(var h in available.Values)if(h.Component!=null&&h.Machine!=null&&h.Machine.Pointer==owner.Pointer)handles.Add(h.Component.Pointer,h);
                var requestedHand=requestedLeft?ControllerTransport.Left:ControllerTransport.Right;var requestedGrab=requestedLeft?left:right;
                if(grant&&requestedRemote&&requestedHandle!=null&&requestedHandle.Enabled&&requestedHand.Grip&&MotionPointer.TryControllerRay(requestedLeft,out var ray)&&StereoTrial.TryTrackingPose(requestedHand.Position,requestedHand.Rotation,out _,out var rotation))
                {
                    var contact=requestedHandle.Transform.TransformPoint(requestedLocalPoint);
                    requestedGrab.Remote=true;requestedGrab.DragOrigin=contact;
                    requestedGrab.DragAxis=(requestedHandle.Parent==null?requestedHandle.Axis:requestedHandle.Parent.TransformVector(requestedHandle.Axis)).normalized;
                    requestedGrab.DragPlane=new Plane(requestedHandle.Parent==null?requestedHandle.Axis:requestedHandle.Parent.localToWorldMatrix.inverse.transpose.MultiplyVector(requestedHandle.Axis).normalized,contact);
                    BeginGrab(requestedGrab,requestedHandle,contact,rotation,contact);
                }
                requestedHandle=null;requestedRemote=false;
            }
        }
        if(handles.Count==0)return;
        var hud=HUDManager.LocalInstance;
        bool live=owner!=null&&Probe.Focused&&StereoTrial.GameplayActive&&!MachineInteractionBase.isLocalMachineTransitionInProgress&&(hud==null||!hud.IsEscapeMenuOpen);
        Step(left,right,ControllerTransport.Left,live);Step(right,left,ControllerTransport.Right,live);
        if(direct!=null&&(!live||(left.Handle==null&&right.Handle==null)))direct.TriggerExit();
    }
    private static void Release(Grab g){if(g.Remote){if(remoteHud!=null&&MachineInteractionBase.IsLocalPlayerInMachineMode)remoteHud.SetCrosshairVisible(previousCrosshair);remoteHud=null;}g.Handle?.Release();g.Handle=null;g.Armed=false;g.Remote=false;}
    private static void Step(Grab g,Grab other,ControllerTransport.Hand hand,bool live)
    {
        if(!live||!hand.Tracked||!StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var p,out var q)) {Release(g);g.WasGrip=hand.Grip;return;}
        if(!hand.Grip){if(g.Handle!=null)Release(g);g.Armed=true;}
        if(g.Handle!=null&&(g.Handle.Component==null||!g.Handle.Enabled)){Release(g);}
        if(g.Handle==null&&hand.Grip&&!g.WasGrip&&g.Armed)
        {
            Handle nearest=null;Vector3 contact=default;float best=.12f*.12f;
            foreach(var h in handles.Values)
            {
                if(h.Component==null||!h.Enabled||h.Busy||h==other.Handle)continue;
                foreach(var c in h.Colliders)
                {if(c==null||!c.enabled||!c.gameObject.activeInHierarchy)continue;var pt=c.ClosestPoint(p);float d=(pt-p).sqrMagnitude;if(d<best){best=d;nearest=h;contact=pt;}}
            }
            if(nearest!=null)
            {
                BeginGrab(g,nearest,contact,q,p);
            }
        }
        g.WasGrip=hand.Grip;
        if(g.Handle==null||g.Handle.Busy)return;
        if(g.Remote&&g.Handle.Sauce!=null)
        {
            if(TrySauceTarget(g,out var target))g.Handle.Target(target);
            return;
        }
        if(g.Remote&&!TryDragPoint(g,out p))return;
        var handle=g.Handle;var local=handle.Parent==null?p:handle.Parent.InverseTransformPoint(p);
        float delta;
        if(handle.Rotary)
        {
            var pivot=handle.Transform.localPosition;var from=Vector3.ProjectOnPlane(g.StartController-pivot,handle.Axis);var to=Vector3.ProjectOnPlane(local-pivot,handle.Axis);
            delta=from.sqrMagnitude>.0001f&&to.sqrMagnitude>.0001f?Vector3.SignedAngle(from,to,handle.Axis):0;
        }
        else {
            delta=Vector3.Dot(local-g.StartController,handle.Axis);
            // The sliding oven stores a 0..90 angle internally, then maps
            // its normalized progress onto the authored slide endpoints.
            if(handle.Oven!=null){float travel=handle.Oven.slideClosedValue-handle.Oven.slideOpenValue;delta=Mathf.Abs(travel)>.00001f?delta*(handle.Oven.maxLocalRotationY-handle.Oven.minLocalRotationY)/travel:0;}
        }
        handle.Target(g.StartValue+delta);
    }
    private static bool TrySauceTarget(Grab g,out float target)
    {
        target=g.Handle.Sauce.targetRotationX;
        if(!MotionPointer.TryControllerRay(g==left,out var ray))return false;
        var h=g.Handle;var pivot=h.Transform.localPosition;
        var origin=h.Parent.InverseTransformPoint(ray.origin)-pivot;
        var direction=h.Parent.InverseTransformVector(ray.direction);
        // Intersect the ray with the authored contact's cylindrical sweep.
        // This remains defined when the ray lies in the rotation plane, where
        // ray/plane intersection cannot recover a drag point. Axial motion is
        // ignored, just as it is for a physical rotary handle.
        origin=Vector3.ProjectOnPlane(origin,h.Axis);direction=Vector3.ProjectOnPlane(direction,h.Axis);
        var start=Vector3.ProjectOnPlane(g.StartController-pivot,h.Axis);
        float a=Vector3.Dot(direction,direction),b=Vector3.Dot(origin,direction),radius2=start.sqrMagnitude;
        if(a<1e-8f||radius2<1e-8f)return false;
        float discriminant=b*b-a*(origin.sqrMagnitude-radius2);
        if(discriminant<0)
        {
            // Outside the projected arc, use its closest radial direction.
            var point=origin+direction*Mathf.Max(0,-b/a);
            if(point.sqrMagnitude<1e-8f)return false;
            target=g.StartValue+Vector3.SignedAngle(start,point,h.Axis);return true;
        }
        float root=Mathf.Sqrt(discriminant),near=(-b-root)/a,far=(-b+root)/a;
        bool found=false;float bestOutside=float.PositiveInfinity,bestTravel=float.PositiveInfinity;
        ConsiderSauceRoot(g,start,origin,direction,near,ref found,ref bestOutside,ref bestTravel,ref target);
        ConsiderSauceRoot(g,start,origin,direction,far,ref found,ref bestOutside,ref bestTravel,ref target);
        return found;
    }
    private static void ConsiderSauceRoot(Grab g,Vector3 start,Vector3 origin,Vector3 direction,float distance,ref bool found,ref float bestOutside,ref float bestTravel,ref float target)
    {
        if(distance<0)return;
        var h=g.Handle;float value=g.StartValue+Vector3.SignedAngle(start,origin+direction*distance,h.Axis);
        float min=Mathf.Min(h.Sauce.startRotationX,h.Sauce.endRotationX),max=Mathf.Max(h.Sauce.startRotationX,h.Sauce.endRotationX);
        // Prefer the branch inside the native travel range. If both projected
        // branches are possible, continuity preserves the currently held arc.
        float outside=Mathf.Abs(value-Mathf.Clamp(value,min,max));
        float travel=Mathf.Abs(value-h.Sauce.targetRotationX);
        if(found&&(outside>bestOutside||(outside==bestOutside&&travel>=bestTravel)))return;
        found=true;bestOutside=outside;bestTravel=travel;target=value;
    }
    private static bool TryDragPoint(Grab g,out Vector3 p)
    {
        p=default;
            if(!MotionPointer.TryControllerRay(g==left,out var ray))return false;
            if(g.Handle.Rotary)
            {
                // The moving contact follows the handle's rotation plane.
                if(!g.DragPlane.Raycast(ray,out float distance)||distance<0)return false;
                p=ray.GetPoint(distance);
            }
            else
            {
                // Closest point between the aim ray and authored travel line.
                // A view-facing plane would attenuate travel on oblique handles.
                float cosine=Vector3.Dot(g.DragAxis,ray.direction),denominator=1-cosine*cosine;
                if(denominator<.0001f)return false;
                var offset=ray.origin-g.DragOrigin;
                float along=(Vector3.Dot(g.DragAxis,offset)-cosine*Vector3.Dot(ray.direction,offset))/denominator;
                p=g.DragOrigin+g.DragAxis*along;
                if(Vector3.Dot(p-ray.origin,ray.direction)<0)return false;
            }
        return true;
    }
    private static void BeginGrab(Grab g,Handle h,Vector3 contact,Quaternion rotation,Vector3 controller)
    {g.Handle=h;if(g.Remote&&h.Sauce==null&&TryDragPoint(g,out var projected))controller=projected;g.LocalPoint=h.Transform.InverseTransformPoint(contact);g.LocalRotation=Quaternion.Inverse(h.Transform.rotation)*rotation;g.StartController=h.Parent==null?controller:h.Parent.InverseTransformPoint(controller);g.StartValue=h.Value;h.Grab();MotionHaptics.Pulse(g==left);if(g.Remote){remoteHud=HUDManager.LocalInstance;if(remoteHud!=null&&remoteHud.crosshairElement!=null){previousCrosshair=remoteHud.crosshairElement.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None;remoteHud.SetCrosshairVisible(true);}}}
    internal static bool DistancePoint(out Vector3 position)
    {var g=left.Remote?left:right;position=default;if(!g.Remote||g.Handle==null||g.Handle.Component==null)return false;position=g.Handle.Transform.TransformPoint(g.LocalPoint);return true;}
    internal static bool Pose(bool useLeft,out Vector3 position,out Quaternion rotation)
    {
        if(OrderSwitchHands.Pose(useLeft,out position,out rotation))return true;
        var g=useLeft?left:right;position=default;rotation=default;
        if(g.Remote||g.Handle==null||g.Handle.Component==null)return false;
        position=g.Handle.Transform.TransformPoint(g.LocalPoint);rotation=g.Handle.Transform.rotation*g.LocalRotation;return true;
    }
}
