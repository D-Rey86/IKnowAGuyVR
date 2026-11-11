using System;
using System.IO;
using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
internal static class MotionRigAudit
{
    internal static void Run(InventoryManager inventory)
    {
        string path = Path.Combine(BepInEx.Paths.GameRootPath, "MotionRigAudit.log");
        void Log(string value) => File.AppendAllText(path, value + "\n");
        File.WriteAllText(path, "Cold motion rig/action census\n");
        try
        {
            ControllerTransport.Sample();
            var left = ControllerTransport.Left; var right = ControllerTransport.Right;
            Log($"CONTROLLERS left={left.Tracked} p={left.Position:R} q={left.Rotation:R} aim={left.AimPosition:R}/{left.AimRotation:R} stick={left.Stick:R} primary={left.Primary} right={right.Tracked} p={right.Position:R} q={right.Rotation:R} aim={right.AimPosition:R}/{right.AimRotation:R}");
            var input = Core.Input.InputManager.Instance;
            foreach (var map in input.inputActionsAsset.actionMaps)
                foreach (var action in map.actions)
                {
                    Log($"ACTION {map.name}/{action.name} type={action.type} interactions={action.interactions}");
                    foreach (var binding in action.bindings) Log($" BIND {binding.effectivePath} interactions={binding.effectiveInteractions} processors={binding.effectiveProcessors}");
                }
            var interaction = inventory.interactionManager;
            foreach(var surface in UnityEngine.Object.FindObjectsOfType<RenderTextureScreenInput>(true))
            {
                var machine=surface.GetComponentInParent<MachineInteractionBase>();
                Log($"SCREEN name={surface.name} active={surface.isActiveAndEnabled} mesh={(surface.screenMesh==null?"none":surface.screenMesh.name)} rect={surface.screenRect:R} gate={(surface.InputGate==null||surface.InputGate.Invoke())} machine={(machine==null?"none":machine.name)} position={(surface.screenRenderer==null?surface.transform.position:surface.screenRenderer.bounds.center):R} maxDistance={surface.maxRayDistance:R} root={(surface.uiDocument==null?"none":surface.uiDocument.name)}");
            }
            foreach(var renderer in inventory.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Log($"SKIN {renderer.name} enabled={renderer.enabled} shadows={renderer.shadowCastingMode} bones={(renderer.bones==null?0:renderer.bones.Length)} mesh={(renderer.sharedMesh==null?"none":renderer.sharedMesh.name)} layer={renderer.gameObject.layer}");
            Log($"HOLD transform={interaction.holdPosition.name} pos={interaction.holdPosition.position:R} rot={interaction.holdPosition.rotation:R}");
            foreach (var t in inventory.GetComponentsInChildren<Transform>(true))
                Log($"RIG {t.name} parent={(t.parent == null ? "none" : t.parent.name)} pos={t.position:R} rot={t.rotation:R} layer={t.gameObject.layer} active={t.gameObject.activeInHierarchy}");
            foreach (var a in inventory.GetComponentsInChildren<Animator>(true))
            {
                Log($"ANIMATOR {a.name} human={a.isHuman} avatar={(a.avatar == null ? "none" : a.avatar.name)}");
                if (a.isHuman) foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                { var t = a.GetBoneTransform(bone); Log($"BONE {bone} name={(t == null ? "none" : t.name)}"); }
            }
            UiInputAudit.Dump("Assembly-CSharp.dll", "Core.Input", "InputManager", "Update", "get_IsInteractHeld", "get_IsPrimaryHeld", "get_IsSellHeld", "get_IsSprintHeld", "WasInteractPressedRaw", "OnPrimaryPerformed", "OnSecondaryPerformed");
            UiInputAudit.Dump("Assembly-CSharp.dll", "", "InteractionManager", "Update", "FixedUpdate", "LateUpdate", "HandleDrop", "UpdateHoldingPosition", "UpdateHeldItems", "GetHoldingConfiguration");
            UiInputAudit.Dump("Assembly-CSharp.dll", "", "RenderTextureScreenInput", "ScreenToPanel", "TryHitSurface", "GetViewCamera");
            Log("MOTION_AUDIT_COMPLETE");
        }
        catch (Exception e) { Log("MOTION_AUDIT_FAILED " + e); }
    }
}
