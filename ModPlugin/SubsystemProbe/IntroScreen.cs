using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using System.Reflection;
using UnityEngine.Rendering.Universal;
namespace IKnowAGuyVR.SubsystemProbe;

// Reuse the movie and its black backdrop as binocular planes. Both are legacy
// uGUI: copying their overlay pixels into asymmetric eye images cannot fuse.
internal static class IntroScreen
{
    private sealed class Surface
    {
        internal Canvas Canvas;internal RectTransform Rect;internal GameObject Source;
        private RenderMode mode;private Vector3 position,scale;private Quaternion rotation;private Vector2 size;
        private Graphic[] graphics;private Material[] originals,copies;
        internal void Bind(GameObject next)
        {
            if(next==null){if(graphics!=null||Source!=null)Restore();return;}
            if(Source!=null&&Source==next)return;
            Restore();Source=next;
            Canvas=next.GetComponent<Canvas>();Rect=next.GetComponent<RectTransform>();if(Canvas==null||Rect==null)return;
            mode=Canvas.renderMode;position=Rect.localPosition;rotation=Rect.localRotation;scale=Rect.localScale;size=Rect.sizeDelta;
            RefreshMaterials();
            Canvas.renderMode=RenderMode.WorldSpace;
        }
        internal void RefreshMaterials()
        {
            ClearMaterials();if(Source==null)return;
            var all=Source.GetComponentsInChildren<Graphic>(true);graphics=new Graphic[all.Length];originals=new Material[all.Length];copies=new Material[all.Length];
            for(int i=0;i<all.Length;i++)
            {
                graphics[i]=all[i];originals[i]=all[i].material;copies[i]=new Material(originals[i]);
                // Keep the existing UI shader, textures, stencil and colors.
                copies[i].SetInt("unity_GUIZTestMode",(int)UnityEngine.Rendering.CompareFunction.Always);
                graphics[i].material=copies[i];
#if IKAG_DIAGNOSTICS
                Plugin.Line("INTRO_MATERIAL name="+graphics[i].name+" shader="+copies[i].shader.name+" ztest="+copies[i].GetInt("unity_GUIZTestMode"));
                var r=graphics[i].rectTransform;Plugin.Line("INTRO_RECT name="+r.name+" rect="+r.rect+" scale="+r.localScale+" pos="+r.localPosition+" anchors="+r.anchorMin+","+r.anchorMax+" pivot="+r.pivot+" offset="+r.offsetMin+","+r.offsetMax);
#endif
            }
        }
        internal void Place(Matrix4x4 head,float distance,float width,float height,Vector2 center)
        {
            if(Canvas==null||Rect==null)return;
            var pixels=new Vector2(Screen.width,Screen.height);if(Rect.sizeDelta!=pixels)Rect.sizeDelta=pixels;
            Rect.localScale=new Vector3(width/pixels.x,height/pixels.y,1);
            Rect.SetPositionAndRotation(head.MultiplyPoint3x4(new Vector3(center.x,center.y,distance)),head.rotation);
        }
        private void ClearMaterials()
        {
            if(graphics!=null)for(int i=0;i<graphics.Length;i++)
            {if(graphics[i]!=null)graphics[i].material=originals[i];if(copies[i]!=null)Object.Destroy(copies[i]);}
            graphics=null;originals=copies=null;
        }
        internal void Restore()
        {
            ClearMaterials();
            if(Canvas!=null&&Rect!=null){Canvas.renderMode=mode;Rect.sizeDelta=size;Rect.localScale=scale;Rect.SetLocalPositionAndRotation(position,rotation);}
            Source=null;Canvas=null;Rect=null;graphics=null;originals=copies=null;
        }
    }
    private static readonly Surface movie=new(),backdrop=new();
    private static bool materialsDirty;
    internal static void Install(Harmony patch)
    {
        var hook=new HarmonyMethod(typeof(IntroScreen).GetMethod(nameof(ChildrenCreated),BindingFlags.Static|BindingFlags.NonPublic));
        patch.Patch(typeof(TutorialManager).GetMethod(nameof(TutorialManager.CreateIntroSubtitleUI)),postfix:hook);
        patch.Patch(typeof(TutorialManager).GetMethod(nameof(TutorialManager.CreateSkipPromptUI)),postfix:hook);
        patch.Patch(typeof(TutorialManager).GetMethod(nameof(TutorialManager.CreateSkipProgressBar)),postfix:hook);
    }
    // Resolve once for each movie. Use the feature's native activation API,
    // scoped to the existing eye render, instead of detouring RenderingData's ABI.
    private static OccaSoftware.LSPP.Runtime.LightScatteringRenderFeature[] scattering;
    private static bool[] scatteringActive;
    private static UniversalAdditionalCameraData worldData,heldData;
    private static bool worldPost,heldPost;
    private static bool effectsScoped;
    internal static void BeginEye()
    {
        if(movie.Canvas==null||scattering==null)return;
        effectsScoped=true;
        // The native screen overlay is drawn after grading. Preserve the movie's
        // original colors instead of running scene grading on its decoded pixels.
        if(worldData!=null){worldPost=worldData.renderPostProcessing;worldData.renderPostProcessing=false;}
        if(heldData!=null){heldPost=heldData.renderPostProcessing;heldData.renderPostProcessing=false;}
        for(int i=0;i<scattering.Length;i++)if(scattering[i]!=null)
        {scatteringActive[i]=scattering[i].isActive;scattering[i].SetActive(false);}
#if IKAG_DIAGNOSTICS
        if(!scatteringSuppressed){scatteringSuppressed=true;Plugin.Line("INTRO_SCATTERING_SUPPRESSED count="+scattering.Length);}
#endif
    }
    internal static void EndEye()
    {
        if(!effectsScoped)return;effectsScoped=false;
        if(worldData!=null)worldData.renderPostProcessing=worldPost;
        if(heldData!=null)heldData.renderPostProcessing=heldPost;
        for(int i=0;i<scattering.Length;i++)if(scattering[i]!=null)scattering[i].SetActive(scatteringActive[i]);
    }
    private static void ChildrenCreated()=>materialsDirty=true;
#if IKAG_DIAGNOSTICS
    private static int samples;
    private static bool scatteringSuppressed;
    internal static Canvas Canvas=>movie.Canvas;
#endif
    internal static void Prepare(Matrix4x4 head,float distance,float halfHeight,float aspect)
    {
        var tutorial=TutorialManager.Instance;
        var next=StereoTrial.GameplayActive&&tutorial!=null?tutorial.introVideoCanvas:null;
#if IKAG_DIAGNOSTICS
        if(movie.Source!=next)samples=0;
#endif
        bool changed=next!=null&&(movie.Source==null||movie.Source!=next);
        if(changed)
        {
            var camera=InteractionManager.LocalInstance?.interactionCamera;
            worldData=camera!=null?camera.GetComponent<UniversalAdditionalCameraData>():null;
            var overlay=StereoTrial.GameplayOverlay;heldData=overlay!=null?overlay.GetComponent<UniversalAdditionalCameraData>():null;
            var all=Resources.FindObjectsOfTypeAll<OccaSoftware.LSPP.Runtime.LightScatteringRenderFeature>();
            scattering=new OccaSoftware.LSPP.Runtime.LightScatteringRenderFeature[all.Length];scatteringActive=new bool[all.Length];
            for(int i=0;i<all.Length;i++)scattering[i]=all[i];
        }
        if(next==null)
        {
            EndEye();scattering=null;scatteringActive=null;
#if IKAG_DIAGNOSTICS
            if(scatteringSuppressed){scatteringSuppressed=false;Plugin.Line("INTRO_SCATTERING_RESTORED");}
#endif
        }
        movie.Bind(next);
        if(materialsDirty){materialsDirty=false;movie.RefreshMaterials();}
        var fade=next==null?null:ScreenFadeController._instance;
        backdrop.Bind(fade!=null&&fade.fadeImage!=null?fade.fadeImage.canvas.gameObject:null);
        if(movie.Canvas==null)return;
        movie.Place(head,distance,halfHeight*aspect*2,halfHeight*2,Vector2.zero);
        float behind=distance*1.01f;
        if(backdrop.Canvas!=null)
        {EyeScreenUI.FullFrustumPlane(behind,out var center,out var size);backdrop.Place(head,behind,size.x,size.y,center);}
#if IKAG_DIAGNOSTICS
        if(++samples==10)
        {
            var corners=new Vector3[4];var native=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);movie.Rect.GetWorldCorners(native);
            for(int i=0;i<4;i++)corners[i]=native[i];EyeScreenUI.IntroCorners(corners,head);
            var player=tutorial.introVideoPlayer;
            if(player!=null)Plugin.Line("INTRO_PLAYER mode="+player.renderMode+" camera="+player.targetCamera?.name+" target="+player.targetTexture?.name+" texture="+player.texture?.name+" aspect="+player.aspectRatio);
            foreach(var g in movie.Source.GetComponentsInChildren<Graphic>(true))
            {g.rectTransform.GetWorldCorners(native);for(int i=0;i<4;i++)corners[i]=native[i];Plugin.Line("INTRO_GRAPHIC "+g.name+" texture="+g.mainTexture?.name);EyeScreenUI.IntroCorners(corners,head);}
        }
#endif
    }
    internal static void Restore(){EndEye();movie.Restore();backdrop.Restore();scattering=null;scatteringActive=null;}
}
