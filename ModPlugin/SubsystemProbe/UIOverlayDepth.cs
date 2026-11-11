using System;
using System.Numerics;
namespace IKnowAGuyVR.SubsystemProbe;

// Preserve native UIR's foreground and depth-failing stencil planes while x/y
// use a projective eye view. Scale distance from the device far plane by minW/W:
// it is positive and <= 1 everywhere in bounds, so masks remain inside the clip
// volume and retain their depth-fail stencil operations even with canted eyes.
internal static class UIOverlayDepth
{
    internal static float MinimumW(Vector4 w,Vector4 bounds)=>w.W+MathF.Min(w.X*bounds.X,w.X*bounds.Z)+MathF.Min(w.Y*bounds.Y,w.Y*bounds.W);
    internal static bool InFront(Vector4 w,Vector4 bounds){float minimum=MinimumW(w,bounds);return float.IsFinite(minimum)&&minimum>.0001f;}
    internal static Vector4 Row(Vector4 targetW,Vector4 nativeZ,Vector4 nativeW,Vector4 bounds,float deviceScale,float deviceBias,float farDepth)
    {
        float minW=MinimumW(targetW,bounds);
        if(!(minW>0)||!float.IsFinite(minW)||nativeW.W<=0||deviceScale==0)throw new InvalidOperationException("Invalid overlay depth projection");
        float scale=minW/nativeW.W;
        var nativeFromFar=nativeZ*deviceScale+nativeW*(deviceBias-farDepth);
        return (targetW*(farDepth-deviceBias)+nativeFromFar*scale)/deviceScale;
    }
}
