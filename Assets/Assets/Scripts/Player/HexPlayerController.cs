using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hex — plays like Harlie (red waves, 1.5× damage and the triple charged wave are set on her prefab),
/// with Omega Zero (Mega Man Zero) moves on top:
/// Down + Attack in the air: her pogo becomes a dive stab straight down that pierces anything it hits,
/// and the landing slam fires 5 waves in an upward fan (back, back-up, up, forward-up, forward).
/// Up + Attack: summon a pair of V-Bots (one pair at a time, short cooldown).
/// Sparks burst when she's hurt and stream while her charge is high.
/// Hyper Ability (full health): faster V-Bot cooldown, stronger / longer V-Bots (homing at max level),
/// and faster, stronger, longer dive-slam waves that grow as they travel.
/// Attack Style (Speed or Heavy, level 3+): Up + Attack summons 8 orbiting V-Bots; each tap slash or kick
/// sends one flying ahead. The batch must be used up before the next.
/// </summary>
public class HexPlayerController : HarliePlayerController
{
    [Header("Hex - Dive Stab (Down + Attack in the air)")]
    [SerializeField] private float diveSpeed = 24f;
    [SerializeField] private int diveDamage = 3;
    [Tooltip("The dive gives up after this long if she never lands.")]
    [SerializeField] private float diveMaxSeconds = 2f;

    [Header("Hex - Landing Slam (5-wave upward fan)")]
    [SerializeField] private int slamWaveDamage = 2;
    [Tooltip("Wave base travel distance × this.")]
    [SerializeField] private float slamWaveDistanceMultiplier = 4f;
    [Tooltip("Pose lock after landing (the Slash 3 clip length is used when attack durations sync to animation).")]
    [SerializeField] private float slamDuration = 0.38f;

    [Header("Hex - V-Bots (Up + Attack)")]
    [SerializeField] private HexVBot vBotPrefab;
    [SerializeField] private float vBotCooldown = 3f;
    [Tooltip("Spawn distance to each side of Hex (world units, 1 = one space).")]
    [SerializeField] private float vBotSideOffset = 1f;
    [Tooltip("V-Bot hit damage before upgrades (never multiplied).")]
    [SerializeField] private int vBotBaseDamage = 2;
    [Tooltip("Spaces a V-Bot shoots forward before upgrades.")]
    [SerializeField] private float vBotBaseSpaces = 10f;

    [Header("Hex - Upgrades: Hyper Ability (full health only)")]
    [SerializeField] private float hyperVBotCooldownCutPerLevel = 1f;
    [SerializeField] private int hyperVBotDamagePerLevel = 1;
    [SerializeField] private float hyperVBotSpacesPerLevel = 1f;
    [Tooltip("From this level V-Bots home in on the closest enemy.")]
    [SerializeField] private int hyperVBotHomingLevel = 5;
    [SerializeField] private int hyperSlamDamagePerLevel = 1;
    [Tooltip("Extra dive-slam wave distance per level (world units / spaces).")]
    [SerializeField] private float hyperSlamDistancePerLevel = 2f;
    [Tooltip("Extra dive-slam wave speed per level (0.35 = +35%).")]
    [SerializeField] private float hyperSlamSpeedPerLevel = 0.35f;
    [Tooltip("With any Hyper level, dive-slam waves grow to this × their size by max distance.")]
    [SerializeField] private float hyperSlamGrowMultiplier = 3f;

    [Header("Hex - Upgrades: Attack Style (orbiting V-Bots, Speed or Heavy style)")]
    [SerializeField] private int orbitMinStyleLevel = 3;
    [SerializeField] private int orbitBotCount = 8;
    [SerializeField] private float orbitRadius = 2.4f;
    [SerializeField] private float orbitDegreesPerSecond = 200f;
    [Tooltip("Damage an orbiting bot deals to enemies it touches.")]
    [SerializeField] private int orbitContactDamage = 1;

    [Header("Hex - Sparks")]
    [SerializeField] private HexSparkEmitter sparkEmitter;
    [Tooltip("The spark aura appears once the charge is at least this full (Count's aura shows almost right away).")]
    [SerializeField] [Range(0f, 1f)] private float sparkStreamChargeRatio = 0.07f;

    private static readonly Vector2[] SlamFan =
    {
        new Vector2(-1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
    };

    private bool isDiving;
    private float diveTimer;
    private float slamTimer;
    private float vBotLastUsedAt = -999f;
    private readonly List<HexVBot> liveVBots = new List<HexVBot>(2);
    private readonly List<HexVBot> orbitBots = new List<HexVBot>(8);

    private float CurrentVBotCooldown =>
        Mathf.Max(0f, vBotCooldown - Mathf.Max(0f, hyperVBotCooldownCutPerLevel) * ActiveHyperLevel);
    private int CurrentVBotDamage => Mathf.Max(0, vBotBaseDamage + Mathf.Max(0, hyperVBotDamagePerLevel) * ActiveHyperLevel);
    private float CurrentVBotSpaces => Mathf.Max(0f, vBotBaseSpaces + Mathf.Max(0f, hyperVBotSpacesPerLevel) * ActiveHyperLevel);
    private bool VBotsHome => ActiveHyperLevel > 0 && ActiveHyperLevel >= Mathf.Max(1, hyperVBotHomingLevel);
    private bool OrbitUnlocked => StyleLevel >= Mathf.Max(1, orbitMinStyleLevel) && (UsesSpeedStyle() || UsesHeavyStyle());
    private bool VBotCooldownReady => Time.time >= vBotLastUsedAt + CurrentVBotCooldown;

    protected override string HarlieCharacterId => "Hex";
    protected override bool SilverSwordsAllowed => false;
    protected override int JumpAttackDamage => isDiving ? ScaleOutgoingDamage(diveDamage) : base.JumpAttackDamage;
    private bool IsSlamming => slamTimer > 0f;

    public override bool WantsExtraCameraSmoothing => base.WantsExtraCameraSmoothing || isDiving;

    protected override void Awake()
    {
        base.Awake();
        if (sparkEmitter == null)
            sparkEmitter = GetComponent<HexSparkEmitter>();
    }

    protected override void OnDisable()
    {
        isDiving = false;
        slamTimer = 0f;
        if (sparkEmitter != null)
            sparkEmitter.Streaming = false;
        DestroyBots(orbitBots);
        base.OnDisable();
    }

    protected override void ClearForVehicleRide()
    {
        isDiving = false;
        slamTimer = 0f;
        if (sparkEmitter != null)
            sparkEmitter.StopImmediate();
        DestroyBots(orbitBots);
        DestroyBots(liveVBots);
        base.ClearForVehicleRide();
    }

    private static void DestroyBots(List<HexVBot> bots)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            if (bots[i] != null)
                Destroy(bots[i].gameObject);
        }
        bots.Clear();
    }

    protected override bool BlocksActionCancel()
    {
        return base.BlocksActionCancel() || isDiving || IsSlamming;
    }

    protected override bool CanPogoNow()
    {
        return !isDiving && base.CanPogoNow();
    }

    protected override void OnHitStunStarted()
    {
        EndDive();
        slamTimer = 0f;
        base.OnHitStunStarted();
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned || isDiving || IsSlamming)
            return;

        if (IsAimingDown() && !isGrounded && !IsChargeSlideActive)
        {
            BeginDive();
            return;
        }

        if (IsAimingUp())
        {
            if (OrbitUnlocked && CanSummonOrbit())
            {
                SummonOrbit();
                return;
            }

            if (!OrbitUnlocked && CanSummonVBots())
            {
                SummonVBots();
                return;
            }
        }

        base.OnAttackStarted(context);
    }

    protected override void OnRegularAttackStarted()
    {
        base.OnRegularAttackStarted();
        LaunchNextOrbitBot();
    }

    protected override void HandleCharacterUpdate()
    {
        base.HandleCharacterUpdate();

        if (isDiving)
        {
            diveTimer += Time.deltaTime;
            if (isGrounded)
                BeginSlam();
            else if (diveTimer >= Mathf.Max(0.1f, diveMaxSeconds))
                EndDive();
        }

        if (slamTimer > 0f)
            slamTimer -= Time.deltaTime;

        liveVBots.RemoveAll(bot => bot == null);
        orbitBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);

        if (sparkEmitter != null)
        {
            float charge = ChargeRatio;
            sparkEmitter.Streaming = IsChargingKick && charge >= sparkStreamChargeRatio;
            sparkEmitter.ChargeProgress = charge;
            sparkEmitter.FullCharge = charge >= 0.999f;
        }
    }

    protected override void HandleCharacterFixedUpdate()
    {
        base.HandleCharacterFixedUpdate();
        if (isDiving && rb != null)
            rb.linearVelocity = new Vector2(0f, -Mathf.Max(1f, diveSpeed));
    }

    protected override void ApplyHorizontalMove()
    {
        if (rb != null && (isDiving || (IsSlamming && isGrounded)))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override void OnLanded()
    {
        base.OnLanded();
        if (isDiving)
            BeginSlam();
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator != null && IsSlamming)
            animator.SetBool("IsAttacking", true);
    }

    // ---------- Dive stab + landing slam ----------

    private void BeginDive()
    {
        CancelChargeKick();
        if (IsMeleeAttacking)
            EndMeleeImmediate();
        if (isDashing)
            CancelDash(keepHorizontalMomentum: false);
        ClearAirSlashHold();

        isDiving = true;
        diveTimer = 0f;
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, -Mathf.Max(1f, diveSpeed));

        // The falling attack is re-armed with the dive's damage; pogo is off, so it pierces.
        RestartJumpAttack();
        SoundManager.Instance?.PlayHarlieSwordSwing();
        ReportTutorialAction(TutorialAction.DiveStab);
    }

    private void EndDive()
    {
        if (!isDiving)
            return;

        isDiving = false;
        if (!isGrounded)
            RestartJumpAttack();
    }

    private void BeginSlam()
    {
        if (!isDiving)
            return;

        EndDive();
        slamTimer = ResolveDuration("slash 3", slamDuration);
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (animator != null)
        {
            animator.SetInteger("AttackCombo", 3);
            animator.SetBool("IsAttacking", true);
            animator.SetBool("IsGrounded", true);
            animator.SetBool("IsInAir", false);
            animator.ResetTrigger("Attack");
            animator.Play("slash 3", 0, 0f);
        }

        Vector3 origin = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        int hyper = ActiveHyperLevel;
        int damage = ScaleOutgoingDamage(slamWaveDamage + Mathf.Max(0, hyperSlamDamagePerLevel) * hyper);
        float bonusDistance = Mathf.Max(0f, hyperSlamDistancePerLevel) * hyper;
        float speedMultiplier = 1f + Mathf.Max(0f, hyperSlamSpeedPerLevel) * hyper;
        MaliceSlashVolley volley = new MaliceSlashVolley();
        for (int i = 0; i < SlamFan.Length; i++)
        {
            Vector2 dir = new Vector2(SlamFan[i].x * FacingSign, SlamFan[i].y).normalized;
            MaliceSlashProjectile wave = FireWaveWithUpgrades(null, origin, damage, slamWaveDistanceMultiplier, dir, volley,
                bonusDistance, speedMultiplier);
            if (wave != null && hyper > 0)
                wave.SetGrowOverTravel(hyperSlamGrowMultiplier);
        }

        SoundManager.Instance?.PlayHarlieBigSwordClash();
        SoundManager.Instance?.PlayHarlieSwordSwing();
        if (sparkEmitter != null)
        {
            Vector3 feet = bodyCollider != null
                ? new Vector3(origin.x, bodyCollider.bounds.min.y, 0f)
                : transform.position;
            sparkEmitter.Burst(feet);
        }
    }

    // ---------- V-Bots ----------

    private bool CanSummonVBots()
    {
        liveVBots.RemoveAll(bot => bot == null);
        orbitBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);
        return vBotPrefab != null && VBotCooldownReady && liveVBots.Count == 0 && orbitBots.Count == 0 &&
               !IsChargeSlideActive;
    }

    private void SummonVBots()
    {
        ReportTutorialAction(TutorialAction.VBots);
        vBotLastUsedAt = Time.time;
        Vector3 center = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        float forward = FacingSign;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 pos = new Vector3(center.x + side * vBotSideOffset, center.y, 0f);
            HexVBot bot = Instantiate(vBotPrefab, pos, Quaternion.identity);
            bot.Launch(transform, forward, CurrentVBotDamage, CurrentVBotSpaces, VBotsHome);
            liveVBots.Add(bot);
            if (sparkEmitter != null)
                sparkEmitter.Burst(pos);
        }
    }

    // ---------- Orbiting V-Bots (Attack Style) ----------

    /// <summary>The whole batch must be used up (and the cooldown over) before the next one.</summary>
    private bool CanSummonOrbit()
    {
        orbitBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);
        return vBotPrefab != null && VBotCooldownReady && orbitBots.Count == 0 && !IsChargeSlideActive;
    }

    private void SummonOrbit()
    {
        ReportTutorialAction(TutorialAction.VBots);
        Vector3 center = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        int count = Mathf.Max(1, orbitBotCount);
        float step = 360f / count;
        for (int i = 0; i < count; i++)
        {
            HexVBot bot = Instantiate(vBotPrefab, center, Quaternion.identity);
            bot.BeginOrbit(transform, i * step, orbitRadius, orbitDegreesPerSecond, orbitContactDamage);
            orbitBots.Add(bot);
        }

        SoundManager.Instance?.PlayHarlieSwordSwing();
        if (sparkEmitter != null)
            sparkEmitter.Burst(center);
    }

    /// <summary>Sends the orbiting bot that is furthest toward her facing side flying ahead.</summary>
    private void LaunchNextOrbitBot()
    {
        orbitBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);
        if (orbitBots.Count == 0)
            return;

        float facing = FacingSign;
        int bestIndex = 0;
        float bestAhead = float.MinValue;
        for (int i = 0; i < orbitBots.Count; i++)
        {
            float ahead = (orbitBots[i].transform.position.x - transform.position.x) * facing;
            if (ahead > bestAhead)
            {
                bestAhead = ahead;
                bestIndex = i;
            }
        }

        HexVBot bot = orbitBots[bestIndex];
        orbitBots.RemoveAt(bestIndex);
        bot.LaunchFromOrbit(facing, CurrentVBotDamage, CurrentVBotSpaces, VBotsHome);
        if (orbitBots.Count == 0)
            vBotLastUsedAt = Time.time;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        diveSpeed = Mathf.Max(1f, diveSpeed);
        diveDamage = Mathf.Max(0, diveDamage);
        diveMaxSeconds = Mathf.Max(0.1f, diveMaxSeconds);
        slamWaveDamage = Mathf.Max(0, slamWaveDamage);
        slamWaveDistanceMultiplier = Mathf.Max(0.1f, slamWaveDistanceMultiplier);
        slamDuration = Mathf.Max(0.05f, slamDuration);
        vBotCooldown = Mathf.Max(0f, vBotCooldown);
        vBotSideOffset = Mathf.Max(0f, vBotSideOffset);
        vBotBaseDamage = Mathf.Max(0, vBotBaseDamage);
        vBotBaseSpaces = Mathf.Max(0f, vBotBaseSpaces);
        hyperVBotCooldownCutPerLevel = Mathf.Max(0f, hyperVBotCooldownCutPerLevel);
        hyperVBotDamagePerLevel = Mathf.Max(0, hyperVBotDamagePerLevel);
        hyperVBotSpacesPerLevel = Mathf.Max(0f, hyperVBotSpacesPerLevel);
        hyperVBotHomingLevel = Mathf.Clamp(hyperVBotHomingLevel, 1, PlayerUpgrades.MaxLevel);
        hyperSlamDamagePerLevel = Mathf.Max(0, hyperSlamDamagePerLevel);
        hyperSlamDistancePerLevel = Mathf.Max(0f, hyperSlamDistancePerLevel);
        hyperSlamSpeedPerLevel = Mathf.Max(0f, hyperSlamSpeedPerLevel);
        hyperSlamGrowMultiplier = Mathf.Max(1f, hyperSlamGrowMultiplier);
        orbitMinStyleLevel = Mathf.Clamp(orbitMinStyleLevel, 1, PlayerUpgrades.MaxLevel);
        orbitBotCount = Mathf.Clamp(orbitBotCount, 1, 16);
        orbitRadius = Mathf.Max(0.2f, orbitRadius);
        orbitContactDamage = Mathf.Max(0, orbitContactDamage);
    }
#endif
}
