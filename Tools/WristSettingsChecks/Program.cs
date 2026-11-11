using System;
using System.IO;
using BepInEx.Configuration;
using IKnowAGuyVR.SubsystemProbe;

var root=Path.Combine(Path.GetTempPath(),"IKAG-WristSettings-"+Guid.NewGuid());
Directory.CreateDirectory(root);
int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
ConfigFile Open(string path)=>new ConfigFile(path,false){SaveOnConfigSet=false};
foreach(int? old in new int?[]{null,50,75,100,150,200})
{
    string path=Path.Combine(root,(old?.ToString()??"fresh")+".cfg");
    File.WriteAllText(path,"[Interface]\n"+(old.HasValue?$"WristDisplayScale = {old}\n":"")+"\n[Gameplay]\nGamepadMode = true\n");
    var config=Open(path);var entry=WristDisplaySettings.Bind(config,out bool migrated);
    Check(migrated==old.HasValue,"migration presence");
    Check(entry.Value==(old.HasValue?old.Value*2:100),"initial percentage");
    Check((int)entry.DefaultValue==100,"default must always be 100");
    if(old.HasValue)Check(Math.Abs(old.Value*.01f-entry.Value*WristDisplaySettings.ScalePerPercent)<.000001f,"physical size changed");
    config.Save();
    Check(!File.ReadAllText(path).Contains("WristDisplayScale ="),"old key not retired");
    Check(Open(path).Bind("Gameplay","GamepadMode",false).Value,"unrelated setting lost");
    var reload=Open(path);var again=WristDisplaySettings.Bind(reload,out bool repeated);
    Check(!repeated&&again.Value==entry.Value,"repeat conversion");
    again.Value=63;reload.Save();
    Check(WristDisplaySettings.Bind(Open(path),out _).Value==63,"new custom value lost");
    again.Value=1;Check(again.Value==50,"lower range");
    again.Value=999;Check(again.Value==400,"upper range");
}
Console.WriteLine($"PASS {checks} actual BepInEx config checks; artifacts: {root}");
