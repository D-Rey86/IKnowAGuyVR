using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionPlacementAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_PLACEMENT_AUDIT")=="1";
    private static int phase,placeCalls;private static float next=24;
    private static InteractionManager interaction;private static PlayerMovementController player;
    private static IBuildable target;private static Collider collider;private static Vector3 originalPlayer,originalTarget;
    private static PhotoCamera photo;private static PizzaBox box;private static Vector3 counterTarget;
    private static readonly bool transactions=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_TRANSACTION_AUDIT")=="1";
    private static readonly bool gripPlacement=Environment.GetEnvironmentVariable("IKAG_XR_GRIP_PLACEMENT_AUDIT")=="1";
    private static bool transactionGrip;
    private static int transactionPhase,sellCalls,candidate;
    private static float transactionNext=24;
    private static Vector3[] floorPoints;
    private static GameObject transactionObject;
    private static int machinesBeforeSale;
    private static Vector3 expectedPlacement;
    private static void SellCalled(IBuildable buildable){++sellCalls;Plugin.Line("SELL_NATIVE_CALLED "+buildable.GetGameObject().name);}
    private static void PlaceCalled(){++placeCalls;}
    internal static void Tick()
    {
        if(transactions){TransactionTick();return;}
        if(!enabled||phase>21||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            var building=BuildingManager.Instance;var inv=InventoryManager.LocalInstance;string command=null;
            if(phase==0)
            {
                new Harmony("ikag-placement-audit").Patch(typeof(BuildingManager).GetMethod("PlaceReplacedBuildable"),prefix:new HarmonyMethod(typeof(MotionPlacementAudit).GetMethod(nameof(PlaceCalled),BindingFlags.NonPublic|BindingFlags.Static)));
                interaction=InteractionManager.LocalInstance;player=interaction.movementController;originalPlayer=player.transform.position;inv.SetSelectedSlot(7);
                float distance=float.PositiveInfinity;
                foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    var b=m.TryCast<IBuildable>();if(b==null||!b.CanBePickedUp())continue;
                    var c=m.GetComponent<Collider>();if(c==null)c=m.GetComponentInChildren<Collider>();if(c==null)continue;
                    float d=(c.bounds.center-interaction.interactionRay.origin).sqrMagnitude;
                    if(d<distance){target=b;collider=c;distance=d;}
                }
                if(target==null)throw new InvalidOperationException("Build fixture missing");
                originalTarget=target.GetTransform().position;
                var delta=Vector3.ProjectOnPlane(collider.bounds.center-interaction.interactionRay.origin,Vector3.up);
                player.Teleport(originalPlayer+delta.normalized*Mathf.Max(0,delta.magnitude-1.2f));
            }
            else if(phase==1)command=MotionPointer.AimAtWorld(collider.bounds.center);
            else if(phase==2)command="btn b press 100";
            else if(phase==3)
            {
                if(!building.isReplacingMode)throw new InvalidOperationException("Native build fixture did not open");
                command=MotionPointer.AimAtWorld(originalTarget);
            }
            else if(phase==4)command="btn b press 100";
            else if(phase==5)
            {
                Plugin.Line($"BUILD_PLACE_CONTROL nativeCalls={placeCalls} replacing={building.isReplacingMode} valid={building.isValidPlacement} positionError={(target.GetTransform().position-originalTarget).magnitude:R}");
                if(placeCalls!=1)throw new InvalidOperationException("B did not call native build placement exactly once");
                if(building.isReplacingMode)building.CancelReplacement();
                command=MotionPointer.AimAtWorld(collider.bounds.center);
            }
            else if(phase==6)command="btn b down";
            else if(phase==7)
            {
                Plugin.Line($"SELL_HOLD_CONTROL held={Core.Input.InputManager.Instance.IsSellHeld} nativeHolding={building.isHoldingSellKey} duration={building.sellHoldDuration:R} sellable={BuildingManager.CanSell(target)}");
                command="btn b up";
            }
            else if(phase==8){player.Teleport(originalPlayer);inv.SetSelectedSlot(1);command="hands reset\ntrigger l 1";}
            else if(phase==9){photo=inv.GetItemInSlot(1).GetComponent<PhotoCamera>();command="trigger l 0\nstick r 0 0.8";}
            else if(phase==10)command="stick r 0 0\nhand r aimtrim 70 0";
            else if(phase==11){Plugin.Line($"PHOTO_ZOOM_CONTROL target={photo.targetZoom:R} actual={photo.currentZoom:R} fov={photo.captureCamera.fieldOfView:R}");command="trigger r 1";}
            else if(phase==12)command="trigger r 0";
            else if(phase==13)command="trigger l 1";
            else if(phase==14)
            {
                command="trigger l 0\nhands reset";inv.SetSelectedSlot(7);
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<PizzaBox>())
                    if(!candidate.transform.IsChildOf(player.transform)&&interaction.TryPickupItem(candidate.Cast<IHoldable>())){box=candidate;break;}
                Plugin.Line($"ITEM_PLACE_FIXTURE box={(box==null?"none":box.name)}");
                if(box!=null)
                {
                    BuildableCounter nearest=null;float distance=float.PositiveInfinity;
                    foreach(var counter in UnityEngine.Object.FindObjectsOfType<BuildableCounter>())
                    {
                        if(!counter.AcceptsPlacedObjects)continue;
                        var c=counter.GetComponent<Collider>();if(c==null)c=counter.GetComponentInChildren<Collider>();if(c==null)continue;
                        float d=(c.bounds.center-player.transform.position).sqrMagnitude;
                        if(d<distance){nearest=counter;collider=c;distance=d;}
                    }
                    if(nearest!=null)
                    {
                        counterTarget=collider.bounds.center+Vector3.up*collider.bounds.extents.y;
                        var delta=Vector3.ProjectOnPlane(counterTarget-interaction.interactionRay.origin,Vector3.up);
                        player.Teleport(player.transform.position+delta.normalized*Mathf.Max(0,delta.magnitude-1.2f));
                    }
                }
            }
            else if(phase==15&&box!=null)command=MotionPointer.AimAtWorld(counterTarget);
            else if(phase==16&&box!=null)
            {
                Plugin.Line($"ITEM_PREVIEW_CONTROL preview={(interaction.currentPreviewObject==null?"none":interaction.currentPreviewObject.name)} target={(interaction.lastInteractableForPreview==null?"none":interaction.lastInteractableForPreview.Pointer.ToString())} point={(interaction.lastPlacementPointForPreview==null?Vector3.zero:interaction.lastPlacementPointForPreview.position):R} armed={interaction.ingredientPlacementArmed} closest={(interaction.closestInteractable==null?"none":interaction.closestInteractable.name)}");
                command="btn b press 100";
            }
            else if(phase==17&&box!=null)Plugin.Line($"ITEM_PLACE_CONTROL held={interaction.heldItems.Count} position={box.transform.position:R} parent={(box.transform.parent==null?"none":box.transform.parent.name)}");
            else if(phase==18){if(interaction.IsHoldingItem())interaction.DropLastItem();player.Teleport(originalPlayer);inv.SetSelectedSlot(7);command="hands reset";}
            else if(phase==21)Plugin.Line("MOTION_PLACEMENT_AUDIT_COMPLETE");
            Plugin.Line($"PLACEMENT_PHASE {phase} valid={building.isValidPlacement} replacing={building.isReplacingMode}");
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+(phase==7?.7f:phase==3?building.pickupHoldDuration+.6f:1.2f);
        }
        catch(Exception e)
        {
            Plugin.Line("MOTION_PLACEMENT_AUDIT_FAILED "+e);
            var b=BuildingManager.Instance;if(b!=null&&b.isReplacingMode)b.CancelReplacement();
            if(player!=null)player.Teleport(originalPlayer);phase=22;
        }
    }
    private static void TransactionTick()
    {
        if(transactionPhase>10||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<transactionNext)return;
        try
        {
            var b=BuildingManager.Instance;string command=null;float delay=.5f;
            if(transactionPhase==0)
            {
                new Harmony("ikag-transaction-audit").Patch(typeof(BuildingManager).GetMethod("SellBuildable"),prefix:new HarmonyMethod(typeof(MotionPlacementAudit).GetMethod(nameof(SellCalled),BindingFlags.NonPublic|BindingFlags.Static)));
                interaction=InteractionManager.LocalInstance;player=interaction.movementController;originalPlayer=player.transform.position;InventoryManager.LocalInstance.SetSelectedSlot(7);
                float distance=float.PositiveInfinity;
                foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    var buildable=m.TryCast<IBuildable>();if(buildable==null||!buildable.CanBePickedUp())continue;
                    var c=m.GetComponent<Collider>();if(c==null)c=m.GetComponentInChildren<Collider>();if(c==null)continue;
                    float d=(c.bounds.center-interaction.interactionRay.origin).sqrMagnitude;
                    if(d<distance){target=buildable;collider=c;distance=d;}
                }
                if(target==null)throw new InvalidOperationException("No transaction fixture");
                originalTarget=target.GetTransform().position;transactionObject=target.GetGameObject();
                var delta=Vector3.ProjectOnPlane(collider.bounds.center-interaction.interactionRay.origin,Vector3.up);
                player.Teleport(originalPlayer+delta.normalized*Mathf.Max(0,delta.magnitude-1.2f));
                Plugin.Line($"TRANSACTION_FIXTURE name={transactionObject.name} bounds={collider.bounds:R} original={originalTarget:R} mask={b.placementRaycastMask} sellable={BuildingManager.CanSell(target)}");
            }
            else if(transactionPhase==1)command=MotionPointer.AimAtWorld(collider.bounds.center);
            else if(transactionPhase==2){command="btn b press 100";delay=b.pickupHoldDuration+.7f;}
            else if(transactionPhase==3)
            {
                if(!b.isReplacingMode)throw new InvalidOperationException("Pickup did not enter replacement mode");
                // Cold fixture search only: measure real support surfaces, then
                // let native preview/validity decide each candidate untouched.
                var points=new System.Collections.Generic.List<Vector3>();
                Vector3 toward=Vector3.ProjectOnPlane(player.transform.position-originalTarget,Vector3.up).normalized;
                Vector3 across=Vector3.Cross(Vector3.up,toward);
                for(int forward=0;forward<5;++forward)for(int side=-3;side<=3;++side)
                {
                    var p=originalTarget+toward*(forward*.4f)+across*(side*.4f);
                    if(Physics.Raycast(p+Vector3.up*2,Vector3.down,out var hit,5,b.placementRaycastMask,QueryTriggerInteraction.Ignore)&&hit.normal.y>=b.minSurfaceUpAngle)points.Add(hit.point);
                }
                floorPoints=points.ToArray();candidate=0;
                Plugin.Line($"TRANSACTION_SUPPORTS count={floorPoints.Length} bounds={b.previewLocalBounds:R}");
                if(floorPoints.Length==0)throw new InvalidOperationException("No measured support points");
                command=MotionPointer.AimAtWorld(floorPoints[0]);
            }
            else if(transactionPhase==4)
            {
                Plugin.Line($"TRANSACTION_PREVIEW candidate={candidate} aim={floorPoints[candidate]:R} preview={b.currentPreviewObject.transform.position:R} valid={b.isValidPlacement} ray={interaction.interactionRay.origin:R}/{interaction.interactionRay.direction:R}");
                if(!b.isValidPlacement)
                {
                    if(++candidate>=floorPoints.Length)throw new InvalidOperationException("Native validator rejected all measured support fixtures");
                    command=MotionPointer.AimAtWorld(floorPoints[candidate]);--transactionPhase;
                }
                else
                {
                    expectedPlacement=b.currentPreviewObject.transform.position;
                    if(gripPlacement&&!transactionGrip){transactionGrip=true;command="grip r 1";--transactionPhase;}
                    else command=gripPlacement?"grip r 0":"btn b press 100";
                }
            }
            else if(transactionPhase==5)
            {
                float error=(target.GetTransform().position-expectedPlacement).magnitude;
                Plugin.Line($"TRANSACTION_PLACED replacing={b.isReplacingMode} expected={expectedPlacement:R} actual={target.GetTransform().position:R} error={error:R}");
                if(b.isReplacingMode||error>.001f)throw new InvalidOperationException("Native placement did not commit");
                command=MotionPointer.AimAtWorld(collider.bounds.center);
            }
            else if(transactionPhase==6)
            {
                TransactionSaleState(b);
                // The measured rejected ray intersects Player_0 at ~0.12 m:
                // this fixture aims backward through the capsule. Turn the
                // simulated head and following hands toward the fixture while
                // preserving native collision rules, player position and reach.
                command="head rot 180 0 0";
            }
            else if(transactionPhase==7)
            {
                command=MotionPointer.AimAtWorld(collider.bounds.center);
            }
            else if(transactionPhase==8){TransactionSaleState(b);machinesBeforeSale=b.GetTotalPlacedMachinesCount();command="btn b down";delay=b.sellHoldDuration+1.3f;}
            else if(transactionPhase==9)
            {
                TransactionSaleState(b);
                command="btn b up";
                bool retired=transactionObject==null||!transactionObject.activeSelf;
                Plugin.Line($"TRANSACTION_SOLD nativeCalls={sellCalls} objectRetired={retired} machinesBefore={machinesBeforeSale} machinesAfter={b.GetTotalPlacedMachinesCount()}");
                if(sellCalls!=1||!retired)throw new InvalidOperationException("Native sale did not retire the fixture");
            }
            else if(transactionPhase==10){player.Teleport(originalPlayer);command="hands reset\nhead rot 0 0 0";Plugin.Line("MOTION_TRANSACTION_AUDIT_COMPLETE");}
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++transactionPhase;transactionNext=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e)
        {
            Plugin.Line("MOTION_TRANSACTION_AUDIT_FAILED "+e);
            var b=BuildingManager.Instance;if(b!=null&&b.isReplacingMode)b.CancelReplacement();
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),"btn b up\nstick r 0 0\n");
            if(player!=null)player.Teleport(originalPlayer);transactionPhase=11;
        }
    }
    private static void TransactionSaleState(BuildingManager b)
    {
        var looking=b.currentLookingAtBuildable;
        var r=b.GetInteractionRay();
        bool hit=Physics.Raycast(r,out var h,10,b.buildableLayerMask,QueryTriggerInteraction.UseGlobal);
        Plugin.Line($"TRANSACTION_SELL_STATE focus={Probe.Focused} cursor={Cursor.lockState} held={Core.Input.InputManager.Instance.IsSellHeld} nativeHold={b.isHoldingSellKey} target={(looking==null?"none":looking.GetGameObject().name)} intended={(transactionObject==null?"destroyed":transactionObject.name)} sellable={(transactionObject!=null&&BuildingManager.CanSell(target))} collider={(collider==null?"destroyed":collider.enabled+"/"+collider.gameObject.activeInHierarchy)} distance={(collider==null?-1:(collider.bounds.center-interaction.interactionRay.origin).magnitude):R} reach={interaction.rayDistance:R}/{b.maxRaycastDistance:R} hit={(hit?h.collider.name:"none")} hitDistance={(hit?h.distance:-1):R} ray={r.origin:R}/{r.direction:R}");
    }
}
