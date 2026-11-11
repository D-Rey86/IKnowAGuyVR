using UnityEngine;
using UnityEngine.InputSystem;
namespace IKnowAGuyVR.SubsystemProbe;

// Unity's desktop background gate can disable a pad while the headset remains
// focused. Override only that gate on the current pad, and restore its ownership.
internal static class VrGamepadDevice
{
    private static Gamepad pad;private static int frame=-1;private static bool backgroundOverride;
    internal static Gamepad Current
    {
        get
        {
            if(frame==Time.frameCount)return pad;frame=Time.frameCount;var current=Gamepad.current;
            if(pad?.Pointer!=current?.Pointer){Restore();pad=current;}
            return pad;
        }
    }
    internal static void Tick()
    {
        if(!VrSettings.Gamepad.Value){Restore();pad=null;frame=-1;return;}
        var current=Current;
        if(!Probe.Focused){Restore();return;}
        if(current!=null&&current.added&&!current.enabled&&current.disabledWhileInBackground)
        {
            InputSystem.EnableDevice(current);backgroundOverride=true;
#if IKAG_DIAGNOSTICS
            Plugin.Line("VR_GAMEPAD_BACKGROUND_OVERRIDE");
#endif
        }
    }
    private static void Restore()
    {
        if(!backgroundOverride)return;backgroundOverride=false;
        if(pad!=null&&pad.added&&!Application.isFocused&&!pad.canRunInBackground)
        {InputSystem.DisableDevice(pad,true);pad.disabledWhileInBackground=true;}
    }
}
