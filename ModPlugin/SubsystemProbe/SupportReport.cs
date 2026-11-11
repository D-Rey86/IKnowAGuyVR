using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;

namespace IKnowAGuyVR.SubsystemProbe;

internal static class SupportReport
{
    // Unity OpenXR 1.17.1 OpenXRRuntime.cs ABI; read-only provider metadata.
    [DllImport("UnityOpenXR",EntryPoint="NativeConfig_GetRuntimeName")]
    [return:MarshalAs(UnmanagedType.U1)]private static extern bool RuntimeName(out IntPtr name);
    [DllImport("UnityOpenXR",EntryPoint="NativeConfig_GetRuntimeVersion")]
    [return:MarshalAs(UnmanagedType.U1)]private static extern bool RuntimeVersion(out ushort major,out ushort minor,out uint patch);
    private static Task<string> pending;
    internal static bool Busy=>pending!=null;
    internal static int Revision{get;private set;}
    internal static string Status{get;private set;}="Reports stay on this computer. Nothing is sent automatically.";
    internal static string LastFile{get;private set;}
    internal static string DirectoryPath=>Path.Combine(Paths.BepInExRootPath,"diagnostics","IKnowAGuyVR");
    internal static void Create(bool includeLogs)
    {
        if(Busy)return;
        try
        {
            // Unity/native state is read on the requesting (main) thread only.
            string snapshot=Snapshot(includeLogs),directory=DirectoryPath,assembly=typeof(Plugin).Assembly.Location;
            string[] paths={Paths.GameRootPath,Application.persistentDataPath,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
            string[] logs=includeLogs?new[]{Plugin.LogPath,Path.Combine(Paths.GameRootPath,"StereoTrial.log"),Path.Combine(Paths.BepInExRootPath,"LogOutput.log")}:Array.Empty<string>();
            pending=Task.Run(()=>SupportReportFiles.Write(directory,snapshot,assembly,logs,paths));
            Status="Creating local report...";Revision++;
        }
        catch(Exception e){Status="Report could not be created ("+e.GetType().Name+"). You can try again.";Revision++;}
    }
    // No allocations or queries while idle; filesystem/compression work stays off the game thread.
    internal static void Tick()
    {
        if(pending==null||!pending.IsCompleted)return;
        try{string name=pending.GetAwaiter().GetResult();LastFile=Path.Combine(DirectoryPath,name);Status="Saved: BepInEx/diagnostics/IKnowAGuyVR/\n"+name+"\nReview the ZIP before sharing it.";}
        catch(Exception e){Status="Report could not be saved ("+e.GetType().Name+"). Check free disk space and folder permissions, then retry.";}
        pending=null;Revision++;
    }
    private static string Snapshot(bool logs)
    {
        var b=new StringBuilder(4096);
        void Add(string key,object value)=>b.Append(key).Append(": ").Append(value is IFormattable f?f.ToString(null,CultureInfo.InvariantCulture):value?.ToString()??"unavailable").Append('\n');
        void Section(string title,Action capture){b.Append('\n').Append('[').Append(title).Append("]\n");try{capture();}catch(Exception e){Add("Section partially unavailable",e.GetType().Name);}}
        Add("Report format",1);Add("UTC",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        Add("VR mod",Plugin.Version);Add("Game",Application.version);Add("Unity",Application.unityVersion);Add("BepInEx",typeof(Paths).Assembly.GetName().Version);
#if IKAG_DIAGNOSTICS
        Add("Build","development diagnostics");
#else
        Add("Build","release");
#endif
        Add("Optional recent logs",logs);Add("Note","Snapshot taken in Settings; input/focus state may reflect the menu. No FPS claim or continuous recording.");
        Section("Hardware",()=>{Add("OS",SystemInfo.operatingSystem);Add("Process bits",IntPtr.Size*8);Add("CPU",SystemInfo.processorType);Add("Logical processors",SystemInfo.processorCount);Add("System RAM MiB",SystemInfo.systemMemorySize);Add("GPU",SystemInfo.graphicsDeviceName);Add("GPU vendor",SystemInfo.graphicsDeviceVendor);Add("GPU RAM MiB",SystemInfo.graphicsMemorySize);Add("Graphics API",SystemInfo.graphicsDeviceType);Add("Graphics version",SystemInfo.graphicsDeviceVersion);Add("Maximum texture size",SystemInfo.maxTextureSize);});
        Section("XR",()=>{Add("Session active",Probe.Active);Add("Session focused",Probe.Focused);Add("Gameplay stereo",StereoTrial.GameplayActive);Add("Menu stereo",StereoTrial.MenuActive);Add("Provider","Unity OpenXR (native provider integration)");Add("Controller transport ready",ControllerTransport.Ready);Add("Head tracked (cached)",ControllerTransport.HeadTracked);Hand("Left",ControllerTransport.Left);Hand("Right",ControllerTransport.Right);});
        Section("Runtime",()=>{Add("Runtime name",Probe.Active&&RuntimeName(out var name)?Marshal.PtrToStringAnsi(name):"unavailable");Add("Runtime version",Probe.Active&&RuntimeVersion(out var major,out var minor,out var patch)?$"{major}.{minor}.{patch}":"unavailable");});
        Section("Device names",()=>{Add("Caveat","Provider names may be generic; these are not serial numbers or proof of the active interaction profile.");Device(XRNode.Head);Device(XRNode.LeftHand);Device(XRNode.RightHand);});
        Section("Rendering",()=>{Add("Stereo waiting for tracking or eye passes",StereoTrial.WaitingForFrame);Add("Recommended width per eye",VrGraphics.RecommendedWidth);Add("Recommended height per eye",VrGraphics.RecommendedHeight);Add("Observed width per eye",VrGraphics.Width);Add("Observed height per eye",VrGraphics.Height);Add("Maximum resolution percent",VrGraphics.MaximumPercent);Add("Tracking scale",StereoTrial.TrackingScale);Add("Native quality level",QualitySettings.GetQualityLevel());Add("Native MSAA setting",QualitySettings.antiAliasing);Add("Native shadow distance",QualitySettings.shadowDistance);Add("Native vSync",QualitySettings.vSyncCount);Add("Application frame cap",Application.targetFrameRate);});
        Section("VR settings",()=>{
            Add("Resolution percent",VrSettings.Resolution.Value);Add("World scale percent",VrSettings.WorldScale.Value);Add("Stereo separation percent",VrSettings.Separation.Value);Add("Desktop eye mirror",VrSettings.Mirror.Value);
            Add("Snap turning",VrSettings.SnapTurn.Value);Add("Snap angle",VrSettings.SnapAngle.Value);Add("Smooth degrees per second",VrSettings.TurnSpeed.Value);Add("Teleport",VrSettings.Teleport.Value);Add("Gamepad mode",VrSettings.Gamepad.Value);
            Add("Left handed",VrSettings.LeftHanded.Value);Add("Swap motion sticks",VrSettings.SwapSticks.Value);Add("Seated",VrSettings.Seated.Value);Add("Seated height cm",VrSettings.SeatedHeight.Value);
            Add("Immersive UI configured",ImmersiveHud.ConfiguredImmersive);Add("Wrist size percent",VrSettings.WristScale.Value);Add("Belt enabled",PhysicalBelt.Enabled);Add("Belt below eyes metres",PhysicalBelt.Height);Add("Belt distance metres",PhysicalBelt.Distance);
        });
        Section("Interaction state",()=>{Add("Motion input enabled",MotionInput.Enabled);Add("Gameplay input ready",MotionInput.GameplayReady);Add("Controls armed",MotionInput.ControlsArmed);Add("Grab hand left",ObjectGrabs.LeftHand);Add("Left cursor visible",MotionPointer.CursorVisible(true));Add("Right cursor visible",MotionPointer.CursorVisible(false));Add("Wrist open",ImmersiveHud.WristOpen);Add("Immersive pointer owns UI",ImmersiveHud.PointerOwned);});
        return b.ToString();
        void Device(XRNode node){var device=InputDevices.GetDeviceAtXRNode(node);Add(node+" device valid",device.isValid);if(device.isValid){Add(node+" device name",device.name);Add(node+" manufacturer",device.manufacturer);}}
        void Hand(string name,ControllerTransport.Hand h){Add(name+" tracked (cached)",h.Tracked);Add(name+" grip",h.GripValue);Add(name+" trigger",h.TriggerValue);Add(name+" stick X",h.Stick.x);Add(name+" stick Y",h.Stick.y);}
    }
}
