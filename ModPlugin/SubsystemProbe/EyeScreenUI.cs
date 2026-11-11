using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;

namespace IKnowAGuyVR.SubsystemProbe;

// Reproject the existing screen UI's draw chain; no duplicate panel, mesh or RT.
internal static class EyeScreenUI
{
    private const float Distance = 2f;
    private static readonly Matrix4x4[] eyeFromHead = new Matrix4x4[2];
    private static float halfHeight, aspect;
    private static int eye = -1;
    private static bool root;
    private static Rect layout;
    private static Matrix4x4 projection, savedProjection;
    private static RectInt viewport;
    private struct Scope { internal bool Root; internal Rect Layout; internal Matrix4x4 Projection, SavedProjection; internal RectInt Viewport; }
    private static readonly List<Scope> scopes = new(16);
#if IKAG_DIAGNOSTICS
    private static bool reported;
#endif
    private static float deviceDepthScale,deviceDepthBias,farDeviceDepth;
    internal static bool InEye=>eye>=0;
#if IKAG_DIAGNOSTICS
    internal static int Draws { get; private set; }
    internal static int Scissors { get; private set; }
#endif
    internal static bool RayToScreen(Vector3 localOrigin,Vector3 localDirection,out Vector2 point,out float distance)
    {
        point=default;distance=0;
        if(halfHeight<=0||localDirection.z<=.0001f) return false;
        distance=(Distance-localOrigin.z)/localDirection.z;
        if(distance<=0) return false;
        var hit=localOrigin+localDirection*distance;
        point=new Vector2((hit.x/(halfHeight*aspect)+1)*.5f*Screen.width,(hit.y/halfHeight+1)*.5f*Screen.height);
        return point.x>=0&&point.x<=Screen.width&&point.y>=0&&point.y<=Screen.height;
    }
#if IKAG_DIAGNOSTICS
    internal static int CurrentEye=>eye;
    internal static void IntroCorners(Vector3[] corners,Matrix4x4 head)
    {
        var inverse=head.inverse;
        for(int i=0;i<2;i++)for(int j=0;j<corners.Length;j++)
        {var p=inverse.MultiplyPoint3x4(corners[j]);var clip=eyeFromHead[i]*new Vector4(p.x,p.y,p.z,1);Plugin.Line("INTRO_CORNER eye="+i+" corner="+j+" ndc="+new Vector3(clip.x/clip.w,clip.y/clip.w,clip.z/clip.w));}
    }
    internal static Vector3 ScreenTarget(Vector2 uiPoint)=>new Vector3((uiPoint.x/Screen.width*2-1)*halfHeight*aspect,(1-uiPoint.y/Screen.height*2)*halfHeight,Distance);
#endif

    internal static void Install(Harmony patch)
    {
        IntroScreen.Install(patch);
        var deviceConversion=GL.GetGPUProjectionMatrix(Matrix4x4.identity,false);
        deviceDepthScale=deviceConversion.m22;deviceDepthBias=deviceConversion.m23;
        farDeviceDepth=SystemInfo.usesReversedZBuffer?0:1;
        scopes.Clear(); root = false;
#if IKAG_DIAGNOSTICS
        Draws = Scissors = 0; reported = false;
#endif
        patch.Patch(typeof(RenderTreeManager).GetMethod("RenderSingleTree"), prefix: Hook(nameof(BeginTree)), postfix: Hook(nameof(EndTree)));
        UiEvaluationBridge.Install();
        patch.Patch(typeof(RenderChainCommand).GetMethod("PushScissor"), prefix: Hook(nameof(PushScissor)));
        patch.Patch(typeof(RenderChainCommand).GetMethod("PopScissor"), prefix: Hook(nameof(PopScissor)));
        UiCommandBridge.Install();
    }
    private static HarmonyMethod Hook(string name) => new(typeof(EyeScreenUI).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

    internal static void Prepare(RendererAudit.NativeParameter[] parameters, Matrix4x4 head)
    {
        ImmersiveHud.Prepare(head);
        InteractionReticle.Prepare(head);
        ComputerKeyboard.Prepare(head);
        aspect = (float)Screen.width / Screen.height;
        // Largest aspect-preserving panel inside both runtime frustums, with
        // a ten-percent NDC margin. No symmetric-FOV or parallel-eye assumption.
        halfHeight = float.PositiveInfinity;
        for (int i = 0; i < 2; ++i)
        {
            eyeFromHead[i] = parameters[i].Projection * parameters[i].View * head;
            var center = eyeFromHead[i] * new Vector4(0, 0, Distance, 1);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var direction = eyeFromHead[i] * new Vector4(x * aspect, y, 0, 0);
                    Fit(center.w * .9f - center.x, direction.x - direction.w * .9f);
                    Fit(center.w * .9f + center.x, -direction.x - direction.w * .9f);
                    Fit(center.w * .9f - center.y, direction.y - direction.w * .9f);
                    Fit(center.w * .9f + center.y, -direction.y - direction.w * .9f);
                }
        }
        if (!(halfHeight > 0) || !float.IsFinite(halfHeight)) throw new InvalidOperationException("No binocular screen UI fit");
        IntroScreen.Prepare(head,Distance,halfHeight,aspect);
    }
    private static void Fit(float available, float growth)
    {
        if (growth > 0) halfHeight = Math.Min(halfHeight, available / growth);
    }
    internal static void FullFrustumPlane(float distance,out Vector2 center,out Vector2 size)
    {
        var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=-min;
        for(int eye=0;eye<2;eye++)
        {
            var inverse=eyeFromHead[eye].inverse;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
            {
                var a=inverse*new Vector4(x,y,-1,1);var b=inverse*new Vector4(x,y,0,1);
                var near=new Vector3(a.x/a.w,a.y/a.w,a.z/a.w);var far=new Vector3(b.x/b.w,b.y/b.w,b.z/b.w);
                var ray=far-near;var p=near+ray*((distance-near.z)/ray.z);
                min=Vector2.Min(min,new Vector2(p.x,p.y));max=Vector2.Max(max,new Vector2(p.x,p.y));
            }
        }
        center=(min+max)*.5f;size=(max-min)*1.001f;
    }
    internal static void BeginEye(int index) { eye = index; IntroScreen.BeginEye(); }
    internal static void EndEye() { IntroScreen.EndEye(); ImmersiveHud.EndDraw();ComputerKeyboard.EndDraw();InteractionReticle.EndDraw();eye = -1; root = false; scopes.Clear(); }

    private static void BeginTree(RenderTreeManager __instance, RenderTree renderTree, RenderTexture nestedTreeRT)
    {
        scopes.Add(new Scope { Root = root, Layout = layout, Projection = projection, SavedProjection = savedProjection, Viewport = viewport });
        root = false;
        if ((eye<0&&!ComputerKeyboard.IsOpen&&!ImmersiveHud.Enabled)||nestedTreeRT!=null||renderTree.Pointer!=__instance.m_RootRenderTree.Pointer)return;
        var panel = __instance.panel.TryCast<BaseRuntimePanel>();
        if (panel == null || panel.drawsInCameras || panel.targetTexture != null) return;
        ImmersiveHud.Tree(panel);
        ComputerKeyboard.Tree(panel);
        if(eye<0)return;
        layout = panel.visualTree.layout;
        if (!(layout.width > 0 && layout.height > 0)) return;
        var pixelsToHead = Matrix4x4.zero;
        pixelsToHead.m00 = 2 * halfHeight * aspect / layout.width;
        pixelsToHead.m11 = -2 * halfHeight / layout.height;
        pixelsToHead.m03 = -halfHeight * aspect - layout.x * pixelsToHead.m00;
        pixelsToHead.m13 = halfHeight - layout.y * pixelsToHead.m11;
        pixelsToHead.m23 = Distance;
        pixelsToHead.m33 = 1;
        projection = eyeFromHead[eye] * pixelsToHead;
        root = true;
        InteractionReticle.Tree(panel);
    }
    private static void EndTree()
    {
        if (scopes.Count == 0) return;
        var previous = scopes[scopes.Count - 1]; scopes.RemoveAt(scopes.Count - 1);
        root = previous.Root; layout = previous.Layout; projection = previous.Projection;
        savedProjection = previous.SavedProjection; viewport = previous.Viewport;
    }
    internal static void BeginDraw(bool isSerializing, bool isRenderingNestedTreeRT)
    {
        if (!root || isSerializing || isRenderingNestedTreeRT) return;
        viewport = Utility.GetActiveViewport();
        savedProjection = Utility.GetUnityProjectionMatrix();
        // Retain both native UIR depth planes. Its far-plane geometry must fail
        // depth to increment/decrement stencil, rather than paint over children.
        GL.LoadProjectionMatrix(WithOverlayDepth(projection,layout));
#if IKAG_DIAGNOSTICS
        ShadyUiAudit.Projected(projection,eye);
#endif
#if IKAG_DIAGNOSTICS
        ++Draws;
        if (!reported) { reported = true; Plugin.Line($"EYE_UI_LAYOUT layout={layout} viewport={viewport} halfHeight={halfHeight:R} distance={Distance:R} overlayNdcDepth={savedProjection.m23 / savedProjection.m33:R}"); }
#endif
    }
    internal static Matrix4x4 WithOverlayDepth(Matrix4x4 target,Rect bounds)
    {
        var row=UIOverlayDepth.Row(
            new System.Numerics.Vector4(target.m30,target.m31,target.m32,target.m33),
            new System.Numerics.Vector4(savedProjection.m20,savedProjection.m21,savedProjection.m22,savedProjection.m23),
            new System.Numerics.Vector4(savedProjection.m30,savedProjection.m31,savedProjection.m32,savedProjection.m33),
            new System.Numerics.Vector4(bounds.xMin,bounds.yMin,bounds.xMax,bounds.yMax),deviceDepthScale,deviceDepthBias,farDeviceDepth);
        target.m20=row.X;target.m21=row.Y;target.m22=row.Z;target.m23=row.W;return target;
    }
    internal static void EndDraw(bool isSerializing, bool isRenderingNestedTreeRT)
    {
        ImmersiveHud.EndDraw();ComputerKeyboard.EndDraw();InteractionReticle.EndDraw();
        if (root && !isSerializing && !isRenderingNestedTreeRT) GL.LoadProjectionMatrix(savedProjection);
    }
    internal static void NativeCommand(IntPtr command,IntPtr drawParams)
    {
        ImmersiveHud.Command(command,drawParams);
        if(root)InteractionReticle.Command(command);
        ComputerKeyboard.Command(command,drawParams);
    }
    internal static Matrix4x4 GroupProjection(Matrix4x4 next){var old=projection;projection=next;return old;}
    internal static void RestoreScissor(Rect scissor){if(scissor==DrawParams.k_UnlimitedRect)Utility.DisableScissor();else Utility.SetScissorRect(ProjectScissor(scissor));}
    internal static bool WorldPanelProjection(Matrix4x4 headFromUi,Rect bounds,out Matrix4x4 result)
    {
        result=default;if(!root||eye<0)return false;result=eyeFromHead[eye]*headFromUi;
        // An anchored keyboard can be behind the viewer. Cull the whole group
        // through clip space instead of letting its original 2D layout appear.
        if(result.m33+Math.Min(result.m30*bounds.xMin,result.m30*bounds.xMax)+Math.Min(result.m31*bounds.yMin,result.m31*bounds.yMax)<=.0001f)
        {result=Matrix4x4.zero;result.m03=result.m13=-2;result.m33=1;}
        return true;
    }
    internal static bool ReticleProjection(Vector3 target,Rect bounds,out Matrix4x4 result)
    {
        result=default;if(!root||eye<0)return false;
        var matrix=Matrix4x4.zero;var center=bounds.center;
        float depth=Math.Max(.0001f,target.z)/Distance;
        matrix.m00=2*halfHeight*aspect/layout.width*depth;matrix.m11=-2*halfHeight/layout.height*depth;
        matrix.m03=target.x-center.x*matrix.m00;matrix.m13=target.y-center.y*matrix.m11;
        matrix.m23=target.z;matrix.m33=1;result=eyeFromHead[eye]*matrix;
        // A controller/UI contact may lie behind either eye after a physical
        // turn. Hide that dot through the existing group suppression path;
        // a nonpositive clip W cannot be used by the overlay depth mapping.
        return UIOverlayDepth.InFront(new System.Numerics.Vector4(result.m30,result.m31,result.m32,result.m33),new System.Numerics.Vector4(bounds.xMin,bounds.yMin,bounds.xMax,bounds.yMax));
    }
    private static bool PushScissor(DrawParams drawParams, Rect scissor)
    {
        if (!root) return true;
        var combined = Intersect(scissor, drawParams.scissor.Peek());
        drawParams.scissor.Push(combined);
        Utility.SetScissorRect(ProjectScissor(combined));
#if IKAG_DIAGNOSTICS
        ++Scissors;
#endif
        return false;
    }
    private static bool PopScissor(DrawParams drawParams)
    {
        if (!root) return true;
        drawParams.scissor.Pop();
        var previous = drawParams.scissor.Peek();
        if (previous == DrawParams.k_UnlimitedRect) Utility.DisableScissor();
        else Utility.SetScissorRect(ProjectScissor(previous));
        return false;
    }
    private static Rect Intersect(Rect a, Rect b)
    {
        float x = Math.Max(a.xMin, b.xMin), y = Math.Max(a.yMin, b.yMin);
        return new Rect(x, y, Math.Max(0, Math.Min(a.xMax, b.xMax) - x), Math.Max(0, Math.Min(a.yMax, b.yMax) - y));
    }
    private static RectInt ProjectScissor(Rect rect)
    {
        rect = Intersect(rect, layout);
        float minX = float.PositiveInfinity, minY = minX, maxX = float.NegativeInfinity, maxY = maxX;
        for (int x = 0; x < 2; ++x)
            for (int y = 0; y < 2; ++y)
            {
                var clip = projection * new Vector4(x == 0 ? rect.xMin : rect.xMax, y == 0 ? rect.yMin : rect.yMax, 0, 1);
                float px = viewport.x + (clip.x / clip.w + 1) * viewport.width * .5f;
                float py = viewport.y + (clip.y / clip.w + 1) * viewport.height * .5f;
                minX = Math.Min(minX, px); minY = Math.Min(minY, py); maxX = Math.Max(maxX, px); maxY = Math.Max(maxY, py);
            }
        return new RectInt(Mathf.FloorToInt(minX), Mathf.FloorToInt(minY), Math.Max(0, Mathf.CeilToInt(maxX) - Mathf.FloorToInt(minX)), Math.Max(0, Mathf.CeilToInt(maxY) - Mathf.FloorToInt(minY)));
    }
}
