using System;
using System.Numerics;
using IKnowAGuyVR.SubsystemProbe;
static void Check(bool v,string message){if(!v)throw new Exception(message);}
var bounds=new Vector4(0,0,2560,1440);var nativeZ=new Vector4(0,0,1.9960078f,-.9980039f);var nativeW=Vector4.UnitW;
int samples=0;
var dotBounds=new Vector4(1278,718,1282,722);
Check(!UIOverlayDepth.InFront(new Vector4(0,1.849263e-21f,0,-.45000458f),dotBounds),"Measured behind-head cursor was accepted");
Check(UIOverlayDepth.InFront(new Vector4(0,0,0,.45f),dotBounds),"Front cursor was culled");
Check(!UIOverlayDepth.InFront(new Vector4(0,0,0,float.NaN),dotBounds),"Nonfinite cursor was accepted");
Check(!UIOverlayDepth.InFront(new Vector4(.01f,0,0,-12.8f),dotBounds),"Cursor crossing eye plane was accepted");
foreach(var api in new[]{(scale:-.5f,bias:.5f,far:0f),(scale:.5f,bias:.5f,far:1f),(scale:1f,bias:0f,far:1f)})
foreach(var w in new[]{new Vector4(0,0,0,2),new Vector4(.0003f,-.0002f,0,1.8f),new Vector4(-.0004f,.0001f,0,2.2f),new Vector4(.0006f,.0003f,0,1.0f)})
{
 var z=UIOverlayDepth.Row(w,nativeZ,nativeW,bounds,api.scale,api.bias,api.far);
 for(int x=0;x<=16;++x)for(int y=0;y<=9;++y)foreach(float plane in new[]{0f,1f})
 {
  var p=new Vector4(x*160,y*160,plane,1);float clipW=Vector4.Dot(w,p),raw=Vector4.Dot(z,p)/clipW,device=api.scale*raw+api.bias;
  float depth=api.scale==1?(device+1)/2:device;float fromFar=api.far==0?depth:1-depth;
  Check(depth>=-2e-6&&depth<=1+2e-6,"UI geometry escaped clip volume");
  Check(plane==0?fromFar>.01f:fromFar<.01f,"Foreground/mask depth classification changed");
  if(w.X==0&&w.Y==0)Check(Math.Abs(raw-Vector4.Dot(nativeZ,p))<1e-5,"Parallel-eye native depth changed");
  ++samples;
 }
}
Console.WriteLine($"PASS: {samples} depth/stencil-plane checks across parallel/canted bounds and reversed/non-reversed device conversions.");
