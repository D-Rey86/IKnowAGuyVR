using System;
using System.Numerics;
using IKnowAGuyVR.SubsystemProbe;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
static void Settle(PunchGesture g,Vector3 p,ref double time)
{for(int i=0;i<12;++i){time+=.01;Check(!g.Sample(p,p,Vector3.UnitZ,true,time),"Stationary fist must not punch");}}
double now=0;var gesture=new PunchGesture();var point=Vector3.Zero;
Settle(gesture,point,ref now);
for(int i=0;i<20;++i){now+=.01;point.X+=.03f;Check(!gesture.Sample(point,point,Vector3.UnitZ,true,now),"Lateral sweep must not punch");}
Settle(gesture,point,ref now);
for(int i=0;i<20;++i){now+=.01;point.Z-=.03f;Check(!gesture.Sample(point,point,Vector3.UnitZ,true,now),"Retraction must not punch");}
Settle(gesture,point,ref now);int strikes=0;
for(int i=0;i<20;++i){now+=.01;point.Z+=.03f;if(gesture.Sample(point,point,Vector3.UnitZ,true,now))++strikes;}
Check(strikes==1,"One continuous thrust must produce exactly one proposal");
Settle(gesture,point,ref now);now+=.2;point.Z+=1;
Check(!gesture.Sample(point,point,Vector3.UnitZ,true,now),"Tracking gap/teleport must reset history");
now+=.01;point.Z+=.03f;Check(!gesture.Sample(point,point,Vector3.UnitZ,true,now),"Tracking recovery requires settling");
Settle(gesture,point,ref now);now+=.01;point.Z+=.03f;
Check(!gesture.Sample(point,point,Vector3.UnitZ,false,now),"Released grip must not punch");
var left=new PunchGesture();var right=new PunchGesture();double pairTime=0;
for(int i=0;i<12;++i){pairTime+=.01;left.Sample(Vector3.Zero,Vector3.Zero,Vector3.UnitZ,true,pairTime);right.Sample(Vector3.Zero,Vector3.Zero,Vector3.UnitZ,true,pairTime);}
pairTime+=.01;
Check(left.Sample(new Vector3(0,0,.03f),new Vector3(0,0,.03f),Vector3.UnitZ,true,pairTime)&&right.Sample(new Vector3(0,0,.03f),new Vector3(0,0,.03f),Vector3.UnitZ,true,pairTime),"Both hands must have independent gesture histories");
var headOnly=new PunchGesture();var bodyOnly=new PunchGesture();double motionTime=0;
for(int i=0;i<12;++i){motionTime+=.01;headOnly.Sample(Vector3.Zero,Vector3.Zero,Vector3.UnitZ,true,motionTime);bodyOnly.Sample(Vector3.Zero,Vector3.Zero,Vector3.UnitZ,true,motionTime);}
for(int i=1;i<=20;++i){motionTime+=.01;Check(!headOnly.Sample(new Vector3(0,0,i*.03f),Vector3.Zero,Vector3.UnitZ,true,motionTime),"Head motion with stationary fist must not punch");Check(!bodyOnly.Sample(Vector3.Zero,new Vector3(0,0,i*.03f),Vector3.UnitZ,true,motionTime),"Common head/body motion must not punch");}
Console.WriteLine("PASS: stationary, lateral, retract, thrust/rearm, tracking recovery, released grip, bilateral histories, head-only and common body movement.");

