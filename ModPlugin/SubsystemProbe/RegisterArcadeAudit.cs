using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class RegisterArcadeAudit
{
    internal static bool Complete {get;private set;}
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_REGISTER_ARCADE_AUDIT")=="1";
    private static int clicks; private static readonly EventCallback<PointerDownEvent> watch=(Action<PointerDownEvent>)(e=>{clicks++;Plugin.Line("MOLE_POINTER_TARGET "+e.elementTarget?.name+" pos="+e.position);});
    private static int phase=Environment.GetEnvironmentVariable("IKAG_XR_ARCADE_LIFECYCLE_ONLY")=="1"?21:0,key,step,lane,typed;
    private static float next;
    private static CashRegisterMachine register;
    private static ArcadeMiniGameManager arcade;
    private static readonly string[] keys={"1","2",".","3","4","5","backspace","5"};
    private static readonly string[] values={"1","12","12.","12.3","12.34","12.34","12.3","12.35"};
    private static void Check(bool v,string msg){if(!v)throw new InvalidOperationException(msg);}
    private static void Send(string s)=>File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),s+"\n");
    private static void Click(VisualElement element)
    {var e=PointerDownEvent.GetPooled();try{e.pointerId=0;e.pointerType=UnityEngine.UIElements.PointerType.mouse;e.button=0;e.pressedButtons=1;e.elementTarget=element;e.position=element.worldBound.center;element.SendEvent(e);}finally{e.Dispose();}}
    private static void Aim(VisualElement element)
    {
        var p=element.worldBound.center;var panel=element.panel.visualTree.worldBound;
        Send(MotionPointer.AimAt(new Vector2(p.x/panel.width*Screen.width,p.y/panel.height*Screen.height)));
    }
    internal static void Tick()
    {
        if(!enabled||phase==99||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            float delay=.4f;
            switch(phase)
            {
                case 0:
                    register=UnityEngine.Object.FindObjectOfType<CashRegisterMachine>();arcade=ArcadeMiniGameManager.LocalInstance;
                    Check(register!=null&&arcade!=null,"Native register/arcade absent");
                    register.isPaymentPending=true;register.currentMode=CashRegisterMode.CreditCard;register.posInputBuffer="";register.orderTotal=999;
                    register.EnterMachineModeLocal();delay=2;break;
                case 1:
                    Check(MachineInteractionBase.CurrentActiveMachine?.Pointer==register.Pointer,"Register entry failed");
                    register.isPaymentPending=true;register.cashRegisterUI.SetMode(CashRegisterMode.CreditCard);register.UpdateUI();var display=register.cashRegisterUI.posInputDisplay;Send(MotionPointer.AimAtWorld(register.cashRegisterUI.uiDocument.transform.TransformPoint(display.worldTransform.MultiplyPoint3x4(display.rect.center))));break;
                case 2:Send("trigger r pull 100");break;
                case 3:
                    Check(ComputerKeyboard.IsOpen,"Register laser click did not open numpad");
                    if(key>=keys.Length){phase=4;return;}
                    if(step==0){Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint(keys[key])));step=1;}
                    else if(step==1){Send("trigger r pull 100");step=2;}
                    else {Check(register.GetPOSInputBuffer()==values[key],"Register key "+keys[key]+" got "+register.GetPOSInputBuffer());Plugin.Line("REGISTER_KEY_PASS "+keys[key]+"="+values[key]);key++;step=0;}
                    next=Time.realtimeSinceStartup+delay;return;
                case 4:Send("shot register-numpad");step=0;phase=40;next=Time.realtimeSinceStartup+.4f;return;
                case 40:phase=4;Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("enter")));break;
                case 5:Send("trigger r pull 100");break;
                case 6:Check(register.GetPOSInputBuffer()==""&&!ComputerKeyboard.IsOpen,"Incorrect payment did not reset native buffer/close keypad");register.TriggerExit();delay=2;break;
                case 7:register.isPaymentPending=false;arcade=ArcadeMiniGameManager.LocalInstance;UiInputAudit.Dump("Assembly-CSharp.dll","","RacingMiniGame","UpdateRunning","Update");Check(!ComputerKeyboard.IsOpen,"Register exit retained keypad");Check(arcade.StartStandaloneGame(ArcadeMiniGameType.Racing),"Race start failed");delay=3;break;
                case 8:Check(arcade.racingGame.IsRunning,"Race not running");lane=arcade.racingGame.playerLane;Send("stick l 0 0");break;
                case 9:Send("stick l -1 0");break;
                case 10:Check(arcade.racingGame.playerLane==Math.Max(0,lane-1),"Left stick failed race lane initial="+lane+" actual="+arcade.racingGame.playerLane+" switch="+arcade.racingGame.laneSwitching+" running="+arcade.racingGame.IsRunning+" stick="+ControllerTransport.Left.Stick);Plugin.Line("ARCADE_RACE_LEFT_PASS");Send("stick l 0 0");break;
                case 11:Send("stick l 1 0");break;
                case 12:Check(arcade.racingGame.playerLane==Math.Min(arcade.racingGame.laneCentersX.Length-1,Math.Max(0,lane-1)+1),"Right stick failed race lane");Plugin.Line("ARCADE_RACE_RIGHT_PASS");Send("stick l 0 0");arcade.AbortActiveGame();delay=1;break;
                case 13:arcade=ArcadeMiniGameManager.LocalInstance;Check(arcade.StartStandaloneGame(ArcadeMiniGameType.Quiz),"Quiz start failed");delay=3;break;
                case 14:arcade.uiDocument.rootVisualElement.RegisterCallback(watch,TrickleDown.TrickleDown);Plugin.Line("QUIZ_RECT "+arcade.quizGame.answerButtons[0].worldBound+" panel="+arcade.uiDocument.rootVisualElement.panel.visualTree.worldBound+" screen="+Screen.width+"/"+Screen.height);Aim(arcade.quizGame.answerButtons[0]);break;
                case 15:Send("trigger r pull 100");break;
                case 16:MotionPointer.TraceWorldPick();MotionPointer.Trace();Plugin.Line("ARCADE_QUIZ_PHASE "+arcade.quizGame.phase);Check((int)arcade.quizGame.phase!=0,"Quiz laser click not received");arcade.AbortActiveGame();delay=1;break;
                case 17:arcade=ArcadeMiniGameManager.LocalInstance;Check(arcade.StartStandaloneGame(ArcadeMiniGameType.WhackAMole),"Mole start failed");delay=3;break;
                case 18:arcade.uiDocument.rootVisualElement.RegisterCallback(watch,TrickleDown.TrickleDown);Plugin.Line("MOLE_BOUNDS figure="+arcade.moleGame.cells[0].figure.worldBound+" panel="+arcade.uiDocument.rootVisualElement.panel.visualTree.worldBound);typed=arcade.moleGame.hits;arcade.moleGame.PopCell(arcade.moleGame.cells[0]);arcade.moleGame.cells[0].linger=5;Aim(arcade.moleGame.cells[0].figure);break;
                case 19:Aim(arcade.moleGame.cells[0].figure);step=0;break;
                case 20:if(step==0){Send("trigger r pull 100");step=1;next=Time.realtimeSinceStartup+.5f;return;}MotionPointer.Trace();Plugin.Line("MOLE_CLICKS "+clicks);Plugin.Line("ARCADE_MOLE_HAMMER "+arcade.moleGame.hammerTimer);Check(arcade.moleGame.hits==typed+1,"Mole pointer did not score hit state="+arcade.moleGame.cells[0].state+" hits="+arcade.moleGame.hits);arcade.AbortActiveGame();delay=1;break;
                case 21:arcade=ArcadeMiniGameManager.LocalInstance;arcade.typingGame.timeBase=60;Check(arcade.StartStandaloneGame(ArcadeMiniGameType.Typing),"Typing start failed");delay=3;break;
                case 22:arcade.uiDocument.rootVisualElement.RegisterCallback(watch,TrickleDown.TrickleDown);Check(ComputerKeyboard.IsOpen,"Typing keyboard absent");typed=arcade.typingGame.typedCount;Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint(arcade.typingGame.currentSentence[typed].ToString().ToLowerInvariant())));break;
                case 23:Send("trigger r pull 100");break;
                case 24:Check(arcade.typingGame.typedCount==typed+1,"Typing key not received");Plugin.Line("ARCADE_TYPING_PASS");Send("shot arcade-typing");break;
                case 25:Send(MotionPointer.AimAtWorld(ComputerKeyboard.KeyPoint("close")));phase=240;next=Time.realtimeSinceStartup+.4f;return;
                case 240:Send("trigger r pull 100");break;
                case 241:Check(!ComputerKeyboard.IsOpen,"Typing close failed");Aim(arcade.typingGame.sentenceLabel);break;
                case 242:Send("trigger r pull 100");break;
                case 243:Check(ComputerKeyboard.IsOpen,"Typing reopen failed");Send("btn b down");phase=26;next=Time.realtimeSinceStartup+1;return;
                case 26:Check(!arcade.IsGameActive&&!ComputerKeyboard.IsOpen,"B arcade exit failed");Send("btn b up");Plugin.Line("REGISTER_ARCADE_COMPLETE");Complete=true;phase=99;return;
            }
            Plugin.Line("REGISTER_ARCADE_PHASE "+phase);phase++;next=Time.realtimeSinceStartup+delay;
        }
        catch(Exception e){Plugin.Line("REGISTER_ARCADE_FAILED phase="+phase+" "+e);Send("trigger r 0");phase=99;}
    }
}

