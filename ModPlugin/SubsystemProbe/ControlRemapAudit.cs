using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;

// Opt-in isolated controller/native-consumer acceptance. Excluded from shipping.
internal static class ControlRemapAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_CONTROL_REMAP_AUDIT")=="1";
    private static int phase,interacts,primaries,stock;private static float next=25;
    private static InteractionManager owner;private static Pizza pizza;private static PizzaBox box;
    private static IngredientContainerBase container;private static Ingredient ingredient;private static Freezer freezer;
    private static bool CursorDrawn=>((LineRenderer)typeof(MotionPointer).GetField("laser",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))?.enabled==true;
    private static void Check(bool value,string why){if(!value)throw new Exception(why);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void Interact()=>interacts++;
    private static void Primary()=>primaries++;
    private static void Clear(){interacts=primaries=0;}
    private static void Target(Component target)
    {
        var collider=target.GetComponent<Collider>()??target.GetComponentInChildren<Collider>();Check(collider!=null,"Fixture collider missing");
        Check(StereoTrial.TryHeadWorld(out var head,out var rotation),"Head unavailable");
        var body=target.GetComponent<Rigidbody>();if(body!=null)body.isKinematic=true;
        target.transform.position+=head+rotation*Vector3.forward*1.4f-collider.bounds.center;Physics.SyncTransforms();
        Send(MotionPointer.AimAtWorld(collider.bounds.center,ObjectGrabs.LeftHand));
    }
    private static void Selected(Component target)
    {
        Check(owner.currentInteractable!=null&&owner.currentInteractable.Pointer==target.Pointer,"Native target mismatch: "+owner.currentInteractable?.GetTransform()?.name);
        Check(owner.currentInteractable.CanInteract(),"Native receiver refused fixture");Clear();Send("trigger r 1");
    }
    internal static void Tick()
    {
        if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            var hud=HUDManager.LocalInstance;
            switch(phase)
            {
                case 0:
                    owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);
                    var harmony=new Harmony("ikag.control-remap-audit");
                    harmony.Patch(typeof(InteractionManager).GetMethod("HandleInteractPressed"),prefix:new HarmonyMethod(typeof(ControlRemapAudit).GetMethod(nameof(Interact),BindingFlags.NonPublic|BindingFlags.Static)));
                    harmony.Patch(typeof(InteractionManager).GetMethod("HandlePrimaryPressed"),prefix:new HarmonyMethod(typeof(ControlRemapAudit).GetMethod(nameof(Primary),BindingFlags.NonPublic|BindingFlags.Static)));
                    foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()){pizza=p;break;}
                    foreach(var b in UnityEngine.Object.FindObjectsOfType<PizzaBox>())if(b.IsEmpty()&&b.CanPickup()){box=b;break;}
                    foreach(var c in UnityEngine.Object.FindObjectsOfType<IngredientContainerBase>())
                    {
                        var data=c.GetIngredientData();Plugin.Line("REMAP_CONTAINER name="+c.name+" type="+data?.ingredientType+" stock="+c.GetCurrentAmount());
                        if(data!=null&&data.ingredientPrefab!=null&&(container==null||data.ingredientType.ToString().Contains("Dough")))container=c;
                    }
                    Check(pizza!=null&&box!=null&&container!=null,"Missing native fixtures");
                    Send("input clear\nhands reset\nhand r aimtrim 70 0\nhand l aimtrim 70 0");break;
                case 1:
                    Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Recipe held pizza fixture");Clear();Send("btn x down");break;
                case 2:
                    Check(hud.IsRecipeBookOpen,"X did not open recipes");Check(interacts==0&&primaries==0,"X emitted an item action");
                    Check(owner.heldItems.Contains(pizza.gameObject),"X consumed held pizza");Send("shot remap-recipes-open");break;
                case 3:Check(hud.IsRecipeBookOpen,"Held X repeated recipe toggle");Send("btn x up");break;
                case 4:Send("btn x down");break;
                case 5:Check(!hud.IsRecipeBookOpen&&interacts==0&&primaries==0,"Second X did not close cleanly");Plugin.Line("REMAP_RECIPE_X_PASS");Send("btn x up");break;
                case 6:Check(MotionPointer.CursorVisible(false)&&MotionPointer.CursorVisible(true),"Initial visibility");Send("click r down");break;
                case 7:Check(!MotionPointer.CursorVisible(false)&&MotionPointer.CursorVisible(true)&&!hud.IsRecipeBookOpen&&!CursorDrawn,"R3 did not hide right only");Send("shot remap-right-hidden");break;
                case 8:Check(!MotionPointer.CursorVisible(false),"Held R3 repeated");Send("click r up");break;
                case 9:Send("click r down");break;
                case 10:Check(MotionPointer.CursorVisible(false)&&!hud.IsRecipeBookOpen&&CursorDrawn,"R3 did not restore right");Send("shot remap-right-shown\nclick r up");owner.DropLastItem();break;
                case 11:Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Left pizza fixture");Check(ObjectGrabs.AdoptDistance(owner,pizza.gameObject,true,requireGrip:false),"Left adoption");break;
                case 12:Check(DistanceGrips.AimLeft,"Left hand not active");Send("click r down");break;
                case 13:Check(!MotionPointer.CursorVisible(true)&&MotionPointer.CursorVisible(false)&&!CursorDrawn,"R3 did not hide left only");Send("shot remap-left-hidden\nclick r up");break;
                case 14:Send("click r down");break;
                case 15:Check(MotionPointer.CursorVisible(true)&&MotionPointer.CursorVisible(false)&&CursorDrawn,"Left did not restore independently");Plugin.Line("REMAP_CURSOR_HANDS_PASS");Send("shot remap-left-shown\nclick r up");break;
                case 16:
                    owner.DropLastItem();owner.movementController.Teleport(owner.transform.position+Vector3.up*20);owner.movementController.enabled=false;
                    var storageData=container.GetIngredientData();var clone=UnityEngine.Object.Instantiate(storageData.ingredientPrefab);clone.transform.position=owner.transform.position+Vector3.up*3;clone.SetActive(true);
                    var net=clone.GetComponent<NetworkObject>();if(!net.IsSpawned)net.Spawn();ingredient=clone.GetComponent<Ingredient>();Check(ingredient!=null,"Ingredient fixture missing");
                    container.SetAmount(Math.Max(0,container.GetMaxCapacity()-2));stock=container.GetCurrentAmount();Check(owner.TryPickupItem(ingredient.Cast<IHoldable>()),"Ingredient pickup");
                    Check(ObjectGrabs.AdoptDistance(owner,ingredient.gameObject,false,requireGrip:false),"Ingredient hand");Target(container);break;
                case 17:Selected(container);break;
                case 18:
                    Check(container.GetCurrentAmount()>stock&&!owner.IsHoldingItem(),"RT did not return ingredient to storage");
                    Check(interacts==1&&primaries==0,"Storage dispatched duplicate or primary use");Plugin.Line("REMAP_STORAGE_RT_PASS type="+container.GetIngredientData().ingredientType);Send("trigger r 0");break;
                case 19:
                    container.transform.position+=Vector3.right*10;
                    var toppings=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();toppings.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));
                    pizza.RestoreFromSave(DoughState.CookedDough,100,true,CookingQuality.Perfect,toppings);Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Packing pizza pickup");
                    Check(ObjectGrabs.AdoptDistance(owner,pizza.gameObject,true,requireGrip:false),"Packing left hand");Target(box);break;
                case 20:Selected(box);break;
                case 21:
                    Check(box.HasWholePizza()&&owner.heldItems.Contains(box.gameObject)&&ObjectGrabs.LeftHand,"RT did not pack pizza and preserve left hand");Check(interacts==1&&primaries==0,"Packing emitted primary use");
                    Plugin.Line("REMAP_PACK_RT_PASS");Send("shot remap-packed-left\ntrigger r 0");break;
                case 22:
                    owner.DropLastItem();box.transform.position+=Vector3.right*10;Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Fallback box pickup");Clear();Send("hand r aimtrim -80 0\nhand l aimtrim -80 0");break;
                case 23:Check(owner.currentInteractable==null&&owner.currentHoldable==null,"Fallback aim has target");Send("trigger r 1");break;
                case 24:Check(primaries==1&&interacts==0&&MotionInput.PrimaryHeld,"RT primary fallback failed");Send("btn x down");break;
                case 25:Check(hud.IsRecipeBookOpen&&!MotionInput.PrimaryHeld&&!MotionInput.InteractHeld,"Recipe entry left trigger latched");Send("btn x up");break;
                case 26:Send("btn x down");break;
                case 27:Check(!hud.IsRecipeBookOpen&&primaries==1,"Held RT repeated on recipe exit");Send("btn x up\ntrigger r 0");break;
                case 28:
                    Check(!MotionInput.PrimaryHeld&&!MotionInput.InteractHeld,"RT release stuck");Plugin.Line("REMAP_RELEASE_OWNERSHIP_PASS");
                    owner.DropLastItem();Clear();Send("trigger r 1");break;
                case 29:Check(primaries==0&&interacts==0,"Empty RT emitted punch/interaction");Send("trigger r 0");break;
                case 30:
                    freezer=UnityEngine.Object.FindObjectOfType<Freezer>();Check(freezer!=null,"Dough freezer missing");
                    var dough=UnityEngine.Object.Instantiate(freezer.GetDoughPrefab());dough.transform.position=owner.transform.position+Vector3.up*3;dough.SetActive(true);
                    var doughNet=dough.GetComponent<NetworkObject>();if(!doughNet.IsSpawned)doughNet.Spawn();pizza=dough.GetComponent<Pizza>();
                    freezer.SetAmount(Math.Max(0,freezer.GetMaxCapacity()-2));stock=freezer.GetCurrentAmount();
                    Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Dough pickup");Check(freezer.CanPutPizzaBackInternal(pizza),"Native freezer rejects raw dough");
                    Check(ObjectGrabs.AdoptDistance(owner,pizza.gameObject,false,requireGrip:false),"Dough right hand");Target(freezer);break;
                case 31:Selected(freezer);break;
                case 32:
                    Check(freezer.GetCurrentAmount()>stock&&!owner.IsHoldingItem()&&interacts==1&&primaries==0,"RT dough storage failed");
                    Plugin.Line("REMAP_DOUGH_FREEZER_RT_PASS");Send("trigger r 0");break;
                case 33:Plugin.Line("CONTROL_REMAP_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();phase=99;return;
            }
            phase++;next=Time.realtimeSinceStartup+.8f;
        }
        catch(Exception e){Plugin.Line("CONTROL_REMAP_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
    }
}
