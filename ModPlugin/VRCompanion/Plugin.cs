using BepInEx;
using BepInEx.Unity.IL2CPP;
using IKnowAGuyVR.Networking;

namespace IKnowAGuyVR.Companion;

[BepInPlugin("iknowaguyvr.companion","I Know a Guy VR Companion",Plugin.Version)]
[BepInDependency("iknowaguyvr.subsystemprobe",BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal const string Version="1.0.1";
    public override void Load()=>NetworkPoseRuntime.Initialize(false,message=>Log.LogInfo(message));
}
