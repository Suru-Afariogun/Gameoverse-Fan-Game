using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared cinematic helpers for crystal near-death full heals and Mega Man 11-style boss deaths.
/// </summary>
public static class BossCinematics
{
    public const float NearDeathBarFillSeconds = 0.4f;
    public const float DeathFloatSpaces = 3f;
    public const float DeathFallTimeout = 2.5f;
    public const float DeathFloatSeconds = 0.85f;
    public const float DeathExplodeHoldSeconds = 0.35f;
    public const int DeathExplosionWaves = 10;
    public const int DeathExplosionsPerWave = 4;
    public const float DeathWaveSeconds = 0.22f;
    public const float SpaceSize = 1f;

    private static readonly Color BrightDeathRed = new Color(1f, 0.12f, 0.08f, 1f);

    public static IEnumerator NearDeathFullHeal(Boss boss, int lifeBarSegments)
    {
        if (boss == null)
            yield break;

        int bars = Mathf.Max(1, lifeBarSegments);
        float perBar = Mathf.Max(0.1f, NearDeathBarFillSeconds);

        // Gather waves of death-energy orbs into the body while bars refill.
        Coroutine gather = boss.StartCoroutine(PlayEnergyGatherWaves(boss, bars * perBar + 0.35f));

        for (int i = 1; i <= bars; i++)
        {
            float ratio = i / (float)bars;
            int target = Mathf.Max(1, Mathf.RoundToInt(boss.MaxHealth * ratio));
            boss.ApplyCinematicHealth(target);
            yield return new WaitForSecondsRealtime(perBar);
        }

        boss.ApplyCinematicHealth(boss.MaxHealth);

        if (gather != null)
            boss.StopCoroutine(gather);
    }

    public static IEnumerator MegaManStyleDeath(Boss boss, GameVisualEffect[] explosionPrefabs)
    {
        if (boss == null)
            yield break;

        if (explosionPrefabs == null || explosionPrefabs.Length == 0)
            explosionPrefabs = VisualEffects.ResolveEnemyExplosionSmallPurplePrefabs();

        // Cache spark origin before float so waves stay locked to the spark spawner.
        boss.ResolveSparkSpawnPoint();

        Rigidbody2D rb = boss.Body;
        Transform t = boss.transform;

        // Fall to ground while stunned.
        float fallTimer = 0f;
        while (fallTimer < DeathFallTimeout)
        {
            fallTimer += Time.unscaledDeltaTime;
            if (boss.IsGrounded)
                break;
            yield return null;
        }

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Float up 3 spaces.
        float startY = t.position.y;
        float endY = startY + DeathFloatSpaces * SpaceSize;
        float floatT = 0f;
        while (floatT < DeathFloatSeconds)
        {
            floatT += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(floatT / DeathFloatSeconds);
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            Vector3 pos = t.position;
            pos.y = Mathf.Lerp(startY, endY, eased);
            t.position = pos;
            if (rb != null)
            {
                rb.position = pos;
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = 0f;
            }

            yield return null;
        }

        // Fade body sprites to bright red while explosions + Hex sparks rain.
        SpriteRenderer[] bodySprites = GatherBodySprites(boss);
        Color[] originals = new Color[bodySprites.Length];
        for (int i = 0; i < bodySprites.Length; i++)
            originals[i] = bodySprites[i] != null ? bodySprites[i].color : Color.white;

        SortingGroup sortGroup = boss.EffectSortingGroup;
        SpriteRenderer sortBody = boss.GetComponent<SpriteRenderer>();
        Transform sparkAnchor = boss.ResolveSparkSpawnPoint();
        Vector3 sparkOrigin = sparkAnchor != null ? sparkAnchor.position : boss.transform.position;

        // Opening spark burst as the explode phase begins.
        HexSparkEmitter.BurstRandomDirections(
            sparkOrigin,
            Random.Range(10, 19),
            sortGroup,
            sortBody,
            useUnscaledTime: true);

        float explodeWindow = DeathExplosionWaves * DeathWaveSeconds + DeathExplodeHoldSeconds;
        float tintT = 0f;
        int wave = 0;
        float waveTimer = 0f;

        while (wave < DeathExplosionWaves || tintT < explodeWindow)
        {
            float dt = Time.unscaledDeltaTime;
            tintT += dt;
            waveTimer += dt;

            float u = Mathf.Clamp01(tintT / Mathf.Max(0.1f, explodeWindow * 0.85f));
            for (int i = 0; i < bodySprites.Length; i++)
            {
                if (bodySprites[i] == null)
                    continue;
                Color from = originals[i];
                Color to = BrightDeathRed;
                to.a = from.a;
                bodySprites[i].color = Color.Lerp(from, to, u);
            }

            if (wave < DeathExplosionWaves && waveTimer >= DeathWaveSeconds)
            {
                waveTimer = 0f;
                sparkOrigin = sparkAnchor != null ? sparkAnchor.position : boss.transform.position;
                SpawnExplosionWave(boss, explosionPrefabs, DeathExplosionsPerWave);
                HexSparkEmitter.BurstRandomDirections(
                    sparkOrigin,
                    Random.Range(4, 11),
                    sortGroup,
                    sortBody,
                    useUnscaledTime: true);
                wave++;
            }

            yield return null;
        }

        // Final burst into death energy balls (hides host).
        VisualEffects.PlayDeathEnergyBalls(boss.DeathEnergyBallPrefab, boss, hideHost: true);
    }

    private static SpriteRenderer[] GatherBodySprites(Boss boss)
    {
        if (boss == null)
            return System.Array.Empty<SpriteRenderer>();

        SpriteRenderer[] all = boss.GetComponentsInChildren<SpriteRenderer>(true);
        var kept = new System.Collections.Generic.List<SpriteRenderer>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer sr = all[i];
            if (sr == null)
                continue;
            // Skip afterimage / aura helpers that live as sibling VFX under the boss.
            string n = sr.gameObject.name;
            if (n != null &&
                (n.IndexOf("Afterimage", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 n.IndexOf("Aura", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 n.IndexOf("Spark", System.StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            kept.Add(sr);
        }

        return kept.ToArray();
    }

    private static void SpawnExplosionWave(Boss boss, GameVisualEffect[] prefabs, int count)
    {
        if (boss == null)
            return;

        if (prefabs == null || prefabs.Length == 0)
            prefabs = VisualEffects.ResolveEnemyExplosionSmallPurplePrefabs();
        if (prefabs == null || prefabs.Length == 0)
            return;

        Transform anchor = boss.ResolveSparkSpawnPoint();
        Vector3 origin = anchor != null ? anchor.position : boss.transform.position;

        // Spread relative to body size so waves fill the silhouette around the spark origin.
        Bounds bounds = boss.GetComponent<Collider2D>() != null
            ? boss.GetComponent<Collider2D>().bounds
            : new Bounds(origin, Vector3.one * 1.2f);
        float spreadX = Mathf.Max(0.85f, bounds.extents.x + 0.55f);
        float spreadY = Mathf.Max(0.7f, bounds.extents.y + 0.45f);

        SpriteRenderer sort = boss.GetComponent<SpriteRenderer>();
        for (int i = 0; i < count; i++)
        {
            GameVisualEffect prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null)
                continue;

            Vector3 offset = new Vector3(
                Random.Range(-spreadX, spreadX),
                Random.Range(-spreadY * 0.65f, spreadY),
                0f);
            VisualEffects.SpawnEnemyExplosionSmallPurple(
                prefab,
                origin + offset,
                sort,
                useUnscaledTime: true);
            SoundManager.Instance?.PlayEnemyExplosion();
        }
    }

    private static IEnumerator PlayEnergyGatherWaves(Boss boss, float duration)
    {
        if (boss == null || boss.DeathEnergyBallPrefab == null)
            yield break;

        float elapsed = 0f;
        float nextWave = 0f;
        while (elapsed < duration)
        {
            if (elapsed >= nextWave)
            {
                nextWave = elapsed + 0.45f;
                boss.StartCoroutine(SpawnGatherRing(boss, 8, 0.55f));
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static IEnumerator SpawnGatherRing(Boss boss, int count, float travelSeconds)
    {
        GameVisualEffect prefab = boss.DeathEnergyBallPrefab;
        if (prefab == null)
            yield break;

        Vector3 center = boss.transform.position;
        Collider2D body = boss.GetComponent<Collider2D>();
        if (body != null)
            center = body.bounds.center;

        float radius = 2.4f;
        var orbs = new GameObject[count];
        var starts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float ang = (360f * i / count) * Mathf.Deg2Rad;
            starts[i] = center + new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            GameObject go = Object.Instantiate(prefab.gameObject, starts[i], Quaternion.identity);
            go.SetActive(true);
            // Disable heavy drivers — we only want the look (SpriteRenderer is not a MonoBehaviour).
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null)
                    mb.enabled = false;
            }

            orbs[i] = go;
        }

        float t = 0f;
        while (t < travelSeconds)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / travelSeconds);
            float fade = 1f - u * u;
            for (int i = 0; i < count; i++)
            {
                if (orbs[i] == null)
                    continue;
                orbs[i].transform.position = Vector3.Lerp(starts[i], center, u * u);
                SpriteRenderer sr = orbs[i].GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    Color c = sr.color;
                    c.a = fade;
                    sr.color = c;
                }
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (orbs[i] != null)
                Object.Destroy(orbs[i]);
        }
    }
}
