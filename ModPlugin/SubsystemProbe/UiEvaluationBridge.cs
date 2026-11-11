using System;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;

internal static unsafe class UiEvaluationBridge
{
    // Windows x64: Nullable<Rect> is larger than 8 bytes and passed indirectly.
    // Preserve that pointer without boxing it. All other argument types retain
    // their actual ABI, including the two single-byte booleans and exception **.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Evaluate(IntPtr device,IntPtr head,IntPtr material,IntPtr gradient,IntPtr shader,IntPtr scissor,Vector2 bounds,float pixels,
        [MarshalAs(UnmanagedType.U1)]bool serializing,TextureSlotCount slots,[MarshalAs(UnmanagedType.U1)]bool nested,IntPtr exception,IntPtr method);
    private static INativeDetour detour;private static Evaluate original;
    internal static string Failure{get;private set;}
    internal static void Install()
    {
        if(detour!=null)return;
        EngineBinaryContract.ValidateBinary();
        uint alignment=0;
        int size=IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore<Il2CppSystem.Nullable<Rect>>.NativeClassPtr,ref alignment);
        if(IntPtr.Size!=8||sizeof(Vector2)!=8||sizeof(TextureSlotCount)!=4||size!=20||alignment!=4)
            throw new InvalidOperationException("Unsupported native UI evaluation layout");
        var klass=IL2CPP.GetIl2CppClass("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements.UIR","UIRenderDevice");
        var method=IL2CPP.il2cpp_class_get_method_from_name(klass,"EvaluateChain",11);
        if(method==IntPtr.Zero)throw new MissingMethodException("UIRenderDevice.EvaluateChain");
        Failure=null;
        detour=INativeDetour.CreateAndApply(UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer,(Evaluate)Run,out original);
    }
    private static void Run(IntPtr device,IntPtr head,IntPtr material,IntPtr gradient,IntPtr shader,IntPtr scissor,Vector2 bounds,float pixels,bool serializing,TextureSlotCount slots,bool nested,IntPtr exception,IntPtr method)
    {
        bool begun=false;
        try{EyeScreenUI.BeginDraw(serializing,nested);begun=true;}
        catch(Exception e){Failure=e.ToString();}
        try{original(device,head,material,gradient,shader,scissor,bounds,pixels,serializing,slots,nested,exception,method);}
        finally{if(begun)try{EyeScreenUI.EndDraw(serializing,nested);}catch(Exception e){Failure=e.ToString();}}
    }
    internal static void Remove(){detour?.Dispose();detour=null;original=null;}
}
