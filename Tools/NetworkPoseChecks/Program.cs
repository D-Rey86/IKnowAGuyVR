using System.Numerics;
using System.Buffers.Binary;
using IKnowAGuyVR.Networking;

int assertions=0;
void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
var frame=new PoseFrame{Kind=PoseKind.Body,Role=1,Epoch=7,Sender=9,Object=11,Rig=13,Sequence=1,Mask=(1UL<<55)-1};
var random=new Random(4107);var bytes=new byte[PoseProtocol.MaximumBytes];var decoded=new PoseFrame();
float maxPosition=0,maxRotation=0,maxScale=0;
for(int iteration=0;iteration<200;iteration++)
{
    for(int i=0;i<55;i++)frame.Bones[i]=new BonePose{Position=new Vector3((float)random.NextDouble()*6-3,(float)random.NextDouble()*6-3,(float)random.NextDouble()*6-3),Scale=new Vector3(.1f+(float)random.NextDouble()*3),Rotation=Quaternion.CreateFromYawPitchRoll((float)random.NextDouble()*6,(float)random.NextDouble()*6,(float)random.NextDouble()*6)};
    int length=PoseProtocol.Encode(frame,bytes);Check(length==1152,"Body packet bound");Check(PoseProtocol.Decode(bytes.AsSpan(0,length),decoded),"Round trip");
    Check(decoded.Mask==frame.Mask&&decoded.Epoch==7&&decoded.Sender==9&&decoded.Object==11&&decoded.Rig==13,"Identity round trip");
    for(int i=0;i<55;i++)
    {
        maxPosition=Math.Max(maxPosition,Vector3.Distance(frame.Bones[i].Position,decoded.Bones[i].Position));
        maxScale=Math.Max(maxScale,Vector3.Distance(frame.Bones[i].Scale,decoded.Bones[i].Scale));
        maxRotation=Math.Max(maxRotation,1-Math.Abs(Quaternion.Dot(frame.Bones[i].Rotation,decoded.Bones[i].Rotation)));
    }
}
Check(maxPosition<.00087f&&maxScale<.00022f&&maxRotation<.000001f,"Quantization error bounds");
int bodyLength=PoseProtocol.Encode(frame,bytes);var pristine=(byte[])bytes.Clone();
for(int n=0;n<bodyLength;n++)Check(!PoseProtocol.Decode(bytes.AsSpan(0,n),decoded),"Truncated packet accepted");
bytes[4]=2;Check(!PoseProtocol.Decode(bytes.AsSpan(0,bodyLength),decoded),"Version mismatch accepted");Array.Copy(pristine,bytes,bytes.Length);
bytes[5]=255;Check(!PoseProtocol.Decode(bytes.AsSpan(0,bodyLength),decoded),"Unknown kind accepted");Array.Copy(pristine,bytes,bytes.Length);
bytes[7]=0;Check(!PoseProtocol.Decode(bytes.AsSpan(0,bodyLength),decoded),"Wrong bone count accepted");Array.Copy(pristine,bytes,bytes.Length);
BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(44,8),ulong.MaxValue);Check(!PoseProtocol.Decode(bytes.AsSpan(0,bodyLength),decoded),"Out-of-range bone mask accepted");Array.Copy(pristine,bytes,bytes.Length);
Array.Clear(bytes,PoseProtocol.Header+6,8);Check(!PoseProtocol.Decode(bytes.AsSpan(0,bodyLength),decoded),"Zero quaternion accepted");Array.Copy(pristine,bytes,bytes.Length);
frame.Bones[0].Position=new Vector3(float.NaN);Check(PoseProtocol.Encode(frame,bytes)==0,"NaN encoded");frame.Bones[0]=decoded.Bones[1];frame.Bones[0].Position=new Vector3(33);Check(PoseProtocol.Encode(frame,bytes)==0,"Overflow silently encoded");frame.Bones[0]=decoded.Bones[1];
frame.Kind=PoseKind.Items;frame.Count=16;
for(int i=0;i<16;i++)frame.Items[i]=(ulong)(100+i);
int itemLength=PoseProtocol.Encode(frame,bytes);Check(itemLength==500&&PoseProtocol.Decode(bytes.AsSpan(0,itemLength),decoded),"Full held-item packet");
BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(PoseProtocol.Header+28,8),100);Check(!PoseProtocol.Decode(bytes.AsSpan(0,itemLength),decoded),"Duplicate item identity accepted");
frame.Count=0;itemLength=PoseProtocol.Encode(frame,bytes);Check(itemLength==PoseProtocol.Header&&PoseProtocol.Decode(bytes.AsSpan(0,itemLength),decoded),"Empty-hand update");
frame.Kind=PoseKind.Hello;int helloLength=PoseProtocol.Encode(frame,bytes);Check(helloLength==PoseProtocol.Header&&PoseProtocol.Decode(bytes.AsSpan(0,helloLength),decoded),"Handshake");
Check(PoseProtocol.Newer(0,uint.MaxValue)&&!PoseProtocol.Newer(uint.MaxValue,0)&&!PoseProtocol.Newer(4,4),"Sequence wrap/duplicates");
Check(PoseProtocol.Authorized(1,true,9,9,9,0,0,true),"Valid client relay denied");
Check(!PoseProtocol.Authorized(1,true,10,9,9,0,0,true),"Spoofed sender accepted");
Check(!PoseProtocol.Authorized(1,true,9,9,8,0,0,true),"Wrong object owner accepted");
Check(!PoseProtocol.Authorized(1,true,9,9,9,0,0,false),"Unnegotiated peer accepted");
Check(PoseProtocol.Authorized(1,false,0,9,9,7,0,false),"Host relay denied");
Check(!PoseProtocol.Authorized(1,false,9,9,9,7,0,false),"Client bypassed relay");
Check(!PoseProtocol.Authorized(1,false,0,7,7,7,0,false),"Remote pose changed local player");
Check(!PoseProtocol.Authorized(0,false,0,9,9,7,0,false),"Uninitialized session accepted");
var timeline=new PoseTimeline();frame.Kind=PoseKind.Body;frame.Count=1;frame.Mask=1;frame.Sequence=uint.MaxValue-1;frame.Bones[0]=new BonePose{Position=Vector3.Zero,Rotation=Quaternion.Identity,Scale=Vector3.One};
Check(timeline.Push(frame,1),"First snapshot");frame.Sequence++;frame.Bones[0].Position=Vector3.One;Check(timeline.Push(frame,1.1),"Second snapshot");
Check(timeline.Sample(1.125,out var a,out var b,out float blend)&&Math.Abs(blend-.5)<.0001,"Interpolation clock");Check(Vector3.Distance(BonePose.Blend(a.Bones[0],b.Bones[0],blend).Position,new Vector3(.5f))<.0001,"Interpolation result");
Check(!timeline.Push(frame,1.11),"Duplicate applied");frame.Sequence--;Check(!timeline.Push(frame,1.12),"Reordered packet applied");frame.Sequence=0;Check(timeline.Push(frame,1.2),"Sequence wrap rejected");
Check(!timeline.Sample(1.71,out _,out _,out _),"Stale peer froze instead of restoring native");
frame.Epoch++;frame.Sequence=0;Check(timeline.Push(frame,2),"New session rejected");
frame.Sequence=1;Check(!timeline.Push(frame,1.9),"Backwards receipt clock accepted");
frame.Rig++;frame.Sequence++;Check(timeline.Push(frame,2.1)&&timeline.Sample(2.1,out a,out b,out _)&&a.Rig==frame.Rig&&b.Rig==frame.Rig,"Avatar change interpolated incompatible rigs");
for(int i=0;i<100;i++){frame.Sequence++;Check(timeline.Push(frame,3+i*.05),"Long jitter stream");}
Check(timeline.Sample(7.95,out _,out _,out _),"Ring buffer overflow");timeline.Clear();Check(!timeline.Sample(8,out _,out _,out _),"Disconnect retained pose");
frame.Kind=PoseKind.Body;frame.Mask=1;int encoded=PoseProtocol.Encode(frame,bytes);
for(int i=0;i<1000;i++){PoseProtocol.Encode(frame,bytes);PoseProtocol.Decode(bytes.AsSpan(0,encoded),decoded);frame.Sequence++;timeline.Push(frame,10+i*.05);timeline.Sample(10+i*.05,out _,out _,out _);}
long before=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++){PoseProtocol.Encode(frame,bytes);PoseProtocol.Decode(bytes.AsSpan(0,encoded),decoded);frame.Sequence++;timeline.Push(frame,100+i*.05);timeline.Sample(100+i*.05,out _,out _,out _);}
long allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(allocated==0,"Recurring codec/buffer allocations: "+allocated);
Console.WriteLine($"PASS {assertions} assertions; body=1152 bytes; items(max)=500 bytes; position error<={maxPosition:R}m; quaternion dot error<={maxRotation:R}; scale error<={maxScale:R}; recurring managed allocations={allocated}");
Console.WriteLine("Scope: protocol, sender authority, interpolation and bounded buffers. Does not prove native NGO or in-game rendering.");
