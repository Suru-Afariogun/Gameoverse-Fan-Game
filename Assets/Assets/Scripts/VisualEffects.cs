using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Static helpers for spawning GameVisualEffect prefabs and tracking active stunned FX.
/// Prefabs are assigned on characters/bosses (like projectile prefabs), not on a central list.
/// </summary>
public static class VisualEffects
{
    private static readonly Dictionary<int, DefaultStunnedEffect> ActiveStunnedByHostId =
        new Dictionary<int, DefaultStunnedEffect>();

    private static readonly Dictionary<int, BriefHitSparkEffect> ActiveBriefHitSparksByHostId =
        new Dictionary<int, BriefHitSparkEffect>();

    private static readonly List<BusterBlastEffect> ActiveBusterBlasts = new List<BusterBlastEffect>(12);
    private const int MaxActiveBusterBlasts = 8;

    private static readonly List<HealedVisualEffect> ActiveHealedEffects = new List<HealedVisualEffect>(16);
    private static readonly List<Collider2D> HealedSpawnColliders = new List<Collider2D>(4);
    private const int MaxActiveHealedEffects = 15;

    /// <summary>
    /// Spawn a Buster Blast from the prefab you assigned on Kit/Boss Kit.
    /// Parented to FirePoint; preserves prefab size/offset and scales to match the projectile.
    /// Concurrent blasts are hard-capped so Machine Gun cannot flood the scene.
    /// </summary>
    public static void SpawnBusterBlast(GameVisualEffect prefab, Transform firePoint, Projectile projectile)
    {
        if (prefab == null || firePoint == null)
            return;

        if (!prefab.IsBusterBlastType)
        {
            Debug.LogWarning(
                $"[VisualEffects] Prefab '{prefab.name}' is not type BusterBlast (got {prefab.EffectType}).",
                prefab);
            return;
        }

        CapActiveBusterBlasts();

        GameObject go = Object.Instantiate(
            prefab.gameObject,
            firePoint.position,
            firePoint.rotation,
            firePoint);

        // Keep authored prefab offset / rotation (small blast is positioned differently on FirePoint).
        go.transform.localPosition = prefab.transform.localPosition;
        go.transform.localRotation = prefab.transform.localRotation;

        float prefabVisual = GetBusterBlastVisualSize(prefab);
        float projectileVisual = GetProjectileVisualSize(projectile);
        float shotSizeMul = prefabVisual > 0.0001f ? projectileVisual / prefabVisual : 1f;
        Vector3 startLocalScale = prefab.transform.localScale * shotSizeMul;
        go.transform.localScale = startLocalScale;

        GameVisualEffect instance = go.GetComponent<GameVisualEffect>();
        BusterBlastEffect driver = go.GetComponent<BusterBlastEffect>();
        if (driver == null)
            driver = go.AddComponent<BusterBlastEffect>();

        ActiveBusterBlasts.Add(driver);

        driver.Play(
            projectile,
            instance.MaxTravelDistance,
            instance.MaxLifetime,
            instance.MaxEndScale,
            instance.BlastColor,
            instance.TintBlastSprites,
            startLocalScale);
    }

    private static void CapActiveBusterBlasts()
    {
        for (int i = ActiveBusterBlasts.Count - 1; i >= 0; i--)
        {
            if (ActiveBusterBlasts[i] == null)
                ActiveBusterBlasts.RemoveAt(i);
        }

        while (ActiveBusterBlasts.Count >= MaxActiveBusterBlasts)
        {
            BusterBlastEffect oldest = ActiveBusterBlasts[0];
            ActiveBusterBlasts.RemoveAt(0);
            if (oldest != null)
                Object.Destroy(oldest.gameObject);
        }
    }

    internal static void UnregisterBusterBlast(BusterBlastEffect blast)
    {
        if (blast == null)
            return;

        ActiveBusterBlasts.Remove(blast);
    }

    /// <summary>
    /// Pick small prefab for small shots; medium/big prefab for medium and big (same art, scaled up for big).
    /// </summary>
    public static GameVisualEffect ResolveBusterBlastPrefab(
        ProjectileShotType shotType,
        GameVisualEffect smallPrefab,
        GameVisualEffect mediumBigPrefab)
    {
        switch (shotType)
        {
            case ProjectileShotType.Medium:
            case ProjectileShotType.Big:
                return mediumBigPrefab != null ? mediumBigPrefab : smallPrefab;
            default:
                return smallPrefab;
        }
    }

    /// <summary>
    /// Max sprite dimension using the prefab root's local scale (authored art size).
    /// </summary>
    public static float GetBusterBlastVisualSize(GameVisualEffect prefab)
    {
        if (prefab == null)
            return 1f;

        SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
        return GetSpriteVisualSize(prefab.transform, sr);
    }

    /// <summary>
    /// Max sprite dimension for the projectile that was just fired.
    /// </summary>
    public static float GetProjectileVisualSize(Projectile projectile)
    {
        if (projectile == null)
            return 1f;

        SpriteRenderer sr = projectile.GetComponent<SpriteRenderer>();
        return GetSpriteVisualSize(projectile.transform, sr);
    }

    private static float GetSpriteVisualSize(Transform root, SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null)
            return 1f;

        Vector2 spriteSize = sr.sprite.bounds.size;
        Vector3 scale = root.localScale;
        return Mathf.Max(
            spriteSize.x * Mathf.Abs(scale.x),
            spriteSize.y * Mathf.Abs(scale.y));
    }

    /// <summary>
    /// Hit-stun shock from the prefab assigned on the character/boss.
    /// One instance per host; refreshes on re-hit while stunned.
    /// </summary>
    public static void PlayStunned(GameVisualEffect prefab, Component host)
    {
        PlayStunned(prefab, host, null);
    }

    /// <summary>
    /// Boss / enemy hit spark. When <paramref name="snapAnchor"/> is set (e.g. Spark Spawn Box),
    /// the effect tracks that point instead of the body collider center.
    /// </summary>
    public static void PlayStunned(GameVisualEffect prefab, Component host, Transform snapAnchor)
    {
        if (prefab == null || host == null)
            return;

        if (!prefab.IsStunnedType)
        {
            Debug.LogWarning(
                $"[VisualEffects] Prefab '{prefab.name}' is not type DefaultStunned (got {prefab.EffectType}).",
                prefab);
            return;
        }

        int id = host.gameObject.GetInstanceID();

        if (ActiveStunnedByHostId.TryGetValue(id, out DefaultStunnedEffect existing) &&
            existing != null)
        {
            existing.Refresh(snapAnchor);
            return;
        }

        Collider2D body = FindBodyCollider(host.gameObject);
        Vector3 worldCenter = snapAnchor != null
            ? snapAnchor.position
            : body != null ? body.bounds.center : host.transform.position;

        GameObject go = Object.Instantiate(
            prefab.gameObject,
            worldCenter,
            Quaternion.identity,
            host.transform);
        go.SetActive(true);

        GameVisualEffect instance = go.GetComponent<GameVisualEffect>();
        DefaultStunnedEffect effect = go.GetComponent<DefaultStunnedEffect>();
        if (effect == null)
            effect = go.AddComponent<DefaultStunnedEffect>();

        effect.Initialize(host.transform, body, instance, snapAnchor);
        ActiveStunnedByHostId[id] = effect;
    }

    /// <summary>
    /// Short enemy hit spark (Laser / Tanker / ScrapNit): shows at the spark box for
    /// <paramref name="duration"/> seconds then despawns. Does not use the long-lived stun ring.
    /// </summary>
    public static void PlayBriefHitSpark(
        GameVisualEffect prefab,
        Component host,
        Transform snapAnchor,
        float duration = 0.15f)
    {
        if (prefab == null || host == null)
            return;

        int id = host.gameObject.GetInstanceID();
        if (ActiveBriefHitSparksByHostId.TryGetValue(id, out BriefHitSparkEffect existing) &&
            existing != null)
        {
            existing.Restart(duration);
            if (snapAnchor != null)
                existing.transform.position = snapAnchor.position;
            return;
        }

        Vector3 worldCenter = snapAnchor != null
            ? snapAnchor.position
            : host.transform.position;

        GameObject go = Object.Instantiate(
            prefab.gameObject,
            worldCenter,
            Quaternion.identity,
            host.transform);
        go.SetActive(true);

        // Strip physics so sparks never shove anything.
        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
            Object.Destroy(rb);
        Collider2D col = go.GetComponent<Collider2D>();
        if (col != null)
            Object.Destroy(col);

        BriefHitSparkEffect effect = go.GetComponent<BriefHitSparkEffect>();
        if (effect == null)
            effect = go.AddComponent<BriefHitSparkEffect>();

        effect.Initialize(id, Mathf.Max(0.05f, duration), snapAnchor);
        ActiveBriefHitSparksByHostId[id] = effect;
    }

    internal static void UnregisterBriefHitSpark(BriefHitSparkEffect effect, int hostId)
    {
        if (effect == null)
            return;

        if (ActiveBriefHitSparksByHostId.TryGetValue(hostId, out BriefHitSparkEffect current) &&
            current == effect)
        {
            ActiveBriefHitSparksByHostId.Remove(hostId);
        }
    }

    public static void StopStunned(Component host)
    {
        if (host == null)
            return;

        int id = host.gameObject.GetInstanceID();
        if (!ActiveStunnedByHostId.TryGetValue(id, out DefaultStunnedEffect effect))
            return;

        ActiveStunnedByHostId.Remove(id);
        if (effect != null)
            effect.Despawn();
    }

    internal static void UnregisterStunned(DefaultStunnedEffect effect, int hostId)
    {
        if (effect == null)
            return;

        if (ActiveStunnedByHostId.TryGetValue(hostId, out DefaultStunnedEffect current) &&
            current == effect)
        {
            ActiveStunnedByHostId.Remove(hostId);
        }
    }

    /// <summary>
    /// Prefer a non-trigger collider on the root, then any non-trigger child (skip AttackBox tools).
    /// </summary>
    public static Collider2D FindBodyCollider(GameObject host)
    {
        if (host == null)
            return null;

        Collider2D root = host.GetComponent<Collider2D>();
        if (root != null && !root.isTrigger)
            return root;

        Collider2D[] cols = host.GetComponentsInChildren<Collider2D>(true);
        Collider2D fallback = null;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D c = cols[i];
            if (c == null)
                continue;

            if (c.GetComponent<AttackHitbox>() != null)
                continue;

            if (EnemyDetectionZone.IsDetectionOnlyCollider(c))
                continue;

            if (!c.isTrigger)
                return c;

            if (fallback == null)
                fallback = c;
        }

        return fallback;
    }

    /// <summary>
    /// Spawn 3–5 heal sparks from the host's body collider, attack box, and fire point.
    /// They rise in world space at one shared speed and fade 0.7 spaces above their spawn.
    /// </summary>
    public static void PlayHealed(GameVisualEffect prefab, Component host)
    {
        if (prefab == null || host == null)
            return;

        if (!prefab.IsHealedType)
        {
            Debug.LogWarning(
                $"[VisualEffects] Prefab '{prefab.name}' is not type Healed (got {prefab.EffectType}).",
                prefab);
            return;
        }

        int minCount = Mathf.Max(1, prefab.HealMinCount);
        int maxCount = Mathf.Max(minCount, prefab.HealMaxCount);
        int count = Random.Range(minCount, maxCount + 1);
        float speed = prefab.HealRiseSpeed;
        float rise = prefab.HealRiseDistance;

        CollectHealedSpawnSources(host.gameObject, HealedSpawnColliders, out Transform firePoint);
        int sourceCount = HealedSpawnColliders.Count + (firePoint != null ? 1 : 0);
        if (sourceCount <= 0)
            sourceCount = 1;

        ResolveHealedSort(host, prefab.SortOrderInFrontOfHost, out int sortLayerId, out int sortOrder);

        for (int i = 0; i < count; i++)
        {
            CapActiveHealedEffects();

            Vector3 spawnPos = PickHealedSpawnPosition(
                host.transform,
                HealedSpawnColliders,
                firePoint,
                i,
                sourceCount);

            GameObject go = Object.Instantiate(
                prefab.gameObject,
                spawnPos,
                Quaternion.identity);

            HealedVisualEffect driver = go.GetComponent<HealedVisualEffect>();
            if (driver == null)
                driver = go.AddComponent<HealedVisualEffect>();

            ActiveHealedEffects.Add(driver);
            driver.Play(prefab, speed, rise, sortLayerId, sortOrder);
        }
    }

    internal static void UnregisterHealed(HealedVisualEffect effect)
    {
        if (effect == null)
            return;

        ActiveHealedEffects.Remove(effect);
    }

    /// <summary>
    /// Spawn the one-shot purple enemy death explosion at a world position.
    /// </summary>
    public static void SpawnEnemyExplosionSmallPurple(
        GameVisualEffect prefab,
        Vector3 worldPosition,
        SpriteRenderer sortReference = null)
    {
        if (prefab == null)
            return;

        if (!prefab.IsEnemyExplosionSmallPurpleType)
        {
            Debug.LogWarning(
                $"[VisualEffects] Prefab '{prefab.name}' is not type EnemyExplosionSmallPurple (got {prefab.EffectType}).",
                prefab);
            return;
        }

        GameObject go = Object.Instantiate(prefab.gameObject, worldPosition, Quaternion.identity);
        // Prefab assets are often left inactive as scene templates; clones inherit that
        // and would never play unless explicitly enabled.
        go.SetActive(true);
        GameVisualEffect instance = go.GetComponent<GameVisualEffect>();

        EnemyExplosionSmallPurpleEffect driver = go.GetComponent<EnemyExplosionSmallPurpleEffect>();
        if (driver == null)
            driver = go.AddComponent<EnemyExplosionSmallPurpleEffect>();

        int sortLayerId = 0;
        int sortOrder = prefab.SortOrderInFrontOfHost;
        if (sortReference != null)
        {
            sortLayerId = sortReference.sortingLayerID;
            sortOrder = sortReference.sortingOrder + prefab.SortOrderInFrontOfHost;
        }

        driver.Play(
            instance != null ? instance.EnemyExplosionAnimationStateName : "Enemy explosion small purple",
            instance != null ? instance.EnemyExplosionMaxLifetime : 0.2f,
            sortLayerId,
            sortOrder);
    }

    /// <summary>
    /// Blocker Bot hit: four sparks (2 left, 2 right) rise then fall while fading.
    /// Spread farther apart and slightly staggered so they don't pile on the same pixel.
    /// </summary>
    public static void SpawnBlockerBotHitSparks(GameVisualEffect prefab, Vector3 origin)
    {
        if (prefab == null)
            return;

        float[] xSigns = { -1f, -1f, 1f, 1f };
        // Inner / outer lanes — spaced so they don't overlap immediately.
        float[] xSpreads = { 1.15f, 1.85f, 1.15f, 1.85f };
        float[] startDelays = { 0f, 0.05f, 0.025f, 0.075f };
        for (int i = 0; i < 4; i++)
        {
            GameObject go = Object.Instantiate(prefab.gameObject, origin, Quaternion.identity);
            go.SetActive(true);

            BlockerBotHitSparkEffect spark = go.GetComponent<BlockerBotHitSparkEffect>();
            if (spark == null)
                spark = go.AddComponent<BlockerBotHitSparkEffect>();

            spark.Play(origin, xSigns[i] * xSpreads[i], startDelays[i]);
        }
    }

    /// <summary>
    /// CheckPoint Bot activate: N sparks burst like confetti, rise toward max height (slowing),
    /// then fall slowly while fading out over <paramref name="lifetime"/>.
    /// </summary>
    public static void SpawnCheckPointBotConfettiSparks(
        GameVisualEffect prefab,
        Vector3 origin,
        int count,
        float speed,
        float maxHeight,
        float lifetime,
        SpriteRenderer sortReference)
    {
        if (prefab == null)
            return;

        int n = Mathf.Max(1, count);
        int sortLayerId = sortReference != null ? sortReference.sortingLayerID : 0;
        int sortOrder = sortReference != null ? sortReference.sortingOrder + 2 : 2;

        for (int i = 0; i < n; i++)
        {
            // Even ring + small jitter so they don't look identical.
            float angle = (Mathf.PI * 2f * i) / n + Random.Range(-0.18f, 0.18f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            GameObject go = Object.Instantiate(prefab.gameObject, origin, Quaternion.identity);
            go.SetActive(true);

            // Strip physics so sparks don't shove anything.
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
                Object.Destroy(rb);
            Collider2D col = go.GetComponent<Collider2D>();
            if (col != null)
                Object.Destroy(col);

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null)
                sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerID = sortLayerId;
                sr.sortingOrder = sortOrder + (i % 3);
            }

            CheckPointBotConfettiSparkEffect spark = go.GetComponent<CheckPointBotConfettiSparkEffect>();
            if (spark == null)
                spark = go.AddComponent<CheckPointBotConfettiSparkEffect>();

            spark.Play(origin, dir, speed, maxHeight, lifetime);
        }
    }

    /// <summary>
    /// Blocker Bot death: 2 of each smoke particle, scattered, rising and fading (Contract Rush style).
    /// </summary>
    public static void SpawnBlockerSmokeDeath(
        GameVisualEffect[] particlePrefabs,
        Vector3 center,
        int copiesPerPrefab,
        float scatterRadius,
        float riseDistance,
        float riseSpeed,
        SpriteRenderer sortReference)
    {
        if (particlePrefabs == null || particlePrefabs.Length == 0)
            return;

        int sortLayerId = sortReference != null ? sortReference.sortingLayerID : 0;
        int sortOrder = sortReference != null ? sortReference.sortingOrder + 2 : 2;
        int copies = Mathf.Max(1, copiesPerPrefab);

        for (int p = 0; p < particlePrefabs.Length; p++)
        {
            GameVisualEffect prefab = particlePrefabs[p];
            if (prefab == null)
                continue;

            for (int c = 0; c < copies; c++)
            {
                Vector2 offset = Random.insideUnitCircle * Mathf.Max(0.05f, scatterRadius);
                // Bias slightly upward so some spawn "on top".
                offset.y = Mathf.Abs(offset.y) * 0.65f + Random.Range(0f, scatterRadius * 0.45f);
                Vector3 spawnPos = center + (Vector3)offset;

                GameObject go = Object.Instantiate(prefab.gameObject, spawnPos, Quaternion.identity);
                go.SetActive(true);

                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (sr == null)
                    sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sortingLayerID = sortLayerId;
                    sr.sortingOrder = sortOrder + Random.Range(0, 3);
                }

                BlockerSmokeParticleEffect smoke = go.GetComponent<BlockerSmokeParticleEffect>();
                if (smoke == null)
                    smoke = go.AddComponent<BlockerSmokeParticleEffect>();

                float speedJitter = riseSpeed * Random.Range(0.85f, 1.15f);
                smoke.Play(riseDistance, speedJitter);
            }
        }
    }

    private static void CapActiveHealedEffects()
    {
        for (int i = ActiveHealedEffects.Count - 1; i >= 0; i--)
        {
            if (ActiveHealedEffects[i] == null)
                ActiveHealedEffects.RemoveAt(i);
        }

        while (ActiveHealedEffects.Count >= MaxActiveHealedEffects)
        {
            HealedVisualEffect oldest = ActiveHealedEffects[0];
            ActiveHealedEffects.RemoveAt(0);
            if (oldest != null)
                Object.Destroy(oldest.gameObject);
        }
    }

    /// <summary>
    /// Order is body, attack box, fire point so extras round-robin onto body + attack
    /// (e.g. 5 sparks → 2 body, 2 attack, 1 fire).
    /// </summary>
    private static void CollectHealedSpawnSources(
        GameObject host,
        List<Collider2D> colliders,
        out Transform firePoint)
    {
        colliders.Clear();
        firePoint = null;
        if (host == null)
            return;

        Collider2D body = FindBodyCollider(host);
        if (body != null)
            colliders.Add(body);

        AttackHitbox hitbox = host.GetComponentInChildren<AttackHitbox>(true);
        if (hitbox != null)
        {
            Collider2D attackCol = hitbox.GetComponent<Collider2D>();
            if (attackCol != null && attackCol != body)
                colliders.Add(attackCol);
        }

        firePoint = FindChildByName(host.transform, "FirePoint");
    }

    private static Vector3 PickHealedSpawnPosition(
        Transform host,
        List<Collider2D> colliders,
        Transform firePoint,
        int index,
        int sourceCount)
    {
        int sourceIndex = sourceCount > 0 ? index % sourceCount : 0;
        int colliderCount = colliders != null ? colliders.Count : 0;

        if (sourceIndex < colliderCount)
        {
            Collider2D col = colliders[sourceIndex];
            if (col != null)
                return RandomPointInCollider(col, host != null ? host.position.z : 0f);
            return host != null ? host.position : Vector3.zero;
        }

        if (firePoint != null)
        {
            Vector2 jitter = Random.insideUnitCircle * 0.08f;
            Vector3 p = firePoint.position + (Vector3)jitter;
            p.z = host != null ? host.position.z : p.z;
            return p;
        }

        return host != null ? host.position : Vector3.zero;
    }

    private static Vector3 RandomPointInCollider(Collider2D col, float z)
    {
        if (col == null)
            return new Vector3(0f, 0f, z);

        Bounds b = col.bounds;
        if (b.size.x > 0.02f && b.size.y > 0.02f)
        {
            return new Vector3(
                Random.Range(b.min.x, b.max.x),
                Random.Range(b.min.y, b.max.y),
                z);
        }

        if (col is BoxCollider2D box)
        {
            Vector2 size = box.size;
            Vector2 offset = box.offset;
            Vector2 local = offset + new Vector2(
                Random.Range(-size.x, size.x) * 0.5f,
                Random.Range(-size.y, size.y) * 0.5f);
            Vector3 world = col.transform.TransformPoint(local);
            world.z = z;
            return world;
        }

        if (col is CapsuleCollider2D capsule)
        {
            Vector2 size = capsule.size;
            Vector2 offset = capsule.offset;
            Vector2 local = offset + new Vector2(
                Random.Range(-size.x, size.x) * 0.5f,
                Random.Range(-size.y, size.y) * 0.5f);
            Vector3 world = col.transform.TransformPoint(local);
            world.z = z;
            return world;
        }

        Vector3 fallback = col.transform.position;
        fallback.z = z;
        return fallback;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void ResolveHealedSort(
        Component host,
        int sortOffset,
        out int sortingLayerId,
        out int sortingOrder)
    {
        sortingLayerId = 0;
        sortingOrder = Mathf.Max(1, sortOffset);
        if (host == null)
            return;

        SortingGroup group = host.GetComponent<SortingGroup>();
        if (group != null)
        {
            sortingLayerId = group.sortingLayerID;
            sortingOrder = group.sortingOrder + Mathf.Max(1, sortOffset);
            return;
        }

        SpriteRenderer body = host.GetComponentInChildren<SpriteRenderer>(true);
        if (body == null)
            return;

        sortingLayerId = body.sortingLayerID;
        sortingOrder = body.sortingOrder + Mathf.Max(1, sortOffset);
    }

    /// <summary>
    /// Latest Mega Man-style death burst (for fade/respawn waits).
    /// </summary>
    public static DeathEnergyBallBurst ActiveDeathBurst { get; private set; }

    /// <summary>
    /// Spawn 8 (default) death energy balls in a circle from the host collider center.
    /// Balls never collide. Optional hideHost for final death / boss defeat.
    /// </summary>
    public static DeathEnergyBallBurst PlayDeathEnergyBalls(
        GameVisualEffect prefab,
        Component host,
        bool hideHost)
    {
        if (prefab == null || host == null)
            return null;

        if (!prefab.IsDeathEnergyBallType)
        {
            Debug.LogWarning(
                $"[VisualEffects] Prefab '{prefab.name}' is not type DeathEnergyBall (got {prefab.EffectType}).",
                prefab);
            return null;
        }

        StopStunned(host);

        Collider2D body = FindBodyCollider(host.gameObject);
        Vector3 center = body != null ? body.bounds.center : host.transform.position;

        if (hideHost)
            SetHostSpritesVisible(host.gameObject, false);

        GameObject root = new GameObject($"{prefab.name}_DeathBurst");
        root.transform.position = center;
        DeathEnergyBallBurst burst = root.AddComponent<DeathEnergyBallBurst>();
        burst.Begin(prefab, center, host);
        ActiveDeathBurst = burst;
        return burst;
    }

    public static void ClearActiveDeathBurst(DeathEnergyBallBurst burst)
    {
        if (ActiveDeathBurst == burst)
            ActiveDeathBurst = null;
    }

    /// <summary>
    /// Wait until every ball in the burst has traveled at least the wait distance
    /// (defaults to the prefab's Death Ball Wait Travel Distance, usually 8).
    /// </summary>
    public static System.Collections.IEnumerator WaitForDeathBallsTravel(
        DeathEnergyBallBurst burst,
        float overrideWaitDistance = -1f)
    {
        if (burst == null)
            yield break;

        float wait = overrideWaitDistance > 0f
            ? overrideWaitDistance
            : burst.WaitTravelDistance;

        while (burst != null && !burst.HasReachedTravelDistance(wait))
            yield return null;
    }

    /// <summary>
    /// Crystal respawn: balls fly out to turnDistance, then reverse home. Completes when all have regrouped.
    /// </summary>
    public static System.Collections.IEnumerator WaitForDeathBallsCrystalRecall(
        DeathEnergyBallBurst burst,
        float turnDistance = 9f)
    {
        if (burst == null)
            yield break;

        if (!burst.IsCrystalRecallMode)
            burst.EnableCrystalRecall(turnDistance);

        while (burst != null && !burst.AllBallsReturned)
            yield return null;
    }

    public static void SetHostSpritesVisible(GameObject host, bool visible)
    {
        if (host == null)
            return;

        SpriteRenderer[] renderers = host.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    internal static bool AnimatorHasParameter(Animator animator, string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        for (int i = 0; i < animator.parameterCount; i++)
        {
            if (animator.GetParameter(i).name == parameterName)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Runtime driver for Buster Blast prefabs: fade SpriteRenderers + grow scale,
/// despawn when shot travel OR lifetime hits the limit.
/// </summary>
public class BusterBlastEffect : MonoBehaviour
{
    private Projectile projectile;
    private Vector3 projectileSpawnPos;
    private float maxTravel;
    private float maxLifetime;
    private float maxScale;
    private float age;
    private Vector3 baseLocalScale = Vector3.one;
    private Color blastTint = Color.white;
    private bool applyTint;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private bool running;

    public void Play(
        Projectile linkedProjectile,
        float travelLimit,
        float lifetimeLimit,
        float endScale,
        Color tint,
        bool tintSprites,
        Vector3 startingLocalScale)
    {
        projectile = linkedProjectile;
        projectileSpawnPos = linkedProjectile != null
            ? linkedProjectile.transform.position
            : transform.position;
        maxTravel = travelLimit;
        maxLifetime = lifetimeLimit;
        maxScale = endScale;
        baseLocalScale = startingLocalScale;
        blastTint = tint;
        applyTint = tintSprites;
        age = 0f;
        running = true;

        CacheRenderers();
        ApplyVisual(0f);
    }

    private void CacheRenderers()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            Color c = renderers[i].color;
            if (applyTint)
            {
                c.r *= blastTint.r;
                c.g *= blastTint.g;
                c.b *= blastTint.b;
                c.a *= blastTint.a;
            }

            baseColors[i] = c;
            renderers[i].color = c;
        }
    }

    private void Update()
    {
        if (!running)
            return;

        age += Time.deltaTime;

        float traveled = 0f;
        if (projectile != null)
            traveled = Vector3.Distance(projectileSpawnPos, projectile.transform.position);

        float tTime = age / Mathf.Max(0.0001f, maxLifetime);
        float tDist = traveled / Mathf.Max(0.0001f, maxTravel);
        float t = Mathf.Clamp01(Mathf.Max(tTime, tDist));

        if (projectile == null)
            t = Mathf.Clamp01(Mathf.Max(t, tTime));

        ApplyVisual(t);

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        VisualEffects.UnregisterBusterBlast(this);
    }

    private void ApplyVisual(float t)
    {
        float growMul = Mathf.Lerp(1f, maxScale, t);
        transform.localScale = baseLocalScale * growMul;

        if (renderers == null)
            return;

        float alpha = 1f - t;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
                continue;

            Color c = baseColors[i];
            c.a = baseColors[i].a * alpha;
            sr.color = c;
        }
    }
}

/// <summary>
/// Runtime driver for Default Stunned prefabs: tracks body collider center
/// and plays the stunned animation until despawned.
/// </summary>
public class DefaultStunnedEffect : MonoBehaviour
{
    private Transform host;
    private Transform snapAnchor;
    private Collider2D bodyCollider;
    private Animator animator;
    private string isStunnedBoolParameter = "IsStunned";
    private string stunnedTriggerParameter = "stunned";
    private int sortOrderOffset = 1;
    private SpriteRenderer[] effectRenderers;
    private int hostId;

    public void Initialize(Transform hostTransform, Collider2D body, GameVisualEffect definition)
    {
        Initialize(hostTransform, body, definition, null);
    }

    public void Initialize(
        Transform hostTransform,
        Collider2D body,
        GameVisualEffect definition,
        Transform worldSnapAnchor)
    {
        host = hostTransform;
        bodyCollider = body;
        snapAnchor = worldSnapAnchor;
        hostId = hostTransform != null ? hostTransform.gameObject.GetInstanceID() : 0;

        if (definition != null)
        {
            isStunnedBoolParameter = definition.IsStunnedBoolParameter;
            stunnedTriggerParameter = definition.StunnedTriggerParameter;
            sortOrderOffset = definition.SortOrderInFrontOfHost;
        }

        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        effectRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        transform.SetAsLastSibling();

        SnapToColliderCenter();
        ApplySortInFrontOfHost();
        DriveStunnedAnimator(restart: true);
    }

    public void Refresh()
    {
        Refresh(snapAnchor);
    }

    public void Refresh(Transform worldSnapAnchor)
    {
        if (worldSnapAnchor != null)
            snapAnchor = worldSnapAnchor;

        SnapToColliderCenter();
        ApplySortInFrontOfHost();
        DriveStunnedAnimator(restart: true);
    }

    public void Despawn()
    {
        ClearStunnedAnimator();
        VisualEffects.UnregisterStunned(this, hostId);
        Destroy(gameObject);
    }

    private void LateUpdate()
    {
        if (host == null)
        {
            Despawn();
            return;
        }

        SnapToColliderCenter();
        ApplySortInFrontOfHost();
    }

    private void ApplySortInFrontOfHost()
    {
        if (host == null || effectRenderers == null || effectRenderers.Length == 0)
            return;

        SpriteRenderer[] hostRenderers = host.GetComponentsInChildren<SpriteRenderer>(true);
        if (hostRenderers == null || hostRenderers.Length == 0)
            return;

        int hostLayerId = 0;
        int hostMaxOrder = int.MinValue;
        bool foundHostRenderer = false;
        for (int i = 0; i < hostRenderers.Length; i++)
        {
            SpriteRenderer sr = hostRenderers[i];
            if (sr == null || IsEffectRenderer(sr))
                continue;

            if (!foundHostRenderer || sr.sortingOrder >= hostMaxOrder)
            {
                hostMaxOrder = sr.sortingOrder;
                hostLayerId = sr.sortingLayerID;
                foundHostRenderer = true;
            }
        }

        if (!foundHostRenderer)
            return;

        int frontOrder = hostMaxOrder + sortOrderOffset;
        for (int i = 0; i < effectRenderers.Length; i++)
        {
            SpriteRenderer fx = effectRenderers[i];
            if (fx == null)
                continue;

            fx.sortingLayerID = hostLayerId;
            fx.sortingOrder = frontOrder;
        }
    }

    private bool IsEffectRenderer(SpriteRenderer sr)
    {
        if (sr == null || effectRenderers == null)
            return false;

        for (int i = 0; i < effectRenderers.Length; i++)
        {
            if (effectRenderers[i] == sr)
                return true;
        }

        return sr.transform.IsChildOf(transform) || sr.transform == transform;
    }

    private void OnDestroy()
    {
        VisualEffects.UnregisterStunned(this, hostId);
    }

    private void SnapToColliderCenter()
    {
        if (host == null)
            return;

        if (snapAnchor != null)
        {
            transform.position = snapAnchor.position;
            return;
        }

        Vector3 world = bodyCollider != null ? bodyCollider.bounds.center : host.position;
        transform.position = world;
    }

    private void DriveStunnedAnimator(bool restart)
    {
        if (animator == null)
            return;

        if (!string.IsNullOrEmpty(isStunnedBoolParameter) &&
            HasAnimatorParameter(isStunnedBoolParameter, AnimatorControllerParameterType.Bool))
            animator.SetBool(isStunnedBoolParameter, true);

        if (!string.IsNullOrEmpty(stunnedTriggerParameter) &&
            HasAnimatorParameter(stunnedTriggerParameter, AnimatorControllerParameterType.Trigger))
        {
            animator.ResetTrigger(stunnedTriggerParameter);
            if (restart)
                animator.SetTrigger(stunnedTriggerParameter);
        }
        else if (restart && animator.runtimeAnimatorController != null)
        {
            // Controllers like Laser Bot spark only loop a default clip (IsActive).
            animator.Play(0, 0, 0f);
        }
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p != null && p.type == type && p.name == parameterName)
                return true;
        }

        return false;
    }

    private void ClearStunnedAnimator()
    {
        if (animator == null ||
            string.IsNullOrEmpty(isStunnedBoolParameter) ||
            !HasAnimatorParameter(isStunnedBoolParameter, AnimatorControllerParameterType.Bool))
            return;

        animator.SetBool(isStunnedBoolParameter, false);
    }
}

/// <summary>
/// Spawns Mega Man-style death energy balls in an equal circle and tracks travel for fade waits.
/// Crystal respawns can reverse the balls home so the boss reforms at the death center.
/// </summary>
public class DeathEnergyBallBurst : MonoBehaviour
{
    private readonly System.Collections.Generic.List<DeathEnergyBallEffect> balls =
        new System.Collections.Generic.List<DeathEnergyBallEffect>(8);

    private float waitTravelDistance = 8f;
    private int ballCount;
    private int ballsPastWaitDistance;
    private int ballsReturned;
    private bool crystalRecallMode;
    private Coroutine autoDestroyRoutine;

    public float WaitTravelDistance => waitTravelDistance;
    public bool IsCrystalRecallMode => crystalRecallMode;

    /// <summary>
    /// True once every still-living ball has returned (destroyed balls are ignored so we never hang).
    /// </summary>
    public bool AllBallsReturned
    {
        get
        {
            if (!crystalRecallMode)
                return false;

            int living = 0;
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i] != null)
                    living++;
            }

            if (living <= 0)
                return true;

            return ballsReturned >= living;
        }
    }

    public float MinDistanceTraveled
    {
        get
        {
            if (balls.Count == 0)
                return 0f;

            float min = float.MaxValue;
            for (int i = 0; i < balls.Count; i++)
            {
                DeathEnergyBallEffect ball = balls[i];
                if (ball == null)
                    continue;
                if (ball.DistanceTraveled < min)
                    min = ball.DistanceTraveled;
            }

            return min == float.MaxValue ? 0f : min;
        }
    }

    public bool HasReachedTravelDistance(float distance)
    {
        if (ballCount <= 0)
            return true;

        // Prefer the latch so destroyed balls still count once they've cleared the wait.
        if (distance <= waitTravelDistance && ballsPastWaitDistance >= ballCount)
            return true;

        if (balls.Count == 0)
            return ballsPastWaitDistance >= ballCount;

        for (int i = 0; i < balls.Count; i++)
        {
            DeathEnergyBallEffect ball = balls[i];
            if (ball == null)
                continue;
            if (ball.DistanceTraveled < distance)
                return false;
        }

        return true;
    }

    public void NotifyBallReachedWaitDistance()
    {
        ballsPastWaitDistance++;
    }

    public void NotifyBallReturned()
    {
        ballsReturned++;
    }

    /// <summary>
    /// Switch an in-flight death burst into crystal recall: fly out to turnDistance, then reverse home.
    /// </summary>
    public void EnableCrystalRecall(float turnDistance)
    {
        crystalRecallMode = true;
        ballsReturned = 0;
        waitTravelDistance = Mathf.Max(0.1f, turnDistance);

        if (autoDestroyRoutine != null)
        {
            StopCoroutine(autoDestroyRoutine);
            autoDestroyRoutine = null;
        }

        for (int i = 0; i < balls.Count; i++)
        {
            if (balls[i] != null)
                balls[i].EnableCrystalRecall(waitTravelDistance);
        }
    }

    public void Begin(GameVisualEffect prefab, Vector3 center, Component hostComponent)
    {
        waitTravelDistance = prefab.DeathBallWaitTravelDistance;
        ballCount = Mathf.Max(1, prefab.DeathBallCount);
        ballsPastWaitDistance = 0;
        ballsReturned = 0;
        crystalRecallMode = false;

        float angleStep = 360f / ballCount;
        float angleOffset = prefab.DeathBallAngleOffsetDegrees;

        SpriteRenderer hostSprite = hostComponent != null
            ? hostComponent.GetComponentInChildren<SpriteRenderer>()
            : null;

        for (int i = 0; i < ballCount; i++)
        {
            float angleDeg = angleOffset + angleStep * i;
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            GameObject go = Object.Instantiate(
                prefab.gameObject,
                center,
                Quaternion.identity,
                transform);

            DisableAllColliders(go);

            GameVisualEffect instance = go.GetComponent<GameVisualEffect>();
            DeathEnergyBallEffect ball = go.GetComponent<DeathEnergyBallEffect>();
            if (ball == null)
                ball = go.AddComponent<DeathEnergyBallEffect>();

            ball.Play(dir, instance, hostSprite, this, waitTravelDistance);
            balls.Add(ball);
        }

        autoDestroyRoutine = StartCoroutine(AutoDestroyAfter(
            Mathf.Max(prefab.DeathBallMaxLifetime, 3f) + 1f));
    }

    private System.Collections.IEnumerator AutoDestroyAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!crystalRecallMode)
            Destroy(gameObject);
    }

    private static void DisableAllColliders(GameObject go)
    {
        Collider2D[] cols = go.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = false;
        }

        Collider[] cols3d = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols3d.Length; i++)
        {
            if (cols3d[i] != null)
                cols3d[i].enabled = false;
        }

        Rigidbody2D[] bodies = go.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
            {
                bodies[i].simulated = false;
                bodies[i].bodyType = RigidbodyType2D.Kinematic;
            }
        }
    }

    private void OnDestroy()
    {
        VisualEffects.ClearActiveDeathBurst(this);
    }
}

/// <summary>
/// One death energy ball: flies outward, plays death anim, fades with travel.
/// Crystal recall mode: flies out to a turn distance, then reverses home at the same speed.
/// </summary>
public class DeathEnergyBallEffect : MonoBehaviour
{
    private enum FlightPhase
    {
        Outbound,
        Returning,
        Done
    }

    private Vector2 direction = Vector2.right;
    private Vector3 spawnPos;
    private float speed = 8f;
    private float fadeDistance = 6f;
    private float maxLifetime = 2.5f;
    private float waitNotifyDistance = 3f;
    private float age;
    private float distanceTraveled;
    private bool notifiedWait;
    private bool crystalRecall;
    private float recallTurnDistance = 9f;
    private FlightPhase phase = FlightPhase.Outbound;
    private DeathEnergyBallBurst ownerBurst;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Animator animator;
    private bool running;

    public float DistanceTraveled => distanceTraveled;

    public void Play(
        Vector2 dir,
        GameVisualEffect definition,
        SpriteRenderer hostSprite,
        DeathEnergyBallBurst burst,
        float waitDistance)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        spawnPos = transform.position;
        age = 0f;
        distanceTraveled = 0f;
        notifiedWait = false;
        crystalRecall = false;
        phase = FlightPhase.Outbound;
        ownerBurst = burst;
        waitNotifyDistance = waitDistance;
        running = true;

        if (definition != null)
        {
            speed = definition.DeathBallSpeed;
            fadeDistance = definition.DeathBallFadeDistance;
            maxLifetime = definition.DeathBallMaxLifetime;
        }

        CacheRenderers();
        ApplySortInFront(hostSprite, definition != null ? definition.SortOrderInFrontOfHost : 1);
        DriveDeathAnimator(definition);
        ApplyFade(0f);
    }

    public void EnableCrystalRecall(float turnDistance)
    {
        crystalRecall = true;
        recallTurnDistance = Mathf.Max(0.1f, turnDistance);
        waitNotifyDistance = recallTurnDistance;
        // Stay fully visible for the crystal reform sequence.
        fadeDistance = float.MaxValue;
        maxLifetime = float.MaxValue;
        ApplyFade(0f);

        // If we already passed the turn distance, reverse immediately.
        if (phase == FlightPhase.Outbound && distanceTraveled >= recallTurnDistance)
            BeginReturn();
    }

    private void BeginReturn()
    {
        phase = FlightPhase.Returning;
        direction = -direction;
        if (!notifiedWait)
        {
            notifiedWait = true;
            if (ownerBurst != null)
                ownerBurst.NotifyBallReachedWaitDistance();
        }
    }

    private void CacheRenderers()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i] != null ? renderers[i].color : Color.white;
    }

    private void ApplySortInFront(SpriteRenderer hostSprite, int offset)
    {
        if (renderers == null || renderers.Length == 0)
            return;

        int layerId = hostSprite != null ? hostSprite.sortingLayerID : renderers[0].sortingLayerID;
        int order = (hostSprite != null ? hostSprite.sortingOrder : renderers[0].sortingOrder) + Mathf.Max(1, offset);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            renderers[i].sortingLayerID = layerId;
            renderers[i].sortingOrder = order;
        }
    }

    private void DriveDeathAnimator(GameVisualEffect definition)
    {
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (animator == null || definition == null)
            return;

        string boolName = definition.DeathBoolParameter;
        string triggerName = definition.DeathTriggerParameter;
        string stateName = definition.DeathAnimationStateName;

        if (VisualEffects.AnimatorHasParameter(animator, boolName))
            animator.SetBool(boolName, true);

        if (VisualEffects.AnimatorHasParameter(animator, triggerName))
        {
            animator.ResetTrigger(triggerName);
            animator.SetTrigger(triggerName);
        }

        // Always try the authored state name so empty/partial controllers still play the clip.
        if (!string.IsNullOrWhiteSpace(stateName))
            animator.Play(stateName, 0, 0f);
    }

    private void Update()
    {
        if (!running || phase == FlightPhase.Done)
            return;

        float dt = Time.deltaTime;
        age += dt;

        if (phase == FlightPhase.Outbound)
        {
            transform.position += (Vector3)(direction * speed * dt);
            distanceTraveled = Vector3.Distance(spawnPos, transform.position);

            if (!notifiedWait && distanceTraveled >= waitNotifyDistance)
            {
                notifiedWait = true;
                if (ownerBurst != null)
                    ownerBurst.NotifyBallReachedWaitDistance();
            }

            if (crystalRecall && distanceTraveled >= recallTurnDistance)
            {
                BeginReturn();
                return;
            }
        }
        else if (phase == FlightPhase.Returning)
        {
            transform.position += (Vector3)(direction * speed * dt);
            float distToCenter = Vector3.Distance(transform.position, spawnPos);
            // Passed the center or close enough — regrouped.
            if (distToCenter <= Mathf.Max(0.05f, speed * dt * 1.25f) ||
                Vector2.Dot(direction, (Vector2)(spawnPos - transform.position)) <= 0f)
            {
                transform.position = spawnPos;
                phase = FlightPhase.Done;
                running = false;
                ApplyFade(0f);
                if (ownerBurst != null)
                    ownerBurst.NotifyBallReturned();
                gameObject.SetActive(false);
                return;
            }
        }

        if (crystalRecall)
        {
            ApplyFade(0f);
            return;
        }

        float tDist = distanceTraveled / Mathf.Max(0.0001f, fadeDistance);
        float tTime = age / Mathf.Max(0.0001f, maxLifetime);
        float t = Mathf.Clamp01(Mathf.Max(tDist, tTime));
        ApplyFade(t);

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void ApplyFade(float t)
    {
        if (renderers == null)
            return;

        float alpha = 1f - t;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
                continue;

            Color c = baseColors[i];
            c.a = baseColors[i].a * alpha;
            sr.color = c;
        }
    }
}

/// <summary>
/// Runtime driver for Healed prefabs: rise in world space, fade the spark at 0.7 units,
/// and fade a green 80% aura to invisible over the same travel.
/// </summary>
public class HealedVisualEffect : MonoBehaviour
{
    private static Shader cachedAuraShader;

    private float speed = 1.4f;
    private float riseDistance = 0.7f;
    private float traveled;
    private bool running;
    private SpriteRenderer sparkRenderer;
    private SpriteRenderer auraRenderer;
    private Material auraMaterial;
    private Color sparkBaseColor = Color.white;
    private Color auraBaseColor = new Color(0.2f, 0.95f, 0.3f, 0.8f);

    public void Play(GameVisualEffect definition, float riseSpeed, float rise, int sortingLayerId, int sortingOrder)
    {
        speed = Mathf.Max(0.1f, riseSpeed);
        riseDistance = Mathf.Max(0.05f, rise);
        traveled = 0f;
        running = true;

        if (definition != null)
            auraBaseColor = definition.HealAuraColor;

        sparkRenderer = GetComponent<SpriteRenderer>();
        if (sparkRenderer == null)
            sparkRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (sparkRenderer != null)
        {
            sparkBaseColor = sparkRenderer.color;
            sparkRenderer.sortingLayerID = sortingLayerId;
            sparkRenderer.sortingOrder = sortingOrder + 1;
        }

        CreateAura(definition, sortingLayerId, sortingOrder);
        ApplyVisual(0f);
    }

    private void CreateAura(GameVisualEffect definition, int sortingLayerId, int sortingOrder)
    {
        if (sparkRenderer == null || sparkRenderer.sprite == null)
            return;

        float auraScale = definition != null ? Mathf.Max(1f, definition.HealAuraScale) : 1.22f;

        GameObject auraGo = new GameObject("HealedAura");
        auraGo.transform.SetParent(transform, false);
        auraGo.transform.localPosition = Vector3.zero;
        auraGo.transform.localRotation = Quaternion.identity;
        auraGo.transform.localScale = Vector3.one * auraScale;
        auraGo.transform.SetAsFirstSibling();

        auraRenderer = auraGo.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = sparkRenderer.sprite;
        auraRenderer.flipX = sparkRenderer.flipX;
        auraRenderer.flipY = sparkRenderer.flipY;
        auraRenderer.sortingLayerID = sortingLayerId;
        auraRenderer.sortingOrder = sortingOrder;

        if (cachedAuraShader == null)
            cachedAuraShader = Shader.Find("Gameoverse/SpriteSolidColor");

        if (cachedAuraShader != null)
        {
            auraMaterial = new Material(cachedAuraShader);
            auraRenderer.sharedMaterial = auraMaterial;
        }

        auraRenderer.color = auraBaseColor;
    }

    private void Update()
    {
        if (!running)
            return;

        float dt = Time.deltaTime;
        float step = speed * dt;
        transform.position += Vector3.up * step;
        traveled += step;

        float t = Mathf.Clamp01(traveled / riseDistance);
        ApplyVisual(t);

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void ApplyVisual(float t)
    {
        // Spark stays visible, then fades out as it reaches 0.7 spaces up.
        float sparkFade = t < 0.75f ? 0f : Mathf.InverseLerp(0.75f, 1f, t);
        if (sparkRenderer != null)
        {
            Color c = sparkBaseColor;
            c.a = sparkBaseColor.a * (1f - sparkFade);
            sparkRenderer.color = c;
        }

        if (auraRenderer != null)
        {
            Color c = auraBaseColor;
            c.a = auraBaseColor.a * (1f - t);
            auraRenderer.color = c;
        }
    }

    private void OnDestroy()
    {
        VisualEffects.UnregisterHealed(this);
        if (auraMaterial != null)
            Destroy(auraMaterial);
    }
}

/// <summary>
/// Runtime driver for Enemy Explosion Small Purple: plays the authored clip once, then destroys itself.
/// </summary>
public class EnemyExplosionSmallPurpleEffect : MonoBehaviour
{
    private Animator animator;
    private SpriteRenderer[] renderers;
    private float lifetime;
    private float timer;
    private bool running;

    public void Play(string animationStateName, float maxLifetime, int sortLayerId, int sortOrder)
    {
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
                continue;

            sr.sortingLayerID = sortLayerId;
            sr.sortingOrder = sortOrder;
        }

        lifetime = Mathf.Max(0.05f, maxLifetime);
        if (animator != null && !string.IsNullOrWhiteSpace(animationStateName))
        {
            animator.Play(animationStateName, 0, 0f);
            lifetime = ResolveClipLength(animator, animationStateName, lifetime);
        }

        timer = 0f;
        running = true;
    }

    private void Update()
    {
        if (!running)
            return;

        timer += Time.deltaTime;
        if (timer >= lifetime)
            Destroy(gameObject);
    }

    private static float ResolveClipLength(Animator anim, string stateName, float fallback)
    {
        if (anim == null || string.IsNullOrWhiteSpace(stateName))
            return fallback;

        int hash = Animator.StringToHash(stateName);
        if (anim.HasState(0, hash))
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.length > 0.01f)
                return info.length;
        }

        RuntimeAnimatorController controller = anim.runtimeAnimatorController;
        if (controller == null)
            return fallback;

        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && clip.name == stateName)
                return Mathf.Max(clip.length, fallback);
        }

        return fallback;
    }
}

/// <summary>
/// Short one-shot hit spark for Laser / Tanker / ScrapNit: visible briefly then destroyed.
/// </summary>
public class BriefHitSparkEffect : MonoBehaviour
{
    private int hostId;
    private float lifetime = 0.15f;
    private float timer;
    private Transform snapAnchor;
    private bool running;

    public void Initialize(int hostInstanceId, float duration, Transform worldSnapAnchor)
    {
        hostId = hostInstanceId;
        snapAnchor = worldSnapAnchor;
        Restart(duration);
    }

    public void Restart(float duration)
    {
        lifetime = Mathf.Max(0.05f, duration);
        timer = 0f;
        running = true;
        gameObject.SetActive(true);

        Animator animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    private void LateUpdate()
    {
        if (!running)
            return;

        if (snapAnchor != null)
            transform.position = snapAnchor.position;

        timer += Time.deltaTime;
        if (timer < lifetime)
            return;

        running = false;
        VisualEffects.UnregisterBriefHitSpark(this, hostId);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        VisualEffects.UnregisterBriefHitSpark(this, hostId);
    }
}

/// <summary>
/// Blocker Bot hit spark: shoots sideways, rises (slowing), then falls while fading.
/// </summary>
public class BlockerBotHitSparkEffect : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;
    private Vector3 start;
    private float sideOffset;
    private float startDelay;
    private float timer;
    private bool running;
    private bool started;

    private const float RiseDistance = 1.85f;
    private const float FallDistance = 3.25f;
    private const float RiseDuration = 0.32f;
    private const float FallDuration = 0.45f;

    public void Play(Vector3 origin, float horizontalOffset)
    {
        Play(origin, horizontalOffset, 0f);
    }

    public void Play(Vector3 origin, float horizontalOffset, float delay)
    {
        start = origin;
        sideOffset = horizontalOffset;
        startDelay = Mathf.Max(0f, delay);
        timer = 0f;
        running = true;
        started = startDelay <= 0f;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            if (!started)
            {
                Color c = baseColor;
                c.a = 0f;
                spriteRenderer.color = c;
            }
        }

        transform.position = origin;
    }

    private void Update()
    {
        if (!running)
            return;

        timer += Time.deltaTime;

        if (!started)
        {
            if (timer < startDelay)
                return;

            started = true;
            timer = 0f;
            ApplyAlpha(1f);
        }

        float riseT = Mathf.Clamp01(timer / RiseDuration);

        if (timer <= RiseDuration)
        {
            // Ease-out rise (slows near the top).
            float riseEase = 1f - (1f - riseT) * (1f - riseT);
            float x = Mathf.Lerp(0f, sideOffset, riseEase);
            float y = RiseDistance * riseEase;
            transform.position = start + new Vector3(x, y, 0f);
            ApplyAlpha(1f);
            return;
        }

        float fallTimer = timer - RiseDuration;
        float fallT = Mathf.Clamp01(fallTimer / FallDuration);
        // Ease-in fall (picks up speed).
        float fallEase = fallT * fallT;
        float yFall = RiseDistance - FallDistance * fallEase;
        transform.position = start + new Vector3(sideOffset, yFall, 0f);

        // Fade faster near the end of the fall.
        float fade = fallT * fallT;
        ApplyAlpha(1f - fade);

        if (fallT >= 1f)
            Destroy(gameObject);
    }

    private void ApplyAlpha(float alpha)
    {
        if (spriteRenderer == null)
            return;

        Color c = baseColor;
        c.a = baseColor.a * Mathf.Clamp01(alpha);
        spriteRenderer.color = c;
    }
}

/// <summary>
/// CheckPoint Bot confetti spark: flies outward at speed, eases into maxHeight, then falls slowly while fading.
/// </summary>
public class CheckPointBotConfettiSparkEffect : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;
    private Vector3 start;
    private Vector2 velocity;
    private float maxHeight = 2f;
    private float lifetime = 1.5f;
    private float timer;
    private bool falling;
    private bool running;

    public void Play(Vector3 origin, Vector2 direction, float speed, float height, float life)
    {
        start = origin;
        maxHeight = Mathf.Max(0.1f, height);
        lifetime = Mathf.Max(0.1f, life);
        timer = 0f;
        falling = false;
        running = true;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
        velocity = dir * Mathf.Max(0.1f, speed);

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;

        transform.position = origin;
    }

    private void Update()
    {
        if (!running)
            return;

        float dt = Time.deltaTime;
        timer += dt;

        if (!falling)
        {
            // Decelerate vertical as we approach max height (ease into the peak).
            float heightNow = transform.position.y - start.y;
            float remain = Mathf.Max(0f, maxHeight - heightNow);
            float heightT = 1f - Mathf.Clamp01(remain / maxHeight);

            // Gravity tuned so a straight-up shot at this speed peaks near maxHeight.
            float gravity = (velocity.y > 0.01f)
                ? (velocity.y * velocity.y) / (2f * maxHeight)
                : 16f;

            // Stronger drag near the top so they "slow down as they reach max height".
            float drag = Mathf.Lerp(1f, 0.25f, heightT * heightT);
            velocity.y -= gravity * dt;
            velocity.x *= Mathf.Lerp(1f, 0.92f, dt * 8f);

            Vector3 next = transform.position + (Vector3)(velocity * drag * dt);
            if (next.y >= start.y + maxHeight)
            {
                next.y = start.y + maxHeight;
                velocity.y = 0f;
                falling = true;
            }
            else if (velocity.y <= 0f && heightNow > 0.05f)
            {
                falling = true;
            }

            transform.position = next;
        }
        else
        {
            // Slow fall after the peak.
            velocity.y -= 4.5f * dt;
            velocity.x *= Mathf.Lerp(1f, 0.9f, dt * 6f);
            transform.position += (Vector3)(velocity * dt);
        }

        float lifeT = Mathf.Clamp01(timer / lifetime);
        // Hold solid early, then fade through the fall / end of life.
        float alpha = lifeT < 0.35f ? 1f : 1f - Mathf.InverseLerp(0.35f, 1f, lifeT);
        ApplyAlpha(alpha);

        if (timer >= lifetime)
            Destroy(gameObject);
    }

    private void ApplyAlpha(float alpha)
    {
        if (spriteRenderer == null)
            return;

        Color c = baseColor;
        c.a = baseColor.a * Mathf.Clamp01(alpha);
        spriteRenderer.color = c;
    }
}

/// <summary>
/// Blocker Bot death smoke particle: floats upward and fades (Contract Rush-style puffs).
/// </summary>
public class BlockerSmokeParticleEffect : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;
    private Vector3 start;
    private float riseDistance = 4.5f;
    private float riseSpeed = 3.2f;
    private float traveled;
    private bool running;

    public void Play(float distance, float speed)
    {
        start = transform.position;
        riseDistance = Mathf.Max(0.1f, distance);
        riseSpeed = Mathf.Max(0.1f, speed);
        traveled = 0f;
        running = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
    }

    private void Update()
    {
        if (!running)
            return;

        float step = riseSpeed * Time.deltaTime;
        traveled += step;
        float t = Mathf.Clamp01(traveled / riseDistance);

        // Slight ease so the rise feels smooth rather than linear-snappy.
        float ease = 1f - (1f - t) * (1f - t);
        transform.position = start + Vector3.up * (riseDistance * ease);

        if (spriteRenderer != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * (1f - t);
            spriteRenderer.color = c;
        }

        if (t >= 1f)
            Destroy(gameObject);
    }
}
