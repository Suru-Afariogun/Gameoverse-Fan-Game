using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Count — Mega Man-style blaster with Short/Long Hand, attack styles, hyper speed (Clock), and Dual Override aura.
/// </summary>
public class CountPlayerController : PlayerController
{
    private struct RunShotTrack
    {
        public Projectile Shot;
        public Vector3 SpawnPos;
    }

    [Header("Count - Projectiles")]
    [SerializeField] private Projectile shortHandProjectilePrefab;
    [SerializeField] private Projectile longHandProjectilePrefab;
    [SerializeField] private float shootCooldown = 0.12f;
    [Tooltip("Short Hand buster muzzle flash.")]
    [SerializeField] private GameVisualEffect busterBlastSmallPrefab;
    [Tooltip("Long Hand buster muzzle flash.")]
    [SerializeField] private GameVisualEffect busterBlastMediumBigPrefab;

    [Header("Count - Charge")]
    [SerializeField] private float fullChargeSeconds = 3f;
    [SerializeField] private float spreadFullChargeSeconds = 3.45f;
    [SerializeField] private float chargeMoveSpeedBonus = 5f;
    [SerializeField] private float auraDelayAfterShortShot = 0.2f;
    [SerializeField] private CountChargeAuraVisual chargeAuraVisual;

    [Header("Count - Red Charge Hue")]
    [SerializeField] private bool showChargeHue = true;
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color chargeAuraStrongColor = new Color(1f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 0.75f, 0.75f, 1f);
    [SerializeField] private float auraBaseScale = 1.05f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtMedium = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtBig = 0.95f;

    [Header("Count - Health Up Clock")]
    [SerializeField] private CollectableHealthUpClock healthUpClockPrefab;

    [Header("Count - Animation")]
    [SerializeField] private float shootAnimDuration = 0.28f;
    [SerializeField] private float shotAnimWaitTimeout = 0.4f;
    [SerializeField] private float shootAnimMinNormalizedTime = 0.08f;
    [SerializeField] private float shootTransitionMinNormalizedTime = 0.85f;
    [SerializeField] private float firePointReadyMinDistance = 0.18f;
    [SerializeField] private float runShootInputThreshold = 0.01f;
    [SerializeField] private float runShootHoldDistance = 6f;
    [SerializeField] private string[] shootAnimStateNames =
    {
        "Ground shoot",
        "Ground Shoot aim up",
        "Jump Shoot",
        "Run Shoot"
    };

    [Header("Count - Attack Styles")]
    [Tooltip("Short Hand only — each of the two pellets angles this many degrees from aim (top/bottom when horizontal).")]
    [SerializeField] private float spreadShotAngleDegrees = 14f;
    [Tooltip("Long Hand charged spread fan half-width (+/- this angle).")]
    [SerializeField] private float spreadChargedMaxAngleDegrees = 50f;
    [SerializeField] private int spreadChargedShotCount = 6;
    [Tooltip("Short Hand pellets spawn slightly ahead so the pair is visible at the muzzle.")]
    [SerializeField] private float spreadShotMuzzleOffset = 0.12f;
    [SerializeField] private float machineGunFireInterval = 0.18f;
    [SerializeField] private int machineGunMaxLiveShots = 8;

    [Header("Count - Hyper Speed (Hold Up/Down + Dash)")]
    [SerializeField] private float hyperSpeedHoldSeconds = 0.3f;
    [Tooltip("Total active Hyper Speed time available (not tied to charge).")]
    [SerializeField] private float hyperSpeedMaxSeconds = 16f;
    [Tooltip("Cooldown after the full Hyper Speed duration is used up.")]
    [SerializeField] private float hyperSpeedCooldownSeconds = 3f;
    [Tooltip("Fire-interval multiplier while Hyper Speed is on cooldown (2 = half fire rate).")]
    [SerializeField] private float hyperSpeedCooldownFireRateMultiplier = 2f;
    [Tooltip("Max charge fraction while Hyper Speed is on cooldown (0.5 = halfway aura / no Long Hand).")]
    [SerializeField] [Range(0.1f, 0.9f)] private float hyperSpeedCooldownMaxChargeFraction = 0.5f;
    [SerializeField] [Range(0.05f, 1f)] private float hyperSpeedWorldTimeScale = 0.5f;
    [SerializeField] private string clockActivationTrigger = "ClockActivation";
    [SerializeField] private int countHyperSpeedAfterimageCount = 6;
    [SerializeField] [Range(0f, 1f)] private float hyperSpeedWorldRedTint = 0.28f;
    [SerializeField] private float hyperSpeedAuraActivityBoost = 2.5f;
    [SerializeField] private float auraDissipateDuration = 0.45f;
    [Tooltip("Extra dash speed scale during Up-hyper (on top of world time scale). Lower = slower dash.")]
    [SerializeField] [Range(0.15f, 1f)] private float hyperUpDashSpeedFactor = 0.72f;
    [Tooltip("Longer ease-out window during Up-hyper dashes.")]
    [SerializeField] private float hyperUpDashSmoothStop = 0.38f;
    [Tooltip("Extra dash travel distance during Up-hyper only (world units).")]
    [SerializeField] private float hyperUpDashDistanceBonus = 0.85f;

    [Header("Count - Dash")]
    [SerializeField] private float countDashDistance = 5.25f;
    [SerializeField] private float countDashSpeed = 17f;
    [SerializeField] private float countDashSmoothStop = 0.14f;
    [SerializeField] private int countMaxAirDashes = 2;
    [SerializeField] private bool countAllowDashJump = true;
    [SerializeField] private float countDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool countDashJumpMomentumUntilLanded = true;

    [Header("Count - Afterimages")]
    [SerializeField] private bool countEnableDashAfterimages = true;
    [SerializeField] private int countAfterimageCount = 3;
    [SerializeField] private float countAfterimageSpacing = 0.1f;
    [SerializeField] private Color countAfterimageColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] [Range(0f, 1f)] private float countAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float countAfterimageAlphaEnd = 0.15f;

    [Header("Count - Ariel Action Float (hold Jump in the air)")]
    [Tooltip("Ariel Action level that unlocks the float.")]
    [SerializeField] private int floatMinAerialLevel = 2;
    [Tooltip("Max fall speed while floating (world units per second).")]
    [SerializeField] private float floatFallSpeed = 1.6f;

    [Header("Count - Hyper Ability (full health)")]
    [Tooltip("Extra Hyper Speed seconds per Hyper Ability level.")]
    [SerializeField] private float hyperSpeedSecondsPerLevel = 1f;
    [Tooltip("Extra damage on every shot per Hyper Ability level.")]
    [SerializeField] private int hyperDamagePerLevel = 1;

    [Header("Count - Rewind (Hyper Ability, hold Dash without Up/Down; works at any HP)")]
    [SerializeField] private int rewindMinHyperLevel = 2;
    [SerializeField] private float rewindHoldSeconds = 0.8f;
    [Tooltip("Seconds rewound at the unlock level.")]
    [SerializeField] private float rewindBaseSeconds = 2f;
    [Tooltip("Extra seconds rewound per level above the unlock level.")]
    [SerializeField] private float rewindSecondsPerLevel = 1f;
    [SerializeField] private float rewindCooldownSeconds = 8f;
    [SerializeField] private float rewindFlashSeconds = 0.35f;
    [SerializeField] [Range(0f, 1f)] private float rewindRedTint = 0.4f;
    [SerializeField] private int rewindGhostCount = 6;

    [Header("Count - Time Clones (Attack Style, Spread / Machine Gun only)")]
    [Tooltip("Attack Style level where dashing leaves a Time Clone (the last afterimage).")]
    [SerializeField] private int timeCloneMinStyleLevel = 2;
    [Tooltip("Max live clones at the unlock level; +1 per level after. At the cap the oldest clone vanishes.")]
    [SerializeField] private int timeCloneBaseCap = 3;
    [SerializeField] private int timeCloneHealth = 10;
    [SerializeField] private float timeCloneShootInterval = 0.6f;
    [SerializeField] private float timeCloneSearchRadius = 14f;
    [Tooltip("Machine Gun clones fire this many pellets per volley.")]
    [SerializeField] private int timeCloneMachineGunPellets = 3;
    [SerializeField] private float timeCloneMachineGunGap = 0.1f;
    [SerializeField] private Color timeCloneColor = new Color(1f, 0.45f, 0.45f, 0.8f);
    [Tooltip("Attack Style level where Count takes over his newest clone instead of dying.")]
    [SerializeField] private int lastCloneStyleLevel = 5;
    [SerializeField] private int lastCloneHealth = 10;
    [SerializeField] private Color lastCloneBodyColor = new Color(1f, 0.62f, 0.62f, 1f);

    private struct RewindSample
    {
        public float Time;
        public Vector3 Position;
        public int Health;
        public Sprite Sprite;
        public bool FlipX;
    }

    private readonly List<RewindSample> rewindHistory = new List<RewindSample>(512);
    private float rewindHoldTimer;
    private float rewindReadyAt;
    private float rewindTintReleaseAt = -1f;
    private readonly List<CountTimeClone> timeClones = new List<CountTimeClone>(8);
    private bool playingAsLastClone;
    private bool floating;
    private bool floatTrailUntilLanded;
    private float lastHyperPool;

    private bool isCharging;
    private float chargeTimer;
    private float shootCooldownTimer;
    private float auraAllowedAfterTime;

    private bool pendingShot;
    private bool pendingLongHand;
    private bool pendingRunShoot;
    private bool spawningRunShot;
    private float pendingShotDeadline;
    private Vector2 pendingShotAim;

    private bool machineGunHolding;
    private float machineGunAutoCharge;
    private bool machineGunFirstShotQueued;
    private bool spreadHolding;
    private float spreadAutoCharge;
    private int machineGunPelletIndex;
    private readonly List<Projectile> liveMachineGunShots = new List<Projectile>(16);
    private readonly List<RunShotTrack> runShotTracks = new List<RunShotTrack>(8);

    private float hyperSpeedRemaining;
    private float hyperSpeedDuration;
    private bool hyperSpeedActive;
    private bool hyperSpeedCountMovesNormal;
    private float hyperSpeedHoldTimer;
    private float hyperSpeedCooldownRemaining;
    private float hyperSpeedCancelGraceRemaining;
    private bool hyperSpeedPoolExhaustedForCooldown;

    private bool runShootAnimEngaged;

    private Vector3 firePointRestLocal;
    private int savedDashAfterimageCount;
    private CountHyperSpeedWorldTint hyperSpeedWorldTint;
    private float auraHueDissipateEndTime;
    private float auraHueDissipateStartAlpha;
    private bool auraHueDissipating;

    private GameObject chargeHueObject;
    private SpriteRenderer chargeHueRenderer;
    private Material chargeHueMaterial;
    private float auraFlickerPhase;

    public bool IsHyperSpeedActive => hyperSpeedActive && hyperSpeedRemaining > 0f;
    public bool HyperSpeedCountMovesNormal => IsHyperSpeedActive && hyperSpeedCountMovesNormal;
    public bool IsHyperSpeedOnCooldown => hyperSpeedCooldownRemaining > 0f;
    public bool IsFloating => floating;
    public bool IsPlayingAsLastClone => playingAsLastClone;

    public override bool WeaponGearBoostsShots => true;

    private int AerialLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);
    private int HyperLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.HyperAbility);
    private int StyleLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AttackStyle);
    private int HyperBonusLevel => PlayerUpgrades.IsHyperActive(this) ? HyperLevel : 0;
    private float HyperSpeedPoolSeconds =>
        hyperSpeedMaxSeconds + HyperBonusLevel * Mathf.Max(0f, hyperSpeedSecondsPerLevel);
    private bool RewindUnlocked => HyperLevel >= Mathf.Max(1, rewindMinHyperLevel);
    private float RewindSeconds =>
        rewindBaseSeconds + Mathf.Max(0, HyperLevel - rewindMinHyperLevel) * Mathf.Max(0f, rewindSecondsPerLevel);
    private bool TimeClonesUnlocked =>
        StyleLevel >= Mathf.Max(1, timeCloneMinStyleLevel) && (UsesSpreadShotStyle() || UsesMachineGunStyle());
    private int TimeCloneCap => Mathf.Max(1, timeCloneBaseCap + Mathf.Max(0, StyleLevel - timeCloneMinStyleLevel));

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Count");

        dashDistance = countDashDistance;
        dashSpeed = countDashSpeed;
        dashSmoothStopDuration = countDashSmoothStop;
        maxAirDashes = countMaxAirDashes;
        allowDashJump = countAllowDashJump;
        dashJumpMomentumDuration = countDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = countDashJumpMomentumUntilLanded;
        enableDashAfterimages = countEnableDashAfterimages;
        dashAfterimageCount = countAfterimageCount;
        dashAfterimageSpacing = countAfterimageSpacing;
        dashAfterimageColor = countAfterimageColor;
        dashAfterimageAlphaStart = countAfterimageAlphaStart;
        dashAfterimageAlphaEnd = countAfterimageAlphaEnd;
        airDashesRemaining = MaxAirDashesWithUpgrades;

        if (chargeAuraVisual == null)
            chargeAuraVisual = GetComponentInChildren<CountChargeAuraVisual>(true);

        if (firePoint != null)
        {
            firePointRestLocal = firePoint.localPosition;
            firePoint.gameObject.SetActive(false);
        }

        hyperSpeedWorldTint = GetComponent<CountHyperSpeedWorldTint>();
        if (hyperSpeedWorldTint == null)
            hyperSpeedWorldTint = gameObject.AddComponent<CountHyperSpeedWorldTint>();

        SetupDashAfterimages();
        SetupChargeHue();

        lastHyperPool = HyperSpeedPoolSeconds;
        hyperSpeedRemaining = lastHyperPool;
        hyperSpeedDuration = lastHyperPool;
    }

    protected override void OnDestroy()
    {
        VanishAllTimeClones();
        hyperSpeedWorldTint?.Release();
        EndHyperSpeed(force: true);
        if (chargeHueObject != null)
            Destroy(chargeHueObject);
        if (chargeHueMaterial != null)
            Destroy(chargeHueMaterial);
        base.OnDestroy();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearCountExtras();
    }

    protected override void ClearForVehicleRide()
    {
        isCharging = false;
        ClearCountExtras();
        base.ClearForVehicleRide();
    }

    private void ClearCountExtras()
    {
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
        spreadHolding = false;
        spreadAutoCharge = 0f;
        liveMachineGunShots.Clear();
        runShotTracks.Clear();
        CancelPendingShot();
        EndHyperSpeed(force: true);
        SetChargeAuraVisible(false, instant: true);
        VanishAllTimeClones();
        rewindHistory.Clear();
        rewindHoldTimer = 0f;
        floating = false;
        if (rewindTintReleaseAt > 0f)
        {
            rewindTintReleaseAt = -1f;
            hyperSpeedWorldTint?.Release();
        }
    }

    protected override void HandleCharacterUpdate()
    {
        float gameplayDt = CountSlowsWithHyperWorld() ? HyperSpeedWorldSlow.WorldDeltaTime : Time.deltaTime;

        if (shootCooldownTimer > 0f)
            shootCooldownTimer -= gameplayDt;

        TickFloat();
        RecordRewindHistory();
        TickRewindHold();
        TickRewindTint();
        SyncHyperSpeedPool();
        TickHyperSpeed(Time.unscaledDeltaTime);
        TickHyperSpeedCooldown(Time.unscaledDeltaTime);
        TickHyperSpeedHold();

        if (UsesMachineGunStyle())
            TickMachineGunStyle(gameplayDt);
        else if (UsesSpreadShotStyle())
            TickSpreadShotStyle(gameplayDt);
        else if (isCharging && !IsHyperSpeedActive)
        {
            chargeTimer += gameplayDt;
            chargeTimer = Mathf.Min(chargeTimer, GetMaxChargeSeconds());
            if (Time.time >= auraAllowedAfterTime)
            {
                isShooting = false;
                shootAnimTimer = 0f;
                UpdateChargeAura(chargeTimer);
            }
            else
            {
                SetChargeAuraVisible(false);
            }
        }
        else if (!IsHyperSpeedActive)
        {
            SetChargeAuraVisible(false, instant: true);
        }

        PruneRunShotTracks();
        TickRunShootAnimState();
        UpdateFirePointGate();
        TickAuraHueDissipate();
        TickPendingShot();
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();

        if (HyperSpeedCountMovesNormal)
            return speed + chargeMoveSpeedBonus;

        if (CountSlowsWithHyperWorld())
            speed *= HyperSpeedWorldSlow.WorldTimeScale;

        if (!IsChargeAuraActive())
            return speed;

        if (GetCurrentChargeSeconds() >= GetActiveFullChargeSeconds())
            return speed + chargeMoveSpeedBonus;

        return speed;
    }

    protected override float GetCurrentDashVelocityX()
    {
        if (!CountSlowsWithHyperWorld())
            return base.GetCurrentDashVelocityX();

        float scale = HyperSpeedWorldSlow.WorldTimeScale * Mathf.Clamp(hyperUpDashSpeedFactor, 0.15f, 1f);
        float peakSpeed = dashSpeed * scale;
        float peak = dashDirSign * peakSpeed;
        float stopWindow = Mathf.Min(
            Mathf.Max(dashSmoothStopDuration, hyperUpDashSmoothStop),
            dashDuration * 0.7f);

        if (stopWindow <= 0.0001f || dashTimer < dashDuration - stopWindow)
            return peak;

        float t = Mathf.InverseLerp(dashDuration - stopWindow, dashDuration, dashTimer);
        t = t * t * (3f - 2f * t);
        float end = dashDirSign * peakSpeed * dashEndSpeedMultiplier;
        return Mathf.Lerp(peak, end, t);
    }

    protected override void OnDashStarted()
    {
        base.OnDashStarted();
        if (!CountSlowsWithHyperWorld())
            return;

        // Slightly longer travel at slowed Up-hyper dash speed.
        float scale = HyperSpeedWorldSlow.WorldTimeScale * Mathf.Clamp(hyperUpDashSpeedFactor, 0.15f, 1f);
        float slowSpeed = Mathf.Max(0.1f, dashSpeed * scale);
        float travel = dashDistance + Mathf.Max(0f, hyperUpDashDistanceBonus);
        dashDuration = Mathf.Max(0.05f, travel / slowSpeed);
        BeginDashAfterimageTrail(dashDuration);
    }

    protected override float GetDashJumpHorizontalSpeed()
    {
        float speed = base.GetDashJumpHorizontalSpeed();
        if (!CountSlowsWithHyperWorld())
            return speed;

        return speed * HyperSpeedWorldSlow.WorldTimeScale * Mathf.Clamp(hyperUpDashSpeedFactor, 0.15f, 1f);
    }

    protected override void ApplyJump()
    {
        base.ApplyJump();
    }

    protected override int UpgradeAirJumps => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);

    /// <summary>Hyper Ability (full health): every press fires a full charge shot, unless Hyper Speed's cooldown weakness is on.</summary>
    private bool HyperMaxChargeReady => PlayerUpgrades.IsHyperActive(this) && !IsHyperSpeedOnCooldown;

    protected override void OnLanded()
    {
        base.OnLanded();
        floatTrailUntilLanded = false;
    }

    protected override void OnDashEnded()
    {
        base.OnDashEnded();
        TrySpawnTimeClone();
    }

    protected override void OnTimeFrozenChanged(bool frozen)
    {
        if (!frozen)
            return;

        // Someone else stopped time: Hyper Speed ends (two red washes would restore colors in the wrong order).
        if (hyperSpeedActive)
            EndHyperSpeed(force: true);
        if (rewindTintReleaseAt >= 0f)
        {
            rewindTintReleaseAt = -1f;
            hyperSpeedWorldTint?.Release();
        }
        rewindHoldTimer = 0f;
        floating = false;
    }

    public override void TakeDamage(int amount, Transform hitSource, bool applyKnockback)
    {
        if (TryAutoUseItemAgainstHit(amount))
            return;

        if (amount > 0 && amount >= currentHealth && currentHealth > 0 && !IsInvincible && !IsRidingVehicle &&
            !IsScriptedInvulnerable && !IsStarPowered && TryBecomeLastClone())
            return;

        base.TakeDamage(amount, hitSource, applyKnockback);
    }

    public override void SetHealth(int value)
    {
        if (value <= 0 && currentHealth > 0 && TryBecomeLastClone())
            return;

        base.SetHealth(value);
    }

    // ---------- Ariel Action: Float ----------

    /// <summary>Hold Jump in the air to drift down slowly; afterimages trail until he lands.</summary>
    private void TickFloat()
    {
        bool wants = AerialLevel >= Mathf.Max(1, floatMinAerialLevel) &&
                     !isGrounded && !isDashing && !isStunned && !inputLocked && !IsRidingVehicle && !IsDead &&
                     controls != null && controls.PlayerControls.Jump.IsPressed();

        if (wants && !floating)
        {
            floatTrailUntilLanded = true;
            BeginDashAfterimageTrail(GetMaxAfterimageDelay());
        }

        floating = wants;
        if (floatTrailUntilLanded && !isGrounded)
            afterimagesVisibleUntil = Mathf.Max(afterimagesVisibleUntil,
                Time.time + GetMaxAfterimageDelay() + dashAfterimageLifetimePadding);
    }

    // ---------- Hyper Ability: Rewind ----------

    private void RecordRewindHistory()
    {
        if (!RewindUnlocked || IsDead || spriteRenderer == null)
        {
            if (rewindHistory.Count > 0)
                rewindHistory.Clear();
            return;
        }

        rewindHistory.Add(new RewindSample
        {
            Time = Time.time,
            Position = transform.position,
            Health = currentHealth,
            Sprite = spriteRenderer.sprite,
            FlipX = spriteRenderer.flipX
        });

        float cutoff = Time.time - (RewindSeconds + 0.25f);
        int expired = 0;
        while (expired < rewindHistory.Count && rewindHistory[expired].Time < cutoff)
            expired++;
        if (expired > 0)
            rewindHistory.RemoveRange(0, expired);
    }

    private void TickRewindHold()
    {
        if (!RewindUnlocked || IsDead || inputLocked || isStunned || IsRidingVehicle || Time.time < rewindReadyAt)
        {
            rewindHoldTimer = 0f;
            return;
        }

        bool directionHeld = moveInput.y >= upAimThreshold || moveInput.y <= -upAimThreshold;
        if (!IsDashHeld() || directionHeld)
        {
            rewindHoldTimer = 0f;
            return;
        }

        rewindHoldTimer += Time.unscaledDeltaTime;
        if (rewindHoldTimer < rewindHoldSeconds)
            return;

        rewindHoldTimer = 0f;
        PerformRewind();
    }

    /// <summary>Jumps back to where he stood <see cref="RewindSeconds"/> ago and takes back HP lost since then.</summary>
    private void PerformRewind()
    {
        if (rewindHistory.Count == 0)
            return;

        float targetTime = Time.time - RewindSeconds;
        int index = 0;
        for (int i = rewindHistory.Count - 1; i >= 0; i--)
        {
            if (rewindHistory[i].Time <= targetTime)
            {
                index = i;
                break;
            }
        }

        RewindSample sample = rewindHistory[index];
        SpawnRewindGhosts(index);

        CancelAllDashState();
        CancelPendingShot();
        TeleportTo(sample.Position);

        if (sample.Health > currentHealth)
            SetHealth(sample.Health);

        rewindHistory.Clear();
        rewindReadyAt = Time.time + Mathf.Max(0f, rewindCooldownSeconds);
        FlashTimeTint();
        SoundManager.Instance?.PlayClockworkTic();
    }

    private void SpawnRewindGhosts(int oldestIndex)
    {
        int newest = rewindHistory.Count - 1;
        int count = Mathf.Max(0, rewindGhostCount);
        if (count == 0 || newest <= oldestIndex)
            return;

        Color ghostColor = countAfterimageColor;
        ghostColor.a = 0.6f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : i / (float)(count - 1);
            RewindSample s = rewindHistory[Mathf.RoundToInt(Mathf.Lerp(newest, oldestIndex, t))];
            CountTimeGhost.Spawn(s.Position, s.Sprite, s.FlipX, ghostColor, spriteRenderer, EffectSortingGroup,
                i * 0.035f, 0.35f);
        }
    }

    private void TeleportTo(Vector3 position)
    {
        transform.position = position;
        if (rb == null)
            return;

        rb.position = position;
        rb.linearVelocity = Vector2.zero;
    }

    private void FlashTimeTint()
    {
        if (hyperSpeedActive || hyperSpeedWorldTint == null)
            return;

        hyperSpeedWorldTint.Apply(transform, rewindRedTint);
        rewindTintReleaseAt = Time.time + Mathf.Max(0.05f, rewindFlashSeconds);
    }

    private void TickRewindTint()
    {
        if (rewindTintReleaseAt < 0f || Time.time < rewindTintReleaseAt)
            return;

        rewindTintReleaseAt = -1f;
        if (!hyperSpeedActive)
            hyperSpeedWorldTint?.Release();
    }

    // ---------- Attack Style: Time Clones ----------

    /// <summary>The last afterimage of a dash stays behind as a Time Clone.</summary>
    private void TrySpawnTimeClone()
    {
        if (!TimeClonesUnlocked || IsDead || shortHandProjectilePrefab == null || spriteRenderer == null)
            return;

        if (!TrySamplePose(GetMaxAfterimageDelay(), out PoseSample sample))
            return;

        PruneTimeClones();
        while (timeClones.Count >= TimeCloneCap)
        {
            CountTimeClone oldest = timeClones[0];
            timeClones.RemoveAt(0);
            if (oldest != null)
                oldest.Vanish();
        }

        CountTimeClone clone = CountTimeClone.Spawn(
            this,
            sample.position,
            sample.sprite != null ? sample.sprite : spriteRenderer.sprite,
            sample.flipX,
            timeCloneColor,
            shortHandProjectilePrefab,
            timeCloneHealth,
            timeCloneShootInterval,
            timeCloneSearchRadius,
            spreadShotAngleDegrees,
            timeCloneMachineGunPellets,
            timeCloneMachineGunGap);

        if (clone != null)
            timeClones.Add(clone);
    }

    private void PruneTimeClones()
    {
        for (int i = timeClones.Count - 1; i >= 0; i--)
        {
            if (timeClones[i] == null || timeClones[i].IsDead)
                timeClones.RemoveAt(i);
        }
    }

    private void VanishAllTimeClones()
    {
        for (int i = 0; i < timeClones.Count; i++)
        {
            if (timeClones[i] != null)
                timeClones[i].Vanish();
        }

        timeClones.Clear();
    }

    /// <summary>
    /// Attack Style max level: a lethal hit moves Count into his newest clone instead. The other clones vanish and he
    /// keeps fighting with base abilities only (no upgrades) until he goes down again.
    /// </summary>
    private bool TryBecomeLastClone()
    {
        if (playingAsLastClone || StyleLevel < Mathf.Max(1, lastCloneStyleLevel))
            return false;

        PruneTimeClones();
        if (timeClones.Count == 0)
            return false;

        Vector3 position = timeClones[timeClones.Count - 1].transform.position;
        VanishAllTimeClones();

        // Before CancelAllDashState: ending a dash would otherwise leave a fresh clone behind.
        playingAsLastClone = true;
        UpgradesSuppressed = true;

        EndHyperSpeed(force: true);
        CancelAllDashState();
        CancelPendingShot();
        isCharging = false;
        chargeTimer = 0f;
        machineGunHolding = false;
        spreadHolding = false;
        SoundManager.Instance?.StopChargeLoop();
        SetChargeAuraVisible(false, instant: true);
        TeleportTo(position);

        rewindHistory.Clear();
        floating = false;
        floatTrailUntilLanded = false;
        airDashesRemaining = Mathf.Min(airDashesRemaining, MaxAirDashesWithUpgrades);

        base.SetHealth(Mathf.Max(1, Mathf.Min(lastCloneHealth, maxHealth)));
        invincibilityTimer = Mathf.Max(invincibilityTimer, hitInvincibilityDuration);
        if (spriteRenderer != null)
            spriteRenderer.color = lastCloneBodyColor;

        FlashTimeTint();
        SoundManager.Instance?.PlayClockworkToc();
        return true;
    }

    protected override void PerformDashJump()
    {
        jumpRequested = false;
        CancelDash(keepHorizontalMomentum: true);

        float jumpUp = dashJumpForce > 0f ? dashJumpForce : jumpForce;
        float jumpX = dashDirSign * GetDashJumpHorizontalSpeed();
        rb.linearVelocity = new Vector2(jumpX, jumpUp);
        SoundManager.Instance?.PlayCountJump();

        isDashJumping = true;
        dashJumpMomentumTimer = dashJumpMomentumUntilLanded
            ? float.PositiveInfinity
            : Mathf.Max(0.01f, dashJumpMomentumDuration);

        BeginDashAfterimageTrail(Mathf.Max(dashJumpMomentumDuration, GetMaxAfterimageDelay()));

        if (animator != null)
            animator.SetTrigger("Jump");
    }

    protected override void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded || rb.linearVelocity.y >= 0f)
            return;

        if (floating)
        {
            float cap = -Mathf.Max(0.1f, floatFallSpeed);
            if (rb.linearVelocity.y < cap)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, cap);
            return;
        }

        float mult = fallMultiplier;
        if (CountSlowsWithHyperWorld())
            mult = HyperSpeedWorldSlow.ScaleFallMultiplier(fallMultiplier);

        rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (mult - 1f) * Time.fixedDeltaTime;
    }

    private bool CountSlowsWithHyperWorld() =>
        IsHyperSpeedActive && !hyperSpeedCountMovesNormal;

    protected override void TryBeginDash()
    {
        // Allow dashing while Hyper Speed is active. Cancel is hold Up/Down+Dash via TickHyperSpeedHold.
        if (ShouldBlockDashForHyperHold())
            return;

        base.TryBeginDash();
    }

    protected override void ApplyHorizontalMove()
    {
        if (rb == null)
            return;

        // Kit-style shoot lock: freeze walk input, but keep riding moving platforms.
        bool chargeMoveUnlocked = IsChargeAuraActive();
        bool aimUpLock = IsAimingUp() && isGrounded &&
                         (isShooting || pendingShot || isCharging || machineGunHolding || spreadHolding);
        bool groundShootLock = isGrounded && !chargeMoveUnlocked && !ShouldHoldRunShootAnimation() &&
                               (isShooting || pendingShot || (isCharging && !chargeMoveUnlocked) || machineGunHolding);

        if (aimUpLock || groundShootLock)
        {
            ApplyStandInPlaceMove();
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        if (ShouldHoldRunShootAnimation())
            return false;

        return false;
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();

        if (animator == null)
            return;

        if (ShouldHoldRunShootAnimation())
        {
            bool moving = Mathf.Abs(moveInput.x) > runShootInputThreshold && !isDashing;
            animator.SetBool("IsMoving", moving);
            animator.SetBool("IsShooting", true);
        }
    }

    protected override Vector2 GetAimDirection()
    {
        if (!isGrounded && moveInput.y >= upAimThreshold)
            return new Vector2(facingSign, 0f);

        return base.GetAimDirection();
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (IsHyperSpeedActive)
        {
            if (UsesMachineGunStyle())
                BeginMachineGunFire();
            else if (UsesSpreadShotStyle())
                BeginSpreadFire();
            else
                QueueShot(longHand: false);
            return;
        }

        if (UsesMachineGunStyle())
        {
            BeginMachineGunFire();
            return;
        }

        if (UsesSpreadShotStyle())
        {
            spreadHolding = true;
            BeginSpreadFire();
            return;
        }

        if (HyperMaxChargeReady)
        {
            if (shootCooldownTimer <= 0f && !pendingShot)
                QueueShot(longHand: true, replacePending: true);
            return;
        }

        isCharging = true;
        chargeTimer = 0f;
        auraAllowedAfterTime = float.PositiveInfinity;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StartChargeLoop(SoundManager.ChargeLoopId.Count);
        QueueShot(longHand: false);
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (UsesMachineGunStyle())
        {
            EndMachineGunFire();
            return;
        }

        if (UsesSpreadShotStyle())
        {
            spreadHolding = false;
            return;
        }

        if (InputLocked || isStunned || !isCharging)
            return;

        float held = chargeTimer;
        isCharging = false;
        chargeTimer = 0f;
        SoundManager.Instance?.StopChargeLoop();

        if (held >= GetActiveFullChargeSeconds() && !IsHyperSpeedOnCooldown)
        {
            BeginAuraDissipate();
            QueueShot(longHand: true, replacePending: true);
        }
        else
            SetChargeAuraVisible(false, instant: true);
    }

    protected override void OnHitStunStarted()
    {
        isCharging = false;
        chargeTimer = 0f;
        spreadHolding = false;
        spreadAutoCharge = 0f;
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
        pendingShot = false;
        runShotTracks.Clear();
        runShootAnimEngaged = false;
        SoundManager.Instance?.StopChargeLoop();
        SetChargeAuraVisible(false, instant: true);
        isShooting = false;
        shootAnimTimer = 0f;
        base.OnHitStunStarted();
    }

    private bool UsesMachineGunStyle() => PlayerAttackStyle.Is(AttackStyleId.MachineGun);
    private bool UsesSpreadShotStyle() => PlayerAttackStyle.Is(AttackStyleId.SpreadShot);

    private float GetActiveFullChargeSeconds()
    {
        return UsesSpreadShotStyle() ? spreadFullChargeSeconds : fullChargeSeconds;
    }

    /// <summary>
    /// Charge ceiling: half of full while Hyper Speed cooldown is active (temporary weakness).
    /// </summary>
    private float GetMaxChargeSeconds()
    {
        float full = GetActiveFullChargeSeconds();
        if (!IsHyperSpeedOnCooldown)
            return full;

        return full * Mathf.Clamp(hyperSpeedCooldownMaxChargeFraction, 0.1f, 0.9f);
    }

    private float GetHyperSpeedCooldownFireMultiplier()
    {
        return IsHyperSpeedOnCooldown
            ? Mathf.Max(1f, hyperSpeedCooldownFireRateMultiplier)
            : 1f;
    }

    private float GetShortHandShootCooldown() =>
        shootCooldown * GetHyperSpeedCooldownFireMultiplier();

    private float GetMachineGunFireInterval() =>
        Mathf.Max(0.05f, machineGunFireInterval) * GetHyperSpeedCooldownFireMultiplier();

    private void ClampChargeToCooldownCap()
    {
        float cap = GetMaxChargeSeconds();
        if (chargeTimer > cap)
            chargeTimer = cap;
        if (spreadAutoCharge > cap)
            spreadAutoCharge = cap;
        if (machineGunAutoCharge > cap)
            machineGunAutoCharge = cap;
    }

    private float GetCurrentChargeSeconds()
    {
        if (UsesMachineGunStyle())
            return machineGunAutoCharge;

        if (UsesSpreadShotStyle())
            return spreadAutoCharge;

        return isCharging ? chargeTimer : 0f;
    }

    private bool IsChargeAuraActive()
    {
        if (IsHyperSpeedActive)
            return true;

        if (UsesMachineGunStyle())
            return !machineGunHolding && machineGunAutoCharge > 0.01f;

        if (UsesSpreadShotStyle())
            return !spreadHolding && spreadAutoCharge > 0.01f;

        return isCharging && Time.time >= auraAllowedAfterTime;
    }

    private void TickSpreadShotStyle(float dt)
    {
        if (InputLocked || isStunned)
        {
            spreadHolding = false;
            spreadAutoCharge = 0f;
            SetChargeAuraVisible(false, instant: true);
            return;
        }

        if (IsHyperSpeedActive && !spreadHolding)
        {
            SetChargeAuraVisible(false, instant: true);
            return;
        }

        if (spreadHolding)
        {
            SetChargeAuraVisible(false);
            return;
        }

        spreadAutoCharge += dt;
        spreadAutoCharge = Mathf.Min(spreadAutoCharge, GetMaxChargeSeconds());
        chargeTimer = spreadAutoCharge;
        isCharging = false;

        if (spreadAutoCharge >= 0.12f)
            UpdateChargeAura(spreadAutoCharge);
        else
            SetChargeAuraVisible(false);
    }

    private void BeginSpreadFire()
    {
        spreadHolding = true;
        bool longHand = HyperMaxChargeReady ||
                        (!IsHyperSpeedOnCooldown && spreadAutoCharge >= GetActiveFullChargeSeconds());
        spreadAutoCharge = 0f;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StopChargeLoop();
        QueueShot(longHand, replacePending: true);
    }

    private void TickMachineGunStyle(float dt)
    {
        if (InputLocked || isStunned)
        {
            EndMachineGunFire();
            SetChargeAuraVisible(false);
            return;
        }

        if (IsHyperSpeedActive && !machineGunHolding)
        {
            SetChargeAuraVisible(false);
            return;
        }

        if (machineGunHolding)
        {
            SetChargeAuraVisible(false);
            if (!machineGunFirstShotQueued && !pendingShot && shootCooldownTimer <= 0f)
                FireMachineGunPellet();
            return;
        }

        machineGunAutoCharge += dt;
        machineGunAutoCharge = Mathf.Min(machineGunAutoCharge, GetMaxChargeSeconds());
        chargeTimer = machineGunAutoCharge;
        isCharging = false;

        if (machineGunAutoCharge >= 0.12f)
            UpdateChargeAura(machineGunAutoCharge);
        else
            SetChargeAuraVisible(false);
    }

    private void BeginMachineGunFire()
    {
        machineGunHolding = true;
        machineGunPelletIndex = 0;
        bool longHand = HyperMaxChargeReady ||
                        (!IsHyperSpeedOnCooldown && machineGunAutoCharge >= GetActiveFullChargeSeconds());
        machineGunAutoCharge = 0f;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StopChargeLoop();
        machineGunFirstShotQueued = true;
        QueueShot(longHand, replacePending: true);
    }

    private void EndMachineGunFire()
    {
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
        runShootAnimEngaged = false;
    }

    private void FireMachineGunPellet()
    {
        if (shortHandProjectilePrefab == null || pendingShot)
            return;

        QueueShot(longHand: false, replacePending: true);
    }

    private void QueueShot(bool longHand, bool replacePending = false)
    {
        Projectile prefab = longHand ? longHandProjectilePrefab : shortHandProjectilePrefab;
        if (prefab == null)
            return;

        if (!longHand && shootCooldownTimer > 0f && !replacePending)
            return;

        if (pendingShot && !replacePending && !longHand)
            return;

        CancelPendingShot();

        pendingShot = true;
        pendingLongHand = longHand;
        pendingRunShoot = false;
        pendingShotAim = GetAimDirection();
        pendingShotDeadline = Time.time + Mathf.Max(0.05f, shotAnimWaitTimeout);

        bool sustainRunShoot = ShouldSustainRunShootAnimation();
        if (sustainRunShoot)
        {
            isShooting = true;
            shootAnimTimer = Mathf.Max(shootAnimTimer, machineGunHolding ? GetMachineGunFireInterval() : shootAnimDuration);
            PlayShootAnimatorTriggers();
            return;
        }

        BeginShootAnimation(Mathf.Max(shootAnimDuration, shotAnimWaitTimeout));
        PlayShootAnimatorTriggers();
    }

    private bool ShouldSustainRunShootAnimation()
    {
        return WantsRunShootContext() && (IsInRunShootAnimation() || runShootAnimEngaged);
    }

    private bool WantsRunShootContext()
    {
        return isGrounded && !IsAimingUp() && Mathf.Abs(moveInput.x) > runShootInputThreshold;
    }

    private void TickRunShootAnimState()
    {
        if (!machineGunHolding && !pendingShot && !isShooting)
            runShootAnimEngaged = false;

        if (runShootAnimEngaged && !machineGunHolding && animator != null
            && !IsInRunShootAnimation() && !animator.IsInTransition(0))
        {
            runShootAnimEngaged = false;
        }
    }

    private void TickPendingShot()
    {
        if (!pendingShot)
            return;

        if (Time.time >= pendingShotDeadline)
        {
            CancelPendingShot();
            return;
        }

        if (!IsInShootAnimation() || !IsFirePointReady())
            return;

        FirePendingShot();
    }

    private void FirePendingShot()
    {
        if (!pendingShot || !CanSpawnFromFirePoint())
            return;

        bool longHand = pendingLongHand;
        bool runShoot = pendingRunShoot;
        Vector2 aim = pendingShotAim;
        pendingShot = false;

        spawningRunShot = runShoot;
        SpawnShotBurst(aim, longHand, trackMachineGun: false);
        spawningRunShot = false;

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
        if (longHand)
            ReportTutorialAction(TutorialAction.ChargeAttack);

        if (runShoot)
        {
            isShooting = true;
            shootAnimTimer = Mathf.Max(shootAnimTimer, 0.35f);
        }

        shootCooldownTimer = machineGunHolding
            ? GetMachineGunFireInterval()
            : GetShortHandShootCooldown();

        if (machineGunHolding)
            SoundManager.Instance?.PlayCountFireRapid();
        else
            SoundManager.Instance?.PlayCountFire(longHand ? ProjectileShotType.Big : ProjectileShotType.Small);

        if (longHand && !machineGunHolding && !IsHyperSpeedActive)
            BeginAuraDissipate();

        if (machineGunHolding)
            machineGunFirstShotQueued = false;
        else if (!longHand)
            auraAllowedAfterTime = Time.time + Mathf.Max(0f, auraDelayAfterShortShot);
    }

    private void CancelPendingShot() => pendingShot = false;

    private void SpawnShotBurst(Vector2 aim, bool longHand, bool trackMachineGun)
    {
        if (UsesSpreadShotStyle())
        {
            if (longHand)
            {
                int count = Mathf.Max(2, spreadChargedShotCount);
                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? 0.5f : i / (float)(count - 1);
                    float angle = Mathf.Lerp(spreadChargedMaxAngleDegrees, -spreadChargedMaxAngleDegrees, t);
                    SpawnProjectile(RotateAim(aim, angle), longHand, trackMachineGun);
                }
            }
            else
            {
                float angle = spreadShotAngleDegrees;
                SpawnProjectile(RotateAim(aim, angle), false, trackMachineGun, spreadShotMuzzleOffset);
                SpawnProjectile(RotateAim(aim, -angle), false, trackMachineGun, spreadShotMuzzleOffset);
            }

            return;
        }

        SpawnProjectile(aim, longHand, trackMachineGun);
    }

    private void SpawnProjectile(Vector2 direction, bool longHand, bool trackMachineGun, float muzzleOffset = 0f)
    {
        if (!CanSpawnFromFirePoint())
            return;

        Projectile prefab = longHand ? longHandProjectilePrefab : shortHandProjectilePrefab;
        if (prefab == null)
            return;

        if (trackMachineGun)
            PruneAndCapMachineGunShots();

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        Vector3 spawnPos = GetFirePosition() + (Vector3)(dir * Mathf.Max(0f, muzzleOffset));
        Projectile shot = Instantiate(prefab, spawnPos, Quaternion.identity);

        bool chargedHit = longHand || IsFullyChargedForHealthDrop();
        shot.SetCountChargedHit(chargedHit);
        int damage = prefab.Damage + HyperBonusLevel * Mathf.Max(0, hyperDamagePerLevel);
        shot.Launch(dir, prefab.Speed, damage, transform);
        shot.BeginSpawnBehindOwnerUntilClear(spriteRenderer, EffectSortingGroup, bodyCollider, firePoint, facingSign);

        GameVisualEffect blastPrefab = VisualEffects.ResolveBusterBlastPrefab(
            longHand ? ProjectileShotType.Big : ProjectileShotType.Small,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
        if (blastPrefab != null)
        {
            Transform muzzle = firePoint != null ? firePoint : transform;
            VisualEffects.SpawnBusterBlast(blastPrefab, muzzle, shot);
        }

        if (trackMachineGun)
            liveMachineGunShots.Add(shot);

        if (spawningRunShot)
        {
            runShotTracks.Add(new RunShotTrack
            {
                Shot = shot,
                SpawnPos = spawnPos
            });
        }
    }

    private static Vector2 RotateAim(Vector2 direction, float degrees)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos).normalized;
    }

    private void PruneAndCapMachineGunShots()
    {
        for (int i = liveMachineGunShots.Count - 1; i >= 0; i--)
        {
            if (liveMachineGunShots[i] == null)
                liveMachineGunShots.RemoveAt(i);
        }

        int maxLive = Mathf.Max(2, machineGunMaxLiveShots);
        while (liveMachineGunShots.Count >= maxLive)
        {
            Projectile oldest = liveMachineGunShots[0];
            liveMachineGunShots.RemoveAt(0);
            if (oldest != null)
                Destroy(oldest.gameObject);
        }
    }

    private void PruneRunShotTracks()
    {
        for (int i = runShotTracks.Count - 1; i >= 0; i--)
        {
            if (runShotTracks[i].Shot == null)
                runShotTracks.RemoveAt(i);
        }
    }

    private bool ShouldHoldRunShootAnimation()
    {
        if (machineGunHolding && WantsRunShootContext())
            return true;

        if (runShotTracks.Count == 0)
            return false;

        for (int i = 0; i < runShotTracks.Count; i++)
        {
            RunShotTrack track = runShotTracks[i];
            if (track.Shot == null)
                continue;

            if (Vector3.Distance(track.Shot.transform.position, track.SpawnPos) < runShootHoldDistance)
                return true;
        }

        return false;
    }

    private bool ShouldBlockDashForHyperHold()
    {
        // While Hyper Speed is active, dash freely (cancel is hold Up/Down+Dash).
        if (IsHyperSpeedActive)
            return false;

        if (hyperSpeedCooldownRemaining > 0f || hyperSpeedRemaining <= 0.01f)
            return false;

        return moveInput.y >= upAimThreshold || moveInput.y <= -upAimThreshold;
    }

    private void TickHyperSpeedCooldown(float dt)
    {
        if (hyperSpeedCooldownRemaining <= 0f)
            return;

        hyperSpeedCooldownRemaining = Mathf.Max(0f, hyperSpeedCooldownRemaining - dt);
        if (hyperSpeedCooldownRemaining <= 0f && hyperSpeedPoolExhaustedForCooldown)
        {
            hyperSpeedPoolExhaustedForCooldown = false;
            lastHyperPool = HyperSpeedPoolSeconds;
            hyperSpeedRemaining = lastHyperPool;
            hyperSpeedDuration = lastHyperPool;
        }
    }

    /// <summary>
    /// The Hyper Ability bonus seconds come and go with full health. A full, idle pool follows the bonus;
    /// a partly used pool is only clamped down.
    /// </summary>
    private void SyncHyperSpeedPool()
    {
        if (hyperSpeedActive || hyperSpeedPoolExhaustedForCooldown)
            return;

        float pool = HyperSpeedPoolSeconds;
        if (Mathf.Abs(pool - lastHyperPool) < 0.001f)
            return;

        hyperSpeedRemaining = hyperSpeedRemaining >= lastHyperPool - 0.01f
            ? pool
            : Mathf.Min(hyperSpeedRemaining, pool);
        lastHyperPool = pool;
    }

    private bool CanActivateHyperSpeed()
    {
        return isGrounded
            && !IsDashJumping
            && hyperSpeedCooldownRemaining <= 0f
            && hyperSpeedRemaining > 0.05f;
    }

    private void TickHyperSpeedHold()
    {
        bool pressingUp = moveInput.y >= upAimThreshold;
        bool pressingDown = moveInput.y <= -upAimThreshold;
        bool directionHeld = pressingUp || pressingDown;

        if (IsHyperSpeedActive)
        {
            if (hyperSpeedCancelGraceRemaining > 0f)
            {
                hyperSpeedCancelGraceRemaining = Mathf.Max(0f, hyperSpeedCancelGraceRemaining - Time.unscaledDeltaTime);
                hyperSpeedHoldTimer = 0f;
                return;
            }

            if (!directionHeld || !IsDashHeld())
            {
                hyperSpeedHoldTimer = 0f;
                return;
            }

            hyperSpeedHoldTimer += Time.unscaledDeltaTime;
            if (hyperSpeedHoldTimer >= hyperSpeedHoldSeconds)
            {
                hyperSpeedHoldTimer = 0f;
                EndHyperSpeed(force: false, exhausted: false);
            }

            return;
        }

        if (hyperSpeedCooldownRemaining > 0f)
        {
            hyperSpeedHoldTimer = 0f;
            return;
        }

        if (!directionHeld)
        {
            hyperSpeedHoldTimer = 0f;
            return;
        }

        if (!IsDashHeld() || !CanActivateHyperSpeed())
        {
            hyperSpeedHoldTimer = 0f;
            return;
        }

        hyperSpeedHoldTimer += Time.unscaledDeltaTime;
        if (hyperSpeedHoldTimer >= hyperSpeedHoldSeconds)
        {
            hyperSpeedHoldTimer = 0f;
            BeginHyperSpeed(pressingDown);
        }
    }

    private bool IsDashHeld()
    {
        return controls != null && controls.PlayerControls.Dash.IsPressed();
    }

    private void BeginHyperSpeed(bool pressingDown)
    {
        if (!CanActivateHyperSpeed())
            return;

        if (hyperSpeedRemaining <= 0.05f)
            hyperSpeedRemaining = HyperSpeedPoolSeconds;

        hyperSpeedDuration = Mathf.Max(HyperSpeedPoolSeconds, hyperSpeedRemaining);
        bool pressingUp = moveInput.y >= upAimThreshold;
        hyperSpeedCountMovesNormal = pressingDown;
        hyperSpeedActive = true;
        hyperSpeedHoldTimer = 0f;
        hyperSpeedCancelGraceRemaining = hyperSpeedHoldSeconds + 0.05f;
        HyperSpeedWorldSlow.Begin(hyperSpeedWorldTimeScale, slowPlayer: pressingUp);

        if (animator != null && !string.IsNullOrWhiteSpace(clockActivationTrigger) && !IsInShootAnimation())
            animator.SetTrigger(clockActivationTrigger);

        float full = GetActiveFullChargeSeconds();
        chargeAuraVisual?.SetActivityMultiplier(hyperSpeedAuraActivityBoost);
        UpdateChargeAura(full);

        savedDashAfterimageCount = dashAfterimageCount;
        dashAfterimageCount = Mathf.Max(1, countHyperSpeedAfterimageCount);
        SetupDashAfterimages();
        RefreshHyperSpeedAfterimages();

        hyperSpeedWorldTint?.Apply(transform, hyperSpeedWorldRedTint);

        if (pendingShot)
            pendingShotDeadline = Time.time + Mathf.Max(0.05f, shotAnimWaitTimeout);
    }

    private void TickHyperSpeed(float dt)
    {
        if (!hyperSpeedActive)
            return;

        hyperSpeedRemaining = Mathf.Max(0f, hyperSpeedRemaining - dt);
        float chargeT = hyperSpeedDuration > 0f ? hyperSpeedRemaining / hyperSpeedDuration : 0f;
        float full = GetActiveFullChargeSeconds();
        UpdateChargeAura(full * Mathf.Max(0.05f, chargeT));

        float auraActivity = Mathf.Max(0.05f, chargeT) * hyperSpeedAuraActivityBoost;
        chargeAuraVisual?.SetActivityMultiplier(auraActivity);

        RefreshHyperSpeedAfterimages();

        if (hyperSpeedRemaining <= 0f)
            EndHyperSpeed(force: false, exhausted: true);
    }

    private void RefreshHyperSpeedAfterimages()
    {
        if (!hyperSpeedActive || !enableDashAfterimages)
            return;

        // Keep afterimages alive for the full remaining Hyper Speed window.
        afterimagesVisibleUntil = Time.time
            + Mathf.Max(0.1f, hyperSpeedRemaining)
            + GetMaxAfterimageDelay()
            + dashAfterimageLifetimePadding
            + 0.25f;
        SetDashAfterimagesActive(true);
    }

    protected override void UpdateDashAfterimages()
    {
        if (hyperSpeedActive)
            RefreshHyperSpeedAfterimages();

        base.UpdateDashAfterimages();
    }

    private void EndHyperSpeed(bool force)
    {
        EndHyperSpeed(force, exhausted: force || hyperSpeedRemaining <= 0.01f);
    }

    private void EndHyperSpeed(bool force, bool exhausted)
    {
        if (!hyperSpeedActive && !force)
            return;

        bool wasActive = hyperSpeedActive;
        hyperSpeedActive = false;
        hyperSpeedCountMovesNormal = false;
        hyperSpeedHoldTimer = 0f;
        hyperSpeedCancelGraceRemaining = 0f;

        // Cooldown only when the full duration pool was consumed.
        if (!force && exhausted)
        {
            hyperSpeedRemaining = 0f;
            hyperSpeedPoolExhaustedForCooldown = true;
            hyperSpeedCooldownRemaining = hyperSpeedCooldownSeconds;
            ClampChargeToCooldownCap();
        }
        // Early cancel keeps hyperSpeedRemaining for the next activation.

        if (wasActive || force)
            HyperSpeedWorldSlow.End();

        if (Time.timeScale != 1f)
            Time.timeScale = 1f;

        chargeAuraVisual?.SetActivityMultiplier(1f);
        SetChargeAuraVisible(false, instant: true);
        hyperSpeedWorldTint?.Release();

        if (savedDashAfterimageCount > 0)
        {
            dashAfterimageCount = savedDashAfterimageCount;
            savedDashAfterimageCount = 0;
            SetupDashAfterimages();
        }

        afterimagesVisibleUntil = Time.time + GetMaxAfterimageDelay() + dashAfterimageLifetimePadding;
    }

    public bool IsAtFullChargeForHealthDrop()
    {
        return IsFullyChargedForHealthDrop();
    }

    public bool IsFullyChargedForHealthDrop()
    {
        if (IsHyperSpeedOnCooldown)
            return false;

        return GetCurrentChargeSeconds() >= GetActiveFullChargeSeconds() - 0.01f;
    }

    public void TrySpawnHealthClockFromEnemyHit(Vector3 enemyCenter)
    {
        if (healthUpClockPrefab == null)
            return;

        CollectableHealthUpClock.SpawnFromEnemyHit(healthUpClockPrefab, enemyCenter);
    }

    private bool IsInShootAnimation()
    {
        if (animator == null)
            return true;

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
        if (shootAnimStateNames == null)
            return false;

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

    private bool IsInRunShootAnimation()
    {
        if (animator == null)
            return false;

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.IsName("Run Shoot"))
            return true;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsName("Run Shoot"))
                return true;
        }

        return false;
    }

    private void PlayShootAnimatorTriggers()
    {
        if (animator == null)
            return;

        pendingRunShoot = false;

        if (IsAimingUp() && isGrounded)
        {
            runShootAnimEngaged = false;
            animator.SetTrigger("ShootUp");
        }
        else if (!isGrounded && rb != null && rb.linearVelocity.y >= 0f)
        {
            runShootAnimEngaged = false;
            animator.SetTrigger("JumpShot");
        }
        else if (!isGrounded)
        {
            runShootAnimEngaged = false;
            animator.SetTrigger("FallShot");
        }
        else if (WantsRunShootContext())
        {
            pendingRunShoot = true;
            if (!IsInRunShootAnimation() && !runShootAnimEngaged)
            {
                animator.SetTrigger("RunShoot");
                runShootAnimEngaged = true;
            }
        }
        else
        {
            runShootAnimEngaged = false;
            animator.SetTrigger("Shoot");
        }
    }

    private void UpdateChargeAura(float chargeSeconds)
    {
        UpdateChargeHue(chargeSeconds);
        if (chargeAuraVisual == null)
            return;

        float full = GetActiveFullChargeSeconds();
        float capped = Mathf.Min(chargeSeconds, GetMaxChargeSeconds());
        float charge01 = Mathf.Clamp01(capped / Mathf.Max(0.01f, full));

        chargeAuraVisual.SetVisible(true);
        // Cooldown weakness: aura never reaches full / never shows full-charge look.
        bool atTrueFull = !IsHyperSpeedOnCooldown && capped >= full - 0.01f;
        chargeAuraVisual.SetFullCharge(atTrueFull);
        chargeAuraVisual.SetChargeProgress(charge01);

        if (IsHyperSpeedActive && hyperSpeedDuration > 0f)
        {
            float chargeT = hyperSpeedRemaining / hyperSpeedDuration;
            chargeAuraVisual.SetActivityMultiplier(Mathf.Max(0.05f, chargeT) * hyperSpeedAuraActivityBoost);
        }
    }

    private void BeginAuraDissipate()
    {
        auraHueDissipating = true;
        auraHueDissipateEndTime = Time.time + auraDissipateDuration;
        auraHueDissipateStartAlpha = chargeHueRenderer != null ? chargeHueRenderer.color.a : auraAlphaAtBig;

        if (chargeAuraVisual != null)
            chargeAuraVisual.BeginDissipate(auraDissipateDuration);
        else
            SetChargeHueVisible(false);
    }

    private void TickAuraHueDissipate()
    {
        if (!auraHueDissipating || chargeHueRenderer == null || chargeHueObject == null)
            return;

        float remaining = auraHueDissipateEndTime - Time.time;
        if (remaining <= 0f)
        {
            auraHueDissipating = false;
            SetChargeHueVisible(false);
            return;
        }

        float t = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.05f, auraDissipateDuration));
        Color c = chargeHueRenderer.color;
        c.a = Mathf.Lerp(auraHueDissipateStartAlpha, 0f, t);
        chargeHueRenderer.color = c;
    }

    private void SetChargeAuraVisible(bool visible, bool instant = false)
    {
        if (visible)
            return;

        auraHueDissipating = false;
        SetChargeHueVisible(false);

        if (chargeAuraVisual == null)
            return;

        chargeAuraVisual.SetVisible(false, instant);
    }

    private void UpdateFirePointGate()
    {
        if (firePoint == null)
            return;

        bool wantsShot = pendingShot || isShooting || machineGunHolding;
        bool shouldEnable = wantsShot && IsInShootAnimation();

        if (firePoint.gameObject.activeSelf != shouldEnable)
            firePoint.gameObject.SetActive(shouldEnable);
    }

    private bool CanSpawnFromFirePoint()
    {
        if (firePoint == null || !firePoint.gameObject.activeSelf)
            return false;

        return IsInShootAnimation() && IsFirePointReady();
    }

    private bool IsFirePointReady()
    {
        if (firePoint == null)
            return false;

        if (animator == null)
            return true;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (!StateMatchesShoot(next))
                return false;

            if (animator.GetAnimatorTransitionInfo(0).normalizedTime < shootTransitionMinNormalizedTime)
                return false;
        }
        else
        {
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (!StateMatchesShoot(current))
                return false;

            if (current.normalizedTime < shootAnimMinNormalizedTime)
                return false;
        }

        return Vector3.Distance(firePoint.localPosition, firePointRestLocal) >= firePointReadyMinDistance;
    }

    private void SetupChargeHue()
    {
        if (!showChargeHue || spriteRenderer == null)
            return;

        if (chargeHueObject != null)
            Destroy(chargeHueObject);
        if (chargeHueMaterial != null)
            Destroy(chargeHueMaterial);

        chargeHueObject = new GameObject($"{name}_ChargeHue");
        chargeHueObject.transform.SetParent(transform, false);
        chargeHueObject.transform.localPosition = Vector3.zero;
        chargeHueObject.transform.localScale = Vector3.one * auraBaseScale;
        chargeHueObject.transform.SetAsFirstSibling();

        chargeHueRenderer = chargeHueObject.AddComponent<SpriteRenderer>();
        chargeHueRenderer.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeHueRenderer, spriteRenderer, EffectSortingGroup);
        chargeHueRenderer.flipX = spriteRenderer.flipX;

        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            chargeHueMaterial = new Material(solidShader);
            chargeHueRenderer.sharedMaterial = chargeHueMaterial;
        }

        Color c = chargeAuraColor;
        c.a = 0f;
        chargeHueRenderer.color = c;
        chargeHueObject.SetActive(false);
    }

    private void UpdateChargeHue(float chargeSeconds)
    {
        if (!showChargeHue)
            return;

        if (chargeHueRenderer == null || chargeHueObject == null)
            SetupChargeHue();

        if (chargeHueRenderer == null || spriteRenderer == null)
            return;

        float full = GetActiveFullChargeSeconds();
        float capped = Mathf.Min(chargeSeconds, GetMaxChargeSeconds());
        float charge01 = Mathf.Clamp01(capped / Mathf.Max(0.01f, full));
        float mediumThreshold = full * 0.5f;

        chargeHueObject.SetActive(true);
        chargeHueRenderer.sprite = spriteRenderer.sprite;
        chargeHueRenderer.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeHueRenderer, spriteRenderer, EffectSortingGroup);

        float alpha;
        if (!IsHyperSpeedOnCooldown && capped >= full)
            alpha = auraAlphaAtBig;
        else if (capped >= mediumThreshold)
            alpha = Mathf.Lerp(auraAlphaAtMedium, auraAlphaAtBig, Mathf.InverseLerp(mediumThreshold, full, capped));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtMedium, capped / Mathf.Max(0.01f, mediumThreshold));

        Color red = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(auraFlickerPhase * Mathf.PI * 2f);
        float lightMix = shimmer * auraFlickerStrength * Mathf.Lerp(0.55f, 1f, charge01);
        Color auraColor = Color.Lerp(red, chargeAuraFlickerColor, lightMix);
        auraColor.a = alpha;
        chargeHueRenderer.color = auraColor;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount * charge01;
        chargeHueObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }

    private void SetChargeHueVisible(bool visible)
    {
        if (chargeHueObject == null)
            return;

        if (!visible)
        {
            chargeHueObject.SetActive(false);
            auraFlickerPhase = 0f;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        fullChargeSeconds = Mathf.Max(0.05f, fullChargeSeconds);
        spreadFullChargeSeconds = Mathf.Max(fullChargeSeconds, spreadFullChargeSeconds);
        chargeMoveSpeedBonus = Mathf.Max(0f, chargeMoveSpeedBonus);
        shootCooldown = Mathf.Max(0f, shootCooldown);
        shotAnimWaitTimeout = Mathf.Max(0.05f, shotAnimWaitTimeout);
        auraDelayAfterShortShot = Mathf.Max(0f, auraDelayAfterShortShot);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        spreadShotAngleDegrees = Mathf.Clamp(spreadShotAngleDegrees, 1f, 45f);
        spreadChargedMaxAngleDegrees = Mathf.Clamp(spreadChargedMaxAngleDegrees, 5f, 75f);
        spreadChargedShotCount = Mathf.Max(2, spreadChargedShotCount);
        spreadShotMuzzleOffset = Mathf.Max(0f, spreadShotMuzzleOffset);
        machineGunFireInterval = Mathf.Max(0.05f, machineGunFireInterval);
        runShootHoldDistance = Mathf.Max(0.5f, runShootHoldDistance);
        hyperSpeedMaxSeconds = Mathf.Max(0.1f, hyperSpeedMaxSeconds);
        hyperSpeedHoldSeconds = Mathf.Max(0.05f, hyperSpeedHoldSeconds);
        hyperSpeedCooldownSeconds = Mathf.Max(0f, hyperSpeedCooldownSeconds);
        hyperSpeedCooldownFireRateMultiplier = Mathf.Max(1f, hyperSpeedCooldownFireRateMultiplier);
        hyperSpeedCooldownMaxChargeFraction = Mathf.Clamp(hyperSpeedCooldownMaxChargeFraction, 0.1f, 0.9f);
        hyperUpDashSpeedFactor = Mathf.Clamp(hyperUpDashSpeedFactor, 0.15f, 1f);
        hyperUpDashSmoothStop = Mathf.Max(0.05f, hyperUpDashSmoothStop);
        hyperUpDashDistanceBonus = Mathf.Max(0f, hyperUpDashDistanceBonus);
        hyperSpeedAuraActivityBoost = Mathf.Max(1f, hyperSpeedAuraActivityBoost);
        countDashDistance = Mathf.Max(0.1f, countDashDistance);
        countDashSpeed = Mathf.Max(0.1f, countDashSpeed);
        countMaxAirDashes = Mathf.Max(0, countMaxAirDashes);
        floatMinAerialLevel = Mathf.Max(1, floatMinAerialLevel);
        floatFallSpeed = Mathf.Max(0.1f, floatFallSpeed);
        hyperSpeedSecondsPerLevel = Mathf.Max(0f, hyperSpeedSecondsPerLevel);
        hyperDamagePerLevel = Mathf.Max(0, hyperDamagePerLevel);
        rewindMinHyperLevel = Mathf.Max(1, rewindMinHyperLevel);
        rewindHoldSeconds = Mathf.Max(0.1f, rewindHoldSeconds);
        rewindBaseSeconds = Mathf.Max(0.1f, rewindBaseSeconds);
        rewindSecondsPerLevel = Mathf.Max(0f, rewindSecondsPerLevel);
        rewindCooldownSeconds = Mathf.Max(0f, rewindCooldownSeconds);
        rewindFlashSeconds = Mathf.Max(0.05f, rewindFlashSeconds);
        rewindGhostCount = Mathf.Max(0, rewindGhostCount);
        timeCloneMinStyleLevel = Mathf.Max(1, timeCloneMinStyleLevel);
        timeCloneBaseCap = Mathf.Max(1, timeCloneBaseCap);
        timeCloneHealth = Mathf.Max(1, timeCloneHealth);
        timeCloneShootInterval = Mathf.Max(0.1f, timeCloneShootInterval);
        timeCloneSearchRadius = Mathf.Max(1f, timeCloneSearchRadius);
        timeCloneMachineGunPellets = Mathf.Max(1, timeCloneMachineGunPellets);
        timeCloneMachineGunGap = Mathf.Max(0f, timeCloneMachineGunGap);
        lastCloneStyleLevel = Mathf.Max(1, lastCloneStyleLevel);
        lastCloneHealth = Mathf.Max(1, lastCloneHealth);
    }
#endif
}
