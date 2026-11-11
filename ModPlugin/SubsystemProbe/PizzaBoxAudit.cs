using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class PizzaBoxAudit
{
    private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_PIZZA_BOX_AUDIT");
    private static int phase;private static float next=25;private static InteractionManager owner;private static PizzaBox box;private static Pizza pizza;
    private static int sampled=-1;private static Rigidbody boxBody;
    private static MeshRenderer[] originalMeshes;private static bool[] originalFlags;private static float maximumAttachmentError;
    internal static void RecipeInput(bool now,bool before,bool live,bool gameplay)
    {if(mode!=null&&now&&!before)Plugin.Line("RECIPE_EDGE live="+live+" gameplay="+gameplay+" open="+HUDManager.LocalInstance?.IsRecipeBookOpen+" subscribed="+HUDManager.LocalInstance?.recipeBookInputSubscribed+" cursor="+Cursor.lockState);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void Check(bool v,string s){if(!v)throw new Exception(s);}
    internal static void Rendered()
    {
        if(mode!=null&&box!=null&&pizza!=null&&box.HasWholePizza()&&owner!=null&&owner.heldItems.Contains(box.gameObject))
            maximumAttachmentError=Mathf.Max(maximumAttachmentError,Vector3.Distance(pizza.transform.position,box.pizzaPlacementPoint.position));
        if(mode==null||box==null||pizza==null||sampled==phase)return;sampled=phase;
        Plugin.Line("BOX_RENDER phase="+phase+" box="+box.transform.position+" anchor="+box.pizzaPlacementPoint.position+" pizza="+pizza.transform.position+" parent="+pizza.transform.parent?.name+" follow="+pizza.followTarget?.name+" following="+pizza.isFollowing+" continuous="+pizza.continuousFollow+" relative="+box.pizzaPlacementPoint.InverseTransformPoint(pizza.transform.position));
    }
    internal static void Tick()
    {
        if(mode==null||phase>24||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);Send("hands reset");
                    UiInputAudit.Dump("Assembly-CSharp.dll","","PizzaBox","SetupPizzaFollowing","OnInteract","CanPack","AddWholePizzaInternal");
                    UiInputAudit.Dump("Assembly-CSharp.dll","","Pizza","LateUpdate","SyncToFollowTarget","StartFollowing");
                    UiInputAudit.Dump("Assembly-CSharp.dll","","HUDManager","ToggleRecipeBook","CloseRecipeBook","OpenRecipeBook","SubscribeRecipeBookInput");
                    foreach(var b in UnityEngine.Object.FindObjectsOfType<PizzaBox>())
                    {if(b.IsEmpty()&&b.CanPickup()&&box==null)box=b;Plugin.Line("BOX_CENSUS name="+b.name+" empty="+b.IsEmpty()+" lid="+b.IsLidVisible()+" position="+b.transform.position+" pizza="+b.wholePizzaInBox?.pizzaObject?.name);}
                    foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()&&pizza==null)pizza=p;
                    Check(box!=null&&pizza!=null,"Missing native box/pizza fixture");
                    Plugin.Line("BOX_CANDIDATE canPack="+box.CanPack(pizza)+" refusal="+box.GetRefusalPrompt(pizza)+" dough="+pizza.networkDoughState.Value+" quality="+pizza.cookingQuality);break;
                case 1:
                    if(!PizzaBox.IsPizzaBoxable(pizza))
                    {var toppings=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();toppings.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));pizza.RestoreFromSave(DoughState.CookedDough,100,true,CookingQuality.Perfect,toppings);}
                    var meshes=pizza.GetComponentsInChildren<MeshRenderer>(true);originalMeshes=new MeshRenderer[meshes.Length];originalFlags=new bool[meshes.Length];
                    for(int i=0;i<meshes.Length;i++){originalMeshes[i]=meshes[i];originalFlags[i]=meshes[i].allowGPUDrivenRendering;}
                    Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Pizza pickup failed");
                    Plugin.Line("BOX_PACK_ELIGIBLE "+box.CanPack(pizza));box.OnInteract();break;
                case 2:
                    Check(box.HasWholePizza(),"Native packing failed");
                    Check(owner.heldItems.Contains(box.gameObject)||owner.TryPickupItem(box.Cast<IHoldable>()),"Box pickup failed");break;
                case 3:Send("shot pizza-box-baseline\nhand r grip pos .35 1.25 -.6\nhand r aimtrim 25 30");break;
                case 4:Send("shot pizza-box-moved\nbtn x down");break;
                case 5:Check(HUDManager.LocalInstance.IsRecipeBookOpen,"X did not open recipes");Send("btn x up");break;
                case 6:Send("btn x down");break;
                case 7:Check(!HUDManager.LocalInstance.IsRecipeBookOpen,"Second X did not close recipes");Plugin.Line("RECIPE_TOGGLE_PASS");Send("btn x up");break;
                case 8:owner.DropLastItem();pizza=box.ServerReleasePizza();Check(pizza!=null,"Native unpack fixture failed");Send("hands reset");break;
                case 9:Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Unpacked pizza pickup failed");Send("grip r 1");break;
                case 10:
                    Check(MotionHeldItems.TryAnchor(owner,out var visible,out _,out _),"No held pizza pose");
                    boxBody=box.GetComponent<Rigidbody>();boxBody.isKinematic=true;
                    var position=boxBody.position+visible-box.pizzaPlacementPoint.position;boxBody.position=position;box.transform.position=position;Physics.SyncTransforms();
                    Plugin.Line("BOX_RELEASE_GEOMETRY anchor="+box.pizzaPlacementPoint.position+" pizza="+visible+" collider="+box.col.enabled+" size="+box.col.TryCast<BoxCollider>()?.size+" eligible="+box.CanPack(pizza));
                    Send("hand r aimtrim 0 80");break;
                case 11:Send("grip r 0");break;
                case 12:Check(box.HasWholePizza(),"Physical grip-release packing failed");Plugin.Line("BOX_PHYSICAL_RELEASE_PASS");Send("shot pizza-box-release");break;
                case 13:owner.DropLastItem();pizza=box.ServerReleasePizza();Check(pizza!=null,"Second native unpack failed");boxBody.position+=Vector3.up*3;box.transform.position=boxBody.position;Send("hands reset");break;
                case 14:Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Distance pizza pickup failed");Send("grip r 1");break;
                case 15:
                    Check(StereoTrial.TryHeadWorld(out var head,out var heading),"No head pose");
                    var delta=head+heading*Vector3.forward*1.5f-box.pizzaPlacementPoint.position;
                    boxBody.position+=delta;box.transform.position=boxBody.position;Physics.SyncTransforms();break;
                case 16:Send(MotionPointer.AimAtWorld(box.col.bounds.center));break;
                case 17:Check(owner.currentInteractable!=null&&owner.currentInteractable.GetTransform()==box.transform,"Distance ray did not select box");Check(box.CanPack(pizza),"Distance box not eligible");Send("grip r 0");break;
                case 18:Check(box.HasWholePizza(),"Distance grip-release packing failed");Plugin.Line("BOX_DISTANCE_RELEASE_PASS");owner.DropLastItem();break;
                case 19:
                    for(int i=0;i<originalMeshes.Length;i++)if(originalMeshes[i]!=null)Check(originalMeshes[i].allowGPUDrivenRendering==originalFlags[i],"Contained renderer GPU flag not restored: "+originalMeshes[i].name);
                    Check(maximumAttachmentError<.00002f,"Contained pizza drifted from box");
                    Plugin.Line("BOX_FLOW_ACCEPTANCE_COMPLETE maxAttachmentError="+maximumAttachmentError);StereoTrial.DiagnosticMetrics();phase=25;return;
            }
            phase++;next=Time.realtimeSinceStartup+1;
        }
        catch(Exception e){Plugin.Line("PIZZA_BOX_AUDIT_FAILED phase="+phase+" "+e);phase=25;Send("input clear");}
    }
}

