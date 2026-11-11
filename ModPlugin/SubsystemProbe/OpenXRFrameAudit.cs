using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class OpenXRFrameAudit
{
    private static readonly bool enabled=Environment.GetEnvironmentVariable("IKAG_XR_FRAME_POSE_AUDIT")=="1";
    private static bool finished;
    private static PlayerMovementController player;
    private static double firstReadAt;
    private static int firstReader,firstReadFixed;
    internal static readonly bool RefreshRender=enabled&&Environment.GetEnvironmentVariable("IKAG_XR_DIAGNOSTIC_RENDER_REFRESH")=="1";
    [DllImport("IKAGFrameAudit",EntryPoint="frame_audit_wrap",CharSet=CharSet.Unicode)]private static extern IntPtr WrapNative(IntPtr original,string output);
    [DllImport("IKAGFrameAudit",EntryPoint="frame_audit_publish")]private static extern int Publish(int frame,int gameplay,ref Vector3 position,ref Quaternion rotation,ref Vector3 anchorPosition,ref Quaternion anchorRotation,ref Vector2 look,ref Vector2 stick,float targetYaw,float headAgeMs,int headReader,int headFixed);
    [DllImport("UnityOpenXR",EntryPoint="NativeConfig_GetUseOpenXRPredictedTime")][return:MarshalAs(UnmanagedType.U1)]private static extern bool Prediction();
    [DllImport("UnityOpenXR",EntryPoint="NativeConfig_SetUseOpenXRPredictedTime")]private static extern void Prediction([MarshalAs(UnmanagedType.I1)]bool value);
    internal static IntPtr Wrap(IntPtr original)=>enabled?WrapNative(original,Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"),"frame-pose.csv")):original;
    internal static void Configure()
    {
        if(!enabled)return;
        string requested=Environment.GetEnvironmentVariable("IKAG_XR_DIAGNOSTIC_PREDICTION");
        if(requested=="1"||requested=="0")Prediction(requested=="1");
        Plugin.Line("FRAME_POSE_AUDIT prediction="+Prediction()+" diagnosticRequest="+requested);
    }
    internal static void Rendered(int frame,bool gameplay,Vector3 position,Quaternion rotation,Transform anchor)
    {
        if(!enabled||finished||anchor==null)return;
        if(!gameplay)player=null;
        else if(player==null){var interaction=InteractionManager.LocalInstance;if(interaction!=null)player=interaction.movementController;}
        var ap=anchor.position;var aq=anchor.rotation;
        var look=player==null?Vector2.zero:player.lookInput;var stick=ControllerTransport.Right.Stick;
        finished=Publish(frame,gameplay?1:0,ref position,ref rotation,ref ap,ref aq,ref look,ref stick,player==null?0:player.targetHorizontalRotation,(float)((Time.realtimeSinceStartupAsDouble-firstReadAt)*1000),firstReader,firstReadFixed)==0;
    }
    internal static void FirstRead(string caller)
    {
        if(!enabled||finished)return;
        firstReadAt=Time.realtimeSinceStartupAsDouble;
        firstReader=caller switch {"OnCamera"=>1,"TryInteractionPose"=>2,"TryTrackingPose"=>3,"TryHeadWorld"=>4,_=>0};
        firstReadFixed=Time.inFixedTimeStep?1:0;
    }
}
