using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.UIR;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class ShadyUiAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_SHADY_UI_AUDIT")=="1";
    private static bool capture;
    private static int phase,eyeMask;
    private static float next=20,originalScore;
    private static HUDManager hud;
    private static string pendingLabel;
    private static readonly Dictionary<int,object> projections=new();
    internal static void Init(){if(enabled){capture=Environment.GetEnvironmentVariable("IKAG_XR_RENDERDOC")=="1";Environment.SetEnvironmentVariable("IKAG_XR_RENDERDOC","0");}}
    private static object R(Rect r)=>new[]{r.x,r.y,r.width,r.height};
    private static string Directory=>Environment.GetEnvironmentVariable("XRSIM_DIR");
    private static void Shot(string name){pendingLabel=name;eyeMask=0;projections.Clear();File.WriteAllText(Path.Combine(Directory,"command.txt"),"shot "+name+"\n");}
    internal static void Projected(Matrix4x4 matrix,int eye)
    {
        if(!enabled||hud==null||pendingLabel==null||(eyeMask&(1<<eye))!=0)return;
        var viewport=Utility.GetActiveViewport();
        object Pixel(Vector2 p){var c=matrix*new Vector4(p.x,p.y,0,1);return new[]{(c.x/c.w+1)*viewport.width*.5f,(1-c.y/c.w)*viewport.height*.5f};}
        var colors=new List<object>();var bar=hud.reputationBarContainer;var marker=hud.reputationMarker.worldBound;
        for(int i=0;i<bar.childCount;++i)
        {
            var child=bar.ElementAt(i);if(child.Pointer==hud.reputationMarker.Pointer)continue;
            var r=child.worldBound;var color=child.resolvedStyle.backgroundColor;
            var point=new Vector2(r.x+r.width*.25f,r.center.y);
            if(Math.Abs(point.x-marker.center.x)<marker.width*.5f+3)point.x=r.x+r.width*.75f;
            colors.Add(new{color=new[]{color.r,color.g,color.b,color.a},pixel=Pixel(point),bounds=R(r)});
        }
        projections[eye]=new{eye,viewport=new[]{viewport.width,viewport.height},colors,marker=Pixel(marker.center),markerBounds=R(marker),bar=R(bar.worldBound),zone=hud.reputationZoneLabel.text};eyeMask|=1<<eye;
        if(eyeMask==3)File.WriteAllText(Path.Combine(Directory,pendingLabel+"-projection.json"),JsonSerializer.Serialize(projections,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Tick()
    {
        if(!enabled||phase>13||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            if(phase==0)
            {
                hud=HUDManager.LocalInstance;originalScore=hud.lastReputationScore;
                var rows=new List<object>();
                void Inspect(VisualElement v,int depth)
                {
                    if(v==null||depth>5)return;var s=v.resolvedStyle;var b=s.backgroundImage;var label=v.TryCast<Label>();
                    rows.Add(new{name=v.name,depth,layout=R(v.layout),world=R(v.worldBound),display=s.display.ToString(),visibility=s.visibility.ToString(),opacity=s.opacity,bg=s.backgroundColor.ToString(),color=s.color.ToString(),image=b.texture==null?null:b.texture.name,sprite=b.sprite==null?null:b.sprite.name,vector=b.vectorImage==null?null:b.vectorImage.name,rt=b.renderTexture==null?null:b.renderTexture.name,text=label==null?null:label.text,usage=v.usageHints.ToString()});
                    for(int i=0;i<v.childCount;++i)Inspect(v.ElementAt(i),depth+1);
                }
                Inspect(hud.reputationMeter,0);
                File.WriteAllText(Path.Combine(Directory,"shady-elements.json"),JsonSerializer.Serialize(new{score=originalScore,min=BipolarReputationManager.MinScore,max=BipolarReputationManager.MaxScore,rows},new JsonSerializerOptions{WriteIndented=true}));
                UiInputAudit.Dump("Assembly-CSharp.dll","","HUDManager","ShowReputationMeter","UpdateReputationDisplay");
                if(capture)RenderDocCapture.Trigger();Shot("shady-balanced");
            }
            else if(phase==1)hud.UpdateReputationDisplay(BipolarReputationManager.MinScore*.9f);
            else if(phase==2)Shot("shady-negative");
            else if(phase==3)hud.UpdateReputationDisplay(BipolarReputationManager.MaxScore*.9f);
            else if(phase==4)Shot("shady-positive");
            else if(phase==5){hud.UpdateReputationDisplay(originalScore);File.WriteAllText(Path.Combine(Directory,"command.txt"),"head rot 20 -15 10\nhead pos 0.1 1.65 0.05\n");}
            else if(phase==6)Shot("shady-head-moved");
            else if(phase==7)hud.ShowEscapeMenu();
            else if(phase==8)Shot("shady-pause");
            else if(phase==9)hud.OpenSettingsPanel();
            else if(phase==10)Shot("shady-settings");
            else if(phase==11){hud.CloseSettingsPanel();hud.HideEscapeMenu();File.WriteAllText(Path.Combine(Directory,"command.txt"),"head rot 0 0 0\nhead pos 0 1.6 0\n");}
            else if(phase==12)Shot("shady-restored");
            else {hud.UpdateReputationDisplay(originalScore);Plugin.Line("SHADY_UI_AUDIT_COMPLETE native score extremes, marker, head pose, pause/settings and restoration");}
            Plugin.Line("SHADY_UI_PHASE "+phase);++phase;next=Time.realtimeSinceStartup+.9f;
        }
        catch(Exception e){if(hud!=null)hud.UpdateReputationDisplay(originalScore);Plugin.Line("SHADY_UI_AUDIT_FAILED "+e);phase=14;}
    }
}
