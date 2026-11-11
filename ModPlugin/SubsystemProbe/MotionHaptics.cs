using System;
using BepInEx.Configuration;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionHaptics
{
 private static ConfigEntry<float> strength;
 private static float leftAt=-1,rightAt=-1;
 internal static void Initialize(ConfigFile config)=>strength=config.Bind("Motion","HapticStrength",1f,new ConfigDescription("Controller feedback strength. Set 0 to disable.",new AcceptableValueRange<float>(0,1)));
 internal static void Pulse(bool left,float amplitude=.3f,float duration=.035f)
 {
  if(!MotionInput.Enabled||strength==null||!Probe.Focused)return;
  float intensity=strength.Value;if(!float.IsFinite(intensity)||intensity<=0)return;
  float now=UnityEngine.Time.realtimeSinceStartup;ref float at=ref (left?ref leftAt:ref rightAt);
  if(now-at<.02f)return;
  bool sent=ControllerTransport.Pulse(left,amplitude*intensity,duration);if(sent)at=now;
#if IKAG_DIAGNOSTICS
  ObjectGrabAudit.Haptic(left,amplitude*strength.Value,duration,sent);
  ShovelPlayAudit.Haptic(left,amplitude,sent);
  PhysicalItemAudit.Haptic(left,sent);
#endif
 }
}
