using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace IKnowAGuyVR.SubsystemProbe;

// Visual-only copies. Stored network objects never become active or change size.
internal static class BeltModels
{
    private static readonly Vector3 LoopSize=new(.075f,.018f,.065f);
    private const float BoxFitMargin=.002f;
    private sealed class Slot
    {
        internal GameObject Source, Root;
        internal Transform RootTransform, DockTransform;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal bool VisualDirty;
        internal readonly List<Mesh> Meshes = new();
        internal MeshRenderer Dock;
        internal void Clear()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            foreach (var mesh in Meshes) UnityEngine.Object.Destroy(mesh);
            Meshes.Clear(); Root = Source = null; RootTransform = null; VisualDirty=false;
        }
    }
    private static GameObject root;
    private static Transform rootTransform, band, buckleTransform;
    private static float layoutReach = float.NaN;
    private static Slot[] slots = Array.Empty<Slot>();
    private static Material leather, brass, highlight;
    private static Mesh bandMesh, cube;
    private static int selected = -1;
    private static int leftTarget = -1, rightTarget = -1;
    private static InventoryManager inventory;

    internal static void Install()=>new Harmony("iknowaguyvr.belt-box-icons").Patch(
        typeof(CargoBox).GetMethod("UpdateCanvasIcons"),postfix:new HarmonyMethod(
            typeof(BeltModels).GetMethod(nameof(BoxIconsChanged),BindingFlags.Static|BindingFlags.NonPublic)));
    private static void BoxIconsChanged(CargoBox __instance)
    {
        var source=__instance.gameObject;
        foreach(var slot in slots)if(slot.Source==source)slot.VisualDirty=true;
    }

    private static MeshRenderer Renderer(GameObject go, Mesh mesh, Material[] materials)
    {
        go.layer = 10;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.allowGPUDrivenRendering = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }
    private static Mesh BandMesh()
    {
        const int segments = 64;
        var vertices = new Vector3[segments * 4];
        var triangles = new int[segments * 24];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            for (int k = 0; k < 4; k++)
            {
                float radius = k < 2 ? 1 : .96f;
                vertices[i * 4 + k] = new Vector3(Mathf.Sin(angle) * radius, (k % 2 == 0 ? -.5f : .5f), Mathf.Cos(angle) * radius);
            }
            int a = i * 4, b = (i + 1) % segments * 4, t = i * 24;
            int[] faces = {a,b,a+1, a+1,b,b+1, a+2,a+3,b+2, a+3,b+3,b+2,
                           a+1,b+1,a+3, a+3,b+1,b+3, a,a+2,b, a+2,b+2,b};
            Array.Copy(faces,0,triangles,t,24);
        }
        var mesh = new Mesh { name = "VR belt strap", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    private static void Create(int count)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("Belt material shader unavailable");
        leather = new Material(shader); leather.SetColor("_BaseColor", new Color(.075f,.035f,.018f));
        brass = new Material(shader); brass.SetColor("_BaseColor", new Color(.68f,.38f,.075f));
        highlight = new Material(shader); highlight.SetColor("_BaseColor", new Color(.18f,.85f,.90f));
        root = new GameObject("IKAG_MiniatureInventoryBelt"); root.SetActive(false);
        rootTransform = root.transform;
        UnityEngine.Object.DontDestroyOnLoad(root);
        bandMesh = BandMesh();
        var strap = new GameObject("Leather strap"); strap.transform.SetParent(root.transform,false);
        band = strap.transform; Renderer(strap,bandMesh,new[]{leather});
        // Unity's primitive mesh is shared. Destroy the temporary collider and
        // object before any physics update, keeping only the immutable mesh.
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.SetActive(false); cube = template.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.Destroy(template);
        slots = new Slot[count];
        for (int i=0;i<count;i++)
        {
            var dock = new GameObject("Belt loop " + (i+1)); dock.transform.SetParent(root.transform,false);
            dock.transform.localScale = LoopSize;
            slots[i] = new Slot { Dock = Renderer(dock,cube,new[]{leather}), DockTransform = dock.transform };
        }
        var buckle = new GameObject("Belt buckle"); buckle.transform.SetParent(root.transform,false);
        Renderer(buckle,cube,new[]{brass});
        buckleTransform = buckle.transform;
        buckle.transform.localScale = new Vector3(.045f,.045f,.018f);
    }
    private static bool ChildVisible(Transform child, Transform item)
    {
        for (var t=child;t!=null&&t!=item;t=t.parent) if(!t.gameObject.activeSelf) return false;
        return true;
    }
    private static Transform CopyTransform(Transform source, Transform parent, Dictionary<IntPtr,Transform> copies)
    {
        if(copies.TryGetValue(source.Pointer,out var existing))return existing;
        var copiedParent=source.parent==null?parent:CopyTransform(source.parent,parent,copies);
        var node=new GameObject(source.name).transform;
        node.SetParent(copiedParent,false);
        node.localPosition=source.localPosition;
        node.localRotation=source.localRotation;
        node.localScale=source.localScale;
        copies.Add(source.Pointer,node);
        return node;
    }
    private static void CopyParts(GameObject source, Transform frame, Matrix4x4 fromWorld,
        Dictionary<IntPtr,Transform> copies, Slot slot, ref Bounds bounds, ref bool hasBounds)
    {
        bool cargo=source.GetComponent<CargoBox>()!=null;
        foreach(var sourceRenderer in source.GetComponentsInChildren<Renderer>(true))
        {
            // Native inventory disables every stored renderer. Child activation
            // still distinguishes actual content from unused pooled visuals.
            if(!ChildVisible(sourceRenderer.transform,source.transform))continue;
            Bounds local;
            var sprite=sourceRenderer.TryCast<SpriteRenderer>();
            if(sprite!=null)
            {
                if(!cargo||sprite.sprite==null)continue;
                var node=CopyTransform(sprite.transform,frame,copies);node.gameObject.layer=10;
                var copy=node.gameObject.AddComponent<SpriteRenderer>();
                copy.sprite=sprite.sprite;copy.color=sprite.color;copy.flipX=sprite.flipX;copy.flipY=sprite.flipY;
                copy.drawMode=sprite.drawMode;copy.size=sprite.size;copy.tileMode=sprite.tileMode;
                copy.adaptiveModeThreshold=sprite.adaptiveModeThreshold;copy.spriteSortPoint=sprite.spriteSortPoint;
                copy.maskInteraction=sprite.maskInteraction;copy.sortingLayerID=sprite.sortingLayerID;copy.sortingOrder=sprite.sortingOrder;
                copy.sharedMaterials=sprite.sharedMaterials;copy.allowGPUDrivenRendering=false;
                copy.shadowCastingMode=ShadowCastingMode.Off;copy.receiveShadows=false;
                var properties=new MaterialPropertyBlock();sprite.GetPropertyBlock(properties);copy.SetPropertyBlock(properties);
                local=sprite.localBounds;
            }
            else
            {
                Mesh mesh;
                var skin=sourceRenderer.TryCast<SkinnedMeshRenderer>();
                if(skin!=null)
                {
                    mesh=new Mesh();
                    // The measured cargo rig needs its scaled bake to share the
                    // authored coordinate space of the child label sprites.
                    if(cargo)skin.BakeMesh(mesh,true);else skin.BakeMesh(mesh);
                    slot.Meshes.Add(mesh);
                }
                else
                {
                    var filter=sourceRenderer.GetComponent<MeshFilter>();
                    if(filter==null||filter.sharedMesh==null)continue;
                    mesh=filter.sharedMesh;
                }
                var transform=CopyTransform(sourceRenderer.transform,frame,copies);
                var copy=Renderer(transform.gameObject,mesh,sourceRenderer.sharedMaterials);
                var properties=new MaterialPropertyBlock();sourceRenderer.GetPropertyBlock(properties);copy.SetPropertyBlock(properties);
                local=mesh.bounds;
            }
            var matrix=fromWorld*sourceRenderer.transform.localToWorldMatrix;
            for(int corner=0;corner<8;corner++)
            {
                var point=local.center+Vector3.Scale(local.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                point=matrix.MultiplyPoint3x4(point);
                if(!hasBounds){bounds=new Bounds(point,Vector3.zero);hasBounds=true;}else bounds.Encapsulate(point);
            }
        }
    }
    private static void Bind(Slot slot, GameObject source)
    {
        slot.Clear();slot.Source=source;if(source==null)return;
        slot.Root=new GameObject("Miniature " + source.name);
        slot.RootTransform=slot.Root.transform;slot.RootTransform.SetParent(rootTransform,false);
        var fit=new GameObject("Bounds fit").transform;fit.SetParent(slot.RootTransform,false);
        var frame=new GameObject("Source frame").transform;frame.SetParent(fit,false);
        // A rigid inverse frame plus copied ancestor TRS preserves shear and
        // non-uniform scaling without reading or modifying the original mesh.
        var inverseRotation=Quaternion.Inverse(source.transform.rotation);
        frame.SetLocalPositionAndRotation(inverseRotation*(-source.transform.position),inverseRotation);
        var fromWorld=Matrix4x4.TRS(source.transform.position,source.transform.rotation,Vector3.one).inverse;
        var copies=new Dictionary<IntPtr,Transform>();
        Bounds bounds=default;bool hasBounds=false;
        CopyParts(source,frame,fromWorld,copies,slot,ref bounds,ref hasBounds);
        var box=source.GetComponent<PizzaBox>();var content=box==null?null:box.wholePizzaInBox?.pizzaObject;
        if(content!=null&&!content.transform.IsChildOf(source.transform))CopyParts(content,frame,fromWorld,copies,slot,ref bounds,ref hasBounds);
        if(!hasBounds)return;
        float largest=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        float scale=largest>.00001f?Mathf.Min(1,.07f/largest):1;
        if(source.GetComponent<CargoBox>()!=null&&bounds.size.x>.00001f&&bounds.size.y>.00001f&&bounds.size.z>.00001f)
        {
            // Fit the measured box and its native labels in the loop footprint.
            scale=Mathf.Min((LoopSize.x-2*BoxFitMargin)/bounds.size.x,
                Mathf.Min(.07f/bounds.size.y,(LoopSize.z-2*BoxFitMargin)/bounds.size.z));
        }
        fit.localPosition=-bounds.center;
        slot.RootTransform.localScale=Vector3.one*scale;
    }
    internal static void Update(InventoryManager current, Vector3 waist, Quaternion heading, float reach)
    {
        if(inventory!=current||slots.Length!=current.totalSlots){Clear();inventory=current;Create(current.totalSlots);}
        rootTransform.SetPositionAndRotation(waist,heading);
        bool layoutChanged=layoutReach!=reach;
        if(layoutChanged)
        {
            layoutReach=reach;
            band.localScale=new Vector3(.32f,.035f,reach);
            buckleTransform.localPosition = new Vector3(0,0,reach+.008f);
        }
        bool changed=selected!=current.currentSelectedSlot;
        int previous=selected;selected=current.currentSelectedSlot;
        for(int i=0;i<slots.Length;i++)
        {
            var slot=slots[i];var source=current.GetItemInSlot(i);
            // The selected item is held, so its miniature is hidden. Preserve
            // invalidation and rebuild it on stow, before it becomes visible.
            bool rebound=i!=selected&&(slot.VisualDirty||slot.Source!=source||(changed&&i==previous));
            if(rebound)Bind(slot,source);
            if(layoutChanged)
            {
                float degrees=PhysicalBelt.SlotAngle(i), angle=degrees*Mathf.Deg2Rad;
                slot.Position=new Vector3(Mathf.Sin(angle)*.32f,.045f,Mathf.Cos(angle)*reach);
                slot.Rotation=Quaternion.Euler(0,degrees,0);
                slot.DockTransform.SetLocalPositionAndRotation(slot.Position-Vector3.up*.045f,slot.Rotation);
            }
            if(changed)UpdateDock(i);
            if(slot.Root!=null)
            {
                if(layoutChanged||rebound)slot.RootTransform.SetLocalPositionAndRotation(slot.Position,slot.Rotation);
                if(changed||rebound)slot.Root.SetActive(i!=selected);
            }
        }
    }

    internal static int FindSlot(Vector3 palm, float reach)
    {
        if (rootTransform == null) return -1;
        Vector3 local = rootTransform.InverseTransformPoint(palm);
        int nearest = -1;
        float limit = reach * reach;
        for (int i = 0; i < slots.Length; i++)
        {
            float distance = (local - slots[i].Position).sqrMagnitude;
            if (distance >= limit) continue;
            nearest = i;
            limit = distance;
        }
        return nearest;
    }

    internal static void Highlight(int left, int right)
    {
        if (left == leftTarget && right == rightTarget) return;
        int oldLeft = leftTarget, oldRight = rightTarget;
        leftTarget = left;
        rightTarget = right;
        UpdateDock(oldLeft);
        UpdateDock(oldRight);
        UpdateDock(left);
        UpdateDock(right);
    }

    private static void UpdateDock(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        slots[index].Dock.sharedMaterial = index == leftTarget || index == rightTarget
            ? highlight : index == selected ? brass : leather;
    }
    internal static void BeginRender()
    {
        if(root==null)return;
        bool show=PhysicalBelt.RenderVisible;
        root.SetActive(show);
    }
    internal static void EndRender(){if(root!=null)root.SetActive(false);}
    internal static void Clear()
    {
        EndRender();foreach(var slot in slots)slot.Clear();slots=Array.Empty<Slot>();
        if(root!=null)UnityEngine.Object.Destroy(root);
        if(bandMesh!=null)UnityEngine.Object.Destroy(bandMesh);
        if(leather!=null)UnityEngine.Object.Destroy(leather);
        if(brass!=null)UnityEngine.Object.Destroy(brass);
        if(highlight!=null)UnityEngine.Object.Destroy(highlight);
        root=null;rootTransform=null;inventory=null;selected=-1;layoutReach=float.NaN;
        leftTarget=rightTarget=-1;
    }
}
