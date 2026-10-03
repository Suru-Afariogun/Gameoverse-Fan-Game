using UnityEngine;

/// <summary>Rare pickup worth a big pile of crystals the moment it is touched.</summary>
public class CollectableEmerald : CollectablePickupBase
{
    [SerializeField] private int crystalValue = 100;

    protected override void ApplyPickup(PlayerController player)
    {
        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.AddCrystals(Mathf.Max(0, crystalValue) * PlayerGear.CrystalMultiplier(player));
    }
}
