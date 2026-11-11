using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace IKnowAGuyVR.Networking;

// Same source is compiled into the VR plugin and the flatscreen companion.
// The host relays only to peers that completed this protocol's handshake.
internal static class NetworkPoseRuntime
{
    private sealed class Peer
    {
        internal byte Role;internal double LastHello,Window;internal int Packets;
        internal bool Accept(double now){if(now-Window>=1){Window=now;Packets=0;}return ++Packets<=90;}
    }
    private sealed class Actor
    {
        internal readonly NetworkObject Net;internal readonly InteractionManager Interaction;
        internal readonly PoseTimeline Body=new(),Items=new();
        internal AvatarPose Rig;internal double Retry;internal bool RigDirty=true;
        internal readonly RemoteHeldPose[] Held=new RemoteHeldPose[PoseProtocol.MaxItems];
        internal readonly double[] HeldRetry=new double[PoseProtocol.MaxItems];
        internal readonly ulong[] Pending=new ulong[PoseProtocol.MaxItems];
        internal Actor(NetworkObject net,InteractionManager interaction){Net=net;Interaction=interaction;}
        internal void Restore(){Rig?.Restore();for(int i=0;i<Held.Length;i++)Held[i]?.Restore();}
        internal void Dispose(){Restore();Rig?.SetStreaming(false);for(int i=0;i<Held.Length;i++){Held[i]?.Disable();Held[i]=null;}Body.Clear();Items.Clear();}
        internal void Update(double now)
        {
            if((RigDirty||Rig==null||Rig.Animator==null)&&now>=Retry)
            {
                Retry=now+.5;Rig?.SetStreaming(false);Rig?.Restore();Rig=null;
                var bridge=Net.GetComponentInChildren<CharacterAnimatorBridge>(true);
                if(bridge!=null&&bridge.animator!=null&&bridge.animator.isHuman){Rig=new AvatarPose(Net,bridge.animator,Interaction);RigDirty=false;}
            }
            bool bodyLive=Body.Sample(now,out _,out var latest,out _);
            Rig?.SetStreaming(bodyLive&&Rig.Valid&&latest.Rig==Rig.Rig&&latest.Mask==Rig.Mask);
            bool live=Items.Sample(now,out _,out var frame,out _);
            // Restore every displaced slot before binding any new slot. A
            // reordered stack may otherwise cache another slot's temporary
            // GPU flag, then have that old slot undo the new binding.
            for(int i=0;i<Held.Length;i++)
            {
                ulong id=live&&i<frame.Count?frame.Items[i]:ulong.MaxValue;
                if(Held[i]!=null&&(Held[i].Id!=id||Held[i].Item==null)){Held[i].Disable();Held[i]=null;}
            }
            for(int i=0;i<Held.Length;i++)
            {
                ulong id=live&&i<frame.Count?frame.Items[i]:ulong.MaxValue;
                if(id==ulong.MaxValue)continue;
                if(Held[i]!=null){if(IsHeld(this,Held[i]))Held[i].Enable();else Held[i].Disable();continue;}
                if(Pending[i]!=id){Pending[i]=id;HeldRetry[i]=0;}if(now<HeldRetry[i])continue;HeldRetry[i]=now+.5;
                if(TryObject(id,out var net))
                {
                    Held[i]=new RemoteHeldPose(net);if(IsHeld(this,Held[i]))Held[i].Enable();
                }
            }
        }
        internal void Render(double now)
        {
            if(Net==null||!Net.IsSpawned||Net.OwnerClientId==manager.LocalClientId)return;
            if(Body.Sample(now,out var a,out var b,out float t)&&b.Sender==Net.OwnerClientId)Rig?.Apply(a,b,t);
            if(!Items.Sample(now,out a,out b,out t))return;
            for(int i=0;i<b.Count;i++)
            {
                var held=Held[i];if(held==null||held.Id!=b.Items[i]||b.Sender!=Net.OwnerClientId||!IsHeld(this,held))continue;
                var pose=b.Bones[i];
                for(int j=0;j<a.Count;j++)if(a.Items[j]==b.Items[i]){pose=BonePose.Blend(a.Bones[j],b.Bones[i],t);break;}
                held.Apply(Net.transform,pose);
            }
        }
    }
    private static readonly Dictionary<ulong,Peer> peers=new();
    private static readonly Dictionary<ulong,Actor> actors=new();
    private static readonly List<ulong> retired=new();
    private static readonly PoseFrame incoming=new(),control=new(),body=new(),items=new();
    private static readonly byte[] sendBytes=new byte[PoseProtocol.MaximumBytes];
    private static NetworkManager manager;private static CustomMessagingManager messaging;
    private static CustomMessagingManager.HandleNamedMessageDelegate receive;
    private static Il2CppSystem.Action<ulong> disconnected;
    private static Il2CppSystem.Action<ScriptableRenderContext,Il2CppSystem.Collections.Generic.List<Camera>> begin,end;
    private static FastBufferWriter writer;private static bool writerCreated,vr,started,failed;
    private static ulong epoch;private static int helloAttempts,renderDepth;private static double helloAt,nextCapture;
    private static uint sequence;private static AvatarPose localRig;
    private static Action<string> log;
    // The source selection cache avoids component/hierarchy searches per send.
    private static readonly GameObject[] sourceItems=new GameObject[PoseProtocol.MaxItems];
    private static readonly NetworkObject[] sourceNetworks=new NetworkObject[PoseProtocol.MaxItems];
    private static readonly PizzaBox[] sourceBoxes=new PizzaBox[PoseProtocol.MaxItems];
    private static readonly GameObject[] sourceContents=new GameObject[PoseProtocol.MaxItems];
    private static readonly NetworkObject[] contentNetworks=new NetworkObject[PoseProtocol.MaxItems];

    internal static void Initialize(bool isVr,Action<string> logger)
    {
        try{InitializeCore(isVr,logger);}catch(Exception e){Fail(e);}
    }
    private static void InitializeCore(bool isVr,Action<string> logger)
    {
        if(started||AppDomain.CurrentDomain.GetData("IKAG.VRPose.Owner")!=null)return;
        AppDomain.CurrentDomain.SetData("IKAG.VRPose.Owner",isVr?"VR":"Companion");started=true;vr=isVr;log=logger;
        ClassInjector.RegisterTypeInIl2Cpp<NetworkPoseDriver>();
        var go=new GameObject("IKAG_NetworkPoses");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<NetworkPoseDriver>();
        receive=(Action<ulong,FastBufferReader>)Receive;disconnected=(Action<ulong>)Disconnected;
        begin=(Action<ScriptableRenderContext,Il2CppSystem.Collections.Generic.List<Camera>>)Begin;
        end=(Action<ScriptableRenderContext,Il2CppSystem.Collections.Generic.List<Camera>>)End;
        RenderPipelineManager.add_beginContextRendering(begin);RenderPipelineManager.add_endContextRendering(end);
        new Harmony("ikag.network-pose-lifecycle").Patch(typeof(CharacterAnimatorBridge).GetMethod("SetAnimator"),postfix:new HarmonyMethod(typeof(NetworkPoseRuntime).GetMethod(nameof(AnimatorChanged),System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)));
        log("NETWORK_POSE_READY protocol=1 role="+(isVr?"VR":"Companion"));
    }
    internal static void Tick()
    {
        if(failed)return;
        try
        {
            var next=NetworkManager.Singleton;
            if(next==null||!next.IsListening||(!next.IsServer&&!next.IsConnectedClient)){if(manager!=null)Detach();return;}
            if(next.CustomMessagingManager==null||next.SpawnManager==null)return;
            if(manager==null||manager.Pointer!=next.Pointer){Detach();Attach(next);}
            // Recover a render scope if the engine aborted before its end callback.
            if(renderDepth!=0){foreach(var actor in actors.Values)actor.Restore();renderDepth=0;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(!manager.IsServer&&epoch==0&&helloAttempts<5&&now>=helloAt){helloAttempts++;helloAt=now+2;Control(PoseKind.Hello,NetworkManager.ServerClientId);}
            retired.Clear();
            foreach(var pair in actors)
            {
                if(pair.Value.Net==null||!pair.Value.Net.IsSpawned){pair.Value.Dispose();retired.Add(pair.Key);}
                else pair.Value.Update(now);
            }
            for(int i=0;i<retired.Count;i++)actors.Remove(retired[i]);
        }
        catch(Exception e){Fail(e);}
    }
    private static void Attach(NetworkManager next)
    {
        manager=next;messaging=next.CustomMessagingManager;
        writer=new FastBufferWriter(PoseProtocol.MaximumBytes,Allocator.Persistent,PoseProtocol.MaximumBytes);writerCreated=true;
        messaging.RegisterNamedMessageHandler(PoseProtocol.Channel,receive);manager.add_OnClientDisconnectCallback(disconnected);
        epoch=manager.IsServer?BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0):0;if(manager.IsServer&&epoch==0)epoch=1;
        helloAttempts=0;helloAt=nextCapture=0;sequence=0;
    }
    private static void Detach()
    {
        foreach(var actor in actors.Values)actor.Dispose();actors.Clear();peers.Clear();localRig=null;renderDepth=0;
        if(messaging!=null){try{messaging.UnregisterNamedMessageHandler(PoseProtocol.Channel);}catch{}}
        if(manager!=null){try{manager.remove_OnClientDisconnectCallback(disconnected);}catch{}}
        if(writerCreated){writer.Dispose();writerCreated=false;}
        manager=null;messaging=null;epoch=0;
        Array.Clear(sourceItems,0,sourceItems.Length);Array.Clear(sourceNetworks,0,sourceNetworks.Length);Array.Clear(sourceBoxes,0,sourceBoxes.Length);Array.Clear(sourceContents,0,sourceContents.Length);Array.Clear(contentNetworks,0,contentNetworks.Length);
    }
    internal static void Shutdown(){try{Detach();}finally{failed=true;}}
    private static void Fail(Exception e){if(failed)return;failed=true;try{Detach();}finally{log?.Invoke("NETWORK_POSE_DISABLED "+e.GetType().Name+": "+e.Message);}}
    private static void Control(PoseKind kind,ulong destination)
    {
        control.Kind=kind;control.Role=vr?(byte)1:(byte)0;control.Epoch=epoch;control.Sender=manager.LocalClientId;control.Object=control.Rig=control.Mask=0;control.Count=0;control.Sequence=0;
        int n=PoseProtocol.Encode(control,sendBytes);Send(destination,sendBytes.AsSpan(0,n),true);
    }
    private static unsafe void Send(ulong destination,ReadOnlySpan<byte> data,bool reliable=false)
    {
        if(data.IsEmpty)return;writer.Seek(0);writer.Truncate();
        fixed(byte* p=data){writer.WriteBytesSafe(p,data.Length);}
        messaging.SendNamedMessage(PoseProtocol.Channel,destination,writer,reliable?NetworkDelivery.ReliableSequenced:NetworkDelivery.UnreliableSequenced);
    }
    private static void Broadcast(ReadOnlySpan<byte> bytes,ulong except)
    {foreach(var peer in peers)if(peer.Key!=except&&peer.Key!=manager.LocalClientId)Send(peer.Key,bytes);}
    private static unsafe void Receive(ulong sender,FastBufferReader reader)
    {
        if(failed||manager==null)return;
        try
        {
            int size=reader.Length-reader.Position;if(size<PoseProtocol.Header||size>PoseProtocol.MaximumBytes)return;
            var bytes=new ReadOnlySpan<byte>(reader.GetUnsafePtrAtCurrentPosition(),size);if(!PoseProtocol.Decode(bytes,incoming))return;
            double now=Time.realtimeSinceStartupAsDouble;
            if(incoming.Kind==PoseKind.Hello)
            {
                if(!manager.IsServer||sender==manager.LocalClientId||incoming.Sender!=sender)return;
                if(!peers.TryGetValue(sender,out var peer)){peer=new Peer();peers.Add(sender,peer);}else if(now-peer.LastHello<.5)return;
                peer.Role=incoming.Role;peer.LastHello=now;Control(PoseKind.Welcome,sender);return;
            }
            if(incoming.Kind==PoseKind.Welcome)
            {if(!manager.IsServer&&sender==NetworkManager.ServerClientId&&incoming.Sender==sender&&incoming.Epoch!=0)epoch=incoming.Epoch;return;}
            if(epoch==0||incoming.Epoch!=epoch)return;
            bool known=peers.TryGetValue(sender,out var source)&&source.Role==1;
            if(manager.IsServer&&(!known||!source.Accept(now)))return;
            if(!TryObject(incoming.Object,out var net)||!PoseProtocol.Authorized(epoch,manager.IsServer,sender,incoming.Sender,net.OwnerClientId,manager.LocalClientId,NetworkManager.ServerClientId,known))return;
            if(actors.TryGetValue(incoming.Object,out var stale)&&stale.Net!=net){stale.Dispose();actors.Remove(incoming.Object);}
            if(!actors.TryGetValue(incoming.Object,out var actor))
            {
                var interaction=net.GetComponent<InteractionManager>();if(interaction==null)return;
                actor=new Actor(net,interaction);actors.Add(incoming.Object,actor);
            }
            bool accepted=(incoming.Kind==PoseKind.Body?actor.Body:actor.Items).Push(incoming,now);
            if(accepted&&manager.IsServer)Broadcast(bytes,sender);
        }
        catch(Exception e){Fail(e);}
    }
    private static bool TryObject(ulong id,out NetworkObject obj)
    {obj=null;return manager!=null&&manager.SpawnManager!=null&&manager.SpawnManager.SpawnedObjects.TryGetValue(id,out obj)&&obj!=null&&obj.IsSpawned;}
    private static void Disconnected(ulong client)
    {
        peers.Remove(client);retired.Clear();
        foreach(var pair in actors)if(pair.Value.Net==null||pair.Value.Net.OwnerClientId==client){pair.Value.Dispose();retired.Add(pair.Key);}
        for(int i=0;i<retired.Count;i++)actors.Remove(retired[i]);
        if(manager!=null&&client==manager.LocalClientId)Detach();
    }
    private static void AnimatorChanged(CharacterAnimatorBridge __instance)
    {
        if(__instance==null)return;
        if(localRig!=null&&localRig.Player!=null&&localRig.Player.OwnerClientId==__instance.OwnerClientId)localRig=null;
        foreach(var actor in actors.Values)if(actor.Net!=null&&actor.Net.OwnerClientId==__instance.OwnerClientId){actor.RigDirty=true;actor.Retry=0;}
    }
    private static void Begin(ScriptableRenderContext context,Il2CppSystem.Collections.Generic.List<Camera> cameras)
    {
        if(failed||manager==null)return;
        if(renderDepth++!=0)return;
        try{double now=Time.realtimeSinceStartupAsDouble;foreach(var actor in actors.Values)actor.Render(now);}
        catch(Exception e){Fail(e);}
    }
    private static void End(ScriptableRenderContext context,Il2CppSystem.Collections.Generic.List<Camera> cameras)
    {
        if(renderDepth==0)return;if(--renderDepth!=0)return;
        try{foreach(var actor in actors.Values)actor.Restore();}catch(Exception e){Fail(e);}
    }
    internal static bool CanCapture=>started&&!failed&&vr&&manager!=null&&epoch!=0&&Time.realtimeSinceStartupAsDouble>=nextCapture&&(!manager.IsServer||peers.Count>0);
    // Called only after the existing local solver and held-item render pose.
    internal static void Capture(InteractionManager owner,Animator animator,Transform hiddenHead,Vector3 headScale)
    {
        if(!CanCapture||owner==null||animator==null)return;
        nextCapture=Time.realtimeSinceStartupAsDouble+PoseProtocol.Interval;
        try
        {
            var net=owner.NetworkObject;if(net==null||!net.IsSpawned||net.OwnerClientId!=manager.LocalClientId)return;
            if(localRig==null||localRig.Player!=net||localRig.Animator!=animator)localRig=new AvatarPose(net,animator,owner);
            body.Kind=PoseKind.Body;body.Role=1;body.Epoch=epoch;body.Sender=manager.LocalClientId;body.Sequence=++sequence;
            if(!localRig.Capture(body,hiddenHead,headScale))return;
            Dispatch(body);
            items.Kind=PoseKind.Items;items.Role=1;items.Epoch=epoch;items.Sender=manager.LocalClientId;items.Sequence=sequence;items.Object=body.Object;items.Rig=0;items.Mask=0;items.Count=0;
            int count=owner.heldItems.Count;if(count>sourceItems.Length)return;
            for(int i=0;i<count;i++)
            {
                var item=owner.heldItems[i];if(item==null)continue;
                if(sourceItems[i]!=item){sourceItems[i]=item;sourceNetworks[i]=item.GetComponent<NetworkObject>();sourceBoxes[i]=item.GetComponent<PizzaBox>();sourceContents[i]=null;contentNetworks[i]=null;}
                if(!CaptureItem(sourceNetworks[i],net.transform))return;
                var box=sourceBoxes[i];var content=box==null?null:box.wholePizzaInBox?.pizzaObject;
                if(sourceContents[i]!=content){sourceContents[i]=content;contentNetworks[i]=content==null?null:content.GetComponent<NetworkObject>();}
                if(content!=null&&!CaptureItem(contentNetworks[i],net.transform))return;
            }
            Dispatch(items);
        }
        catch(Exception e){Fail(e);}
    }
    private static bool CaptureItem(NetworkObject net,Transform root)
    {
        if(net==null||!net.IsSpawned)return true;
        for(int i=0;i<items.Count;i++)if(items.Items[i]==net.NetworkObjectId)return true;
        if(items.Count>=PoseProtocol.MaxItems)return false;
        var t=net.transform;var pose=new BonePose{Position=AvatarPose.ToN(root.InverseTransformPoint(t.position)),Rotation=AvatarPose.ToN(Quaternion.Inverse(root.rotation)*t.rotation),Scale=System.Numerics.Vector3.One};
        if(!PoseProtocol.Valid(pose))return false;
        items.Items[items.Count]=net.NetworkObjectId;items.Bones[items.Count++]=pose;return true;
    }
    private static void Dispatch(PoseFrame frame)
    {
        int length=PoseProtocol.Encode(frame,sendBytes);if(length==0)return;
        if(manager.IsServer)Broadcast(sendBytes.AsSpan(0,length),manager.LocalClientId);else Send(NetworkManager.ServerClientId,sendBytes.AsSpan(0,length));
    }
    private static bool IsHeld(Actor actor,RemoteHeldPose item)
    {
        if(item==null||item.Network==null||!item.Network.IsSpawned||actor.Net==null)return false;
        // Remote InteractionManager.heldItems is not a network list. Check the
        // game's replicated ownership/held/stored state on the item itself.
        if(item.Network.OwnerClientId==actor.Net.OwnerClientId&&item.Holdable!=null&&item.Holdable.IsBeingHeld()&&!item.Holdable.IsStored())return true;
        if(item.Pizza!=null&&item.Pizza.continuousFollow)
            for(int i=0;i<actor.Held.Length;i++)
            {
                var box=actor.Held[i];
                if(box!=null&&box.Box!=null&&box.Network!=null&&box.Network.OwnerClientId==actor.Net.OwnerClientId&&box.Holdable!=null&&box.Holdable.IsBeingHeld()&&!box.Holdable.IsStored()&&
                   box.Box.wholePizzaInBox?.pizzaObject==item.Item&&item.Pizza.followTarget==box.Box.pizzaPlacementPoint)return true;
            }
        return false;
    }
}

public sealed class NetworkPoseDriver : MonoBehaviour
{
    public NetworkPoseDriver(IntPtr pointer):base(pointer){}
    private void Update()=>NetworkPoseRuntime.Tick();
    private void OnApplicationQuit()=>NetworkPoseRuntime.Shutdown();
}
