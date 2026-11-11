using System;
using System.Buffers.Binary;
using System.Numerics;

namespace IKnowAGuyVR.Networking;

// Shared wire contract. No Unity, XR, native pointers or gameplay commands.
internal enum PoseKind : byte { Hello=1, Welcome=2, Body=3, Items=4 }
internal struct BonePose
{
    internal Vector3 Position, Scale;
    internal Quaternion Rotation;
    internal static BonePose Blend(in BonePose a,in BonePose b,float t)=>new()
    {Position=Vector3.Lerp(a.Position,b.Position,t),Rotation=Quaternion.Slerp(a.Rotation,b.Rotation,t),Scale=Vector3.Lerp(a.Scale,b.Scale,t)};
}
internal sealed class PoseFrame
{
    internal PoseKind Kind;
    internal byte Role;
    internal ulong Epoch,Sender,Object,Rig,Mask;
    internal uint Sequence;
    internal int Count;
    internal readonly BonePose[] Bones=new BonePose[PoseProtocol.BoneCount];
    internal readonly ulong[] Items=new ulong[PoseProtocol.MaxItems];
    internal void Copy(PoseFrame source)
    {
        Kind=source.Kind;Role=source.Role;Epoch=source.Epoch;Sender=source.Sender;Object=source.Object;
        Rig=source.Rig;Mask=source.Mask;Sequence=source.Sequence;Count=source.Count;
        Array.Copy(source.Bones,Bones,Bones.Length);Array.Copy(source.Items,Items,Items.Length);
    }
}
internal static class PoseProtocol
{
    internal const string Channel="IKAG.VRPose.v1";
    internal const uint Magic=0x50474B49;
    internal const int Version=1,BoneCount=55,MaxItems=16,Header=52,MaximumBytes=1160;
    internal const double Interval=.05,StaleAfter=.5,PlaybackDelay=.075;
    internal static bool Newer(uint next,uint previous)=>unchecked((int)(next-previous))>0;
    internal static bool Authorized(ulong session,bool server,ulong transportSender,ulong claimedSender,ulong objectOwner,ulong localClient,ulong serverClient,bool knownVrPeer)
        =>session!=0&&claimedSender==objectOwner&&claimedSender!=localClient&&
          (server?(knownVrPeer&&transportSender==claimedSender):transportSender==serverClient);
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    internal static bool Valid(in BonePose p)
    {
        float n=p.Rotation.LengthSquared();
        return Finite(p.Position)&&Finite(p.Scale)&&float.IsFinite(n)&&n>.99f&&n<1.01f&&
            Vector3.Abs(p.Position).X<=32&&Vector3.Abs(p.Position).Y<=32&&Vector3.Abs(p.Position).Z<=32&&
            p.Scale.X>0&&p.Scale.Y>0&&p.Scale.Z>0&&p.Scale.X<=8&&p.Scale.Y<=8&&p.Scale.Z<=8;
    }
    internal static int Encode(PoseFrame f,Span<byte> bytes)
    {
        if(bytes.Length<MaximumBytes||f.Kind<PoseKind.Hello||f.Kind>PoseKind.Items||f.Role>1)return 0;
        int count=0;
        if(f.Kind==PoseKind.Body){if(f.Mask==0||(f.Mask>>BoneCount)!=0)return 0;for(int i=0;i<BoneCount;i++)if((f.Mask&(1UL<<i))!=0){if(!Valid(f.Bones[i]))return 0;count++;}}
        if(f.Kind==PoseKind.Items){if(f.Count<0||f.Count>MaxItems)return 0;for(int i=0;i<f.Count;i++)if(!Valid(f.Bones[i]))return 0;count=f.Count;}
        bytes.Slice(0,Header).Clear();U32(bytes,0,Magic);bytes[4]=Version;bytes[5]=(byte)f.Kind;bytes[6]=f.Role;bytes[7]=(byte)count;
        U64(bytes,8,f.Epoch);U64(bytes,16,f.Sender);U64(bytes,24,f.Object);U64(bytes,32,f.Rig);U32(bytes,40,f.Sequence);U64(bytes,44,f.Kind==PoseKind.Body?f.Mask:0);
        int at=Header;
        if(f.Kind==PoseKind.Body){for(int i=0;i<BoneCount;i++)if((f.Mask&(1UL<<i))!=0)WritePose(bytes,ref at,f.Bones[i]);}
        else if(f.Kind==PoseKind.Items){for(int i=0;i<count;i++){U64(bytes,at,f.Items[i]);at+=8;WritePose(bytes,ref at,f.Bones[i]);}}
        return at;
    }
    internal static bool Decode(ReadOnlySpan<byte> bytes,PoseFrame f)
    {
        if(bytes.Length<Header||bytes.Length>MaximumBytes||R32(bytes,0)!=Magic||bytes[4]!=Version||bytes[6]>1)return false;
        var kind=(PoseKind)bytes[5];if(kind<PoseKind.Hello||kind>PoseKind.Items)return false;
        int count=bytes[7];ulong mask=R64(bytes,44);
        if(kind==PoseKind.Body){if(mask==0||(mask>>BoneCount)!=0||BitOperations.PopCount(mask)!=count||bytes.Length!=Header+20*count)return false;}
        else if(kind==PoseKind.Items){if(mask!=0||count>MaxItems||bytes.Length!=Header+28*count)return false;}
        else if(count!=0||mask!=0||bytes.Length!=Header)return false;
        f.Kind=kind;f.Role=bytes[6];f.Count=count;f.Epoch=R64(bytes,8);f.Sender=R64(bytes,16);f.Object=R64(bytes,24);f.Rig=R64(bytes,32);f.Sequence=R32(bytes,40);f.Mask=mask;
        int at=Header;
        if(kind==PoseKind.Body){for(int i=0;i<BoneCount;i++)if((mask&(1UL<<i))!=0){f.Bones[i]=ReadPose(bytes,ref at);if(!Valid(f.Bones[i]))return false;}}
        else if(kind==PoseKind.Items){for(int i=0;i<count;i++){f.Items[i]=R64(bytes,at);at+=8;for(int j=0;j<i;j++)if(f.Items[j]==f.Items[i])return false;f.Bones[i]=ReadPose(bytes,ref at);if(!Valid(f.Bones[i]))return false;}}
        return true;
    }
    private static void WritePose(Span<byte> b,ref int at,in BonePose p)
    {
        Short(b,ref at,p.Position.X,1000);Short(b,ref at,p.Position.Y,1000);Short(b,ref at,p.Position.Z,1000);
        var q=Quaternion.Normalize(p.Rotation);Short(b,ref at,q.X,32767);Short(b,ref at,q.Y,32767);Short(b,ref at,q.Z,32767);Short(b,ref at,q.W,32767);
        Scale(b,ref at,p.Scale.X);Scale(b,ref at,p.Scale.Y);Scale(b,ref at,p.Scale.Z);
    }
    private static BonePose ReadPose(ReadOnlySpan<byte> b,ref int at)
    {
        var p=new Vector3(ReadShort(b,ref at)/1000f,ReadShort(b,ref at)/1000f,ReadShort(b,ref at)/1000f);
        var q=new Quaternion(ReadShort(b,ref at)/32767f,ReadShort(b,ref at)/32767f,ReadShort(b,ref at)/32767f,ReadShort(b,ref at)/32767f);
        var s=new Vector3(ReadScale(b,ref at),ReadScale(b,ref at),ReadScale(b,ref at));
        // Reject corrupt quaternions before normalizing; normalization must not hide malformed input.
        if(q.LengthSquared()<.99f||q.LengthSquared()>1.01f)return default;
        return new BonePose{Position=p,Rotation=Quaternion.Normalize(q),Scale=s};
    }
    private static void Short(Span<byte>b,ref int at,float v,float scale){BinaryPrimitives.WriteInt16LittleEndian(b.Slice(at,2),(short)MathF.Round(v*scale));at+=2;}
    private static short ReadShort(ReadOnlySpan<byte>b,ref int at){short v=BinaryPrimitives.ReadInt16LittleEndian(b.Slice(at,2));at+=2;return v;}
    private static void Scale(Span<byte>b,ref int at,float v){BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(at,2),(ushort)Math.Clamp((int)MathF.Round(v*4096),1,32768));at+=2;}
    private static float ReadScale(ReadOnlySpan<byte>b,ref int at){float v=BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at,2))/4096f;at+=2;return v;}
    private static void U32(Span<byte>b,int at,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(at,4),v);
    private static void U64(Span<byte>b,int at,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(at,8),v);
    private static uint R32(ReadOnlySpan<byte>b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at,4));
    private static ulong R64(ReadOnlySpan<byte>b,int at)=>BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at,8));
}

// Bounded jitter buffer, receipt-time clock, no extrapolation. Missing tracking
// or a silent peer returns control to native animation after half a second.
internal sealed class PoseTimeline
{
    private readonly PoseFrame[] frames=new PoseFrame[8];private readonly double[] times=new double[8];
    private int start,count;private uint last;
    internal PoseTimeline(){for(int i=0;i<frames.Length;i++)frames[i]=new PoseFrame();}
    internal void Clear(){start=count=0;last=0;}
    internal bool Push(PoseFrame frame,double now)
    {
        if(!double.IsFinite(now))return false;
        if(count>0)
        {
            int latest=(start+count-1)%8;var old=frames[latest];
            if(frame.Object!=old.Object||frame.Epoch!=old.Epoch||frame.Sender!=old.Sender)Clear();
            else
            {
                if(!PoseProtocol.Newer(frame.Sequence,last)||now<times[latest])return false;
                if(frame.Rig!=old.Rig||frame.Mask!=old.Mask||now-times[latest]>PoseProtocol.StaleAfter)Clear();
            }
        }
        if(count==8){start=(start+1)%8;count--;}
        int i=(start+count)%8;frames[i].Copy(frame);times[i]=now;count++;last=frame.Sequence;return true;
    }
    internal bool Sample(double now,out PoseFrame a,out PoseFrame b,out float blend)
    {
        a=b=null;blend=0;if(count==0||now-times[(start+count-1)%8]>PoseProtocol.StaleAfter)return false;
        double target=now-PoseProtocol.PlaybackDelay;
        while(count>2&&times[(start+1)%8]<target){start=(start+1)%8;count--;}
        int lo=start,hi=start;for(int n=1;n<count;n++){hi=(start+n)%8;if(times[hi]>=target)break;lo=hi;}
        a=frames[lo];b=frames[hi];double span=times[hi]-times[lo];blend=span>0?(float)Math.Clamp((target-times[lo])/span,0,1):0;return true;
    }
}
