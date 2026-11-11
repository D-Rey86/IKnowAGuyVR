using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionActionAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_ACTION_AUDIT")=="1";
    private static int phase;private static float next=24;
    private static InteractionManager interaction;private static PlayerMovementController player;
    private static KitchenOrderDisplay kitchen;
    private static PhotoCamera photo;private static IHoldable savedItem;
    internal static void Tick()
    {
        if(!enabled||phase>31||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;
            var input=Core.Input.InputManager.Instance;var inv=InventoryManager.LocalInstance;var hud=HUDManager.LocalInstance;
            if(phase==0)
            {
                interaction=InteractionManager.LocalInstance;player=interaction.movementController;
                kitchen=UnityEngine.Object.FindObjectOfType<KitchenOrderDisplay>();
                var collider=kitchen.GetComponent<Collider>();if(collider==null)collider=kitchen.GetComponentInChildren<Collider>();
                Plugin.Line($"ACTION_NATIVE_LISTENERS build={(input.OnBuildModePressed==null?0:input.OnBuildModePressed.GetInvocationList().Length)} sprint={(input.OnSprintChanged==null?0:input.OnSprintChanged.GetInvocationList().Length)}");
                command=MotionPointer.AimAtWorld(collider.bounds.center);
            }
            else if(phase==1)command="btn x down";
            else if(phase==2)command="btn x up";
            else if(phase==3){kitchen.TriggerExit();command="hand r aimtrim 70 0";}
            else if(phase==4)command="btn a down";
            else if(phase==5)command="btn a up";
            else if(phase==6)command="click l down\nstick l 0 0.6";
            else if(phase==7)command="click l up\nstick l 0 0";
            else if(phase==8)command="trigger r 1";
            else if(phase==9)command="trigger r 0\nclick r down";
            else if(phase==10)command="click r up";
            else if(phase==11)command="btn b down";
            else if(phase==12){command="btn b up";}
            else if(phase==13)
            {
                hud.CloseRecipeBookIfOpen();
                // Cold diagnostic selection; native build targeting and pickup
                // still own the actual result after this controller ray command.
                MonoBehaviour nearest=null;float distance=float.PositiveInfinity;
                foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    var b=m.TryCast<IBuildable>();if(b==null||!b.CanBePickedUp())continue;
                    var c=m.GetComponent<Collider>();if(c==null)c=m.GetComponentInChildren<Collider>();if(c==null)continue;
                    float d=(c.bounds.center-interaction.interactionRay.origin).sqrMagnitude;
                    if(d<distance){nearest=m;distance=d;}
                }
                if(nearest==null)throw new InvalidOperationException("No native buildable found");
                var collider=nearest.GetComponent<Collider>();if(collider==null)collider=nearest.GetComponentInChildren<Collider>();
                Plugin.Line($"BUILD_TARGET_CONTROL name={nearest.name} distance={Mathf.Sqrt(distance):R}");
                command=MotionPointer.AimAtWorld(collider.bounds.center)+"\nbtn b press 100";
            }
            else if(phase==14)command=null;
            else if(phase==15)command="stick r 0.5 0";
            else if(phase==16)command="stick r 0 0\nbtn b press 100";
            else if(phase==17)
            {
                inv.SetSelectedSlot(1);command="hands reset";
            }
            else if(phase==18)
            {
                photo=inv.GetItemInSlot(1).GetComponent<PhotoCamera>();savedItem=interaction.GetHeldItem();
                var config=interaction.GetHoldingConfiguration(photo.gameObject);
                Plugin.Line($"HELD_CONFIG camera position={config.position:R} rotation={config.rotation:R} scale={config.scaleModifier:R}");
                foreach(var renderer in photo.GetComponentsInChildren<Renderer>(true))Plugin.Line($"ITEM_GEOMETRY name={renderer.name} center={renderer.bounds.center:R} size={renderer.bounds.size:R} local={renderer.transform.localPosition:R}");
                foreach(var t in photo.GetComponentsInChildren<Transform>(true))Plugin.Line($"ITEM_CHILD {t.name} local={t.localPosition:R} rotation={t.localRotation:R}");
                command="trigger l 1";
            }
            else if(phase==19)
            {
                var r=ControllerTransport.Right;StereoTrial.TryTrackingPose(r.Position,r.Rotation,out var grip,out _);
                Plugin.Line($"HELD_ROOT_CONTROL error={(photo.transform.position-grip).magnitude:R} root={photo.transform.position:R} grip={grip:R}");
                command="trigger l 0\nhand r aimtrim 70 0\ntrigger r 1";
            }
            else if(phase==20)command="trigger r 0";
            else if(phase==21)command="trigger l 1";
            else if(phase==22)command="trigger l 0\nbtn y press 100";
            else if(phase==23){Plugin.Line("PLACE_RESULT held="+interaction.heldItems.Count);interaction.TryPickupItem(savedItem);}
            else if(phase==24)command="btn y down";
            else if(phase==25)command="btn y up";
            else if(phase==26){Plugin.Line("DROP_RESULT held="+interaction.heldItems.Count);interaction.TryPickupItem(savedItem);}
            else if(phase==27)inv.SetSelectedSlot(0);
            else if(phase==28)command="trigger r 1";
            else if(phase==29)command="trigger r 0";
            else if(phase==30)command="trigger r 1";
            else if(phase==31){inv.SetSelectedSlot(2);command="trigger r 0";Plugin.Line("MOTION_ACTION_AUDIT_COMPLETE");}
            var building=BuildingManager.Instance;
            Plugin.Line($"ACTION_CONTROL phase={phase} machine={MachineInteractionBase.IsLocalPlayerInMachineMode} closest={(interaction.closestInteractable==null?"none":interaction.closestInteractable.name)} jump={player.isJumping}/{player.isAirborne} sprint={player.sprintHeld}/{player.isSprinting} speed={player.currentSpeed:R} punchAt={interaction.lastPunchTime:R} recipe={hud.IsRecipeBookOpen} buildHeld={input.IsBuildModeHeld} replacing={(building!=null&&building.isReplacingMode)} buildHolding={(building!=null&&building.isHoldingFKey)} sellHolding={(building!=null&&building.isHoldingSellKey)} held={interaction.heldItems.Count} slot={inv.currentSelectedSlot} photo={(photo!=null&&photo.isInPhotoMode)} photos={(photo==null?0:photo.GetPhotoCount())} captureCamera={(photo==null||photo.captureCamera==null?"none":photo.captureCamera.name)} skating={player.isSkating}");
            MotionInput.Trace();
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+(phase==5?.15f:1.1f);
        }
        catch(Exception e){Plugin.Line("MOTION_ACTION_AUDIT_FAILED "+e);phase=32;}
    }
}
