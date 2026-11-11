using System;
using System.Numerics;
using IKnowAGuyVR.SubsystemProbe;
static void Check(bool v,string s){if(!v)throw new Exception(s);}
foreach(double hz in new[]{72d,90d,120d,144d})
{
 var m=new ToolMotion();var s=new ToolSwing();double t=0;int starts=0;bool sweep=false;
 for(int i=0;i<hz/4;i++){t+=1/hz;bool valid=m.Sample(Vector3.Zero,t);Check(!s.Sample(m,t,valid),"Stationary tool swung");}
 for(int i=1;i<=hz*.1;i++){t+=1/hz;bool valid=m.Sample(new Vector3(i*(float)(2/hz),0,0),t);sweep|=s.Sample(m,t,valid);if(s.Started)starts++;}
 Check(sweep&&starts==1,"Refresh-rate swing onset failed");Check(m.TryThrow(t,out var v)&&Math.Abs(v.X-2)<.01,"Throw velocity changed with refresh rate");
 s.Contact();t+=1/hz;Check(!s.Sample(m,t,true),"Same swing hit twice");
 Check(!m.Sample(new Vector3(20,0,0),t+1/hz),"Tracking jump retained intent");Check(!m.TryThrow(t+1/hz,out _),"Tracking jump threw");
 t+=.5;Check(!m.Sample(Vector3.Zero,t),"Tracking gap retained intent");Check(!m.TryThrow(t,out _),"Tracking gap threw");
 for(int i=0;i<hz/4;i++){t+=1/hz;m.Sample(Vector3.Zero,t);Check(!m.TryThrow(t,out _),"Stationary release threw");}
}
{
 var motion=new ToolMotion();var swing=new ToolSwing();double time=0;
 for(int i=0;i<30;i++){time+=1d/90;bool valid=motion.Sample(Vector3.Zero,time);swing.Sample(motion,time,valid);}
 int queries=0,starts=0;
 for(int i=1;i<=24;i++){time+=1d/90;bool valid=motion.Sample(new Vector3((i/2)*.04444444f,0,0),time);if(swing.Sample(motion,time,valid)&&motion.InstantVelocity.Length()>1)queries++;if(swing.Started)starts++;}
 Check(starts==1&&queries>=7,"Repeated runtime pose samples ended the swing early");
}
{
 var m=new ToolMotion();Check(!m.Sample(new Vector3(float.NaN,0,0),1),"Nonfinite pose accepted");Check(!m.TryThrow(1,out _),"Nonfinite pose retained throw");Check(!m.Sample(Vector3.Zero,double.NaN),"Nonfinite clock accepted");
}
Console.WriteLine("PASS: stationary control, swing onset/one contact, throw velocity at 72/90/120/144 Hz, repeated poses, tracking jumps/gaps and nonfinite input.");
for(int degrees=0;degrees<=180;degrees+=5)
{
 var oldLow=new Vector3(0,-.38f,0);var oldHigh=-oldLow;
 var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,degrees*MathF.PI/180);
 var translation=new Vector3(.03f,-.01f,.02f);var low=Vector3.Transform(oldLow,rotation)+translation;var high=Vector3.Transform(oldHigh,rotation)+translation;
 ToolCapsule.Sweep(oldLow,oldHigh,low,high,.034f,out var a,out var b,out var travel,out var radius);
 for(int tick=0;tick<=100;tick++)for(int k=0;k<=10;k++)
 {
  float t=tick/100f;var q=Quaternion.Slerp(Quaternion.Identity,rotation,t);var point=Vector3.Transform(Vector3.Lerp(oldLow,oldHigh,k/10f),q)+translation*t;
  var at=a+travel*t;var axis=b-a;float s=axis.LengthSquared()>1e-12?Math.Clamp(Vector3.Dot(point-at,axis)/axis.LengthSquared(),0,1):0;
  Check(Vector3.Distance(point,at+axis*s)<=radius-.034f+1e-6,"Rotating barrel escaped swept envelope");
 }
}
Console.WriteLine("PASS: full bat conservative envelope through 180 degrees, including fixed and translating centers.");
foreach(double hz in new[]{72d,90d,120d,144d})
{
 var bat=new BatMotion();double t=0;var head=new Vector3(0,1.6f,0);bool hit=false;
 for(int i=0;i<hz/4;i++){t+=1/hz;Check(!bat.Sample(Vector3.Zero,Vector3.UnitY*.76f,Vector3.Zero,head,t),"Stationary bat hit");}
 for(int i=1;i<hz*.3;i++){t+=1/hz;var p=Vector3.UnitX*(float)(i*.8/hz);hit|=bat.Sample(p,p+Vector3.UnitY*.76f,p,head,t);}
 Check(hit,"Gentle swing rejected");
 var start=Vector3.UnitX*.24f;
 for(int i=1;i<hz*.5;i++){t+=1/hz;var p=start+Vector3.UnitX*(float)(i*.4/hz);bool eligible=bat.Sample(p,p+Vector3.UnitY*.76f,p,head,t);if(i>hz*.12)Check(!eligible,"Earlier swing enabled slow touch");}
 bat.Reset();t+=1;
 for(int i=0;i<hz/2;i++){t+=1/hz;var p=Vector3.UnitZ*(float)(i*1.5/hz);Check(!bat.Sample(p,p+Vector3.UnitY*.76f,p,head+p,t),"Walking with the bat became a swing");}
 bat.Reset();t+=1;
 for(int i=0;i<hz/2;i++){t+=1/hz;Check(!bat.Sample(Vector3.Zero,Vector3.UnitY*.76f,Vector3.Zero,head+Vector3.UnitX*(float)(i/hz),t),"Head movement alone became a swing");}
 bat.Reset();t+=1;
 for(int i=0;i<hz/4;i++){t+=1/hz;bat.Sample(-Vector3.UnitY*.1f,Vector3.UnitY*.66f,Vector3.Zero,head,t);}
 hit=false;
 for(int i=1;i<=hz*.12;i++){t+=1/hz;var q=Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)(i/hz/.12*Math.PI));hit|=bat.Sample(Vector3.Transform(-Vector3.UnitY*.1f,q),Vector3.Transform(Vector3.UnitY*.66f,q),Vector3.Zero,head,t);}
 Check(hit,"Fast wrist rotation rejected as a tracking jump");
 t+=1/hz;Check(!bat.Sample(Vector3.One*20,Vector3.One*20+Vector3.UnitY*.76f,Vector3.One*20,head,t),"Grip tracking jump hit");
 t+=.5;Check(!bat.Sample(Vector3.Zero,Vector3.UnitY*.76f,Vector3.Zero,head,t),"Tracking gap hit");
 for(int i=0;i<hz/4;i++){t+=1/hz;var p=Vector3.UnitX*(i%2==0?.001f:-.001f);Check(!bat.Sample(p,p+Vector3.UnitY*.76f,p,head,t),"Jitter or tracking recovery hit");}
}
Console.WriteLine("PASS: current gentle contact, slow touch after a swing, room walking, head-only motion, fast wrist rotation, tracking jumps/gaps/recovery at 72/90/120/144 Hz.");
