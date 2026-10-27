using System.Collections;
using UnityEngine;

/// <summary>
/// Bubble Air bubble — MM2 Bubble Lead style: arcs forward under gravity and bounces on
/// floors/walls. Environment bounces redirect travel; pops after maxBouncesBeforePop (default 8).
/// Enemy contact pops immediately (2 damage). On pop: releases Air Tornado + 8 remnant bits.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class BassBubbleBullet : MonoBehaviour
{
    [SerializeField] private float gravity = 12f;
    [SerializeField] private float launchSpeed = 11f;
    [SerializeField] private float upwardLaunchBoost = 1.6f;
    [SerializeField] private float bounceRestitution = 0.88f;
    [SerializeField] private float minBounceSpeed = 3.5f;
    [SerializeField] private float groundFriction = 0.04f;
    [SerializeField] private int maxBouncesBeforePop = 8;
    [SerializeField] private float damage = 2f;
    [SerializeField] private float lifetime = 14f;
    [SerializeField] private float castRadius = 0.42f;
    [SerializeField] private LayerMask environmentLayers;
    [SerializeField] private float spawnGraceSeconds = 0.18f;
    [SerializeField] private float bounceCooldown = 0.06f;
    [SerializeField] private BassAirTornado tornadoPrefab;
    [SerializeField] private Transform tornadoContainer;
    [SerializeField] private string onHitStateName = "Bubble Bullet On hit";
    [SerializeField] private BassBubbleBit bubbleBitPrefab;
    [SerializeField] private int popBitCount = 8;
    [SerializeField] private float popBitSpeed = 5.5f;

    private Rigidbody2D rb;
    private CircleCollider2D circleCollider;
    private Collider2D hitCollider;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private BassAirTornado containedTornado;

    private Transform owner;
    private BassPlayerController bassOwner;
    private float bossAbilityScale = 1f;
    private int bossSpawnMultiplier = 1;
    private bool hostileToPlayer;
    private float facingSign = 1f;
    private float spaceSize = 1f;
    private Vector2 velocity;
    private int bounceCount;
    private bool launched;
    private bool popping;
    private float lifeRemaining;
    private float graceRemaining;
    private float bounceLockUntil;
    private bool spawnUnstickPending;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[12];
    private readonly Collider2D[] overlapHits = new Collider2D[16];
    private Vector3 baseLocalScale = Vector3.one;
    private bool capturedBaseScale;

    public bool IsAlive => launched && !popping;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hitCollider = GetComponent<Collider2D>();
        circleCollider = hitCollider as CircleCollider2D;
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        CaptureBaseScale();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }

        RefreshCastRadiusFromCollider();

        if (tornadoContainer == null)
        {
            Transform found = transform.Find("Air Tornado Container");
            tornadoContainer = found != null ? found : transform;
        }
    }

    /// <summary>
    /// Double Gear + ability: +30% size while DG is on; pop bits (and extra tornado) ×2 while DG is on.
    /// </summary>
    public void ConfigureDoubleGearBoosts(BassPlayerController bass)
    {
        bassOwner = bass;
        bossAbilityScale = 1f;
        bossSpawnMultiplier = 1;
        CaptureBaseScale();
        ApplyDoubleGearScale();
        RefreshCastRadiusFromCollider();
    }

    /// <summary>Boss Bass Double Gear + Dual: +30% size and ×2 pop / tornado spawns.</summary>
    public void ConfigureBossAbilityBoosts(float scale, int spawnMultiplier)
    {
        bassOwner = null;
        bossAbilityScale = Mathf.Max(0.1f, scale);
        bossSpawnMultiplier = Mathf.Max(1, spawnMultiplier);
        CaptureBaseScale();
        ApplyDoubleGearScale();
        RefreshCastRadiusFromCollider();
    }

    private void CaptureBaseScale()
    {
        if (capturedBaseScale)
            return;
        baseLocalScale = transform.localScale;
        if (baseLocalScale.sqrMagnitude < 0.0001f)
            baseLocalScale = Vector3.one;
        capturedBaseScale = true;
    }

    private void ApplyDoubleGearScale()
    {
        float s = bassOwner != null ? bassOwner.GetAbilityProjectileScale() : bossAbilityScale;
        Vector3 next = baseLocalScale * s;
        if ((transform.localScale - next).sqrMagnitude > 0.0001f)
        {
            transform.localScale = next;
            RefreshCastRadiusFromCollider();
        }
    }

    private int GetSpawnMultiplier() =>
        bassOwner != null
            ? Mathf.Max(1, bassOwner.GetAbilitySpawnMultiplier())
            : Mathf.Max(1, bossSpawnMultiplier);

    private void RefreshCastRadiusFromCollider()
    {
        float scale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            0.01f);

        float fromCollider = 0f;
        if (circleCollider != null)
            fromCollider = circleCollider.radius * scale;
        else if (hitCollider != null)
            fromCollider = Mathf.Min(hitCollider.bounds.extents.x, hitCollider.bounds.extents.y);

        float fromSprite = 0f;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Bounds b = spriteRenderer.sprite.bounds;
            fromSprite = Mathf.Max(b.extents.x, b.extents.y) * scale * 0.92f;
        }

        // Prefer the larger of authored collider vs sprite so floors can't be clipped through.
        castRadius = Mathf.Max(0.2f, fromCollider, fromSprite);
        if (circleCollider != null && fromSprite > fromCollider + 0.05f)
            circleCollider.radius = fromSprite / scale;
    }

    private void ResolveEnvironmentLayers()
    {
        if (environmentLayers.value != 0)
            return;

        PlayerController bass = owner != null ? owner.GetComponentInParent<PlayerController>() : null;
        if (bass != null && bass.GroundLayers.value != 0)
        {
            environmentLayers = bass.GroundLayers;
            return;
        }

        int ground = LayerMask.NameToLayer("Ground");
        int def = LayerMask.NameToLayer("Default");
        int mask = 0;
        if (ground >= 0)
            mask |= 1 << ground;
        if (def >= 0)
            mask |= 1 << def;
        environmentLayers = mask != 0 ? mask : ~0;
    }

    public void Launch(
        Vector2 aim,
        float moveSpeed,
        float dmg,
        float worldSpaceSize,
        BassAirTornado tornadoTemplate,
        Transform shotOwner)
    {
        owner = shotOwner;
        spaceSize = Mathf.Max(0.01f, worldSpaceSize);
        damage = Mathf.Max(0f, dmg);
        facingSign = shotOwner != null
            ? Mathf.Sign(shotOwner.localScale.x != 0f ? shotOwner.localScale.x : 1f)
            : Mathf.Sign(aim.x != 0f ? aim.x : 1f);

        PlayerController bass = shotOwner != null ? shotOwner.GetComponentInParent<PlayerController>() : null;
        if (bass != null)
            facingSign = bass.FacingSign;

        // Boss Bass fires the same bubble: pops on the player instead of bosses / enemies.
        Boss bossOwner = shotOwner != null ? shotOwner.GetComponentInParent<Boss>() : null;
        hostileToPlayer = bossOwner != null;
        if (bossOwner != null)
            facingSign = bossOwner.FacingSign >= 0f ? 1f : -1f;

        ResolveEnvironmentLayers();
        RefreshCastRadiusFromCollider();

        Vector2 dir = aim.sqrMagnitude > 0.001f ? aim.normalized : new Vector2(facingSign, 0f);
        float speed = Mathf.Max(0.5f, moveSpeed > 0.01f ? moveSpeed : launchSpeed);
        velocity = dir * speed;
        // MM2-style: slight rise before the heavy drop.
        if (velocity.y < upwardLaunchBoost * 0.35f)
            velocity.y += upwardLaunchBoost;

        bounceCount = 0;
        launched = true;
        popping = false;
        lifeRemaining = Mathf.Max(1f, lifetime);
        graceRemaining = Mathf.Max(0f, spawnGraceSeconds);
        bounceLockUntil = 0f;
        spawnUnstickPending = true;

        if (tornadoTemplate != null)
            tornadoPrefab = tornadoTemplate;

        EnsureContainedTornado();

        // If spawned overlapping the floor/wall, lift clear before any bounce counting.
        Vector2 spawnPos = rb != null ? rb.position : (Vector2)transform.position;
        if (TryUnstickFromGround(ref spawnPos))
        {
            if (rb != null)
                rb.position = spawnPos;
            transform.position = spawnPos;
        }
    }

    private void EnsureContainedTornado()
    {
        if (containedTornado != null)
        {
            containedTornado.ContainInBubble(tornadoContainer != null ? tornadoContainer : transform, facingSign);
            return;
        }

        BassAirTornado existing = GetComponentInChildren<BassAirTornado>(true);
        if (existing != null)
        {
            containedTornado = existing;
            containedTornado.ContainInBubble(tornadoContainer != null ? tornadoContainer : transform, facingSign);
            return;
        }

        if (tornadoPrefab == null)
            return;

        containedTornado = Instantiate(tornadoPrefab, tornadoContainer != null ? tornadoContainer : transform);
        containedTornado.gameObject.SetActive(true);
        containedTornado.ContainInBubble(tornadoContainer != null ? tornadoContainer : transform, facingSign);
    }

    private void Update()
    {
        if (!launched || popping)
            return;
        ApplyDoubleGearScale();
    }

    private void FixedUpdate()
    {
        if (!launched || popping)
            return;

        float dt = Time.fixedDeltaTime;
        lifeRemaining -= dt;
        if (lifeRemaining <= 0f)
        {
            Pop(releaseTornado: true, playHitAnim: false);
            return;
        }

        if (graceRemaining > 0f)
            graceRemaining -= dt;

        velocity.y -= gravity * dt;
        Vector2 delta = velocity * dt;
        Vector2 start = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 end = start + delta;

        // Spawn unstick: push out of ground/walls with NO bounce count.
        if (spawnUnstickPending || graceRemaining > 0f)
        {
            if (TryUnstickFromGround(ref start))
            {
                if (rb != null)
                    rb.position = start;
                else
                    transform.position = start;
            }
            else if (spawnUnstickPending)
            {
                spawnUnstickPending = false;
            }
        }

        if (graceRemaining <= 0f)
        {
            // Mid-flight penetration still counts as a bounce.
            if (TryResolvePenetration(ref start, countBounce: true))
            {
                if (rb != null)
                    rb.position = start;
                else
                    transform.position = start;
            }

            end = ResolveEnvironmentMotion(start, end, delta);
        }
        else
        {
            // During grace, keep traveling but don't bounce-count environment.
            end = start + delta;
        }

        if (rb != null)
            rb.MovePosition(end);
        else
            transform.position = end;

        TryHitCombatTargets();
    }

    /// <summary>
    /// Push the bubble clear of overlapping solids (prefer up for floors).
    /// Does not increment bounceCount — used for spawn-stuck cases.
    /// </summary>
    private bool TryUnstickFromGround(ref Vector2 position)
    {
        ContactFilter2D filter = BuildEnvironmentFilter();
        int count = Physics2D.OverlapCircle(position, castRadius, filter, overlapHits);
        if (count <= 0)
            return false;

        bool moved = false;
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapHits[i];
            if (!IsEnvironmentCollider(col))
                continue;

            Vector2 closest = col.ClosestPoint(position);
            Vector2 away = position - closest;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector2.up;
            else
                away.Normalize();

            // Prefer lifting out of floors so spawn-near-ground doesn't dig sideways.
            if (away.y < 0.35f && Mathf.Abs(away.x) > Mathf.Abs(away.y))
            {
                // Wall overlap: keep horizontal push.
            }
            else if (away.y < 0.2f)
            {
                away = Vector2.up;
            }
            else
            {
                // Bias floor escapes upward.
                away = new Vector2(away.x * 0.25f, Mathf.Max(0.75f, away.y)).normalized;
            }

            float push = castRadius - Vector2.Distance(position, closest) + 0.06f;
            if (push <= 0f)
                continue;

            position += away * push;
            // Kill downward dig so we don't immediately re-embed.
            if (velocity.y < 0f)
                velocity.y = Mathf.Max(velocity.y, 1.5f);
            moved = true;
        }

        return moved;
    }

    private bool TryResolvePenetration(ref Vector2 position, bool countBounce)
    {
        ContactFilter2D filter = BuildEnvironmentFilter();
        int count = Physics2D.OverlapCircle(position, castRadius, filter, overlapHits);
        if (count <= 0)
            return false;

        bool moved = false;
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapHits[i];
            if (!IsEnvironmentCollider(col))
                continue;

            Vector2 closest = col.ClosestPoint(position);
            Vector2 away = position - closest;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector2.up;
            else
                away.Normalize();

            float push = castRadius - Vector2.Distance(position, closest) + 0.03f;
            if (push <= 0f)
                continue;

            position += away * push;
            if (countBounce && Time.time >= bounceLockUntil)
            {
                bounceLockUntil = Time.time + bounceCooldown;
                bounceCount++;
                velocity = Vector2.Reflect(velocity, away);
                if (velocity.magnitude < minBounceSpeed)
                    velocity = velocity.normalized * minBounceSpeed;
                velocity *= bounceRestitution;
                if (away.y > 0.55f)
                    velocity.x *= 1f - Mathf.Clamp01(groundFriction);

                if (bounceCount >= Mathf.Max(1, maxBouncesBeforePop))
                    Pop(releaseTornado: true, playHitAnim: true);
            }

            moved = true;
        }

        return moved;
    }

    private ContactFilter2D BuildEnvironmentFilter()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(environmentLayers);
        filter.useLayerMask = true;
        return filter;
    }

    private bool IsEnvironmentCollider(Collider2D col)
    {
        if (col == null || col.isTrigger)
            return false;
        if (col.transform == transform || col.transform.IsChildOf(transform))
            return false;
        if (owner != null && (col.transform == owner || col.transform.IsChildOf(owner)))
            return false;
        if (col.GetComponentInParent<BassBubbleBullet>() != null)
            return false;
        if (col.GetComponentInParent<BassAirTornado>() != null)
            return false;
        if (col.GetComponentInParent<PlayerController>() != null)
            return false;
        if (col.GetComponentInParent<ICommonEnemy>() != null)
            return false;
        if (col.GetComponentInParent<Boss>() != null)
            return false;
        return true;
    }

    private Vector2 ResolveEnvironmentMotion(Vector2 start, Vector2 proposed, Vector2 delta)
    {
        float dist = delta.magnitude;
        if (dist < 0.0001f)
            return proposed;

        ContactFilter2D filter = BuildEnvironmentFilter();
        int count = Physics2D.CircleCast(start, castRadius, delta.normalized, filter, castHits, dist + 0.02f);
        if (count <= 0)
            return proposed;

        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (!IsEnvironmentCollider(hit.collider))
                continue;
            if (hit.distance < bestDist)
            {
                bestDist = hit.distance;
                best = hit;
            }
        }

        if (best.collider == null)
            return proposed;

        Vector2 hitPoint = start + delta.normalized * Mathf.Max(0f, best.distance - 0.02f);
        if (Time.time < bounceLockUntil)
            return hitPoint;

        bounceLockUntil = Time.time + bounceCooldown;
        bounceCount++;

        Vector2 normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : Vector2.up;
        velocity = Vector2.Reflect(velocity, normal);
        if (velocity.magnitude < minBounceSpeed)
            velocity = velocity.normalized * minBounceSpeed;
        velocity *= bounceRestitution;

        if (normal.y > 0.55f)
            velocity.x *= 1f - Mathf.Clamp01(groundFriction);

        if (bounceCount >= Mathf.Max(1, maxBouncesBeforePop))
        {
            Pop(releaseTornado: true, playHitAnim: true);
            return hitPoint;
        }

        return hitPoint;
    }

    private void TryHitCombatTargets()
    {
        if (popping)
            return;

        ContactFilter2D combatFilter = new ContactFilter2D();
        combatFilter.useTriggers = true;
        combatFilter.NoFilter();
        int count = Physics2D.OverlapCircle(transform.position, castRadius * 1.15f, combatFilter, overlapHits);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlapHits[i];
            if (other == null || EnemyDetectionZone.IsDetectionOnlyCollider(other))
                continue;
            if (other.transform == transform || other.transform.IsChildOf(transform))
                continue;
            if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
                continue;
            if (other.GetComponentInParent<BassBubbleBullet>() != null)
                continue;
            if (other.GetComponentInParent<BassAirTornado>() != null)
                continue;
            if (other.GetComponentInParent<PlayerController>() != null && !hostileToPlayer)
                continue;

            if (TryDamageTarget(other))
            {
                Pop(releaseTornado: true, playHitAnim: true);
                return;
            }
        }
    }

    private bool TryDamageTarget(Collider2D other)
    {
        if (hostileToPlayer)
            return BassBossHostility.TryHurtPlayer(other, damage, owner);

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (boss.IsDead)
                return false;
            int applied = FractionalDamage.Consume(boss, damage);
            if (applied > 0)
                boss.TakeDamage(applied, ProjectileShotType.Small);
            return true;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy != null)
        {
            if (enemy.IsDead)
                return false;
            Component host = enemy as Component;
            int applied = FractionalDamage.Consume(host, damage);
            if (applied > 0)
                enemy.TakeDamage(applied);
            return true;
        }

        Crystal crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            int applied = FractionalDamage.Consume(crystal, damage);
            if (applied > 0)
                crystal.TakeDamage(applied);
            return true;
        }

        EnemyCrystal enemyCrystal = other.GetComponentInParent<EnemyCrystal>();
        if (enemyCrystal != null)
        {
            int applied = FractionalDamage.Consume(enemyCrystal, damage);
            if (applied > 0)
                enemyCrystal.TakeDamage(applied);
            return true;
        }

        return false;
    }

    private void Pop(bool releaseTornado, bool playHitAnim)
    {
        if (popping)
            return;

        popping = true;
        launched = false;
        velocity = Vector2.zero;

        if (hitCollider != null)
            hitCollider.enabled = false;

        SpawnPopBits();

        if (releaseTornado)
            ReleaseTornado();

        bool canPlayHit = playHitAnim
            && animator != null
            && !string.IsNullOrEmpty(onHitStateName)
            && animator.HasState(0, Animator.StringToHash(onHitStateName));

        if (canPlayHit)
        {
            // Keep the SpriteRenderer on — On Hit drives its sprite frames.
            if (spriteRenderer != null)
                spriteRenderer.enabled = true;
            if (animator != null)
                animator.enabled = true;
            animator.Play(onHitStateName, 0, 0f);
            StartCoroutine(CoDestroyAfterHitAnim());
            return;
        }

        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
        Destroy(gameObject);
    }

    private void SpawnPopBits()
    {
        if (bubbleBitPrefab == null)
            return;

        // Double Gear + ability doubles remnant bit count while DG is active at pop time.
        int count = Mathf.Max(1, popBitCount) * GetSpawnMultiplier();
        float radius = castRadius;
        if (circleCollider != null)
        {
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            radius = Mathf.Max(0.05f, circleCollider.radius * scale);
        }

        Vector3 center = transform.position;
        int sort = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 6;

        for (int i = 0; i < count; i++)
        {
            float angle = (Mathf.PI * 2f * i) / count;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector3 spawnPos = center + (Vector3)(dir * radius);

            BassBubbleBit bit = Instantiate(bubbleBitPrefab, spawnPos, Quaternion.identity);
            bit.gameObject.SetActive(true);

            Vector2 bitVel = dir * popBitSpeed;
            bitVel.y += Random.Range(0.8f, 1.8f);
            bit.Launch(bitVel, sort);
        }
    }

    private void ReleaseTornado()
    {
        EnsureContainedTornado();
        if (containedTornado == null)
            return;

        BassAirTornado tornado = containedTornado;
        containedTornado = null;
        tornado.Release(owner, facingSign, spaceSize);

        // Double Gear + ability: release an extra tornado for each additional spawn multiplier.
        int mult = GetSpawnMultiplier();
        if (mult <= 1 || tornadoPrefab == null)
            return;

        for (int i = 1; i < mult; i++)
        {
            BassAirTornado extra = Instantiate(tornadoPrefab, transform.position, Quaternion.identity);
            extra.gameObject.SetActive(true);
            extra.Release(owner, facingSign, spaceSize);
        }
    }

    private IEnumerator CoDestroyAfterHitAnim()
    {
        float wait = 0.12f;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name.IndexOf("on hit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wait = Mathf.Max(0.05f, clips[i].length);
                    break;
                }
            }
        }

        yield return new WaitForSeconds(wait);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!popping && containedTornado != null && containedTornado.transform.IsChildOf(transform))
            Destroy(containedTornado.gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        gravity = Mathf.Max(0.1f, gravity);
        launchSpeed = Mathf.Max(0.5f, launchSpeed);
        bounceRestitution = Mathf.Clamp(bounceRestitution, 0.05f, 1.2f);
        maxBouncesBeforePop = Mathf.Max(1, maxBouncesBeforePop);
        lifetime = Mathf.Max(0.5f, lifetime);
        castRadius = Mathf.Max(0.05f, castRadius);
        popBitCount = Mathf.Max(1, popBitCount);
        popBitSpeed = Mathf.Max(0.5f, popBitSpeed);
    }
#endif
}
