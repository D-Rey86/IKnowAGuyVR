using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace IKnowAGuyVR.SubsystemProbe;

// Diagnostic-only original game UI actions and bounded simulator captures.
internal static class UiScenarioControl
{
    private static readonly bool enabled = Environment.GetEnvironmentVariable("IKAG_XR_UI_SCENARIO") == "1";
    private static int phase;
    private static float nextAt;
    private static HUDManager hud;
    private static string commandPath;
    internal static void Tick()
    {
        if (!enabled || phase == 9) return;
        try
        {
            if (phase == 0)
            {
                if (!StereoTrial.GameplayActive) return;
                hud = HUDManager.LocalInstance;
                if (hud == null || hud.escapeMenuController == null) return;
                commandPath = Path.Combine(Environment.GetEnvironmentVariable("XRSIM_DIR"), "command.txt");
                phase = 1; nextAt = Time.realtimeSinceStartup + 3;
            }
            if (Time.realtimeSinceStartup < nextAt) return;
            if (phase == 4 || phase == 6) OverlayCursor.Trace();
            switch (phase)
            {
                case 1: Shot("ui-full-hud"); break;
                case 2: hud.ShowEscapeMenu(); break;
                case 3: SeedPointer(); Shot("ui-pause"); break;
                case 4: hud.OpenSettingsPanel(); break;
                case 5: SeedPointer(); Shot("ui-settings"); break;
                case 6: hud.CloseSettingsPanel(); hud.HideEscapeMenu(); File.WriteAllText(commandPath, "head rot 0 15 0\n"); break;
                case 7: Shot("ui-head-pitch"); break;
                case 8: File.WriteAllText(commandPath, "head rot 0 0 0\n"); Plugin.Line("UI_SCENARIO_COMPLETE original pause/settings calls, pointer paint control, head pitch restored"); break;
            }
            Plugin.Line("UI_SCENARIO_PHASE " + phase);
            ++phase; nextAt = Time.realtimeSinceStartup + 1.5f;
        }
        catch (Exception e) { Plugin.Line("UI_SCENARIO_FAILED " + e); phase = 9; }
    }
    private static void Shot(string name) => File.WriteAllText(commandPath, "shot " + name + "\n");
    private static void SeedPointer()
    {
        var panels = UIElementsRuntimeUtility.GetSortedScreenOverlayPlayerPanels();
        for (int i = 0; i < panels.Count; ++i)
        {
            var panel = panels[i];
            if (panel.targetTexture != null || panel.drawsInCameras) continue;
            var bounds = panel.visualTree.layout;
            PointerDeviceState.SavePointerPosition(PointerId.mousePointerId, new Vector3(bounds.width * .5f, bounds.height * .5f, 0), panel.Cast<IPanel>(), ContextType.Player);
            return;
        }
    }
}
