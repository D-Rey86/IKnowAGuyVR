using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class VrLocomotion
{
    private static bool turnReady,moveReady,wasReady;private static Gamepad pad;private static StickControl padMove,padLook;
    private static PlayerMovementController moveOwner;private static Transform moveRoot;
    internal static bool Aiming{get;private set;}internal static bool TargetValid{get;private set;}
    internal static Ray AimRay{get;private set;}internal static Vector3 AimEnd{get;private set;}
    private static Vector3 destination;
#if IKAG_DIAGNOSTICS
    internal static Vector3 Destination=>destination;
    internal static RaycastHit LastHit{get;private set;}
    internal static bool HasHit{get;private set;}
#endif
    internal static void Reset(){Aiming=TargetValid=turnReady=moveReady=wasReady=false;}
    internal static void GamepadMovement(PlayerMovementController player)
    {
        var current=VrGamepadDevice.Current;
        if(current==null){Reset();pad=null;padMove=padLook=null;MotionPointer.UpdateTeleportVisual();return;}
        if(pad==null||pad.Pointer!=current.Pointer){pad=current;padMove=pad.leftStick;padLook=pad.rightStick;Reset();}
        Apply(player,padMove.ReadValue(),padLook.ReadValue(),true,!ImmersiveHud.BoardOwnsStick);
        MotionPointer.UpdateTeleportVisual();
    }
    internal static void Apply(PlayerMovementController player,Vector2 move,Vector2 turn,bool gamepad,bool allowTurn)
    {
        var hud=HUDManager.LocalInstance;
        bool ready=StereoTrial.GameplayActive&&Probe.Focused&&!MachineInteractionBase.IsLocalPlayerInMachineMode&&
            !ArcadeMotion.Active&&!SpyMotion.Active&&!MotionInput.WheelOpen&&!MotionInput.ConversationActive&&!MotionInput.PhotoMode&&
            !ContextHud.ModalActive&&(hud==null||!hud.IsEscapeMenuOpen)&&Cursor.lockState==CursorLockMode.Locked;
        if(!ready){Reset();return;}
        if(!wasReady){turnReady=Mathf.Abs(turn.x)<.2f;moveReady=move.sqrMagnitude<.04f;wasReady=true;}
        var look=player.lookInput;if(gamepad)look.x=0;
        if(Mathf.Abs(turn.x)<.2f)turnReady=true;
        if(allowTurn&&player.mouseSensitivity>0&&player.canLook&&!player.forcedLookActive)
        {
            float degrees=0;
            if(VrSettings.SnapTurn.Value){if(turnReady&&Mathf.Abs(turn.x)>.65f){float snapped=player.horizontalRotation+Mathf.Sign(turn.x)*VrSettings.SnapAngle.Value;player.horizontalRotation=snapped;player.targetHorizontalRotation=snapped;turnReady=false;}}
            else degrees=MotionInput.Deadzone(turn).x*VrSettings.TurnSpeed.Value*Time.deltaTime;
            look.x+=degrees/player.mouseSensitivity;
        }
        else turnReady=false;
        player.lookInput=look;
        if(!VrSettings.Teleport.Value){Aiming=TargetValid=false;if(!gamepad)player.moveInput=WalkingInput(player,MotionInput.Deadzone(move));return;}
        if(!player.canMove||player.forcedLookActive||player.isClimbing||player.isSwimming||player.isSkating){Aiming=TargetValid=false;moveReady=false;return;}
        player.moveInput=Vector2.zero;
        if(move.sqrMagnitude<.04f)
        {
            if(Aiming&&TargetValid)player.Teleport(destination);
            Aiming=TargetValid=false;moveReady=true;return;
        }
        if(!Aiming&&moveReady&&move.y>.65f){Aiming=true;moveReady=false;}
        if(!Aiming)return;
        if(gamepad){if(!StereoTrial.TryHeadWorld(out var p,out var q)){Reset();return;}AimRay=new Ray(p,q*Vector3.forward);}
        else if(MotionPointer.TryControllerRay(!VrControls.LeftHanded,out var ray))AimRay=ray;else {Reset();return;}
        AimEnd=AimRay.GetPoint(8);TargetValid=false;
        bool hasHit=Physics.Raycast(AimRay,out var hit,8,~player.ignoreLayers,QueryTriggerInteraction.Ignore);
#if IKAG_DIAGNOSTICS
        HasHit=hasHit;LastHit=hit;
#endif
        if(!hasHit)return;
        AimEnd=hit.point;
        if(Vector3.Dot(hit.normal,Vector3.up)<Mathf.Cos(player.slopeLimit*Mathf.Deg2Rad)||player.capsuleCollider==null)return;
        // Reuse native capsule geometry, clearance and network teleport.
        float feet=player.capsuleCollider.bounds.min.y-player.transform.position.y;
        destination=hit.point+Vector3.up*(player.skinWidth-feet);
        TargetValid=player.IsCapsuleClearAt(destination);
    }
    private static Vector2 WalkingInput(PlayerMovementController player,Vector2 move)
    {
        // Native movement consumes a vector in the character's yaw frame. The
        // headset can physically turn independently of that frame. Express the
        // requested tracked-heading movement in native coordinates, preserving
        // analog magnitude and native collision/speed/acceleration handling.
        // Ladder axes and the game's skating/swimming controls keep their own
        // semantics; this is the smooth walking (including airborne) path.
        if(move.sqrMagnitude==0||player.isClimbing||player.isSkating||player.isSwimming||!StereoTrial.TryHeadWorld(out _,out var head))return move;
        if(moveOwner!=player||moveRoot==null){moveOwner=player;moveRoot=player.transform;}
        var forward=Vector3.ProjectOnPlane(head*Vector3.forward,Vector3.up);
        // Use the sight heading when pitched and rolled together. The projected
        // right axis alone skews that heading. Near vertical, sight has no
        // stable horizontal bearing, so retain the head's right-axis heading.
        if(forward.sqrMagnitude<.03f){var horizontalRight=Vector3.ProjectOnPlane(head*Vector3.right,Vector3.up);if(horizontalRight.sqrMagnitude<1e-6f)return move;forward=Vector3.Cross(horizontalRight,Vector3.up);}
        forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
        var native=Quaternion.Inverse(moveRoot.rotation)*(right*move.x+forward*move.y);
        return new Vector2(native.x,native.z);
    }
}
