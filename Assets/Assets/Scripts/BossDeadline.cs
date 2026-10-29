using System.Collections;
using UnityEngine;

/// <summary>
/// Boss Deadline — playable Deadline's weapons / DA / Future Clones, with Boss Count's zone AI
/// as the fight blueprint only. Count Hyper Speed, red tint, orbit Midnight clocks, rewind
/// playback, and Count time-clones are hard-sealed and never run.
/// </summary>
public class BossDeadline : BossCount
{
    [Header("Deadline - Projectiles")]
    [SerializeField] private Projectile acceleratorShotPrefab;
    [SerializeField] private float acceleratorShotSpeed = 18f;
    [SerializeField] private int acceleratorShotDamage = 2;

    [Header("Deadline - Deterioration Acceleration")]
    [SerializeField] private float daEnemyScale = 2.4f;
    [SerializeField] private float daBossSelfScale = 1.6f;
    [SerializeField] private float daPlayerPressureScale = 1.35f;
    [SerializeField] [Range(0f, 1f)] private float daYellowTint = 0.52f;
    [SerializeField] private float daTickInterval = 0.55f;
    [SerializeField] private int daTickDamage = 1;
    [SerializeField] private float daRange = 14f;
    [Tooltip("DA / clone specials need this many normal patterns between uses.")]
    [SerializeField] private int daSpecialEveryNormal = 1;
    [SerializeField] private int daSpecialEveryEasy = 2;
    [SerializeField] private int daSpecialEveryHard = 1;
    [SerializeField] [Range(0f, 1f)] private float daLayerChanceNormal = 0.88f;
    [SerializeField] [Range(0f, 1f)] private float daLayerChanceHard = 0.95f;
    [SerializeField] [Range(0f, 1f)] private float daLayerChanceEasy = 0.7f;
    [SerializeField] private float daMidnightDuration = 2.4f;
    [Tooltip("Extra chance to open with a dedicated DA chase.")]
    [SerializeField] [Range(0f, 1f)] private float daOpenChaseChance = 0.75f;

    [Header("Deadline - Clone Ambush")]
    [SerializeField] private Color futureCloneTint = new Color(1f, 0.92f, 0.15f, 0.85f);
    [SerializeField] private float cloneAmbushSearchRadius = 16f;
    [SerializeField] private float cloneShootInterval = 0.5f;

    private DeadlineDeteriorationWorldTint daTint;
    private bool daActive;
    private float daTickTimer;
    private float daElapsed;
    private float midnightAccelNextShotAt;

    protected override string DefaultBossId => "Deadline";

    /// <summary>Blocks CountSignatureTimeModule from spawning Count orbit Long Hands.</summary>
    protected override bool HasOrbitLongHands => false;

    /// <summary>Deadline TimeStop / Midnight use DA + accelerators — only need short hand / cooldown.</summary>
    protected override bool IsPatternReady(Pattern pattern)
    {
        if (pattern == Pattern.TimeStop || pattern == Pattern.Midnight)
            return shortHandPrefab != null && Time.time >= timeStopReadyAt;
        if (pattern == Pattern.MetalHop || pattern == Pattern.QuickBoomerang || pattern == Pattern.TimeClones)
            return shortHandPrefab != null;
        if (pattern is Pattern.HyperChase or Pattern.HyperEvade or Pattern.SoloRewind or Pattern.TimeClones)
            return AllowSpecialPowerNow();
        return true;
    }

    protected override void Awake()
    {
        // Yellow telegraphs before base builds auras.
        telegraphColor = new Color(1f, 0.92f, 0.2f, 1f);
        enrageAuraColor = new Color(0.85f, 0.7f, 0.05f, 1f);
        enrageAuraFlickerColor = new Color(1f, 0.95f, 0.35f, 1f);

        base.Awake();

        // Strip every Count Hyper / red-tint leftover. Deadline only uses yellow DA.
        CountHyperSpeedWorldTint countTint = GetComponent<CountHyperSpeedWorldTint>();
        if (countTint != null)
            Destroy(countTint);

        CountChargeAuraVisual countAura = GetComponentInChildren<CountChargeAuraVisual>(true);
        if (countAura != null)
            countAura.enabled = false;

        if (acceleratorShotPrefab == null)
            acceleratorShotPrefab = shortHandPrefab;

        daTint = GetComponent<DeadlineDeteriorationWorldTint>()
                 ?? gameObject.AddComponent<DeadlineDeteriorationWorldTint>();

        // Never leave Count turret clones from a prior fight / mistaken spawn.
        BossCountTimeClone.DestroyAllForOwner(this);

        // Ready for DA / clone specials on the first eligible pattern.
        patternsSinceSpecial = 99;

        ClearAllCountHyperSlow();
        ForceAssertNoCountHyper();
    }

    /// <summary>Hard seal: orbit Long Hands never spawn for Deadline.</summary>
    protected override void SpawnOrbitHand(float angleDegrees, float scale)
    {
    }

    protected override void LaunchOrbitHand(int index, float speed, int damage)
    {
    }

    protected override void ClearOrbitHands()
    {
    }

    protected override void UpdateAnimator()
    {
        if (animator == null)
            return;

        // DA speeds Deadline up slightly; never key off Count HyperSpeedWorldSlow.
        animator.speed = daActive ? Mathf.Max(1f, daBossSelfScale * 0.75f) : 1f;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = motion == Motion.Walk && Mathf.Abs(walkDir) > 0.01f && !isDashing;
        // Hold IsMoving through the shoot window so Run Shoot doesn't snap to Ground Shoot.
        if (!moving && Time.time < shootPoseUntil && rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.2f)
            moving = true;
        SetAnimBool("IsMoving", moving);
        SetAnimBool("IsGrounded", isGrounded);
        SetAnimBool("IsInAir", !isGrounded);
        SetAnimBool("IsDashing", isDashing);
        SetAnimBool("IsShooting", Time.time < shootPoseUntil);
        SetAnimBool("AimUp", false);
        SetAnimBool("IsStunned", false);
        SetAnimBool("IsFalling", !isGrounded && vy < -0.01f);
        SetAnimBool("IsJumping", !isGrounded && vy >= -0.01f);
        SetAnimFloat("VerticalSpeed", vy);
    }

    private void ForceAssertNoCountHyper()
    {
        if (HyperSpeedWorldSlow.HasSlowLayers)
            ClearAllCountHyperSlow();
    }

    /// <summary>Never run Count's Hyper Midnight chain — always Deadline DA Midnight.</summary>
    protected override IEnumerator RunSignatureMidnight()
    {
        yield return MidnightPattern();
    }

    /// <summary>Time Stop / Midnight module hooks → DA instead of Hyper Speed world slow.</summary>
    public override void BeginDrasticTimeSlow(float worldScale)
    {
        BeginBossDa();
    }

    public override void EndDrasticTimeSlow()
    {
        EndBossDaInternal(clearWorld: true);
    }

    public override void BeginTimeTintOnly()
    {
        // No Count red tint.
    }

    public override void EndTimeTintOnly()
    {
    }

    protected override void OnDisable()
    {
        BossDeadlineFutureClone.DestroyAllForOwner(this);
        BossCountTimeClone.DestroyAllForOwner(this);
        ClearAllCountHyperSlow();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        BossDeadlineFutureClone.DestroyAllForOwner(this);
        BossCountTimeClone.DestroyAllForOwner(this);
        ClearAllCountHyperSlow();
        base.OnDestroy();
    }

    protected override void OnCinematicDeathStarted()
    {
        EndBossDaInternal(clearWorld: true);
        BossDeadlineFutureClone.DestroyAllForOwner(this);
        BossCountTimeClone.DestroyAllForOwner(this);
        base.OnCinematicDeathStarted();
    }

    protected override void HandleBossFixedUpdate()
    {
        base.HandleBossFixedUpdate();
        if (daActive)
            TickBossDa(Time.fixedDeltaTime);
    }

    /// <summary>Playable Count only — Hyper Speed is the polar opposite of DA.</summary>
    public static bool IsImmuneToBossDeadlineDa(PlayerController player)
    {
        if (player == null)
            return true;
        if (player is DeadlinePlayerController)
            return false;
        return string.Equals(player.CharacterId, "Count", System.StringComparison.OrdinalIgnoreCase);
    }

    protected override bool AllowSpecialPowerNow()
    {
        // DA chase / evade / clones should be available almost every cycle.
        int every = daSpecialEveryNormal;
        switch (GameSettings.Difficulty)
        {
            case GameDifficulty.Easy:
                every = Mathf.Max(1, daSpecialEveryEasy);
                break;
            case GameDifficulty.Hard:
                every = Mathf.Max(1, daSpecialEveryHard);
                break;
            default:
                every = Mathf.Max(1, daSpecialEveryNormal);
                break;
        }

        return patternsSinceSpecial >= every;
    }

    protected override bool ShouldLayerHyperOnPattern(Pattern pattern)
    {
        // Layer DA onto nearly every normal approach so yellow pressure is constant.
        if (pattern is Pattern.TimeStop or Pattern.Midnight or Pattern.RewindRush)
            return false;
        if (pattern is Pattern.HyperChase or Pattern.HyperEvade)
            return false;

        float chance = GameSettings.Difficulty == GameDifficulty.Hard
            ? daLayerChanceHard
            : GameSettings.Difficulty == GameDifficulty.Easy
                ? daLayerChanceEasy
                : daLayerChanceNormal;
        if (Random.value < chance)
            return true;

        return Random.value < daOpenChaseChance &&
               pattern is Pattern.MetalHop or Pattern.QuickRush or Pattern.QuickBoomerang
                   or Pattern.SoloRewind or Pattern.TimeClones;
    }

    protected override void PlayShootAnim()
    {
        // Always prefer Run Shoot while walking — Deadline's Ground Shoot looks like a slide.
        shootPoseUntil = Time.time + 0.3f;
        if (animator == null)
            return;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool movingOnGround = isGrounded &&
                              ((motion == Motion.Walk && Mathf.Abs(walkDir) > 0.01f) ||
                               (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.25f));

        string trigger;
        string stateName;
        if (!isGrounded)
        {
            trigger = vy < -0.01f ? "FallShot" : "JumpShot";
            stateName = "Jump Shoot";
        }
        else if (movingOnGround)
        {
            trigger = "RunShoot";
            stateName = "Run Shoot";
        }
        else
        {
            trigger = "Shoot";
            stateName = "Ground shoot";
        }

        SetAnimBool("IsMoving", movingOnGround);
        SetAnimBool("IsShooting", true);
        SetAnimBool("IsDashing", false);

        if (HasParam("Shoot"))
            animator.ResetTrigger("Shoot");
        if (HasParam("RunShoot"))
            animator.ResetTrigger("RunShoot");
        if (HasParam("JumpShot"))
            animator.ResetTrigger("JumpShot");
        if (HasParam("FallShot"))
            animator.ResetTrigger("FallShot");

        if (HasParam(trigger))
            animator.SetTrigger(trigger);

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);
    }

    // ---------- Hyper / Flash → Deterioration Acceleration only ----------

    protected override void BeginBossHyperSpeed()
    {
        BeginBossDa();
    }

    protected override void EndBossHyperSpeed()
    {
        EndBossDaInternal(clearWorld: true);
    }

    /// <summary>DA chase with accelerator pressure — never Count Hyper Speed.</summary>
    protected override IEnumerator HyperChasePattern()
    {
        yield return HyperSpeedAssist(chase: true);
        float t = 0f;
        float nextShot = Time.time + 0.12f;
        while (t < 1.25f)
        {
            t += ActionDeltaTime;
            float dx = DxToPlayer();
            if (isGrounded)
            {
                motion = Motion.Walk;
                walkDir = Mathf.Abs(dx) > 0.2f ? Mathf.Sign(dx) : 0f;
            }

            FacePlayer();
            if (Time.time >= nextShot)
            {
                nextShot = Time.time + Mathf.Max(0.14f, midnightShootInterval * 0.8f);
                FireAcceleratorAtPlayer();
            }

            yield return null;
        }

        EndBossHyperSpeed();
        motion = Motion.Stop;
    }

    /// <summary>DA evade dash — never Count Hyper Speed.</summary>
    protected override IEnumerator HyperEvadePattern()
    {
        yield return HyperSpeedAssist(chase: false);
        float away = -Mathf.Sign(DxToPlayer());
        if (Mathf.Abs(away) < 0.01f)
            away = -facingSign;
        yield return Rush(away, Mathf.Clamp(dashDistanceSafe(), rushMinDistance, rushMaxDistance),
            rushSpeed * 1.15f, 0, null);
        EndBossHyperSpeed();
    }

    /// <summary>
    /// Count Flash Man Time Stop remapped: DA pressure + accelerator volleys (no Hyper world slow,
    /// no red tint, no orbit Long Hands).
    /// </summary>
    protected override IEnumerator TimeStopPattern()
    {
        timeStopReadyAt = Time.time + Mathf.Max(0f, timeStopCooldown);
        yield return ClockWindup(timeStopWindupSeconds, 1);

        BeginBossDa();
        float frozen = 0f;
        float nextShot = Time.time + 0.08f;
        while (frozen < timeStopFrozenSeconds)
        {
            frozen += ActionDeltaTime;
            PlayerController player = FindPlayer();
            if (player == null || player.IsDead)
                break;

            FacePlayer();
            float dx = DxToPlayer();
            if (isGrounded && Mathf.Abs(dx) > 0.4f)
            {
                motion = Motion.Walk;
                walkDir = Mathf.Sign(dx);
            }
            else
            {
                motion = Motion.Stop;
                walkDir = 0f;
            }

            if (Time.time >= nextShot)
            {
                nextShot = Time.time + Mathf.Max(0.12f, midnightShootInterval * 0.7f);
                FireAcceleratorAtPlayer();
            }

            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        EndBossDaInternal(clearWorld: true);
        yield return Wait(0.2f);
    }

    private void BeginBossDa()
    {
        // Hard rule: never leave Count Hyper Speed running on Deadline.
        ForceClearCountHyperSlow();

        daActive = true;
        daElapsed = 0f;
        daTickTimer = 0f;
        hyperSpeedActive = true;
        ownsHyperSlow = false;
        ownsFlashSlow = false;
        forceTrail = true;

        float enemy = Mathf.Max(1.2f, daEnemyScale);
        float boss = Mathf.Max(1.1f, daBossSelfScale);
        // Keep player personal scale at 1 — Boss DA pressures via damage ticks, not player speed-up.
        DeteriorationAccelerationWorld.SetScales(enemy, boss, 1f);

        if (daTint == null)
            daTint = GetComponent<DeadlineDeteriorationWorldTint>()
                     ?? gameObject.AddComponent<DeadlineDeteriorationWorldTint>();

        // Strong yellow wash so DA is obvious; Exclude boss body so he stays readable.
        float tint = Mathf.Clamp(daYellowTint, 0.4f, 0.75f);
        daTint.Apply(transform, tint);

        // Count is unaffected by DA — keep his sprites out of the yellow wash.
        PlayerController player = FindPlayer();
        if (IsImmuneToBossDeadlineDa(player) && player != null)
            DeadlineDeteriorationWorldYellowness.Retain(player.transform, tint);

        HyperSpeedWorldSlow.RefreshActiveFromAcceleration();
        SoundManager.Instance?.PlayClockworkToc();
    }

    private void EndBossDaInternal(bool clearWorld)
    {
        ForceClearCountHyperSlow();

        daActive = false;
        daElapsed = 0f;
        daTickTimer = 0f;
        hyperSpeedActive = false;
        ownsHyperSlow = false;
        ownsFlashSlow = false;
        forceTrail = false;
        daTint?.Release();

        PlayerController player = FindPlayer();
        if (player != null)
            DeadlineDeteriorationWorldYellowness.Release(player.transform);

        if (clearWorld)
            DeteriorationAccelerationWorld.Clear();
    }

    private void ForceClearCountHyperSlow()
    {
        ClearAllCountHyperSlow();
    }

    private void TickBossDa(float dt)
    {
        daElapsed += dt;
        float ramp = 1f + daElapsed * 0.35f;
        DeteriorationAccelerationWorld.SetScales(
            Mathf.Min(4.5f, daEnemyScale * ramp),
            Mathf.Min(2.5f, daBossSelfScale * ramp),
            1f);

        daTickTimer += dt;
        if (daTickTimer < Mathf.Max(0.15f, daTickInterval))
            return;
        daTickTimer = 0f;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead || IsImmuneToBossDeadlineDa(player))
            return;

        float dist = Vector2.Distance(transform.position, player.transform.position);
        if (dist > Mathf.Max(2f, daRange))
            return;

        int dmg = Mathf.Max(1, Damage(daTickDamage));
        player.TakeDamage(dmg, transform, applyKnockback: false);
    }

    // ---------- Midnight → DA + accelerator shots ----------

    protected override IEnumerator MidnightPattern()
    {
        yield return RushTelegraph(Mathf.Max(0.15f, midnightWindupSeconds * 0.55f));
        BeginBossDa();
        midnightAccelNextShotAt = Time.time + 0.1f;

        float t = 0f;
        float dur = Mathf.Max(1.2f, daMidnightDuration);
        while (t < dur)
        {
            t += ActionDeltaTime;
            PlayerController player = FindPlayer();
            if (player == null || player.IsDead)
                break;

            FacePlayer();
            float dx = DxToPlayer();
            if (isGrounded)
            {
                motion = Motion.Walk;
                walkDir = Mathf.Abs(dx) > 0.35f ? Mathf.Sign(dx) : facingSign;
            }

            if (Time.time >= midnightAccelNextShotAt)
            {
                midnightAccelNextShotAt = Time.time + Mathf.Max(0.12f, midnightShootInterval * 0.75f);
                FireAcceleratorAtPlayer();
            }

            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        EndBossHyperSpeed();
        yield return Wait(0.15f);
    }

    // ---------- Rewind Rush → Clone Ambush ----------

    protected override IEnumerator RewindRushPattern()
    {
        BossCountTimeClone.DestroyAllForOwner(this);

        int dashes = Mathf.Max(2, rewindRushDashes);
        float speed = rewindRushSpeed * DifficultyTuning.BossRewindRushSpeedMul();
        float pause = rewindRushPause * DifficultyTuning.BossRewindRushPauseMul();
        float windup = rushWindupSeconds * DifficultyTuning.BossRewindRushPauseMul();

        yield return RushTelegraph(windup);
        forceTrail = true;

        for (int i = 0; i < dashes; i++)
        {
            float dx = DxToPlayer();
            float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
            float distance = Mathf.Clamp(Mathf.Abs(dx) + rushOvershoot, rushMinDistance, rushMaxDistance);
            yield return Rush(dir, distance, speed, Damage(rewindRushDamage), null);
            SpawnAmbushCloneBesideSelf(i);
            if (pause > 0.01f)
                yield return Wait(pause);
        }

        forceTrail = false;
        FacePlayer();
        yield return Wait(0.85f);
    }

    protected override IEnumerator TimeClonesPattern()
    {
        if (shortHandPrefab == null || spriteRenderer == null)
            yield break;

        // Never leave Count time-clones around if anything spawned them.
        BossCountTimeClone.DestroyAllForOwner(this);

        FacePlayer();
        int clones = GameSettings.Difficulty == GameDifficulty.Hard ? 3 : 2;
        for (int i = 0; i < clones; i++)
        {
            Vector3 offset = new Vector3((i - (clones - 1) * 0.5f) * 1.6f, 0.1f, 0f);
            SpawnAmbushCloneAt(transform.position + offset);
            yield return Wait(0.12f);
        }

        yield return Wait(1.2f);
    }

    protected override IEnumerator SoloRewindPattern()
    {
        // Mini ambush: two short dashes that seed clones, then press with a short hand.
        forceTrail = true;
        yield return RushTelegraph(0.2f);
        for (int i = 0; i < 2; i++)
        {
            float dir = DirToPlayer();
            float distance = Mathf.Clamp(Mathf.Abs(DxToPlayer()) + 2f, rushMinDistance, rushMaxDistance);
            yield return Rush(dir, distance, rushSpeed * 1.05f, 0, null);
            SpawnAmbushCloneBesideSelf(i);
            yield return Wait(0.12f);
        }

        forceTrail = false;
        FacePlayer();
        FireShortHandAtPlayer();
        yield return Wait(0.35f);
    }

    protected override void FireShortHandAtPlayer()
    {
        // Prefer accelerator during DA / midnight pressure; otherwise Deadline short hand.
        if (daActive && acceleratorShotPrefab != null)
        {
            FireAcceleratorAtPlayer();
            return;
        }

        base.FireShortHandAtPlayer();
    }

    private void FireAcceleratorAtPlayer()
    {
        Projectile prefab = acceleratorShotPrefab != null ? acceleratorShotPrefab : shortHandPrefab;
        if (prefab == null)
            return;

        float speed = Mathf.Max(prefab.Speed, acceleratorShotSpeed) * 1.15f;
        FireAtPlayer(prefab, speed, Damage(acceleratorShotDamage), 0f);
    }

    private void SpawnAmbushCloneBesideSelf(int index)
    {
        float side = (index % 2 == 0) ? facingSign : -facingSign;
        Vector3 pos = transform.position + new Vector3(side * (1.1f + index * 0.35f), 0.1f, 0f);
        SpawnAmbushCloneAt(pos);
    }

    private void SpawnAmbushCloneAt(Vector3 position)
    {
        if (shortHandPrefab == null)
            return;

        // Never snapshot the boss's current Dash sprite — clones own a Deadline animator.
        Vector2 bodySize = new Vector2(0.7f, 1.35f);
        Vector2 bodyOffset = Vector2.zero;
        if (bodyCollider is BoxCollider2D box)
        {
            bodySize = box.size;
            bodyOffset = box.offset;
        }

        BossDeadlineFutureClone.Spawn(
            this,
            position,
            futureCloneTint,
            shortHandPrefab,
            longHandPrefab,
            Mathf.Max(4, Damage(metalShotDamage) + 3),
            cloneShootInterval,
            cloneAmbushSearchRadius,
            moveSpeed * 0.95f,
            jumpForce,
            fallMultiplier,
            groundLayers,
            groundCheckRadius,
            bodySize,
            bodyOffset,
            facingSign > 0f);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        acceleratorShotSpeed = Mathf.Max(1f, acceleratorShotSpeed);
        acceleratorShotDamage = Mathf.Max(1, acceleratorShotDamage);
        daEnemyScale = Mathf.Max(1f, daEnemyScale);
        daBossSelfScale = Mathf.Max(1f, daBossSelfScale);
        daPlayerPressureScale = Mathf.Max(1f, daPlayerPressureScale);
        daTickInterval = Mathf.Max(0.1f, daTickInterval);
        daTickDamage = Mathf.Max(1, daTickDamage);
        daRange = Mathf.Max(1f, daRange);
        daSpecialEveryNormal = Mathf.Max(1, daSpecialEveryNormal);
        daSpecialEveryEasy = Mathf.Max(1, daSpecialEveryEasy);
        daSpecialEveryHard = Mathf.Max(1, daSpecialEveryHard);
        daMidnightDuration = Mathf.Max(0.8f, daMidnightDuration);
        cloneAmbushSearchRadius = Mathf.Max(2f, cloneAmbushSearchRadius);
        cloneShootInterval = Mathf.Max(0.15f, cloneShootInterval);
        daLayerChanceNormal = Mathf.Clamp01(daLayerChanceNormal);
        daLayerChanceHard = Mathf.Clamp01(daLayerChanceHard);
        daLayerChanceEasy = Mathf.Clamp01(daLayerChanceEasy);
        daOpenChaseChance = Mathf.Clamp01(daOpenChaseChance);
    }
#endif
}
