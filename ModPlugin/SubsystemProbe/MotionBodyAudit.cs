using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionBodyAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_MOTION_BODY_AUDIT")=="1";
    private static int phase;private static float next=24;
    private static SkinnedMeshRenderer body,outfit;private static bool bodyEnabled,outfitEnabled;
    internal static void Tick()
    {
        if(!enabled||phase>9||!StereoTrial.GameplayActive||Time.realtimeSinceStartup<next)return;
        try
        {
            string command=null;
            if(phase==0)
            {
                var inv=InventoryManager.LocalInstance;inv.SetSelectedSlot(1);
                var bridge=inv.GetComponentInChildren<CharacterAnimatorBridge>(true);
                foreach(var skin in inv.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(skin.name=="Body"){body=skin;bodyEnabled=body.enabled;}
                    if(skin.name=="Outerwear"){outfit=skin;outfitEnabled=outfit.enabled;}
                    if(skin.sharedMesh==null)continue;
                    var mesh=skin.sharedMesh;
                    Plugin.Line($"BODY_MESH_CONTROL skin={skin.name} mesh={mesh.name} readable={mesh.isReadable} vertices={mesh.vertexCount}");

                }
                if(body==null||outfit==null)throw new InvalidOperationException("Measured body/outfit renderers missing");
                command="head rot 0 -55 0\nhand r grip pose 0.15 1.3 -0.45 0 0 0";
            }
            else if(phase==1)command="shot body-head-hidden";
            else if(phase==2)body.enabled=false;
            else if(phase==3)command="shot body-only-outfit";
            else if(phase==4){body.enabled=bodyEnabled;outfit.enabled=false;}
            else if(phase==5)command="shot body-only-skin";
            else if(phase==6){outfit.enabled=outfitEnabled;MotionBody.DiagnosticHideNeck=true;}
            else if(phase==7)command="shot body-neck-hidden";
            else if(phase==8){MotionBody.DiagnosticHideNeck=false;command="hands reset\nhead rot 0 0 0";InventoryManager.LocalInstance.SetSelectedSlot(7);}
            else if(phase==9)Plugin.Line("MOTION_BODY_AUDIT_COMPLETE");
            if(command!=null)File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"command.txt"),command+"\n");
            ++phase;next=Time.realtimeSinceStartup+1.2f;
        }
        catch(Exception e)
        {
            if(body!=null)body.enabled=bodyEnabled;if(outfit!=null)outfit.enabled=outfitEnabled;
            MotionBody.DiagnosticHideNeck=false;Plugin.Line("MOTION_BODY_AUDIT_FAILED "+e);phase=10;
        }
    }
}
