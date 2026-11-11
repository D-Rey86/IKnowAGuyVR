using System;
using System.Runtime.InteropServices;
namespace IKnowAGuyVR.SubsystemProbe;

// Read the mandatory startup enumeration already performed by Unity. The
// original call/result/data pass through unchanged; no additional XR queries.
internal static unsafe class XrViewLimits
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int GetProc(ulong instance,byte* name,IntPtr* function);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Enumerate(ulong instance,ulong system,int type,uint capacity,uint* count,View* views);
    [StructLayout(LayoutKind.Sequential)]private struct View
    {internal int Type;internal IntPtr Next;internal uint Width,MaxWidth,Height,MaxHeight,Samples,MaxSamples;}
    private static GetProc originalGet;private static Enumerate originalEnumerate;
    private static readonly GetProc get=Get;private static readonly Enumerate enumerate=Read;
    private static readonly byte[] name=System.Text.Encoding.ASCII.GetBytes("xrEnumerateViewConfigurationViews");
    private static readonly IntPtr enumerationPointer=Marshal.GetFunctionPointerForDelegate(enumerate);
    internal static IntPtr Wrap(IntPtr original)
    {originalGet=Marshal.GetDelegateForFunctionPointer<GetProc>(original);return Marshal.GetFunctionPointerForDelegate(get);}
    private static int Get(ulong instance,byte* requested,IntPtr* function)
    {
        int result=originalGet(instance,requested,function);
        if(result!=0||requested==null||function==null||*function==IntPtr.Zero)return result;
        for(int i=0;i<name.Length;++i)if(requested[i]!=name[i])return result;
        if(requested[name.Length]!=0)return result;
        originalEnumerate=Marshal.GetDelegateForFunctionPointer<Enumerate>(*function);*function=enumerationPointer;return result;
    }
    private static int Read(ulong instance,ulong system,int type,uint capacity,uint* count,View* views)
    {
        int result=originalEnumerate(instance,system,type,capacity,count,views);
        if(result==0&&type==2&&capacity>=2&&count!=null&&*count>=2&&views!=null)
            VrGraphics.RecordLimits((int)Math.Max(views[0].Width,views[1].Width),(int)Math.Max(views[0].Height,views[1].Height),
                (int)Math.Min(views[0].MaxWidth,views[1].MaxWidth),(int)Math.Min(views[0].MaxHeight,views[1].MaxHeight));
        return result;
    }
}
