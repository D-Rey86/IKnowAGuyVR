using System;
using System.Numerics;
namespace IKnowAGuyVR.SubsystemProbe;
// Finite planar face of an authored control, in its own local frame.
internal static class PressSurface
{
 private static float At(Vector3 v,int axis)=>axis==0?v.X:axis==1?v.Y:v.Z;
 private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
 internal static bool Cross(Vector3 previous,Vector3 current,Vector3 min,Vector3 max,int axis,float sign,out Vector3 contact)
 {
  contact=default;if(!Finite(previous)||!Finite(current)||!Finite(min)||!Finite(max))return false;float face=sign>0?At(max,axis):At(min,axis);float old=(At(previous,axis)-face)*sign,now=(At(current,axis)-face)*sign;
  if(!float.IsFinite(old)||!float.IsFinite(now)||old<=0||now>0)return false;
  contact=Vector3.Lerp(previous,current,old/(old-now));
  for(int i=0;i<3;i++)if(i!=axis&&(At(contact,i)<At(min,i)||At(contact,i)>At(max,i)))return false;
  return true;
 }
 internal static bool Near(Vector3 p,Vector3 min,Vector3 max,int axis,float sign,Vector3 localMargin,bool keepBehind=false)
 {
  for(int i=0;i<3;i++)if(i!=axis&&(At(p,i)<At(min,i)-At(localMargin,i)||At(p,i)>At(max,i)+At(localMargin,i)))return false;
  float face=sign>0?At(max,axis):At(min,axis);float depth=(At(p,axis)-face)*sign;return (keepBehind?depth:MathF.Abs(depth))<=At(localMargin,axis);
 }
}
