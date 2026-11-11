using System;
using System.Numerics;
namespace IKnowAGuyVR.SubsystemProbe;
// A single conservative sweep for a moving, rotating capsule. At each time the
// segment endpoints differ from the mean axis by at most a quarter axis delta.
internal static class ToolCapsule
{
 internal static void Sweep(Vector3 oldLow,Vector3 oldHigh,Vector3 low,Vector3 high,float radius,out Vector3 a,out Vector3 b,out Vector3 travel,out float expandedRadius)
 {
  var oldAxis=oldHigh-oldLow;var axis=high-low;var mean=(oldAxis+axis)*.25f;
  var center=(oldLow+oldHigh)*.5f;a=center-mean;b=center+mean;
  travel=(low+high-oldLow-oldHigh)*.5f;expandedRadius=radius+(axis-oldAxis).Length()*.25f;
 }
}
// Tracking-space samples; independent of locomotion, rendering and refresh rate.
internal sealed class ToolMotion
{
 private readonly Vector3[] points=new Vector3[32];private readonly double[] times=new double[32];
 private int head,count;private double last;
 internal Vector3 Previous{get;private set;}
 internal Vector3 Velocity{get;private set;}
 internal Vector3 InstantVelocity{get;private set;}
 internal float Travel{get;private set;}
 internal void Reset(){head=count=0;Velocity=InstantVelocity=default;Travel=0;}
 internal bool Sample(Vector3 p,double now,float maximumSpeed=12f)
 {
  if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||!double.IsFinite(now)){Reset();return false;}
  double dt=now-last;bool continuous=count>0&&dt>0&&dt<=.12;
  var previous=count>0?points[(head+31)%32]:p;
  if(!continuous||(p-previous).LengthSquared()>dt*dt*maximumSpeed*maximumSpeed){Reset();continuous=false;}
  Previous=continuous?previous:p;InstantVelocity=continuous?(p-previous)/(float)dt:default;
  points[head]=p;times[head]=now;head=(head+1)%32;count=Math.Min(count+1,32);last=now;
  int oldest=(head-count+32)%32;
  while(count>2&&now-times[(oldest+1)%32]>=.08){oldest=(oldest+1)%32;count--;}
  double span=now-times[oldest];var delta=p-points[oldest];Travel=delta.Length();Velocity=span>.015?delta/(float)span:default;
  return continuous;
 }
 internal bool TryThrow(double now,out Vector3 velocity)
 {velocity=Velocity;return count>1&&now-last>=0&&now-last<=.1&&Travel>=.06f&&velocity.LengthSquared()>=1f;}
}

// Contact is eligible only while the bat is actually moving relative to the
// player. No latched swing state: preparation cannot enable a later slow touch
// or consume the attack before impact. Validate tracking at the grip/head;
// rotation legitimately moves the far end faster than the controller itself.
internal sealed class BatMotion
{
 private readonly ToolMotion grip=new(),head=new(),low=new(),high=new(),relativeLow=new(),relativeHigh=new();
 private Vector3 axis;private bool haveAxis;
 internal bool Swinging{get;private set;}
 internal bool Started{get;private set;}
 internal Vector3 PreviousLow=>low.Previous;
 internal Vector3 PreviousHigh=>high.Previous;
 internal void Reset(){grip.Reset();head.Reset();low.Reset();high.Reset();relativeLow.Reset();relativeHigh.Reset();haveAxis=Swinging=Started=false;}
 private static bool Moving(ToolMotion m)=>m.Travel>=.035f&&m.Velocity.LengthSquared()>=.65f*.65f;
 private static bool Changed(ToolMotion m)=>m.InstantVelocity.LengthSquared()>=.35f*.35f;
 internal bool Sample(Vector3 lo,Vector3 hi,Vector3 hand,Vector3 headPosition,double now)
 {
  bool gripValid=grip.Sample(hand,now),headValid=head.Sample(headPosition,now);
  var nextAxis=hi-lo;bool continuous=!haveAxis||Vector3.Dot(axis,nextAxis)>=-.5f*MathF.Sqrt(axis.LengthSquared()*nextAxis.LengthSquared());
  axis=nextAxis;haveAxis=true;
  if(!gripValid||!headValid||!continuous){low.Reset();high.Reset();relativeLow.Reset();relativeHigh.Reset();}
  bool a=low.Sample(lo,now,float.PositiveInfinity),b=high.Sample(hi,now,float.PositiveInfinity);
  bool c=relativeLow.Sample(lo-headPosition,now,float.PositiveInfinity),d=relativeHigh.Sample(hi-headPosition,now,float.PositiveInfinity);
  bool lowMoving=Moving(low)&&Moving(relativeLow),highMoving=Moving(high)&&Moving(relativeHigh);
  bool moving=gripValid&&headValid&&continuous&&a&&b&&c&&d&&(lowMoving||highMoving);
  Started=moving&&!Swinging;Swinging=moving;
  return moving&&((lowMoving&&Changed(low)&&Changed(relativeLow))||(highMoving&&Changed(high)&&Changed(relativeHigh)));
 }
}

internal sealed class ToolSwing
{
 private bool armed,active,hit;private double settled=-1,expires;
 internal bool Started{get;private set;}
 internal void Reset(){armed=active=hit=false;settled=-1;Started=false;}
 internal void Contact(){hit=true;}
 internal bool Sample(ToolMotion motion,double now,bool valid)
 {
  Started=false;if(!valid){Reset();return false;}
  // Hammer behavior is unchanged. Bat contact uses BatMotion above.
  float speed=motion.Velocity.Length();
  if(speed<.6f)
  {
   active=false;if(settled<0)settled=now;
   if(now-settled>=.06){armed=true;hit=false;}return false;
  }
  settled=-1;
  if(armed&&speed>=1.2f&&motion.Travel>=.06f){armed=false;active=true;hit=false;expires=now+.6;Started=true;}
  return active&&!hit&&now<expires;
 }
}
