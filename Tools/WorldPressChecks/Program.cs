using System;
using System.Numerics;
using IKnowAGuyVR.SubsystemProbe;
static void Check(bool v,string why){if(!v)throw new Exception(why);}
for(int axis=0;axis<3;axis++)foreach(float sign in new[]{-1f,1f})
{
 var min=new Vector3(-.03f,-.04f,-.005f);var max=-min;var face=sign>0?max:min;
 Vector3 Along(float d){var p=Vector3.Zero;p[axis]=face[axis]+sign*d;return p;}
 Check(PressSurface.Cross(Along(.04f),Along(-.04f),min,max,axis,sign,out var hit),"Fast front crossing missed");
 Check(Math.Abs(hit[axis]-face[axis])<1e-6,"Contact did not stay on actual face");
 Check(PressSurface.Cross(Along(.0001f),Along(-.0001f),min,max,axis,sign,out _),"Slow crossing missed");
 Check(!PressSurface.Cross(Along(-.01f),Along(.01f),min,max,axis,sign,out _),"Back-side withdrawal pressed");
 Check(!PressSurface.Cross(Along(0),Along(0),min,max,axis,sign,out _),"Stationary contact pressed");
 var a=Along(.01f);var b=Along(-.01f);a[(axis+1)%3]=b[(axis+1)%3]=1;
 Check(!PressSurface.Cross(a,b,min,max,axis,sign,out _),"Press outside button footprint accepted");
 Check(PressSurface.Near(Along(-.2f),min,max,axis,sign,new Vector3(.01f),true),"Deep penetration rearmed the button");
 Check(!PressSurface.Near(Along(.02f),min,max,axis,sign,new Vector3(.01f),true),"Withdrawal did not release latch");
}
Check(!PressSurface.Cross(new Vector3(float.NaN,0,1),new Vector3(0,0,-1),-Vector3.One,Vector3.One,2,1,out _),"Invalid tangent accepted");
foreach(double hz in new[]{72d,90d,120d,144d})
{
 var motion=new ToolMotion();double time=0;for(int i=0;i<24;i++){time+=1/hz;motion.Sample(Vector3.Zero,time);}
 Check(motion.Velocity==Vector3.Zero,"Stationary room-space pose acquired intent");
 for(int i=1;i<=12;i++){time+=1/hz;motion.Sample(new Vector3(0,0,-i*.1f/(float)hz),time);}
 Check(motion.Velocity.Z<-.09f,"Slow physical press lost intent");
 Check(!motion.Sample(new Vector3(20,0,0),time+1/hz),"Tracking discontinuity retained press intent");
}
Console.WriteLine("PASS: six face directions, fast/slow crossings, side/back/stationary negatives, release hysteresis, invalid input, room-space intent at 72/90/120/144 Hz and tracking jumps.");
