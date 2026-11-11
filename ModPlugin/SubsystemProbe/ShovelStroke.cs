using System;
using System.Numerics;
namespace IKnowAGuyVR.SubsystemProbe;

// Intent is measured in tracking space. Changing the world/contact plane alone
// (walking, turning, or a sinking dig patch) cannot earn another stroke.
internal sealed class ShovelStroke
{
 private Vector3 previous,apex;
 private double previousTime;
 private float previousHeight;
 private bool seeded,armed;
 internal void Reset(){seeded=armed=false;}
#if IKAG_DIAGNOSTICS
 internal bool Armed=>armed;
 internal Vector3 Apex=>apex;
#endif
 internal bool Sample(Vector3 roomTip,float height,Vector3 roomNormal,float travel,double now)
 {
  double dt=now-previousTime;
  if(!seeded||dt<=0||dt>.12||(roomTip-previous).LengthSquared()>(float)(dt*dt*100))
  {previous=apex=roomTip;previousHeight=height;previousTime=now;seeded=true;armed=false;return false;}
  float prior=previousHeight;previous=roomTip;previousHeight=height;previousTime=now;
  if(height>=travel)
  {
   if(!armed||Vector3.Dot(roomTip-apex,roomNormal)>0)apex=roomTip;
   armed=true;
  }
  if(!armed||height>0||prior<=0)return false;
  armed=false;
  return Vector3.Dot(apex-roomTip,roomNormal)>=travel;
 }
}
