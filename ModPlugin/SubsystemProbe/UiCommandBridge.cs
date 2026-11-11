using System;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
namespace IKnowAGuyVR.SubsystemProbe;

// Preserve native command execution and its exception out-pointer. Only commands
// owned by an existing VR UI group need managed object wrappers afterward.
internal static unsafe class UiCommandBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Execute(IntPtr command,IntPtr parameters,float pixelsPerPoint,IntPtr immediateException,IntPtr method);
    private static INativeDetour detour;
    private static Execute original;
    internal static string Failure{get;private set;}
    internal static void Install()
    {
        if(detour!=null)return;
        EngineBinaryContract.ValidateBinary();
        var klass=IL2CPP.GetIl2CppClass("UnityEngine.UIElementsModule.dll","UnityEngine.UIElements.UIR","RenderChainCommand");
        var method=IL2CPP.il2cpp_class_get_method_from_name(klass,"ExecuteNonDrawMesh",3);
        if(method==IntPtr.Zero)throw new MissingMethodException("RenderChainCommand.ExecuteNonDrawMesh");
        Failure=null;
        detour=INativeDetour.CreateAndApply(UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer,(Execute)Run,out original);
    }
    private static void Run(IntPtr command,IntPtr parameters,float pixelsPerPoint,IntPtr immediateException,IntPtr method)
    {
        original(command,parameters,pixelsPerPoint,immediateException,method);
        try{if(Failure!=null)return;EyeScreenUI.NativeCommand(command,parameters);}
        catch(Exception e){Failure=e.ToString();}
    }
    internal static UnityEngine.UIElements.UIR.DrawParams Parameters(IntPtr pointer){return Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get<UnityEngine.UIElements.UIR.DrawParams>(pointer);}
    internal static void Remove(){detour?.Dispose();detour=null;original=null;}
}
