using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Heat Flare Stack pillar — ticks 1 damage / 0.5s, blocked by terrain, phases through combatants.
/// After 2s, explodes into 5 remnant small heat flares.
/// Heat Dash trail stacks fall under gravity until they hit the ground.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BassHeatFlareStack : MonoBehaviour
{
    [SerializeField] private float damage = 1f;
    [SerializeField] private float tickInterval = 0.5f;
    [SerializeField] private float lifetime = 2f;
    [SerializeField] private BassSmallHeatFlare remnantFlarePrefab;
    [SerializeField] private int remnantCount = 5;
    [SerializeField] private float remnantBurstSpeed = 4f;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float fallGravity = 28f;
    [SerializeField] private float fallCastRadius = 0.14f;
    [SerializeField] private float maxFallDistance = 40f;

    private Collider2D hitCollider;
    private SpriteRenderer spriteRenderer;
    private SortingGroup hostGroup;
    private SpriteRenderer hostBody;
    private float age;
    private float tickTimer;
    private bool active;
    private bool falling;
    private float fallVelocityY;
    private float damageMultiplier = 1f;
    private bool hostileToPlayer;
    private Transform owner;
    private System.Func<int, bool> trySpendAmmo;
    private readonly Collider2D[] overlaps = new Collider2D[16];
    private readonly RaycastHit2D[] casts = new RaycastHit2D[8];
    private readonly System.Collections.Generic.Dictionary<int, float> nextHitAt =
        new System.Collections.Generic.Dictionary<int, float>(16);

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }

        if (groundLayers.value == 0)
        {
            int g = LayerMask.NameToLayer("Ground");
            int d = LayerMask.NameToLayer("Default");
            int mask = 0;
            if (g >= 0) mask |= 1 << g;
            if (d >= 0) mask |= 1 << d;
            groundLayers = mask != 0 ? (LayerMask)mask : ~0;
        }
    }

    public void ConfigureHostSorting(SortingGroup group, SpriteRenderer body)
    {
        hostGroup = group;
        hostBody = body;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    public void SetRemnantPrefab(BassSmallHeatFlare remnant)
    {
        if (remnant != null)
            remnantFlarePrefab = remnant;
    }

    /// <summary>Align stack bottom with the landing small flare's bottom.</summary>
    public void BeginFromFlare(Transform flare, Transform shotOwner, System.Func<int, bool> spendAmmo)
    {
        owner = shotOwner;
        trySpendAmmo = spendAmmo;
        active = true;
        falling = false;
        fallVelocityY = 0f;
        age = 0f;
        tickTimer = 0f;

        if (flare != null && spriteRenderer != null)
        {
            SpriteRenderer flareSr = flare.GetComponent<SpriteRenderer>();
            float flareBottom = flareSr != null
                ? flareSr.bounds.min.y
                : flare.position.y;
            float stackHalf = spriteRenderer.bounds.extents.y;
            Vector3 p = transform.position;
            p.x = flare.position.x;
            p.y = flareBottom + stackHalf;
            transform.position = p;
        }

        PhaseThroughCombatants(true);
    }

    /// <summary>
    /// Activate at a world position without a landing flare (Heat Dash trail).
    /// Spawns mid-air then falls until the bottom rests on solid ground.
    /// </summary>
    public void BeginAtPosition(Vector3 worldBottomCenter, Transform shotOwner, System.Func<int, bool> spendAmmo)
    {
        owner = shotOwner;
        trySpendAmmo = spendAmmo;
        active = true;
        falling = true;
        fallVelocityY = 0f;
        age = 0f;
        tickTimer = 0f;

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        Vector3 p = worldBottomCenter;
        transform.position = p;
        if (spriteRenderer != null)
        {
            p.y = worldBottomCenter.y + spriteRenderer.bounds.extents.y;
            transform.position = p;
        }

        PhaseThroughCombatants(true);
        // If already overlapping / just above ground, settle immediately.
        TrySnapToGroundBelow(maxFallDistance);
    }

    /// <summary>Boss Bass stacks tick on the player instead of bosses / enemies.</summary>
    public void SetHostile(bool hostile)
    {
        hostileToPlayer = hostile;
    }

    /// <summary>Attack Style level 5 etc. — scales tick damage.</summary>
    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = Mathf.Max(0f, multiplier);
    }

    private void Update()
    {
        if (!active)
            return;

        float dt = Time.deltaTime;
        age += dt;
        tickTimer += dt;

        if (falling)
            TickFall(dt);

        if (tickTimer >= tickInterval)
        {
            tickTimer = 0f;
            TickDamage();
        }

        if (!falling)
            ResolveTerrainBlock();

        if (age >= lifetime)
            ExplodeIntoRemnants();
    }

    private void TickFall(float dt)
    {
        fallVelocityY -= Mathf.Max(0.1f, fallGravity) * dt;
        Vector2 start = transform.position;
        // Cast from near the bottom of the stack so the pillar sits on the floor.
        float halfY = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.4f;
        Vector2 feet = start + Vector2.down * Mathf.Max(0.05f, halfY - fallCastRadius);
        Vector2 delta = new Vector2(0f, fallVelocityY * dt);
        if (delta.y >= -0.0001f)
            delta.y = -0.01f;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundLayers);

        float dist = Mathf.Abs(delta.y);
        int count = Physics2D.CircleCast(feet, fallCastRadius, Vector2.down, filter, casts, dist + 0.05f);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = casts[i];
            if (!IsSolidGround(hit.collider))
                continue;

            // Land with stack bottom on the hit surface.
            float landY = hit.point.y + halfY;
            Vector3 p = transform.position;
            p.y = landY;
            transform.position = p;
            falling = false;
            fallVelocityY = 0f;
            return;
        }

        transform.position = start + delta;

        // Safety: if we fell forever, snap or destroy.
        if (transform.position.y < -200f)
            Destroy(gameObject);
    }

    private void TrySnapToGroundBelow(float maxDist)
    {
        float halfY = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.4f;
        Vector2 origin = (Vector2)transform.position + Vector2.down * Mathf.Max(0.05f, halfY - fallCastRadius);

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundLayers);

        int count = Physics2D.CircleCast(origin, fallCastRadius, Vector2.down, filter, casts, Mathf.Max(0.5f, maxDist));
        float best = float.MaxValue;
        RaycastHit2D bestHit = default;
        for (int i = 0; i < count; i++)
        {
            if (!IsSolidGround(casts[i].collider))
                continue;
            if (casts[i].distance < best)
            {
                best = casts[i].distance;
                bestHit = casts[i];
            }
        }

        // Only snap if ground is very close (already nearly landed).
        if (bestHit.collider != null && bestHit.distance <= 0.35f)
        {
            Vector3 p = transform.position;
            p.y = bestHit.point.y + halfY;
            transform.position = p;
            falling = false;
            fallVelocityY = 0f;
        }
    }

    private bool IsSolidGround(Collider2D col)
    {
        if (col == null || col.isTrigger)
            return false;
        if (col.transform.IsChildOf(transform))
            return false;
        if (owner != null && col.transform.IsChildOf(owner))
            return false;
        if (col.GetComponentInParent<ICommonEnemy>() != null)
            return false;
        if (col.GetComponentInParent<Boss>() != null)
            return false;
        if (col.GetComponentInParent<PlayerController>() != null)
            return false;
        return true;
    }

    private void TickDamage()
    {
        if (hitCollider == null)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        int count = Physics2D.OverlapCollider(hitCollider, filter, overlaps);
        float now = Time.time;
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlaps[i];
            if (other == null)
                continue;

            Component key = hostileToPlayer
                ? other.GetComponentInParent<PlayerController>() as Component
                : other.GetComponentInParent<Boss>() as Component
                    ?? other.GetComponentInParent<ICommonEnemy>() as Component;
            if (key == null)
                continue;

            int id = key.GetInstanceID();
            if (nextHitAt.TryGetValue(id, out float ready) && now < ready)
                continue;
            nextHitAt[id] = now + tickInterval;
            BassCrashHeatCombat.ApplyDamageOnly(other, damage * damageMultiplier, false, hostileToPlayer, transform);
        }
    }

    private void ResolveTerrainBlock()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundLayers);
        int count = Physics2D.OverlapCircle(transform.position, 0.2f, filter, overlaps);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlaps[i];
            if (!IsSolidGround(col))
                continue;

            Vector2 closest = col.ClosestPoint(transform.position);
            Vector2 away = (Vector2)transform.position - closest;
            if (away.sqrMagnitude < 0.0001f)
                continue;
            float push = 0.15f - away.magnitude;
            if (push > 0f)
                transform.position += (Vector3)(away.normalized * push);
        }
    }

    private void PhaseThroughCombatants(bool ignore)
    {
        if (hitCollider == null)
            return;

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) continue;
            Collider2D[] cols = players[i].GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] != null && !cols[c].isTrigger)
                    Physics2D.IgnoreCollision(hitCollider, cols[c], ignore);
            }
        }
    }

    private void ExplodeIntoRemnants()
    {
        active = false;
        falling = false;
        if (remnantFlarePrefab != null)
        {
            int n = Mathf.Max(1, remnantCount);
            for (int i = 0; i < n; i++)
            {
                float angle = (Mathf.PI * 2f * i) / n + Random.Range(-0.15f, 0.15f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                BassSmallHeatFlare remnant = Instantiate(
                    remnantFlarePrefab,
                    transform.position + (Vector3)(dir * 0.15f),
                    Quaternion.identity);
                remnant.ConfigureHostSorting(hostGroup, hostBody);
                remnant.LaunchRemnant(dir * remnantBurstSpeed + Vector2.up * Random.Range(1f, 2.5f));
            }
        }

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        PhaseThroughCombatants(false);
    }
}
