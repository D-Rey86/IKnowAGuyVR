using System;
using BepInEx.Configuration;
using UnityEngine;

namespace IKnowAGuyVR.SubsystemProbe;

internal static class PhysicalBelt
{
    private const float Width = .64f;
    private const float DefaultHeight = .46f;
    private const float DefaultReach = .34f;
    private const float MinimumReach = .10f;
    private const float MaximumReach = .52f;
    private const float FreeLookYaw = 25f;
    private const float MinimumViewDepth = .07f;
    private const float ContactRadius = .055f;
    private static ConfigEntry<bool> enabled;
    private static ConfigEntry<float> height, reach;
    private static readonly ImmersiveHud.Surface surface = new();
    private static InventoryManager inventory;
    private static InteractionManager owner;
    private static Transform body;
    private static bool visible;
    private static float headYawFromBody;

    internal static bool Enabled => enabled != null && enabled.Value;
    internal static float Height {get=>height.Value;set=>height.Value=value;}
    internal static float Distance {get=>reach.Value;set=>reach.Value=value;}

    internal static void Initialize(ConfigFile config)
    {
        enabled = config.Bind("Interface", "PhysicalInventory", true, "Grab miniature items from the waist belt when Immersive HUD is active.");
        height = config.Bind("Interface", "BeltHeightBelowEyes", DefaultHeight,
            new ConfigDescription("Belt height in metres below the headset.", new AcceptableValueRange<float>(.22f, .65f)));
        reach = config.Bind("Interface", "BeltReach", DefaultReach,
            new ConfigDescription("Belt distance in metres in front of the player.", new AcceptableValueRange<float>(MinimumReach, MaximumReach)));
    }

    internal static void Hide() => visible = false;

    internal static void Update(InventoryManager current, Vector3 head, Quaternion view)
    {
        if (!Enabled || current == null || !current.inventoryEnabled || current.totalSlots <= 0 ||
            UnityEngine.Cursor.lockState != CursorLockMode.Locked || MotionInput.PhotoMode || MotionInput.ConstructionPlacementActive || RepairMotion.Active)
            return;

        var manager = InteractionManager.LocalInstance;
        if (manager != owner || body == null)
        {
            owner = manager;
            body = manager == null || manager.movementController == null ? null : manager.movementController.transform;
            headYawFromBody = 0;
        }
        if (body == null || !manager.IsInteractionEnabled || manager.skatingSuppressed) return;

        var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .5f) return;
        var look = view * Vector3.forward;
        var levelLook = Vector3.ProjectOnPlane(look, Vector3.up);
        // Keep ordinary glances anchored to the body. A physical turn beyond
        // that range brings the belt around, even if the avatar never turned.
        // Keep the last heading near vertical, where head yaw is ambiguous.
        if (levelLook.sqrMagnitude > .0025f)
            headYawFromBody = Vector3.SignedAngle(forward, levelLook, Vector3.up);
        // Taper the dead zone near directly behind, so crossing ±180° does
        // not jump between a left and right offset.
        float freeLook = FreeLookYaw * (1f - Mathf.InverseLerp(90f, 180f, Mathf.Abs(headYawFromBody)));
        float beltYaw = headYawFromBody - Mathf.Clamp(headYawFromBody, -freeLook, freeLook);
        if (beltYaw != 0) forward = Quaternion.AngleAxis(beltYaw, Vector3.up) * forward;

        float down = float.IsFinite(height.Value) ? height.Value : DefaultHeight;
        float distance = float.IsFinite(reach.Value) ? reach.Value : DefaultReach;
        var offset = forward * distance - Vector3.up * down;
        surface.Center = head + offset;
        surface.Right = Vector3.Cross(Vector3.up, forward);
        surface.Normal = offset.normalized;
        surface.Up = Vector3.Cross(surface.Normal, surface.Right);
        surface.Width = Width;
        surface.Height = .09f;
        inventory = current;
        // The headset's actual frustum handles visibility. Only reject the
        // belt interaction region if it crosses the near edge of the view.
        float nearest = Vector3.Dot(look, offset)
                        - Mathf.Abs(Vector3.Dot(look, surface.Right)) * surface.Width * .5f
                        - Mathf.Abs(Vector3.Dot(look, surface.Up)) * surface.Height * .5f;
        visible = nearest > MinimumViewDepth;
        BeltModels.Update(current, head - Vector3.up * down, Quaternion.LookRotation(forward), distance);
    }

    internal static float SlotAngle(int index) => inventory.totalSlots <= 1 ? 0 : Mathf.Lerp(-65, 65, (float)index / (inventory.totalSlots - 1));
    internal static bool RenderVisible => visible && Enabled && ImmersiveHud.Enabled && StereoTrial.GameplayActive && Probe.Focused;

    internal static int Target(InteractionManager manager, bool left, ControllerTransport.Hand hand)
    {
        if (!RenderVisible || owner != manager || inventory == null || !inventory.inventoryEnabled || hand.Trigger ||
            !manager.IsInteractionEnabled || manager.skatingSuppressed || MotionInput.ConstructionPlacementActive || RepairMotion.Active ||
            MotionShovel.OwnsGrip(left) || PhysicalHeldItems.OwnsGrip(left) || WorldPress.OwnsGrip(left) || DistanceGrips.OwnsGrip(left) ||
            !StereoTrial.TryTrackingPose(hand.Position, hand.Rotation, out var palm, out var rotation))
            return -1;

        float reach = ContactRadius;
        if (MotionBody.TryPalmGeometry(manager, left, out var center, out _, out _, out float radius))
        {
            palm += rotation * center;
            reach += radius;
        }
        return BeltModels.FindSlot(palm, reach);
    }

    internal static bool CanSelect(int slot, GameObject held)
    {
        return slot >= 0 && (held != null || inventory.GetItemInSlot(slot) != null);
    }

    internal static bool TrySelect(InteractionManager manager, int slot, out GameObject item)
    {
        item = null;
        if (!CanSelect(slot, manager.cachedTopItem)) return false;
        bool alreadySelected = inventory.currentSelectedSlot == slot;
        var expected = alreadySelected ? null : inventory.GetItemInSlot(slot);
        if (!alreadySelected) inventory.SetSelectedSlot(slot);
        // Only attach items that native selection actually equipped.
        var held = manager.heldItems;
        if (inventory.currentSelectedSlot == slot && held != null && held.Count > 0 &&
            (alreadySelected || (expected != null && held.Contains(expected))))
            item = held[held.Count - 1];
        return true;
    }

    internal static bool CanStow(int target, GameObject item)
    {
        if (target < 0 || item == null) return false;
        return inventory.GetItemInSlot(inventory.currentSelectedSlot) == item;
    }

    // A full inventory cannot switch to empty hands. Consume a belt release
    // there so it cannot turn into an accidental world drop.
    internal static bool TryStow(int target, GameObject item, out bool stored)
    {
        stored = false;
        if (!CanStow(target, item)) return false;
        int selected = inventory.currentSelectedSlot;
        if (target != selected)
        {
            var displaced = inventory.GetItemInSlot(target);
            if (displaced != null)
            {
                stored = SwapOccupiedSlots(selected, target, item, displaced);
                return true;
            }

            // Saving a slot records the live held group without unequipping it.
            // Forget the old slot, save into the chosen loop, then load the now
            // empty active slot to run native stow/ownership handling.
            try
            {
                if (!inventory.ForgetStoredItem(item)) return true;
                inventory.SaveHeldItemsToSlot(target);
                if (inventory.GetItemInSlot(target) == item)
                    inventory.LoadItemsFromSlot(selected);
            }
            finally
            {
                if (inventory.GetItemInSlot(target) != item && inventory.IsSlotEmpty(selected) && owner.heldItems.Contains(item))
                    inventory.SaveHeldItemsToSlot(selected);
            }
            stored = inventory.GetItemInSlot(target) == item && !owner.heldItems.Contains(item);
            return true;
        }

        int empty = inventory.FindFirstEmptySlot();
        if (empty >= 0 && empty < inventory.totalSlots && empty != selected && inventory.GetItemInSlot(empty) == null)
        {
            inventory.SetSelectedSlot(empty);
            stored = inventory.GetItemInSlot(selected) == item && !owner.heldItems.Contains(item);
        }
        return true;
    }

    private static bool SwapOccupiedSlots(int source, int target, GameObject item, GameObject displaced)
    {
        int empty = inventory.FindFirstEmptySlot();
        if (empty >= 0)
            inventory.SetSelectedSlot(empty);
        else
            inventory.SaveHeldItemsToSlot(source);

        if (inventory.GetItemInSlot(source) != item || inventory.GetItemInSlot(target) != displaced)
            return false;

        // Move the complete native groups, including stacked holdables and bodies.
        var items = inventory.inventoryItems;
        var holdables = inventory.inventoryHoldables;
        var bodies = inventory.inventoryRigidbodies;
        (items[source], items[target]) = (items[target], items[source]);
        (holdables[source], holdables[target]) = (holdables[target], holdables[source]);
        (bodies[source], bodies[target]) = (bodies[target], bodies[source]);

        // With every slot occupied, keep the active slot and equip its new item.
        if (empty < 0) inventory.LoadItemsFromSlot(source);
        inventory.UpdateSlotIcon(source);
        inventory.UpdateSlotIcon(target);
        return !owner.heldItems.Contains(item);
    }

}
