using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Holds the usable items the player carries (1 slot, +1 per Item Pouch level). The front item floats
/// above the active player's head and the rest trail behind it in a line. Use Item uses the front item
/// and the rest move up. Touching another usable item adds it to the back; when every slot is full it
/// swaps out the front item instead (the old one pops back out).
/// Created on demand per scene, so held items are lost on scene change; they are also lost on death.
/// </summary>
public sealed class HeldItemCarrier : MonoBehaviour
{
    private const float HoverAboveHead = 2f;
    private const float FollowSharpness = 14f;
    private const float BobAmplitude = 0.08f;
    private const float BobSpeed = 2.4f;
    private const float QueueSpacing = 1.2f;
    private const float QueueBobPhaseStep = 0.6f;

    private static HeldItemCarrier instance;

    private readonly List<UsableItem> items = new List<UsableItem>(6);
    private InputActions controls;
    private float bobTime;

    /// <summary>The front item (the one Use Item uses next), or null.</summary>
    public static UsableItem HeldItem => instance != null && instance.items.Count > 0 ? instance.items[0] : null;

    public static int HeldCount => instance != null ? instance.items.Count : 0;

    public static void Give(UsableItem item, PlayerController player)
    {
        if (item == null)
            return;

        if (instance == null)
            instance = new GameObject("Held Item Carrier").AddComponent<HeldItemCarrier>();

        instance.Take(item, player);
    }

    /// <summary>Removes every held item without using them (e.g. boarding the rocket ship).</summary>
    public static void DiscardHeldItems()
    {
        if (instance == null)
            return;

        instance.DestroyAll();
    }

    /// <summary>Auto Use gear: uses the front item for the player. False when nothing is held.</summary>
    public static bool TryAutoUse(PlayerController player)
    {
        if (instance == null || player == null || player.IsRidingVehicle)
            return false;

        return instance.UseFront(player);
    }

    private void Take(UsableItem item, PlayerController player)
    {
        PruneMissing();
        if (items.Contains(item))
            return;

        item.AttachToCarrier();
        if (items.Count < GetCapacity(player))
        {
            items.Add(item);
        }
        else
        {
            UsableItem previous = items[0];
            items[0] = item;
            previous.DropFromCarrier();
        }

        if (player != null && items.Count == 1)
        {
            bobTime = 0f;
            item.transform.position = GetSlotPoint(player, 0, item);
        }

        PlayerController reporter = player != null ? player : PlayerController.ResolveActive();
        if (reporter != null)
            reporter.ReportTutorialAction(TutorialAction.ItemPickup);
    }

    private bool UseFront(PlayerController player)
    {
        PruneMissing();
        if (items.Count == 0)
            return false;

        UsableItem item = items[0];
        if (item.Use(player))
            items.Remove(item);
        return true;
    }

    private void DestroyAll()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
                Destroy(items[i].gameObject);
        }

        items.Clear();
    }

    private void PruneMissing()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] == null)
                items.RemoveAt(i);
        }
    }

    private static int GetCapacity(PlayerController player)
    {
        PlayerController owner = player != null ? player : PlayerController.ResolveActive();
        return Mathf.Max(1, PlayerGear.ItemSlots(owner));
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputActions();

        controls.PlayerControls.Enable();
        controls.PlayerControls.UseItem.performed += OnUseItemPerformed;
    }

    private void OnDisable()
    {
        if (controls == null)
            return;

        controls.PlayerControls.UseItem.performed -= OnUseItemPerformed;
        controls.PlayerControls.Disable();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    private void LateUpdate()
    {
        PruneMissing();
        if (items.Count == 0)
            return;

        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
        {
            DestroyAll();
            return;
        }

        // Item Pouch unequipped / swapped character: extra items pop back out from the back of the line.
        int capacity = GetCapacity(player);
        while (items.Count > capacity)
        {
            UsableItem extra = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            extra.DropFromCarrier();
        }

        bobTime += Time.deltaTime;
        float follow = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
        for (int i = 0; i < items.Count; i++)
        {
            UsableItem item = items[i];
            float bob = Mathf.Sin(bobTime * BobSpeed * Mathf.PI + i * QueueBobPhaseStep) * BobAmplitude;
            Vector3 target = GetSlotPoint(player, i, item) + Vector3.up * bob;
            Transform t = item.transform;
            t.position = Vector3.Lerp(t.position, new Vector3(target.x, target.y, t.position.z), follow);
        }
    }

    private void OnUseItemPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || HeldCount == 0 || Time.timeScale <= 0f)
            return;

        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead || player.InputLocked || player.IsRidingVehicle)
            return;

        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return;

        if (UseFront(player))
            player.ReportTutorialAction(TutorialAction.ItemUse);
    }

    /// <summary>
    /// Slot 0 is centered <see cref="HoverAboveHead"/> above the top of the player's body; later slots trail
    /// behind the player (opposite their facing) at the same height.
    /// </summary>
    private static Vector3 GetSlotPoint(PlayerController player, int slot, UsableItem item)
    {
        Bounds body = player.BodyCollider != null && player.BodyCollider.enabled
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);

        float itemHalfHeight = 0f;
        if (item != null && item.TryGetComponent(out SpriteRenderer sr))
            itemHalfHeight = sr.bounds.extents.y;

        float behind = player.FacingSign >= 0f ? -1f : 1f;
        return new Vector3(body.center.x + behind * QueueSpacing * slot, body.max.y + HoverAboveHead + itemHalfHeight, 0f);
    }
}
