using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public enum ProjectileShotType
{
    Small,
    Medium,
    Big
}

/// <summary>
/// Configurable projectile for Kit (and later characters).
/// Set Shot Type in the Inspector on each prefab: Small, Medium, or Big.
/// Small / Medium play OnHit (Fading + OnHit params) before despawning.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    private static readonly int FadingHash = Animator.StringToHash("Fading");
    private static readonly int OnHitHash = Animator.StringToHash("OnHit");

    [Header("Shot Type")]
    [Tooltip("Choose Small, Medium, or Big on each projectile prefab.")]
    [SerializeField] private ProjectileShotType shotType = ProjectileShotType.Small;

    [Header("Movement")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private bool rotateToDirection = true;

    [Header("Combat")]
    [SerializeField] private int damage = 1;
    [SerializeField] private bool destroyOnHit = true;
    [SerializeField] private LayerMask hitLayers = ~0;
    [Tooltip("Solid Ground / platforms shots stop on (defaults to Ground layer at runtime).")]
    [SerializeField] private LayerMask environmentLayers;

    [Header("On Hit Animation (Small / Medium)")]
    [Tooltip("If empty, uses Animator on this object (Small Shot / Medium Shot controllers).")]
    [SerializeField] private Animator animator;
    [Tooltip("Fallback destroy delay if the OnHit / MOnHit clip length cannot be read.")]
    [SerializeField] private float hitAnimFallbackDuration = 0.1f;

    [Header("Optional Defaults By Type")]
    [Tooltip("If enabled, changing Shot Type in the Inspector fills in suggested speed/damage.")]
    [SerializeField] private bool applySuggestedStatsWhenTypeChanges = true;

    public ProjectileShotType ShotType => shotType;
    public int Damage => damage;
    public float Speed => speed;
    public Transform Owner => owner;
    public bool IsLaunched => launched;
    public bool IsResolvingHit => resolvingHit;
    public bool HasResolvedCombatHit => hasResolvedCombatHit;

    /// <summary>
    /// Instantly remove every live projectile. Used on player death/respawn so leftover shots cannot hit after revival.
    /// </summary>
    public static void DestroyAllLive()
    {
        Projectile[] shots = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < shots.Length; i++)
        {
            if (shots[i] != null)
                Destroy(shots[i].gameObject);
        }
    }

    private Vector2 direction = Vector2.right;
    private Transform owner;
    private bool launched;
    private bool resolvingHit;
    private bool countChargedHit;
    private bool spawnBehindOwnerUntilClear;
    private bool promotedInFrontOfOwner;
    private SpriteRenderer spriteRenderer;
    private SortingGroup ownerSortingGroup;
    private SpriteRenderer ownerBodyRenderer;
    private Collider2D ownerBodyCollider;
    private Transform ownerFirePoint;
    private float ownerFacingSign = 1f;
    private ProjectileShotType lastValidatedType;
    private Coroutine lifetimeRoutine;
    private float environmentGraceTimer;
    private Coroutine hitAnimRoutine;
    private Coroutine shrinkRoutine;
    private Collider2D hitCollider;
    private Rigidbody2D rb;
    private float castRadius = 0.08f;
    private bool hasResolvedCombatHit;
    private float maxTravelDistance;
    private float traveledDistance;
    private bool pierceCommonKills;

    [Header("Lifetime End")]
    [Tooltip("When lifetime / max travel ends, scale down over this many seconds instead of popping.")]
    [SerializeField] private float lifetimeShrinkDuration = 0.22f;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        hitCollider = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (environmentLayers.value == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            environmentLayers = groundLayer >= 0 ? (1 << groundLayer) : hitLayers;
        }

        if (hitCollider != null)
        {
            // Keep environment probes small. Using full sprite extents (e.g. Big Red Volt)
            // makes CircleCast overlap the floor on spawn and instantly despawn the shot.
            float fitted = Mathf.Min(hitCollider.bounds.extents.x, hitCollider.bounds.extents.y);
            castRadius = Mathf.Clamp(fitted, 0.04f, 0.2f);
            // Detection radii are vision-only — never let a new shot register against them.
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>
    /// Launch using this prefab's Inspector speed/damage.
    /// </summary>
    public void Launch(Vector2 dir, Transform shotOwner)
    {
        Launch(dir, speed, damage, shotOwner);
    }

    /// <summary>
    /// Launch with optional runtime overrides (Kit can pass its own values if desired).
    /// </summary>
    public void Launch(Vector2 dir, float moveSpeed, int dmg, Transform shotOwner)
    {
        Launch(dir, moveSpeed, dmg, lifetime, shotOwner);
    }

    /// <summary>
    /// Launch with an explicit lifetime (used for fixed travel-distance shots like Tanker volts).
    /// </summary>
    public void Launch(Vector2 dir, float moveSpeed, int dmg, float lifeSeconds, Transform shotOwner)
    {
        Launch(dir, moveSpeed, dmg, lifeSeconds, shotOwner, environmentSpawnGraceSeconds: 0f);
    }

    /// <summary>
    /// Launch with lifetime and optional environment grace (set atomically so frame-0 ground overlap cannot kill the shot).
    /// </summary>
    public void Launch(
        Vector2 dir,
        float moveSpeed,
        int dmg,
        float lifeSeconds,
        Transform shotOwner,
        float environmentSpawnGraceSeconds)
    {
        direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        speed = moveSpeed;
        damage = dmg;
        lifetime = Mathf.Max(0.05f, lifeSeconds);
        owner = shotOwner;
        launched = true;
        resolvingHit = false;
        hasResolvedCombatHit = false;
        countChargedHit = false;
        environmentGraceTimer = Mathf.Max(0f, environmentSpawnGraceSeconds);
        traveledDistance = 0f;
        ApplyWeaponGear();
        // Expire by distance so Tanker volts (and others) shrink after real travel, not a clock mismatch.
        maxTravelDistance = Mathf.Max(0.05f, speed) * lifetime;

        if (rb != null)
            rb.position = transform.position;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        if (lifetimeRoutine != null)
            StopCoroutine(lifetimeRoutine);
        lifetimeRoutine = StartCoroutine(CoLifetime());
    }

    /// <summary>Blacksmith Weapon Gear on Kit / Count shots: more damage, faster and farther, kill shots fly on at max.</summary>
    private void ApplyWeaponGear()
    {
        pierceCommonKills = false;
        if (owner == null)
            return;

        PlayerController shooter = owner.GetComponentInParent<PlayerController>();
        if (shooter == null || !shooter.WeaponGearBoostsShots)
            return;

        damage += PlayerGear.WeaponDamageBonus(shooter);
        speed *= PlayerGear.WeaponShotSpeedMultiplier(shooter);
        pierceCommonKills = PlayerGear.WeaponFinisher(shooter);
    }

    /// <summary>
    /// Skip wall/ground despawn briefly so large shots can clear their muzzle without dying in place.
    /// </summary>
    public void BeginEnvironmentSpawnGrace(float seconds)
    {
        environmentGraceTimer = Mathf.Max(environmentGraceTimer, Mathf.Max(0f, seconds));
    }

    /// <summary>Steer a launched shot (Boss Count's boomerang hands). Speed and travel limit are unchanged.</summary>
    public void SetDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f)
            return;

        direction = dir.normalized;
        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    public Vector2 Direction => direction;

    /// <summary>Force shot type (e.g. Medium so Tanker volts stop on walls).</summary>
    public void SetShotType(ProjectileShotType type)
    {
        shotType = type;
    }

    /// <summary>
    /// Count: shot fired while fully charged (Long Hand or full-charge window).
    /// </summary>
    public void SetCountChargedHit(bool charged)
    {
        countChargedHit = charged;
    }

    /// <summary>
    /// Count: draw behind the body until the shot no longer overlaps the fire point / owner.
    /// </summary>
    public void BeginSpawnBehindOwnerUntilClear(
        SpriteRenderer bodyRenderer,
        SortingGroup sortingGroup,
        Collider2D bodyCollider,
        Transform firePointTransform,
        float facingSign)
    {
        spawnBehindOwnerUntilClear = true;
        promotedInFrontOfOwner = false;
        ownerBodyRenderer = bodyRenderer;
        ownerSortingGroup = sortingGroup;
        ownerBodyCollider = bodyCollider;
        ownerFirePoint = firePointTransform;
        ownerFacingSign = facingSign >= 0f ? 1f : -1f;
        ApplyBehindOwnerSorting();
    }

    private void LateUpdate()
    {
        if (!spawnBehindOwnerUntilClear || promotedInFrontOfOwner)
            return;

        if (!OverlapsOwnerSpawnZone())
        {
            promotedInFrontOfOwner = true;
            ApplyInFrontOfOwnerSorting();
        }
        else
        {
            ApplyBehindOwnerSorting();
        }
    }

    private bool OverlapsOwnerSpawnZone()
    {
        if (spriteRenderer == null)
            return false;

        Bounds shotBounds = spriteRenderer.bounds;

        if (ownerFirePoint != null)
        {
            Bounds fp = new Bounds(ownerFirePoint.position, Vector3.one * 0.35f);
            if (fp.Intersects(shotBounds))
                return true;
        }

        if (ownerBodyCollider != null && ownerBodyCollider.enabled)
            return ownerBodyCollider.bounds.Intersects(shotBounds);

        return false;
    }

    private void ApplyBehindOwnerSorting()
    {
        if (spriteRenderer == null)
            return;

        CharacterEffectSorting.ApplyTrailBehindBody(spriteRenderer, ownerBodyRenderer, ownerSortingGroup, 0);
    }

    private void ApplyInFrontOfOwnerSorting()
    {
        if (spriteRenderer == null)
            return;

        if (ownerSortingGroup != null)
        {
            spriteRenderer.sortingLayerID = ownerSortingGroup.sortingLayerID;
            spriteRenderer.sortingOrder = ownerSortingGroup.sortingOrder + 5;
            return;
        }

        if (ownerBodyRenderer != null)
        {
            spriteRenderer.sortingLayerID = ownerBodyRenderer.sortingLayerID;
            spriteRenderer.sortingOrder = ownerBodyRenderer.sortingOrder + 1;
        }
    }

    private float GetSimulationFixedDelta()
    {
        bool playerOwned = owner != null && owner.GetComponentInParent<PlayerController>() != null;

        if (playerOwned)
        {
            // Up-hyper: Count is slowed with the world → his shots travel slower too.
            // Down-hyper: Count stays full-speed, so his shots stay full-speed.
            if (HyperSpeedWorldSlow.IsActive && HyperSpeedWorldSlow.SlowsPlayer)
                return HyperSpeedWorldSlow.WorldFixedDeltaTime;

            return Time.fixedDeltaTime;
        }

        if (HyperSpeedWorldSlow.IsActive)
            return HyperSpeedWorldSlow.WorldFixedDeltaTime;

        return Time.fixedDeltaTime;
    }

    private void FixedUpdate()
    {
        if (!launched || resolvingHit || rb == null)
            return;

        float dt = GetSimulationFixedDelta();
        if (environmentGraceTimer > 0f)
            environmentGraceTimer = Mathf.Max(0f, environmentGraceTimer - dt);

        float stepDistance = speed * dt;
        if (stepDistance <= 0.0001f)
            return;

        Vector2 start = rb.position;

        if (environmentGraceTimer <= 0f)
        {
            RaycastHit2D hit = Physics2D.CircleCast(
                start,
                castRadius,
                direction,
                stepDistance,
                environmentLayers);

            if (hit.collider != null && ShouldStopOnEnvironment(hit.collider))
            {
                rb.MovePosition(hit.point - direction * 0.02f);
                if (destroyOnHit)
                    Despawn(playHitAnimation: true);
                return;
            }
        }

        float moveDist = stepDistance;
        if (maxTravelDistance > 0.01f)
        {
            float remaining = maxTravelDistance - traveledDistance;
            if (remaining <= 0f)
            {
                Despawn(playHitAnimation: false, shrinkOut: true);
                return;
            }

            if (moveDist > remaining)
                moveDist = remaining;
        }

        rb.MovePosition(start + direction * moveDist);
        traveledDistance += moveDist;

        if (maxTravelDistance > 0.01f && traveledDistance >= maxTravelDistance - 0.0001f)
            Despawn(playHitAnimation: false, shrinkOut: true);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleTrigger(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryHandleTrigger(other);
    }

    private void TryHandleTrigger(Collider2D other)
    {
        if (!launched || resolvingHit || hasResolvedCombatHit)
            return;

        if (IsOwnerCollider(other))
            return;

        // Same-owner spread / machine-gun pellets must not cancel each other.
        Projectile otherShot = other.GetComponentInParent<Projectile>();
        if (otherShot != null && otherShot != this)
        {
            TryResolveProjectileClash(otherShot);
            return;
        }

        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;

        if (ShouldStopOnEnvironment(other))
        {
            if (destroyOnHit)
                Despawn(playHitAnimation: true);
            return;
        }

        if (other.GetComponent<EnemyDetectionZone>() != null ||
            other.GetComponentInParent<EnemyDetectionZone>() != null)
            return;

        // Melee / grapple AttackBox colliders are tools — never deal body damage through them.
        // Only playable Malice can dissipate (destroy) projectiles with an active slash/grapple box.
        AttackHitbox tool = other.GetComponent<AttackHitbox>();
        if (tool == null)
            tool = other.GetComponentInParent<AttackHitbox>();

        if (tool != null)
        {
            PlayerController toolPlayer = tool.Owner;
            if (toolPlayer == null)
                toolPlayer = tool.GetComponentInParent<PlayerController>();

            if (toolPlayer != null)
            {
                if (tool.IsActive)
                {
                    toolPlayer.TryHandleProjectileContact(this, other);
                    return;
                }

                return;
            }

            // Boss AttackBox tool — ignore for body damage; Boss Malice slash can deflect / dissipate.
            Boss toolBoss = tool.OwnerBoss;
            if (toolBoss == null)
                toolBoss = tool.GetComponentInParent<Boss>();
            if (toolBoss != null && tool.gameObject != toolBoss.gameObject)
            {
                if (tool.IsActive &&
                    toolBoss is BossMalice bossMalice &&
                    bossMalice.CanDeflectProjectilesWithSlash())
                {
                    if (shotType == ProjectileShotType.Small)
                    {
                        ReflectTowardShooter(bossMalice.transform);
                        return;
                    }

                    if (shotType == ProjectileShotType.Medium)
                    {
                        Despawn(playHitAnimation: true);
                        return;
                    }

                    // Big shots are too strong — pass through the slash and keep flying.
                }

                return;
            }

            // Common enemy AttackBox tool — ignore for body damage.
            if (tool.OwnerCommonEnemy is Component ownerEnemy &&
                tool.gameObject != ownerEnemy.gameObject)
                return;
        }

        if (!TryResolveDamageTarget(other, out IDamageable damageable, out CrankyClanky cranky))
            return;

        if (damageable.IsDead)
            return;

        // Count's Time Clones fight on the player's side.
        if (damageable is CountTimeClone && IsPlayerOwned(owner))
            return;

        // Boss-owned shots pass through the crystal (no damage, no despawn).
        if (damageable is Crystal &&
            owner != null &&
            owner.GetComponentInParent<Boss>() != null)
        {
            return;
        }

        Boss ownerBoss = owner != null ? owner.GetComponentInParent<Boss>() : null;
        if (ownerBoss != null && ownerBoss.IsCopyBot && damageable is Boss hitBoss && hitBoss.IsCopyBot)
            return;

        hasResolvedCombatHit = true;
        launched = false;
        if (hitCollider != null)
            hitCollider.enabled = false;

        if (damageable is Boss boss)
            boss.TakeDamage(damage, shotType);
        else if (damageable is PlayerController player)
            player.TakeDamage(damage, owner != null ? owner : transform);
        else if (cranky != null)
            cranky.TakeDamage(damage, shotType);
        else if (damageable is LaserBot laserBot)
            laserBot.TakeDamage(damage, shotType);
        else if (damageable is BlockerBot blockerBot)
            blockerBot.TakeDamage(damage, shotType);
        else if (damageable is ChaoticTanker tanker)
            tanker.TakeDamage(damage, shotType);
        else
            damageable.TakeDamage(damage);

        TrySpawnCountHealthClock(other);

        SoundManager.Instance?.PlayProjectileHit(shotType);

        if (pierceCommonKills && damageable is ICommonEnemy && damageable.IsDead)
        {
            hasResolvedCombatHit = false;
            launched = true;
            if (hitCollider != null)
                hitCollider.enabled = true;
            return;
        }

        if (destroyOnHit)
            Despawn(playHitAnimation: true);
    }

    /// <summary>
    /// Resolve the damage target from the collider that was hit — never a nearby sibling enemy.
    /// </summary>
    private bool TryResolveDamageTarget(
        Collider2D other,
        out IDamageable damageable,
        out CrankyClanky cranky)
    {
        damageable = null;
        cranky = null;

        if (other == null)
            return false;

        cranky = other.GetComponent<CrankyClanky>();
        if (cranky == null)
            cranky = other.GetComponentInParent<CrankyClanky>();

        if (cranky != null)
        {
            damageable = cranky;
            return true;
        }

        LaserBot laserBot = other.GetComponent<LaserBot>();
        if (laserBot == null)
            laserBot = other.GetComponentInParent<LaserBot>();
        if (laserBot != null)
        {
            damageable = laserBot;
            return true;
        }

        BlockerBot blockerBot = other.GetComponent<BlockerBot>();
        if (blockerBot == null)
            blockerBot = other.GetComponentInParent<BlockerBot>();
        if (blockerBot != null)
        {
            damageable = blockerBot;
            return true;
        }

        ChaoticTanker tanker = other.GetComponent<ChaoticTanker>();
        if (tanker == null)
            tanker = other.GetComponentInParent<ChaoticTanker>();
        if (tanker != null)
        {
            damageable = tanker;
            return true;
        }

        Boss boss = other.GetComponent<Boss>();
        if (boss == null)
            boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            damageable = boss;
            return true;
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            damageable = player;
            return true;
        }

        Crystal crystal = other.GetComponent<Crystal>();
        if (crystal == null)
            crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            damageable = crystal;
            return true;
        }

        EnemyCrystal enemyCrystal = other.GetComponent<EnemyCrystal>();
        if (enemyCrystal == null)
            enemyCrystal = other.GetComponentInParent<EnemyCrystal>();
        if (enemyCrystal != null)
        {
            damageable = enemyCrystal;
            return true;
        }

        damageable = other.GetComponent<IDamageable>();
        if (damageable == null)
            damageable = other.GetComponentInParent<IDamageable>();

        return damageable != null;
    }

    private void TrySpawnCountHealthClock(Collider2D other)
    {
        if (owner == null || other == null)
            return;

        CountPlayerController count = owner.GetComponent<CountPlayerController>();
        if (count == null)
            return;

        if (!countChargedHit && !count.IsAtFullChargeForHealthDrop())
            return;

        Vector3 spawnCenter = other.bounds.center;
        count.TrySpawnHealthClockFromEnemyHit(spawnCenter);
    }

    private bool ShouldStopOnEnvironment(Collider2D other)
    {
        if (shotType == ProjectileShotType.Big)
            return false;

        // Large muzzle-spawned shots (Tanker volts) often overlap ground on frame 0.
        if (environmentGraceTimer > 0f)
            return false;

        if (other == null || other.isTrigger)
            return false;

        // Never treat the shooter's own body as a wall (e.g. Tanker volts clearing the chassis).
        if (IsOwnerCollider(other))
            return false;

        if (other.GetComponentInParent<Projectile>() != null)
            return false;

        if (other.GetComponent<EnemyDetectionZone>() != null ||
            other.GetComponentInParent<EnemyDetectionZone>() != null)
            return false;

        if (other.GetComponent<AttackHitbox>() != null ||
            other.GetComponentInParent<AttackHitbox>() != null)
            return false;

        // Walkable enemies (e.g. Blocker Bot on Ground) must still take damage from all sides.
        if (other.GetComponentInParent<ICommonEnemy>() != null)
            return false;
        if (other.GetComponentInParent<Boss>() != null)
            return false;

        if (((1 << other.gameObject.layer) & environmentLayers.value) != 0)
            return true;

        return IsNamedPlatform(other.gameObject);
    }

    /// <summary>
    /// True when <paramref name="other"/> belongs to this shot's owner hierarchy
    /// (child, self, or parent body when the owner script sits on a child).
    /// </summary>
    private bool IsOwnerCollider(Collider2D other)
    {
        if (owner == null || other == null)
            return false;

        Transform otherT = other.transform;
        if (otherT == owner || otherT.IsChildOf(owner) || owner.IsChildOf(otherT))
            return true;

        // Same common enemy / boss / player even if collider sits on a sibling under a shared root.
        ICommonEnemy ownerEnemy = owner.GetComponentInParent<ICommonEnemy>();
        if (ownerEnemy != null)
        {
            ICommonEnemy hitEnemy = other.GetComponentInParent<ICommonEnemy>();
            if (hitEnemy != null && ReferenceEquals(ownerEnemy, hitEnemy))
                return true;
        }

        Boss ownerBoss = owner.GetComponentInParent<Boss>();
        if (ownerBoss != null)
        {
            Boss hitBoss = other.GetComponentInParent<Boss>();
            if (hitBoss != null && hitBoss == ownerBoss)
                return true;
        }

        PlayerController ownerPlayer = owner.GetComponentInParent<PlayerController>();
        if (ownerPlayer != null)
        {
            PlayerController hitPlayer = other.GetComponentInParent<PlayerController>();
            if (hitPlayer != null && hitPlayer == ownerPlayer)
                return true;
        }

        return false;
    }

    private static bool IsNamedPlatform(GameObject go)
    {
        if (go == null)
            return false;

        string name = go.name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Contains("Factory Platform")
            || name.Contains("Factroy")
            || name.IndexOf("Thick platform", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator CoLifetime()
    {
        // Failsafe if distance tracking never fires (e.g. zero speed). Prefer max-travel shrink in FixedUpdate.
        float failsafe = Mathf.Max(0.05f, lifetime) * 2f;
        float elapsed = 0f;
        while (elapsed < failsafe)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        lifetimeRoutine = null;
        Despawn(playHitAnimation: false, shrinkOut: true);
    }

    /// <summary>
    /// Stop the shot. Hit clashes play OnHit; max-range expiry shrinks out.
    /// </summary>
    private void Despawn(bool playHitAnimation, bool shrinkOut = false)
    {
        if (resolvingHit)
            return;

        resolvingHit = true;
        launched = false;

        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
            hitCollider.enabled = false;

        if (lifetimeRoutine != null)
        {
            StopCoroutine(lifetimeRoutine);
            lifetimeRoutine = null;
        }

        if (shrinkOut)
        {
            BeginShrinkAndDestroy();
            return;
        }

        if (playHitAnimation && CanPlayHitAnimation())
        {
            BeginHitAnimationAndDestroy();
            return;
        }

        Destroy(gameObject);
    }

    private void BeginShrinkAndDestroy()
    {
        if (shrinkRoutine != null)
            StopCoroutine(shrinkRoutine);
        shrinkRoutine = StartCoroutine(ShrinkAndDestroyRoutine());
    }

    private IEnumerator ShrinkAndDestroyRoutine()
    {
        Vector3 startScale = transform.localScale;
        float duration = Mathf.Max(0.05f, lifetimeShrinkDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float s = 1f - u * u;
            transform.localScale = startScale * s;
            yield return null;
        }

        shrinkRoutine = null;
        Destroy(gameObject);
    }

    private bool CanPlayHitAnimation()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return false;

        if (shotType == ProjectileShotType.Small || shotType == ProjectileShotType.Medium)
            return true;

        return shotType == ProjectileShotType.Big && TryGetHitAnimationClip(out _);
    }

    private void BeginHitAnimationAndDestroy()
    {
        resolvingHit = true;
        launched = false;

        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
            hitCollider.enabled = false;

        // Controllers transition Any → OnHit when BOTH Fading (bool) and OnHit (trigger) are set.
        animator.SetBool(FadingHash, true);
        animator.SetTrigger(OnHitHash);

        if (hitAnimRoutine != null)
            StopCoroutine(hitAnimRoutine);
        hitAnimRoutine = StartCoroutine(DestroyAfterHitAnimation());
    }

    private IEnumerator DestroyAfterHitAnimation()
    {
        float duration = ResolveHitAnimDuration();
        yield return new WaitForSeconds(duration);
        hitAnimRoutine = null;
        Destroy(gameObject);
    }

    private float ResolveHitAnimDuration()
    {
        float fallback = Mathf.Max(0.05f, hitAnimFallbackDuration);
        if (animator == null || animator.runtimeAnimatorController == null)
            return fallback;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return fallback;

        if (TryGetHitAnimationClip(out AnimationClip hitClip))
            return Mathf.Max(0.05f, hitClip.length);

        return fallback;
    }

    private bool TryGetHitAnimationClip(out AnimationClip hitClip)
    {
        hitClip = null;
        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null || !IsHitAnimationClipName(clip.name))
                continue;

            hitClip = clip;
            return true;
        }

        return false;
    }

    private static bool IsHitAnimationClipName(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
            return false;

        return clipName == "OnHit"
            || clipName == "MOnHit"
            || clipName.IndexOf("On Hit", System.StringComparison.OrdinalIgnoreCase) >= 0
            || clipName.IndexOf("OnHit", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Clash with a melee box — play OnHit animation, no damage to the defender.</summary>
    public void ClashAndDespawn()
    {
        Despawn(playHitAnimation: true);
    }

    private bool TryResolveProjectileClash(Projectile other)
    {
        if (other == null || other == this)
            return false;

        if (!launched || resolvingHit || hasResolvedCombatHit)
            return false;

        if (!other.IsLaunched || other.IsResolvingHit || other.HasResolvedCombatHit)
            return false;

        if (IsSameProjectileSide(other))
            return false;

        if (!AreHostileProjectiles(other))
            return false;

        if (GetInstanceID() > other.GetInstanceID())
            return false;

        ResolveProjectileClash(other);
        return true;
    }

    private bool IsSameProjectileSide(Projectile other)
    {
        if (owner == null || other.owner == null)
            return false;

        if (owner == other.owner)
            return true;

        bool minePlayer = IsPlayerOwned(owner);
        bool otherPlayer = IsPlayerOwned(other.owner);
        if (minePlayer && otherPlayer)
            return true;

        bool mineBoss = IsBossOwned(owner);
        bool otherBoss = IsBossOwned(other.owner);
        if (mineBoss && otherBoss)
            return true;

        bool mineEnemy = IsCommonEnemyOwned(owner);
        bool otherEnemy = IsCommonEnemyOwned(other.owner);
        return mineEnemy && otherEnemy;
    }

    private static bool AreHostileProjectiles(Projectile a, Projectile b)
    {
        if (a.owner == null || b.owner == null)
            return false;

        bool aPlayer = IsPlayerOwned(a.owner);
        bool bPlayer = IsPlayerOwned(b.owner);
        return aPlayer != bPlayer;
    }

    private bool AreHostileProjectiles(Projectile other) => AreHostileProjectiles(this, other);

    private static bool IsPlayerOwned(Transform shotOwner)
    {
        return shotOwner != null && shotOwner.GetComponentInParent<PlayerController>() != null;
    }

    private static bool IsBossOwned(Transform shotOwner)
    {
        return shotOwner != null && shotOwner.GetComponentInParent<Boss>() != null;
    }

    private static bool IsCommonEnemyOwned(Transform shotOwner)
    {
        if (shotOwner == null)
            return false;

        return shotOwner.GetComponentInParent<ICommonEnemy>() != null
               && shotOwner.GetComponentInParent<Boss>() == null
               && shotOwner.GetComponentInParent<PlayerController>() == null;
    }

    private void ResolveProjectileClash(Projectile other)
    {
        // Player shots vs common-enemy shots (Tanker volts, etc.): both take the hit.
        // Medium volts previously beat Small player shots and kept flying.
        bool thisPlayer = IsPlayerOwned(owner);
        bool otherPlayer = IsPlayerOwned(other.owner);
        bool thisEnemy = IsCommonEnemyOwned(owner);
        bool otherEnemy = IsCommonEnemyOwned(other.owner);
        if ((thisPlayer && otherEnemy) || (thisEnemy && otherPlayer))
        {
            ClashAndDespawn();
            other.ClashAndDespawn();
            return;
        }

        int priority = CompareShotPriority(shotType, other.shotType);
        if (priority > 0)
        {
            other.ClashAndDespawn();
            return;
        }

        if (priority < 0)
        {
            ClashAndDespawn();
            return;
        }

        ClashAndDespawn();
        other.ClashAndDespawn();
    }

    private static int CompareShotPriority(ProjectileShotType a, ProjectileShotType b)
    {
        return GetShotPriorityRank(a).CompareTo(GetShotPriorityRank(b));
    }

    private static int GetShotPriorityRank(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Big:
                return 3;
            case ProjectileShotType.Medium:
                return 2;
            default:
                return 1;
        }
    }

    /// <summary>
    /// Send this shot back at its original owner (or Active player), now owned by the deflector.
    /// </summary>
    public void ReflectFromDeflector(Transform newOwner)
    {
        ReflectTowardShooter(newOwner);
    }

    /// <summary>
    /// Send this shot back at its original owner (or Active player), now owned by the deflector.
    /// </summary>
    private void ReflectTowardShooter(Transform newOwner)
    {
        Vector2 newDir = -direction;

        if (owner != null)
        {
            Vector2 toOwner = (Vector2)owner.position - (Vector2)transform.position;
            if (toOwner.sqrMagnitude > 0.0001f)
                newDir = toOwner.normalized;
        }
        else if (PlayerController.Active != null)
        {
            Vector2 toPlayer = (Vector2)PlayerController.Active.transform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f)
                newDir = toPlayer.normalized;
        }

        owner = newOwner;
        pierceCommonKills = false;
        direction = newDir.sqrMagnitude > 0.0001f ? newDir.normalized : Vector2.left;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void ApplySuggestedStatsForType(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Small:
                speed = 12f;
                damage = 1;
                lifetime = 3f;
                break;
            case ProjectileShotType.Medium:
                speed = 11f;
                damage = 2;
                lifetime = 3.25f;
                break;
            case ProjectileShotType.Big:
                speed = 10f;
                damage = 4;
                lifetime = 3.5f;
                break;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        lifetime = Mathf.Max(0.05f, lifetime);
        speed = Mathf.Max(0.1f, speed);
        damage = Mathf.Max(0, damage);
        hitAnimFallbackDuration = Mathf.Max(0.05f, hitAnimFallbackDuration);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;

        if (animator == null)
            animator = GetComponent<Animator>();

        if (applySuggestedStatsWhenTypeChanges && shotType != lastValidatedType)
        {
            ApplySuggestedStatsForType(shotType);
            lastValidatedType = shotType;
        }
    }

    private void Reset()
    {
        ApplySuggestedStatsForType(shotType);
        lastValidatedType = shotType;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;

        animator = GetComponent<Animator>();
    }
#endif
}
