using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Super Candy: temporary invincibility with a smooth neon color cycle. Touching a common enemy
/// destroys it; touching a boss deals a fixed hit once per touch.
/// </summary>
[DisallowMultipleComponent]
public sealed class SuperCandyStarPower : MonoBehaviour
{
    // Smooth blends (never hard cuts) keep the cycle photosensitivity-safe.
    private static readonly Color[] NeonColors =
    {
        new Color(0.1f, 0.65f, 1f),
        new Color(1f, 0.25f, 0.8f),
        new Color(0.25f, 1f, 0.2f),
        new Color(1f, 0.2f, 0.2f),
        new Color(0.7f, 0.25f, 1f)
    };

    private const float SecondsPerColor = 0.18f;
    private const float BossRehitCooldown = 0.4f;

    private PlayerController player;
    private SpriteRenderer body;
    private float remaining;
    private float colorTime;
    private int bossTouchDamage = 5;
    private ContactFilter2D contactFilter;
    private Color originalColor = Color.white;
    private bool hasOriginalColor;

    private readonly List<Collider2D> overlaps = new List<Collider2D>(16);
    private readonly HashSet<Boss> bossesTouching = new HashSet<Boss>();
    private readonly HashSet<Boss> bossesTouchingNow = new HashSet<Boss>();
    private readonly Dictionary<Boss, float> bossNextHitTime = new Dictionary<Boss, float>();

    public static void Grant(PlayerController target, float seconds, int bossDamage)
    {
        if (target == null || target.IsDead)
            return;

        if (!target.TryGetComponent(out SuperCandyStarPower star))
            star = target.gameObject.AddComponent<SuperCandyStarPower>();

        star.Begin(target, seconds, bossDamage);
    }

    /// <summary>Ends Super Candy early (e.g. boarding the rocket ship).</summary>
    public static void Cancel(PlayerController target)
    {
        if (target != null && target.TryGetComponent(out SuperCandyStarPower star))
            star.End();
    }

    private void Begin(PlayerController target, float seconds, int bossDamage)
    {
        if (!hasOriginalColor)
        {
            body = target.BodySpriteRenderer;
            if (body != null)
                originalColor = body.color;
            hasOriginalColor = true;
        }

        player = target;
        remaining = Mathf.Max(0.1f, seconds);
        bossTouchDamage = Mathf.Max(0, bossDamage);
        contactFilter = new ContactFilter2D { useTriggers = true };
        contactFilter.NoFilter();
        player.SetStarPowered(true);
    }

    private void Update()
    {
        if (player == null || player.IsDead)
        {
            End();
            return;
        }

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            End();
            return;
        }

        colorTime += Time.deltaTime;
        ApplyNeonTint();
    }

    private void FixedUpdate()
    {
        if (player == null || player.IsDead || player.BodyCollider == null || !player.BodyCollider.enabled)
            return;

        Bounds b = player.BodyCollider.bounds;
        overlaps.Clear();
        Physics2D.OverlapBox(b.center, (Vector2)b.size + Vector2.one * 0.1f, 0f, contactFilter, overlaps);

        bossesTouchingNow.Clear();
        for (int i = 0; i < overlaps.Count; i++)
            HandleTouch(overlaps[i]);

        bossesTouching.Clear();
        bossesTouching.UnionWith(bossesTouchingNow);
    }

    private void HandleTouch(Collider2D other)
    {
        if (other == null || other.transform.IsChildOf(player.transform))
            return;

        // Body contact only: no detection radii or attack boxes.
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other) || other.GetComponentInParent<AttackHitbox>() != null)
            return;

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (boss.IsDead || !bossesTouchingNow.Add(boss) || bossesTouching.Contains(boss))
                return;

            if (bossNextHitTime.TryGetValue(boss, out float next) && Time.time < next)
                return;

            bossNextHitTime[boss] = Time.time + BossRehitCooldown;
            boss.TakeDamage(bossTouchDamage);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy != null && !enemy.IsDead)
            enemy.TakeDamage(Mathf.Max(1, enemy.CurrentHealth) + 999);
    }

    private void ApplyNeonTint()
    {
        if (body == null)
            return;

        float step = colorTime / SecondsPerColor;
        int index = Mathf.FloorToInt(step) % NeonColors.Length;
        Color from = NeonColors[index];
        Color to = NeonColors[(index + 1) % NeonColors.Length];
        Color tint = Color.Lerp(from, to, Mathf.SmoothStep(0f, 1f, step - Mathf.Floor(step)));

        // Keep alpha: the hit / invincibility flicker drives it.
        tint.a = body.color.a;
        body.color = tint;
    }

    private void End()
    {
        if (body != null)
            body.color = new Color(originalColor.r, originalColor.g, originalColor.b, body.color.a);

        if (player != null)
            player.SetStarPowered(false);

        Destroy(this);
    }

    private void OnDestroy()
    {
        if (player != null && player.IsStarPowered)
            player.SetStarPowered(false);
    }
}
