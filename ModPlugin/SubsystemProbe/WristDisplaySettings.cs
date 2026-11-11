using System;
using BepInEx.Configuration;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class WristDisplaySettings
{
    internal const int Minimum=50,Maximum=400;
    internal const float ScalePerPercent=.005f;
    internal static ConfigEntry<int> Bind(ConfigFile config,out bool migrated)
    {
        // Read and retire the old key once. Zero is only an absence sentinel;
        // valid old settings were 50..200. Fresh installs still default to 100.
        var legacy=config.Bind("Interface","WristDisplayScale",0,"Legacy wrist scale; migrated automatically.");
        var result=config.Bind("Interface","WristDisplaySizePercent",100,new ConfigDescription(
            "Wrist display size percentage. 100% uses the compact default (30 cm wide at world scale 100%). Scales all content and interaction targets together.",
            new AcceptableValueRange<int>(Minimum,Maximum)));
        migrated=legacy.Value!=0;
        if(migrated)result.Value=Math.Clamp(legacy.Value,50,200)*2;
        config.Remove(legacy.Definition);
        return result;
    }
}
