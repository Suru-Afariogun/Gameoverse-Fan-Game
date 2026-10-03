using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hex's V-Bot (art from Little Program Harlie). Spawned in pairs beside Hex: slowly backs up one space,
/// then rockets forward and vanishes. Phases through everything and is harmless while backing up.
/// Hits each target once for a fixed amount (no damage multipliers; Hex's upgrades pass their own numbers).
/// Can also orbit Hex (Attack Style upgrade), touching enemies for small damage until it is sent flying,
/// and can home in on the closest enemy like a missile (Hyper Ability max level).
/// Player-owned bots hit bosses / enemies / crystals; boss-owned bots hit the player.
/// </summary>
public class HexVBot : MonoBehaviour
{
    private enum Phase
    {
        BackUp,
        Shoot,
        Orbit,
        Fade
    }

    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("True when the art faces left (it is flipped when flying right).")]
    [SerializeField] private bool artFacesLeft = true;
    [SerializeField] private int damage = 2;
    [Tooltip("World units per space.")]
    [SerializeField] private float spaceSize = 1f;
    [SerializeField] private float backUpSpaces = 1f;
    [SerializeField] private float backUpSeconds = 0.45f;
    [SerializeField] private float shootSpaces = 10f;
    [Tooltip("World units per second while shooting forward.")]
    [SerializeField] private float shootSpeed = 24f;
    [SerializeField] private float fadeSeconds = 0.12f;
    [Tooltip("Hit area as a fraction of the sprite bounds (x = width, y = height).")]
    [SerializeField] private Vector2 hitboxScale = new Vector2(0.8f, 0.7f);
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private int sortingOffset = 3;

    [Header("Homing")]
    [SerializeField] private float homingTurnDegreesPerSecond = 540f;
    [Tooltip("How far away (world units) a homing bot looks for the closest enemy.")]
    [SerializeField] private float homingSearchRadius = 14f;
    [SerializeField] private float homingRetargetInterval = 0.1f;

    [Header("Orbit")]
    [Tooltip("Seconds before an orbiting bot can touch the same target again.")]
    [SerializeField] private float orbitRehitSeconds = 0.5f;

    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(24);
    private readonly HashSet<int> hitIds = new HashSet<int>();
    private readonly Dictionary<int, float> contactHitTimes = new Dictionary<int, float>();

    private Transform owner;
    private PlayerController ownerPlayer;
    private Collider2D ownerBody;
    private bool ownerIsPlayer;
    private float forwardSign = 1f;
    private Phase phase = Phase.BackUp;
    private float timer;
    private float backUpStartX;
    private float remainingShoot;
    private int activeDamage;
    private Vector2 shootDirection = Vector2.right;
    private bool homing;
    private Collider2D homingTarget;
    private float retargetTimer;
    private float orbitAngle;
    private float orbitRadius;
    private float orbitDegreesPerSecond;
    private int contactDamage;
    private Color baseColor = Color.white;
    private bool launched;

    public bool IsOrbiting => launched && phase == Phase.Orbit;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
    }

    /// <summary>
    /// Starts the bot. forward = the direction it rockets toward (it backs up the other way first).
    /// Negative overrides keep the prefab's damage / shoot distance.
    /// </summary>
    public void Launch(Transform summoner, float forward, int damageOverride = -1, float shootSpacesOverride = -1f,
        bool homingMissile = false)
    {
        SetOwner(summoner);
        phase = Phase.BackUp;
        timer = 0f;
        backUpStartX = transform.position.x;
        launched = true;
        ConfigureShot(forward, damageOverride, shootSpacesOverride, homingMissile);
        ApplySorting();
    }

    /// <summary>Circles the summoner, touching enemies for <paramref name="touchDamage"/>, until LaunchFromOrbit.</summary>
    public void BeginOrbit(Transform summoner, float startAngleDegrees, float radius, float degreesPerSecond,
        int touchDamage)
    {
        SetOwner(summoner);
        phase = Phase.Orbit;
        orbitAngle = startAngleDegrees;
        orbitRadius = Mathf.Max(0f, radius);
        orbitDegreesPerSecond = degreesPerSecond;
        contactDamage = Mathf.Max(0, touchDamage);
        contactHitTimes.Clear();
        launched = true;
        transform.rotation = Quaternion.identity;
        PlaceOnOrbit();
        ApplySorting();
    }

    /// <summary>Sends an orbiting bot straight ahead (no back-up).</summary>
    public void LaunchFromOrbit(float forward, int damageOverride = -1, float shootSpacesOverride = -1f,
        bool homingMissile = false)
    {
        if (!launched || phase == Phase.Fade)
            return;

        ConfigureShot(forward, damageOverride, shootSpacesOverride, homingMissile);
        phase = Phase.Shoot;
        SoundManager.Instance?.PlayDash();
    }

    /// <summary>Sends an orbiting bot in a straight line along <paramref name="direction"/> (no steering after launch).</summary>
    public void LaunchFromOrbitToward(Vector2 direction, int damageOverride = -1, float shootSpacesOverride = -1f)
    {
        if (!launched || phase == Phase.Fade)
            return;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;

        phase = Phase.Shoot;
        ConfigureShot(direction.x >= 0f ? 1f : -1f, damageOverride, shootSpacesOverride, false);
        shootDirection = direction.normalized;
        FaceDirection(shootDirection);
        SoundManager.Instance?.PlayDash();
    }

    private void SetOwner(Transform summoner)
    {
        owner = summoner;
        ownerPlayer = summoner != null ? summoner.GetComponentInParent<PlayerController>() : null;
        ownerIsPlayer = ownerPlayer != null;
        ownerBody = summoner != null ? summoner.GetComponent<Collider2D>() : null;
    }

    private void ConfigureShot(float forward, int damageOverride, float shootSpacesOverride, bool homingMissile)
    {
        forwardSign = forward >= 0f ? 1f : -1f;
        shootDirection = new Vector2(forwardSign, 0f);
        activeDamage = damageOverride >= 0 ? damageOverride : Mathf.Max(0, damage);
        float spaces = shootSpacesOverride >= 0f ? shootSpacesOverride : shootSpaces;
        remainingShoot = Mathf.Max(0f, spaces * spaceSize);
        homing = homingMissile && ownerIsPlayer;
        homingTarget = null;
        retargetTimer = 0f;
        hitIds.Clear();
        transform.rotation = Quaternion.identity;
        FaceDirection(shootDirection);
    }

    private void ApplySorting()
    {
        if (owner == null || spriteRenderer == null)
            return;

        Boss boss = ownerPlayer == null ? owner.GetComponentInParent<Boss>() : null;
        SortingGroup group = ownerPlayer != null ? ownerPlayer.EffectSortingGroup : boss != null ? boss.EffectSortingGroup : null;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, group, owner.GetComponent<SpriteRenderer>(), sortingOffset);
    }

    private void Update()
    {
        if (!launched)
            return;

        float dt = Time.deltaTime * GetTimeScale();
        switch (phase)
        {
            case Phase.BackUp:
                TickBackUp(dt);
                break;
            case Phase.Shoot:
                TickShoot(dt);
                break;
            case Phase.Orbit:
                TickOrbit(dt);
                break;
            default:
                TickFade(Time.deltaTime);
                break;
        }
    }

    private void TickBackUp(float dt)
    {
        timer += dt;
        float duration = Mathf.Max(0.01f, backUpSeconds);
        float t = Mathf.Clamp01(timer / duration);
        float eased = t * t * (3f - 2f * t);
        Vector3 pos = transform.position;
        pos.x = backUpStartX - forwardSign * Mathf.Max(0f, backUpSpaces * spaceSize) * eased;
        transform.position = pos;

        if (t < 1f)
            return;

        phase = Phase.Shoot;
        SoundManager.Instance?.PlayDash();
    }

    private void TickShoot(float dt)
    {
        if (homing)
            SteerTowardTarget(dt);

        float step = Mathf.Min(remainingShoot, Mathf.Max(0.1f, shootSpeed) * dt);
        transform.position += (Vector3)(shootDirection * step);
        remainingShoot -= step;
        DetectHits();

        if (remainingShoot > 0.0001f)
            return;

        BeginFade();
    }

    private void TickOrbit(float dt)
    {
        if (owner == null || (ownerPlayer != null && ownerPlayer.IsDead))
        {
            BeginFade();
            return;
        }

        orbitAngle = Mathf.Repeat(orbitAngle + orbitDegreesPerSecond * dt, 360f);
        PlaceOnOrbit();
        if (ownerPlayer != null)
            FaceDirection(new Vector2(ownerPlayer.FacingSign, 0f));
        DetectHits();
    }

    private void PlaceOnOrbit()
    {
        Vector3 center = ownerBody != null ? ownerBody.bounds.center : (owner != null ? owner.position : transform.position);
        float rad = orbitAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(center.x + Mathf.Cos(rad) * orbitRadius, center.y + Mathf.Sin(rad) * orbitRadius, 0f);
    }

    private void BeginFade()
    {
        phase = Phase.Fade;
        timer = 0f;
    }

    private void TickFade(float dt)
    {
        timer += dt;
        float u = fadeSeconds <= 0.001f ? 1f : Mathf.Clamp01(timer / fadeSeconds);
        if (spriteRenderer != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * (1f - u);
            spriteRenderer.color = c;
        }

        if (u >= 1f)
            Destroy(gameObject);
    }

    // ---------- Homing ----------

    private void SteerTowardTarget(float dt)
    {
        retargetTimer -= dt;
        if (retargetTimer <= 0f || !IsValidHomingTarget(homingTarget))
        {
            retargetTimer = Mathf.Max(0.02f, homingRetargetInterval);
            homingTarget = FindClosestEnemy();
        }

        if (homingTarget == null)
            return;

        Vector2 toTarget = (Vector2)(homingTarget.bounds.center - transform.position);
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        float maxRadians = Mathf.Max(0f, homingTurnDegreesPerSecond) * Mathf.Deg2Rad * dt;
        Vector3 turned = Vector3.RotateTowards(shootDirection, toTarget.normalized, maxRadians, 0f);
        shootDirection = ((Vector2)turned).normalized;
        FaceDirection(shootDirection);
    }

    private Collider2D FindClosestEnemy()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(hitLayers);

        Overlaps.Clear();
        Physics2D.OverlapCircle(transform.position, Mathf.Max(0.5f, homingSearchRadius), filter, Overlaps);

        Collider2D best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < Overlaps.Count; i++)
        {
            Collider2D col = Overlaps[i];
            if (!IsValidHomingTarget(col))
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

    private bool IsValidHomingTarget(Collider2D col)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return false;
        if (owner != null && (col.transform == owner || col.transform.IsChildOf(owner)))
            return false;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(col) || col.GetComponent<AttackHitbox>() != null)
            return false;

        Boss boss = col.GetComponentInParent<Boss>();
        if (boss != null)
            return !boss.IsDead && !hitIds.Contains(boss.GetInstanceID());

        ICommonEnemy enemy = col.GetComponentInParent<ICommonEnemy>();
        if (enemy is Component enemyComponent)
            return !enemy.IsDead && !hitIds.Contains(enemyComponent.GetInstanceID());

        return false;
    }

    private void FaceDirection(Vector2 direction)
    {
        if (spriteRenderer == null || direction.sqrMagnitude < 0.0001f)
            return;

        float sign = Mathf.Abs(direction.x) > 0.01f ? (direction.x > 0f ? 1f : -1f) : forwardSign;
        spriteRenderer.flipX = artFacesLeft ? sign > 0f : sign < 0f;
        if (phase == Phase.Orbit)
            return;

        float angle = Vector2.SignedAngle(new Vector2(sign, 0f), direction);
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ---------- Hits ----------

    private float GetTimeScale()
    {
        if (!HyperSpeedWorldSlow.IsActive)
            return 1f;
        if (ownerIsPlayer && !HyperSpeedWorldSlow.SlowsPlayer)
            return 1f;
        return HyperSpeedWorldSlow.WorldTimeScale;
    }

    private int CurrentDamage => phase == Phase.Orbit ? contactDamage : activeDamage;

    private bool Claim(int targetId)
    {
        if (phase != Phase.Orbit)
            return hitIds.Add(targetId);

        if (contactHitTimes.TryGetValue(targetId, out float last) && Time.time - last < orbitRehitSeconds)
            return false;

        contactHitTimes[targetId] = Time.time;
        return true;
    }

    private void DetectHits()
    {
        if (spriteRenderer == null || CurrentDamage <= 0)
            return;

        Bounds b = spriteRenderer.bounds;
        Vector2 size = new Vector2(b.size.x * hitboxScale.x, b.size.y * hitboxScale.y);
        if (size.x <= 0.001f || size.y <= 0.001f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(hitLayers);

        Overlaps.Clear();
        Physics2D.OverlapBox(b.center, size, transform.eulerAngles.z, filter, Overlaps);
        for (int i = 0; i < Overlaps.Count; i++)
            TryHit(Overlaps[i]);
    }

    private void TryHit(Collider2D other)
    {
        if (other == null)
            return;
        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
            return;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other) || other.GetComponent<AttackHitbox>() != null)
            return;
        if (other.GetComponentInParent<Projectile>() != null || other.GetComponentInParent<MaliceSlashProjectile>() != null)
            return;

        int amount = CurrentDamage;
        if (!ownerIsPlayer)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && !player.IsDead && Claim(player.GetInstanceID()))
                player.TakeDamage(amount, owner != null ? owner : transform);
            return;
        }

        Crystal crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            if (!crystal.IsDead && Claim(crystal.GetInstanceID()))
                crystal.TakeDamage(amount);
            return;
        }

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (!boss.IsDead && Claim(boss.GetInstanceID()))
                boss.TakeDamage(amount);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy is Component enemyComponent)
        {
            if (!enemy.IsDead && Claim(enemyComponent.GetInstanceID()))
                enemy.TakeDamage(amount);
            return;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable is Component damageableComponent && !damageable.IsDead &&
            Claim(damageableComponent.GetInstanceID()))
            damageable.TakeDamage(amount);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        damage = Mathf.Max(0, damage);
        spaceSize = Mathf.Max(0.01f, spaceSize);
        backUpSpaces = Mathf.Max(0f, backUpSpaces);
        backUpSeconds = Mathf.Max(0.01f, backUpSeconds);
        shootSpaces = Mathf.Max(0f, shootSpaces);
        shootSpeed = Mathf.Max(0.1f, shootSpeed);
        fadeSeconds = Mathf.Max(0f, fadeSeconds);
        hitboxScale = new Vector2(Mathf.Clamp(hitboxScale.x, 0.05f, 1.5f), Mathf.Clamp(hitboxScale.y, 0.05f, 1.5f));
        homingTurnDegreesPerSecond = Mathf.Max(0f, homingTurnDegreesPerSecond);
        homingSearchRadius = Mathf.Max(0.5f, homingSearchRadius);
        homingRetargetInterval = Mathf.Max(0.02f, homingRetargetInterval);
        orbitRehitSeconds = Mathf.Max(0.05f, orbitRehitSeconds);
    }
#endif
}
