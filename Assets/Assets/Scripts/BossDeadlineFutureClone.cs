using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Boss Deadline ambush clone — mirrors playable <see cref="DeadlineFutureClone"/>:
/// independent Deadline animator (never stuck on Dash), dynamic hunt / parkour toward the
/// player, Short Hand fire. Not a Count time-clone turret.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BossDeadlineFutureClone : MonoBehaviour, IDamageable
{
    private BossDeadline owner;
    private Projectile shortHandPrefab;
    private Projectile longHandPrefab;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Rigidbody2D rb;
    private BoxCollider2D bodyCollider;
    private Color baseColor;
    private int health;
    private float shootInterval;
    private float searchRadius;
    private float moveSpeed;
    private float jumpForce;
    private float fallMultiplier;
    private LayerMask groundLayers;
    private float groundCheckRadius;
    private float nextShotAt;
    private float fadeOutStartedAt = -1f;
    private bool artFacesLeft = true;
    private bool isGrounded;
    private float facingSign = 1f;
    private float jumpCooldownUntil;
    private float leapCommitUntil;
    private float preferShootDistance = 5.2f;
    private float chargeTimer;
    private bool isCharging;
    private float chargeSeconds = 2.4f;
    private bool isShootingAnim;
    private float shootAnimTimer;
    private bool voluntarilyAirborne;
    private float coyoteTimer;
    private float lastMoveDir;

    private const float FadeOutSeconds = 0.22f;
    private const float GapLookAhead = 1.25f;
    private const float WallProbe = 0.7f;
    private const float CoyoteTime = 0.12f;

    public bool IsDead => health <= 0 || fadeOutStartedAt >= 0f;

    public static void DestroyAllForOwner(BossDeadline boss)
    {
        if (boss == null)
            return;

        BossDeadlineFutureClone[] clones = FindObjectsByType<BossDeadlineFutureClone>(FindObjectsSortMode.None);
        for (int i = 0; i < clones.Length; i++)
        {
            if (clones[i] != null && clones[i].owner == boss)
                Destroy(clones[i].gameObject);
        }
    }

    public static BossDeadlineFutureClone Spawn(
        BossDeadline owner,
        Vector3 position,
        Color tint,
        Projectile shortHand,
        Projectile longHand,
        int health,
        float shootInterval,
        float searchRadius,
        float moveSpeed,
        float jumpForce,
        float fallMultiplier,
        LayerMask groundLayers,
        float groundCheckRadius,
        Vector2 bodySize,
        Vector2 bodyOffset,
        bool faceRight)
    {
        if (owner == null || shortHand == null)
            return null;

        var go = new GameObject($"{owner.name}_FutureClone");
        go.transform.position = position;
        var clone = go.AddComponent<BossDeadlineFutureClone>();
        clone.Init(
            owner, tint, shortHand, longHand, health, shootInterval, searchRadius,
            moveSpeed, jumpForce, fallMultiplier, groundLayers, groundCheckRadius,
            bodySize, bodyOffset, faceRight);
        return clone;
    }

    private void Init(
        BossDeadline cloneOwner,
        Color tint,
        Projectile shortHand,
        Projectile longHand,
        int hp,
        float interval,
        float radius,
        float speed,
        float jump,
        float fallMul,
        LayerMask grounds,
        float groundRadius,
        Vector2 bodySize,
        Vector2 bodyOffset,
        bool faceRight)
    {
        owner = cloneOwner;
        shortHandPrefab = shortHand;
        longHandPrefab = longHand;
        health = Mathf.Max(1, hp);
        shootInterval = Mathf.Max(0.15f, interval);
        searchRadius = Mathf.Max(2f, radius);
        moveSpeed = Mathf.Max(2f, speed);
        jumpForce = Mathf.Max(4f, jump);
        fallMultiplier = Mathf.Max(1f, fallMul);
        groundLayers = grounds;
        groundCheckRadius = Mathf.Max(0.05f, groundRadius);
        baseColor = tint;
        nextShotAt = Time.time + shootInterval * 0.3f;
        facingSign = faceRight ? 1f : -1f;
        artFacesLeft = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = tint;
        spriteRenderer.flipX = faceRight;

        SpriteRenderer body = owner.GetComponent<SpriteRenderer>();
        SortingGroup group = owner.EffectSortingGroup;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, group, body, -1);
        if (body != null)
            spriteRenderer.sharedMaterial = body.sharedMaterial;

        // Own Deadline controller — never freeze a Dash birth frame from the boss.
        SetupIndependentAnimator();

        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        Rigidbody2D ownerRb = owner.GetComponent<Rigidbody2D>();
        rb.gravityScale = ownerRb != null ? ownerRb.gravityScale : 3f;

        bodyCollider = gameObject.AddComponent<BoxCollider2D>();
        if (bodySize.sqrMagnitude > 0.01f)
        {
            bodyCollider.size = bodySize;
            bodyCollider.offset = bodyOffset;
        }
        else
        {
            bodyCollider.size = new Vector2(0.7f, 1.35f);
        }

        Collider2D ownerCol = owner.GetComponent<Collider2D>();
        if (ownerCol != null)
            Physics2D.IgnoreCollision(bodyCollider, ownerCol, true);

        EnemyDetectionZone.IgnoreAgainstAllZones(bodyCollider);
    }

    private void SetupIndependentAnimator()
    {
        Animator ownerAnim = owner != null ? owner.GetComponent<Animator>() : null;
        animator = gameObject.GetComponent<Animator>();
        if (animator == null)
            animator = gameObject.AddComponent<Animator>();

        if (ownerAnim != null && ownerAnim.runtimeAnimatorController != null)
        {
            animator.runtimeAnimatorController = ownerAnim.runtimeAnimatorController;
            animator.avatar = ownerAnim.avatar;
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.speed = 1f;

        // Force out of any Dash state the controller might default into.
        animator.SetBool("IsDashing", false);
        animator.SetBool("IsMoving", false);
        animator.SetBool("IsGrounded", true);
        animator.SetBool("IsInAir", false);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsFalling", false);
        animator.SetBool("IsShooting", false);
        animator.SetBool("AimUp", false);
        animator.SetBool("IsStunned", false);
        int idle = Animator.StringToHash("Idle");
        if (animator.HasState(0, idle))
            animator.Play(idle, 0, 0f);
    }

    private void FixedUpdate()
    {
        if (fadeOutStartedAt >= 0f || IsDead || rb == null)
            return;

        UpdateGrounded();
        ApplyFallMultiplier();

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
        {
            SetHorizontal(0f);
            return;
        }

        float dist = Vector2.Distance(transform.position, player.transform.position);
        TickHunt(player, closeIn: dist > preferShootDistance);
    }

    private void Update()
    {
        if (fadeOutStartedAt >= 0f)
        {
            TickFadeOut();
            return;
        }

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        TickCombat(Time.deltaTime);
        TickShootAnimTimer(Time.deltaTime);
        UpdateAnimator();
    }

    private void TickHunt(PlayerController player, bool closeIn)
    {
        Bounds aim = player.BodyCollider != null
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);
        float dx = aim.center.x - transform.position.x;
        float dy = aim.center.y - transform.position.y;
        float dir = Mathf.Abs(dx) > 0.35f ? Mathf.Sign(dx) : facingSign;

        if (Time.time < leapCommitUntil)
        {
            SetHorizontal(facingSign);
            Face(facingSign);
            return;
        }

        if (closeIn)
        {
            NavigateToward(dir, aim.center.y);
        }
        else if (Mathf.Abs(dx) < 1.35f)
        {
            SetHorizontal(0f);
            if (dy > 1.0f)
                TryJump(dir, fullJump: true);
        }
        else if (Mathf.Abs(dx) < preferShootDistance * 0.55f)
        {
            NavigateToward(-dir, aim.center.y);
        }
        else
        {
            NavigateToward(dir * 0.4f, aim.center.y);
        }

        Face(dir);
    }

    private void NavigateToward(float dir, float targetY)
    {
        if (Mathf.Abs(dir) < 0.01f)
        {
            SetHorizontal(0f);
            return;
        }

        float sign = Mathf.Sign(dir);

        if (NeedsJumpForGap(sign) || NeedsJumpOverWall(sign) ||
            (targetY > transform.position.y + 1.0f && CanStartJump()))
        {
            if (TryJump(sign, fullJump: true))
            {
                SetHorizontal(sign);
                return;
            }
        }

        if (CanStartJump() && IsBlockedHorizontally(sign) && TryJump(sign, fullJump: true))
        {
            SetHorizontal(sign);
            return;
        }

        SetHorizontal(sign);
    }

    private void TickCombat(float dt)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead || shortHandPrefab == null)
            return;

        float dist = Vector2.Distance(transform.position, player.transform.position);
        if (dist > searchRadius)
        {
            isCharging = false;
            chargeTimer = 0f;
            return;
        }

        Vector2 aim = AimAt(player);
        Face(aim.x);

        if (!isCharging)
        {
            isCharging = true;
            chargeTimer = 0f;
        }

        chargeTimer += dt;
        bool charged = chargeTimer >= chargeSeconds && longHandPrefab != null;
        if (Time.time < nextShotAt)
            return;

        FireShot(charged, aim);
        isCharging = false;
        chargeTimer = 0f;
    }

    private Vector2 AimAt(PlayerController player)
    {
        Vector3 muzzle = MuzzlePosition();
        Vector3 center = player.BodyCollider != null
            ? player.BodyCollider.bounds.center
            : player.transform.position;
        Vector2 aim = (Vector2)(center - muzzle);
        if (aim.sqrMagnitude < 0.0001f)
            aim = new Vector2(facingSign, 0f);
        return aim.normalized;
    }

    private void FireShot(bool charged, Vector2 dir)
    {
        Projectile prefab = charged && longHandPrefab != null ? longHandPrefab : shortHandPrefab;
        if (prefab == null || owner == null)
            return;

        Projectile shot = Instantiate(prefab, MuzzlePosition(), Quaternion.identity);
        float damage = Mathf.Max(1f, prefab.Damage);
        shot.Launch(dir, prefab.Speed, damage, owner.transform);
        SoundManager.Instance?.PlayCountFire(charged ? ProjectileShotType.Big : ProjectileShotType.Small);
        nextShotAt = Time.time + shootInterval * (charged ? 1.05f : 0.85f);

        isShootingAnim = true;
        shootAnimTimer = charged ? 0.34f : 0.26f;
        PlayShootAnimationTriggers();
    }

    private void PlayShootAnimationTriggers()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        animator.SetBool("IsShooting", true);
        animator.SetBool("IsDashing", false);
        animator.ResetTrigger("Shoot");
        animator.ResetTrigger("ShootUp");
        animator.ResetTrigger("JumpShot");
        animator.ResetTrigger("FallShot");
        animator.ResetTrigger("RunShoot");

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        float vx = rb != null ? Mathf.Abs(rb.linearVelocity.x) : 0f;
        bool airborne = !isGrounded && coyoteTimer <= 0f;
        // Use move intent + velocity so we don't Ground-Shoot while sliding/running.
        bool running = !airborne && (vx > 0.2f || Mathf.Abs(lastMoveDir) > 0.01f);

        string trigger;
        if (airborne && vy >= -0.01f)
            trigger = "JumpShot";
        else if (airborne)
            trigger = "FallShot";
        else if (running)
            trigger = "RunShoot";
        else
            trigger = "Shoot";

        animator.SetBool("IsMoving", running);
        animator.SetTrigger(trigger);
        string stateName = trigger == "RunShoot" ? "Run Shoot"
            : trigger == "JumpShot" || trigger == "FallShot" ? "Jump Shoot"
            : "Ground shoot";
        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);
    }

    private Vector3 MuzzlePosition()
    {
        Vector3 center = bodyCollider != null ? (Vector3)bodyCollider.bounds.center : transform.position;
        return center + new Vector3(facingSign * 0.7f, -0.1f, 0f);
    }

    private bool CanStartJump() =>
        (isGrounded || coyoteTimer > 0f) && Time.time >= jumpCooldownUntil;

    private bool TryJump(float dir, bool fullJump)
    {
        if (!CanStartJump() || rb == null)
            return false;

        float force = fullJump ? jumpForce : jumpForce * 0.72f;
        Vector2 v = rb.linearVelocity;
        v.y = force;
        rb.linearVelocity = v;
        jumpCooldownUntil = Time.time + 0.28f;
        leapCommitUntil = Time.time + 0.38f;
        voluntarilyAirborne = true;
        coyoteTimer = 0f;
        if (Mathf.Abs(dir) > 0.01f)
            facingSign = Mathf.Sign(dir);

        if (animator != null)
        {
            animator.SetBool("IsDashing", false);
            animator.SetBool("IsGrounded", false);
            animator.SetBool("IsInAir", true);
            animator.SetBool("IsJumping", true);
            animator.SetBool("IsFalling", false);
            int jumpState = Animator.StringToHash("Jump");
            if (animator.HasState(0, jumpState))
                animator.Play(jumpState, 0, 0f);
        }

        return true;
    }

    private bool NeedsJumpForGap(float sign)
    {
        Vector2 origin = bodyCollider != null
            ? new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.05f)
            : (Vector2)transform.position;
        Vector2 ahead = origin + new Vector2(sign * GapLookAhead, 0f);
        return Physics2D.Raycast(ahead, Vector2.down, 1.4f, groundLayers).collider == null;
    }

    private bool NeedsJumpOverWall(float sign)
    {
        Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, new Vector2(sign, 0f), WallProbe, groundLayers);
        if (hit.collider == null)
            return false;

        float top = hit.collider.bounds.max.y;
        float feet = bodyCollider != null ? bodyCollider.bounds.min.y : transform.position.y;
        return top > feet + 0.15f && top < feet + 2.4f;
    }

    private bool IsBlockedHorizontally(float sign)
    {
        Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
        return Physics2D.Raycast(origin, new Vector2(sign, 0f), 0.45f, groundLayers).collider != null;
    }

    private void UpdateGrounded()
    {
        Vector2 origin = bodyCollider != null
            ? new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y)
            : (Vector2)transform.position;
        bool groundedNow = Physics2D.OverlapCircle(origin, groundCheckRadius, groundLayers) != null;
        if (groundedNow)
        {
            isGrounded = true;
            coyoteTimer = CoyoteTime;
            if (rb != null && rb.linearVelocity.y <= 0.05f)
                voluntarilyAirborne = false;
        }
        else
        {
            isGrounded = false;
            coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
        }
    }

    private void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded)
            return;
        if (rb.linearVelocity.y >= -0.01f)
            return;

        rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
    }

    private void SetHorizontal(float dir)
    {
        if (rb == null)
            return;

        lastMoveDir = Mathf.Abs(dir) < 0.01f ? 0f : Mathf.Sign(dir);
        float x = lastMoveDir * moveSpeed;
        Vector2 v = rb.linearVelocity;
        v.x = x;
        rb.linearVelocity = v;
        if (Mathf.Abs(lastMoveDir) > 0.01f)
            facingSign = lastMoveDir;
    }

    private void Face(float x)
    {
        if (spriteRenderer == null || Mathf.Abs(x) < 0.01f)
            return;
        bool right = x > 0f;
        spriteRenderer.flipX = artFacesLeft ? right : !right;
        facingSign = right ? 1f : -1f;
    }

    private void TickShootAnimTimer(float dt)
    {
        if (!isShootingAnim)
            return;
        shootAnimTimer -= dt;
        if (shootAnimTimer <= 0f)
        {
            isShootingAnim = false;
            shootAnimTimer = 0f;
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
        float horizontalSpeed = rb != null ? rb.linearVelocity.x : 0f;
        bool groundedForAnim = isGrounded && !voluntarilyAirborne;
        bool moving = groundedForAnim &&
                      (Mathf.Abs(horizontalSpeed) > 0.25f || Mathf.Abs(lastMoveDir) > 0.01f);
        // Keep Run Shoot locked while the shoot window is active and we intended to move.
        if (isShootingAnim && groundedForAnim && Mathf.Abs(lastMoveDir) > 0.01f)
            moving = true;

        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsGrounded", groundedForAnim);
        animator.SetBool("IsInAir", !groundedForAnim);
        // Hard rule: clones never play Dash — birth from boss Rush must not stick.
        animator.SetBool("IsDashing", false);
        animator.SetBool("IsShooting", isShootingAnim);
        animator.SetBool("AimUp", false);
        animator.SetBool("IsStunned", false);
        animator.SetBool("IsFalling", !groundedForAnim && verticalSpeed < -0.01f);
        animator.SetBool("IsJumping", !groundedForAnim && verticalSpeed >= -0.01f);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
    }

    private void TickFadeOut()
    {
        float u = (Time.time - fadeOutStartedAt) / FadeOutSeconds;
        if (spriteRenderer != null)
        {
            Color c = baseColor;
            c.a = Mathf.Lerp(baseColor.a, 0f, Mathf.Clamp01(u));
            spriteRenderer.color = c;
        }

        if (u >= 1f)
            Destroy(gameObject);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        health = Mathf.Max(0, health - amount);
        if (health <= 0)
            BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        if (fadeOutStartedAt >= 0f)
            return;
        fadeOutStartedAt = Time.time;
        health = 0;
        if (bodyCollider != null)
            bodyCollider.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }
}
