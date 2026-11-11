using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace IKnowAGuyVR.SubsystemProbe;

// The OS cursor is absent from XR textures. Reuse the existing UI panel and
// its authoritative pointer state; this adds no camera, canvas or render target.
internal static class OverlayCursor
{
    private sealed class Entry
    {
        internal BaseRuntimePanel Panel;
        internal Image Image;
        internal VisualElement Root;
        internal IStyle Style;
        internal bool Visible;
        internal Vector2 Position = new(float.NaN, float.NaN);
    }
    private static bool installed, enabled;
    private static readonly List<BaseRuntimePanel> pending = new();
    private static readonly List<Entry> entries = new();
    private static Texture2D texture;
    private static Texture2D transparentCursor;
    internal static void Install()
    {
        enabled = true;
        if (installed) { Cursor.SetCursor(transparentCursor, Vector2.zero, CursorMode.Auto); return; }
        Stage("texture");
        texture = MakeTexture();
        transparentCursor = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "IKnowAGuyVR_NativeCursorMask", hideFlags = HideFlags.DontUnloadUnusedAsset };
        transparentCursor.SetPixels32(new Il2CppStructArray<Color32>(32 * 32));
        transparentCursor.Apply(false, false);
        // LowLevel UI includes the native software cursor in unprojected window
        // coordinates. Keep its visibility/input semantics but replace its image;
        // the existing panel now supplies the correctly projected visible cursor.
        Cursor.SetCursor(transparentCursor, Vector2.zero, CursorMode.Auto);
        Stage("create-callback");
        UIElementsRuntimeUtility.add_onCreatePanel((Action<BaseRuntimePanel>)(panel => pending.Add(panel)));
        Stage("cached-panels");
        var panels = UIElementsRuntimeUtility.GetSortedScreenOverlayPlayerPanels();
        for (int i = 0; i < panels.Count; ++i) pending.Add(panels[i]);
        installed = true;
        Stage("ready");
    }
    internal static void Suspend()
    {
        enabled = false;
        if (installed) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        for (int i = 0; i < entries.Count; ++i)
            if (entries[i].Visible) { entries[i].Image.visible = false; entries[i].Visible = false; }
    }
    internal static void Update()
    {
        if (!installed) return;
        // Creation is deferred until PanelSettings has applied its render mode.
        for (int i = 0; i < pending.Count; ++i)
        {
            var panel = pending[i];
            // Texture-backed menus already use an authored world screen. Draw
            // their cursor into that same existing texture; do not reproject it.
            if (panel.disposed || panel.drawsInCameras) continue;
            bool exists = false;
            for (int j = 0; j < entries.Count; ++j) if (entries[j].Panel.Pointer == panel.Pointer) { exists = true; break; }
            if (exists) continue;
            var image = new Image { name = "IKnowAGuyVR_Cursor", image = texture, pickingMode = PickingMode.Ignore, visible = false };
            image.style.position = Position.Absolute; image.style.width = 24; image.style.height = 32;
            panel.visualTree.Add(image);
            entries.Add(new Entry { Panel = panel, Image = image, Root = panel.visualTree, Style = image.style });
        }
        pending.Clear();
        // The retail destroy-event subscription helper is stripped. Validate
        // the cached panel's own disposed flag instead; never rescan the scene.
        for (int i = entries.Count - 1; i >= 0; --i)
            if (entries[i].Panel.disposed) entries.RemoveAt(i);
        bool show = enabled && Cursor.visible && Cursor.lockState != CursorLockMode.Locked &&
            (!MotionInput.Enabled || MotionPointer.CursorVisible(MotionPointer.PointerLeft));
        var pointerPanel = show ? PointerDeviceState.GetPanel(PointerId.mousePointerId, ContextType.Player) : null;
        Vector3 pointer = show ? PointerDeviceState.GetPointerPosition(PointerId.mousePointerId, ContextType.Player) : default;
        for (int i = 0; i < entries.Count; ++i)
        {
            var entry = entries[i];
            bool visible = pointerPanel != null && pointerPanel.Pointer == entry.Panel.Pointer;
            if (visible != entry.Visible) { entry.Visible = visible; entry.Image.visible = visible; }
            if (!visible) continue;
            var position = new Vector2(pointer.x - 2, pointer.y - 1);
            if (position != entry.Position)
            {
                entry.Position = position;
                entry.Style.left = position.x; entry.Style.top = position.y;
            }
            var root = entry.Root;
            if (root.childCount > 0 && root.ElementAt(root.childCount - 1).Pointer != entry.Image.Pointer) entry.Image.BringToFront();
        }
    }
#if IKAG_DIAGNOSTICS
    internal static void Trace()
    {
        var panel = PointerDeviceState.GetPanel(PointerId.mousePointerId, ContextType.Player);
        Plugin.Line($"CURSOR_STATE enabled={enabled} visible={Cursor.visible} lock={Cursor.lockState} pointerPanel={(panel == null ? 0 : panel.Pointer.ToInt64())} pos={PointerDeviceState.GetPointerPosition(PointerId.mousePointerId, ContextType.Player)} entries={entries.Count}");
        foreach (var entry in entries) Plugin.Line($"CURSOR_ENTRY panel={entry.Panel.Pointer.ToInt64()} owner={entry.Panel.ownerObject.name} visible={entry.Visible} resolved={entry.Image.visible} bound={entry.Image.worldBound} styleDisplay={entry.Image.resolvedStyle.display}");
    }
#endif
    private static Texture2D MakeTexture()
    {
        Stage("texture-ctor");
        var result = new Texture2D(24, 32, TextureFormat.RGBA32, false) { name = "IKnowAGuyVR_Cursor", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        Stage("pixel-array");
        var pixels = new Il2CppStructArray<Color32>(24 * 32);
        Stage("pixel-fill");
        var shape = new[] { new Vector2(2, 1), new Vector2(2, 25), new Vector2(8, 19), new Vector2(13, 30), new Vector2(18, 28), new Vector2(13, 17), new Vector2(22, 17) };
        for (int y = 0; y < 32; ++y)
            for (int x = 0; x < 24; ++x)
                if (Inside(shape, x + .5f, y + .5f))
                {
                    bool edge = !Inside(shape, x - 1, y + .5f) || !Inside(shape, x + 2, y + .5f) || !Inside(shape, x + .5f, y - 1) || !Inside(shape, x + .5f, y + 2);
                    pixels[(31-y)*24+x] = edge ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
        Stage("set-pixels");
        result.SetPixels32(pixels);
        Stage("texture-apply");
        result.Apply(false, true);
        return result;
    }
    private static void Stage(string stage)
    {
#if IKAG_DIAGNOSTICS
        Plugin.Line("CURSOR_INSTALL " + stage);
#endif
    }
    private static bool Inside(Vector2[] polygon, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].y > y) != (polygon[j].y > y) && x < (polygon[j].x-polygon[i].x)*(y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x) inside = !inside;
        return inside;
    }
}
