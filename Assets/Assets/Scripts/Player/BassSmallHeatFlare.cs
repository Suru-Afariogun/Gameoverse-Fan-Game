using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Small Heat Flare — stack-spawning arc flare, or remnant (shrink+fade on ground, no stack).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BassSmallHeatFlare : MonoBehaviour
{
    public enum Mode
    {
        StackSpawner,
        Remnant
    }

    [SerializeField] private float damage = 0.5f;
    [SerializeField] private float gravity = 22f;
    [SerializeField] private float remnantShrinkSeconds = 0.35f;
    [SerializeField] private BassHeatFlareStack stackPrefab;
    [SerializeField] private LayerMask groundLayers;

    private Mode mode = Mode.StackSpawner;
    private Rigidbody2D rb;
    private Collider2D hitCollider;
    private SpriteRenderer spriteRenderer;
    private Vector2 velocity;
    private bool launched;
    private bool landed;
    private bool remnantFading;
    private float remnantTimer;
    private Vector3 remnantStartScale;
    private float spaceSize = 1f;
    private System.Func<int, bool> trySpendAmmo;
    private int stackAmmoCost = 1;
    private Transform owner;
    private BassSmallHeatFlare remnantPrefabSource;
    private SortingGroup hostGroup;
    private SpriteRenderer hostBody;
    private float stackDamageMultiplier = 1f;
    private bool hostileToPlayer;
    private readonly RaycastHit2D[] casts = new RaycastHit2D[6];

    public void ConfigureHostSorting(SortingGroup group, SpriteRenderer body)
    {
        hostGroup = group;
        hostBody = body;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
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

    public void LaunchStackSpawner(
        Vector2 initialVelocity,
        Vector3 targetLandWorld,
        BassHeatFlareStack stackTemplate,
        BassSmallHeatFlare remnantTemplate,
        float worldSpace,
        System.Func<int, bool> spendAmmo,
        int stackCost,
        Transform shotOwner)
    {
        mode = Mode.StackSpawner;
        stackPrefab = stackTemplate != null ? stackTemplate : stackPrefab;
        remnantPrefabSource = remnantTemplate;
        spaceSize = worldSpace;
        trySpendAmmo = spendAmmo;
        stackAmmoCost = Mathf.Max(0, stackCost);
        owner = shotOwner;
        velocity = initialVelocity;
        launched = true;
        landed = false;
        _ = targetLandWorld;
    }

    public void LaunchRemnant(Vector2 initialVelocity)
    {
        mode = Mode.Remnant;
        velocity = initialVelocity;
        launched = true;
        landed = false;
        remnantFading = false;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    /// <summary>Boss Bass flares (and the stacks they spawn) hurt the player instead of bosses / enemies.</summary>
    public void SetHostile(bool hostile)
    {
        hostileToPlayer = hostile;
    }

    /// <summary>Passed through to the Heat Flare Stack spawned on land (Attack Style level 5 etc.).</summary>
    public void SetStackDamageMultiplier(float multiplier)
    {
        stackDamageMultiplier = Mathf.Max(0f, multiplier);
    }

    private void FixedUpdate()
    {
        if (!launched || remnantFading)
            return;

        float dt = Time.fixedDeltaTime;
        velocity.y -= gravity * dt;
        Vector2 start = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 delta = velocity * dt;
        Vector2 end = start + delta;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundLayers);
        float dist = delta.magnitude;
        if (dist > 0.0001f)
        {
            int count = Physics2D.CircleCast(start, 0.12f, delta.normalized, filter, casts, dist);
            for (int i = 0; i < count; i++)
            {
                if (casts[i].collider == null || casts[i].collider.isTrigger)
                    continue;
                if (casts[i].collider.transform.IsChildOf(transform))
                    continue;
                if (owner != null && casts[i].collider.transform.IsChildOf(owner))
                    continue;
                // Phasable through enemies — only solid ground/walls stop us.
                if (casts[i].collider.GetComponentInParent<ICommonEnemy>() != null)
                    continue;
                if (casts[i].collider.GetComponentInParent<Boss>() != null)
                    continue;
                if (casts[i].collider.GetComponentInParent<PlayerController>() != null)
                    continue;

                end = start + delta.normalized * Mathf.Max(0f, casts[i].distance - 0.02f);
                Land(end, casts[i].normal);
                return;
            }
        }

        if (rb != null)
            rb.MovePosition(end);
        else
            transform.position = end;

        TryDamagePassThrough();
    }

    private void Update()
    {
        if (!remnantFading)
            return;

        remnantTimer += Time.deltaTime;
        float t = Mathf.Clamp01(remnantTimer / Mathf.Max(0.05f, remnantShrinkSeconds));
        transform.localScale = Vector3.Lerp(remnantStartScale, Vector3.zero, t);
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = Mathf.Lerp(1f, 0f, t);
            spriteRenderer.color = c;
        }

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void TryDamagePassThrough()
    {
        if (mode != Mode.StackSpawner || damage <= 0f || hitCollider == null)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        var buf = new Collider2D[8];
        int count = Physics2D.OverlapCollider(hitCollider, filter, buf);
        for (int i = 0; i < count; i++)
            BassCrashHeatCombat.ApplyDamageOnly(buf[i], damage, false, hostileToPlayer, transform);
    }

    private void Land(Vector2 pos, Vector2 normal)
    {
        if (landed)
            return;
        landed = true;
        launched = false;
        velocity = Vector2.zero;

        if (rb != null)
            rb.position = pos;
        transform.position = pos;

        if (mode == Mode.Remnant)
        {
            remnantFading = true;
            remnantTimer = 0f;
            remnantStartScale = transform.localScale;
            if (hitCollider != null)
                hitCollider.enabled = false;
            return;
        }

        // Stack spawner: align bottoms, then become pillar (stack owns damage).
        if (stackPrefab != null && (trySpendAmmo == null || trySpendAmmo(stackAmmoCost)))
        {
            BassHeatFlareStack stack = Instantiate(stackPrefab, pos, Quaternion.identity);
            stack.ConfigureHostSorting(hostGroup, hostBody);
            if (remnantPrefabSource != null)
                stack.SetRemnantPrefab(remnantPrefabSource);
            stack.SetDamageMultiplier(stackDamageMultiplier);
            stack.SetHostile(hostileToPlayer);
            stack.BeginFromFlare(transform, owner, trySpendAmmo);
        }

        Destroy(gameObject);
    }
}
