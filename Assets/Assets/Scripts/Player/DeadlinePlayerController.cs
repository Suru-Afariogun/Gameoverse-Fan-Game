using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Playable Deadline — Count's evil opposite.
/// Deterioration Acceleration: progressive time speed-up, yellow wash, range/camera HP drain,
/// accelerator shots while active, and pierce/explode short/long hands. No Hyper Speed.
/// </summary>
public class DeadlinePlayerController : CountPlayerController
{
    public const string DeadlineCharacterId = "Deadline";

    private static readonly Color SuperYellow = new Color(1f, 1f, 0.08f, 1f);
    private static readonly Color SuperYellowStrong = new Color(1f, 0.95f, 0f, 1f);
    private static readonly Color SuperYellowFlicker = new Color(1f, 1f, 0.55f, 1f);

    [Header("Deadline - Projectiles")]
    [Tooltip("Overrides Count short-hand prefab so Deadline never fires Count art.")]
    [SerializeField] private Projectile deadlineShortHandPrefab;
    [Tooltip("Overrides Count long-hand prefab so Deadline never fires Count art.")]
    [SerializeField] private Projectile deadlineLongHandPrefab;
    [SerializeField] private Projectile acceleratorShotPrefab;
    [SerializeField] private CollectableDeadlineHealClock healClockPrefab;
    [SerializeField] private CollectableDeadlineRechargeClock rechargeClockPrefab;
    [SerializeField] private float shotFadeTravelDistance = 20f;

    [Header("Deadline - Deterioration Acceleration")]
    [SerializeField] private float deteriorationDuration = 10f;
    [SerializeField] private float maxWorldTimeScale = 6f;
    [SerializeField] private float maxBossTimeScale = 2f;
    [SerializeField] private float maxPlayerTimeScale = 3f;
    [SerializeField] private float hyper3PlayerTimeScale = 5f;
    [SerializeField] private float deteriorationRange = 13f;
    [SerializeField] private float deteriorateTickInterval = 0.5f;
    [SerializeField] private int deteriorateEnemyBaseDamage = 2;
    [SerializeField] private int deteriorateBossBaseDamage = 1;
    [SerializeField] private float overdriveSelfDamageInterval = 1f;
    [SerializeField] private int overdriveForceStopAtHp = 2;
    [SerializeField] [Range(0f, 1f)] private float yellowWorldTint = 0.28f;
    [SerializeField] private float hardModeDaExtraIFrames = 0.8f;

    [Header("Deadline - DA Visuals")]
    [SerializeField] private GameObject deteriorationVisualContainer;
    [SerializeField] private Transform deteriorationClock;
    [SerializeField] private Transform clockShortHand;
    [SerializeField] private Transform clockLongHand;
    [SerializeField] private Transform durationBar;
    [SerializeField] private SpriteRenderer durationFill;
    [SerializeField] private float shortHandSpinDegPerSec = 160f;
    [SerializeField] private float longHandSpinDegPerSec = 110f;
    [SerializeField] private Color durationFillColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Header("Deadline - Accelerator Shot")]
    [SerializeField] private int acceleratorBaseDamage = 1;
    [SerializeField] private int acceleratorDamageCap = 20;
    [SerializeField] private float acceleratorDamageRampSeconds = 2f;
    [SerializeField] [Range(0f, 1f)] private float hyperMaxHpClockBurstChance = 0.7f;

    [Header("Deadline - Upgrades")]
    [SerializeField] private float hyperClockSeekSpeedPerLevel = 3f;
    [SerializeField] private float attackStyleShotSpeedPerLevel = 1f;
    [SerializeField] private float hyperAcceleratorSpeedPerLevel = 2f;
    [SerializeField] private int hyperAcceleratorPowerPerLevel = 2;
    [SerializeField] private int attackStyleAcceleratorMinLevel = 2;
    [SerializeField] private int aerialDaRefillMinLevel = 4;
    [SerializeField] [Range(0f, 1f)] private float aerialDaRefillChance = 0.5f;

    [Header("Deadline - Future Clones (Attack Style, Spread / Machine Gun)")]
    [Tooltip("Hard cap. At the cap, dashes leave no new clone until one dies.")]
    [SerializeField] private int futureCloneCap = 3;
    [SerializeField] private float futureCloneSearchRadius = 30f;
    [SerializeField] private float futureCloneGuardAhead = 3f;
    [SerializeField] private float futureCloneShootInterval = 0.55f;
    [SerializeField] private float futureCloneChargeSeconds = 3f;
    [SerializeField] private Color futureCloneColor = new Color(1f, 0.92f, 0.15f, 0.85f);
    [Tooltip("Body tint while Deadline is piloting a Future Clone after a killing blow.")]
    [SerializeField] private Color futureClonePlayBodyColor = new Color(1f, 0.92f, 0.15f, 1f);
    [Tooltip("Hold Dash this long to teleport all Future Clones beside Deadline.")]
    [SerializeField] private float futureCloneRecallHoldSeconds = 0.8f;
    [Tooltip("Attack Style L4+: clones emit heal/recharge clocks on this interval.")]
    [SerializeField] private float futureCloneClockPulseSeconds = 5f;
    [SerializeField] private int futureCloneClockPulseCount = 1;

    private bool daActive;
    private bool daPoolInitialized;
    private float daRemaining;
    private float daMaxCached = 10f;
    private float daActiveElapsed;
    private float deteriorateTickTimer;
    private float overdriveHpTimer;
    private bool daOverdrive;
    private bool pendingAccelerator;
    private Vector2 pendingAcceleratorAim;
    private float pendingAcceleratorDeadline;
    private DeadlineDeteriorationWorldTint worldTint;
    private float durationBarFullWidth = 1.98f;
    private float durationBarFillHeight = 0.22f;
    private Texture2D durationFillTexture;
    private Transform shortHandPivot;
    private Transform longHandPivot;
    private readonly HashSet<int> explodeOnHitShotIds = new HashSet<int>();
    private readonly HashSet<Projectile> acceleratorShots = new HashSet<Projectile>();
    private readonly HashSet<int> deterioratedThisTick = new HashSet<int>();
    private readonly List<Projectile> acceleratorPruneBuffer = new List<Projectile>(8);
    private readonly List<DeadlineFutureClone> futureClones = new List<DeadlineFutureClone>(4);
    private float futureCloneRecallHold;
    private bool futureCloneRecallLatched;
    private bool playingAsFutureClone;
    private float futureClonePlayPowerScale = 0.6f;

    public bool IsDeteriorationAccelerationActive => daActive;
    public float DeteriorationAccelerationRemaining => daRemaining;
    public bool IsPlayingAsFutureClone => playingAsFutureClone;
    public CollectableDeadlineHealClock HealClockPrefab => healClockPrefab;
    public CollectableDeadlineRechargeClock RechargeClockPrefab => rechargeClockPrefab;

    /// <summary>
    /// Future Clones at Attack Style L1+ (Spread / Machine Gun). Count Time Clones stay at their own min level.
    /// </summary>
    private bool FutureClonesUnlocked =>
        StyleLevel >= 1 && (UsesSpreadShotStyle() || UsesMachineGunStyle());

    /// <summary>HP scale caps at Style 3 (95%). Power keeps rising at 4+.</summary>
    public float GetFutureCloneHpScale()
    {
        int level = Mathf.Max(0, StyleLevel);
        if (level <= 1) return 0.60f;
        if (level == 2) return 0.80f;
        return 0.95f;
    }

    public float GetFutureClonePowerScale()
    {
        int level = Mathf.Max(0, StyleLevel);
        if (level <= 1) return 0.60f;
        if (level == 2) return 0.80f;
        if (level == 3) return 0.95f;
        if (level == 4) return 1.20f;
        return 2.00f;
    }

    public bool FutureCloneClockSupportUnlocked => StyleLevel >= 4;

    /// <summary>Same air-jump budget Deadline uses (1 + Aerial Action), ignoring Future-Clone body limits.</summary>
    public int GetFutureCloneAirJumpBudget() =>
        Mathf.Max(0, 1 + PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction));

    /// <summary>Hyper Ability L1+: clocks seek Deadline at level × this speed (any HP).</summary>
    public float HyperClockSeekSpeed
    {
        get
        {
            int level = HyperLevel;
            return level >= 1 ? level * Mathf.Max(0.1f, hyperClockSeekSpeedPerLevel) : 0f;
        }
    }

    private int AerialLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);
    private bool AcceleratorUsesAttackStyle =>
        StyleLevel >= Mathf.Max(1, attackStyleAcceleratorMinLevel) &&
        (UsesMachineGunStyle() || UsesSpreadShotStyle());

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId(DeadlineCharacterId);

        // Force Deadline shot prefabs (Count anim controllers were overriding art before).
        if (deadlineShortHandPrefab != null)
            shortHandProjectilePrefab = deadlineShortHandPrefab;
        if (deadlineLongHandPrefab != null)
            longHandProjectilePrefab = deadlineLongHandPrefab;

        // Super-yellow charge aura (solid shader kills leftover red from Count particle art).
        chargeAuraColor = SuperYellow;
        chargeAuraStrongColor = SuperYellowStrong;
        chargeAuraFlickerColor = SuperYellowFlicker;
        if (chargeAuraVisual == null)
            chargeAuraVisual = GetComponentInChildren<CountChargeAuraVisual>(true);
        chargeAuraVisual?.SetParticleTint(SuperYellow);

        ResolveDeteriorationVisuals();
        SetupDurationFill();
        SetupClockHands();
        SetDeteriorationVisualsActive(false);

        daMaxCached = Mathf.Max(0.1f, deteriorationDuration);
        daRemaining = daMaxCached;
        daPoolInitialized = true;

        worldTint = GetComponent<DeadlineDeteriorationWorldTint>()
                    ?? gameObject.AddComponent<DeadlineDeteriorationWorldTint>();
    }

    protected override void OnDestroy()
    {
        VanishAllFutureClones();
        EndDeteriorationAcceleration(force: true);
        base.OnDestroy();
    }

    protected override void OnDisable()
    {
        VanishAllFutureClones();
        EndDeteriorationAcceleration(force: true);
        base.OnDisable();
    }

    protected override void ClearForVehicleRide()
    {
        VanishAllFutureClones();
        EndDeteriorationAcceleration(force: true);
        base.ClearForVehicleRide();
    }

    /// <summary>Deadline never uses Count's Time Clones — Future Clones instead.</summary>
    protected override void OnStyleDashCloneOpportunity()
    {
        TrySpawnFutureClone();
    }

    /// <summary>No Rewind — Deadline uses Deterioration Acceleration / Future Clones instead.</summary>
    protected override bool CanUseRewind() => false;

    /// <summary>
    /// Killing blow: transfer into a living Future Clone (clone limits) until none remain, then die.
    /// </summary>
    protected override bool TryBecomeLastClone()
    {
        PruneFutureClones();
        if (futureClones.Count == 0)
            return false;

        // Newest first — same ordering spirit as Count's Last Clone.
        DeadlineFutureClone host = futureClones[futureClones.Count - 1];
        if (host == null || host.IsDead)
        {
            PruneFutureClones();
            return futureClones.Count > 0 && TryBecomeLastClone();
        }

        Vector3 position = host.AbsorbPosition;
        int hostHp = Mathf.Max(1, host.Health);
        futureClones.Remove(host);
        host.ConsumeForOwnerTransfer();

        // Bake power scale before upgrades are suppressed (StyleLevel becomes 0).
        futureClonePlayPowerScale = GetFutureClonePowerScale();
        playingAsFutureClone = true;
        UpgradesSuppressed = true;
        EndDeteriorationAcceleration(force: true);
        ResetCombatStateForCloneTransfer();
        TeleportTo(position);

        base.SetHealth(Mathf.Max(1, Mathf.Min(hostHp, maxHealth)));
        invincibilityTimer = Mathf.Max(invincibilityTimer, hitInvincibilityDuration);
        if (spriteRenderer != null)
            spriteRenderer.color = futureClonePlayBodyColor;

        SoundManager.Instance?.PlayClockworkToc();
        return true;
    }

    /// <summary>No Hyper Speed — Deterioration Acceleration is Deadline's time ability.</summary>
    protected override bool CanActivateHyperSpeed() => false;

    /// <summary>Base double jump at Aerial Action 0; +1 air jump per Aerial Action level.
    /// Future-Clone body: ground jump only (matches AI clone limits).</summary>
    protected override int UpgradeAirJumps =>
        playingAsFutureClone
            ? 0
            : 1 + PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);

    protected override float GetHitInvincibilityDuration()
    {
        float duration = base.GetHitInvincibilityDuration();
        if (daActive && GameSettings.Difficulty == GameDifficulty.Hard)
            duration += Mathf.Max(0f, hardModeDaExtraIFrames);
        return duration;
    }

    protected override void GetBusterBlastTint(out Color? colorOverride, out bool replaceTint)
    {
        colorOverride = SuperYellow;
        replaceTint = true;
    }

    protected override void OnPerformedJump(bool wasAirJump)
    {
        if (AerialLevel < Mathf.Max(1, aerialDaRefillMinLevel))
            return;
        if (Random.value >= aerialDaRefillChance)
            return;

        EnsureDaPool();
        daRemaining = daMaxCached;
        if (daActive)
            daOverdrive = false;
        RefreshDurationBar();
    }

    // ---------- Attack Style: Future Clones ----------

    private void TrySpawnFutureClone()
    {
        // Playing as a Future Clone: stuck to clone limits — no new clones.
        if (playingAsFutureClone || !FutureClonesUnlocked || IsDead || spriteRenderer == null)
            return;
        if (shortHandProjectilePrefab == null && deadlineShortHandPrefab == null)
            return;

        PruneFutureClones();
        if (futureClones.Count >= Mathf.Max(1, futureCloneCap))
            return;

        if (!TrySamplePose(GetMaxAfterimageDelay(), out PoseSample sample))
            return;

        Projectile shortHand = deadlineShortHandPrefab != null ? deadlineShortHandPrefab : shortHandProjectilePrefab;
        Projectile longHand = deadlineLongHandPrefab != null ? deadlineLongHandPrefab : longHandProjectilePrefab;

        float hpScale = GetFutureCloneHpScale();
        float powerScale = GetFutureClonePowerScale();
        int hp = Mathf.Max(1, Mathf.RoundToInt(currentHealth * hpScale));
        Vector2 bodySize = Vector2.zero;
        Vector2 bodyOffset = Vector2.zero;
        if (bodyCollider is BoxCollider2D box)
        {
            bodySize = box.size;
            bodyOffset = box.offset;
        }

        DeadlineFutureClone clone = DeadlineFutureClone.Spawn(
            this,
            sample.position,
            sample.sprite != null ? sample.sprite : spriteRenderer.sprite,
            sample.flipX,
            futureCloneColor,
            shortHand,
            longHand,
            hp,
            powerScale,
            NormalMoveSpeed,
            JumpForce,
            FallMultiplier,
            futureCloneSearchRadius,
            futureCloneGuardAhead,
            futureCloneShootInterval,
            futureCloneChargeSeconds,
            GroundLayers,
            GroundCheckRadius,
            bodySize,
            bodyOffset,
            GetFutureCloneAirJumpBudget(),
            FutureCloneClockSupportUnlocked,
            healClockPrefab,
            rechargeClockPrefab,
            futureCloneClockPulseSeconds,
            futureCloneClockPulseCount);

        if (clone == null)
            return;

        // Clones ignore each other so they don't stack-shove.
        BoxCollider2D cloneCol = clone.GetComponent<BoxCollider2D>();
        if (cloneCol != null)
        {
            for (int i = 0; i < futureClones.Count; i++)
            {
                if (futureClones[i] == null)
                    continue;
                BoxCollider2D other = futureClones[i].GetComponent<BoxCollider2D>();
                if (other != null)
                    Physics2D.IgnoreCollision(cloneCol, other, true);
            }
        }

        futureClones.Add(clone);
    }

    private void PruneFutureClones()
    {
        for (int i = futureClones.Count - 1; i >= 0; i--)
        {
            if (futureClones[i] == null || futureClones[i].IsDead)
                futureClones.RemoveAt(i);
        }
    }

    private void VanishAllFutureClones()
    {
        for (int i = 0; i < futureClones.Count; i++)
        {
            if (futureClones[i] != null)
                futureClones[i].Vanish();
        }

        futureClones.Clear();
    }

    public override void TrySpawnHealthClockFromEnemyHit(Vector3 enemyCenter)
    {
        // Deadline uses explode-on-hit clocks, not Count's charged health drop.
    }

    public void AddDeteriorationAccelerationTime(float seconds)
    {
        if (seconds <= 0f)
            return;

        EnsureDaPool();
        daRemaining = Mathf.Min(daMaxCached, daRemaining + seconds);
        if (daActive && daRemaining > 0.05f)
            daOverdrive = false;
        RefreshDurationBar();
    }

    protected override void HandleCharacterUpdate()
    {
        base.HandleCharacterUpdate();
        TickPendingAccelerator();
        TickDeteriorationAcceleration(Time.unscaledDeltaTime);
        TickFutureCloneRecall(Time.unscaledDeltaTime);
    }

    /// <summary>Hold Dash for <see cref="futureCloneRecallHoldSeconds"/> to pull all clones beside Deadline.</summary>
    private void TickFutureCloneRecall(float dt)
    {
        PruneFutureClones();
        if (futureClones.Count == 0 || controls == null || InputLocked || isStunned || IsDead)
        {
            futureCloneRecallHold = 0f;
            futureCloneRecallLatched = false;
            return;
        }

        bool holdingDash = controls.PlayerControls.Dash.IsPressed();
        if (!holdingDash)
        {
            futureCloneRecallHold = 0f;
            futureCloneRecallLatched = false;
            return;
        }

        if (futureCloneRecallLatched)
            return;

        futureCloneRecallHold += Mathf.Max(0f, dt);
        if (futureCloneRecallHold < Mathf.Max(0.1f, futureCloneRecallHoldSeconds))
            return;

        futureCloneRecallLatched = true;
        futureCloneRecallHold = 0f;
        RecallFutureClonesBesidePlayer();
    }

    private void RecallFutureClonesBesidePlayer()
    {
        PruneFutureClones();
        float facing = facingSign;
        for (int i = 0; i < futureClones.Count; i++)
        {
            DeadlineFutureClone clone = futureClones[i];
            if (clone == null || clone.IsDead)
                continue;

            // Fan them slightly so they don't stack on one tile.
            float side = (i % 2 == 0) ? facing : -facing;
            float offset = 1.1f + (i / 2) * 0.85f;
            clone.TeleportBesideOwner(side, offset);
        }
    }

    protected override bool ExtraWantsFirePoint() => pendingAccelerator;

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        if (daActive)
            animator.speed = DeteriorationAccelerationWorld.PlayerTimeScale;
        else if (!IsTimeFrozen)
            animator.speed = 1f;
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();
        if (daActive)
            speed *= DeteriorationAccelerationWorld.PlayerTimeScale;
        return speed;
    }

    protected override float GetCurrentDashVelocityX()
    {
        float v = base.GetCurrentDashVelocityX();
        if (daActive)
            v *= DeteriorationAccelerationWorld.PlayerTimeScale;
        return v;
    }

    protected override float GetDashJumpHorizontalSpeed()
    {
        float v = base.GetDashJumpHorizontalSpeed();
        if (daActive)
            v *= DeteriorationAccelerationWorld.PlayerTimeScale;
        return v;
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (IsAimingDown())
            TryToggleDeteriorationAcceleration();

        if (daActive)
        {
            if (AcceleratorUsesAttackStyle)
            {
                if (UsesMachineGunStyle())
                {
                    BeginMachineGunFire();
                    return;
                }

                if (UsesSpreadShotStyle())
                {
                    BeginSpreadFire();
                    return;
                }
            }

            FireAcceleratorShot();
            return;
        }

        base.OnAttackStarted(context);
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (daActive && AcceleratorUsesAttackStyle)
        {
            base.OnAttackCanceled(context);
            return;
        }

        if (daActive)
            return;

        base.OnAttackCanceled(context);
    }

    protected override void SpawnProjectile(Vector2 direction, bool longHand, bool trackMachineGun, float muzzleOffset = 0f)
    {
        if (daActive)
        {
            SpawnAcceleratorProjectile(direction, trackMachineGun, muzzleOffset);
            return;
        }

        base.SpawnProjectile(direction, longHand, trackMachineGun, muzzleOffset);
    }

    protected override void OnProjectileSpawned(Projectile shot, bool longHand)
    {
        if (shot == null)
            return;

        ApplyDeadlineShotSpeedBonus(shot);
        shot.SetMaxTravelDistance(Mathf.Max(0.5f, shotFadeTravelDistance));

        // Future-Clone body: baked Attack Style power scale from the transfer.
        if (playingAsFutureClone)
            shot.SetDamage(Mathf.Max(1f, shot.Damage * futureClonePlayPowerScale));

        // 50% pierce until fade, 50% explode into heal/recharge clocks on combat hit.
        if (Random.value < 0.5f)
            shot.ConfigurePierceAndContinue();
        else
            explodeOnHitShotIds.Add(shot.GetInstanceID());
    }

    public override void NotifyDeadlineShotImpact(Projectile shot, Collider2D hitCollider)
    {
        if (shot == null || hitCollider == null)
            return;

        int id = shot.GetInstanceID();
        Vector3 center = hitCollider.bounds.center;

        if (acceleratorShots.Contains(shot))
        {
            TryHyperAcceleratorClockBurst(center);
            return;
        }

        if (!explodeOnHitShotIds.Remove(id))
            return;

        float seek = HyperClockSeekSpeed;
        if (Random.value < 0.5f)
            CollectableDeadlineHealClock.SpawnFloatingCluster(healClockPrefab, center, 3, seek);
        else
            CollectableDeadlineRechargeClock.SpawnFloatingCluster(rechargeClockPrefab, center, 3, seek);
    }

    private void TryHyperAcceleratorClockBurst(Vector3 center)
    {
        if (HyperLevel < 1)
            return;
        if (currentHealth < maxHealth)
            return;
        if (Random.value >= hyperMaxHpClockBurstChance)
            return;

        float seek = HyperClockSeekSpeed;
        CollectableDeadlineHealClock.SpawnFloatingCluster(healClockPrefab, center, 2, seek);
        CollectableDeadlineRechargeClock.SpawnFloatingCluster(rechargeClockPrefab, center, 2, seek);
    }

    private void FireAcceleratorShot()
    {
        if (acceleratorShotPrefab == null)
            return;
        if (shootCooldownTimer > 0f || pendingAccelerator)
            return;

        // Same gate as Count: queue, play shoot anim, spawn when FirePoint is ready.
        pendingAccelerator = true;
        pendingAcceleratorAim = GetAimDirection();
        if (pendingAcceleratorAim.sqrMagnitude < 0.0001f)
            pendingAcceleratorAim = new Vector2(facingSign, 0f);
        pendingAcceleratorDeadline = Time.time + 0.45f;

        BeginShootAnimation(0.28f);
        PlayShootAnimatorTriggers();
    }

    private void TickPendingAccelerator()
    {
        if (!pendingAccelerator)
            return;

        // Prefer Count-style FirePoint gate; if the anim never opens it, still fire so DA never blocks attacks.
        bool ready = CanSpawnFromFirePoint();
        bool timedOut = Time.time >= pendingAcceleratorDeadline;
        if (!ready && !timedOut)
            return;

        SpawnAcceleratorNow(pendingAcceleratorAim);
        pendingAccelerator = false;
        shootCooldownTimer = Mathf.Max(0.05f, shootCooldown);
    }

    private void SpawnAcceleratorProjectile(Vector2 aim, bool trackMachineGun, float muzzleOffset)
    {
        if (acceleratorShotPrefab == null)
            return;
        if (!CanSpawnFromFirePoint())
            return;

        Vector2 dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : new Vector2(facingSign, 0f);
        Vector3 spawnPos = GetFirePosition() + (Vector3)(dir * Mathf.Max(0f, muzzleOffset));
        LaunchAccelerator(spawnPos, dir);
    }

    private void SpawnAcceleratorNow(Vector2 aim)
    {
        if (acceleratorShotPrefab == null)
            return;

        Vector2 dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : new Vector2(facingSign, 0f);
        LaunchAccelerator(GetFirePosition(), dir);
    }

    private void LaunchAccelerator(Vector3 spawnPos, Vector2 dir)
    {
        Projectile shot = Instantiate(acceleratorShotPrefab, spawnPos, Quaternion.identity);

        int damage = GetAcceleratorDamage();
        float speed = GetAcceleratorShotSpeed();
        float life = Mathf.Max(0.05f, shotFadeTravelDistance / Mathf.Max(0.1f, speed));
        shot.ConfigurePierceAndContinue();
        shot.Launch(dir, speed, damage, life, transform);
        shot.SetMaxTravelDistance(shotFadeTravelDistance);
        shot.BeginSpawnBehindOwnerUntilClear(spriteRenderer, EffectSortingGroup, bodyCollider, firePoint, facingSign);
        PruneAcceleratorShots();
        acceleratorShots.Add(shot);

        SpawnBusterBlastForShot(shot, ProjectileShotType.Small);

        SoundManager.Instance?.PlayCountFire(ProjectileShotType.Small);
        ReportTutorialAction(TutorialAction.Attack);
    }

    private void PruneAcceleratorShots()
    {
        acceleratorPruneBuffer.Clear();
        foreach (Projectile shot in acceleratorShots)
        {
            if (shot == null)
                acceleratorPruneBuffer.Add(shot);
        }

        for (int i = 0; i < acceleratorPruneBuffer.Count; i++)
            acceleratorShots.Remove(acceleratorPruneBuffer[i]);
    }

    private void ApplyDeadlineShotSpeedBonus(Projectile shot)
    {
        if (shot == null)
            return;

        float bonus = StyleLevel * Mathf.Max(0f, attackStyleShotSpeedPerLevel);
        if (bonus > 0.01f)
            shot.SetSpeed(shot.Speed + bonus);
    }

    private float GetAcceleratorShotSpeed()
    {
        float speed = Mathf.Max(0.1f, acceleratorShotPrefab != null ? acceleratorShotPrefab.Speed : 12f);
        speed += StyleLevel * Mathf.Max(0f, attackStyleShotSpeedPerLevel);
        speed += HyperLevel * Mathf.Max(0f, hyperAcceleratorSpeedPerLevel);
        return speed;
    }

    private int GetAcceleratorDamage()
    {
        int ramp = Mathf.FloorToInt(daActiveElapsed / Mathf.Max(0.1f, acceleratorDamageRampSeconds));
        int power = HyperLevel * Mathf.Max(0, hyperAcceleratorPowerPerLevel);
        return Mathf.Clamp(
            acceleratorBaseDamage + ramp + power,
            acceleratorBaseDamage,
            acceleratorDamageCap + power);
    }

    private void TryToggleDeteriorationAcceleration()
    {
        // Future-Clone body: normal / charged hands only — no DA.
        if (playingAsFutureClone)
            return;

        if (daActive)
        {
            EndDeteriorationAcceleration(force: false);
            return;
        }

        EnsureDaPool();
        if (daRemaining <= 0.05f)
            return;

        BeginDeteriorationAcceleration();
    }

    private void EnsureDaPool()
    {
        if (daPoolInitialized)
            return;
        daMaxCached = Mathf.Max(0.1f, deteriorationDuration);
        daRemaining = daMaxCached;
        daPoolInitialized = true;
    }

    private void BeginDeteriorationAcceleration()
    {
        EnsureDaPool();
        daRemaining = Mathf.Min(daRemaining, Mathf.Max(0.1f, deteriorationDuration));
        if (daRemaining <= 0.05f)
            return;

        daActive = true;
        daOverdrive = false;
        daActiveElapsed = 0f;
        deteriorateTickTimer = 0f;
        overdriveHpTimer = 0f;

        SetDeteriorationVisualsActive(true);
        RefreshDurationBar();
        ApplyProgressiveTimeScales();

        if (worldTint == null)
            worldTint = GetComponent<DeadlineDeteriorationWorldTint>()
                        ?? gameObject.AddComponent<DeadlineDeteriorationWorldTint>();
        worldTint.Apply(transform, yellowWorldTint);
    }

    private void TickDeteriorationAcceleration(float unscaledDt)
    {
        // Regen while off (Double Gear-style early-cancel regen).
        if (!daActive)
        {
            float poolMax = Mathf.Max(0.1f, deteriorationDuration);
            if (daPoolInitialized && daRemaining < poolMax)
            {
                daRemaining = Mathf.Min(poolMax, daRemaining + unscaledDt);
                RefreshDurationBar();
            }
            return;
        }

        daActiveElapsed += unscaledDt;
        ApplyProgressiveTimeScales();
        SpinClockHands(unscaledDt);
        TickDeteriorationDamage(unscaledDt);

        if (!daOverdrive)
        {
            daRemaining = Mathf.Max(0f, daRemaining - unscaledDt);
            RefreshDurationBar();

            if (daRemaining <= 0f)
            {
                // Empty meter: stay on, enter overdrive (unlike Double Gear forced end).
                daRemaining = 0f;
                daOverdrive = true;
                overdriveHpTimer = 0f;
                if (currentHealth <= overdriveForceStopAtHp)
                {
                    EndDeteriorationAcceleration(force: true);
                    return;
                }
            }
        }
        else
        {
            RefreshDurationBar();
            if (currentHealth <= overdriveForceStopAtHp)
            {
                EndDeteriorationAcceleration(force: true);
                return;
            }

            overdriveHpTimer += unscaledDt;
            if (overdriveHpTimer >= Mathf.Max(0.05f, overdriveSelfDamageInterval))
            {
                overdriveHpTimer = 0f;
                ApplyOverdriveSelfDamage();
                if (currentHealth <= overdriveForceStopAtHp)
                    EndDeteriorationAcceleration(force: true);
            }
        }
    }

    private void ApplyProgressiveTimeScales()
    {
        // +1 scale per second from 1, with separate caps for Deadline / enemies / bosses.
        float playerCap = maxPlayerTimeScale;
        float enemyCap = maxWorldTimeScale;
        float bossCap = maxBossTimeScale;

        if (HyperLevel >= 3)
        {
            playerCap = Mathf.Max(playerCap, hyper3PlayerTimeScale);
            enemyCap = Mathf.Max(1f, maxWorldTimeScale - 3f);
        }

        float enemy = Mathf.Min(enemyCap, 1f + daActiveElapsed);
        float boss = Mathf.Min(bossCap, 1f + daActiveElapsed);
        float player = Mathf.Min(playerCap, 1f + daActiveElapsed);
        DeteriorationAccelerationWorld.SetScales(enemy, boss, player);
    }

    private void TickDeteriorationDamage(float dt)
    {
        deteriorateTickTimer += dt;
        if (deteriorateTickTimer < Mathf.Max(0.05f, deteriorateTickInterval))
            return;

        deteriorateTickTimer = 0f;
        deterioratedThisTick.Clear();

        int ramp = Mathf.FloorToInt(daActiveElapsed);
        int enemyDmg = deteriorateEnemyBaseDamage + ramp;
        int bossDmg = deteriorateBossBaseDamage + ramp;

        Camera cam = Camera.main;
        Vector3 origin = transform.position;

        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null || boss.IsDead)
                continue;
            if (!IsInDeteriorationRange(boss.transform.position, boss.GetComponentInChildren<Renderer>(), cam, origin))
                continue;
            if (!deterioratedThisTick.Add(boss.GetInstanceID()))
                continue;
            boss.TakeDamage(bossDmg);
        }

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not ICommonEnemy enemy)
                continue;
            if (enemy.IsDead)
                continue;

            Component asComponent = behaviours[i];
            if (asComponent.GetComponentInParent<Boss>() != null)
                continue;

            Transform t = asComponent.transform;
            Renderer renderer = asComponent.GetComponentInChildren<Renderer>();
            if (!IsInDeteriorationRange(t.position, renderer, cam, origin))
                continue;
            if (!deterioratedThisTick.Add(asComponent.GetInstanceID()))
                continue;

            enemy.TakeDamage(enemyDmg);
        }
    }

    private bool IsInDeteriorationRange(Vector3 worldPos, Renderer renderer, Camera cam, Vector3 origin)
    {
        if (Vector2.Distance(origin, worldPos) <= deteriorationRange)
            return true;

        if (cam == null)
            return false;

        if (renderer != null)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        return vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
    }

    private void EndDeteriorationAcceleration(bool force)
    {
        if (!daActive && !force)
            return;

        daActive = false;
        daOverdrive = false;
        daActiveElapsed = 0f;
        deteriorateTickTimer = 0f;
        overdriveHpTimer = 0f;
        pendingAccelerator = false;
        ClearAttackStyleHold();

        DeteriorationAccelerationWorld.Clear();
        worldTint?.Release();
        SetDeteriorationVisualsActive(false);

        // Overdrive hurt sparks are not tied to real stun — clear any leftover on DA end.
        if (!IsStunned)
            VisualEffects.StopStunned(this);

        if (animator != null && !IsTimeFrozen)
            animator.speed = 1f;
    }

    private void ResolveDeteriorationVisuals()
    {
        if (deteriorationVisualContainer == null)
        {
            Transform found = FindChildRecursive(transform, "Deterioration Acceleretor Visuals")
                              ?? FindChildRecursive(transform, "Deterioration Accelerator Visuals");
            if (found != null)
                deteriorationVisualContainer = found.gameObject;
        }

        Transform root = deteriorationVisualContainer != null
            ? deteriorationVisualContainer.transform
            : transform;

        if (deteriorationClock == null)
        {
            Transform clock = FindChildRecursive(root, "Deterioration Accelerator clock");
            if (clock != null)
                deteriorationClock = clock;
        }

        if (clockLongHand == null && deteriorationClock != null)
        {
            Transform hand = FindChildRecursive(deteriorationClock, "Long Hand");
            if (hand != null)
                clockLongHand = hand;
        }

        if (clockShortHand == null && deteriorationClock != null)
        {
            Transform hand = FindChildRecursive(deteriorationClock, "Short Hand");
            if (hand != null)
                clockShortHand = hand;
        }

        if (durationBar == null)
        {
            Transform bar = FindChildRecursive(root, "meter UI")
                            ?? FindChildRecursive(root, "Deterioration Accelerator meter UI");
            if (bar != null)
                durationBar = bar;
        }
    }

    /// <summary>
    /// Overdrive HP drain: lose life + hurt spark, but no stun / knockback / invuln lock.
    /// Spark lasts only as long as normal hit stun (same timer as armored hits).
    /// </summary>
    private void ApplyOverdriveSelfDamage()
    {
        if (currentHealth <= 0)
            return;

        SetHealth(currentHealth - 1);
        if (currentHealth > 0)
            PlayTimedHurtSpark();
    }

    private void SetupClockHands()
    {
        // Hands point down; attach via their TOP to a pivot at the clock center, then spin the pivot.
        longHandPivot = CreateHandPivot(clockLongHand, "Long Hand Pivot");
        shortHandPivot = CreateHandPivot(clockShortHand, "Short Hand Pivot");
    }

    private Transform CreateHandPivot(Transform hand, string pivotName)
    {
        if (hand == null || deteriorationClock == null)
            return null;

        Transform existing = deteriorationClock.Find(pivotName);
        Transform pivot = existing != null ? existing : new GameObject(pivotName).transform;
        pivot.SetParent(deteriorationClock, false);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;

        hand.SetParent(pivot, false);
        hand.localRotation = Quaternion.identity;

        SpriteRenderer sr = hand.GetComponent<SpriteRenderer>();
        float localHalfY = 0.5f;
        if (sr != null && sr.sprite != null)
            localHalfY = sr.sprite.bounds.extents.y;

        // Sprite pivot is center → shift down so the TOP of the hand sits on the clock center.
        hand.localPosition = new Vector3(0f, -localHalfY, 0f);
        return pivot;
    }

    private void SetupDurationFill()
    {
        if (durationBar == null)
            return;

        // Match Bass Double Gear meter: white 1×1 fill scaled to the bar track, yellow tint.
        BoxCollider2D barCol = durationBar.GetComponent<BoxCollider2D>();
        SpriteRenderer barSr = durationBar.GetComponent<SpriteRenderer>();
        if (barCol != null)
        {
            durationBarFullWidth = Mathf.Max(0.05f, barCol.size.x * 0.96f);
            durationBarFillHeight = Mathf.Max(0.04f, barCol.size.y * 0.7f);
        }
        else if (barSr != null && barSr.sprite != null)
        {
            durationBarFullWidth = barSr.sprite.bounds.size.x * 0.9f;
            durationBarFillHeight = barSr.sprite.bounds.size.y * 0.55f;
        }

        if (durationFill == null)
        {
            Transform existing = durationBar.Find("Duration Fill") ?? durationBar.Find("Fill");
            if (existing != null)
                durationFill = existing.GetComponent<SpriteRenderer>();
        }

        if (durationFill == null && barSr != null)
        {
            var fillGo = new GameObject("Duration Fill");
            fillGo.transform.SetParent(durationBar, false);
            var fillSr = fillGo.AddComponent<SpriteRenderer>();
            fillSr.sprite = CreateWhiteFillSprite();
            fillSr.color = durationFillColor;
            fillSr.sortingLayerID = barSr.sortingLayerID;
            fillSr.sortingOrder = barSr.sortingOrder + 1;
            fillGo.transform.localScale = new Vector3(durationBarFullWidth, durationBarFillHeight, 1f);
            durationFill = fillSr;
        }
        else if (durationFill != null)
        {
            // Replace oversized bar-sprite fills from earlier setups.
            durationFill.sprite = CreateWhiteFillSprite();
            durationFill.color = durationFillColor;
            if (barSr != null)
            {
                durationFill.sortingLayerID = barSr.sortingLayerID;
                durationFill.sortingOrder = barSr.sortingOrder + 1;
            }
        }

        RefreshDurationBar();
    }

    private Sprite CreateWhiteFillSprite()
    {
        if (durationFillTexture == null)
        {
            durationFillTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            durationFillTexture.SetPixel(0, 0, Color.white);
            durationFillTexture.Apply();
            durationFillTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        return Sprite.Create(
            durationFillTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
    }

    private void RefreshDurationBar()
    {
        if (durationFill == null)
            return;

        float poolMax = Mathf.Max(0.1f, deteriorationDuration);
        float used01 = daActive
            ? Mathf.Clamp01((poolMax - daRemaining) / poolMax)
            : 0f;

        // Double Gear style: yellow fill grows left→right as duration is used.
        float width = Mathf.Max(0.01f, durationBarFullWidth);
        float height = Mathf.Max(0.02f, durationBarFillHeight);
        float filledWidth = width * used01;

        Transform fillT = durationFill.transform;
        fillT.localScale = new Vector3(Mathf.Max(0.001f, filledWidth), height, 1f);
        fillT.localPosition = new Vector3((-width * 0.5f) + (filledWidth * 0.5f), 0f, 0f);
        durationFill.color = durationFillColor;
        durationFill.enabled = daActive && used01 > 0.001f;
    }

    private void SpinClockHands(float dt)
    {
        // Rotate pivots at the clock center (hands hang from their tops).
        if (shortHandPivot != null)
            shortHandPivot.Rotate(0f, 0f, -shortHandSpinDegPerSec * dt, Space.Self);
        else if (clockShortHand != null)
            clockShortHand.Rotate(0f, 0f, -shortHandSpinDegPerSec * dt, Space.Self);

        if (longHandPivot != null)
            longHandPivot.Rotate(0f, 0f, -longHandSpinDegPerSec * dt, Space.Self);
        else if (clockLongHand != null)
            clockLongHand.Rotate(0f, 0f, -longHandSpinDegPerSec * dt, Space.Self);
    }

    private void SetDeteriorationVisualsActive(bool active)
    {
        if (deteriorationVisualContainer != null)
            deteriorationVisualContainer.SetActive(active);
        else
        {
            if (deteriorationClock != null)
                deteriorationClock.gameObject.SetActive(active);
            if (durationBar != null)
                durationBar.gameObject.SetActive(active);
        }

        if (!active && durationFill != null)
            durationFill.enabled = false;
    }

    private static Transform FindChildRecursive(Transform root, string nameContains)
    {
        if (root == null || string.IsNullOrEmpty(nameContains))
            return null;

        if (root.name.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), nameContains);
            if (found != null)
                return found;
        }

        return null;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        deteriorationDuration = Mathf.Max(0.1f, deteriorationDuration);
        maxWorldTimeScale = Mathf.Max(1f, maxWorldTimeScale);
        maxBossTimeScale = Mathf.Max(1f, maxBossTimeScale);
        maxPlayerTimeScale = Mathf.Max(1f, maxPlayerTimeScale);
        hyper3PlayerTimeScale = Mathf.Max(maxPlayerTimeScale, hyper3PlayerTimeScale);
        deteriorationRange = Mathf.Max(0.5f, deteriorationRange);
        deteriorateTickInterval = Mathf.Max(0.05f, deteriorateTickInterval);
        shotFadeTravelDistance = Mathf.Max(0.5f, shotFadeTravelDistance);
        acceleratorDamageCap = Mathf.Max(1, acceleratorDamageCap);
        overdriveForceStopAtHp = Mathf.Max(1, overdriveForceStopAtHp);
        hardModeDaExtraIFrames = Mathf.Max(0f, hardModeDaExtraIFrames);
        attackStyleAcceleratorMinLevel = Mathf.Max(1, attackStyleAcceleratorMinLevel);
        aerialDaRefillMinLevel = Mathf.Max(1, aerialDaRefillMinLevel);
        futureCloneCap = Mathf.Max(1, futureCloneCap);
        futureCloneSearchRadius = Mathf.Max(1f, futureCloneSearchRadius);
        futureCloneGuardAhead = Mathf.Max(0.5f, futureCloneGuardAhead);
        futureCloneShootInterval = Mathf.Max(0.1f, futureCloneShootInterval);
        futureCloneChargeSeconds = Mathf.Max(0.5f, futureCloneChargeSeconds);
        futureCloneRecallHoldSeconds = Mathf.Max(0.1f, futureCloneRecallHoldSeconds);
        futureCloneClockPulseSeconds = Mathf.Max(0.5f, futureCloneClockPulseSeconds);
        futureCloneClockPulseCount = Mathf.Max(1, futureCloneClockPulseCount);
    }
#endif
}
