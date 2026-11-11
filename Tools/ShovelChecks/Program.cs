using System;
using System.Numerics;
using IKnowAGuyVR.SubsystemProbe;
static void Check(bool v,string why){if(!v)throw new Exception(why);}
var g=new ShovelStroke();double t=0;
bool Sample(float roomY,float planeHeight){t+=.02;return g.Sample(new Vector3(0,roomY,0),planeHeight,Vector3.UnitY,.15f,t);}
for(int i=0;i<20;i++)Check(!Sample(.3f,.3f),"Stationary raised shovel dug");
int hits=0;for(int i=1;i<=18;i++)if(Sample(.3f-i*.02f,.3f-i*.02f))hits++;
Check(hits==1,"Insertion must produce exactly one stroke");
for(int i=0;i<100;i++)Check(!Sample(-.06f,-.06f),"Stationary soil contact repeated");
for(int i=0;i<18;i++)Check(!Sample(-.06f+i*.02f,-.06f+i*.02f),"Recovery stroke dug");
hits=0;for(int i=1;i<=18;i++)if(Sample(.3f-i*.02f,.3f-i*.02f))hits++;Check(hits==1,"Recovery did not rearm");
g.Reset();for(int i=0;i<20;i++)Sample(.3f,.3f);
for(int i=1;i<=35;i++)Check(!Sample(.3f,.3f-i*.01f),"World locomotion/contact-plane motion counted as hand intent");
g.Reset();for(int i=0;i<20;i++)Sample(.3f,.3f);
t+=.5;Check(!Sample(-.05f,-.05f),"Tracking gap granted progress");
g.Reset();for(int i=0;i<20;i++)Sample(.3f,.3f);
Check(!Sample(-.4f,-.4f),"Tracking jump granted progress");
g.Reset();for(int i=0;i<20;i++)Sample(.3f,.3f);
for(int i=0;i<50;i++){t+=.02;Check(!g.Sample(new Vector3(i*.02f,.3f,0),.3f,Vector3.UnitY,.15f,t),"Lateral motion dug");}
Console.WriteLine("PASS: insertion/recovery, stationary contact, locomotion/plane motion, tracking gaps/jumps and lateral motion.");
