using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Count's Time Clone (Attack Style upgrade): a frozen afterimage that stays where it was left and fires
/// Short Hand shots at the closest enemy using Count's equipped attack style. Enemy shots and attacks can
/// destroy it; Count's own shots pass through. Shots are owned by Count so they count as his.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CountTimeClone : MonoBehaviour, IDamageable
{
    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(32);

    private CountPlayerController owner;
    private Projectile shotPrefab;
    private SpriteRenderer spriteRenderer;
    private BoxCollider2D hurtbox;
    private Color baseColor;
    private int health;
    private float shootInterval;
    private float searchRadius;
    private float spreadAngle;
    private int machineGunPellets;
    private float machineGunPelletGap;
    private float nextVolleyAt;
    private int pendingPellets;
    private float nextPelletAt;
    private Vector2 pendingAim;
    private float hitFlashUntil;
    private float fadeOutStartedAt = -1f;
    private float shimmerPhase;
    private bool artFacesLeft = true;

    private const float FadeOutSeconds = 0.2f;

    public bool IsDead => health <= 0;
    public int Health => health;

    public static CountTimeClone Spawn(
        CountPlayerController owner,
        Vector3 position,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile shotPrefab,
        int health,
        float shootInterval,
        float searchRadius,
        float spreadAngle,
        int machineGunPellets,
        float machineGunPelletGap)
    {
        if (owner == null || sprite == null)
            return null;

        var go = new GameObject($"{owner.name}_TimeClone");
        go.transform.position = position;
        var clone = go.AddComponent<CountTimeClone>();
        clone.Init(owner, sprite, flipX, tint, shotPrefab, health, shootInterval, searchRadius, spreadAngle,
            machineGunPellets, machineGunPelletGap);
        return clone;
    }

    private void Init(
        CountPlayerController cloneOwner,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile prefab,
        int hp,
        float interval,
        float radius,
        float spread,
        int pellets,
        float pelletGap)
    {
        owner = cloneOwner;
        shotPrefab = prefab;
        health = Mathf.Max(1, hp);
        shootInterval = Mathf.Max(0.1f, interval);
        searchRadius = Mathf.Max(1f, radius);
        spreadAngle = spread;
        machineGunPellets = Mathf.Max(1, pellets);
        machineGunPelletGap = Mathf.Max(0.02f, pelletGap);
        baseColor = tint;
        nextVolleyAt = Time.time + shootInterval * 0.5f;

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.flipX = flipX;
        spriteRenderer.color = tint;
        artFacesLeft = true;

        SpriteRenderer body = owner.BodySpriteRenderer;
        SortingGroup group = owner.EffectSortingGroup;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, group, body, -1);
        if (body != null)
            spriteRenderer.sharedMaterial = body.sharedMaterial;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        rb.gravityScale = 0f;

        hurtbox = gameObject.AddComponent<BoxCollider2D>();
        hurtbox.isTrigger = true;
        Collider2D ownerBody = owner.BodyCollider;
        if (ownerBody is BoxCollider2D ownerBox)
        {
            hurtbox.size = ownerBox.size;
            hurtbox.offset = new Vector2(flipX ? -ownerBox.offset.x : ownerBox.offset.x, ownerBox.offset.y);
        }
        else if (ownerBody != null)
        {
            hurtbox.size = ownerBody.bounds.size;
        }

        EnemyDetectionZone.IgnoreAgainstAllZones(hurtbox);
    }

    private void Update()
    {
        if (fadeOutStartedAt >= 0f)
        {
            TickFadeOut();
            return;
        }

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        TickVisuals();

        if (pendingPellets > 0)
        {
            if (Time.time >= nextPelletAt)
            {
                FireShot(pendingAim);
                pendingPellets--;
                nextPelletAt = Time.time + machineGunPelletGap;
            }

            return;
        }

        if (Time.time < nextVolleyAt)
            return;

        nextVolleyAt = Time.time + shootInterval;
        Collider2D target = FindClosestEnemy();
        if (target == null)
            return;

        Vector2 aim = (Vector2)(target.bounds.center - MuzzlePosition(Mathf.Sign(target.bounds.center.x - transform.position.x)));
        if (aim.sqrMagnitude < 0.0001f)
            aim = new Vector2(FacingSign(), 0f);
        aim.Normalize();
        Face(aim.x);
        FireVolley(aim);
    }

    private void FireVolley(Vector2 aim)
    {
        if (PlayerAttackStyle.Is(AttackStyleId.MachineGun))
        {
            FireShot(aim);
            pendingPellets = machineGunPellets - 1;
            pendingAim = aim;
            nextPelletAt = Time.time + machineGunPelletGap;
            return;
        }

        if (PlayerAttackStyle.Is(AttackStyleId.SpreadShot))
        {
            FireShot(Rotate(aim, spreadAngle));
            FireShot(Rotate(aim, -spreadAngle));
            return;
        }

        FireShot(aim);
    }

    private void FireShot(Vector2 dir)
    {
        if (shotPrefab == null || owner == null)
            return;

        Projectile shot = Instantiate(shotPrefab, MuzzlePosition(FacingSign()), Quaternion.identity);
        shot.Launch(dir, shotPrefab.Speed, shotPrefab.Damage, owner.transform);
        SoundManager.Instance?.PlayCountFireRapid();
    }

    private Vector3 MuzzlePosition(float facing)
    {
        Vector3 center = hurtbox != null ? (Vector3)hurtbox.bounds.center : transform.position;
        return center + new Vector3(facing * 0.7f, -0.15f, 0f);
    }

    private float FacingSign()
    {
        if (spriteRenderer == null)
            return 1f;
        bool facingRight = artFacesLeft ? spriteRenderer.flipX : !spriteRenderer.flipX;
        return facingRight ? 1f : -1f;
    }

    private void Face(float x)
    {
        if (spriteRenderer == null || Mathf.Abs(x) < 0.01f)
            return;
        bool right = x > 0f;
        spriteRenderer.flipX = artFacesLeft ? right : !right;
    }

    private Collider2D FindClosestEnemy()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;

        Overlaps.Clear();
        Physics2D.OverlapCircle(transform.position, searchRadius, filter, Overlaps);

        Collider2D best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < Overlaps.Count; i++)
        {
            Collider2D col = Overlaps[i];
            if (!IsValidTarget(col))
                continue;

            float sqr = ((Vector2)(col.bounds.center - transform.position)).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = col;
            }
        }

        return best;
    }

    private static bool IsValidTarget(Collider2D col)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return false;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(col) || col.GetComponent<AttackHitbox>() != null)
            return false;

        Boss boss = col.GetComponentInParent<Boss>();
        if (boss != null)
            return !boss.IsDead;

        ICommonEnemy enemy = col.GetComponentInParent<ICommonEnemy>();
        return enemy != null && !enemy.IsDead;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || fadeOutStartedAt >= 0f)
            return;

        health = Mathf.Max(0, health - amount);
        hitFlashUntil = Time.time + 0.12f;
        if (health <= 0)
            BeginFadeOut();
    }

    /// <summary>Removes the clone right away (cap reached, or Count took its place).</summary>
    public void Vanish()
    {
        health = 0;
        BeginFadeOut();
    }

    private void TickVisuals()
    {
        if (spriteRenderer == null)
            return;

        shimmerPhase += Time.deltaTime * 2.2f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(shimmerPhase * Mathf.PI * 2f);
        Color c = Time.time < hitFlashUntil ? Color.white : Color.Lerp(baseColor, Color.white, shimmer * 0.18f);
        c.a = baseColor.a * Mathf.Lerp(0.85f, 1f, shimmer);
        spriteRenderer.color = c;
    }

    private void BeginFadeOut()
    {
        if (fadeOutStartedAt >= 0f)
            return;

        fadeOutStartedAt = Time.time;
        pendingPellets = 0;
        if (hurtbox != null)
            hurtbox.enabled = false;
    }

    private void TickFadeOut()
    {
        float t = Mathf.Clamp01((Time.time - fadeOutStartedAt) / FadeOutSeconds);
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = baseColor.a * (1f - t);
            spriteRenderer.color = c;
        }

        transform.localScale = Vector3.one * (1f + 0.25f * t);
        if (t >= 1f)
            Destroy(gameObject);
    }

    private static Vector2 Rotate(Vector2 dir, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos).normalized;
    }
}
