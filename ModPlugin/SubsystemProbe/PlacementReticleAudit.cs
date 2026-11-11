using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Text.Json;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class PlacementReticleAudit
{
    private static readonly Dictionary<int,object> projections=new();
    internal static void Projected(Vector3 world,Vector3 local,Vector2 center,Matrix4x4 matrix)
    {
        if(!enabled||phase>14)return;
        var clip=matrix*new Vector4(center.x,center.y,0,1);
        projections[EyeScreenUI.CurrentEye]=new{phase,frame=Time.frameCount,eye=EyeScreenUI.CurrentEye,world=new[]{world.x,world.y,world.z},local=new[]{local.x,local.y,local.z},ndc=new[]{clip.x/clip.w,clip.y/clip.w},viewport=new[]{2064,2208}};
    }
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_PLACEMENT_RETICLE_AUDIT")=="1";
    private static readonly bool release=Environment.GetEnvironmentVariable("IKAG_XR_GRIP_PLACEMENT_AUDIT")=="1";
    private static int phase;
    private static float next=20;
    private static InteractionManager interaction;
    private static CargoBox box;
    private static Vector3 target;
    private static int placedBefore;
    private static int rotationStep;
    private static float rotationAt,rotationBefore;
    internal static bool RotateFixture(BuildingManager building,string label)
    {
        if(rotationStep>=3)return false;
        if(Time.realtimeSinceStartup<rotationAt)return true;
        if(!building.HasActivePreview)throw new InvalidOperationException("Rotation fixture lacks native preview");
        float yaw=building.currentPreviewObject.transform.eulerAngles.y;
        string command;
        if(rotationStep==0){rotationBefore=yaw;command="trigger l 1";}
        else
        {
            float turn=Mathf.DeltaAngle(rotationBefore,yaw);
            Plugin.Line($"PLACEMENT_ROTATION fixture={label} trigger={(rotationStep==1?"LT":"RT")} degrees={turn:R}");
            if(rotationStep==1?turn>=-10:turn<=10)throw new InvalidOperationException("Placement trigger failed direction/magnitude check");
            rotationBefore=yaw;command=rotationStep==1?"trigger l 0\ntrigger r 1":"trigger r 0";
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
        ++rotationStep;rotationAt=Time.realtimeSinceStartup+.5f;return true;
    }
    internal static void Tick()
    {
        if(!enabled||phase>14||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;
            if(phase==3&&RotateFixture(BuildingManager.Instance,"cargo")){next=Time.realtimeSinceStartup+.1f;return;}
            if(phase==0)
            {
                UiInputAudit.Dump("Assembly-CSharp.dll","","InteractionManager","HandleInteractPressed","HandlePrimaryPressed","HandleInteraction","HandlePreviewSystem","HandleIngredientPlacement","TryShowPizzaBoxPreview");
                UiInputAudit.Dump("Assembly-CSharp.dll","","HUDManager","SetCrosshairVisible","ApplyCrosshairSettings","Update");
                UiInputAudit.Dump("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements.UIR","RenderChainCommand","ExecuteNonDrawMesh");
                UiInputAudit.Dump("Assembly-CSharp.dll","","CargoBox","Update","EnterPlacementMode","ExitPlacementMode","TryPlaceMachine");
                UiInputAudit.Dump("Assembly-CSharp.dll","","BuildingManager","Update","TryPlaceMachine","HandleReplacementMode");
                interaction=InteractionManager.LocalInstance;
                placedBefore=BuildingManager.Instance.GetTotalPlacedMachinesCount();
                var player=interaction.movementController;var inv=InventoryManager.LocalInstance;
                inv.SetSelectedSlot(7);
                BuildableCounter counter=null;Collider collider=null;float distance=float.PositiveInfinity;
                foreach(var c in UnityEngine.Object.FindObjectsOfType<BuildableCounter>())
                {
                    if(!c.AcceptsPlacedObjects)continue;
                    var col=c.GetComponent<Collider>();if(col==null)col=c.GetComponentInChildren<Collider>();if(col==null)continue;
                    float d=(col.bounds.m_Center-player.transform.position).sqrMagnitude;if(d<distance){distance=d;counter=c;collider=col;}
                }
                if(counter==null)throw new InvalidOperationException("No native placement counter");
                target=player.transform.position;
                var horizontal=Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;
                var position=target-horizontal*1.5f;position.y=player.transform.position.y;player.Teleport(position);
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<CargoBox>())
                    if(!candidate.transform.IsChildOf(player.transform))
                    {candidate.ConfigureFromMachineSO(counter.GetMachineSO());if(interaction.TryPickupItem(candidate.Cast<IHoldable>())){box=candidate;break;}}
                if(box==null)throw new InvalidOperationException("No cargo box fixture");
                Plugin.Line($"PLACE_FIXTURE box={box.name} target={counter.name}/{target:R} player={position:R}");
                var hud=HUDManager.LocalInstance;var dot=hud.crosshairDot;var cross=hud.crosshairElement;
                Plugin.Line($"RETICLE_CENSUS cross={cross?.name} hints={cross?.usageHints} bounds={cross?.worldBound} parent={cross?.parent?.name} dot={dot?.name}/{dot?.worldBound} document={hud.GetComponent<UnityEngine.UIElements.UIDocument>()!=null}");
                for(int i=0;i<cross.childCount;++i)Plugin.Line("RETICLE_CHILD "+cross.ElementAt(i).name);
                Plugin.Line($"COUNTER_INTERACTABLE {counter.TryCast<IInteractable>()!=null}");
                command="hands reset";
            }
            else if(phase==1)command=MotionPointer.AimAtWorld(target);
            else if(phase==2)
            {
                Report("before-b-enter");command="btn b press 100\nshot placement-before";
            }
            else if(phase==3){Report("after-b-enter");if(!box.isInPlacementMode)throw new InvalidOperationException("B did not enter native furniture mode");command="btn y press 100\nshot placement-preview";}
            else if(phase==4){Report("after-y-cancel");if(box.isInPlacementMode||interaction.heldItems.Count!=1)throw new InvalidOperationException("Y canceled/dropped incorrectly");command="btn b press 100";}
            else if(phase==5){Report("after-b-reenter");command=MotionPointer.AimAtWorld(target+Vector3.up*10)+(release?"\ngrip r 1":"");}
            else if(phase==6){Report("invalid-before-b");if(BuildingManager.Instance.isValidPlacement)throw new InvalidOperationException("Ceiling negative control is valid");command=release?"grip r 0":"btn b press 100";}
            else if(phase==7){Report("invalid-after-b");if(interaction.heldItems.Count!=1||BuildingManager.Instance.GetTotalPlacedMachinesCount()!=placedBefore)throw new InvalidOperationException("Invalid placement consumed furniture");command=MotionPointer.AimAtWorld(target)+(release?"\ngrip r 1":"");}
            else if(phase==8){Report("valid-before-b");if(!BuildingManager.Instance.isValidPlacement)throw new InvalidOperationException("Floor fixture invalid");command=(release?"grip r 0":"btn b press 100")+"\nshot placement-confirm";}
            else if(phase==9){Report("after-b-confirm");if(BuildingManager.Instance.GetTotalPlacedMachinesCount()!=placedBefore+1||interaction.heldItems.Count!=0)throw new InvalidOperationException("B did not commit native furniture transaction");command="head rot 20 0 0";}
            else if(phase==10)command="shot reticle-head-moved";
            else if(phase==11)command="hand r aimtrim -15 20";
            else if(phase==12)command="shot reticle-hand-moved";
            else if(phase==13)command="shot reticle-settled";
            else {Report("settled");Plugin.Line("PLACEMENT_RETICLE_AUDIT_COMPLETE");}
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"reticle-projection-"+phase+".json"),JsonSerializer.Serialize(projections));
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+1.5f;
        }
        catch(Exception e){Plugin.Line("PLACEMENT_RETICLE_AUDIT_FAILED "+e);phase=15;}
    }
    private static void Report(string stage)
    {
        var b=BuildingManager.Instance;
        Plugin.Line($"PLACE_OBSERVE stage={stage} held={interaction.heldItems.Count} boxAlive={box!=null} boxMode={(box!=null&&box.isInPlacementMode)} activePreview={b.HasActivePreview} valid={b.isValidPlacement} placed={b.GetTotalPlacedMachinesCount()} hit={interaction.hasHit}/{interaction.rayHit.point:R}/{interaction.rayHit.collider?.name}");
    }
}
