using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crashing Heat Crash Bomb / Crash Bomb Plus — MM2-style stick fuse + multi-wave explosions.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class BassCrashBomb : MonoBehaviour
{
    [SerializeField] private float speed = 14f;
    [SerializeField] private float stickDamage = 3f;
    [SerializeField] private float fuseSeconds = 1.35f;
    [SerializeField] private float explosionDamage = 1f;
    [SerializeField] private float explosionKnockSpaces = 3f;
    [SerializeField] private int explosionTotal = 15;
    [SerializeField] private int explosionsPerWave = 5;
    [SerializeField] private float waveInterval = 0.1f;
    [SerializeField] private float waveSpawnRadius = 1.1f;
    [SerializeField] private float spaceSize = 1f;
    [SerializeField] private string airStateName = "Crash Bomb in air";
    [SerializeField] private string tickStateName = "Crash Bomb Ticking down to explosion";
    [SerializeField] private string plusAirStateName = "Crash Bomb active";
    [SerializeField] private string plusTickStateName = "Crash Bomb Plus ticking down";
    [SerializeField] private BassCrashBombExplosion explosionPrefab;
    [SerializeField] private BassSmallHeatFlare smallFlarePrefab;
    [SerializeField] private BassHeatFlareStack stackPrefab;
    [SerializeField] private BassHeatFlareBall heatBallPrefab;
    [SerializeField] private GameVisualEffect stunnedEffectPrefab;
    [SerializeField] private int smallFlareStackAmmoCost = 1;
    [SerializeField] private int heatBallAmmoCost = 3;
    [SerializeField] private float smallFlareLandSpacingSpaces = 4f;

    private Rigidbody2D rb;
    private Collider2D hitCollider;
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    private Transform owner;
    private PlayerController ownerPlayer;
    private BassPlayerController bassOwner;
    private float bossAbilityScale = 1f;
    private int bossSpawnMultiplier = 1;
    private SpriteRenderer ownerBody;
    private SortingGroup ownerGroup;
    private Vector2 direction = Vector2.right;
    private bool isPlus;
    private float damageMultiplier = 1f;
    private bool hostileToPlayer;
    private float hostileHeatBallScale = 1f;
    private bool flying;
    private bool stuck;
    private bool exploding;
    private float fuseLeft;
    private Vector2 embedNormal = Vector2.up;
    private System.Func<int, bool> trySpendAmmo;
    private readonly Collider2D[] overlaps = new Collider2D[12];
    private readonly RaycastHit2D[] casts = new RaycastHit2D[8];
    private Vector3 baseLocalScale = Vector3.one;
    private bool capturedBaseScale;

    public bool IsAlive => (flying || stuck) && !exploding;

    /// <summary>Remove crash bombs owned by this root (boss defeat cinema).</summary>
    public static void DestroyOwnedBy(Transform root)
    {
        if (root == null)
            return;

        BassCrashBomb[] bombs = FindObjectsByType<BassCrashBomb>(FindObjectsSortMode.None);
        for (int i = 0; i < bombs.Length; i++)
        {
            BassCrashBomb bomb = bombs[i];
            if (bomb == null || bomb.owner == null)
                continue;
            if (bomb.owner == root || bomb.owner.IsChildOf(root) || root.IsChildOf(bomb.owner))
                Destroy(bomb.gameObject);
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hitCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        CaptureBaseScale();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }
    }

    /// <summary>
    /// Double Gear + ability: +30% size while DG is on; child flares/balls spawn ×2 while DG is on.
    /// </summary>
    public void ConfigureDoubleGearBoosts(BassPlayerController bass)
    {
        bassOwner = bass;
        bossAbilityScale = 1f;
        bossSpawnMultiplier = 1;
        CaptureBaseScale();
        ApplyDoubleGearScale();
    }

    /// <summary>Boss Bass Double Gear + Dual: +30% size and ×2 heat spawns without a playable Bass owner.</summary>
    public void ConfigureBossAbilityBoosts(float scale, int spawnMultiplier)
    {
        bassOwner = null;
        bossAbilityScale = Mathf.Max(0.1f, scale);
        bossSpawnMultiplier = Mathf.Max(1, spawnMultiplier);
        CaptureBaseScale();
        ApplyDoubleGearScale();
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
        transform.localScale = baseLocalScale * s;
    }

    private int GetSpawnMultiplier() =>
        bassOwner != null
            ? Mathf.Max(1, bassOwner.GetAbilitySpawnMultiplier())
            : Mathf.Max(1, bossSpawnMultiplier);

    public void Launch(
        Vector2 dir,
        float moveSpeed,
        bool plus,
        float worldSpace,
        Transform shotOwner,
        System.Func<int, bool> spendAmmo,
        BassCrashBombExplosion explosionTemplate,
        BassSmallHeatFlare flareTemplate,
        BassHeatFlareStack stackTemplate,
        BassHeatFlareBall ballTemplate,
        GameVisualEffect stunnedPrefab)
    {
        owner = shotOwner;
        ownerPlayer = shotOwner != null ? shotOwner.GetComponentInParent<PlayerController>() : null;
        // Boss Bass fires the same bomb: it sticks to the player and everything it spawns hurts the player.
        hostileToPlayer = BassBossHostility.IsHostile(shotOwner);
        BassBossHostility.ResolveHost(shotOwner, out ownerBody, out ownerGroup);
        isPlus = plus;
        spaceSize = Mathf.Max(0.01f, worldSpace);
        trySpendAmmo = spendAmmo;
        speed = Mathf.Max(0.5f, moveSpeed);
        direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        flying = true;
        stuck = false;
        exploding = false;

        if (explosionTemplate != null) explosionPrefab = explosionTemplate;
        if (flareTemplate != null) smallFlarePrefab = flareTemplate;
        if (stackTemplate != null) stackPrefab = stackTemplate;
        if (ballTemplate != null) heatBallPrefab = ballTemplate;
        if (stunnedPrefab != null) stunnedEffectPrefab = stunnedPrefab;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        ApplyBehindBassSorting(spriteRenderer);
        PlayState(isPlus ? plusAirStateName : airStateName);
    }

    private void ApplyBehindBassSorting(SpriteRenderer sr)
    {
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(sr, ownerGroup, ownerBody);
    }

    private void FixedUpdate()
    {
        if (!flying || exploding)
            return;

        float dt = Time.fixedDeltaTime;
        Vector2 start = rb.position;
        Vector2 delta = direction * speed * dt;
        float dist = delta.magnitude;
        if (dist < 0.0001f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        int count = Physics2D.CircleCast(start, GetCastRadius(), direction, filter, casts, dist + 0.05f);
        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = casts[i];
            if (!IsStickTarget(hit.collider))
                continue;
            if (hit.distance < bestDist)
            {
                bestDist = hit.distance;
                best = hit;
            }
        }

        if (best.collider != null)
        {
            StickTo(best);
            return;
        }

        // Also catch trigger overlaps for enemies that CircleCast might skim past.
        filter.useTriggers = true;
        int over = Physics2D.OverlapCircle(start + delta, GetCastRadius(), filter, overlaps);
        for (int i = 0; i < over; i++)
        {
            if (!IsStickTarget(overlaps[i]))
                continue;
            Vector2 surface = overlaps[i].ClosestPoint(rb.position);
            StickToCollider(overlaps[i], surface);
            return;
        }

        rb.MovePosition(start + delta);
    }

    private Transform stickFollow;
    private Vector3 stickLocalOffset;
    private bool stickFollowIsCombatant;

    private void Update()
    {
        ApplyDoubleGearScale();

        if (!stuck || exploding)
            return;

        // Follow host without parenting so the bomb survives if the enemy dies/despawns.
        TickStickFollow();

        fuseLeft -= Time.deltaTime;
        if (fuseLeft <= 0f)
            StartCoroutine(CoExplode());
    }

    private void TickStickFollow()
    {
        if (stickFollow == null)
            return;

        if (stickFollowIsCombatant && IsStickFollowDead())
        {
            stickFollow = null;
            stickFollowIsCombatant = false;
            // Keep world pose and continue fuse / explosion as a free bomb.
            return;
        }

        transform.position = stickFollow.TransformPoint(stickLocalOffset);
        if (rb != null)
            rb.position = transform.position;
    }

    private bool IsStickFollowDead()
    {
        if (stickFollow == null)
            return true;

        ICommonEnemy enemy = stickFollow.GetComponentInParent<ICommonEnemy>();
        if (enemy != null)
            return enemy.IsDead;

        Boss boss = stickFollow.GetComponentInParent<Boss>();
        if (boss != null)
            return boss.IsDead;

        PlayerController player = stickFollow.GetComponentInParent<PlayerController>();
        if (player != null)
            return player.IsDead;

        return false;
    }

    private bool IsStickTarget(Collider2D col)
    {
        if (col == null || EnemyDetectionZone.IsDetectionOnlyCollider(col))
            return false;
        if (col.transform == transform || col.transform.IsChildOf(transform))
            return false;
        if (owner != null && (col.transform == owner || col.transform.IsChildOf(owner)))
            return false;
        if (col.GetComponentInParent<BassCrashBomb>() != null)
            return false;
        if (col.GetComponentInParent<BassSmallHeatFlare>() != null)
            return false;
        if (col.GetComponentInParent<BassHeatFlareBall>() != null)
            return false;
        if (col.GetComponentInParent<BassCrashBombExplosion>() != null)
            return false;

        if (hostileToPlayer)
        {
            // Boss Bass bomb: sticks to the player (and walls), never to bosses / enemies.
            if (col.GetComponentInParent<PlayerController>() != null)
                return true;
            if (col.GetComponentInParent<ICommonEnemy>() != null || col.GetComponentInParent<Boss>() != null)
                return false;
            return !col.isTrigger;
        }

        if (col.GetComponentInParent<PlayerController>() != null)
            return false;
        if (col.GetComponentInParent<ICommonEnemy>() != null)
            return true;
        if (col.GetComponentInParent<Boss>() != null)
            return true;
        if (!col.isTrigger)
            return true; // walls / floors
        return false;
    }

    private void StickTo(RaycastHit2D hit)
    {
        Vector2 surfacePoint = hit.collider != null
            ? hit.point
            : rb.position;
        StickToCollider(hit.collider, surfacePoint);
    }

    private void StickToCollider(Collider2D col, Vector2 surfacePoint)
    {
        if (stuck || exploding)
            return;

        flying = false;
        stuck = true;
        fuseLeft = fuseSeconds;
        embedNormal = direction;

        // Keep the fire-facing rotation (no spin to match the wall).
        float ang = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);

        // Left 60% of the collider (from trailing/"top") must stay outside the target.
        // Only the leading 40% (drill tip) may sink in.
        // Prefer the cast contact point when provided; ClosestPoint only as fallback.
        if (col != null && (surfacePoint - (Vector2)rb.position).sqrMagnitude < 0.0001f)
            surfacePoint = col.ClosestPoint(rb.position);

        float centerOffsetIntoTarget = GetStickCenterOffsetIntoTarget();
        Vector2 stickPos = surfacePoint + direction.normalized * centerOffsetIntoTarget;

        rb.position = stickPos;
        transform.position = stickPos;

        // Track host without SetParent — parenting would destroy the bomb when the enemy dies.
        stickFollow = null;
        stickFollowIsCombatant = false;
        if (col != null)
        {
            var enemy = col.GetComponentInParent<ICommonEnemy>();
            var boss = col.GetComponentInParent<Boss>();
            var stuckPlayer = hostileToPlayer ? col.GetComponentInParent<PlayerController>() : null;
            if (stuckPlayer != null)
            {
                stickFollow = stuckPlayer.transform;
                stickFollowIsCombatant = true;
            }
            else if (enemy is Component ec)
            {
                stickFollow = ec.transform;
                stickFollowIsCombatant = true;
            }
            else if (boss != null)
            {
                stickFollow = boss.transform;
                stickFollowIsCombatant = true;
            }
        }

        if (stickFollow != null)
            stickLocalOffset = stickFollow.InverseTransformPoint(stickPos);

        DealStickDamage(col);
        PlayState(isPlus ? plusTickStateName : tickStateName);
    }

    /// <summary>
    /// Signed offset of the bomb center past the surface along fire direction.
    /// Left 60% of the collider stays outside; only the right 40% (drill) enters.
    /// Center sits slightly outside the surface (negative) so that cut is exact.
    /// </summary>
    private float GetStickCenterOffsetIntoTarget()
    {
        const float visibleOutsideFractionFromLeft = 0.6f;
        float half = GetLeadingHalfExtent();
        float width = half * 2f;
        // Trailing edge = center - half. Surface at trailing + 0.6*width.
        // center - surface = half - 0.6*width = width*(0.5 - 0.6).
        return width * (0.5f - visibleOutsideFractionFromLeft);
    }

    /// <summary>Half of the bomb extent along fire direction (local right after stick rotation).</summary>
    private float GetLeadingHalfExtent()
    {
        if (hitCollider is BoxCollider2D box)
        {
            Vector2 size = Vector2.Scale(box.size, new Vector2(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y)));
            return Mathf.Max(0.08f, size.x * 0.5f);
        }

        if (hitCollider is CircleCollider2D circle)
            return Mathf.Max(0.08f, circle.radius * Mathf.Abs(transform.lossyScale.x));

        if (hitCollider != null)
        {
            Vector2 n = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            Bounds b = hitCollider.bounds;
            return Mathf.Max(0.08f, Mathf.Abs(Vector2.Dot(b.extents, n)));
        }

        return 0.45f;
    }

    private void DealStickDamage(Collider2D col)
    {
        if (col == null)
            return;
        BassCrashHeatCombat.ApplyDamageOnly(col, stickDamage * damageMultiplier, false, hostileToPlayer, transform);
    }

    /// <summary>Boss Bass: scales the Plus bomb's Heat Balls (they are 5 damage on the playable weapon).</summary>
    public void SetHostileHeatBallScale(float scale)
    {
        hostileHeatBallScale = Mathf.Max(0f, scale);
    }

    /// <summary>Attack Style level 5 etc. — scales stick + explosion damage. Call right after Launch.</summary>
    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = Mathf.Max(0f, multiplier);
    }

    private IEnumerator CoExplode()
    {
        if (exploding)
            yield break;
        exploding = true;
        stuck = false;
        flying = false;
        stickFollow = null;
        stickFollowIsCombatant = false;
        transform.SetParent(null, true);

        if (hitCollider != null)
            hitCollider.enabled = false;

        Vector3 center = transform.position;
        // Keep fire-facing: local up is perpendicular to travel; Plus balls use that top axis.
        Vector2 topDir = transform.up;

        // Side payloads first (ammo gated).
        if (isPlus)
            SpawnHeatBalls(center, topDir);
        else
            SpawnSmallFlares(center);

        int waves = Mathf.Max(1, Mathf.CeilToInt(explosionTotal / (float)Mathf.Max(1, explosionsPerWave)));
        int spawned = 0;
        float fadeDuration = waves * waveInterval + 0.15f;
        float fadeAge = 0f;

        for (int w = 0; w < waves; w++)
        {
            int batch = Mathf.Min(explosionsPerWave, explosionTotal - spawned);
            for (int i = 0; i < batch; i++)
            {
                Vector2 offset = Random.insideUnitCircle * waveSpawnRadius;
                Vector3 pos = center + (Vector3)offset;
                if (explosionPrefab != null)
                {
                    BassCrashBombExplosion boom = Instantiate(explosionPrefab, pos, Quaternion.identity);
                    boom.ConfigureHostSorting(ownerGroup, ownerBody);
                    float delay = Random.Range(0f, waveInterval * 0.65f);
                    boom.gameObject.SetActive(false);
                    StartCoroutine(CoArmExplosionDelayed(boom, delay));
                }

                spawned++;
            }

            float wait = waveInterval;
            float waited = 0f;
            while (waited < wait)
            {
                waited += Time.deltaTime;
                fadeAge += Time.deltaTime;
                ApplyBombFade(fadeAge / fadeDuration);
                yield return null;
            }
        }

        ApplyBombFade(1f);
        Destroy(gameObject);
    }

    private IEnumerator CoArmExplosionDelayed(BassCrashBombExplosion boom, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (boom == null)
            yield break;
        boom.gameObject.SetActive(true);
        boom.SetHostile(hostileToPlayer);
        boom.Arm(explosionDamage * damageMultiplier, explosionKnockSpaces, spaceSize, stunnedEffectPrefab);
    }

    private void ApplyBombFade(float t01)
    {
        if (spriteRenderer == null)
            return;
        t01 = Mathf.Clamp01(t01);
        // Flicker while fading.
        float flicker = (Mathf.FloorToInt(Time.time * 30f) % 2 == 0) ? 1f : 0.35f;
        Color c = spriteRenderer.color;
        c.a = Mathf.Lerp(1f, 0f, t01) * flicker;
        spriteRenderer.color = c;
    }

    private void SpawnSmallFlares(Vector3 center)
    {
        if (smallFlarePrefab == null)
            return;

        // Land points: two left, two right, spaced 4 spaces apart along ground line.
        float spacing = smallFlareLandSpacingSpaces * spaceSize;
        // Relative to bomb: outer/inner on each side.
        float[] xs =
        {
            center.x - spacing * 1.5f,
            center.x - spacing * 0.5f,
            center.x + spacing * 0.5f,
            center.x + spacing * 1.5f
        };

        // Double Gear + ability: spawn the usual set twice (× GetAbilitySpawnMultiplier).
        int mult = GetSpawnMultiplier();
        for (int m = 0; m < mult; m++)
        {
            float yBias = m * 0.35f * spaceSize;
            for (int i = 0; i < xs.Length; i++)
            {
                float x = xs[i];
                float side = x < center.x ? -1f : 1f;
                Vector2 launchDir = new Vector2(side, 1f).normalized; // 45° falling arc start
                Vector3 spawn = center + (Vector3)(launchDir * 0.2f) + Vector3.up * yBias;
                Vector3 land = new Vector3(x, center.y - 0.5f + yBias, 0f);

                // Approximate throw velocity toward land with gravity.
                Vector2 vel = launchDir * 7.5f;
                BassSmallHeatFlare flare = Instantiate(smallFlarePrefab, spawn, Quaternion.identity);
                flare.ConfigureHostSorting(ownerGroup, ownerBody);
                flare.SetHostile(hostileToPlayer);
                if (hostileToPlayer)
                    flare.SetStackDamageMultiplier(damageMultiplier);
                flare.LaunchStackSpawner(
                    vel,
                    land,
                    stackPrefab,
                    smallFlarePrefab,
                    spaceSize,
                    trySpendAmmo,
                    smallFlareStackAmmoCost,
                    owner);
            }
        }
    }

    private void SpawnHeatBalls(Vector3 center, Vector2 topDir)
    {
        if (heatBallPrefab == null)
            return;

        // Two balls opposite along top axis at 45° to top.
        // Double Gear + ability: spawn that pair twice (extra pair angled slightly).
        Vector2 baseDir = topDir.sqrMagnitude > 0.01f ? topDir.normalized : Vector2.up;
        int mult = GetSpawnMultiplier();
        for (int m = 0; m < mult; m++)
        {
            float tilt = m * 22.5f;
            Vector2 left = Quaternion.Euler(0f, 0f, 45f + tilt) * baseDir;
            Vector2 right = Quaternion.Euler(0f, 0f, -45f - tilt) * baseDir;
            TrySpawnBall(center, left);
            TrySpawnBall(center, right);
        }
    }

    private void TrySpawnBall(Vector3 center, Vector2 dir)
    {
        if (trySpendAmmo != null && !trySpendAmmo(heatBallAmmoCost))
            return;

        BassHeatFlareBall ball = Instantiate(heatBallPrefab, center, Quaternion.identity);
        ball.ConfigureHostSorting(ownerGroup, ownerBody);
        ball.SetHostile(hostileToPlayer, hostileHeatBallScale);
        ball.Launch(dir, spaceSize);
    }

    private float GetCastRadius()
    {
        if (hitCollider is CircleCollider2D circle)
            return Mathf.Max(0.08f, circle.radius * Mathf.Abs(transform.lossyScale.x));
        if (hitCollider != null)
            return Mathf.Max(0.08f, Mathf.Min(hitCollider.bounds.extents.x, hitCollider.bounds.extents.y));
        return 0.35f;
    }

    private void PlayState(string state)
    {
        if (animator == null || string.IsNullOrEmpty(state))
            return;
        if (!animator.HasState(0, Animator.StringToHash(state)))
            return;
        animator.Play(state, 0, 0f);
    }
}
