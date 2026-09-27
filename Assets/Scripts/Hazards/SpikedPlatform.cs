using UnityEngine;

/// <summary>
/// Ceiling spiked platform hazard: drops when the player is under the Attack Box with clear
/// ground LOS, deals 3 damage on spike contact (no player knockback), then bounces and
/// floats home. Misses that hit the ground bounce higher. Standable top; no HP.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class SpikedPlatform : MonoBehaviour
{
    private enum State
    {
        Idle,
        Slamming,
        BounceUp,
        ReturnHome
    }

    [Header("Combat")]
    [SerializeField] private int contactDamage = 3;
    [SerializeField] private Collider2D attackCollider;
    [SerializeField] private LayerMask playerLayers = ~0;
    [SerializeField] private LayerMask groundLayers;

    [Header("Motion")]
    [SerializeField] private float slamSpeed = 6f;
    [SerializeField] private float returnSpeed = 2.5f;
    [SerializeField] private float bounceDuration = 0.2f;
    [SerializeField] private float bounceOnPlayerHit = 1f;
    [SerializeField] private float bounceOnGroundHit = 3f;
    [SerializeField] private float arriveDistance = 0.04f;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D[] solidColliders;
    private Vector2 homePosition;
    private State state = State.Idle;
    private float bounceTimer;
    private Vector2 bounceStart;
    private Vector2 bounceEnd;
    private bool hitResolvedThisSlam;
    private PlayerController targetPlayer;
    private readonly Collider2D[] overlapBuffer = new Collider2D[8];
    private ContactFilter2D playerFilter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        CacheChildReferences();
        ConfigureBody();
        ConfigureAttackCollider();
        BuildSolidColliderCache();

        if (groundLayers == 0)
            groundLayers = LayerMask.GetMask("Ground");

        playerFilter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = playerLayers != ~0,
            layerMask = playerLayers,
            useDepth = false
        };

        homePosition = rb != null ? rb.position : (Vector2)transform.position;
        state = State.Idle;
    }

    private void Start()
    {
        if (rb != null)
            rb.MovePosition(homePosition);
    }

    private void Update()
    {
        ResolveTargetPlayer();

        if (state == State.Idle)
            TryBeginSlam();
    }

    private void FixedUpdate()
    {
        float dt = HyperSpeedWorldSlow.WorldFixedDeltaTime;

        switch (state)
        {
            case State.Slamming:
                TickSlam(dt);
                break;
            case State.BounceUp:
                TickBounce(dt);
                break;
            case State.ReturnHome:
                TickReturn(dt);
                break;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (state != State.Slamming || hitResolvedThisSlam || collision == null)
            return;

        if (IsOwnCollider(collision.collider))
            return;

        if (((1 << collision.gameObject.layer) & groundLayers) == 0)
            return;

        // Solid body / child platform hit ground — miss bounce.
        BeginBounce(bounceOnGroundHit);
    }

    private void TryBeginSlam()
    {
        if (!IsPlayerUnderWithClearShot())
            return;

        state = State.Slamming;
        hitResolvedThisSlam = false;
    }

    private void TickSlam(float dt)
    {
        if (rb == null)
            return;

        Vector2 next = rb.position + Vector2.down * Mathf.Max(0.1f, slamSpeed) * dt;
        rb.MovePosition(next);

        TrySpikeHitPlayer();
    }

    private void TrySpikeHitPlayer()
    {
        if (hitResolvedThisSlam || attackCollider == null || !attackCollider.enabled)
            return;

        int count = attackCollider.Overlap(playerFilter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null)
                continue;

            PlayerController player = col.GetComponent<PlayerController>();
            if (player == null)
                player = col.GetComponentInParent<PlayerController>();

            if (player == null || player.IsDead)
                continue;

            // Damage only — platform takes the knockback instead.
            player.TakeDamage(Mathf.Max(0, contactDamage), transform, applyKnockback: false);
            BeginBounce(bounceOnPlayerHit);
            return;
        }
    }

    private void BeginBounce(float upSpaces)
    {
        if (state == State.BounceUp || state == State.ReturnHome)
            return;

        hitResolvedThisSlam = true;
        state = State.BounceUp;
        bounceTimer = 0f;
        bounceStart = rb != null ? rb.position : (Vector2)transform.position;
        bounceEnd = bounceStart + Vector2.up * Mathf.Max(0.01f, upSpaces);
    }

    private void TickBounce(float dt)
    {
        if (rb == null)
            return;

        float duration = Mathf.Max(0.05f, bounceDuration);
        bounceTimer += dt;
        float t = Mathf.Clamp01(bounceTimer / duration);
        // Smootherstep: quick but smooth.
        float s = t * t * t * (t * (t * 6f - 15f) + 10f);
        rb.MovePosition(Vector2.Lerp(bounceStart, bounceEnd, s));

        if (t >= 1f)
            state = State.ReturnHome;
    }

    private void TickReturn(float dt)
    {
        if (rb == null)
            return;

        Vector2 pos = rb.position;
        Vector2 delta = homePosition - pos;
        float dist = delta.magnitude;
        if (dist <= arriveDistance)
        {
            rb.MovePosition(homePosition);
            state = State.Idle;
            hitResolvedThisSlam = false;
            return;
        }

        float step = Mathf.Max(0.1f, returnSpeed) * dt;
        if (step >= dist)
            rb.MovePosition(homePosition);
        else
            rb.MovePosition(pos + delta / dist * step);
    }

    private bool IsPlayerUnderWithClearShot()
    {
        if (targetPlayer == null || targetPlayer.IsDead || attackCollider == null)
            return false;

        Bounds ab = attackCollider.bounds;
        Vector2 playerPos = targetPlayer.transform.position;

        if (playerPos.x < ab.min.x || playerPos.x > ab.max.x)
            return false;

        // Must be somewhere below the attack box.
        if (playerPos.y >= ab.max.y)
            return false;

        // Ground between platform and player blocks vision / slam.
        float startY = ab.min.y;
        Collider2D playerCol = targetPlayer.GetComponent<Collider2D>();
        if (playerCol == null)
            playerCol = targetPlayer.GetComponentInChildren<Collider2D>();

        float endY = playerCol != null ? playerCol.bounds.max.y : playerPos.y + 0.5f;
        if (endY >= startY - 0.01f)
            return true; // overlapping vertically already — allow

        Vector2 origin = new Vector2(Mathf.Clamp(playerPos.x, ab.min.x, ab.max.x), startY);
        float distance = startY - endY;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, distance, groundLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCol = hits[i].collider;
            if (hitCol == null || IsOwnCollider(hitCol))
                continue;

            // Ground (or other world solid on the ground mask) between us and the player.
            return false;
        }

        return true;
    }

    private bool IsOwnCollider(Collider2D col)
    {
        if (col == null)
            return false;

        Transform t = col.transform;
        return t == transform || t.IsChildOf(transform);
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
    }

    private void CacheChildReferences()
    {
        if (attackCollider == null)
        {
            Transform found = transform.Find("Attack Box");
            if (found != null)
                attackCollider = found.GetComponent<Collider2D>();
        }
    }

    private void ConfigureBody()
    {
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.useFullKinematicContacts = true;
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void ConfigureAttackCollider()
    {
        if (attackCollider == null)
            return;

        // Spikes are overlap-only; solid top / Box Collider remain walkable.
        attackCollider.isTrigger = true;
    }

    private void BuildSolidColliderCache()
    {
        Collider2D[] all = GetComponentsInChildren<Collider2D>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i] != attackCollider && !all[i].isTrigger)
                count++;
        }

        solidColliders = new Collider2D[count];
        int w = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i] != attackCollider && !all[i].isTrigger)
                solidColliders[w++] = all[i];
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        contactDamage = Mathf.Max(0, contactDamage);
        slamSpeed = Mathf.Max(0.1f, slamSpeed);
        returnSpeed = Mathf.Max(0.1f, returnSpeed);
        bounceDuration = Mathf.Max(0.05f, bounceDuration);
        bounceOnPlayerHit = Mathf.Max(0.01f, bounceOnPlayerHit);
        bounceOnGroundHit = Mathf.Max(0.01f, bounceOnGroundHit);
    }
#endif
}
