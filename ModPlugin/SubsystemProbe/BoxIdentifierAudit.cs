using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in measurements in an isolated profile. Not compiled into shipping builds.
internal static class BoxIdentifierAudit
{
    private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_BOX_IDENTIFIER_AUDIT");
    private static int phase,slot=-1,empty=-1;private static float next=25;
    private static CargoBox box;private static InteractionManager owner;private static InventoryManager inventory;
    private static CargoBox second;private static int secondSlot=-1;private static GameObject firstRoot,secondRoot;
    private static OrderableSO dough;private static IngredientSO other,replacement;
    private static void Check(bool v,string s){if(!v)throw new Exception(s);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static Bounds TransformBounds(Bounds b,Matrix4x4 matrix)
    {
        Bounds result=default;
        for(int i=0;i<8;i++)
        {
            var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            if(i==0)result=new Bounds(p,Vector3.zero);else result.Encapsulate(p);
        }
        return result;
    }
    private static void Measure(string tag)
    {
        foreach(var skin in box.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Plugin.Line("BOX_ICON_SKIN tag="+tag+" name="+skin.name+" scale="+skin.transform.lossyScale.ToString("R")+" nativeBounds="+skin.bounds.size.ToString("R")+" sharedBounds="+skin.sharedMesh.bounds.size.ToString("R")+" localBounds="+skin.localBounds.size.ToString("R"));
            for(int i=0;i<3;i++)
            {
                var mesh=new Mesh();if(i==0)skin.BakeMesh(mesh);else skin.BakeMesh(mesh,i==2);
                var world=TransformBounds(mesh.bounds,skin.transform.localToWorldMatrix);
                Plugin.Line("BOX_ICON_BAKE tag="+tag+" scaleMode="+i+" local="+mesh.bounds.size.ToString("R")+" world="+world.size.ToString("R"));UnityEngine.Object.Destroy(mesh);
            }
        }
        foreach(var sprite in box.GetComponentsInChildren<SpriteRenderer>(true))
            Plugin.Line("BOX_ICON_SPRITE tag="+tag+" name="+sprite.name+" sprite="+sprite.sprite?.name+" enabled="+sprite.enabled+" active="+sprite.gameObject.activeSelf+" scale="+sprite.transform.lossyScale.ToString("R")+" localBounds="+sprite.localBounds.size.ToString("R")+" worldBounds="+sprite.bounds.size.ToString("R")+" localPosition="+sprite.transform.localPosition.ToString("R")+" material="+sprite.sharedMaterial?.name+" shader="+sprite.sharedMaterial?.shader?.name);
    }
    private static GameObject Root(int index)
    {
        var slots=(Array)typeof(BeltModels).GetField("slots",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var slot=slots.GetValue(index);return (GameObject)slot.GetType().GetField("Root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slot);
    }
    private static void Verify(CargoBox source,int index)
    {
        var root=Root(index);Check(root!=null,"No miniature");var copies=root.GetComponentsInChildren<SpriteRenderer>(true);
        int count=0;var fit=root.transform.GetChild(0);
        var fromWorld=Matrix4x4.TRS(source.transform.position,source.transform.rotation,Vector3.one).inverse;
        foreach(var original in source.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if(original.sprite==null||!original.gameObject.activeSelf)continue;
            SpriteRenderer copy=null;foreach(var c in copies)if(c.name==original.name){copy=c;break;}
            Check(copy!=null&&copy.enabled&&copy.sprite==original.sprite&&copy.sharedMaterial==original.sharedMaterial,"Missing/wrong native sprite: "+original.name);
            Check(copy.flipX==original.flipX&&copy.flipY==original.flipY&&copy.color==original.color,"Changed native sprite appearance");
            Check(!original.enabled,"Stored original sprite was enabled");
            var expected=fromWorld*original.transform.localToWorldMatrix;
            var actual=fit.worldToLocalMatrix*copy.transform.localToWorldMatrix;
            var bounds=original.localBounds;
            for(int i=0;i<8;i++)
            {
                var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Check((expected.MultiplyPoint3x4(p)-actual.MultiplyPoint3x4(p)).magnitude<.001f,"Sprite placement diverged from native transform");
            }
            count++;
        }
        Check(count==3&&copies.Length==3,"Expected all three box-face identifiers");
        Bounds all=default;bool have=false;
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var b=TransformBounds(r.localBounds,root.transform.worldToLocalMatrix*r.transform.localToWorldMatrix);
            if(!have){all=b;have=true;}else{all.Encapsulate(b.min);all.Encapsulate(b.max);}
        }
        var size=Vector3.Scale(all.size,root.transform.lossyScale);
        Check(size.x<=.0712f&&size.y<=.0702f&&size.z<=.0612f&&Mathf.Max(size.x,Mathf.Max(size.y,size.z))>.06f,"Box/identifier sizing escaped belt loop");
        Plugin.Line("BOX_IDENTIFIER_MATCH slot="+index+" content="+(source.IsDoughBox()?"Dough":source.GetIngredientType()?.ingredientType.ToString())+" sprite="+copies[0].sprite.name+" size="+size.ToString("R"));
    }
    private static string Palm(bool left,int index)
    {
        var slots=(Array)typeof(BeltModels).GetField("slots",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var slot=slots.GetValue(index);
        var belt=(Transform)typeof(BeltModels).GetField("rootTransform",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var target=belt.TransformPoint((Vector3)slot.GetType().GetField("Position",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slot));
        var hand=left?ControllerTransport.Left:ControllerTransport.Right;Check(StereoTrial.TryTrackingPose(hand.Position,hand.Rotation,out var world,out var rotation),"No hand pose");
        Check(MotionBody.TryPalmGeometry(owner,left,out var palm,out _,out _,out _),"No palm calibration");
        var basis=rotation*Quaternion.Inverse(hand.Rotation);var p=hand.Position+Quaternion.Inverse(basis)*(target-world)-palm;
        return FormattableString.Invariant($"hand {(left?"l":"r")} grip pose {p.x:R} {p.y:R} {-p.z:R} 0 0 0");
    }
    private static CargoBox Spawn(CargoBox template,IngredientSO ingredient)
    {
        var clone=UnityEngine.Object.Instantiate(template.gameObject);clone.transform.position=owner.transform.position+Vector3.up*3;clone.SetActive(true);
        var net=clone.GetComponent<NetworkObject>();if(!net.IsSpawned)net.Spawn();var result=clone.GetComponent<CargoBox>();if(ingredient!=null)result.ConfigureFromSO(ingredient);return result;
    }
    internal static void Tick()
    {
        if(mode==null||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        if(mode=="accept"){Accept();return;}
        try
        {
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;inventory=InventoryManager.LocalInstance;
                    for(int i=0;i<inventory.totalSlots;i++)if(inventory.IsSlotEmpty(i)){if(slot<0)slot=i;else empty=i;}
                    Check(slot>=0&&empty>=0,"Fixture needs two empty slots");
                    foreach(var c in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(c.CanPickup()&&c.GetIngredientType()!=null){box=c;if(c.GetIngredientType().ingredientType.ToString().Contains("Dough"))break;}
                    Check(box!=null,"No configured ingredient box");inventory.SetSelectedSlot(slot);Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Native pickup");Send("input clear\nhands reset");break;
                case 1:Measure("held");inventory.SetSelectedSlot(empty);break;
                case 2:Measure("stored");Plugin.Line("BOX_IDENTIFIER_SURVEY_COMPLETE");phase=99;return;
            }
            phase++;next=Time.realtimeSinceStartup+.7f;
        }
        catch(Exception e){Plugin.Line("BOX_IDENTIFIER_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
    }
    private static void Accept()
    {
        try
        {
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;inventory=InventoryManager.LocalInstance;
                    UiInputAudit.Dump("Assembly-CSharp.dll","","CargoBox","Update","UpdateCanvasIcons","ConfigureFromSO");
                    foreach(var data in Resources.FindObjectsOfTypeAll<IngredientSO>())
                    {
                        if(data.ingredientIcon==null||data.isShadyIngredient)continue;
                        if(other==null)other=data;else if(data.ingredientIcon!=other.ingredientIcon)replacement=data;
                    }
                    foreach(var data in Resources.FindObjectsOfTypeAll<OrderableSO>())if(data.orderableIcon!=null&&!data.IsXLVariant&&data.orderableType.ToString().Contains("Dough")){dough=data;break;}
                    Check(dough!=null&&other!=null&&replacement!=null,"No distinct ingredient icon fixtures");
                    for(int i=0;i<inventory.totalSlots;i++)if(inventory.IsSlotEmpty(i)){if(slot<0)slot=i;else if(secondSlot<0)secondSlot=i;else empty=i;}
                    Check(empty>=0,"Need three empty fixture slots");
                    CargoBox template=null;foreach(var c in UnityEngine.Object.FindObjectsOfType<CargoBox>())if(c.CanPickup()&&c.GetIngredientType()!=null){template=c;break;}
                    Check(template!=null,"No native cargo template");box=Spawn(template,null);box.ConfigureFromOrderableSO(dough);second=Spawn(template,other);Send("input clear\nhands reset");break;
                case 1:
                    Check(box.IsDoughBox(),"Dough configuration failed");
                    inventory.SetSelectedSlot(slot);Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Dough pickup");
                    inventory.SetSelectedSlot(secondSlot);Check(owner.TryPickupItem(second.Cast<IHoldable>()),"Second pickup");inventory.SetSelectedSlot(empty);break;
                case 2:
                    Verify(box,slot);Verify(second,secondSlot);Check(box.sprite1.sprite!=second.sprite1.sprite,"Fixture icons not distinct");
                    firstRoot=Root(slot);secondRoot=Root(secondSlot);Send("head rot 0 -67 0");break;
                case 3:
                    Check(Root(slot)==firstRoot&&Root(secondSlot)==secondRoot,"Idle boxes rebuilt their miniatures");Send("shot belt-dough-and-ingredient-identifiers");Plugin.Line("BOX_IDENTIFIERS_DISTINCT_PASS");break;
                case 4:Send(Palm(true,slot));break;
                case 5:Send("grip l 1");break;
                case 6:
                    Check(owner.heldItems.Contains(box.gameObject)&&ObjectGrabs.LeftHand&&box.sprite1.enabled,"Left draw lost native box/identifier");Send("grip l 0");break;
                case 7:Check(owner.heldItems.Contains(box.gameObject),"Grip release dropped box");inventory.SetSelectedSlot(empty);break;
                case 8:
                    Verify(box,slot);firstRoot=Root(slot);secondRoot=Root(secondSlot);box.ConfigureFromSO(replacement);break;
                case 9:
                    Check(Root(slot)!=firstRoot,"Native icon update did not invalidate miniature");Check(Root(secondSlot)==secondRoot,"Icon update rebuilt unrelated slot");
                    Verify(box,slot);Check(box.sprite1.sprite!=dough.orderableIcon,"Icon stayed dough after native contents update");Plugin.Line("BOX_IDENTIFIER_REFRESH_PASS");Send("shot belt-updated-identifiers");break;
                case 10:Send(Palm(false,secondSlot));break;
                case 11:Send("grip r 1");break;
                case 12:
                    Check(owner.heldItems.Contains(second.gameObject)&&!ObjectGrabs.LeftHand&&second.sprite1.enabled,"Right draw lost native identifier");Send("grip r 0");inventory.SetSelectedSlot(empty);break;
                case 13:
                    Verify(box,slot);Verify(second,secondSlot);Plugin.Line("BOX_IDENTIFIER_BILATERAL_GRIP_PASS");Plugin.Line("BOX_IDENTIFIER_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();Send("input clear");phase=99;return;
            }
            phase++;next=Time.realtimeSinceStartup+.7f;
        }
        catch(Exception e){Plugin.Line("BOX_IDENTIFIER_FAILED acceptance phase="+phase+" "+e);Send("input clear");phase=99;}
    }
}
