namespace IKnowAGuyVR.SubsystemProbe;

// Action roles are independent of anatomical hands. Never swap runtime poses:
// grips, fingers, collisions, haptics and network packets keep physical sides.
internal static class VrControls
{
    internal static bool LeftHanded=>VrSettings.LeftHanded?.Value??false;
    internal static bool SwapSticks=>VrSettings.SwapSticks?.Value??false;
    internal static ControllerTransport.Hand Main=>LeftHanded?ControllerTransport.Left:ControllerTransport.Right;
    internal static ControllerTransport.Hand Off=>LeftHanded?ControllerTransport.Right:ControllerTransport.Left;
    internal static ControllerTransport.Hand Move=>SwapSticks?ControllerTransport.Right:ControllerTransport.Left;
    internal static ControllerTransport.Hand Turn=>SwapSticks?ControllerTransport.Left:ControllerTransport.Right;
    internal static ControllerTransport.Hand MainInput{get{var h=Main;var stick=Turn;h.Stick=stick.Stick;h.Click=stick.Click;return h;}}
    internal static ControllerTransport.Hand OffInput{get{var h=Off;var stick=Move;h.Stick=stick.Stick;h.Click=stick.Click;return h;}}
    internal static string MainTrigger=>LeftHanded?"L trigger":"R trigger";
    internal static string OffTrigger=>LeftHanded?"R trigger":"L trigger";
    internal static string MainPrimary=>LeftHanded?"L primary":"R A";
    internal static string MainSecondary=>LeftHanded?"L secondary":"R B";
    internal static string OffPrimary=>LeftHanded?"R A":"L primary";
    internal static string OffSecondary=>LeftHanded?"R B":"L secondary";
    internal static string MoveStick=>SwapSticks?"R stick":"L stick";
    internal static string TurnStick=>SwapSticks?"L stick":"R stick";
    internal static string MainTriggerTag=>LeftHanded?"[L trigger]":"[R trigger]";
    internal static string MainSecondaryTag=>LeftHanded?"[L secondary]":"[R B]";
    internal static string MoveClickTag=>SwapSticks?"[R stick click]":"[L stick click]";
    internal static string OffPrimaryTag=>LeftHanded?"[R A]":"[L primary]";
    private static bool initialized,lastLeft,lastSwap;
    internal static void Tick()
    {
        if(initialized&&lastLeft==LeftHanded&&lastSwap==SwapSticks)return;
        initialized=true;lastLeft=LeftHanded;lastSwap=SwapSticks;
        MotionInput.ModeChanged();ObjectGrabs.Suspend();MotionPointer.ResetPreferredHand();
    }
}
