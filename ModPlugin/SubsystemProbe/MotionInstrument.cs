using UnityEngine;
using N=System.Numerics.Vector3;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionInstrument
{
 private static readonly ToolMotion motion=new();private static MusicalInstrument instrument;private static bool guitar,near,above;
 // Sound-hole rim and valve-cap regions measured from the shipping meshes.
 private static readonly Bounds strings=new(new Vector3(.000217f,.09f,-.04317f),new Vector3(.14f,.14f,.26f));
 private static readonly Bounds valves=new(new Vector3(-.027466f,.24f,-.04f),new Vector3(.10f,.09f,.26f));
 internal static bool OwnsGrip(bool left)=>near&&PhysicalHeldItems.Selected&&PhysicalHeldItems.Instrument==instrument&&left!=PhysicalHeldItems.Left&&PhysicalHeldItems.Other.Grip;
 internal static void Reset(){motion.Reset();instrument=null;near=above=false;}
 internal static void Input()
 {
  if(instrument!=PhysicalHeldItems.Instrument){Reset();instrument=PhysicalHeldItems.Instrument;guitar=instrument!=null&&instrument.name.Contains("Guitar");}
  if(instrument==null||!PhysicalHeldItems.Ready||!PhysicalHeldItems.TryRoot(out var p,out var q)||!StereoTrial.TryTrackingPose(PhysicalHeldItems.Other.Position,PhysicalHeldItems.Other.Rotation,out var hand,out var rotation)||!MotionBody.TryCarryFrame(!PhysicalHeldItems.Left,out _,out _,out _,out var pinch,out _))
  {motion.Reset();near=above=false;return;}
  var local=Quaternion.Inverse(q)*(hand+rotation*pinch-p);var scale=PhysicalHeldItems.Scale;local=new Vector3(local.x/scale.x,local.y/scale.y,local.z/scale.z);
  var area=guitar?strings:valves;near=area.SqrDistance(local)<.08f*.08f;
  double now=Time.realtimeSinceStartupAsDouble;bool valid=motion.Sample(new N(local.x,local.y,local.z),now);if(!valid){above=false;return;}
  bool play=false;
  if(guitar)
  {
   var old=motion.Previous;float x=strings.center.x;
   if((old.X-x)*(local.x-x)<=0&&Mathf.Abs(local.x-old.X)>.001f&&strings.Contains(local)&&Mathf.Abs(motion.InstantVelocity.X)>.3f)play=true;
  }
  else
  {
   if(local.y>valves.max.y+.025f)above=true;
   if(above&&valves.Contains(local)&&motion.InstantVelocity.Y<-.2f){play=true;above=false;}
  }
  if(play&&Time.time-instrument.lastSoundTime>=instrument.soundCooldown)
  {
   float before=instrument.lastSoundTime;instrument.TryPlaySound();if(instrument.lastSoundTime!=before){MotionHaptics.Pulse(!PhysicalHeldItems.Left,.16f,.03f);
#if IKAG_DIAGNOSTICS
    Plugin.Line("PHYSICAL_INSTRUMENT_PLAY guitar="+guitar+" left="+PhysicalHeldItems.Left);
#endif
   }
  }
 }
}
