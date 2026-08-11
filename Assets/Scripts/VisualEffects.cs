using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static helpers for spawning VisualEffect prefabs and tracking active stunned FX.
/// Prefabs are assigned on characters/bosses (like projectile prefabs), not on a central list.
/// </summary>
public static class VisualEffects
{
    private static readonly Dictionary<int, DefaultStunnedEffect> ActiveStunnedByHostId =
        new Dictionary<int, DefaultStunnedEffect>();

    /// <summary>
    /// Spawn a Buster Blast from the prefab you assigned on Kit/Boss Kit.
    /// Parented to FirePoint; preserves prefab size/offset and scales to match the projectile.
    /// </summary>
    public static void SpawnBusterBlast(VisualEffect prefab, Transform firePoint, Projectile projectile)
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

        VisualEffect instance = go.GetComponent<VisualEffect>();
        BusterBlastEffect driver = go.GetComponent<BusterBlastEffect>();
        if (driver == null)
            driver = go.AddComponent<BusterBlastEffect>();

        driver.Play(
            projectile,
            instance.MaxTravelDistance,
            instance.MaxLifetime,
            instance.MaxEndScale,
            instance.BlastColor,
            instance.TintBlastSprites,
            startLocalScale);
    }

    /// <summary>
    /// Pick small prefab for small shots; medium/big prefab for medium and big (same art, scaled up for big).
    /// </summary>
    public static VisualEffect ResolveBusterBlastPrefab(
        ProjectileShotType shotType,
        VisualEffect smallPrefab,
        VisualEffect mediumBigPrefab)
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
    public static float GetBusterBlastVisualSize(VisualEffect prefab)
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
    public static void PlayStunned(VisualEffect prefab, Component host)
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
            existing.Refresh();
            return;
        }

        Collider2D body = FindBodyCollider(host.gameObject);
        Vector3 worldCenter = body != null ? body.bounds.center : host.transform.position;

        GameObject go = Object.Instantiate(
            prefab.gameObject,
            worldCenter,
            Quaternion.identity,
            host.transform);

        VisualEffect instance = go.GetComponent<VisualEffect>();
        DefaultStunnedEffect effect = go.GetComponent<DefaultStunnedEffect>();
        if (effect == null)
            effect = go.AddComponent<DefaultStunnedEffect>();

        effect.Initialize(host.transform, body, instance);
        ActiveStunnedByHostId[id] = effect;
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

            if (!c.isTrigger)
                return c;

            if (fallback == null)
                fallback = c;
        }

        return fallback;
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
        VisualEffect prefab,
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
    /// (defaults to the prefab's Death Ball Wait Travel Distance, usually 3).
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
    private Collider2D bodyCollider;
    private Animator animator;
    private string isStunnedBoolParameter = "IsStunned";
    private string stunnedTriggerParameter = "stunned";
    private int sortOrderOffset = 1;
    private SpriteRenderer[] effectRenderers;
    private int hostId;

    public void Initialize(Transform hostTransform, Collider2D body, VisualEffect definition)
    {
        host = hostTransform;
        bodyCollider = body;
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

        Vector3 world = bodyCollider != null ? bodyCollider.bounds.center : host.position;
        transform.position = world;
    }

    private void DriveStunnedAnimator(bool restart)
    {
        if (animator == null)
            return;

        if (!string.IsNullOrEmpty(isStunnedBoolParameter))
            animator.SetBool(isStunnedBoolParameter, true);

        if (!string.IsNullOrEmpty(stunnedTriggerParameter))
        {
            animator.ResetTrigger(stunnedTriggerParameter);
            if (restart)
                animator.SetTrigger(stunnedTriggerParameter);
        }
    }

    private void ClearStunnedAnimator()
    {
        if (animator == null || string.IsNullOrEmpty(isStunnedBoolParameter))
            return;

        animator.SetBool(isStunnedBoolParameter, false);
    }
}

/// <summary>
/// Spawns Mega Man-style death energy balls in an equal circle and tracks travel for fade waits.
/// </summary>
public class DeathEnergyBallBurst : MonoBehaviour
{
    private readonly System.Collections.Generic.List<DeathEnergyBallEffect> balls =
        new System.Collections.Generic.List<DeathEnergyBallEffect>(8);

    private float waitTravelDistance = 3f;
    private int ballCount;
    private int ballsPastWaitDistance;

    public float WaitTravelDistance => waitTravelDistance;
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

    public void Begin(VisualEffect prefab, Vector3 center, Component hostComponent)
    {
        waitTravelDistance = prefab.DeathBallWaitTravelDistance;
        ballCount = Mathf.Max(1, prefab.DeathBallCount);
        ballsPastWaitDistance = 0;

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

            VisualEffect instance = go.GetComponent<VisualEffect>();
            DeathEnergyBallEffect ball = go.GetComponent<DeathEnergyBallEffect>();
            if (ball == null)
                ball = go.AddComponent<DeathEnergyBallEffect>();

            ball.Play(dir, instance, hostSprite, this, waitTravelDistance);
            balls.Add(ball);
        }

        Destroy(gameObject, Mathf.Max(prefab.DeathBallMaxLifetime, 3f) + 1f);
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
/// </summary>
public class DeathEnergyBallEffect : MonoBehaviour
{
    private Vector2 direction = Vector2.right;
    private Vector3 spawnPos;
    private float speed = 8f;
    private float fadeDistance = 6f;
    private float maxLifetime = 2.5f;
    private float waitNotifyDistance = 3f;
    private float age;
    private float distanceTraveled;
    private bool notifiedWait;
    private DeathEnergyBallBurst ownerBurst;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Animator animator;
    private bool running;

    public float DistanceTraveled => distanceTraveled;

    public void Play(
        Vector2 dir,
        VisualEffect definition,
        SpriteRenderer hostSprite,
        DeathEnergyBallBurst burst,
        float waitDistance)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        spawnPos = transform.position;
        age = 0f;
        distanceTraveled = 0f;
        notifiedWait = false;
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

    private void DriveDeathAnimator(VisualEffect definition)
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
        if (!running)
            return;

        float dt = Time.deltaTime;
        age += dt;
        transform.position += (Vector3)(direction * speed * dt);
        distanceTraveled = Vector3.Distance(spawnPos, transform.position);

        if (!notifiedWait && distanceTraveled >= waitNotifyDistance)
        {
            notifiedWait = true;
            if (ownerBurst != null)
                ownerBurst.NotifyBallReachedWaitDistance();
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
