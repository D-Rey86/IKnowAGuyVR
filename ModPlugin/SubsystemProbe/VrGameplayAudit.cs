using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace IKnowAGuyVR.SubsystemProbe;

internal static unsafe class VrGameplayAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_SETTINGS_AUDIT")=="gameplay";
    private static int phase,angleIndex;private static readonly int[] angles={15,30,45,90};private static float next=25,yaw,at,baseSeparation,eyeDistance;
    private static PlayerMovementController player;private static VrSettingsMenu.Page page;
    private static Vector3 head,hand,start,snapHead,headAimTarget,goodTeleportPosition;private static Gamepad pad;private static int interactions,jumps,uses,slot;
    internal static void Views(RendererAudit.NativeParameter[] eyes)
    {
        if(!enabled)return;
        var a=eyes[0].View.inverse;var b=eyes[1].View.inverse;
        eyeDistance=Vector3.Distance(new Vector3(a.m03,a.m13,a.m23),new Vector3(b.m03,b.m13,b.m23));
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void QueuePad(Vector2 move,Vector2 look,uint buttons=0,float trigger=0)
    {
        byte* bytes=stackalloc byte[52];*(InputEvent*)bytes=new InputEvent(StateEvent.Type,52,pad.deviceId,-1);
        *(UnityEngine.InputSystem.Utilities.FourCC*)(bytes+20)=GamepadState.Format;
        *(GamepadState*)(bytes+24)=new GamepadState{leftStick=move,rightStick=look,buttons=buttons,rightTrigger=trigger};
        Plugin.Line($"VR_GAMEPAD_EVENT type={((InputEvent*)bytes)->type} size={((InputEvent*)bytes)->sizeInBytes} device={((InputEvent*)bytes)->deviceId} time={((InputEvent*)bytes)->time} format={*(UnityEngine.InputSystem.Utilities.FourCC*)(bytes+20)} enabled={pad.enabled} background={pad.canRunInBackground} appFocus={Application.isFocused} behavior={InputSystem.settings.backgroundBehavior}");
        InputSystem.QueueEvent(new InputEventPtr((InputEvent*)bytes));
    }
    private static Vector3 ClearFloor()
    {
        Ray handRay;if(VrSettings.Gamepad.Value){Check(StereoTrial.TryHeadWorld(out var h,out var q),"No tracked head");handRay=new Ray(h,q*Vector3.forward);}else Check(MotionPointer.TryControllerRay(true,out handRay),"No tracked left aim origin");
        var root=player.transform.position;float feet=player.capsuleCollider.bounds.min.y-root.y;
        foreach(float distance in new[]{1.2f,1.8f,2.4f})for(int i=0;i<12;++i)
        {
            var direction=Quaternion.AngleAxis(i*30,Vector3.up)*player.transform.forward;
            if(!Physics.Raycast(root+direction*distance+Vector3.up*1.5f,Vector3.down,out var hit,3,~player.ignoreLayers,QueryTriggerInteraction.Ignore))continue;
            if(Vector3.Dot(hit.normal,Vector3.up)<.99f||Mathf.Abs(hit.point.y-player.capsuleCollider.bounds.min.y)>.15f)continue;
            if(!player.IsCapsuleClearAt(hit.point+Vector3.up*(player.skinWidth-feet)))continue;
            if(Physics.Linecast(handRay.origin,hit.point+Vector3.up*player.skinWidth,~player.ignoreLayers,QueryTriggerInteraction.Ignore))continue;
            Plugin.Line("VR_TELEPORT_CLEAR_FIXTURE "+hit.point);return hit.point;
        }
        throw new Exception("No measured clear visible floor for teleport fixture");
    }
    internal static void Tick()
    {
        if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try{float delay=.6f;switch(phase)
        {
            case 0:
                player=InteractionManager.LocalInstance.movementController;File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"input-actions.json"),Core.Input.InputManager.Instance.inputActionsAsset.ToJson());
                if(Environment.GetEnvironmentVariable("IKAG_XR_GAMEPAD_STARTUP")=="1")
                {Check(VrSettings.Gamepad.Value,"Gamepad config was not active at startup");Plugin.Line("VR_GAMEPAD_STARTUP_CONFIG_PASS");goodTeleportPosition=player.transform.position;VrSettings.Teleport.Value=false;VrSettings.SnapTurn.Value=false;pad=InputSystem.AddDevice("Gamepad").Cast<Gamepad>();InputSystem.DisableDevice(pad,true);pad.disabledWhileInBackground=true;QueuePad(Vector2.zero,Vector2.zero);phase=19;next=Time.realtimeSinceStartup+.6f;return;}
                if(Environment.GetEnvironmentVariable("IKAG_XR_TELEPORT_TRACE")=="1"){player.Teleport(player.transform.position,Quaternion.Euler(0,141.9f,0));VrSettings.Teleport.Value=true;Send("input clear\nhands reset");phase=15;next=Time.realtimeSinceStartup+1;return;}
                UiInputAudit.Dump("Assembly-CSharp.dll","","PlayerMovementController","HandleCameraRotation","HandleBodyRotation","IsCapsuleClearAt","GetCapsulePointsAt","Teleport");
                VrSettings.WorldScale.Value=100;VrSettings.Separation.Value=100;VrSettings.Teleport.Value=false;VrSettings.SnapTurn.Value=false;VrSettings.Gamepad.Value=false;
                Send("input clear\nhands reset");break;
            case 1:
                HUDManager.LocalInstance.ShowEscapeMenu();HUDManager.LocalInstance.OpenSettingsPanel();break;
            case 2:
                page=VrSettingsMenu.ForPanel(HUDManager.LocalInstance.root);if(page==null)VrSettingsAudit.Dump(HUDManager.LocalInstance.escapeSettingsPanel);Check(page!=null,"Pause Settings did not bind a VR page");page.Show();break;
            case 3:
                Check(page.Root.worldBound.width>200&&page.Root.worldBound.height>200,"Pause VR page has no layout");
                Send("shot vr-pause-picture");phase=299;break;
            case 300:page.Scroll.scrollOffset=new Vector2(0,10000);phase=3;break;
            case 4:
                Send("shot vr-pause-gameplay");phase=399;break;
            case 400:HUDManager.LocalInstance.CloseSettingsPanel();HUDManager.LocalInstance.HideEscapeMenu();phase=4;break;
            case 5:
                baseSeparation=eyeDistance;Check(StereoTrial.TryHeadWorld(out head,out _),"Head pose unavailable");
                StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out hand,out _);
                VrSettings.WorldScale.Value=150;break;
            case 6:
                Check(StereoTrial.TryHeadWorld(out var scaledHead,out _),"Scaled head unavailable");
                StereoTrial.TryTrackingPose(ControllerTransport.Right.Position,ControllerTransport.Right.Rotation,out var scaledHand,out _);
                Plugin.Line($"VR_WORLD_SCALE deltaHead={Vector3.Distance(head,scaledHead):R} handRatio={Vector3.Distance(scaledHead,scaledHand)/Vector3.Distance(head,hand):R} eyeRatio={eyeDistance/baseSeparation:R}");
                Check(Vector3.Distance(head,scaledHead)<.0001f,"World scale moved head center");
                Check(Mathf.Abs(Vector3.Distance(scaledHead,scaledHand)/Vector3.Distance(head,hand)-2f/3)<.001f,"Controller tracking scale mismatch");
                Check(Mathf.Abs(eyeDistance/baseSeparation-2f/3)<.001f,"Eye tracking scale mismatch");VrSettings.WorldScale.Value=100;VrSettings.Separation.Value=200;break;
            case 7:Check(Mathf.Abs(eyeDistance/baseSeparation-2)<.001f,"Separation did not double");VrSettings.Separation.Value=0;break;
            case 8:Check(eyeDistance<.0001f,"Zero separation retained eye offset");VrSettings.Separation.Value=100;Send("stick r 0 0\nstick l 0 0");break;
            case 9:yaw=player.horizontalRotation;at=Time.realtimeSinceStartup;Send("stick r 0.8 0");delay=1;break;
            case 10:
                Plugin.Line($"VR_SMOOTH_TURN degrees={Mathf.DeltaAngle(yaw,player.horizontalRotation):R} seconds={Time.realtimeSinceStartup-at:R} target={player.targetHorizontalRotation} sensitivity={player.mouseSensitivity}");
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,player.horizontalRotation))>40,"Smooth turn did not move camera");Send("stick r 0 0");VrSettings.SnapTurn.Value=true;VrSettings.SnapAngle.Value=angles[angleIndex];break;
            case 11:yaw=player.targetHorizontalRotation;StereoTrial.TryHeadWorld(out snapHead,out _);Send("stick r 0.9 0");break;
            case 12:
                Plugin.Line($"VR_SNAP_TURN angle={angles[angleIndex]} delta={Mathf.DeltaAngle(yaw,player.targetHorizontalRotation):R} actual={player.horizontalRotation} target={player.targetHorizontalRotation}");
                StereoTrial.TryHeadWorld(out var afterSnapHead,out _);Plugin.Line("VR_SNAP_HEAD_DELTA "+Vector3.Distance(snapHead,afterSnapHead));
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,player.targetHorizontalRotation)-angles[angleIndex])<.05f,"Snap angle wrong");yaw=player.targetHorizontalRotation;break;
            case 13:
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,player.targetHorizontalRotation))<.01f,"Held snap stick repeated");Send("stick r 0 0");
                if(++angleIndex<angles.Length){VrSettings.SnapAngle.Value=angles[angleIndex];phase=11;next=Time.realtimeSinceStartup+.6f;return;}break;
            case 14:
                VrSettings.SnapTurn.Value=false;VrSettings.Teleport.Value=true;Send("stick r 0 0\nstick l 0 0");break;
            case 15:
                start=player.transform.position;var point=ClearFloor();
                Send(MotionPointer.AimAtWorld(point,true));break;
            case 16:Send("stick l 0 0.9");break;
            case 17:
                Plugin.Line($"VR_TELEPORT_AIM valid={VrLocomotion.TargetValid} start={start} hit={VrLocomotion.AimEnd} capsule={player.capsuleCollider.bounds} skin={player.skinWidth}");
                Plugin.Line($"VR_TELEPORT_HIT aiming={VrLocomotion.Aiming} hasHit={VrLocomotion.HasHit} collider={VrLocomotion.LastHit.collider?.name} normal={VrLocomotion.LastHit.normal} slope={player.slopeLimit} ray={VrLocomotion.AimRay}");
                if(!VrLocomotion.TargetValid){player.GetCapsulePointsAt(VrLocomotion.Destination,out var top,out var bottom);Plugin.Line($"VR_TELEPORT_CAPSULE destination={VrLocomotion.Destination} p1={top} p2={bottom} radius={player.capsuleCollider.radius} ignore={player.ignoreLayers.value} ground={player.groundLayers.value}");foreach(var c in Physics.OverlapCapsule(top,bottom,player.capsuleCollider.radius,~player.ignoreLayers,QueryTriggerInteraction.Ignore))Plugin.Line($"VR_TELEPORT_OVERLAP {c.name} layer={c.gameObject.layer} bounds={c.bounds}");}
                Check(VrLocomotion.Aiming&&VrLocomotion.TargetValid,"Floor teleport target invalid");
                Check(Vector3.Distance(start,player.transform.position)<.05f,"Teleport aim walked player");Send("shot vr-teleport-valid\nstick l 0 0");break;
            case 18:
                Check(Vector3.Distance(start,player.transform.position)>1,"Release did not teleport");Check(!VrLocomotion.Aiming,"Teleport aim remained latched");
                goodTeleportPosition=player.transform.position;Plugin.Line("VR_TELEPORT_PASS destination="+player.transform.position);VrSettings.Teleport.Value=false;
                pad=InputSystem.AddDevice("Gamepad").Cast<Gamepad>();QueuePad(Vector2.zero,Vector2.zero);VrSettings.Gamepad.Value=true;InputSystem.DisableDevice(pad,true);pad.disabledWhileInBackground=true;
                var input=Core.Input.InputManager.Instance;foreach(var action in new[]{input.MoveAction,input.JumpAction,input.InteractAction,input.PrimaryAction,input.CancelAction,input.SubmitAction}){Plugin.Line("VR_GAMEPAD_ACTION "+action.name+" enabled="+action.enabled);var bindings=action.bindings;for(int i=0;i<bindings.Count;++i)Plugin.Line("VR_GAMEPAD_BINDING "+bindings[i].effectivePath+" groups="+bindings[i].groups);}break;
            case 19:
                Check(!MotionInput.Enabled,"Motion gameplay remained enabled in gamepad mode");Check(pad.enabled,"Focused VR did not recover background-disabled gamepad");yaw=player.targetHorizontalRotation;QueuePad(Vector2.zero,new Vector2(.8f,0));delay=1;break;
            case 20:
                Plugin.Line($"VR_GAMEPAD_TURN delta={Mathf.DeltaAngle(yaw,player.targetHorizontalRotation)} read={pad.rightStick.ReadValue()} scheme={Core.Input.InputManager.Instance.CurrentControlScheme}");
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,player.targetHorizontalRotation))>40,"Gamepad did not turn");start=player.transform.position;QueuePad(new Vector2(0,.8f),Vector2.zero);delay=1;break;
            case 21:
                Plugin.Line($"VR_GAMEPAD_MOVE native={Core.Input.InputManager.Instance.MoveInput} player={player.moveInput} displacement={Vector3.Distance(start,player.transform.position)}");Check(Core.Input.InputManager.Instance.MoveInput.y>.5f,"Native gamepad movement action missing");QueuePad(Vector2.zero,Vector2.zero);break;
            case 22:
                var actions=Core.Input.InputManager.Instance;actions.add_OnInteractPressed((Action)(()=>interactions++));actions.add_OnJumpPressed((Action)(()=>jumps++));actions.add_OnPrimaryPressed((Action)(()=>uses++));QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.West);break;
            case 23:Check(interactions==1,"Gamepad X native interaction missing");QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.South);break;
            case 24:Check(jumps==1,"Gamepad A native jump missing");QueuePad(Vector2.zero,Vector2.zero,trigger:1);break;
            case 25:Check(uses==1&&Core.Input.InputManager.Instance.IsPrimaryHeld,"Gamepad RT native use/hold missing");QueuePad(Vector2.zero,Vector2.zero);break;
            case 26:Check(!Core.Input.InputManager.Instance.IsPrimaryHeld,"Gamepad RT release missing");slot=InventoryManager.LocalInstance.currentSelectedSlot;QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.RightShoulder);break;
            case 27:var nextAction=Core.Input.InputManager.Instance.playerMap.FindAction("Next",false);Plugin.Line($"VR_NEXT_TRACE action={nextAction?.name} enabled={nextAction?.enabled} bindings={nextAction?.bindings.Count} padShoulder={pad.rightShoulder.ReadValue()} nativeControlCount={nextAction?.controls.Count}");if(nextAction!=null)for(int bi=0;bi<nextAction.bindings.Count;bi++)Plugin.Line("VR_NEXT_BIND "+nextAction.bindings[bi].effectivePath);Plugin.Line($"VR_GAMEPAD_SLOT expected={(slot+1)%InventoryManager.LocalInstance.totalSlots} actual={InventoryManager.LocalInstance.currentSelectedSlot} inventory={InventoryManager.LocalInstance.inventoryEnabled}");Check(InventoryManager.LocalInstance.currentSelectedSlot==(slot+1)%InventoryManager.LocalInstance.totalSlots,"Gamepad next inventory slot failed");QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.Start);break;
            case 28:Check(HUDManager.LocalInstance.IsEscapeMenuOpen,"Gamepad Start did not open pause menu");QueuePad(Vector2.zero,Vector2.zero);HUDManager.LocalInstance.OpenSettingsPanel();page=VrSettingsMenu.ForPanel(HUDManager.LocalInstance.root);Check(page!=null,"No gamepad VR page");page.Tab.Focus();break;
            case 29:QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.South);break;
            case 30:Check(page.Open,"Gamepad A did not open focused VR tab");QueuePad(Vector2.zero,Vector2.zero);page.Presets[0].Focus();break;
            case 31:QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.South);break;
            case 32:Check(VrSettings.Resolution.Value==65,"Gamepad A did not activate resolution preset");QueuePad(Vector2.zero,Vector2.zero);Send("shot vr-gamepad-settings");break;
            case 33:VrSettings.Resolution.Value=100;HUDManager.LocalInstance.CloseSettingsPanel();HUDManager.LocalInstance.HideEscapeMenu();player.Teleport(goodTeleportPosition,Quaternion.Euler(0,141.9f,0));VrSettings.Teleport.Value=true;delay=2;QueuePad(Vector2.zero,Vector2.zero);phase=329;break;
            case 330:
                start=player.transform.position;var headTarget=ClearFloor();headAimTarget=headTarget;StereoTrial.TryHeadWorld(out var headOrigin,out _);StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out _,out var anchor);var localDirection=Quaternion.Inverse(anchor)*(headTarget-headOrigin);Plugin.Line($"VR_HEAD_TELEPORT_FIXTURE head={headOrigin} root={player.transform.position} target={headTarget} local={localDirection}");
                Send(FormattableString.Invariant($"head rot {-Mathf.Atan2(localDirection.x,localDirection.z)*Mathf.Rad2Deg:R} {Mathf.Atan2(localDirection.y,Mathf.Sqrt(localDirection.x*localDirection.x+localDirection.z*localDirection.z))*Mathf.Rad2Deg:R} 0"));break;
            case 331:QueuePad(new Vector2(0,.9f),Vector2.zero);break;
            case 332:Plugin.Line($"VR_HEAD_TELEPORT_TRACE aiming={VrLocomotion.Aiming} valid={VrLocomotion.TargetValid} ray={VrLocomotion.AimRay} end={VrLocomotion.AimEnd} collider={VrLocomotion.LastHit.collider?.name} normal={VrLocomotion.LastHit.normal} target={headAimTarget} angle={Vector3.Angle(VrLocomotion.AimRay.direction,headAimTarget-VrLocomotion.AimRay.origin)} cursor={Cursor.lockState}");Check(VrLocomotion.Aiming&&VrLocomotion.TargetValid,"Gamepad head teleport target invalid");Check(Vector3.Distance(start,player.transform.position)<.05f,"Gamepad teleport aim walked player");QueuePad(Vector2.zero,Vector2.zero);break;
            case 333:Check(Vector3.Distance(start,player.transform.position)>1,"Gamepad release did not teleport");Plugin.Line("VR_GAMEPAD_HEAD_TELEPORT_PASS");Check(!MotionPointer.DiagnosticBeam,"Gamepad teleport beam remained visible after release");VrSettings.Teleport.Value=false;QueuePad(Vector2.zero,Vector2.zero);Send("head rot 0 0 0");UnityEngine.Object.FindObjectOfType<KitchenOrderDisplay>().OnInteract();delay=1.5f;break;
            case 334:Check(MachineInteractionBase.IsLocalPlayerInMachineMode&&!MachineInteractionBase.isLocalMachineTransitionInProgress,"Gamepad computer fixture did not enter");QueuePad(Vector2.zero,Vector2.zero,1u<<(int)GamepadButton.East);delay=1.5f;break;
            case 335:Check(!MachineInteractionBase.IsLocalPlayerInMachineMode&&player.canLook&&Cursor.lockState==CursorLockMode.Locked,"Gamepad B did not restore native computer ownership");Plugin.Line("VR_GAMEPAD_MACHINE_EXIT_PASS");QueuePad(Vector2.zero,Vector2.zero);VrSettings.Teleport.Value=true;start=player.transform.position;Send("head rot 0 90 0");break;
            case 336:QueuePad(new Vector2(0,.9f),Vector2.zero);break;
            case 337:Check(VrLocomotion.Aiming&&!VrLocomotion.TargetValid,"Upward teleport target unexpectedly valid");QueuePad(Vector2.zero,Vector2.zero);break;
            case 338:Check(Vector3.Distance(start,player.transform.position)<.05f,"Invalid target release moved player");Plugin.Line("VR_TELEPORT_INVALID_RELEASE_PASS");VrSettings.Teleport.Value=false;VrSettings.Gamepad.Value=false;Send("head rot 0 0 0");phase=33;break;
            case 34:
                InputSystem.RemoveDevice(pad);pad=null;Check(MotionInput.Enabled,"Motion mode did not recover");Send("shot vr-gameplay-final");Plugin.Line("VR_GAMEPLAY_AUDIT_COMPLETE");phase=99;return;
        }phase++;next=Time.realtimeSinceStartup+delay;
        }catch(Exception e){Plugin.Line("VR_GAMEPLAY_AUDIT_FAILED phase="+phase+" "+e);Send("input clear");if(pad!=null){QueuePad(Vector2.zero,Vector2.zero);InputSystem.RemoveDevice(pad);pad=null;}phase=99;}
    }
}
