using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class CashAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_CASH_AUDIT")=="1";
    private static readonly bool grips=Environment.GetEnvironmentVariable("IKAG_XR_CASH_GRIP_AUDIT")=="1";
    private static bool UseLeft=>grips&&index%2==0;
    private static string Button=>grips?"grip "+(UseLeft?"l":"r"):"trigger r";
    private static int phase,index;
    private static float next,value;
    private static Vector3 target;
    private static CashRegisterMachine register;
    private static MoneyCompartment compartment;
    private static POSKeyButton pos;private static string expected;
    private static void Send(string text)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),text+"\n");
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    internal static void Tick()
    {
        if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            float delay=.4f;
            switch(phase)
            {
                case 0:
                    register=UnityEngine.Object.FindObjectOfType<CashRegisterMachine>();Check(register!=null,"Register missing");
                    register.currentMode=CashRegisterMode.Cash;register.isPaymentPending=true;register.orderTotal=100;register.receivedAmount=200;register.changeToGive=100;register.changeGiven=0;register.hasReceivedMoney=true;
                    register.EnterMachineModeLocal();delay=2;break;
                case 1:register.drawer.OpenDrawer();register.drawer.SetAllCompartmentsActive(true);register.cashRegisterUI.SetMode(CashRegisterMode.Cash);register.UpdateUI();delay=1;break;
                case 2:
                    Check(CashMotion.Active,"Cash context inactive");
                    if(index>=register.drawer.allCompartments.Count){phase=10;return;}
                    compartment=register.drawer.allCompartments[index];value=compartment.Value;
                    var collider=compartment.GetComponentInChildren<Collider>();Check(collider!=null,"Missing compartment collider");
                    target=collider.bounds.center;
                    StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var zero,out var rotation);
                    var local=Quaternion.Inverse(rotation)*(target+Vector3.up*.45f-zero);
                    Send(FormattableString.Invariant($"hand {(UseLeft?"l":"r")} grip pose {local.x:R} {local.y:R} {-local.z:R} 0 0 0"));
                    phase=20;next=Time.realtimeSinceStartup+.4f;return;
                case 20:Send(MotionPointer.AimAtWorld(target,UseLeft));phase=3;next=Time.realtimeSinceStartup+.4f;return;
                case 3:if(!grips)Check(register.currentHoveredCompartment?.Pointer==compartment.Pointer,"Wrong cash hover expected="+compartment.name+" actual="+register.currentHoveredCompartment?.name);Send(Button+" 1");break;
                case 4:Check(Math.Abs(register.changeGiven-value)<.001f,"Cash change mismatch expected="+value+" actual="+register.changeGiven);Send(Button+" 0\ntrigger l pull 100");break;
                case 5:Check(Math.Abs(register.changeGiven)<.001f,"LT cash undo mismatch "+register.changeGiven);Plugin.Line("CASH_DENOMINATION_PASS "+value);index++;phase=2;next=Time.realtimeSinceStartup+.4f;return;
                case 10:register.currentMode=CashRegisterMode.CreditCard;register.orderTotal=999;register.SetAllPOSKeysActive(true);register.cashRegisterUI.SetMode(CashRegisterMode.CreditCard);register.UpdateUI();index=0;phase=30;next=Time.realtimeSinceStartup+1;return;
                case 30:
                    if(index>=register.posKeyButtons.Count){phase=40;return;}
                    pos=register.posKeyButtons[index];var key=pos.KeyType;
                    register.posInputBuffer=(key==POSKeyType.Backspace||key==POSKeyType.Enter)?"12.34":"";register.UpdatePOSEnteredAmount();register.UpdateUI();
                    expected=key==POSKeyType.Decimal?"0.":key==POSKeyType.Backspace?"12.3":key==POSKeyType.Enter?"":pos.GetKeyValue();
                    target=pos.GetComponentInChildren<Collider>().bounds.center;
                    StereoTrial.TryTrackingPose(Vector3.zero,Quaternion.identity,out var z,out var q);
                    var l=Quaternion.Inverse(q)*(target+Vector3.up*.45f-z);
                    Send(FormattableString.Invariant($"hand {(UseLeft?"l":"r")} grip pose {l.x:R} {l.y:R} {-l.z:R} 0 0 0"));break;
                case 31:Send(MotionPointer.AimAtWorld(target,UseLeft));break;
                case 32:if(!grips)Check(register.currentHoveredPOSKey?.Pointer==pos.Pointer,"Wrong physical keypad hover "+pos.KeyType);Send(Button+" 1");break;
                case 33:Send(Button+" 0");Check(register.posInputBuffer==expected,"Physical key "+pos.KeyType+" expected="+expected+" got="+register.posInputBuffer);Plugin.Line("PHYSICAL_POS_PASS "+pos.KeyType);index++;phase=30;next=Time.realtimeSinceStartup+.4f;return;
                case 40:register.TriggerExit();phase=11;next=Time.realtimeSinceStartup+2;return;
                case 11:Check(!CashMotion.Active&&!MachineInteractionBase.IsLocalPlayerInMachineMode,"Cash exit context retained");register.isPaymentPending=false;Plugin.Line("CASH_AUDIT_COMPLETE denominations="+index);if(grips){HUDManager.LocalInstance.ExitToMainMenu();MotionMenuAudit.AfterGameplay();}phase=99;return;
            }
            Plugin.Line("CASH_PHASE "+phase);phase++;next=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e){Plugin.Line("CASH_AUDIT_FAILED phase="+phase+" "+e);Send("trigger r 0\ntrigger l 0");phase=99;}
    }
}
