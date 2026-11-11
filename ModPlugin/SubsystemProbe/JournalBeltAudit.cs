using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;

// Isolated, opt-in measurements and controller acceptance; excluded from release.
internal static class JournalBeltAudit
{
    private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_JOURNAL_BELT_AUDIT");
    private static int phase,slot=-1,empty=-1;private static float next=25;
    private static InteractionManager owner;private static InventoryManager inventory;private static CargoBox box;private static Vector3 start,normal,nativeScale;
    private static float yaw,distance;private static int selected;
    private static ImmersiveHud.Surface Board=>(ImmersiveHud.Surface)typeof(ImmersiveHud).GetField("board",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    private static object Slot=>((Array)typeof(BeltModels).GetField("slots",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).GetValue(slot);
    private static Vector3 LoopCenter=>((Transform)typeof(BeltModels).GetField("rootTransform",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).TransformPoint((Vector3)Slot.GetType().GetField("Position",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Slot));
    private static float BoardDistance{get{StereoTrial.TryHeadWorld(out var h,out _);return (Board.Center-h).magnitude;}}
    private static void Check(bool v,string s){if(!v)throw new Exception(s);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static Bounds MeshBounds(GameObject source)
    {
        Bounds bounds=default;bool have=false;
        foreach(var r in source.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
            var b=mesh.bounds;var matrix=source.transform.worldToLocalMatrix*r.transform.localToWorldMatrix;
            for(int i=0;i<8;i++)
            {
                var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                if(!have){bounds=new Bounds(p,Vector3.zero);have=true;}else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }
    private static Vector3 Measure()
    {
        var entries=(Array)typeof(BeltModels).GetField("slots",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var entry=entries.GetValue(slot);var root=(GameObject)entry.GetType().GetField("Root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry);
        Check(root!=null,"Missing box miniature");var bounds=MeshBounds(root);
        Plugin.Line("JOURNAL_BELT_SIZE source="+box.name+" sourceScale="+box.transform.lossyScale.ToString("R")+" miniScale="+root.transform.lossyScale.ToString("R")+" miniLocalBounds="+bounds.size.ToString("R")+" miniWorldSize="+Vector3.Scale(bounds.size,root.transform.lossyScale).ToString("R"));
        foreach(var renderer in box.GetComponentsInChildren<Renderer>(true))
        {
            var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
            Plugin.Line("JOURNAL_BELT_PART name="+renderer.name+" type="+renderer.GetIl2CppType().Name+" active="+renderer.gameObject.activeSelf+" scale="+renderer.transform.lossyScale.ToString("R")+" meshBounds="+mesh?.bounds.size.ToString("R"));
        }
        return Vector3.Scale(bounds.size,root.transform.lossyScale);
    }
    private static string Palm(bool left)
    {
        var hand=left?ControllerTransport.Left:ControllerTransport.Right;
        Check(StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var world,out var rotation),"Hand unavailable");
        Check(MotionBody.TryPalmGeometry(owner,left,out var palm,out _,out _,out _),"Palm unavailable");
        var worldFromRoom=rotation*Quaternion.Inverse(hand.Rotation);
        var p=hand.Position+Quaternion.Inverse(worldFromRoom)*(LoopCenter-world)-palm;
        return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");
    }
    internal static void Tick()
    {
        if(mode==null||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        if(mode=="left"){LeftTick();return;}
        try
        {
            float delay=.65f;
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;inventory=InventoryManager.LocalInstance;
                    for(int i=0;i<inventory.totalSlots;i++)if(inventory.IsSlotEmpty(i)){if(slot<0)slot=i;else empty=i;}
                    Check(slot>=0&&empty>=0,"Need two empty fixture slots");
                    foreach(var c in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(c.CanPickup()&&!c.IsMachineBox()&&!c.IsDecorationBox()){box=c;break;}
                    if(box==null){var prefabs=Resources.FindObjectsOfTypeAll<CargoBox>();foreach(var c in prefabs)if(!c.IsMachineBox()&&!c.IsDecorationBox()){var clone=UnityEngine.Object.Instantiate(c.gameObject);clone.SetActive(true);clone.GetComponent<NetworkObject>().Spawn();box=clone.GetComponent<CargoBox>();break;}}
                    Check(box!=null,"No ingredient cargo box");inventory.SetSelectedSlot(slot);Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Box pickup");
                    inventory.SetSelectedSlot(empty);nativeScale=box.transform.localScale;Send("input clear\nhands reset");break;
                case 1:
                    var size=Measure();Check(size.x<=.0711f&&size.y<=.0701f&&size.z<=.0611f,"Box outside loop envelope");
                    Check(Mathf.Max(size.x/.071f,Mathf.Max(size.y/.07f,size.z/.061f))>.99f,"Box does not fill its loop");
                    Check((box.transform.localScale-nativeScale).sqrMagnitude<1e-10f,"Native box resized");
                    Plugin.Line("JOURNAL_BELT_FIT_PASS");Send(MotionPointer.AimAtWorld(ImmersiveHud.BoardCenter));break;
                case 2:
                    Check(ImmersiveHud.PointerOwned,"Board aim missed");start=owner.transform.position;Send("stick l 0 .6");delay=.15f;break;
                case 3:
                    Plugin.Line("JOURNAL_BELT_MOVEMENT displacement="+(owner.transform.position-start).magnitude+" input="+owner.movementController.moveInput.ToString("R")+" owned="+ImmersiveHud.InputOwned);
                    Check((owner.transform.position-start).magnitude>.05f&&ImmersiveHud.BoardInputOwned&&owner.movementController.moveInput.sqrMagnitude>.1f,"Walking still blocked while board hovered");
                    Plugin.Line("JOURNAL_WALK_PASS");Send("stick l 0 0");break;
                case 4:
                    owner.movementController.Teleport(start);Send(MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle));break;
                case 5:
                    Check(ImmersiveHud.BoardInputOwned&&!ImmersiveHud.BoardOwnsStick,"Reaim missed ungrabbed board");normal=Board.Normal;yaw=owner.movementController.targetHorizontalRotation;
                    Send("stick r .4 0");break;
                case 6:
                    Check(Vector3.Angle(normal,Board.Normal)<.1f,"Hover rotated board without a grab");
                    Check(Mathf.Abs(Mathf.DeltaAngle(yaw,owner.movementController.targetHorizontalRotation))>3,"Hover blocked ordinary player turning");
                    Plugin.Line("JOURNAL_HOVER_ONLY_PASS");Send("stick r 0 0\n"+MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle));break;
                case 7:
                    Check(ImmersiveHud.BoardInputOwned,"Title aim missed");Send("grip r 1");break;
                case 8:
                    Check(ImmersiveHud.PointerLatched&&ImmersiveHud.BoardInputOwned,"Title did not grab");
                    normal=Board.Normal;yaw=owner.movementController.targetHorizontalRotation;selected=inventory.currentSelectedSlot;Send("stick r .6 0");break;
                case 9:
                    Check(Vector3.Angle(normal,Board.Normal)>5,"Grabbed board did not rotate");Check(Mathf.Abs(Mathf.DeltaAngle(yaw,owner.movementController.targetHorizontalRotation))<.1f,"Grabbed board stick turned player");
                    Send("stick r 0 0\nshot journal-grabbed-rotated");break;
                case 10:distance=BoardDistance;Send("stick r 0 .6");break;
                case 11:
                    Check(BoardDistance>distance+.08f,"Grabbed board did not move farther");Check(inventory.currentSelectedSlot==selected,"Grabbed board stick cycled belt");
                    Check((float)typeof(MotionPointer).GetField("scrollSteps",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)==0,"Grabbed board scrolled native orders");Send("stick r 0 0");break;
                case 12:
                    distance=BoardDistance;Send("stick r 0 -.6");break;
                case 13:
                    Check(BoardDistance<distance-.08f,"Grabbed board did not move closer");Check(inventory.currentSelectedSlot==selected,"Grabbed board stick cycled belt");Plugin.Line("JOURNAL_GRABBED_STICK_POSE_PASS");
                    Send("stick r 0 0");break;
                case 14:
                    start=Board.Center;Send(MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle+Vector3.up*.12f+Board.Right*.16f));break;
                case 15:
                    Check((Board.Center-start).magnitude>.12f,"Existing grabbed title movement failed");Plugin.Line("JOURNAL_GRIP_DRAG_PASS");Send("stick r .6 0");break;
                case 16:
                    Send("grip r 0");break;
                case 17:
                    Check(!ImmersiveHud.PointerLatched&&ImmersiveHud.BoardOwnsStick,"Grab release lost stick ownership before neutral");
                    start=Board.Center;normal=Board.Normal;yaw=owner.movementController.targetHorizontalRotation;Send("hand r aimtrim 70 80");break;
                case 18:
                    Check((Board.Center-start).sqrMagnitude<.0001f&&Vector3.Angle(normal,Board.Normal)<.1f&&Mathf.Abs(Mathf.DeltaAngle(yaw,owner.movementController.targetHorizontalRotation))<.1f,"Released stick moved board/player");
                    Send("stick r 0 0");Plugin.Line("JOURNAL_STICK_RELEASE_PASS");break;
                case 19:Check(!ImmersiveHud.BoardOwnsStick,"Neutral failed to release stick");selected=inventory.currentSelectedSlot;Send("stick r 0 1");break;
                case 20:
                    Check(inventory.currentSelectedSlot==(selected+1)%inventory.totalSlots,"Normal belt stick cycling failed");
                    inventory.SetSelectedSlot(empty);HUDManager.LocalInstance.ShowEscapeMenu();Send("stick r 0 0\nstick l 0 .6");break;
                case 21:Check(owner.movementController.moveInput.sqrMagnitude<.0001f,"Pause allowed walking");HUDManager.LocalInstance.HideEscapeMenu();Send("stick l 0 0\nhands reset\nhead rot 0 -67 0");Plugin.Line("JOURNAL_PAUSE_AND_BELT_STICK_PASS");break;
                case 22:Send("shot journal-belt-fitted");break;
                case 23:Send(Palm(true));break;
                case 24:Send("grip l 1");break;
                case 25:Check(owner.heldItems.Contains(box.gameObject)&&ObjectGrabs.LeftHand,"Belt box did not equip in left hand");Send("grip l 0");break;
                case 26:Check(owner.heldItems.Contains(box.gameObject),"Relaxing belt grip dropped box");inventory.SetSelectedSlot(empty);break;
                case 27:Send(Palm(false));break;
                case 28:Send("grip r 1");break;
                case 29:Check(owner.heldItems.Contains(box.gameObject)&&!ObjectGrabs.LeftHand,"Belt box did not equip in right hand");Send("grip r 0");break;
                case 30:
                    Plugin.Line("JOURNAL_BELT_GRIP_PASS");Plugin.Line("JOURNAL_BELT_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();Send("input clear");phase=99;return;
            }
            phase++;next=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e){Plugin.Line("JOURNAL_BELT_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
    }
    private static void LeftTick()
    {
        try
        {
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;inventory=InventoryManager.LocalInstance;inventory.SetSelectedSlot(7);
                    Send("input clear\nhands reset\n"+MotionPointer.AimAtWorld(ImmersiveHud.BoardTitle,true));break;
                case 1:Send("grip l 1");break;
                case 2:
                    Check(MotionPointer.PointerLeft&&ImmersiveHud.PointerLatched&&ImmersiveHud.BoardInputOwned,"Left title grab failed");
                    normal=Board.Normal;yaw=owner.movementController.targetHorizontalRotation;Send("stick r -.6 0");break;
                case 3:
                    Check(Vector3.Angle(normal,Board.Normal)>5&&Mathf.Abs(Mathf.DeltaAngle(yaw,owner.movementController.targetHorizontalRotation))<.1f,"Left grab rotation failed or turned player");
                    distance=BoardDistance;Send("stick r 0 .6");break;
                case 4:Check(BoardDistance>distance+.08f,"Left grab did not push farther");distance=BoardDistance;Send("stick r 0 -.6");break;
                case 5:Check(BoardDistance<distance-.08f,"Left grab did not pull closer");Send("stick r 0 0\ngrip l 0");break;
                case 6:
                    Check(!ImmersiveHud.PointerLatched&&!ImmersiveHud.BoardOwnsStick,"Left grab did not release");
                    Plugin.Line("JOURNAL_LEFT_GRAB_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();phase=99;return;
            }
            phase++;next=Time.realtimeSinceStartup+.65f;
        }
        catch(Exception e){Plugin.Line("JOURNAL_BELT_FAILED left phase="+phase+" "+e);Send("input clear");phase=99;}
    }
}
