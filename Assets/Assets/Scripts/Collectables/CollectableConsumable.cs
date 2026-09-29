using UnityEngine;

/// <summary>
/// Juice Box / Hot Dog pickup spawned only from defeated enemies.
/// </summary>
public class CollectableConsumable : CollectablePickupBase
{
    public enum ConsumableType
    {
        JuiceBox,
        HotDog
    }

    [SerializeField] private ConsumableType consumableType = ConsumableType.JuiceBox;

    public ConsumableType Type => consumableType;

    protected override void ApplyPickup(PlayerController player)
    {
        if (PlayerInventory.Instance == null)
            return;

        if (consumableType == ConsumableType.JuiceBox)
            PlayerInventory.Instance.AddJuice(1);
        else
            PlayerInventory.Instance.AddHotdogs(1);
    }
}
