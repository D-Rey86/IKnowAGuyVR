using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionFocusedAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_FOCUSED_AUDIT")=="1";
    private static int phase;private static float next=24;
    private static InteractionManager interaction;private static PlayerMovementController player;
    private static IBuildable target;private static Collider collider;
    private static Vector3 originalPlayer,originalTarget;private static Quaternion originalRotation;
    private static Vector3 previewBefore;private static float playerYawBefore;
    internal static void Tick()
    {
        if(!enabled||phase>17||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;var building=BuildingManager.Instance;var inv=InventoryManager.LocalInstance;
            if(phase==4&&PlacementReticleAudit.RotateFixture(building,"relocation")){next=Time.realtimeSinceStartup+.1f;return;}
            if(phase==0)
            {
                interaction=InteractionManager.LocalInstance;player=interaction.movementController;
                inv.SetSelectedSlot(7);originalPlayer=player.transform.position;
                float distance=float.PositiveInfinity;
                foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    var b=m.TryCast<IBuildable>();if(b==null||!b.CanBePickedUp())continue;
                    var c=m.GetComponent<Collider>();if(c==null)c=m.GetComponentInChildren<Collider>();if(c==null)continue;
                    float d=(c.bounds.center-interaction.interactionRay.origin).sqrMagnitude;
                    if(d<distance){target=b;collider=c;distance=d;}
                }
                if(target==null)throw new InvalidOperationException("No native buildable found");
                originalTarget=target.GetTransform().position;originalRotation=target.GetTransform().rotation;
                // The first action audit was correctly rejected at 5.3 m.
                // Use native Teleport to arrange a 1.2 m horizontal fixture.
                Vector3 delta=Vector3.ProjectOnPlane(collider.bounds.center-interaction.interactionRay.origin,Vector3.up);
                player.Teleport(originalPlayer+delta.normalized*Mathf.Max(0,delta.magnitude-1.2f));
                Plugin.Line($"BUILD_FIXTURE name={target.GetGameObject().name} maxDistance={building.maxRaycastDistance:R} pickupDuration={building.pickupHoldDuration:R}");
            }
            else if(phase==1)command=MotionPointer.AimAtWorld(collider.bounds.center);
            else if(phase==2)command="btn b press 100";
            else if(phase==3)command=null;
            else if(phase==4)
            {
                if(!building.isReplacingMode)throw new InvalidOperationException("Native build pickup did not enter replacement mode");
                playerYawBefore=Camera.main.transform.eulerAngles.y;previewBefore=building.currentPreviewObject.transform.eulerAngles;command="stick r 0.5 0";
            }
            else if(phase==5)
            {
                float yaw=Mathf.Abs(Mathf.DeltaAngle(playerYawBefore,Camera.main.transform.eulerAngles.y));
                float itemTurn=Mathf.Abs(Mathf.DeltaAngle(previewBefore.y,building.currentPreviewObject.transform.eulerAngles.y));
                if(yaw<10||itemTurn>.1f)throw new Exception($"stick yaw={yaw} item={itemTurn}");
                Plugin.Line($"POLISH_PLACEMENT_PASS playerYaw={yaw:R} itemYaw={itemTurn:R}");
                Plugin.Line($"BUILD_ROTATION_CONTROL before={previewBefore:R} after={building.currentPreviewObject.transform.eulerAngles:R}");
                command="stick r 0 0\nbtn y press 100";
            }
            else if(phase==6)
            {
                Plugin.Line($"BUILD_CANCEL_CONTROL replacing={building.isReplacingMode} positionError={(target.GetTransform().position-originalTarget).magnitude:R} rotationError={Quaternion.Angle(target.GetTransform().rotation,originalRotation):R}");
                player.Teleport(originalPlayer);inv.SetSelectedSlot(1);command="hands reset";
            }
            else if(phase==7)command="hand r aimtrim 70 0\ntrigger l 1";
            else if(phase==8)command="trigger l 0\ntrigger r 1";
            else if(phase==9)command="trigger r 0";
            else if(phase==10)command="trigger l 1\nhead rot 0 -65 0\nhand r grip pose 0.15 1.45 -0.35 20 15 -10";
            else if(phase==11)command="trigger l 0\nshot motion-held-aligned";
            else if(phase==12){inv.SetSelectedSlot(7);command="hands reset\nhead rot 0 0 0";}
            else if(phase==13)command="focus lose\ntrigger r 1\nbtn x down";
            else if(phase==14)command="focus regain";
            else if(phase==15)command="trigger r 0\nbtn x up";
            else if(phase==16)command="shot motion-focus-restored";
            else if(phase==17)Plugin.Line("MOTION_FOCUSED_AUDIT_COMPLETE");
            Plugin.Line($"FOCUSED_CONTROL phase={phase} buildHeld={Core.Input.InputManager.Instance.IsBuildModeHeld} replacing={building.isReplacingMode} holding={building.isHoldingFKey} progress={building.GetPickupHoldProgress():R} target={(building.currentLookingAtBuildable==null?"none":building.currentLookingAtBuildable.GetGameObject().name)} ray={interaction.interactionRay.origin:R}/{interaction.interactionRay.direction:R} punchAt={interaction.lastPunchTime:R}");
            MotionInput.Trace();
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+(phase==4?building.pickupHoldDuration+.5f:1.2f);
        }
        catch(Exception e)
        {
            Plugin.Line("MOTION_FOCUSED_AUDIT_FAILED phase="+phase+" "+e);
            var building=BuildingManager.Instance;if(building!=null&&building.isReplacingMode)building.CancelReplacement();
            if(player!=null)player.Teleport(originalPlayer);
            phase=18;
        }
    }
}
