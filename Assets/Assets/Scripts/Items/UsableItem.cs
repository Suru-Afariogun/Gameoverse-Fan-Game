using System.Collections.Generic;
using UnityEngine;

public enum UsableItemType
{
    SuperCandy = 0,
    Scouter = 1,
    BananaPhone = 2,
    Bomb = 3,
    SilverSwords = 4
}

/// <summary>
/// One-time-use item. Touching it attaches it above the player's head (see <see cref="HeldItemCarrier"/>);
/// Use Item consumes it, fires checkpoint sparks from its box collider, and applies its effect.
/// </summary>
public class UsableItem : CollectablePickupBase
{
    [SerializeField] private UsableItemType itemType = UsableItemType.SuperCandy;

    [Header("Use Sparks")]
    [Tooltip("CheckPoint Bot Sparks prefab.")]
    [SerializeField] private GameVisualEffect useSparkPrefab;
    [SerializeField] private int useSparkCount = 5;
    [SerializeField] private float useSparkSpeed = 8f;
    [SerializeField] private float useSparkMaxHeight = 2f;
    [SerializeField] private float useSparkLifetime = 1.5f;

    [Header("Super Candy")]
    [SerializeField] private float starPowerSeconds = 15f;
    [SerializeField] private int starBossTouchDamage = 5;

    [Header("Scouter")]
    [SerializeField] private float scouterSeconds = 20f;

    [Header("Bannana Phone")]
    [SerializeField] private CollectableConsumable juiceBoxPrefab;
    [SerializeField] private CollectableConsumable hotDogPrefab;
    [SerializeField] private int supplyDropCount = 5;
    [Tooltip("Gap between the top of the player's head and the bottom of the ship.")]
    [SerializeField] private float shipHoverAboveHead = 3f;

    [Header("Bomb")]
    [SerializeField] private int bombBossDamage = 10;

    [Header("Summonable Silver Sword")]
    [Tooltip("4 swords appear on each side of the player, float up, shoot down through enemies and send silver slashes along the ground.")]
    [SerializeField] private SilverSwordSettings silverSwords = new SilverSwordSettings();

    private static readonly List<MonoBehaviour> BehaviourScratch = new List<MonoBehaviour>(64);

    private int usesSpent;

    public UsableItemType ItemType => itemType;

    protected override bool DestroyOnCollect => false;

    protected override bool MagnetPullable => false;

    protected override void ApplyPickup(PlayerController player)
    {
        HeldItemCarrier.Give(this, player);
    }

    /// <summary>Called by the carrier when it takes this item.</summary>
    public void AttachToCarrier()
    {
        BeginHeld();
    }

    /// <summary>Swapped out for another item: pop back out and wait to be picked up again.</summary>
    public void DropFromCarrier()
    {
        float angle = Random.Range(40f, 140f) * Mathf.Deg2Rad;
        LaunchBurst(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), collectDelay: 1f);
    }

    /// <summary>
    /// Applies the item's effect. Returns true when the item is used up (and destroyed); Item Extender lets
    /// Bomb / Banana Phone / Silver Swords stay held for extra uses.
    /// </summary>
    public bool Use(PlayerController player)
    {
        SpawnUseSparks();
        float durationMultiplier = PlayerGear.ItemDurationMultiplier(player);

        switch (itemType)
        {
            case UsableItemType.SuperCandy:
                SuperCandyStarPower.Grant(player, starPowerSeconds * durationMultiplier, starBossTouchDamage);
                SoundManager.Instance?.PlayCollectItem();
                break;
            case UsableItemType.Scouter:
                UseScouter(player, scouterSeconds * durationMultiplier);
                SoundManager.Instance?.PlayUiConfirm();
                break;
            case UsableItemType.BananaPhone:
                UseBananaPhone(player);
                SoundManager.Instance?.PlayUiConfirm();
                break;
            case UsableItemType.Bomb:
                UseBomb();
                SoundManager.Instance?.PlayEnemyExplosion();
                break;
            case UsableItemType.SilverSwords:
                SilverSwordSquad.SpawnItemVolley(player, silverSwords);
                break;
        }

        if (IsMultiUse)
        {
            usesSpent++;
            if (usesSpent < PlayerGear.ItemUses(player))
                return false;
        }

        Destroy(gameObject);
        return true;
    }

    /// <summary>One-shot items that Item Extender gives extra uses (timed items get longer instead).</summary>
    private bool IsMultiUse =>
        itemType == UsableItemType.Bomb ||
        itemType == UsableItemType.BananaPhone ||
        itemType == UsableItemType.SilverSwords;

    private void SpawnUseSparks()
    {
        // The collider is off while held, so derive its center from the box's local offset.
        Vector3 origin = transform.position;
        if (TryGetComponent(out BoxCollider2D box))
            origin = transform.TransformPoint(box.offset);

        VisualEffects.SpawnCheckPointBotConfettiSparks(
            useSparkPrefab,
            origin,
            Mathf.Max(1, useSparkCount),
            Mathf.Max(0.1f, useSparkSpeed),
            Mathf.Max(0.1f, useSparkMaxHeight),
            Mathf.Max(0.1f, useSparkLifetime),
            GetComponent<SpriteRenderer>());
    }

    /// <summary>A random equipped upgrade maxes out; with none equipped, any upgrade this character has.</summary>
    private void UseScouter(PlayerController player, float seconds)
    {
        if (player == null)
            return;

        string id = player.CharacterId;
        List<UpgradeType> equipped = new List<UpgradeType>(3);
        List<UpgradeType> available = new List<UpgradeType>(3);
        foreach (UpgradeType type in System.Enum.GetValues(typeof(UpgradeType)))
        {
            if (!PlayerUpgrades.IsAvailable(id, type))
                continue;

            available.Add(type);
            if (PlayerUpgrades.CanToggleEquipped(id, type) && PlayerUpgrades.IsEquipped(id, type))
                equipped.Add(type);
        }

        List<UpgradeType> pool = equipped.Count > 0 ? equipped : available;
        if (pool.Count == 0)
            return;

        PlayerUpgrades.BoostToMax(id, pool[Random.Range(0, pool.Count)], seconds);
    }

    private void UseBananaPhone(PlayerController player)
    {
        if (player == null)
            return;

        int count = Mathf.Max(0, supplyDropCount);
        if (KitRocketShip.TryDeliverSupplies(player, juiceBoxPrefab, hotDogPrefab, count, shipHoverAboveHead))
            return;

        // No ship available: the supplies just fall from above the player.
        Bounds body = player.BodyCollider != null ? player.BodyCollider.bounds : new Bounds(player.transform.position, Vector3.one);
        Vector3 dropPoint = new Vector3(body.center.x, body.max.y + shipHoverAboveHead, 0f);
        for (int i = 0; i < count; i++)
            DropSupply(juiceBoxPrefab, hotDogPrefab, dropPoint + Vector3.right * Random.Range(-0.6f, 0.6f));
    }

    /// <summary>Drops one random Juice Box / Hot Dog straight down from <paramref name="point"/>.</summary>
    public static void DropSupply(CollectableConsumable juiceBox, CollectableConsumable hotDog, Vector3 point)
    {
        CollectableConsumable prefab = Random.value < 0.5f ? juiceBox : hotDog;
        if (prefab == null)
            prefab = juiceBox != null ? juiceBox : hotDog;
        if (prefab == null)
            return;

        CollectableConsumable drop = Instantiate(prefab, point, Quaternion.identity);
        drop.LaunchBurst(new Vector2(Random.Range(-0.2f, 0.2f), -1f));
        SoundManager.Instance?.PlayItemDrop();
    }

    /// <summary>Every common enemy in the camera view dies; bosses in view take a fixed hit.</summary>
    private void UseBomb()
    {
        if (!TryGetViewRect(out Rect view))
            return;

        BehaviourScratch.Clear();
        BehaviourScratch.AddRange(FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None));
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour behaviour = BehaviourScratch[i];
            if (behaviour == null || !behaviour.isActiveAndEnabled || !IsInView(behaviour.transform, view))
                continue;

            if (behaviour is Boss boss)
            {
                if (!boss.IsDead)
                    boss.TakeDamage(Mathf.Max(0, bombBossDamage));
            }
            else if (behaviour is ICommonEnemy enemy && !enemy.IsDead)
            {
                enemy.TakeDamage(Mathf.Max(1, enemy.CurrentHealth) + 999);
            }
        }

        BehaviourScratch.Clear();
    }

    private static bool IsInView(Transform t, Rect view)
    {
        Vector3 p = t.position;
        return p.x >= view.xMin && p.x <= view.xMax && p.y >= view.yMin && p.y <= view.yMax;
    }

    private static bool TryGetViewRect(out Rect view)
    {
        Camera cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetComponent<Camera>() : Camera.main;
        if (cam == null)
        {
            view = default;
            return false;
        }

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 c = cam.transform.position;
        view = new Rect(c.x - halfWidth, c.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        return true;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        useSparkCount = Mathf.Max(1, useSparkCount);
        starPowerSeconds = Mathf.Max(0.1f, starPowerSeconds);
        starBossTouchDamage = Mathf.Max(0, starBossTouchDamage);
        scouterSeconds = Mathf.Max(0.1f, scouterSeconds);
        supplyDropCount = Mathf.Max(0, supplyDropCount);
        shipHoverAboveHead = Mathf.Max(0f, shipHoverAboveHead);
        bombBossDamage = Mathf.Max(0, bombBossDamage);
        silverSwords?.Validate();
    }
#endif
}
