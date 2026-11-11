using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IKnowAGuyVR.SubsystemProbe;

// Replace only the bound camera's on-screen stack. Native off-screen/photo
// requests and all other cameras retain their original rendering path.
internal static class DesktopMirror
{
    private static bool installed,failed;
    private static HDROutputSettings output;
    internal static bool Supported=>!failed&&output!=null&&!output.active;
    internal static void Install()
    {
        if(installed)return;
        output=HDROutputSettings.main;
        new Harmony("iknowaguyvr.desktop-eye-mirror").Patch(
            typeof(UniversalRenderPipeline).GetMethod("RenderCameraStack"),
            prefix:new HarmonyMethod(typeof(DesktopMirror).GetMethod(nameof(Render),BindingFlags.NonPublic|BindingFlags.Static)));
        installed=true;
    }
    private static bool Render(ScriptableRenderContext context,Camera baseCamera)
    {
        try{return !StereoTrial.TryMirrorStack(context,baseCamera);}
        catch(Exception e){failed=true;Plugin.Line("DESKTOP_MIRROR_DISABLED "+e);return true;}
    }

    internal static void Present(ScriptableRenderContext context,RenderTexture source,Rect viewport)
    {
        // Fill the desktop without stretching the eye image. Crop around the
        // center of its actual viewport, and undo the existing XR-target Y flip.
        float aspect=(float)source.width*viewport.width/(source.height*viewport.height);
        float screenAspect=(float)Screen.width/Screen.height;
        float width=viewport.width,height=viewport.height;
        if(aspect>screenAspect)width*=screenAspect/aspect;
        else height*=aspect/screenAspect;
        var scale=new Vector2(width,-height);
        var offset=new Vector2(viewport.x+(viewport.width-width)*.5f,viewport.y+(viewport.height+height)*.5f);
        var commands=CommandBufferPool.Get("VR desktop mirror");
        try
        {
            commands.Blit(source,new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget),scale,offset);
            context.ExecuteCommandBuffer(commands);
            context.Submit();
        }
        finally{CommandBufferPool.Release(commands);}
    }
}
