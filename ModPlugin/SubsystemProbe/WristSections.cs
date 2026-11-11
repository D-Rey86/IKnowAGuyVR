using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class WristSections
{
    private readonly struct Header
    {
        internal readonly int Index;internal readonly VisualElement Row;
        internal Header(int index,VisualElement row){Index=index;Row=row;}
    }
    private static readonly List<Header> headers=new(6);
    private static readonly HashSet<IntPtr> registered=new();
    private static HUDManager hud;private static bool dirty;
    internal static void Install(Harmony h)=>h.Patch(typeof(HUDManager).GetMethod("RegisterHudDrawer"),postfix:new HarmonyMethod(typeof(WristSections).GetMethod(nameof(Registered),BindingFlags.Static|BindingFlags.NonPublic)));
    private static void Registered(HUDManager __instance){if(hud!=null&&hud.Pointer==__instance.Pointer)dirty=true;}
    internal static void Bind(HUDManager current){hud=current;dirty=true;headers.Clear();registered.Clear();Rebuild();}
    private static void Rebuild()
    {
        headers.Clear();dirty=false;var drawers=hud?.hudDrawers;if(drawers==null)return;
        for(int i=0;i<drawers.Length;i++){var parts=drawers[i];if(parts==null)continue;for(int j=0;j<parts.Count;j++)
        {
            var row=parts[j].row;if(row==null)continue;headers.Add(new Header(i,row));
            if(registered.Add(row.Pointer))
            {
                int index=i;var owner=hud;
                // Classic UI uses the native dispatcher; wrist surfaces reuse
                // the same row bounds through their existing pointer mapping.
                row.RegisterCallback<ClickEvent>((Action<ClickEvent>)(e=>
                {if(!MotionInput.Enabled||owner==null)return;owner.ToggleHudDrawer(index);e.StopImmediatePropagation();}));
            }
        }}
    }
    internal static bool Click(HUDManager current,bool left,Vector2 point)
    {
        if(!MotionInput.Enabled||current==null)return false;
        if(hud==null||hud.Pointer!=current.Pointer)Bind(current);
        if(dirty)Rebuild();
        foreach(var header in headers)
        {
            if((header.Index<3)!=left||header.Row.panel==null||header.Row.resolvedStyle.display==DisplayStyle.None)continue;
            var bounds=header.Row.worldBound;
            if(bounds.width<=0||bounds.height<=0||!bounds.Contains(point))continue;
            // Native state owns persistence, counters, chevrons and linked rows.
            hud.ToggleHudDrawer(header.Index);
            MotionPointer.ConsumePrimaryUntilRelease();return true;
        }
        return false;
    }
}
