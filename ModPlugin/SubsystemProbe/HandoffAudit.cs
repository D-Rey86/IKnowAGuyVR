using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class HandoffAudit
{
 private static readonly string mode=Environment.GetEnvironmentVariable("IKAG_XR_HANDOFF_AUDIT");
 private static int phase;private static float next=25;
 private static InteractionManager owner;private static Pizza pizza;private static PizzaBox box;
 private static CustomerNPC customer;private static NetworkBehaviour bin;private static GameObject trash;private static Vector3 binPosition;
 private static GameObject boxTemplate,pizzaTemplate;private static DrinkingCup cup;private static Courier courier;
 private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
 private static Vector3 Anchor(){Check(MotionHeldItems.TryAnchor(owner,out var p,out _,out _),"No held anchor");return p;}
 private static void AlignBox()
 {
  var body=box.GetComponent<Rigidbody>();body.isKinematic=true;var p=box.transform.position+Anchor()-box.pizzaPlacementPoint.position;
  body.position=p;box.transform.position=p;Physics.SyncTransforms();
  Plugin.Line("PACK_ELIGIBILITY lid="+box.IsLidVisible()+" canPack="+box.CanPack(pizza)+" canInteract="+box.CanInteract()+" ownerLeft="+ObjectGrabs.LeftHand);
 }
 private static PizzaBox EmptyBox()
 {
  foreach(var b in UnityEngine.Object.FindObjectsOfType<PizzaBox>())if(b.IsEmpty()&&b.CanPickup())return b;
  Check(boxTemplate!=null,"No empty box fixture");var clone=UnityEngine.Object.Instantiate(boxTemplate);clone.transform.position=owner.transform.position+Vector3.up*3;clone.SetActive(true);clone.GetComponent<NetworkObject>().Spawn();return clone.GetComponent<PizzaBox>();
 }
 private static void Packed(bool left)
 {Check(box.HasWholePizza()&&owner.heldItems.Contains(box.gameObject),"Native pack/box pickup failed");Check(ObjectGrabs.Grabbed&&ObjectGrabs.LeftHand==left,"Packed box lost hand ownership");Check(!owner.heldItems.Contains(pizza.gameObject),"Pizza remained separately held");}
 internal static void Tick()
 {
  if(mode==null||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
  try
  {
   if(mode=="1"){Census();phase=99;return;}
   switch(phase)
   {
    case 0:
     owner=InteractionManager.LocalInstance;InventoryManager.LocalInstance.SetSelectedSlot(7);box=EmptyBox();
     foreach(var p in UnityEngine.Object.FindObjectsOfType<Pizza>())if(p.CanPickup()){pizza=p;break;}
     Check(pizza!=null,"No pizza fixture");
     var toppings=new Il2CppSystem.Collections.Generic.List<NetworkIngredientData>();toppings.Add(new NetworkIngredientData(IngredientType.Mozzarella,0));
     pizza.RestoreFromSave(DoughState.CookedDough,100,true,CookingQuality.Perfect,toppings);
     boxTemplate=UnityEngine.Object.Instantiate(box.gameObject);boxTemplate.SetActive(false);
     pizzaTemplate=UnityEngine.Object.Instantiate(pizza.gameObject);pizzaTemplate.SetActive(false);
     Send("input clear\nhands reset\nhand r aimtrim 0 80\nhand l aimtrim 0 80");break;
    case 1:Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Pickup pizza");Send("grip r 1");break;
    case 2:ObjectGrabs.AdoptDistance(owner,pizza.gameObject,false);AlignBox();break;
    case 3:Check(!box.HasWholePizza(),"Packed without opposite grip");Send("grip l 1");break;
    case 4:Packed(false);Send("shot pack-right\ngrip l 0");break;
    case 5:Packed(false);Plugin.Line("PACK_RIGHT_PASS");Send("hand r grip pos .5 1.2 -.5");break;
    case 6:Send("grip r 0");break;
    case 7:
     Check(!owner.IsHoldingItem(),"Packed box did not release from owning hand");pizza=box.ServerReleasePizza();Check(pizza!=null,"Unpack fixture");
     var rb=box.GetComponent<Rigidbody>();rb.isKinematic=true;rb.position+=Vector3.up*3;box.transform.position=rb.position;
     Send("input clear\nhands reset\nhand r aimtrim 0 80\nhand l aimtrim 0 80");break;
    case 8:Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Pickup left pizza");Send("grip l 1");break;
    case 9:ObjectGrabs.AdoptDistance(owner,pizza.gameObject,true);box.ShowLid();AlignBox();break;
    case 10:Send("grip r 1");break;
    case 11:Packed(true);Send("shot pack-left\ngrip r 0");break;
    case 12:Packed(true);Plugin.Line("PACK_LEFT_CLOSED_LID_PASS");break;
    case 13:
     foreach(var c in UnityEngine.Object.FindObjectsOfType<CustomerNPC>())if(!c.isShadyCustomer&&!c.IsOpenWorldDeliveryNPC){customer=c;break;}
     Check(customer!=null,"No ordinary customer");customer.StopAllCoroutines();customer.enabled=false;if(customer.navAgent!=null)customer.navAgent.enabled=false;
     customer.networkHasOrdered.Value=true;customer.networkOrderFulfilled.Value=false;customer.networkCanInteract.Value=true;
     customer.networkPizzasDelivered.Value=0;customer.networkPizzasRequired.Value=2;customer.currentOrderId="vr-handoff-fixture";
     customer.currentOrder=new MenuOrder();customer.deliveredPizzaCounts=new Il2CppSystem.Collections.Generic.Dictionary<PizzaOrderLine,int>();
     var customerCollider=customer.mainCollider!=null?customer.mainCollider:customer.GetComponent<Collider>();Check(customerCollider!=null,"No customer collider");
     Plugin.Line("CUSTOMER_FIXTURE coll="+customerCollider.name+" canInteract="+customer.CanInteract()+" ordered="+customer.networkHasOrdered.Value);
     customer.transform.position+=Anchor()-customerCollider.bounds.center;Physics.SyncTransforms();
     Check(customer.CanInteract(),"Customer fixture not interactable");
     Check(!ReleaseHandoffs.TryRelease(owner,box.gameObject)&&owner.heldItems.Contains(box.gameObject)&&!customer.IsInDialogLocally,"Rejected delivery acted/opened dialogue");
     customer.currentOrder.AddPizza(box.GetWholePizza().pizzaType,2);
     Check(customer.WouldAcceptPizza(box),"Customer order does not match fixture pizza");break;
    case 14:Send("grip l 0");break;
    case 15:
     Check(!owner.IsHoldingItem()&&customer.networkPizzasDelivered.Value==1,"Customer release did not fulfill pizza line");
     Check(!customer.IsInDialogLocally,"Delivery opened dialogue");Plugin.Line("CUSTOMER_RELEASE_PASS");customer.transform.position+=Vector3.right*4;
     Send("input clear\nhands reset\nhand r aimtrim 0 80");break;
    case 16:
     box=EmptyBox();trash=box.gameObject;Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Shop bin fixture pickup");
     bin=UnityEngine.Object.FindObjectOfType<BuildableTrashBin>();Check(bin!=null,"No shop bin");binPosition=bin.transform.position;Send("grip r 1");break;
    case 17:ObjectGrabs.AdoptDistance(owner,trash,false);bin.transform.position+=Anchor()-bin.GetComponent<Collider>().bounds.center;Physics.SyncTransforms();break;
    case 18:Send("grip r 0");break;
    case 19:
     Check(!owner.IsHoldingItem()&&(box==null||!box.IsSpawned),"Shop-bin release did not dispose");Plugin.Line("SHOP_BIN_RELEASE_PASS");bin.transform.position=binPosition;
     Send("input clear\nhands reset\nhand l aimtrim 0 80");break;
    case 20:
     box=EmptyBox();trash=box.gameObject;Check(owner.TryPickupItem(box.Cast<IHoldable>()),"Dumpster fixture pickup");
     bin=UnityEngine.Object.FindObjectOfType<TrashBin>();Check(bin!=null,"No dumpster");binPosition=bin.transform.position;Send("grip l 1");break;
    case 21:ObjectGrabs.AdoptDistance(owner,trash,true);bin.transform.position+=Anchor()-bin.GetComponent<Collider>().bounds.center;Physics.SyncTransforms();break;
    case 22:Send("grip l 0");break;
    case 23:
     Check(!owner.IsHoldingItem()&&(box==null||!box.IsSpawned),"Dumpster release did not dispose");Plugin.Line("DUMPSTER_RELEASE_PASS");bin.transform.position=binPosition;Physics.SyncTransforms();
     Send("input clear\nhands reset");break;
    case 24:
     var cups=Resources.FindObjectsOfTypeAll<DrinkingCup>();Check(cups.Length>0,"No cup prefab");
     var go=UnityEngine.Object.Instantiate(cups[0].gameObject);go.transform.position=owner.transform.position+Vector3.up*3;go.SetActive(true);var net=go.GetComponent<NetworkObject>();if(!net.IsSpawned)net.Spawn();
     cup=go.GetComponent<DrinkingCup>();cup.RestoreFromSave(BeverageType.Cola,1,true);break;
    case 25:Check(owner.TryPickupItem(cup.Cast<IHoldable>()),"Cup pickup");Send("grip r 1");break;
    case 26:
     ObjectGrabs.AdoptDistance(owner,cup.gameObject,false);customer.currentOrder.AddBeverage(BeverageType.Cola);customer.networkBeveragesRequired.Value=1;customer.networkBeveragesDelivered.Value=0;customer.deliveredBeverageCounts=new Il2CppSystem.Collections.Generic.Dictionary<BeverageType,int>();
     customer.transform.position+=Anchor()-customer.GetComponent<Collider>().bounds.center;Physics.SyncTransforms();Check(customer.WouldAcceptBeverage(cup),"Cup rejected");break;
    case 27:Send("grip r 0");break;
    case 28:Check(!owner.IsHoldingItem()&&customer.networkBeveragesDelivered.Value==1,"Drink delivery failed");Plugin.Line("DRINK_RELEASE_PASS");customer.transform.position+=Vector3.right*4;Send("input clear\nhands reset");break;
    case 29:
     var pg=UnityEngine.Object.Instantiate(pizzaTemplate);pg.transform.position=owner.transform.position+Vector3.up*3;pg.SetActive(true);pg.GetComponent<NetworkObject>().Spawn();pizza=pg.GetComponent<Pizza>();
     box=EmptyBox();Check(owner.TryPickupItem(pizza.Cast<IHoldable>()),"Courier pizza pickup");box.OnInteract();Check(box.HasWholePizza()&&owner.heldItems.Contains(box.gameObject),"Courier box fixture");Send("grip r 1");break;
    case 30:
     ObjectGrabs.AdoptDistance(owner,box.gameObject,false);courier=UnityEngine.Object.FindObjectOfType<Courier>();Check(courier!=null,"Courier missing");
     var order=new CourierOrder(){orderedPizzaType=box.GetWholePizza().pizzaType,requiredSauce=-1,status=CourierOrderStatus.Accepted};CourierOrderManager.Instance.activeOrders.Clear();CourierOrderManager.Instance.activeOrders.Add(order);
     Check(courier.CanInteract()&&courier.IsPizzaAcceptableForOrder(box,courier.GetActiveOrder()),"Courier fixture rejected");
     // Exercise the shared native distance query, without physical overlap.
     var cam=owner.interactionCamera.transform;courier.transform.position+=cam.position+cam.forward*1.5f-courier.GetComponent<Collider>().bounds.center;Physics.SyncTransforms();
     Send(MotionPointer.AimAtWorld(courier.GetComponent<Collider>().bounds.center));break;
    case 31:Plugin.Line("COURIER_AIM actual="+owner.rayHit.collider?.name+" distance="+owner.rayHit.distance);Check(owner.currentInteractable!=null&&owner.currentInteractable.Pointer==courier.Pointer,"Courier native aim missed");Send("grip r 0");break;
    case 32:Check(!owner.IsHoldingItem()&&courier.isDelivering.Value,"Courier release did not begin delivery");Plugin.Line("COURIER_DISTANCE_RELEASE_PASS");Send("input clear\nhands reset");break;
    case 33:Plugin.Line("HANDOFF_ACCEPTANCE_COMPLETE");StereoTrial.DiagnosticMetrics();phase=99;return;
   }
   phase++;next=Time.realtimeSinceStartup+.8f;
  }
  catch(Exception e){Plugin.Line("HANDOFF_FAILED phase="+phase+" "+e);Send("input clear");phase=99;}
 }
 private static void Census()
 {
  foreach(string type in new[]{"TrashBin","Dustbin","BuildableTrashBin","CustomerNPC","Courier","OpenWorldDeliveryNPC","PizzaBox"})
   UiInputAudit.Dump("Assembly-CSharp.dll","",type,"OnInteract","CanInteract","ThrowAwayItem","ThrowAwayItemsRpc","WouldAcceptPizza","WouldAcceptBeverage","TryDeliverPizzaDirectly","TryDeliverBeverageDirectly","TryDeliverContraband","IsPizzaAcceptableForOrder","GetActiveOrder","CanPack","AddWholePizzaInternal");
  foreach(var t in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
  {
   if(t.TryCast<TrashBin>()==null&&t.TryCast<Dustbin>()==null&&t.TryCast<BuildableTrashBin>()==null&&t.TryCast<CustomerNPC>()==null&&t.TryCast<Courier>()==null)continue;
   Plugin.Line("HANDOFF_TARGET type="+t.GetIl2CppType().Name+" name="+t.name+" position="+t.transform.position);
   foreach(var c in t.GetComponentsInChildren<Collider>())Plugin.Line("HANDOFF_VOLUME name="+c.name+" enabled="+c.enabled+" trigger="+c.isTrigger+" bounds="+c.bounds);
  }
  Plugin.Line("HANDOFF_CENSUS_COMPLETE");
 }
}
