using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

/// <summary>
/// Kit — Mega Man style blaster character.
/// Grounded shooting locks movement. Charging unlocks movement only once the charge aura appears.
/// Small / medium / big shots wait for shoot animation before spawning.
/// </summary>
public class KitPlayerController : PlayerController
{
    [Header("Kit - Projectile Prefabs")]
    [SerializeField] private Projectile smallProjectilePrefab;
    [SerializeField] private Projectile mediumProjectilePrefab;
    [SerializeField] private Projectile bigProjectilePrefab;
    [SerializeField] private float shootCooldown = 0.12f;

    [Header("Kit - VFX Prefabs")]
    [Tooltip("Small Buster Blast prefab for small shots.")]
    [SerializeField] private VisualEffect busterBlastSmallPrefab;
    [Tooltip("Medium + big Buster Blast prefab (big shots scale up from this art).")]
    [FormerlySerializedAs("busterBlastMediumPrefab")]
    [FormerlySerializedAs("busterBlastBigPrefab")]
    [SerializeField] private VisualEffect busterBlastMediumBigPrefab;

    [Header("Kit - Charge Thresholds")]
    [SerializeField] private float mediumChargeSeconds = 1.5f;
    [SerializeField] private float bigChargeSeconds = 3f;
    [Tooltip("Extra move speed once Kit reaches half (medium) charge and can move with the aura.")]
    [SerializeField] private float mediumChargeMoveSpeedBonus = 3f;
    [Tooltip("Extra move speed once Kit reaches max (big) charge.")]
    [SerializeField] private float bigChargeMoveSpeedBonus = 6f;

    [Header("Kit - Pink Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.45f, 0.75f, 1f);
    [Tooltip("More solid pink near full charge.")]
    [SerializeField] private Color chargeAuraStrongColor = new Color(1f, 0.25f, 0.7f, 1f);
    [Tooltip("Soft yellow tint used in the gentle pink↔yellow shimmer.")]
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 0.92f, 0.55f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [Tooltip("Base shimmer cycles per second. Kept slow for photosensitivity safety.")]
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [Tooltip("How strongly yellow mixes in at peak shimmer (keep low for a subtle look).")]
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [Tooltip("Delay after a small shot fires before the charge aura can appear.")]
    [SerializeField] private float auraDelayAfterSmallShot = 0.2f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtMedium = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtBig = 0.95f;
    [Tooltip("Unused — aura now matches the character sorting plane so backgrounds cannot cover it.")]
    [SerializeField] private int auraSortingBehind = 2;

    [Header("Kit - Animation / Ground Lock")]
    [SerializeField] private float shootAnimDuration = 0.28f;
    [Tooltip("While grounded and firing (or charging before the aura appears), Kit cannot walk/run.")]
    [SerializeField] private bool lockMovementWhileGroundShooting = true;
    [Tooltip("How long to wait for a shoot anim state before forcing the shot spawn.")]
    [SerializeField] private float shotAnimWaitTimeout = 0.4f;
    [Tooltip("Animator state names that count as Kit being in a shot animation.")]
    [SerializeField] private string[] shootAnimStateNames =
    {
        "Shooting on ground",
        "Shooting upward",
        "Jump Shot",
        "Fall Shot"
    };

    [Header("Kit - Dash Override")]
    [SerializeField] private float kitDashDistance = 5.25f;
    [SerializeField] private float kitDashSpeed = 17f;
    [SerializeField] private float kitDashSmoothStop = 0.14f;
    [SerializeField] private int kitMaxAirDashes = 2;
    [SerializeField] private bool kitAllowDashJump = true;
    [SerializeField] private float kitDashJumpHorizontalSpeed = 0f;
    [SerializeField] private float kitDashJumpForce = 0f;
    [SerializeField] private float kitDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool kitDashJumpMomentumUntilLanded = true;

    [Header("Kit - Afterimages")]
    [SerializeField] private bool kitEnableDashAfterimages = true;
    [SerializeField] private int kitAfterimageCount = 3;
    [SerializeField] private float kitAfterimageSpacing = 0.1f;
    [SerializeField] private Color kitAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float kitAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float kitAfterimageAlphaEnd = 0.15f;

    private bool isCharging;
    private float chargeTimer;
    private float shootCooldownTimer;

    private bool pendingShot;
    private ProjectileShotType pendingShotType;
    private float pendingShotDeadline;
    private Vector2 pendingShotAim;

    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraAllowedAfterTime;
    private float auraFlickerPhase;

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Kit");

        dashDistance = kitDashDistance;
        dashSpeed = kitDashSpeed;
        dashSmoothStopDuration = kitDashSmoothStop;
        maxAirDashes = kitMaxAirDashes;
        allowDashJump = kitAllowDashJump;
        dashJumpHorizontalSpeed = kitDashJumpHorizontalSpeed;
        dashJumpForce = kitDashJumpForce;
        dashJumpMomentumDuration = kitDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = kitDashJumpMomentumUntilLanded;
        enableDashAfterimages = kitEnableDashAfterimages;
        dashAfterimageCount = kitAfterimageCount;
        dashAfterimageSpacing = kitAfterimageSpacing;
        dashAfterimageColor = kitAfterimageColor;
        dashAfterimageAlphaStart = kitAfterimageAlphaStart;
        dashAfterimageAlphaEnd = kitAfterimageAlphaEnd;
        airDashesRemaining = Mathf.Max(0, maxAirDashes);

        SetupDashAfterimages();
        SetupChargeAura();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        CancelPendingShot();
        SetChargeAuraVisible(false);
    }

    protected override void OnDestroy()
    {
        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);

        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);

        base.OnDestroy();
    }

    protected override void HandleCharacterUpdate()
    {
        if (shootCooldownTimer > 0f)
            shootCooldownTimer -= Time.deltaTime;

        if (isCharging)
        {
            chargeTimer += Time.deltaTime;
            // Aura waits until after the small shot has fired + delay.
            if (Time.time >= auraAllowedAfterTime)
            {
                // Aura is visible → charge movement unlock. Drop shoot-lock anim flag.
                isShooting = false;
                shootAnimTimer = 0f;
                UpdateChargeAura();
            }
            else
            {
                SetChargeAuraVisible(false);
            }
        }
        else
        {
            SetChargeAuraVisible(false);
        }

        TickPendingShot();
    }

    /// <summary>
    /// True once Kit is charging and the pink aura is allowed to show (after small shot + delay).
    /// </summary>
    private bool IsChargeAuraActive()
    {
        return isCharging && Time.time >= auraAllowedAfterTime;
    }

    protected override void ApplyHorizontalMove()
    {
        // Spam / shoot: stay stuck. Charge: stay stuck until the aura appears, then free to move.
        bool chargeMoveUnlocked = IsChargeAuraActive();
        bool shouldLock =
            lockMovementWhileGroundShooting &&
            isGrounded &&
            !chargeMoveUnlocked &&
            (isShooting || pendingShot || isCharging);

        if (shouldLock)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();

        // Speed bonus only while charging with aura up (same window she is free to move).
        if (!IsChargeAuraActive())
            return speed;

        if (chargeTimer >= bigChargeSeconds)
            return speed + bigChargeMoveSpeedBonus;

        if (chargeTimer >= mediumChargeSeconds)
            return speed + mediumChargeMoveSpeedBonus;

        return speed;
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        if (IsChargeAuraActive())
            return false;

        return isShooting || pendingShot || isCharging;
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        isCharging = true;
        chargeTimer = 0f;
        // Aura (and charge-move unlock) only after a small shot actually fires + delay.
        auraAllowedAfterTime = float.PositiveInfinity;
        SetChargeAuraVisible(false);

        QueueShot(ProjectileShotType.Small);
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (!isCharging)
            return;

        float held = chargeTimer;
        isCharging = false;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);

        if (held >= bigChargeSeconds)
            QueueShot(ProjectileShotType.Big, replacePending: true);
        else if (held >= mediumChargeSeconds)
            QueueShot(ProjectileShotType.Medium, replacePending: true);
    }

    protected override void OnHitStunStarted()
    {
        isCharging = false;
        chargeTimer = 0f;
        pendingShot = false;
        SetChargeAuraVisible(false);
        isShooting = false;
        shootAnimTimer = 0f;
        base.OnHitStunStarted();
    }

    private void QueueShot(ProjectileShotType type, bool replacePending = false)
    {
        Projectile prefab = GetPrefab(type);
        if (prefab == null)
            return;

        if (type == ProjectileShotType.Small && shootCooldownTimer > 0f && !replacePending)
            return;

        // Don't stack another small while one is already waiting, unless replacing with charged shot.
        if (pendingShot && type == ProjectileShotType.Small && !replacePending)
            return;

        if (pendingShot && !replacePending && type != ProjectileShotType.Small)
            return;

        CancelPendingShot();

        pendingShot = true;
        pendingShotType = type;
        pendingShotAim = GetAimDirection();
        pendingShotDeadline = Time.time + Mathf.Max(0.05f, shotAnimWaitTimeout);

        BeginShootAnimation(Mathf.Max(shootAnimDuration, shotAnimWaitTimeout));
        PlayShootAnimatorTriggers();
    }

    private void TickPendingShot()
    {
        if (!pendingShot)
            return;

        if (IsInShootAnimation() || Time.time >= pendingShotDeadline)
            FirePendingShot();
    }

    private void FirePendingShot()
    {
        if (!pendingShot)
            return;

        ProjectileShotType firedType = pendingShotType;
        Projectile prefab = GetPrefab(firedType);
        pendingShot = false;

        if (prefab == null)
            return;

        SpawnProjectile(prefab, pendingShotAim);
        shootCooldownTimer = shootCooldown;

        if (firedType == ProjectileShotType.Small)
            auraAllowedAfterTime = Time.time + Mathf.Max(0f, auraDelayAfterSmallShot);
    }

    private void CancelPendingShot()
    {
        pendingShot = false;
    }

    private bool IsInShootAnimation()
    {
        if (animator == null)
            return true;

        if (shootAnimStateNames == null || shootAnimStateNames.Length == 0)
            return animator.GetBool("IsShooting");

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (StateMatchesShoot(current))
            return true;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (StateMatchesShoot(next))
                return true;
        }

        return false;
    }

    private bool StateMatchesShoot(AnimatorStateInfo info)
    {
        for (int i = 0; i < shootAnimStateNames.Length; i++)
        {
            string stateName = shootAnimStateNames[i];
            if (string.IsNullOrWhiteSpace(stateName))
                continue;

            if (info.IsName(stateName))
                return true;
        }

        return false;
    }

    private Projectile GetPrefab(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Medium:
                return mediumProjectilePrefab;
            case ProjectileShotType.Big:
                return bigProjectilePrefab;
            default:
                return smallProjectilePrefab;
        }
    }

    private VisualEffect GetBusterBlastPrefab(ProjectileShotType type)
    {
        return VisualEffects.ResolveBusterBlastPrefab(
            type,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
    }

    private void SpawnProjectile(Projectile prefab, Vector2 direction)
    {
        if (prefab == null)
            return;

        Vector3 spawnPos = GetFirePosition();
        Projectile shot = Instantiate(prefab, spawnPos, Quaternion.identity);
        shot.Launch(direction, transform);

        Transform muzzle = firePoint != null ? firePoint : transform;
        VisualEffects.SpawnBusterBlast(GetBusterBlastPrefab(shot.ShotType), muzzle, shot);
    }

    private void PlayShootAnimatorTriggers()
    {
        if (animator == null)
            return;

        if (IsAimingUp() && isGrounded)
            animator.SetTrigger("ShootUp");
        else if (!isGrounded && rb != null && rb.linearVelocity.y >= 0f)
            animator.SetTrigger("JumpShot");
        else if (!isGrounded)
            animator.SetTrigger("FallShot");
        else
            animator.SetTrigger("Shoot");
    }

    private void SetupChargeAura()
    {
        if (!showChargeAura || spriteRenderer == null)
            return;

        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);
        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);

        chargeAuraObject = new GameObject($"{name}_ChargeAura");
        chargeAuraObject.transform.SetParent(transform, false);
        chargeAuraObject.transform.localPosition = Vector3.zero;
        chargeAuraObject.transform.localScale = Vector3.one * auraBaseScale;
        // Draw before Kit in hierarchy as a small extra hint; sorting order is what matters.
        chargeAuraObject.transform.SetAsFirstSibling();

        chargeAuraRenderer = chargeAuraObject.AddComponent<SpriteRenderer>();
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);
        chargeAuraRenderer.flipX = spriteRenderer.flipX;

        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            chargeAuraMaterial = new Material(solidShader);
            chargeAuraRenderer.sharedMaterial = chargeAuraMaterial;
        }

        Color c = chargeAuraColor;
        c.a = 0f;
        chargeAuraRenderer.color = c;
        chargeAuraObject.SetActive(false);
    }

    private void UpdateChargeAura()
    {
        if (!showChargeAura)
            return;

        if (chargeAuraRenderer == null || chargeAuraObject == null)
            SetupChargeAura();

        if (chargeAuraRenderer == null || spriteRenderer == null)
            return;

        float charge01 = Mathf.Clamp01(chargeTimer / Mathf.Max(0.01f, bigChargeSeconds));

        chargeAuraObject.SetActive(true);
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        chargeAuraRenderer.flipX = spriteRenderer.flipX;
        // Stay on the character's sorting plane so mid-order backgrounds cannot cover the aura.
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);

        // More solid (less transparent) as charge grows.
        float alpha;
        if (chargeTimer >= bigChargeSeconds)
            alpha = auraAlphaAtBig;
        else if (chargeTimer >= mediumChargeSeconds)
            alpha = Mathf.Lerp(auraAlphaAtMedium, auraAlphaAtBig, Mathf.InverseLerp(mediumChargeSeconds, bigChargeSeconds, chargeTimer));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtMedium, chargeTimer / Mathf.Max(0.01f, mediumChargeSeconds));

        // Soft equal pink/yellow shimmer — no hard flashes (photosensitivity-safe).
        // Rate stays well under 3 Hz even at full charge; yellow only gently tints pink.
        Color pink = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
        // Smooth sine: equal time toward pink and yellow, no abrupt swaps.
        float shimmer = 0.5f + 0.5f * Mathf.Sin(auraFlickerPhase * Mathf.PI * 2f);
        float yellowMix = shimmer * auraFlickerStrength * Mathf.Lerp(0.55f, 1f, charge01);
        Color auraColor = Color.Lerp(pink, chargeAuraFlickerColor, yellowMix);
        auraColor.a = alpha;
        chargeAuraRenderer.color = auraColor;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount * charge01;
        chargeAuraObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }

    private void SetChargeAuraVisible(bool visible)
    {
        if (chargeAuraObject == null)
            return;

        if (!visible)
        {
            chargeAuraObject.SetActive(false);
            auraFlickerPhase = 0f;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        mediumChargeSeconds = Mathf.Max(0.05f, mediumChargeSeconds);
        bigChargeSeconds = Mathf.Max(mediumChargeSeconds, bigChargeSeconds);
        mediumChargeMoveSpeedBonus = Mathf.Max(0f, mediumChargeMoveSpeedBonus);
        bigChargeMoveSpeedBonus = Mathf.Max(mediumChargeMoveSpeedBonus, bigChargeMoveSpeedBonus);
        shootCooldown = Mathf.Max(0f, shootCooldown);
        shotAnimWaitTimeout = Mathf.Max(0.05f, shotAnimWaitTimeout);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        auraSortingBehind = Mathf.Max(1, auraSortingBehind);
        auraAlphaStart = Mathf.Min(auraAlphaStart, auraAlphaAtMedium);
        auraAlphaAtMedium = Mathf.Min(auraAlphaAtMedium, auraAlphaAtBig);
        auraDelayAfterSmallShot = Mathf.Max(0f, auraDelayAfterSmallShot);
        // Keep shimmer slow/soft (photosensitivity: avoid hard >3 Hz flashes).
        auraFlickerSpeed = Mathf.Clamp(auraFlickerSpeed, 0.25f, 2.5f);
        auraFlickerStrength = Mathf.Clamp(auraFlickerStrength, 0.05f, 0.5f);
        kitDashDistance = Mathf.Max(0.1f, kitDashDistance);
        kitDashSpeed = Mathf.Max(0.1f, kitDashSpeed);
        kitDashSmoothStop = Mathf.Max(0f, kitDashSmoothStop);
        kitMaxAirDashes = Mathf.Max(0, kitMaxAirDashes);
        kitDashJumpMomentumDuration = Mathf.Max(0.01f, kitDashJumpMomentumDuration);
        kitAfterimageCount = Mathf.Max(1, kitAfterimageCount);
        kitAfterimageSpacing = Mathf.Max(0.01f, kitAfterimageSpacing);
        kitAfterimageAlphaEnd = Mathf.Min(kitAfterimageAlphaEnd, kitAfterimageAlphaStart);
    }
#endif
}
