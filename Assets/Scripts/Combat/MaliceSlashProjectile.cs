using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum MaliceSlashKind
{
    Slash1,
    Slash2,
    Slash3,
    AirSlash
}

/// <summary>
/// Short-range slash wave fired by Malice (playable and boss). Travels a fixed distance, then fades out.
/// Pierces targets and passes through walls. Player-owned waves hit bosses / enemies / crystals;
/// boss-owned waves hit players only.
/// </summary>
public class MaliceSlashProjectile : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("True when the art's leading edge points left (it is flipped when fired to the right).")]
    [SerializeField] private bool artFacesLeft = true;
    [Tooltip("Hit area as a fraction of the current sprite bounds (x = width, y = height).")]
    [SerializeField] private Vector2 hitboxScale = new Vector2(0.7f, 0.8f);
    [SerializeField] private float fadeDuration = 0.12f;
    [SerializeField] private LayerMask hitLayers = ~0;
    [Tooltip("Playable Malice only: enemy projectiles of this power or weaker are dissipated on contact (the wave keeps going).")]
    [SerializeField] private ProjectileShotType power = ProjectileShotType.Small;

    private static readonly List<Collider2D> OverlapResults = new List<Collider2D>(24);

    private readonly HashSet<int> ownHits = new HashSet<int>();
    private MaliceSlashVolley volley;
    private Transform owner;
    private PlayerController ownerPlayer;
    private bool ownerIsPlayer;
    private int damage;
    private float directionSign = 1f;
    private float speed;
    private float remainingDistance;
    private bool launched;
    private bool holding;
    private float holdTimer;
    private bool fading;
    private float fadeTimer;
    private Color baseColor = Color.white;
    private float knockbackDistance;
    private float knockbackDuration;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    public void Launch(
        float dirSign,
        float travelDistance,
        float travelSeconds,
        int hitDamage,
        Transform shotOwner,
        MaliceSlashVolley sharedVolley,
        SpriteRenderer ownerRenderer,
        int sortingOffset,
        float fadeDelaySeconds,
        float pushDistance,
        float pushDuration)
    {
        directionSign = dirSign >= 0f ? 1f : -1f;
        remainingDistance = Mathf.Max(0f, travelDistance);
        speed = remainingDistance / Mathf.Max(0.01f, travelSeconds);
        damage = Mathf.Max(0, hitDamage);
        owner = shotOwner;
        ownerPlayer = shotOwner != null ? shotOwner.GetComponentInParent<PlayerController>() : null;
        ownerIsPlayer = ownerPlayer != null;
        volley = sharedVolley;
        ownHits.Clear();
        launched = true;
        holding = false;
        holdTimer = Mathf.Max(0f, fadeDelaySeconds);
        fading = false;
        fadeTimer = 0f;
        knockbackDistance = Mathf.Max(0f, pushDistance);
        knockbackDuration = Mathf.Max(0.02f, pushDuration);

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = artFacesLeft ? directionSign > 0f : directionSign < 0f;
            baseColor = spriteRenderer.color;
            ApplySorting(ownerRenderer, sortingOffset);
        }

        DetectHits();
        if (remainingDistance <= 0.0001f)
            FinishTravel();
    }

    private void ApplySorting(SpriteRenderer ownerRenderer, int sortingOffset)
    {
        SortingGroup group = owner != null ? owner.GetComponentInParent<SortingGroup>() : null;
        if (group != null)
        {
            spriteRenderer.sortingLayerID = group.sortingLayerID;
            spriteRenderer.sortingOrder = group.sortingOrder + 5 + sortingOffset;
            return;
        }

        if (ownerRenderer != null)
        {
            spriteRenderer.sortingLayerID = ownerRenderer.sortingLayerID;
            spriteRenderer.sortingOrder = ownerRenderer.sortingOrder + 1 + sortingOffset;
        }
    }

    private void Update()
    {
        if (fading)
        {
            TickFade();
            return;
        }

        if (holding)
        {
            holdTimer -= Time.deltaTime * GetTimeScale();
            if (holdTimer <= 0f)
                BeginFade();
            return;
        }

        if (!launched)
            return;

        float dt = Time.deltaTime * GetTimeScale();
        float step = Mathf.Min(remainingDistance, speed * dt);
        transform.position += new Vector3(directionSign * step, 0f, 0f);
        remainingDistance -= step;

        DetectHits();

        if (remainingDistance <= 0.0001f)
            FinishTravel();
    }

    private void FinishTravel()
    {
        launched = false;
        if (holdTimer > 0.0001f)
        {
            holding = true;
            return;
        }

        BeginFade();
    }

    private float GetTimeScale()
    {
        if (!HyperSpeedWorldSlow.IsActive)
            return 1f;
        if (ownerIsPlayer && !HyperSpeedWorldSlow.SlowsPlayer)
            return 1f;
        return HyperSpeedWorldSlow.WorldTimeScale;
    }

    private void BeginFade()
    {
        launched = false;
        holding = false;
        fading = true;
        fadeTimer = 0f;
        if (fadeDuration <= 0.001f)
            Destroy(gameObject);
    }

    private void TickFade()
    {
        fadeTimer += Time.deltaTime;
        float u = Mathf.Clamp01(fadeTimer / Mathf.Max(0.001f, fadeDuration));
        if (spriteRenderer != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * (1f - u);
            spriteRenderer.color = c;
        }

        if (u >= 1f)
            Destroy(gameObject);
    }

    private void DetectHits()
    {
        if (spriteRenderer == null)
            return;

        Bounds b = spriteRenderer.bounds;
        Vector2 size = new Vector2(b.size.x * hitboxScale.x, b.size.y * hitboxScale.y);
        if (size.x <= 0.001f || size.y <= 0.001f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(hitLayers);

        OverlapResults.Clear();
        Physics2D.OverlapBox(b.center, size, 0f, filter, OverlapResults);
        for (int i = 0; i < OverlapResults.Count; i++)
            TryHit(OverlapResults[i]);
    }

    private void TryHit(Collider2D other)
    {
        if (other == null || damage <= 0)
            return;

        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
            return;

        if (EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return;

        // Melee boxes are tools, not bodies.
        if (other.GetComponent<AttackHitbox>() != null)
            return;

        Projectile shot = other.GetComponentInParent<Projectile>();
        if (shot != null)
        {
            TryDissipateProjectile(shot);
            return;
        }

        if (other.GetComponentInParent<MaliceSlashProjectile>() != null)
            return;

        if (!ownerIsPlayer)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.IsDead || !TryClaim(player.GetInstanceID()))
                return;

            player.TakeDamage(damage, owner != null ? owner : transform);
            return;
        }

        Crystal crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            if (crystal.IsDead || !TryClaim(crystal.GetInstanceID()))
                return;

            int before = crystal.CurrentHealth;
            crystal.TakeDamage(damage);
            NotifyOwnerDamageDealt(before - crystal.CurrentHealth);
            return;
        }

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (boss.IsDead || !TryClaim(boss.GetInstanceID()))
                return;

            int before = boss.CurrentHealth;
            boss.TakeDamage(damage);
            NotifyOwnerDamageDealt(before - boss.CurrentHealth);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy is Component enemyComponent)
        {
            if (enemy.IsDead || !TryClaim(enemyComponent.GetInstanceID()))
                return;

            int before = enemy.CurrentHealth;
            enemy.TakeDamage(damage);
            NotifyOwnerDamageDealt(before - enemy.CurrentHealth);
            if (!enemy.IsDead && enemyComponent != null && !IsStationaryEnemy(enemy))
                GentleKnockback.Apply(enemyComponent, directionSign, knockbackDistance, knockbackDuration);
            return;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable is Component damageableComponent && !damageable.IsDead &&
            TryClaim(damageableComponent.GetInstanceID()))
        {
            damageable.TakeDamage(damage);
        }
    }

    /// <summary>Enemies that hold their spot (and Blocker Bot walls) are never pushed.</summary>
    private static bool IsStationaryEnemy(ICommonEnemy enemy)
    {
        return enemy is BlockerBot || enemy is LaserBot || enemy is ChaoticTanker;
    }

    private void TryDissipateProjectile(Projectile shot)
    {
        if (!ownerIsPlayer || !(ownerPlayer is MalicePlayerController))
            return;

        if (!shot.IsLaunched || shot.IsResolvingHit || shot.HasResolvedCombatHit)
            return;

        if (shot.Owner != null && shot.Owner.GetComponentInParent<PlayerController>() != null)
            return;

        if (GetPowerRank(shot.ShotType) > GetPowerRank(power))
            return;

        SoundManager.Instance?.PlayProjectileHit(shot.ShotType);
        shot.ClashAndDespawn();
    }

    private static int GetPowerRank(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Big: return 3;
            case ProjectileShotType.Medium: return 2;
            default: return 1;
        }
    }

    private bool TryClaim(int targetId)
    {
        if (!ownHits.Add(targetId))
            return false;
        return volley == null || volley.TryClaim(targetId);
    }

    private void NotifyOwnerDamageDealt(int amountDealt)
    {
        if (amountDealt > 0 && ownerPlayer is MalicePlayerController malice)
            malice.NotifyDamageDealt(amountDealt);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        hitboxScale = new Vector2(Mathf.Clamp(hitboxScale.x, 0.05f, 1.5f), Mathf.Clamp(hitboxScale.y, 0.05f, 1.5f));
        fadeDuration = Mathf.Max(0f, fadeDuration);
    }
#endif
}

/// <summary>Shared hit list so a whole slash volley counts as one hit per target.</summary>
public sealed class MaliceSlashVolley
{
    private readonly HashSet<int> hitIds = new HashSet<int>();

    public bool TryClaim(int targetId) => hitIds.Add(targetId);
}

/// <summary>
/// Malice projectile-slash layout and prefabs (used by playable Malice and Boss Malice).
/// Slash 1 / 2: a row of purple waves with a red wave just behind each, fired forward from the AttackBox front edge.
/// Slash 3 / Air Slash: big purple + big red pair from the AttackBox front edge (forward)
/// and another pair from the back edge (backward).
/// </summary>
[Serializable]
public class MaliceSlashProjectileSettings
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private MaliceSlashProjectile smallPurplePrefab;
    [SerializeField] private MaliceSlashProjectile smallRedPrefab;
    [SerializeField] private MaliceSlashProjectile bigPurplePrefab;
    [SerializeField] private MaliceSlashProjectile bigRedPrefab;

    [Header("Slash 1 / 2 (small)")]
    [SerializeField] private int smallPurpleCount = 3;
    [Tooltip("World units between each purple/red pair.")]
    [SerializeField] private float smallPurpleSpacing = 0.75f;
    [Tooltip("World units each red wave trails behind its purple wave.")]
    [SerializeField] private float smallRedBehind = 0.1f;
    [SerializeField] private float smallTravelDistance = 1.5f;

    [Header("Slash 3 / Air Slash (big)")]
    [Tooltip("World units the big red wave trails behind the big purple wave.")]
    [SerializeField] private float bigRedBehind = 0.2f;
    [SerializeField] private float bigTravelDistance = 1f;

    [Header("Timing")]
    [Tooltip("Seconds to cover the travel distance before fading out.")]
    [SerializeField] private float travelSeconds = 0.2f;
    [Tooltip("Clip time of each animation's 2nd frame (when the waves fire).")]
    [SerializeField] private float slash1FireClipTime = 0.1667f;
    [SerializeField] private float slash2FireClipTime = 0.125f;
    [SerializeField] private float slash3FireClipTime = 0.125f;
    [SerializeField] private float airSlashFireClipTime = 0.0833f;

    [Tooltip("Slash 1 / 2: after arriving, pairs fade one after another from the last (rear) pair to the first (front) pair.")]
    [SerializeField] private float smallPairFadeStagger = 0.08f;

    [Header("Hits")]
    [Tooltip("Off: a target is hit at most once per volley (all waves of one slash count as one hit). On: every wave can hit separately.")]
    [SerializeField] private bool eachWaveHitsSeparately = false;
    [Tooltip("Gentle push on hit moving enemies (world units). Bosses and stationary enemies (Laser Bot, Chaotic Tanker, Blocker Bot) are never pushed.")]
    [SerializeField] private float knockbackDistance = 1f;
    [SerializeField] private float knockbackDuration = 0.12f;

    public bool Enabled => enabled;

    public float GetFireClipTime(MaliceSlashKind kind)
    {
        switch (kind)
        {
            case MaliceSlashKind.Slash1: return slash1FireClipTime;
            case MaliceSlashKind.Slash2: return slash2FireClipTime;
            case MaliceSlashKind.Slash3: return slash3FireClipTime;
            default: return airSlashFireClipTime;
        }
    }

    public void Fire(
        MaliceSlashKind kind,
        Transform owner,
        Collider2D attackBox,
        float facingSign,
        int damage,
        SpriteRenderer ownerRenderer,
        float extraTravelDistance = 0f,
        int extraSmallPairs = 0,
        float extraKnockback = 0f)
    {
        if (!enabled || owner == null)
            return;

        GetAttackBoxWorld(attackBox, owner, out Vector2 center, out float halfWidth);
        float dir = facingSign >= 0f ? 1f : -1f;
        MaliceSlashVolley volley = eachWaveHitsSeparately ? null : new MaliceSlashVolley();
        float extraDistance = Mathf.Max(0f, extraTravelDistance);
        float knockback = knockbackDistance + Mathf.Max(0f, extraKnockback);

        if (kind == MaliceSlashKind.Slash1 || kind == MaliceSlashKind.Slash2)
        {
            Vector2 smallFront = center + new Vector2(dir * halfWidth, 0f);
            float smallDistance = smallTravelDistance + extraDistance;
            int count = Mathf.Max(1, smallPurpleCount + Mathf.Max(0, extraSmallPairs));
            for (int i = 0; i < count; i++)
            {
                Vector2 purplePos = smallFront - new Vector2(dir * smallPurpleSpacing * i, 0f);
                Vector2 redPos = purplePos - new Vector2(dir * smallRedBehind, 0f);
                float fadeDelay = smallPairFadeStagger * (count - 1 - i);
                Spawn(smallRedPrefab, redPos, dir, smallDistance, damage, owner, volley, ownerRenderer, 0, fadeDelay, knockback);
                Spawn(smallPurplePrefab, purplePos, dir, smallDistance, damage, owner, volley, ownerRenderer, 1, fadeDelay, knockback);
            }
            return;
        }

        float bigDistance = bigTravelDistance + extraDistance;
        Vector2 front = center + new Vector2(dir * halfWidth, 0f);
        Spawn(bigRedPrefab, front - new Vector2(dir * bigRedBehind, 0f), dir, bigDistance, damage, owner, volley, ownerRenderer, 0, 0f, knockback);
        Spawn(bigPurplePrefab, front, dir, bigDistance, damage, owner, volley, ownerRenderer, 1, 0f, knockback);

        Vector2 back = center - new Vector2(dir * halfWidth, 0f);
        Spawn(bigRedPrefab, back + new Vector2(dir * bigRedBehind, 0f), -dir, bigDistance, damage, owner, volley, ownerRenderer, 0, 0f, knockback);
        Spawn(bigPurplePrefab, back, -dir, bigDistance, damage, owner, volley, ownerRenderer, 1, 0f, knockback);
    }

    private void Spawn(
        MaliceSlashProjectile prefab,
        Vector2 position,
        float dir,
        float distance,
        int damage,
        Transform owner,
        MaliceSlashVolley volley,
        SpriteRenderer ownerRenderer,
        int sortingOffset,
        float fadeDelay,
        float knockback)
    {
        if (prefab == null)
            return;

        MaliceSlashProjectile wave = UnityEngine.Object.Instantiate(prefab, new Vector3(position.x, position.y, 0f), Quaternion.identity);
        wave.Launch(dir, distance, travelSeconds, damage, owner, volley, ownerRenderer, sortingOffset,
            fadeDelay, knockback, knockbackDuration);
    }

    private static void GetAttackBoxWorld(Collider2D box, Transform owner, out Vector2 center, out float halfWidth)
    {
        if (box is BoxCollider2D boxCollider)
        {
            Transform t = boxCollider.transform;
            center = t.TransformPoint(boxCollider.offset);
            halfWidth = Mathf.Abs(boxCollider.size.x * t.lossyScale.x) * 0.5f;
            return;
        }

        if (box != null && box.enabled)
        {
            center = box.bounds.center;
            halfWidth = box.bounds.extents.x;
            return;
        }

        center = box != null ? (Vector2)box.transform.position : (Vector2)owner.position;
        halfWidth = 0f;
    }

    public void Validate()
    {
        smallPurpleCount = Mathf.Clamp(smallPurpleCount, 1, 8);
        smallPurpleSpacing = Mathf.Max(0f, smallPurpleSpacing);
        smallRedBehind = Mathf.Max(0f, smallRedBehind);
        smallTravelDistance = Mathf.Max(0f, smallTravelDistance);
        bigRedBehind = Mathf.Max(0f, bigRedBehind);
        bigTravelDistance = Mathf.Max(0f, bigTravelDistance);
        travelSeconds = Mathf.Max(0.02f, travelSeconds);
        slash1FireClipTime = Mathf.Max(0f, slash1FireClipTime);
        slash2FireClipTime = Mathf.Max(0f, slash2FireClipTime);
        slash3FireClipTime = Mathf.Max(0f, slash3FireClipTime);
        airSlashFireClipTime = Mathf.Max(0f, airSlashFireClipTime);
        smallPairFadeStagger = Mathf.Max(0f, smallPairFadeStagger);
        knockbackDistance = Mathf.Max(0f, knockbackDistance);
        knockbackDuration = Mathf.Max(0.02f, knockbackDuration);
    }
}
