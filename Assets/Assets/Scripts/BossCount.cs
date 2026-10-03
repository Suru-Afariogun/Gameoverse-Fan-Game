using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss Count. Mixes three Mega Man 2 Robot Masters:
///  • Metal Man — hops (low / high) and throws Short Hands at the player from the apex. Hops away when shot at.
///  • Quick Man — Hyper Speed rush through the player (red afterimages), and a high jump that throws boomerang hands.
///  • Flash Man — time stop: the screen turns red and the player freezes. Four Long Hands appear 4 spaces around the
///    player's spot and orbit very slowly. 0.5 s after time resumes they all fire into that spot.
/// Each distance zone runs a fixed rotation so the fight can be learned.
/// At ≤ 55% HP (Boss Fight and Copy Bot) he enrages and drops whatever he is doing for his signature combo:
/// Midnight (a 12-hand clock that fires inward like a ticking clock, with a gap) straight into Rewind Rush
/// (three rushes, a rewind, then the same rushes mirrored). The combo repeats every 3rd pattern after that,
/// and while it runs each hit gives him 0.5 s of i-frames. Enraged he also chains his normal moves.
/// Copy Bot Omega (≤ 30% HP): a little faster still.
/// </summary>
public class BossCount : Boss
{
    private enum Pattern
    {
        MetalHop,
        QuickRush,
        QuickBoomerang,
        TimeStop,
        Midnight,
        RewindRush
    }

    private enum Zone { Close, Mid, Far, AntiAir }

    private enum Motion { Free, Stop, Walk, Velocity, Hang }

    private static readonly Pattern[] CloseRotation =
        { Pattern.QuickRush, Pattern.MetalHop, Pattern.TimeStop, Pattern.QuickBoomerang };
    private static readonly Pattern[] MidRotation =
        { Pattern.MetalHop, Pattern.QuickBoomerang, Pattern.QuickRush, Pattern.TimeStop };
    private static readonly Pattern[] FarRotation =
        { Pattern.QuickBoomerang, Pattern.MetalHop, Pattern.QuickRush, Pattern.TimeStop };
    private static readonly Pattern[] AntiAirRotation =
        { Pattern.MetalHop, Pattern.QuickBoomerang, Pattern.TimeStop };

    [Header("Identity")]
    [Tooltip("Max HP when spawned as a Copy Bot.")]
    [SerializeField] private int copyBotMaxHealth = 120;
    [Tooltip("Scales every attack's damage.")]
    [SerializeField] private float damageMultiplier = 1f;

    [Header("Shots")]
    [SerializeField] private Projectile shortHandPrefab;
    [SerializeField] private Projectile longHandPrefab;
    [SerializeField] private GameVisualEffect busterBlastSmallPrefab;
    [SerializeField] private GameVisualEffect busterBlastBigPrefab;
    [Tooltip("Authored for facing LEFT; mirrored when he faces right. Found by name when empty.")]
    [SerializeField] private Transform firePoint;

    [Header("Tempo")]
    [SerializeField] private int lifeBarCount = 10;
    [Tooltip("Speed-up per life-bar segment lost (waits, telegraphs, recoveries).")]
    [SerializeField] private float tempoPerBarLost = 0.03f;
    [SerializeField] private float moveSpeedPerBarLost = 0.15f;
    [SerializeField] private float recoverDurationFullHp = 0.55f;
    [SerializeField] private float recoverDurationLowHp = 0.25f;

    [Header("Zones")]
    [SerializeField] private float closeZoneDistance = 3.5f;
    [SerializeField] private float midZoneDistance = 8f;
    [Tooltip("Player feet this far above his feet (and within Anti Air Max Distance) = anti-air rotation.")]
    [SerializeField] private float antiAirHeight = 1.6f;
    [SerializeField] private float antiAirMaxDistance = 7f;

    [Header("Metal Man - Hand Hops")]
    [SerializeField] private int metalHopsPerPattern = 3;
    [SerializeField] private float metalLowHopForce = 13f;
    [SerializeField] private float metalHighHopForce = 18f;
    [Tooltip("He drifts toward this distance from the player while hopping.")]
    [SerializeField] private float metalPreferredDistance = 6f;
    [Tooltip("White glint at the apex before the throw.")]
    [SerializeField] private float metalApexGlintSeconds = 0.12f;
    [Tooltip("Spaces between hands thrown in the same volley.")]
    [SerializeField] private float metalShotSpacing = 2.2f;
    [SerializeField] private float metalShotSpeed = 15f;
    [SerializeField] private int metalShotDamage = 2;

    [Header("Metal Man - Reactive Hop")]
    [Tooltip("While recovering, he hops and throws a hand when a player shot flies at him.")]
    [SerializeField] private bool dodgePlayerShots = true;
    [SerializeField] private float dodgeShotRadius = 5f;
    [SerializeField] private float dodgeCooldown = 1.4f;

    [Header("Quick Man - Rush")]
    [SerializeField] private float rushWindupSeconds = 0.35f;
    [SerializeField] private float rushSpeed = 22f;
    [Tooltip("How far past the player the rush ends.")]
    [SerializeField] private float rushOvershoot = 3f;
    [SerializeField] private float rushMinDistance = 4f;
    [SerializeField] private float rushMaxDistance = 16f;
    [SerializeField] private float rushTurnPause = 0.22f;
    [SerializeField] private int rushDamage = 3;

    [Header("Quick Man - Boomerang Hands")]
    [SerializeField] private float boomerangJumpForce = 19f;
    [SerializeField] private int boomerangCount = 3;
    [SerializeField] private float boomerangSpreadDegrees = 20f;
    [SerializeField] private float boomerangSpeed = 13f;
    [Tooltip("Distance flown before turning back toward him.")]
    [SerializeField] private float boomerangOutDistance = 7f;
    [SerializeField] private float boomerangTurnDegreesPerSecond = 300f;
    [SerializeField] private float boomerangLifetime = 4f;
    [SerializeField] private float boomerangGlintSeconds = 0.15f;
    [SerializeField] private int boomerangDamage = 2;

    [Header("Flash Man - Time Stop")]
    [Tooltip("Clock activation telegraph before time stops.")]
    [SerializeField] private float timeStopWindupSeconds = 0.6f;
    [SerializeField] private float timeStopCooldown = 7f;
    [SerializeField] private int timeStopHandCount = 4;
    [Tooltip("Spaces from the player's spot.")]
    [SerializeField] private float timeStopRadius = 4f;
    [SerializeField] private float timeStopOrbitDegreesPerSecond = 15f;
    [SerializeField] private float timeStopHandSpawnGap = 0.15f;
    [Tooltip("Total time the player stays frozen (includes the hands appearing).")]
    [SerializeField] private float timeStopFrozenSeconds = 1.5f;
    [Tooltip("Seconds after time resumes before the hands fire.")]
    [SerializeField] private float timeStopFireDelay = 0.5f;
    [SerializeField] private float timeStopHandSpeed = 14f;
    [SerializeField] private int timeStopHandDamage = 3;
    [Tooltip("Red screen wash while he alters time.")]
    [Range(0f, 1f)] [SerializeField] private float timeRedTint = 0.4f;

    [Header("Enrage (HP ratio)")]
    [Range(0f, 1f)] [SerializeField] private float enrageHealthRatio = 0.55f;
    [SerializeField] private float enrageTempoBonus = 0.15f;
    [SerializeField] private float enrageMoveSpeedBonus = 1.5f;
    [SerializeField] private int enrageBonusDamage = 1;
    [Tooltip("Enraged: Boomerang Hands → Rush and Time Stop → Rush, with no recovery between.")]
    [SerializeField] private bool enragedChains = true;

    [Header("Signature Combo: Midnight → Rewind Rush (HP ratio)")]
    [Tooltip("Boss Fight and Copy Bot: at this HP he drops his current move and starts the combo right away.")]
    [Range(0f, 1f)] [SerializeField] private float signatureHealthRatio = 0.55f;
    [Tooltip("After the first combo, it comes back every this many patterns (the combo counts as one).")]
    [SerializeField] private int signatureEvery = 3;
    [Tooltip("I-frames after each hit, only while the combo is running.")]
    [SerializeField] private float signatureHitInvincibility = 0.5f;
    [Tooltip("Body flicker rate (per second) during those i-frames.")]
    [SerializeField] private float signatureFlickerRate = 20f;
    [Range(0f, 1f)] [SerializeField] private float signatureFlickerAlpha = 0.35f;

    [Header("Copy Bot Omega (HP ratio)")]
    [Range(0f, 1f)] [SerializeField] private float omegaHealthRatio = 0.3f;
    [SerializeField] private float omegaTempoBonus = 0.1f;

    [Header("Signature - Midnight")]
    [SerializeField] private float midnightWindupSeconds = 0.9f;
    [SerializeField] private float midnightRadius = 5f;
    [Tooltip("Long Hands are scaled down so the gap can be escaped.")]
    [SerializeField] private float midnightHandScale = 0.7f;
    [SerializeField] private float midnightHandSpawnGap = 0.06f;
    [SerializeField] private float midnightFrozenSeconds = 1.6f;
    [SerializeField] private float midnightFireDelay = 0.5f;
    [Tooltip("Seconds between hands firing (the clock ticking).")]
    [SerializeField] private float midnightFireGap = 0.12f;
    [SerializeField] private float midnightHandSpeed = 15f;
    [SerializeField] private int midnightHandDamage = 2;

    [Header("Signature - Rewind Rush")]
    [SerializeField] private int rewindRushDashes = 3;
    [SerializeField] private float rewindRushSpeed = 26f;
    [SerializeField] private float rewindRushPause = 0.16f;
    [SerializeField] private float rewindPlaybackSeconds = 0.5f;
    [SerializeField] private int rewindRushDamage = 3;

    [Header("Auras")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color glintColor = Color.white;
    [SerializeField] private bool showEnrageAura = true;
    [SerializeField] private Color enrageAuraColor = new Color(0.75f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color enrageAuraFlickerColor = new Color(1f, 0.35f, 0.35f, 1f);
    [Range(0f, 1f)] [SerializeField] private float enrageAuraAlpha = 0.45f;
    [SerializeField] private float auraBaseScale = 1.12f;

    [Header("Afterimages")]
    [SerializeField] private bool enableAfterimages = true;
    [SerializeField] private int afterimageCount = 5;
    [SerializeField] private float afterimageSpacing = 0.045f;
    [SerializeField] private Color afterimageColor = new Color(1f, 0.3f, 0.3f, 1f);
    [Range(0f, 1f)] [SerializeField] private float afterimageAlphaStart = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float afterimageAlphaEnd = 0.12f;
    [SerializeField] private float afterimageLifetimePadding = 0.08f;

    // ---------- Runtime ----------

    private readonly Stack<IEnumerator> actionStack = new Stack<IEnumerator>();
    private readonly int[] zoneRotationIndex = new int[4];
    private Motion motion = Motion.Stop;
    private float walkDir;
    private Vector2 motionVelocity;
    private bool gravityOff;

    private float timeStopReadyAt;
    private float dodgeReadyAt;
    private bool signatureStarted;
    private int signaturePatternCounter;
    private bool signatureActive;
    private bool signatureInterruptPending;
    private bool signatureFlickering;
    private float signatureFlickerBaseAlpha = 1f;

    private int contactDamage;
    private bool contactHitDone;
    private bool forceTrail;
    private float shootPoseUntil;
    private float hitVfxTimer;

    private Vector3 firePointLeftLocal = new Vector3(-0.8f, -0.41f, 0f);

    private CountChargeAuraVisual clockAura;
    private CountHyperSpeedWorldTint timeTint;
    private bool timeTintActive;
    private PlayerController frozenPlayer;

    private readonly List<Projectile> orbitHands = new List<Projectile>(12);
    private readonly List<float> orbitHandAngles = new List<float>(12);
    private Vector2 orbitCenter;
    private float orbitRadius;
    private float orbitDegreesPerSecond;

    private readonly List<Collider2D> dodgeHits = new List<Collider2D>(16);
    private readonly List<Vector2> rewindPath = new List<Vector2>(256);

    private Aura telegraphAura;
    private Aura enrageAura;

    private readonly HashSet<int> animatorParams = new HashSet<int>();
    private GameObject[] afterimageObjects;
    private SpriteRenderer[] afterimageRenderers;
    private float afterimagesVisibleUntil;
    private readonly List<PoseSample> poseHistory = new List<PoseSample>(64);

    private struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    private sealed class Aura
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Material material;
        public float phase;
    }

    public override int CopyBotMaxHealthOverride => copyBotMaxHealth;

    private float HealthRatio => currentHealth / (float)Mathf.Max(1, maxHealth);
    private bool IsEnraged => HealthRatio <= enrageHealthRatio + 0.0001f;
    private bool OmegaActive => IsMainCopyBot && HealthRatio <= omegaHealthRatio + 0.0001f;
    private bool SignaturePhase => HealthRatio <= signatureHealthRatio + 0.0001f;

    private int BarsLost
    {
        get
        {
            int bars = Mathf.Max(1, lifeBarCount);
            return Mathf.Clamp(Mathf.FloorToInt((1f - HealthRatio) * bars + 0.0001f), 0, bars);
        }
    }

    private float BarsLost01 => Mathf.Clamp01(BarsLost / (float)Mathf.Max(1, lifeBarCount - 1));

    private float Tempo =>
        1f + BarsLost * Mathf.Max(0f, tempoPerBarLost) +
        (IsEnraged ? Mathf.Max(0f, enrageTempoBonus) : 0f) +
        (OmegaActive ? Mathf.Max(0f, omegaTempoBonus) : 0f);

    private float RecoverDuration => Mathf.Max(0.05f, Mathf.Lerp(recoverDurationFullHp, recoverDurationLowHp, BarsLost01));

    // ---------- Lifecycle ----------

    protected override void Awake()
    {
        SetBossId("BossCount");
        base.Awake();

        CacheAnimatorParams();
        if (firePoint == null)
            firePoint = transform.Find("FirePoint");
        if (firePoint != null)
            firePointLeftLocal = firePoint.localPosition;

        clockAura = GetComponentInChildren<CountChargeAuraVisual>(true);
        timeTint = GetComponent<CountHyperSpeedWorldTint>();
        if (timeTint == null)
            timeTint = gameObject.AddComponent<CountHyperSpeedWorldTint>();

        telegraphAura = CreateAura("TelegraphAura");
        enrageAura = CreateAura("EnrageAura");
        SetupAfterimages();
    }

    private void OnDisable()
    {
        EndTimeStop();
        ClearOrbitHands();
    }

    private void OnDestroy()
    {
        EndTimeStop();
        ClearOrbitHands();
        DestroyAura(telegraphAura);
        DestroyAura(enrageAura);
        DestroyAfterimages();
    }

    private void LateUpdate()
    {
        SyncFirePoint();
    }

    public void SetCopyBotAfterimageStyle(float grayLevel)
    {
        grayLevel = Mathf.Clamp(grayLevel, 0.05f, 1f);
        afterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
    }

    // ---------- Damage rules ----------

    /// <summary>Never flinches — hits only show sparks.</summary>
    protected override bool ShouldEnterHitStun(int healthBefore, int healthAfter, bool fromBigShot) => false;

    protected override void OnDamageApplied(int healthBefore, int healthAfter, bool enteredStun, bool fromBigShot)
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        hitVfxTimer = Mathf.Max(0.05f, hitStunDuration);

        if (signatureActive)
            invincibilityTimer = Mathf.Max(invincibilityTimer, signatureHitInvincibility);

        // First drop into the signature phase: drop the current move so the combo starts right away.
        if (!signatureStarted && SignaturePhase)
            signatureInterruptPending = true;
    }

    protected override void OnHitStunStarted()
    {
        ResetActions();
        base.OnHitStunStarted();
    }

    protected override float GetMoveSpeed()
    {
        float speed = moveSpeed + crystalMoveSpeedBonus + BarsLost * Mathf.Max(0f, moveSpeedPerBarLost);
        if (IsEnraged)
            speed += Mathf.Max(0f, enrageMoveSpeedBonus);
        return BossSpeed(speed);
    }

    private int Damage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage) + (IsEnraged ? Mathf.Max(0, enrageBonusDamage) : 0);
        dmg = Mathf.FloorToInt(dmg * Mathf.Max(0.1f, damageMultiplier) + 0.5f);
        return ApplyCrystalAttackBonus(dmg);
    }

    // ---------- Update loop ----------

    protected override void HandleBossUpdate()
    {
        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            if (actionStack.Count > 0 || orbitHands.Count > 0 || timeTintActive)
                ResetActions();
            motion = Motion.Stop;
            return;
        }

        if (signatureInterruptPending)
        {
            signatureInterruptPending = false;
            if (!signatureStarted)
                ResetActions();
        }

        if (actionStack.Count == 0)
            actionStack.Push(RunNextPattern());

        TickActions();
        TickOrbitHands();
        TickContactDamage();
    }

    /// <summary>Runs the active pattern; nested IEnumerators run as sub-steps and can be dropped at any time.</summary>
    private void TickActions()
    {
        int guard = 0;
        while (actionStack.Count > 0 && guard++ < 64)
        {
            IEnumerator top = actionStack.Peek();
            if (!top.MoveNext())
            {
                actionStack.Pop();
                continue;
            }

            if (top.Current is IEnumerator child)
            {
                actionStack.Push(child);
                continue;
            }

            return;
        }
    }

    protected override void HandleBossFixedUpdate()
    {
        if (rb == null)
            return;

        switch (motion)
        {
            case Motion.Walk:
                if (Mathf.Abs(walkDir) > 0.01f)
                    MoveHorizontal(walkDir);
                else
                    StopHorizontal();
                break;
            case Motion.Velocity:
                rb.linearVelocity = new Vector2(BossSpeed(motionVelocity.x), BossSpeed(motionVelocity.y));
                break;
            case Motion.Hang:
                rb.linearVelocity = Vector2.zero;
                break;
            case Motion.Stop:
                if (isGrounded)
                    StopHorizontal();
                break;
        }
    }

    protected override void ApplyFallMultiplier()
    {
        if (gravityOff || motion == Motion.Velocity || motion == Motion.Hang)
            return;
        base.ApplyFallMultiplier();
    }

    protected override void TickPostHitVisuals(float dt)
    {
        if (hitVfxTimer > 0f)
        {
            hitVfxTimer -= dt;
            if (hitVfxTimer <= 0f)
                VisualEffects.StopStunned(this);
        }

        RecordPoseHistory();
        UpdateAfterimages();
        TickEnrageAura();
        TickSignatureFlicker();
    }

    /// <summary>Body flickers while the signature-combo i-frames are up.</summary>
    private void TickSignatureFlicker()
    {
        if (spriteRenderer == null)
            return;

        if (invincibilityTimer > 0f && !IsDead)
        {
            if (!signatureFlickering)
            {
                signatureFlickering = true;
                signatureFlickerBaseAlpha = spriteRenderer.color.a;
            }

            bool dim = Mathf.FloorToInt(Time.time * Mathf.Max(1f, signatureFlickerRate)) % 2 == 1;
            Color c = spriteRenderer.color;
            c.a = dim ? signatureFlickerBaseAlpha * signatureFlickerAlpha : signatureFlickerBaseAlpha;
            spriteRenderer.color = c;
            return;
        }

        StopSignatureFlicker();
    }

    private void StopSignatureFlicker()
    {
        if (!signatureFlickering)
            return;

        signatureFlickering = false;
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = signatureFlickerBaseAlpha;
            spriteRenderer.color = c;
        }
    }

    protected override void UpdateAnimator()
    {
        if (animator == null)
            return;

        animator.speed = HyperSpeedWorldSlow.IsActive ? HyperSpeedWorldSlow.WorldTimeScale : 1f;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = motion == Motion.Walk && Mathf.Abs(walkDir) > 0.01f && !isDashing;
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

    // ---------- Pattern selection ----------

    private IEnumerator RunNextPattern()
    {
        PlayerController player = FindPlayer();
        if (player == null)
            yield break;

        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        if (TryStartSignature())
        {
            signatureActive = true;
            yield return RunPattern(Pattern.Midnight);
            CleanupAfterPattern();
            if (!isGrounded)
                yield return WaitUntilGrounded(2f);
            yield return RunPattern(Pattern.RewindRush);
            signatureActive = false;

            yield return Recover();
            yield break;
        }

        Pattern pattern = PickPattern(player);
        yield return RunPattern(pattern);

        if (enragedChains && IsEnraged && TryGetFollowUp(pattern, out Pattern followUp))
        {
            CleanupAfterPattern();
            if (!isGrounded)
                yield return WaitUntilGrounded(2f);
            yield return RunPattern(followUp);
        }

        yield return Recover();
    }

    /// <summary>
    /// Signature phase: the Midnight → Rewind Rush combo right away, then every <see cref="signatureEvery"/> patterns.
    /// </summary>
    private bool TryStartSignature()
    {
        if (!SignaturePhase)
            return false;

        int every = Mathf.Max(1, signatureEvery);
        if (!signatureStarted)
        {
            signatureStarted = true;
            signaturePatternCounter = every;
        }

        if (signaturePatternCounter < every)
        {
            signaturePatternCounter++;
            return false;
        }

        signaturePatternCounter = 1;
        return true;
    }

    private Pattern PickPattern(PlayerController player)
    {
        Zone zone = ResolveZone(player);
        Pattern[] rotation = zone switch
        {
            Zone.Close => CloseRotation,
            Zone.Mid => MidRotation,
            Zone.Far => FarRotation,
            _ => AntiAirRotation
        };

        // Moves that aren't ready (Time Stop on cooldown) are skipped; the rotation order stays the same.
        int z = (int)zone;
        Pattern pattern = rotation[zoneRotationIndex[z] % rotation.Length];
        for (int i = 0; i < rotation.Length; i++)
        {
            pattern = rotation[zoneRotationIndex[z] % rotation.Length];
            zoneRotationIndex[z] = (zoneRotationIndex[z] + 1) % rotation.Length;
            if (IsPatternReady(pattern))
                break;
        }

        return pattern;
    }

    private bool IsPatternReady(Pattern pattern)
    {
        if (pattern == Pattern.TimeStop)
            return longHandPrefab != null && Time.time >= timeStopReadyAt;
        if (pattern == Pattern.MetalHop || pattern == Pattern.QuickBoomerang)
            return shortHandPrefab != null;
        return true;
    }

    private Zone ResolveZone(PlayerController player)
    {
        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        if (dx <= antiAirMaxDistance && PlayerBounds(player).min.y - MyBounds().min.y >= antiAirHeight)
            return Zone.AntiAir;
        if (dx <= closeZoneDistance)
            return Zone.Close;
        return dx <= midZoneDistance ? Zone.Mid : Zone.Far;
    }

    private static bool TryGetFollowUp(Pattern pattern, out Pattern followUp)
    {
        switch (pattern)
        {
            case Pattern.QuickBoomerang:
            case Pattern.TimeStop:
                followUp = Pattern.QuickRush;
                return true;
            default:
                followUp = pattern;
                return false;
        }
    }

    private IEnumerator RunPattern(Pattern pattern)
    {
        switch (pattern)
        {
            case Pattern.QuickRush: return QuickRushPattern();
            case Pattern.QuickBoomerang: return QuickBoomerangPattern();
            case Pattern.TimeStop: return TimeStopPattern();
            case Pattern.Midnight: return MidnightPattern();
            case Pattern.RewindRush: return RewindRushPattern();
            default: return MetalHopPattern();
        }
    }

    private IEnumerator Recover()
    {
        CleanupAfterPattern();
        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        motion = Motion.Stop;
        float duration = RecoverDuration;
        float t = 0f;
        while (t < duration)
        {
            if (CanDodge() && PlayerShotIncoming())
            {
                yield return MetalDodgeHop();
                yield break;
            }

            FacePlayer();
            t += BossDeltaTime * Tempo;
            yield return null;
        }
    }

    // ---------- Metal Man ----------

    /// <summary>Hops alternate low / high; the apex volley throws 2, 1, then 3 hands (3, 2, 3 when enraged).</summary>
    private IEnumerator MetalHopPattern()
    {
        int hops = Mathf.Max(1, metalHopsPerPattern);
        for (int h = 0; h < hops; h++)
        {
            FacePlayer();
            bool high = h % 2 == 1 || PlayerFeetAboveMine() >= antiAirHeight;
            float vy = high ? metalHighHopForce : metalLowHopForce;
            float dx = DxToPlayer();
            float drift = Mathf.Clamp((Mathf.Abs(dx) - metalPreferredDistance) * Mathf.Sign(dx), -3f, 3f);
            LaunchJump(drift / EstimateAirTime(vy), vy);
            yield return WaitUntilAirborne();
            yield return WaitUntilFalling(1.5f);

            yield return ApexThrow(MetalVolleySize(h), metalApexGlintSeconds);
            yield return WaitUntilGrounded(2f);
            motion = Motion.Stop;
            yield return Wait(0.08f);
        }
    }

    private int MetalVolleySize(int hop)
    {
        int[] volleys = IsEnraged ? new[] { 3, 2, 3 } : new[] { 2, 1, 3 };
        return volleys[hop % volleys.Length];
    }

    private bool CanDodge() => dodgePlayerShots && shortHandPrefab != null && Time.time >= dodgeReadyAt && isGrounded;

    /// <summary>A player shot within range that is flying toward him.</summary>
    private bool PlayerShotIncoming()
    {
        Vector2 center = MyBounds().center;
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        dodgeHits.Clear();
        Physics2D.OverlapCircle(center, Mathf.Max(0.5f, dodgeShotRadius), filter, dodgeHits);
        for (int i = 0; i < dodgeHits.Count; i++)
        {
            Collider2D col = dodgeHits[i];
            if (col == null)
                continue;

            Projectile shot = col.GetComponentInParent<Projectile>();
            if (shot == null || !shot.IsLaunched || shot.IsResolvingHit || shot.Owner == null)
                continue;
            if (shot.Owner.GetComponentInParent<PlayerController>() == null)
                continue;

            Vector2 toMe = center - (Vector2)shot.transform.position;
            if (Vector2.Dot(shot.Direction, toMe) > 0f)
                return true;
        }

        return false;
    }

    private IEnumerator MetalDodgeHop()
    {
        dodgeReadyAt = Time.time + Mathf.Max(0f, dodgeCooldown);
        float away = -DirToPlayer();
        LaunchJump(away * 2.5f, metalLowHopForce);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.5f);
        yield return ApexThrow(1, metalApexGlintSeconds * 0.5f);
        yield return WaitUntilGrounded(2f);
        motion = Motion.Stop;
    }

    /// <summary>Stalls at the apex, glints, then throws hands at the player at least Metal Shot Spacing apart.</summary>
    private IEnumerator ApexThrow(int shots, float glintSeconds)
    {
        motion = Motion.Hang;
        SetGravity(false);
        FacePlayer();

        float t = 0f;
        while (t < glintSeconds)
        {
            t += BossDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, glintColor, telegraphColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale);
            yield return null;
        }

        HideAura(telegraphAura);
        float gap = Mathf.Max(0.05f, metalShotSpacing) / Mathf.Max(1f, metalShotSpeed);
        for (int s = 0; s < shots; s++)
        {
            FacePlayer();
            FireAtPlayer(shortHandPrefab, metalShotSpeed, Damage(metalShotDamage), 0f);
            if (s < shots - 1)
                yield return WaitRaw(gap);
        }

        SetGravity(true);
        motion = Motion.Free;
    }

    // ---------- Quick Man ----------

    private IEnumerator QuickRushPattern()
    {
        yield return RushTelegraph(rushWindupSeconds);

        int rushes = IsEnraged ? 2 : 1;
        for (int i = 0; i < rushes; i++)
        {
            float dx = DxToPlayer();
            float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
            float distance = Mathf.Clamp(Mathf.Abs(dx) + rushOvershoot, rushMinDistance, rushMaxDistance);
            yield return Rush(dir, distance, rushSpeed, Damage(rushDamage), null);

            if (i < rushes - 1)
                yield return RushTelegraph(rushTurnPause);
        }

        yield return Wait(0.12f);
    }

    /// <summary>High jump; at the apex he throws a fan of hands that fly out, then curve back to him.</summary>
    private IEnumerator QuickBoomerangPattern()
    {
        FacePlayer();
        float vy = Mathf.Max(1f, boomerangJumpForce);
        float vx = Mathf.Clamp(DxToPlayer() * 0.35f / EstimateAirTime(vy), -6f, 6f);
        LaunchJump(vx, vy);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.6f);

        motion = Motion.Hang;
        SetGravity(false);
        float t = 0f;
        while (t < boomerangGlintSeconds)
        {
            t += BossDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, glintColor, telegraphColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale);
            yield return null;
        }

        HideAura(telegraphAura);
        int count = Mathf.Max(1, boomerangCount);
        float spread = Mathf.Max(0f, boomerangSpreadDegrees);
        for (int i = 0; i < count; i++)
        {
            float offset = count == 1 ? 0f : Mathf.Lerp(-spread, spread, i / (float)(count - 1));
            Projectile shot = FireAtPlayer(shortHandPrefab, boomerangSpeed, Damage(boomerangDamage), offset, boomerangLifetime);
            CountBoomerangHand.Attach(shot, transform, boomerangOutDistance, boomerangTurnDegreesPerSecond);
        }

        yield return WaitRaw(0.08f);
        SetGravity(true);
        motion = Motion.Free;
        yield return WaitUntilGrounded(2f);
        motion = Motion.Stop;
    }

    private IEnumerator RushTelegraph(float seconds)
    {
        motion = Motion.Stop;
        SoundManager.Instance?.PlayClockworkTic();
        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, telegraphColor, Color.white, 0.45f + 0.45f * Mathf.PingPong(t * 8f, 1f), auraBaseScale * 1.1f);
            yield return null;
        }

        HideAura(telegraphAura);
    }

    /// <summary>Straight Hyper Speed run along the ground with contact damage. Records its path when asked.</summary>
    private IEnumerator Rush(float dir, float distance, float speed, int damage, List<Vector2> path)
    {
        if (rb == null)
            yield break;

        SetFacing(dir);
        isDashing = true;
        forceTrail = true;
        contactDamage = damage;
        contactHitDone = false;
        SetGravity(false);
        motion = Motion.Velocity;
        motionVelocity = new Vector2(dir * Mathf.Max(1f, speed), 0f);
        PlayAnimState("Dash");
        SoundManager.Instance?.PlayDash();

        float startX = rb.position.x;
        float lastX = startX;
        float stuck = 0f;
        float t = 0f;
        float timeout = distance / Mathf.Max(1f, speed) + 0.3f;
        while (Mathf.Abs(rb.position.x - startX) < distance && t < timeout)
        {
            path?.Add(rb.position);
            t += BossDeltaTime;
            if (Mathf.Abs(rb.position.x - lastX) < 0.001f)
            {
                stuck += BossDeltaTime;
                if (stuck > 0.1f)
                    break;
            }
            else
            {
                stuck = 0f;
            }

            lastX = rb.position.x;
            yield return null;
        }

        path?.Add(rb.position);
        EndRush();
    }

    private void EndRush()
    {
        isDashing = false;
        forceTrail = false;
        contactDamage = 0;
        SetGravity(true);
        motion = Motion.Stop;
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void TickContactDamage()
    {
        if (contactDamage <= 0 || contactHitDone)
            return;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
            return;

        if (MyBounds().Intersects(PlayerBounds(player)))
        {
            contactHitDone = true;
            player.TakeDamage(contactDamage, transform);
        }
    }

    // ---------- Flash Man ----------

    /// <summary>
    /// Clock telegraph → time stops (red screen, player frozen) → four Long Hands appear 4 spaces around the player's
    /// spot and orbit very slowly while he repositions → time resumes → 0.5 s later the hands fire into the spot.
    /// </summary>
    private IEnumerator TimeStopPattern()
    {
        timeStopReadyAt = Time.time + Mathf.Max(0f, timeStopCooldown);
        yield return ClockWindup(timeStopWindupSeconds, 1);

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead || longHandPrefab == null)
            yield break;

        BeginTimeStop(player);
        orbitCenter = PlayerBounds(player).center;
        orbitRadius = Mathf.Max(0.5f, timeStopRadius);
        orbitDegreesPerSecond = Mathf.Abs(timeStopOrbitDegreesPerSecond) * (Random.value < 0.5f ? 1f : -1f);

        int hands = Mathf.Max(1, timeStopHandCount);
        float frozen = 0f;
        for (int i = 0; i < hands; i++)
        {
            SpawnOrbitHand(45f + 360f * i / hands, 1f);
            PlayClockTick(i);
            float gap = Mathf.Max(0f, timeStopHandSpawnGap);
            frozen += gap;
            yield return WaitRaw(gap);
        }

        // He keeps moving while the world is stopped.
        while (frozen < timeStopFrozenSeconds)
        {
            frozen += BossDeltaTime;
            float dx = DxToPlayer();
            if (isGrounded && Mathf.Abs(dx) > 4f)
            {
                motion = Motion.Walk;
                walkDir = Mathf.Sign(dx);
            }
            else
            {
                motion = Motion.Stop;
                walkDir = 0f;
                FacePlayer();
            }

            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        FacePlayer();
        EndTimeStop();

        yield return WaitRaw(timeStopFireDelay);
        for (int i = 0; i < orbitHands.Count; i++)
            LaunchOrbitHand(i, timeStopHandSpeed, Damage(timeStopHandDamage));
        SoundManager.Instance?.PlayCountFire(ProjectileShotType.Big);
        PlayShootAnim();
        ClearOrbitHands();
        yield return Wait(0.2f);
    }

    /// <summary>
    /// Signature: time stops and twelve Long Hands form a clock around the player (one diameter left open).
    /// After time resumes they fire inward one after another, clockwise from 12.
    /// </summary>
    private IEnumerator MidnightPattern()
    {
        yield return ClockWindup(midnightWindupSeconds, 2);

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead || longHandPrefab == null)
            yield break;

        BeginTimeStop(player);
        orbitCenter = PlayerBounds(player).center;
        orbitRadius = Mathf.Max(1f, midnightRadius);
        orbitDegreesPerSecond = 0f;

        int[] gapChoices = { 3, 3, 2, 4 };
        int gapHour = gapChoices[Random.Range(0, gapChoices.Length)];
        int gapOpposite = (gapHour + 6) % 12;

        float frozen = 0f;
        for (int hour = 0; hour < 12; hour++)
        {
            if (hour == gapHour || hour == gapOpposite)
                continue;

            SpawnOrbitHand(90f - 30f * hour, midnightHandScale);
            PlayClockTick(hour);
            float gap = Mathf.Max(0f, midnightHandSpawnGap);
            frozen += gap;
            yield return WaitRaw(gap);
        }

        while (frozen < midnightFrozenSeconds)
        {
            frozen += BossDeltaTime;
            motion = Motion.Stop;
            FacePlayer();
            yield return null;
        }

        EndTimeStop();
        yield return WaitRaw(midnightFireDelay);

        for (int i = 0; i < orbitHands.Count; i++)
        {
            LaunchOrbitHand(i, midnightHandSpeed, Damage(midnightHandDamage));
            PlayClockTick(i);
            if (i % 3 == 0)
                PlayShootAnim();
            yield return WaitRaw(Mathf.Max(0f, midnightFireGap));
        }

        ClearOrbitHands();
        yield return Wait(0.25f);
    }

    /// <summary>
    /// Signature: three rushes through the player, a red rewind back to where he started, then the same three rushes
    /// mirrored (opposite directions, same lengths).
    /// </summary>
    private IEnumerator RewindRushPattern()
    {
        if (rb == null)
            yield break;

        yield return RushTelegraph(rushWindupSeconds);

        rewindPath.Clear();
        List<float> dashLengths = new List<float>(Mathf.Max(1, rewindRushDashes));
        int dashes = Mathf.Max(1, rewindRushDashes);
        for (int i = 0; i < dashes; i++)
        {
            float dx = DxToPlayer();
            float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
            float distance = Mathf.Clamp(Mathf.Abs(dx) + rushOvershoot, rushMinDistance, rushMaxDistance);
            float x0 = rb.position.x;
            yield return Rush(dir, distance, rewindRushSpeed, Damage(rewindRushDamage), rewindPath);
            dashLengths.Add(rb.position.x - x0);
            yield return RushTelegraph(rewindRushPause);
        }

        // Rewind: replay the path backwards with the red wash on.
        BeginTimeTint();
        SoundManager.Instance?.PlayClockworkToc();
        isDashing = true;
        forceTrail = true;
        SetGravity(false);
        motion = Motion.Hang;
        float playback = Mathf.Max(0.1f, rewindPlaybackSeconds);
        float t = 0f;
        int count = rewindPath.Count;
        while (t < playback && count > 0)
        {
            t += BossDeltaTime;
            int index = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(t / playback)) * (count - 1)), 0, count - 1);
            TeleportTo(rewindPath[index]);
            yield return null;
        }

        if (count > 0)
            TeleportTo(rewindPath[0]);
        EndTimeTint();
        EndRush();
        rewindPath.Clear();

        yield return RushTelegraph(rewindRushPause * 1.5f);
        for (int i = 0; i < dashLengths.Count; i++)
        {
            float length = dashLengths[i];
            if (Mathf.Abs(length) < 0.2f)
                continue;

            yield return Rush(-Mathf.Sign(length), Mathf.Abs(length), rewindRushSpeed, Damage(rewindRushDamage), null);
            if (i < dashLengths.Count - 1)
                yield return RushTelegraph(rewindRushPause);
        }

        yield return Wait(0.15f);
    }

    /// <summary>Clock activation pose, Count's charge aura and clock sounds.</summary>
    private IEnumerator ClockWindup(float seconds, int tickPairs)
    {
        motion = Motion.Stop;
        walkDir = 0f;
        StopHorizontal();
        FacePlayer();
        PlayAnimState("Clock activation");
        if (clockAura != null)
        {
            clockAura.SetFullCharge(false);
            clockAura.SetChargeProgress(0f);
            clockAura.SetVisible(true);
        }

        int ticks = Mathf.Max(1, tickPairs) * 2;
        int played = 0;
        float duration = Mathf.Max(0.05f, seconds);
        float t = 0f;
        while (t < duration)
        {
            float progress = Mathf.Clamp01(t / duration);
            while (played < ticks && progress >= played / (float)ticks)
                PlayClockTick(played++);

            clockAura?.SetChargeProgress(progress);
            ShowAura(telegraphAura, telegraphColor, Color.white, 0.35f + 0.5f * progress, auraBaseScale * (1f + 0.1f * progress));
            t += BossDeltaTime;
            yield return null;
        }

        HideAura(telegraphAura);
        if (clockAura != null)
        {
            clockAura.SetFullCharge(true);
            clockAura.SetVisible(false);
        }
    }

    private static void PlayClockTick(int index)
    {
        if (index % 2 == 0)
            SoundManager.Instance?.PlayClockworkTic();
        else
            SoundManager.Instance?.PlayClockworkToc();
    }

    private void BeginTimeStop(PlayerController player)
    {
        if (player != null && !player.IsDead)
        {
            player.SetTimeFrozen(true);
            frozenPlayer = player;
        }

        BeginTimeTint();
    }

    private void EndTimeStop()
    {
        if (frozenPlayer != null)
            frozenPlayer.SetTimeFrozen(false);
        frozenPlayer = null;
        EndTimeTint();
    }

    private void BeginTimeTint()
    {
        if (timeTint == null)
            return;

        timeTint.Apply(transform, timeRedTint);
        timeTintActive = true;
    }

    private void EndTimeTint()
    {
        if (!timeTintActive)
            return;

        timeTintActive = false;
        if (timeTint != null)
            timeTint.Release();
    }

    private void SpawnOrbitHand(float angleDegrees, float scale)
    {
        if (longHandPrefab == null)
            return;

        Projectile hand = Instantiate(longHandPrefab, orbitCenter, Quaternion.identity);
        if (!Mathf.Approximately(scale, 1f))
            hand.transform.localScale *= Mathf.Max(0.1f, scale);

        Rigidbody2D handBody = hand.GetComponent<Rigidbody2D>();
        if (handBody != null)
            handBody.interpolation = RigidbodyInterpolation2D.None;

        orbitHands.Add(hand);
        orbitHandAngles.Add(angleDegrees);
        PlaceOrbitHand(orbitHands.Count - 1);
    }

    /// <summary>Unlaunched hands circle the frozen spot, always pointing at it.</summary>
    private void TickOrbitHands()
    {
        if (orbitHands.Count == 0 || Mathf.Abs(orbitDegreesPerSecond) < 0.001f)
            return;

        float step = orbitDegreesPerSecond * BossDeltaTime;
        for (int i = 0; i < orbitHands.Count; i++)
        {
            if (orbitHands[i] == null)
                continue;

            orbitHandAngles[i] += step;
            PlaceOrbitHand(i);
        }
    }

    private void PlaceOrbitHand(int index)
    {
        Projectile hand = orbitHands[index];
        if (hand == null)
            return;

        float rad = orbitHandAngles[index] * Mathf.Deg2Rad;
        Vector2 outward = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 pos = orbitCenter + outward * orbitRadius;
        hand.transform.position = new Vector3(pos.x, pos.y, hand.transform.position.z);
        float facing = Mathf.Atan2(-outward.y, -outward.x) * Mathf.Rad2Deg;
        hand.transform.rotation = Quaternion.Euler(0f, 0f, facing);

        Rigidbody2D handBody = hand.GetComponent<Rigidbody2D>();
        if (handBody != null)
            handBody.position = pos;
    }

    private void LaunchOrbitHand(int index, float speed, int damage)
    {
        Projectile hand = orbitHands[index];
        if (hand == null)
            return;

        orbitHands[index] = null;
        Vector2 dir = orbitCenter - (Vector2)hand.transform.position;
        float life = (orbitRadius * 2f + 6f) / Mathf.Max(1f, speed);
        Rigidbody2D handBody = hand.GetComponent<Rigidbody2D>();
        if (handBody != null)
            handBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        hand.Launch(dir, speed, damage, life, transform);
    }

    /// <summary>Destroys hands that never launched (pattern interrupted, boss defeated).</summary>
    private void ClearOrbitHands()
    {
        for (int i = 0; i < orbitHands.Count; i++)
        {
            if (orbitHands[i] != null)
                Destroy(orbitHands[i].gameObject);
        }

        orbitHands.Clear();
        orbitHandAngles.Clear();
    }

    // ---------- Shooting ----------

    private Projectile FireAtPlayer(Projectile prefab, float speed, int damage, float angleOffset, float lifetime = -1f)
    {
        if (prefab == null)
            return null;

        PlayerController player = FindPlayer();
        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)MyBounds().center + new Vector2(facingSign * 0.8f, -0.2f);
        Vector2 dir = player != null
            ? (Vector2)PlayerBounds(player).center - origin
            : new Vector2(facingSign, 0f);
        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector2(facingSign, 0f);
        dir.Normalize();
        if (Mathf.Abs(angleOffset) > 0.01f)
            dir = Quaternion.Euler(0f, 0f, angleOffset) * dir;

        Projectile shot = Instantiate(prefab, origin, Quaternion.identity);
        float life = lifetime > 0f ? lifetime : 30f / Mathf.Max(1f, speed);
        shot.Launch(dir, speed, damage, life, transform);

        GameVisualEffect blast = VisualEffects.ResolveBusterBlastPrefab(shot.ShotType, busterBlastSmallPrefab, busterBlastBigPrefab);
        VisualEffects.SpawnBusterBlast(blast, firePoint != null ? firePoint : transform, shot);
        SoundManager.Instance?.PlayCountFire(shot.ShotType);
        PlayShootAnim();
        return shot;
    }

    private void PlayShootAnim()
    {
        shootPoseUntil = Time.time + 0.25f;
        if (animator == null)
            return;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        string trigger = isGrounded ? "Shoot" : vy < -0.01f ? "FallShot" : "JumpShot";
        if (HasParam(trigger))
            animator.SetTrigger(trigger);
    }

    /// <summary>FirePoint is authored facing left; mirror its X when facing right.</summary>
    private void SyncFirePoint()
    {
        if (firePoint == null || firePoint.parent != transform)
            return;

        Vector3 local = firePointLeftLocal;
        if (facingSign > 0f)
            local.x = -local.x;
        firePoint.localPosition = local;
    }

    // ---------- Building blocks ----------

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime * Tempo;
            yield return null;
        }
    }

    /// <summary>Exact wait, not sped up by Tempo (shot spacing, the 0.5 s fire delay).</summary>
    private IEnumerator WaitRaw(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilAirborne(float timeout = 0.25f)
    {
        float t = 0f;
        while (isGrounded && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilGrounded(float timeout)
    {
        float t = 0f;
        while (!isGrounded && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilFalling(float timeout)
    {
        float t = 0f;
        while (!isGrounded && rb != null && rb.linearVelocity.y > 0f && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private float DxToPlayer()
    {
        PlayerController player = FindPlayer();
        return player != null ? player.transform.position.x - transform.position.x : 0f;
    }

    private float DirToPlayer()
    {
        float dx = DxToPlayer();
        return Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
    }

    private float PlayerFeetAboveMine()
    {
        PlayerController player = FindPlayer();
        return player != null ? PlayerBounds(player).min.y - MyBounds().min.y : 0f;
    }

    private static Bounds PlayerBounds(PlayerController player)
    {
        return player.BodyCollider != null
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);
    }

    private Bounds MyBounds()
    {
        return bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
    }

    private void FacePlayer()
    {
        PlayerController player = FindPlayer();
        if (player != null)
            FaceToward(player.transform);
    }

    private void SetFacing(float dir)
    {
        SetFacingSign(dir);
    }

    private void SetGravity(bool on)
    {
        gravityOff = !on;
        if (rb != null)
            rb.gravityScale = on ? defaultGravityScale : 0f;
    }

    private float EstimateAirTime(float launchVy)
    {
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, defaultGravityScale);
        float up = launchVy / g;
        float peak = launchVy * launchVy / (2f * g);
        float down = Mathf.Sqrt(2f * peak / (g * Mathf.Max(1f, fallMultiplier)));
        return Mathf.Max(0.2f, up + down);
    }

    private void LaunchJump(float vx, float vy)
    {
        if (rb == null)
            return;

        motion = Motion.Free;
        SetGravity(true);
        rb.linearVelocity = new Vector2(BossSpeed(vx), vy);
        isGrounded = false;
        FacePlayer();
        PlayAnimState("Jump");
        SoundManager.Instance?.PlayCountJump();
    }

    private void TeleportTo(Vector2 position)
    {
        if (rb != null)
        {
            rb.position = position;
            rb.linearVelocity = Vector2.zero;
        }

        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    // ---------- Animator ----------

    private void CacheAnimatorParams()
    {
        animatorParams.Clear();
        if (animator == null)
            return;

        foreach (AnimatorControllerParameter param in animator.parameters)
            animatorParams.Add(param.nameHash);
    }

    private bool HasParam(string paramName) => animatorParams.Contains(Animator.StringToHash(paramName));

    private void SetAnimBool(string paramName, bool value)
    {
        if (HasParam(paramName))
            animator.SetBool(paramName, value);
    }

    private void SetAnimFloat(string paramName, float value)
    {
        if (HasParam(paramName))
            animator.SetFloat(paramName, value);
    }

    private void PlayAnimState(string stateName)
    {
        if (animator == null)
            return;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);
    }

    // ---------- Auras ----------

    private Aura CreateAura(string label)
    {
        if (spriteRenderer == null)
            return null;

        Aura aura = new Aura { go = new GameObject($"{name}_{label}") };
        aura.go.transform.SetParent(transform, false);
        aura.go.transform.localPosition = Vector3.zero;
        aura.go.transform.localScale = Vector3.one * auraBaseScale;
        aura.go.transform.SetAsFirstSibling();
        aura.sr = aura.go.AddComponent<SpriteRenderer>();
        aura.sr.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(aura.sr, spriteRenderer, EffectSortingGroup);

        Shader solid = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solid != null)
        {
            aura.material = new Material(solid);
            aura.sr.sharedMaterial = aura.material;
        }

        aura.go.SetActive(false);
        return aura;
    }

    private void ShowAura(Aura aura, Color baseColor, Color flickerColor, float alpha, float scale)
    {
        if (aura == null || aura.go == null || spriteRenderer == null)
            return;

        aura.go.SetActive(true);
        aura.sr.sprite = spriteRenderer.sprite;
        aura.sr.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(aura.sr, spriteRenderer, EffectSortingGroup);

        aura.phase += BossDeltaTime * 1.5f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(aura.phase * Mathf.PI * 2f);
        Color c = Color.Lerp(baseColor, flickerColor, shimmer * 0.3f);
        c.a = Mathf.Clamp01(alpha);
        aura.sr.color = c;
        aura.go.transform.localScale = Vector3.one * scale;
    }

    private static void HideAura(Aura aura)
    {
        if (aura == null || aura.go == null || !aura.go.activeSelf)
            return;

        aura.go.SetActive(false);
        aura.phase = 0f;
    }

    private void DestroyAura(Aura aura)
    {
        if (aura == null)
            return;
        if (aura.go != null)
            Destroy(aura.go);
        if (aura.material != null)
            Destroy(aura.material);
    }

    private void TickEnrageAura()
    {
        if (!showEnrageAura || !IsEnraged || IsDead)
        {
            HideAura(enrageAura);
            return;
        }

        bool omega = OmegaActive;
        float alpha = omega ? Mathf.Min(1f, enrageAuraAlpha + 0.15f) : enrageAuraAlpha;
        ShowAura(enrageAura, enrageAuraColor, enrageAuraFlickerColor, alpha, auraBaseScale * (omega ? 1.13f : 1.08f));
    }

    // ---------- Afterimages ----------

    private void SetupAfterimages()
    {
        DestroyAfterimages();
        if (!enableAfterimages || spriteRenderer == null)
            return;

        int count = Mathf.Max(1, afterimageCount);
        afterimageObjects = new GameObject[count];
        afterimageRenderers = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{name}_Afterimage_{i + 1}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);
            go.SetActive(false);
            afterimageObjects[i] = go;
            afterimageRenderers[i] = sr;
        }
    }

    private void DestroyAfterimages()
    {
        if (afterimageObjects == null)
            return;

        for (int i = 0; i < afterimageObjects.Length; i++)
        {
            if (afterimageObjects[i] != null)
                Destroy(afterimageObjects[i]);
        }

        afterimageObjects = null;
        afterimageRenderers = null;
    }

    private float AfterimageDelay(int index) => Mathf.Max(0.01f, afterimageSpacing) * (index + 1);

    private float MaxAfterimageDelay => AfterimageDelay(Mathf.Max(1, afterimageCount) - 1);

    private void RecordPoseHistory()
    {
        if (!enableAfterimages || spriteRenderer == null)
            return;

        poseHistory.Add(new PoseSample
        {
            time = Time.time,
            position = transform.position,
            sprite = spriteRenderer.sprite,
            flipX = spriteRenderer.flipX
        });

        float cutoff = Time.time - (MaxAfterimageDelay + 0.35f);
        while (poseHistory.Count > 0 && poseHistory[0].time < cutoff)
            poseHistory.RemoveAt(0);
    }

    private bool TrySamplePose(float delay, out PoseSample sample)
    {
        sample = default;
        if (poseHistory.Count == 0)
            return false;

        float targetTime = Time.time - delay;
        for (int i = poseHistory.Count - 1; i >= 0; i--)
        {
            if (poseHistory[i].time <= targetTime)
            {
                sample = poseHistory[i];
                return true;
            }
        }

        sample = poseHistory[0];
        return true;
    }

    private void UpdateAfterimages()
    {
        if (afterimageRenderers == null || spriteRenderer == null)
            return;

        bool moving = isDashing || forceTrail;
        if (moving)
            afterimagesVisibleUntil = Mathf.Max(afterimagesVisibleUntil,
                Time.time + MaxAfterimageDelay + afterimageLifetimePadding);

        bool active = moving || Time.time <= afterimagesVisibleUntil;
        for (int i = 0; i < afterimageRenderers.Length; i++)
        {
            GameObject go = afterimageObjects[i];
            if (go == null)
                continue;

            if (!active || !TrySamplePose(AfterimageDelay(i), out PoseSample sample))
            {
                if (go.activeSelf)
                    go.SetActive(false);
                continue;
            }

            go.SetActive(true);
            go.transform.position = sample.position;
            SpriteRenderer sr = afterimageRenderers[i];
            sr.sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            sr.flipX = sample.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);

            float t = afterimageRenderers.Length == 1 ? 0f : i / (float)(afterimageRenderers.Length - 1);
            Color c = afterimageColor;
            c.a = Mathf.Lerp(afterimageAlphaStart, afterimageAlphaEnd, t);
            sr.color = c;
        }
    }

    private void HideAfterimages()
    {
        if (afterimageObjects == null)
            return;

        for (int i = 0; i < afterimageObjects.Length; i++)
        {
            if (afterimageObjects[i] != null)
                afterimageObjects[i].SetActive(false);
        }
    }

    // ---------- Reset ----------

    private void CleanupAfterPattern()
    {
        EndTimeStop();
        ClearOrbitHands();
        HideAura(telegraphAura);
        if (clockAura != null)
            clockAura.SetVisible(false);
        isDashing = false;
        forceTrail = false;
        contactDamage = 0;
        SetGravity(true);
        if (motion != Motion.Free)
            motion = Motion.Stop;
        walkDir = 0f;
    }

    private void ResetActions()
    {
        actionStack.Clear();
        CleanupAfterPattern();
        rewindPath.Clear();
        motion = Motion.Stop;
        signatureActive = false;
    }

    protected override void OnCombatPaused()
    {
        ResetActions();
        base.OnCombatPaused();
    }

    protected override void OnRevived()
    {
        ResetActions();
        hitVfxTimer = 0f;
        System.Array.Clear(zoneRotationIndex, 0, zoneRotationIndex.Length);
        timeStopReadyAt = 0f;
        dodgeReadyAt = 0f;
        signatureStarted = false;
        signaturePatternCounter = 0;
        signatureInterruptPending = false;
        StopSignatureFlicker();
    }

    protected override void OnBossDefeated()
    {
        ResetActions();
        StopSignatureFlicker();
        HideAura(enrageAura);
        HideAfterimages();
        hitVfxTimer = 0f;
        VisualEffects.StopStunned(this);
        if (animator != null)
            animator.speed = 1f;

        enabled = false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        copyBotMaxHealth = Mathf.Max(0, copyBotMaxHealth);
        damageMultiplier = Mathf.Max(0.1f, damageMultiplier);
        lifeBarCount = Mathf.Max(1, lifeBarCount);
        tempoPerBarLost = Mathf.Max(0f, tempoPerBarLost);
        moveSpeedPerBarLost = Mathf.Max(0f, moveSpeedPerBarLost);
        recoverDurationFullHp = Mathf.Max(0.05f, recoverDurationFullHp);
        recoverDurationLowHp = Mathf.Max(0.05f, recoverDurationLowHp);
        closeZoneDistance = Mathf.Max(0.5f, closeZoneDistance);
        midZoneDistance = Mathf.Max(closeZoneDistance, midZoneDistance);
        antiAirHeight = Mathf.Max(0.1f, antiAirHeight);
        antiAirMaxDistance = Mathf.Max(0f, antiAirMaxDistance);
        metalHopsPerPattern = Mathf.Max(1, metalHopsPerPattern);
        metalLowHopForce = Mathf.Max(1f, metalLowHopForce);
        metalHighHopForce = Mathf.Max(metalLowHopForce, metalHighHopForce);
        metalPreferredDistance = Mathf.Max(0f, metalPreferredDistance);
        metalApexGlintSeconds = Mathf.Max(0f, metalApexGlintSeconds);
        metalShotSpacing = Mathf.Max(2f, metalShotSpacing);
        metalShotSpeed = Mathf.Max(1f, metalShotSpeed);
        metalShotDamage = Mathf.Max(0, metalShotDamage);
        dodgeShotRadius = Mathf.Max(0.5f, dodgeShotRadius);
        dodgeCooldown = Mathf.Max(0f, dodgeCooldown);
        rushWindupSeconds = Mathf.Max(0.05f, rushWindupSeconds);
        rushSpeed = Mathf.Max(1f, rushSpeed);
        rushOvershoot = Mathf.Max(0f, rushOvershoot);
        rushMinDistance = Mathf.Max(0.5f, rushMinDistance);
        rushMaxDistance = Mathf.Max(rushMinDistance, rushMaxDistance);
        rushTurnPause = Mathf.Max(0f, rushTurnPause);
        rushDamage = Mathf.Max(0, rushDamage);
        boomerangJumpForce = Mathf.Max(1f, boomerangJumpForce);
        boomerangCount = Mathf.Max(1, boomerangCount);
        boomerangSpreadDegrees = Mathf.Max(0f, boomerangSpreadDegrees);
        boomerangSpeed = Mathf.Max(1f, boomerangSpeed);
        boomerangOutDistance = Mathf.Max(0.5f, boomerangOutDistance);
        boomerangTurnDegreesPerSecond = Mathf.Max(30f, boomerangTurnDegreesPerSecond);
        boomerangLifetime = Mathf.Max(0.5f, boomerangLifetime);
        boomerangGlintSeconds = Mathf.Max(0f, boomerangGlintSeconds);
        boomerangDamage = Mathf.Max(0, boomerangDamage);
        timeStopWindupSeconds = Mathf.Max(0.05f, timeStopWindupSeconds);
        timeStopCooldown = Mathf.Max(0f, timeStopCooldown);
        timeStopHandCount = Mathf.Max(1, timeStopHandCount);
        timeStopRadius = Mathf.Max(0.5f, timeStopRadius);
        timeStopHandSpawnGap = Mathf.Max(0f, timeStopHandSpawnGap);
        timeStopFrozenSeconds = Mathf.Max(0.1f, timeStopFrozenSeconds);
        timeStopFireDelay = Mathf.Max(0f, timeStopFireDelay);
        timeStopHandSpeed = Mathf.Max(1f, timeStopHandSpeed);
        timeStopHandDamage = Mathf.Max(0, timeStopHandDamage);
        enrageTempoBonus = Mathf.Max(0f, enrageTempoBonus);
        enrageMoveSpeedBonus = Mathf.Max(0f, enrageMoveSpeedBonus);
        enrageBonusDamage = Mathf.Max(0, enrageBonusDamage);
        omegaTempoBonus = Mathf.Max(0f, omegaTempoBonus);
        signatureEvery = Mathf.Max(1, signatureEvery);
        signatureHitInvincibility = Mathf.Max(0f, signatureHitInvincibility);
        signatureFlickerRate = Mathf.Max(1f, signatureFlickerRate);
        midnightWindupSeconds = Mathf.Max(0.05f, midnightWindupSeconds);
        midnightRadius = Mathf.Max(1f, midnightRadius);
        midnightHandScale = Mathf.Max(0.1f, midnightHandScale);
        midnightHandSpawnGap = Mathf.Max(0f, midnightHandSpawnGap);
        midnightFrozenSeconds = Mathf.Max(0.1f, midnightFrozenSeconds);
        midnightFireDelay = Mathf.Max(0f, midnightFireDelay);
        midnightFireGap = Mathf.Max(0f, midnightFireGap);
        midnightHandSpeed = Mathf.Max(1f, midnightHandSpeed);
        midnightHandDamage = Mathf.Max(0, midnightHandDamage);
        rewindRushDashes = Mathf.Max(1, rewindRushDashes);
        rewindRushSpeed = Mathf.Max(1f, rewindRushSpeed);
        rewindRushPause = Mathf.Max(0f, rewindRushPause);
        rewindPlaybackSeconds = Mathf.Max(0.1f, rewindPlaybackSeconds);
        rewindRushDamage = Mathf.Max(0, rewindRushDamage);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        afterimageCount = Mathf.Max(1, afterimageCount);
        afterimageSpacing = Mathf.Max(0.01f, afterimageSpacing);
        afterimageLifetimePadding = Mathf.Max(0f, afterimageLifetimePadding);
    }
#endif
}
