using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.XR;
namespace IKnowAGuyVR.SubsystemProbe;

// Uses the same native provider registration contract as OpenXRInput 1.17.1.
// The provider owns sync, tracking spaces and runtime profile selection.
internal static class ControllerTransport
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct Binding { internal ulong Action; [MarshalAs(UnmanagedType.LPStr)] internal string Path; }
    [DllImport("UnityOpenXR", EntryPoint="OpenXRInputProvider_RegisterDeviceDefinition", CharSet=CharSet.Ansi)]
    private static extern ulong Device(string path,string profile,[MarshalAs(UnmanagedType.I1)] bool additive,uint flags,string name,string manufacturer,string serial);
    [DllImport("UnityOpenXR", EntryPoint="OpenXRInputProvider_CreateActionSet", CharSet=CharSet.Ansi)]
    private static extern ulong Set(string name,string localized,Guid guid);
    [DllImport("UnityOpenXR", EntryPoint="OpenXRInputProvider_CreateAction", CharSet=CharSet.Ansi)]
    private static extern ulong Action(ulong set,string name,string localized,uint type,Guid guid,string[] paths,uint count,[MarshalAs(UnmanagedType.I1)] bool additive,string[] usages,uint usageCount);
    [DllImport("UnityOpenXR", EntryPoint="OpenXRInputProvider_SuggestBindings", CharSet=CharSet.Ansi)]
    [return:MarshalAs(UnmanagedType.I1)] private static extern bool Suggest(string profile,Binding[] bindings,uint count);
    [DllImport("UnityOpenXR", EntryPoint="OpenXRInputProvider_AttachActionSets")]
    [return:MarshalAs(UnmanagedType.I1)] private static extern bool Attach();
    internal static void Register()
    {
        string[] paths={"/user/hand/left","/user/hand/right"};
        int accepted=0;
        foreach(string profile in new[]{"/interaction_profiles/oculus/touch_controller","/interaction_profiles/valve/index_controller"})
        {
            bool index=profile.Contains("valve");
            for(int h=0;h<2;++h)
                if(Device(paths[h],profile,false,(uint)(InputDeviceCharacteristics.HeldInHand|InputDeviceCharacteristics.TrackedDevice|InputDeviceCharacteristics.Controller|(h==0?InputDeviceCharacteristics.Left:InputDeviceCharacteristics.Right)),"IKAG Motion Controller","OpenXR","")==0) throw new InvalidOperationException("Controller definition failed: "+profile);
            // Both names must be unique across the instance, including sets
            // for profiles that are not currently active. Real runtimes reject
            // a shared localized name with XR_ERROR_LOCALIZED_NAME_DUPLICATED.
            string setName=index?"ikag_index":"ikag_touch";
            ulong set=Set(setName,index?"IKAG Motion Index":"IKAG Motion Touch",Guid.Empty);
            if(set==0) throw new InvalidOperationException("Controller set creation failed: "+setName);
            var bindings=new List<Binding>();
            void Add(string name,uint type,string usage,string left,string right)
            {
                for(int h=0;h<2;++h)
                {
                    string actionName=(h==0?"left_":"right_")+name;
                    ulong a=Action(set,actionName,actionName,type,Guid.Empty,new[]{paths[h]},1,false,new[]{usage},1);
                    if(a==0) throw new InvalidOperationException("Controller action creation failed: "+actionName);
                    bindings.Add(new Binding{Action=a,Path=paths[h]+(h==0?left:right)});
                }
            }
            Add("grippose",3,"Device","/input/grip/pose","/input/grip/pose");
            Add("aimpose",3,"Pointer","/input/aim/pose","/input/aim/pose");
            Add("stick",2,"Primary2DAxis","/input/thumbstick","/input/thumbstick");
            Add("stickclick",0,"Primary2DAxisClick","/input/thumbstick/click","/input/thumbstick/click");
            Add("primary",0,"PrimaryButton",index?"/input/a/click":"/input/x/click","/input/a/click");
            Add("secondary",0,"SecondaryButton",index?"/input/b/click":"/input/y/click","/input/b/click");
            Add("trigger",1,"Trigger","/input/trigger/value","/input/trigger/value");
            Add("grip",1,"Grip","/input/squeeze/value","/input/squeeze/value");
            Add("haptic",4,"Haptic","/output/haptic","/output/haptic");
            if(Suggest(profile,bindings.ToArray(),(uint)bindings.Count)) ++accepted;
            else Plugin.Line("MOTION_PROFILE_UNAVAILABLE "+profile);
        }
        if(accepted==0)throw new InvalidOperationException("No requested controller profile is supported by this runtime");
        if(!Attach()) throw new InvalidOperationException("Controller action attach failed");
        Plugin.Line("MOTION_ACTIONS_ATTACHED touch,index");
    }

    [StructLayout(LayoutKind.Explicit, Size=16)] private struct Span { [FieldOffset(0)] internal IntPtr Data; [FieldOffset(8)] internal int Length; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong IdAt(XRNode node);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Valid(ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Bool(ulong id,ref Span name,out byte value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte UInt(ulong id,ref Span name,out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Float(ulong id,ref Span name,out float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Vec2(ulong id,ref Span name,out Vector2 value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Vec3(ulong id,ref Span name,out Vector3 value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Quat(ulong id,ref Span name,out Quaternion value);
    [DllImport("UnityOpenXR",EntryPoint="OpenXRInputProvider_GetDeviceId",CharSet=CharSet.Ansi,CallingConvention=CallingConvention.Cdecl)]
    private static extern uint HapticDevice(InputDeviceCharacteristics characteristics,string name);
    [DllImport("UnityOpenXR",EntryPoint="OpenXRInputProvider_SendHapticImpulseNoISX",CallingConvention=CallingConvention.Cdecl)]
    private static extern void HapticImpulse(uint id,float amplitude,float frequency,float duration);
    internal struct Hand
    {
        internal bool Tracked, Primary, PrimaryPressed, Secondary, Trigger, Grip, Click;
        internal Vector2 Stick;
        internal Vector3 Position, AimPosition;
        internal Quaternion Rotation, AimRotation;
        internal float GripValue,TriggerValue;
    }
    private static IdAt idAt; private static Valid valid; private static Bool boolean; private static UInt integer; private static Float scalar; private static Vec2 vec2; private static Vec3 vec3; private static Quat quat;
    private static Span tracking,position,rotation,aimPosition,aimRotation,stick,primary,secondary,trigger,grip,click;
    private static ulong leftId,rightId,headId; private static float retry;
    private static uint leftHapticId,rightHapticId;
    private static bool headFound;
    private static int frame=-1;
    internal static Hand Left,Right;
    internal static Vector3 HeadPosition;
    internal static bool HeadTracked;
    internal static bool Ready {get;private set;}
    private static T Bind<T>(string name) where T:Delegate
    { var p=IL2CPP.il2cpp_resolve_icall(name); if(p==IntPtr.Zero) throw new MissingMethodException(name); return Marshal.GetDelegateForFunctionPointer<T>(p); }
    private static Span Usage(string name)=>new Span{Data=Marshal.StringToHGlobalUni(name),Length=name.Length};
    internal static void Initialize()
    {
        idAt=Bind<IdAt>("UnityEngine.XR.InputTracking::GetDeviceIdAtXRNode");
        valid=Bind<Valid>("UnityEngine.XR.InputDevices::IsDeviceValid");
        boolean=Bind<Bool>("UnityEngine.XR.InputDevices::TryGetFeatureValue_bool_Injected");
        integer=Bind<UInt>("UnityEngine.XR.InputDevices::TryGetFeatureValue_UInt32_Injected");
        scalar=Bind<Float>("UnityEngine.XR.InputDevices::TryGetFeatureValue_float_Injected");
        vec2=Bind<Vec2>("UnityEngine.XR.InputDevices::TryGetFeatureValue_Vector2f_Injected");
        vec3=Bind<Vec3>("UnityEngine.XR.InputDevices::TryGetFeatureValue_Vector3f_Injected");
        quat=Bind<Quat>("UnityEngine.XR.InputDevices::TryGetFeatureValue_Quaternionf_Injected");
        tracking=Usage("TrackingState"); position=Usage("DevicePosition");rotation=Usage("DeviceRotation");
        aimPosition=Usage("PointerPosition"); aimRotation=Usage("PointerRotation");stick=Usage("Primary2DAxis");
        primary=Usage("PrimaryButton");secondary=Usage("SecondaryButton");trigger=Usage("Trigger");grip=Usage("Grip");click=Usage("Primary2DAxisClick");
        Ready=true;
    }
    internal static bool Pulse(bool left,float amplitude,float duration)
    {var hand=left?Left:Right;uint id=left?leftHapticId:rightHapticId;if(!Ready||!Probe.Focused||!hand.Tracked||id==0||id==uint.MaxValue)return false;HapticImpulse(id,amplitude,0,duration);return true;}
    private static uint ResolveHaptic(ulong id)
    {
        if(id==0||valid(id)==0)return 0;
        // Match OpenXRInput.SendHapticImpulse(XR.InputDevice), OpenXR 1.17.1.
        // Provider IDs are distinct from Unity XR input-device IDs.
        return HapticDevice(InputDevices.GetDeviceCharacteristics(id),InputDevices.GetDeviceName(id));
    }
    internal static void Sample()
    {
        if(!Ready || frame==Time.frameCount) return; frame=Time.frameCount;
        if(Time.realtimeSinceStartup>=retry && (leftId==0 || rightId==0 || !headFound || valid(leftId)==0 || valid(rightId)==0 || valid(headId)==0))
        { leftId=idAt(XRNode.LeftHand);rightId=idAt(XRNode.RightHand);headId=idAt(XRNode.Head);if(valid(headId)==0)headId=idAt(XRNode.CenterEye);headFound=valid(headId)!=0;retry=Time.realtimeSinceStartup+1;
          try{leftHapticId=ResolveHaptic(leftId);rightHapticId=ResolveHaptic(rightId);}catch(Exception e){leftHapticId=rightHapticId=0;Plugin.Line("HAPTICS_UNAVAILABLE "+e.Message);}
        }
        HeadTracked=headFound&&valid(headId)!=0&&integer(headId,ref tracking,out var headState)!=0&&(headState&1)!=0&&vec3(headId,ref position,out HeadPosition)!=0&&float.IsFinite(HeadPosition.x)&&float.IsFinite(HeadPosition.y)&&float.IsFinite(HeadPosition.z);
        Left=Read(leftId,Left);Right=Read(rightId,Right);
    }
    private static bool Button(ulong id,ref Span usage)=>boolean(id,ref usage,out var value)!=0 && value!=0;
    private static Hand Read(ulong id,Hand previous)
    {
        Hand h=default;
        if(id==0 || valid(id)==0 || integer(id,ref tracking,out var state)==0 || (state&3)!=3) return h;
        h.Tracked=vec3(id,ref position,out h.Position)!=0 && quat(id,ref rotation,out h.Rotation)!=0 && vec3(id,ref aimPosition,out h.AimPosition)!=0 && quat(id,ref aimRotation,out h.AimRotation)!=0;
        if(!h.Tracked||!PoseValid(h.Position,h.Rotation)||!PoseValid(h.AimPosition,h.AimRotation)) return default;
        vec2(id,ref stick,out h.Stick);
        if(!float.IsFinite(h.Stick.x)||!float.IsFinite(h.Stick.y))h.Stick=Vector2.zero;
        h.Primary=Button(id,ref primary);h.Secondary=Button(id,ref secondary);
        bool triggerValid=scalar(id,ref trigger,out var t)!=0&&float.IsFinite(t);
        h.TriggerValue=triggerValid?Mathf.Clamp01(t):0;h.Trigger=triggerValid&&t>=(previous.Trigger?.45f:.65f);
        bool gripValid=scalar(id,ref grip,out var g)!=0&&float.IsFinite(g);
        h.GripValue=gripValid?Mathf.Clamp01(g):0;h.Grip=gripValid&&g>=(previous.Grip?.45f:.65f);h.Click=Button(id,ref click);
        h.PrimaryPressed=previous.Tracked&&h.Primary&&!previous.Primary;
        return h;
    }
    private static bool PoseValid(Vector3 p,Quaternion q)
    {float n=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;return float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&float.IsFinite(n)&&Math.Abs(n-1)<.001f;}
}
