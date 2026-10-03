using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>Layout, damage and Grandmother Silk timings shared by the item, playable Harlie and Boss Harlie.</summary>
[Serializable]
public class SilverSwordSettings
{
    [Tooltip("Sword prefab (SilverSword component).")]
    public SilverSword swordPrefab;
    [Tooltip("Silver slash wave sent left and right when a sword lands (base attack).")]
    public MaliceSlashProjectile slashPrefab;

    [Header("Damage")]
    public int swordDamage = 3;
    public int slashDamage = 1;

    [Header("Formation (half on each side)")]
    public int swordsPerSide = 4;
    [Tooltip("Spaces from the owner to the closest sword on each side.")]
    public float firstSwordSpaces = 2f;
    [Tooltip("Spaces between swords on the same side.")]
    public float swordSpacing = 2f;
    [Tooltip("Spaces above the top of the owner's head.")]
    public float heightAboveHead = 2f;
    public float followSharpness = 10f;
    [Tooltip("Swords fly back into place at least this fast (world units per second).")]
    public float returnSpeed = 20f;
    public float summonFadeSeconds = 0.35f;

    [Header("Base Attack: float up, shoot down, ground slashes")]
    public float riseSpaces = 1f;
    public float riseSeconds = 0.7f;
    public float plungeSpeed = 28f;
    [Tooltip("A plunge gives up after this many spaces if there's no ground below.")]
    public float plungeMaxSpaces = 30f;
    public float groundStickSeconds = 0.3f;
    public float slashSpaces = 8f;
    [Tooltip("Silver slash travel speed (world units per second).")]
    public float slashSpeed = 9f;
    [Tooltip("Silver slash height vs. a sword's length (1 = as tall as a sword).")]
    public float slashSizeVsSword = 1f;

    [Header("Grandmother Silk Needles (ring, rain, walls back to back)")]
    [Tooltip("Higher = every needle wait is shorter (1 = Boss Harlie's pace).")]
    public float needleTempo = 1f;
    public float needleMoveSeconds = 0.45f;
    public float needleSpeed = 26f;
    public float needlePauseBetween = 0.35f;
    [Tooltip("Ring: spaces from the target to each needle (they surround the target from the sides and above).")]
    public float ringRadius = 5.5f;
    public float ringAimSeconds = 0.55f;
    public float ringFireGap = 0.2f;
    [Tooltip("Rain: needles line the top of the screen and drop in a sweep, one column at a time.")]
    public float rainAimSeconds = 0.5f;
    public float rainFireGap = 0.12f;
    [Tooltip("Walls: needles fire across the screen from the left / right edge, low (jump) or high (stay down).")]
    public float wallAimSeconds = 0.4f;
    public float wallFireGap = 0.12f;

    public void Validate()
    {
        swordDamage = Mathf.Max(0, swordDamage);
        slashDamage = Mathf.Max(0, slashDamage);
        swordsPerSide = Mathf.Clamp(swordsPerSide, 1, 8);
        firstSwordSpaces = Mathf.Max(0f, firstSwordSpaces);
        swordSpacing = Mathf.Max(0.1f, swordSpacing);
        followSharpness = Mathf.Max(0.1f, followSharpness);
        returnSpeed = Mathf.Max(1f, returnSpeed);
        summonFadeSeconds = Mathf.Max(0.01f, summonFadeSeconds);
        riseSpaces = Mathf.Max(0f, riseSpaces);
        riseSeconds = Mathf.Max(0.05f, riseSeconds);
        plungeSpeed = Mathf.Max(1f, plungeSpeed);
        plungeMaxSpaces = Mathf.Max(1f, plungeMaxSpaces);
        groundStickSeconds = Mathf.Max(0f, groundStickSeconds);
        slashSpaces = Mathf.Max(0f, slashSpaces);
        slashSpeed = Mathf.Max(0.1f, slashSpeed);
        slashSizeVsSword = Mathf.Max(0.1f, slashSizeVsSword);
        needleTempo = Mathf.Max(0.1f, needleTempo);
        needleMoveSeconds = Mathf.Max(0.05f, needleMoveSeconds);
        needleSpeed = Mathf.Max(1f, needleSpeed);
        needlePauseBetween = Mathf.Max(0f, needlePauseBetween);
        ringRadius = Mathf.Max(1f, ringRadius);
        ringAimSeconds = Mathf.Max(0f, ringAimSeconds);
        ringFireGap = Mathf.Max(0f, ringFireGap);
        rainAimSeconds = Mathf.Max(0f, rainAimSeconds);
        rainFireGap = Mathf.Max(0f, rainFireGap);
        wallAimSeconds = Mathf.Max(0f, wallAimSeconds);
        wallFireGap = Mathf.Max(0f, wallFireGap);
    }
}

/// <summary>Boss Harlie's extra sword patterns (Lace spiral, Moorwing blades) and cooldowns.</summary>
[Serializable]
public class SilverSwordBossSettings
{
    [Header("Cooldowns (seconds after the pattern ends)")]
    public float grandmotherSilkCooldown = 5f;
    public float laceSpiralCooldown = 5f;
    public float moorwingCooldown = 4f;

    [Header("Spinning")]
    public float bladeSpinDegreesPerSecond = 900f;

    [Header("Lace Spiral (gather, then an expanding pinwheel)")]
    public float spiralGatherSeconds = 0.6f;
    public float spiralWindupSeconds = 0.45f;
    public float spiralStartRadius = 0.9f;
    [Tooltip("World units per second the pinwheel grows.")]
    public float spiralExpandSpeed = 5f;
    public float spiralMaxRadius = 14f;
    public float spiralDegreesPerSecond = 60f;

    [Header("Moorwing - Boomerangs (out and back in wide loops)")]
    public float boomerangLoopSeconds = 1.6f;
    [Tooltip("Second sword of each pair follows this long after the first.")]
    public float boomerangPairGap = 0.35f;
    public float boomerangThrowGap = 1.1f;
    [Tooltip("Loops reach this many spaces past the player.")]
    public float boomerangExtraSpaces = 4f;
    public float boomerangMaxSpaces = 14f;

    [Header("Moorwing - Ring (circle Harlie, then fly outward)")]
    public float ringGrowSeconds = 1f;
    public float ringStartRadius = 1.2f;
    public float ringEndRadius = 3.2f;
    public float ringDegreesPerSecond = 240f;
    [Tooltip("The ring stops turning this long before the swords fly out.")]
    public float ringLockSeconds = 0.4f;
    public float ringFlySpeed = 16f;
    public float ringFlySpaces = 14f;

    [Header("Moorwing - Sweeps (big arcs across the screen)")]
    public int sweepCount = 5;
    public float sweepAimSeconds = 0.4f;
    public float sweepSeconds = 1.1f;
    public float sweepGap = 1f;
    [Tooltip("Arcs start this many spaces above the floor.")]
    public float sweepTopSpaces = 6f;

    public void Validate()
    {
        grandmotherSilkCooldown = Mathf.Max(0f, grandmotherSilkCooldown);
        laceSpiralCooldown = Mathf.Max(0f, laceSpiralCooldown);
        moorwingCooldown = Mathf.Max(0f, moorwingCooldown);
        spiralGatherSeconds = Mathf.Max(0.05f, spiralGatherSeconds);
        spiralWindupSeconds = Mathf.Max(0f, spiralWindupSeconds);
        spiralStartRadius = Mathf.Max(0.2f, spiralStartRadius);
        spiralExpandSpeed = Mathf.Max(0.5f, spiralExpandSpeed);
        spiralMaxRadius = Mathf.Max(spiralStartRadius + 1f, spiralMaxRadius);
        boomerangLoopSeconds = Mathf.Max(0.3f, boomerangLoopSeconds);
        boomerangPairGap = Mathf.Max(0f, boomerangPairGap);
        boomerangThrowGap = Mathf.Max(0f, boomerangThrowGap);
        boomerangExtraSpaces = Mathf.Max(0f, boomerangExtraSpaces);
        boomerangMaxSpaces = Mathf.Max(5f, boomerangMaxSpaces);
        ringGrowSeconds = Mathf.Max(0.1f, ringGrowSeconds);
        ringStartRadius = Mathf.Max(0.2f, ringStartRadius);
        ringEndRadius = Mathf.Max(ringStartRadius, ringEndRadius);
        ringLockSeconds = Mathf.Max(0f, ringLockSeconds);
        ringFlySpeed = Mathf.Max(1f, ringFlySpeed);
        ringFlySpaces = Mathf.Max(1f, ringFlySpaces);
        sweepCount = Mathf.Clamp(sweepCount, 1, 16);
        sweepAimSeconds = Mathf.Max(0f, sweepAimSeconds);
        sweepSeconds = Mathf.Max(0.2f, sweepSeconds);
        sweepGap = Mathf.Max(0.1f, sweepGap);
        sweepTopSpaces = Mathf.Max(1f, sweepTopSpaces);
    }
}

/// <summary>
/// Summonable Silver Swords: half float to the owner's left and half to the right, above their head, and
/// follow them. Attacks take every sword; they fly back into place afterward.
/// Base attack: float up, shoot straight down through everything, and each landing sends a silver slash
/// left and right. Grandmother Silk (Hollow Knight Silksong): a needle ring that fires into the target,
/// a needle rain sweeping across the screen, then needles firing across from the walls.
/// Boss Harlie also uses Lace's expanding spiral and Moorwing's spinning blades (boomerangs, ring or sweeps).
/// The item version spawns once, does the base attack and fades out.
/// </summary>
public class SilverSwordSquad : MonoBehaviour
{
    private enum BossPattern
    {
        BaseAttack = 0,
        GrandmotherSilk = 1,
        LaceSpiral = 2,
        MoorwingBlades = 3
    }

    private const int BossPatternCount = 4;
    private const float HomeTolerance = 1.6f;
    private const float AimLineThickness = 0.07f;
    private const float DismissSeconds = 0.3f;

    private static readonly List<RaycastHit2D> RayHits = new List<RaycastHit2D>(8);
    private static readonly List<Collider2D> EnemyScratch = new List<Collider2D>(32);
    private static Sprite lineSprite;

    private sealed class Slot
    {
        public SilverSword sword;
        public Vector2 offset;
        public bool busy;
        /// <summary>Back with the owner since the last attack (it may trail a little while they move).</summary>
        public bool home = true;
    }

    private readonly List<Slot> slots = new List<Slot>(8);
    private readonly List<SpriteRenderer> aimLines = new List<SpriteRenderer>(8);
    private readonly float[] bossReadyAt = new float[BossPatternCount];

    private Transform owner;
    private PlayerController ownerPlayer;
    private Boss ownerBoss;
    private Collider2D ownerBody;
    private SilverSwordSettings settings;
    private SilverSwordBossSettings bossSettings;
    private LayerMask groundMask;
    private bool followOwner = true;
    private Vector2 fixedAnchor;
    private Color aimColor;

    private Coroutine attackRoutine;
    private int pendingFlights;
    private int bossRotation;
    private float summonTimer;
    private bool dismissing;
    private float dismissTimer;
    private int lastClashFrame = -1;

    public bool IsAttacking => attackRoutine != null;
    public bool IsReady => !IsAttacking && !dismissing && summonTimer >= SummonSeconds && AllInFormation();

    private bool OwnerIsPlayer => ownerPlayer != null;
    private float SummonSeconds => Mathf.Max(0.01f, settings.summonFadeSeconds);
    private float Dt => Time.deltaTime * TimeScaleFor(OwnerIsPlayer);
    private Vector2 Anchor => followOwner ? OwnerHead() : fixedAnchor;

    public static float TimeScaleFor(bool ownerIsPlayer)
    {
        if (!HyperSpeedWorldSlow.IsActive)
            return 1f;
        if (ownerIsPlayer && !HyperSpeedWorldSlow.SlowsPlayer)
            return 1f;
        return HyperSpeedWorldSlow.WorldTimeScale;
    }

    // ---------- Creation ----------

    /// <summary>Summons a squad that follows <paramref name="summoner"/> until dismissed.</summary>
    public static SilverSwordSquad Create(Transform summoner, SilverSwordSettings swordSettings, LayerMask ground,
        SilverSwordBossSettings bossPatternSettings = null)
    {
        if (summoner == null || swordSettings == null || swordSettings.swordPrefab == null)
            return null;

        SilverSwordSquad squad = new GameObject($"{summoner.name} Silver Swords").AddComponent<SilverSwordSquad>();
        squad.Setup(summoner, swordSettings, ground, bossPatternSettings);
        return squad;
    }

    /// <summary>Item: swords appear around the player, do the base attack once, then fade out.</summary>
    public static void SpawnItemVolley(PlayerController player, SilverSwordSettings swordSettings)
    {
        if (player == null)
            return;

        SilverSwordSquad squad = Create(player.transform, swordSettings, player.GroundLayers);
        if (squad == null)
            return;

        squad.followOwner = false;
        squad.attackRoutine = squad.StartCoroutine(squad.ItemVolley());
    }

    private void Setup(Transform summoner, SilverSwordSettings swordSettings, LayerMask ground,
        SilverSwordBossSettings bossPatternSettings)
    {
        owner = summoner;
        settings = swordSettings;
        bossSettings = bossPatternSettings;
        groundMask = ground;
        ownerPlayer = summoner.GetComponentInParent<PlayerController>();
        ownerBoss = ownerPlayer == null ? summoner.GetComponentInParent<Boss>() : null;
        ownerBody = ownerPlayer != null ? ownerPlayer.BodyCollider : summoner.GetComponent<Collider2D>();
        aimColor = OwnerIsPlayer ? new Color(0.85f, 0.92f, 1f, 0.45f) : new Color(1f, 0.3f, 0.3f, 0.5f);
        fixedAnchor = OwnerHead();
        SpawnSwords();
    }

    private void SpawnSwords()
    {
        SortingGroup group = ownerPlayer != null ? ownerPlayer.EffectSortingGroup : ownerBoss != null ? ownerBoss.EffectSortingGroup : null;
        SpriteRenderer host = ownerPlayer != null ? ownerPlayer.BodySpriteRenderer : owner.GetComponent<SpriteRenderer>();
        int perSide = Mathf.Max(1, settings.swordsPerSide);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int k = 0; k < perSide; k++)
            {
                Vector2 offset = new Vector2(
                    side * (settings.firstSwordSpaces + settings.swordSpacing * k),
                    settings.heightAboveHead);
                Vector2 pos = Anchor + offset;
                SilverSword sword = Instantiate(settings.swordPrefab, new Vector3(pos.x, pos.y, 0f), Quaternion.identity);
                sword.name = settings.swordPrefab.name;
                sword.Init(owner, settings.swordDamage, group, host);
                sword.PointAt(Vector2.down);
                sword.SetAlpha(0f);
                slots.Add(new Slot { sword = sword, offset = offset });
            }
        }

        summonTimer = 0f;
        SoundManager.Instance?.PlayHarlieSwordSwing();
    }

    // ---------- Lifecycle ----------

    private void Update()
    {
        if (!dismissing && !OwnerAlive())
            Dismiss();

        if (dismissing)
        {
            TickDismiss();
            return;
        }

        if (summonTimer < SummonSeconds)
        {
            summonTimer += Time.deltaTime;
            float alpha = Mathf.Clamp01(summonTimer / SummonSeconds);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].sword != null)
                    slots[i].sword.SetAlpha(alpha);
            }
        }

        float dt = Dt;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            if (!slot.busy && slot.sword != null)
                FollowSlot(slot, dt);
        }
    }

    private bool OwnerAlive()
    {
        if (owner == null || !owner.gameObject.activeInHierarchy)
            return false;
        if (ownerPlayer != null && ownerPlayer.IsDead)
            return false;
        return ownerBoss == null || !ownerBoss.IsDead;
    }

    private Vector2 SlotTarget(Slot slot)
    {
        float bob = Mathf.Sin(Time.time * 2f + slot.offset.x) * 0.08f;
        return Anchor + slot.offset + new Vector2(0f, bob);
    }

    private void FollowSlot(Slot slot, float dt)
    {
        Vector2 target = SlotTarget(slot);
        Vector2 pos = slot.sword.Position;
        float dist = Vector2.Distance(pos, target);
        float step = dist * (1f - Mathf.Exp(-settings.followSharpness * dt));
        if (dist > 1.5f)
            step = Mathf.Max(step, settings.returnSpeed * dt);

        pos = Vector2.MoveTowards(pos, target, Mathf.Min(step, dist));
        slot.sword.transform.position = new Vector3(pos.x, pos.y, 0f);
        slot.sword.TurnToward(Vector2.down, 720f * dt);
        if (!slot.home && Vector2.Distance(pos, target) <= HomeTolerance)
            slot.home = true;
    }

    private bool AllInFormation()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            if (slot.sword == null)
                continue;
            if (slot.busy || !slot.home)
                return false;
        }

        return true;
    }

    /// <summary>Stops any attack; the swords fly back into place.</summary>
    public void CancelAttack()
    {
        if (dismissing)
            return;

        StopAllCoroutines();
        attackRoutine = null;
        pendingFlights = 0;
        ClearAimLines();
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].busy = false;
            if (slots[i].sword != null)
            {
                slots[i].sword.Disarm();
                slots[i].sword.SetSpin(0f);
            }
        }
    }

    /// <summary>Fades the swords out and removes the squad.</summary>
    public void Dismiss()
    {
        if (dismissing)
            return;

        StopAllCoroutines();
        attackRoutine = null;
        pendingFlights = 0;
        ClearAimLines();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].sword != null)
                slots[i].sword.Disarm();
        }

        dismissing = true;
        dismissTimer = 0f;
    }

    /// <summary>Removes the swords this frame with no fade (e.g. the owner boarded the rocket ship).</summary>
    public void DespawnNow()
    {
        StopAllCoroutines();
        attackRoutine = null;
        dismissing = true;
        Destroy(gameObject);
    }

    private void TickDismiss()
    {
        dismissTimer += Time.deltaTime;
        float alpha = 1f - Mathf.Clamp01(dismissTimer / DismissSeconds);
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].sword != null)
                slots[i].sword.SetAlpha(alpha);
        }

        if (dismissTimer >= DismissSeconds)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].sword != null)
                Destroy(slots[i].sword.gameObject);
        }

        slots.Clear();
        ClearAimLines();
    }

    // ---------- Attack entry points ----------

    public bool TryBaseAttack()
    {
        if (!IsReady)
            return false;

        attackRoutine = StartCoroutine(RunAttack(BaseAttack(), -1));
        return true;
    }

    public bool TryGrandmotherSilk(Func<Bounds> target)
    {
        if (!IsReady || target == null)
            return false;

        attackRoutine = StartCoroutine(RunAttack(GrandmotherSilk(target), -1));
        return true;
    }

    /// <summary>Boss: the next ready pattern in a fixed order (base, Grandmother Silk, Lace, Moorwing).</summary>
    public bool TryStartBossPattern(Func<Bounds> target)
    {
        if (!IsReady || target == null || bossSettings == null)
            return false;

        for (int i = 0; i < BossPatternCount; i++)
        {
            int p = (bossRotation + i) % BossPatternCount;
            if (Time.time < bossReadyAt[p])
                continue;

            bossRotation = p + 1;
            attackRoutine = StartCoroutine(RunAttack(BossPatternRoutine((BossPattern)p, target), p));
            return true;
        }

        return false;
    }

    private IEnumerator BossPatternRoutine(BossPattern pattern, Func<Bounds> target)
    {
        switch (pattern)
        {
            case BossPattern.GrandmotherSilk: return GrandmotherSilk(target);
            case BossPattern.LaceSpiral: return LaceSpiral();
            case BossPattern.MoorwingBlades: return MoorwingBlades(target);
            default: return BaseAttack();
        }
    }

    private float BossCooldown(int pattern)
    {
        switch ((BossPattern)pattern)
        {
            case BossPattern.GrandmotherSilk: return bossSettings.grandmotherSilkCooldown;
            case BossPattern.LaceSpiral: return bossSettings.laceSpiralCooldown;
            case BossPattern.MoorwingBlades: return bossSettings.moorwingCooldown;
            default: return 0f;
        }
    }

    private IEnumerator RunAttack(IEnumerator body, int bossPattern)
    {
        SetAllBusy(true);
        yield return body;
        yield return WaitForFlights();
        ReleaseAll();
        if (bossPattern >= 0)
            bossReadyAt[bossPattern] = Time.time + Mathf.Max(0f, BossCooldown(bossPattern));
        attackRoutine = null;
    }

    private IEnumerator ItemVolley()
    {
        SetAllBusy(true);
        while (summonTimer < SummonSeconds)
            yield return null;

        yield return BaseAttack();
        yield return WaitForFlights();
        attackRoutine = null;
        Dismiss();
    }

    private void SetAllBusy(bool busy)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].busy = busy;
            if (busy)
                slots[i].home = false;
        }
    }

    private void ReleaseAll()
    {
        ClearAimLines();
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].busy = false;
            if (slots[i].sword != null)
            {
                slots[i].sword.Disarm();
                slots[i].sword.SetSpin(0f);
            }
        }
    }

    // ---------- Base attack ----------

    private IEnumerator BaseAttack()
    {
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        Vector2[] starts = new Vector2[n];
        for (int i = 0; i < n; i++)
            starts[i] = swords[i].Position;

        float t = 0f;
        float duration = Mathf.Max(0.05f, settings.riseSeconds);
        while (true)
        {
            t += Dt;
            float u = Mathf.Clamp01(t / duration);
            float eased = 1f - (1f - u) * (1f - u);
            for (int i = 0; i < n; i++)
            {
                if (swords[i] == null)
                    continue;
                Vector2 pos = starts[i] + Vector2.up * (settings.riseSpaces * eased);
                swords[i].transform.position = new Vector3(pos.x, pos.y, 0f);
                swords[i].PointAt(Vector2.down);
            }

            if (u >= 1f)
                break;
            yield return null;
        }

        for (int i = 0; i < n; i++)
        {
            if (swords[i] != null)
                Launch(PlungeAndSlash(swords[i]));
        }

        SoundManager.Instance?.PlayDash();
        yield return WaitForFlights();
    }

    private IEnumerator PlungeAndSlash(SilverSword sword)
    {
        float embed = sword.TipLength * 0.2f;
        bool ground = CastGround(sword.TipPosition, Vector2.down, settings.plungeMaxSpaces, out float hit);
        float travel = ground ? hit + embed : settings.plungeMaxSpaces;

        sword.PointAt(Vector2.down);
        sword.Arm();
        yield return FlyStraight(sword, Vector2.down, settings.plungeSpeed, travel);
        if (sword == null)
            yield break;

        sword.Disarm();
        if (ground)
        {
            Vector2 tip = sword.TipPosition;
            SpawnGroundSlashes(new Vector2(tip.x, tip.y + embed), sword);
            if (lastClashFrame != Time.frameCount)
            {
                lastClashFrame = Time.frameCount;
                SoundManager.Instance?.PlayHarlieBigSwordClash();
            }
        }

        yield return Wait(settings.groundStickSeconds);
    }

    /// <summary>Two silver waves (left and right) standing on the ground, each as tall as the sword.</summary>
    private void SpawnGroundSlashes(Vector2 groundPoint, SilverSword sword)
    {
        if (settings.slashPrefab == null)
            return;

        float distance = Mathf.Max(0f, settings.slashSpaces);
        float seconds = distance / Mathf.Max(0.1f, settings.slashSpeed);
        float targetHeight = sword.TipLength * 2f * Mathf.Max(0.1f, settings.slashSizeVsSword);
        for (int side = -1; side <= 1; side += 2)
        {
            MaliceSlashProjectile wave = Instantiate(settings.slashPrefab, new Vector3(groundPoint.x, groundPoint.y, 0f), Quaternion.identity);
            SpriteRenderer sr = wave.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.bounds.size.y > 0.01f)
            {
                float factor = targetHeight / sr.bounds.size.y;
                float bottomOffset = sr.bounds.min.y - wave.transform.position.y;
                wave.transform.localScale *= factor;
                wave.transform.position = new Vector3(groundPoint.x, groundPoint.y - bottomOffset * factor, 0f);
            }

            wave.Launch(side, distance, seconds, settings.slashDamage, owner, null, sword.Renderer, 0, 0f,
                OwnerIsPlayer ? 0.5f : 0f, 0.12f);
        }
    }

    // ---------- Grandmother Silk needles ----------

    private float NeedleWait(float seconds) => seconds / Mathf.Max(0.1f, settings.needleTempo);

    private IEnumerator GrandmotherSilk(Func<Bounds> target)
    {
        yield return NeedleRing(target);
        yield return WaitForFlights();
        yield return Wait(NeedleWait(settings.needlePauseBetween));
        yield return NeedleRain(target);
        yield return WaitForFlights();
        yield return Wait(NeedleWait(settings.needlePauseBetween));
        yield return NeedleWalls(target);
    }

    /// <summary>Needles surround the target (sides and above), aim through it, then fire in from both ends.</summary>
    private IEnumerator NeedleRing(Func<Bounds> target)
    {
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        Vector2 center = target().center;
        float radius = settings.ringRadius;
        List<Vector2> spots = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            float degrees = n == 1 ? 90f : Mathf.Lerp(165f, 15f, i / (float)(n - 1));
            spots.Add(center + Polar(degrees, radius));
        }

        yield return MoveSwords(swords, spots, NeedleWait(settings.needleMoveSeconds), (i, p) => center - p, 0f);

        // The aim locks onto wherever the target is when the lines appear.
        Vector2 aimPoint = target().center;
        float maxTravel = radius * 2f + 4f;
        Vector2[] dirs = new Vector2[n];
        SpriteRenderer[] lines = new SpriteRenderer[n];
        for (int i = 0; i < n; i++)
        {
            if (swords[i] == null)
                continue;

            Vector2 toAim = aimPoint - swords[i].Position;
            dirs[i] = toAim.sqrMagnitude > 0.01f ? toAim.normalized : Vector2.down;
            swords[i].PointAt(dirs[i]);
            Vector2 tip = swords[i].TipPosition;
            float length = CastGround(tip, dirs[i], maxTravel, out float hit) ? hit : maxTravel;
            lines[i] = AddAimLine(tip, tip + dirs[i] * length, swords[i]);
        }

        yield return Wait(NeedleWait(settings.ringAimSeconds));

        for (int k = 0; k < n; k++)
        {
            int i = k % 2 == 0 ? k / 2 : n - 1 - k / 2;
            RemoveAimLine(lines[i]);
            if (swords[i] != null)
                Launch(NeedleShot(swords[i], dirs[i], settings.needleSpeed, maxTravel, stopAtGround: true));
            yield return Wait(NeedleWait(settings.ringFireGap));
        }
    }

    /// <summary>Needles line the top of the screen and drop one column at a time, sweeping toward the target.</summary>
    private IEnumerator NeedleRain(Func<Bounds> target)
    {
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        Bounds tb = target();
        Rect view = ViewOr(tb.center);
        const float margin = 1.5f;
        List<Vector2> spots = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            float x = n == 1 ? view.center.x : Mathf.Lerp(view.xMin + margin, view.xMax - margin, i / (float)(n - 1));
            spots.Add(new Vector2(x, view.yMax - 0.8f - swords[i].TipLength));
        }

        yield return MoveSwords(swords, spots, NeedleWait(settings.needleMoveSeconds), (i, p) => Vector2.down, 0f);

        const float maxTravel = 40f;
        SpriteRenderer[] lines = new SpriteRenderer[n];
        for (int i = 0; i < n; i++)
        {
            if (swords[i] == null)
                continue;
            Vector2 tip = swords[i].TipPosition;
            float length = CastGround(tip, Vector2.down, maxTravel, out float hit) ? hit : maxTravel;
            lines[i] = AddAimLine(tip, tip + Vector2.down * length, swords[i]);
        }

        yield return Wait(NeedleWait(settings.rainAimSeconds));

        bool fromRight = tb.center.x < view.center.x;
        for (int k = 0; k < n; k++)
        {
            int i = fromRight ? n - 1 - k : k;
            RemoveAimLine(lines[i]);
            if (swords[i] != null)
                Launch(NeedleShot(swords[i], Vector2.down, settings.needleSpeed * 1.15f, maxTravel, stopAtGround: true));
            yield return Wait(NeedleWait(settings.rainFireGap));
        }
    }

    /// <summary>
    /// Needles wait at the left / right screen edges and fire straight across one at a time, alternating sides.
    /// Each lane is low (jump it) or high (stay on the ground), never the same three times in a row.
    /// </summary>
    private IEnumerator NeedleWalls(Func<Bounds> target)
    {
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        Bounds tb = target();
        Rect view = ViewOr(tb.center);
        float groundY = GroundBelow(tb.center, tb.min.y);
        float height = Mathf.Max(0.5f, tb.size.y);
        float lowY = groundY + height * 0.35f;
        float highY = groundY + height + swords[0].HalfThickness + 0.35f;

        int firstSide = tb.center.x < view.center.x ? 1 : -1;
        int[] sides = new int[n];
        float[] lanes = new float[n];
        List<SilverSword> order = new List<SilverSword>(n);
        List<Vector2> spots = new List<Vector2>(n);
        int leftNext = 0;
        int rightNext = n - 1;
        int leftQueued = 0;
        int rightQueued = 0;
        bool lastLow = false;
        int run = 0;
        for (int k = 0; k < n; k++)
        {
            int side = k % 2 == 0 ? firstSide : -firstSide;
            if (side < 0 && leftNext > rightNext)
                side = 1;
            else if (side > 0 && rightNext < leftNext)
                side = -1;

            bool low = Random.value < 0.5f;
            if (k >= 2 && low == lastLow && run >= 2)
                low = !low;
            run = k > 0 && low == lastLow ? run + 1 : 1;
            lastLow = low;

            sides[k] = side;
            lanes[k] = low ? lowY : highY;
            SilverSword sword = side < 0 ? swords[leftNext++] : swords[rightNext--];
            order.Add(sword);

            int queued = side < 0 ? leftQueued++ : rightQueued++;
            float edgeX = side < 0 ? view.xMin + 0.6f : view.xMax - 0.6f;
            spots.Add(new Vector2(edgeX + side * queued * 0.9f, lanes[k]));
        }

        yield return MoveSwords(order, spots, NeedleWait(settings.needleMoveSeconds), (i, p) => new Vector2(-sides[i], 0f), 0f);

        float travel = view.width + 4f;
        for (int k = 0; k < n; k++)
        {
            SilverSword sword = order[k];
            if (sword == null)
                continue;

            Vector2 dir = new Vector2(-sides[k], 0f);
            Vector2 edgeSpot = new Vector2(sides[k] < 0 ? view.xMin + 0.6f : view.xMax - 0.6f, lanes[k]);
            if (Vector2.Distance(sword.Position, edgeSpot) > 0.05f)
                yield return MoveSwords(new List<SilverSword> { sword }, new List<Vector2> { edgeSpot }, NeedleWait(0.12f), (i, p) => dir, 0f);

            if (sword == null)
                continue;

            Vector2 tip = sword.TipPosition;
            SpriteRenderer line = AddAimLine(tip, tip + dir * travel, sword);
            yield return Wait(NeedleWait(settings.wallAimSeconds));
            RemoveAimLine(line);
            if (sword != null)
                Launch(NeedleShot(sword, dir, settings.needleSpeed, travel, stopAtGround: false));
            yield return Wait(NeedleWait(settings.wallFireGap));
        }
    }

    private IEnumerator NeedleShot(SilverSword sword, Vector2 dir, float speed, float maxDistance, bool stopAtGround)
    {
        if (sword == null)
            yield break;

        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
        sword.PointAt(dir);
        float travel = maxDistance;
        bool landed = false;
        if (stopAtGround && CastGround(sword.TipPosition, dir, maxDistance, out float hit))
        {
            travel = hit + sword.TipLength * 0.25f;
            landed = true;
        }

        sword.Arm();
        SoundManager.Instance?.PlayHarlieSwordSwing();
        yield return FlyStraight(sword, dir, speed, travel);
        if (sword == null)
            yield break;

        sword.Disarm();
        if (landed && lastClashFrame != Time.frameCount)
        {
            lastClashFrame = Time.frameCount;
            SoundManager.Instance?.PlayItemHitGround();
        }
    }

    // ---------- Boss: Lace spiral ----------

    /// <summary>Spinning swords gather around Harlie, then spiral outward as a turning pinwheel.</summary>
    private IEnumerator LaceSpiral()
    {
        SilverSwordBossSettings b = bossSettings;
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        Vector2 center = OwnerCenter() + Vector2.up;
        float step = 360f / n;
        float radius = b.spiralStartRadius;
        List<Vector2> spots = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            spots.Add(center + Polar(i * step, radius));

        yield return MoveSwords(swords, spots, b.spiralGatherSeconds, null, b.bladeSpinDegreesPerSecond);

        float angle = 0f;
        float t = 0f;
        while (t < b.spiralWindupSeconds)
        {
            float dt = Dt;
            t += dt;
            angle += b.spiralDegreesPerSecond * 2f * dt;
            PlaceRing(swords, center, radius, angle, step);
            yield return null;
        }

        ArmAll(swords);
        SoundManager.Instance?.PlayHarlieSwordSwing();
        while (radius < b.spiralMaxRadius)
        {
            float dt = Dt;
            radius += b.spiralExpandSpeed * dt;
            angle += b.spiralDegreesPerSecond * dt;
            PlaceRing(swords, center, radius, angle, step);
            yield return null;
        }

        DisarmAll(swords);
    }

    // ---------- Boss: Moorwing blades ----------

    /// <summary>One of three at random: boomerang loops, a ring that bursts outward, or sweeping arcs.</summary>
    private IEnumerator MoorwingBlades(Func<Bounds> target)
    {
        switch (Random.Range(0, 3))
        {
            case 0: return Boomerangs(target);
            case 1: return RingBurst();
            default: return Sweeps(target);
        }
    }

    /// <summary>
    /// Pairs of spinning swords loop out past the player and back. The first goes out high and comes back low,
    /// the second goes out low and comes back high, so every low pass is its own jump.
    /// </summary>
    private IEnumerator Boomerangs(Func<Bounds> target)
    {
        SilverSwordBossSettings b = bossSettings;
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        for (int p = 0; p * 2 < n; p++)
        {
            SilverSword first = swords[p * 2];
            SilverSword second = p * 2 + 1 < n ? swords[p * 2 + 1] : null;
            if (first != null)
                Launch(BoomerangLoop(first, target, viaTop: true));
            yield return Wait(b.boomerangPairGap);
            if (second != null)
                Launch(BoomerangLoop(second, target, viaTop: false));
            yield return Wait(b.boomerangThrowGap);
        }

        yield return WaitForFlights();
    }

    private IEnumerator BoomerangLoop(SilverSword sword, Func<Bounds> target, bool viaTop)
    {
        SilverSwordBossSettings b = bossSettings;
        Bounds tb = target();
        Vector2 start = OwnerCenter();
        float dir = tb.center.x >= start.x ? 1f : -1f;
        float groundY = GroundBelow(start, OwnerBounds().min.y);
        SpinLanes(tb, groundY, sword.TipLength, out float lowY, out float highY);

        float reach = Mathf.Clamp(Mathf.Abs(tb.center.x - start.x) + b.boomerangExtraSpaces, 5f, b.boomerangMaxSpaces);
        float halfReach = reach * 0.5f;
        float midY = (lowY + highY) * 0.5f;
        float halfHeight = (highY - lowY) * 0.5f;
        Vector2 loopCenter = new Vector2(start.x + dir * halfReach, midY);

        yield return MoveSwords(new List<SilverSword> { sword }, new List<Vector2> { new Vector2(start.x, midY) }, 0.25f,
            null, b.bladeSpinDegreesPerSecond * dir);
        if (sword == null)
            yield break;

        sword.Arm();
        SoundManager.Instance?.PlayHarlieSwordSwing();
        float t = 0f;
        float duration = b.boomerangLoopSeconds;
        while (sword != null)
        {
            t += Dt;
            float u = Mathf.Clamp01(t / duration);
            float phi = Mathf.PI + (viaTop ? -1f : 1f) * 2f * Mathf.PI * u;
            Vector2 pos = loopCenter + new Vector2(dir * halfReach * Mathf.Cos(phi), halfHeight * Mathf.Sin(phi));
            sword.transform.position = new Vector3(pos.x, pos.y, 0f);
            if (u >= 1f)
                break;
            yield return null;
        }

        if (sword != null)
            sword.Disarm();
    }

    /// <summary>Spinning swords circle Harlie in a growing ring, stop to show their lanes, then fly straight out.</summary>
    private IEnumerator RingBurst()
    {
        SilverSwordBossSettings b = bossSettings;
        List<SilverSword> swords = SwordsByX();
        int n = swords.Count;
        if (n == 0)
            yield break;

        float step = 360f / n;
        float angle = 0f;
        Vector2 center = OwnerCenter();
        List<Vector2> spots = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            spots.Add(center + Polar(i * step, b.ringStartRadius));

        yield return MoveSwords(swords, spots, 0.35f, null, b.bladeSpinDegreesPerSecond);
        ArmAll(swords);

        float radius = b.ringStartRadius;
        float t = 0f;
        while (t < b.ringGrowSeconds)
        {
            float dt = Dt;
            t += dt;
            float u = Mathf.Clamp01(t / b.ringGrowSeconds);
            radius = Mathf.Lerp(b.ringStartRadius, b.ringEndRadius, u * u * (3f - 2f * u));
            angle += b.ringDegreesPerSecond * dt;
            center = OwnerCenter();
            PlaceRing(swords, center, radius, angle, step);
            yield return null;
        }

        Vector2[] dirs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            dirs[i] = Polar(i * step + angle, 1f);
            if (swords[i] != null)
                AddAimLine(swords[i].Position, swords[i].Position + dirs[i] * 3f, swords[i]);
        }

        yield return Wait(b.ringLockSeconds);
        ClearAimLines();

        for (int i = 0; i < n; i++)
        {
            if (swords[i] != null)
                Launch(FlyStraight(swords[i], dirs[i], b.ringFlySpeed, b.ringFlySpaces));
        }

        SoundManager.Instance?.PlayHarlieSwordSwing();
        yield return WaitForFlights();
        DisarmAll(swords);
    }

    /// <summary>Spinning swords take turns arcing across the screen; a line shows how low each arc dips.</summary>
    private IEnumerator Sweeps(Func<Bounds> target)
    {
        SilverSwordBossSettings b = bossSettings;
        List<SilverSword> swords = SwordsByX();
        int count = Mathf.Min(b.sweepCount, swords.Count);
        if (count == 0)
            yield break;

        Bounds tb = target();
        Rect view = ViewOr(tb.center);
        int firstSide = tb.center.x < view.center.x ? 1 : -1;
        bool lastLow = false;
        int run = 0;
        for (int k = 0; k < count; k++)
        {
            bool low = Random.value < 0.5f;
            if (k >= 2 && low == lastLow && run >= 2)
                low = !low;
            run = k > 0 && low == lastLow ? run + 1 : 1;
            lastLow = low;

            int side = k % 2 == 0 ? firstSide : -firstSide;
            if (swords[k] != null)
                Launch(SweepOne(swords[k], target, side, low));
            yield return Wait(b.sweepGap);
        }

        yield return WaitForFlights();
    }

    private IEnumerator SweepOne(SilverSword sword, Func<Bounds> target, int side, bool low)
    {
        SilverSwordBossSettings b = bossSettings;
        Bounds tb = target();
        Rect view = ViewOr(tb.center);
        float tip = sword.TipLength;
        float groundY = GroundBelow(tb.center, tb.min.y);
        SpinLanes(tb, groundY, tip, out float lowY, out float highY);
        float dipY = low ? lowY : highY;
        float topY = Mathf.Max(Mathf.Min(view.yMax - tip, groundY + b.sweepTopSpaces), highY + 1.5f);
        float startX = side < 0 ? view.xMin + tip : view.xMax - tip;
        float endX = side < 0 ? view.xMax + tip * 2f : view.xMin - tip * 2f;
        float spin = b.bladeSpinDegreesPerSecond * -side;

        yield return MoveSwords(new List<SilverSword> { sword }, new List<Vector2> { new Vector2(startX, topY) }, 0.4f, null, spin);
        if (sword == null)
            yield break;

        SpriteRenderer line = AddAimLine(new Vector2(view.xMin, dipY), new Vector2(view.xMax, dipY), sword);
        yield return Wait(b.sweepAimSeconds);
        RemoveAimLine(line);
        if (sword == null)
            yield break;

        sword.Arm();
        SoundManager.Instance?.PlayHarlieSwordSwing();
        float t = 0f;
        while (sword != null)
        {
            t += Dt;
            float u = Mathf.Clamp01(t / b.sweepSeconds);
            float x = Mathf.Lerp(startX, endX, u);
            float y = topY - (topY - dipY) * Mathf.Sin(Mathf.PI * u);
            sword.transform.position = new Vector3(x, y, 0f);
            if (u >= 1f)
                break;
            yield return null;
        }

        if (sword != null)
            sword.Disarm();
    }

    /// <summary>Lanes for spinning swords: low = jump over it, high = clears a standing target's head.</summary>
    private static void SpinLanes(Bounds target, float groundY, float tipLength, out float lowY, out float highY)
    {
        float height = Mathf.Max(0.5f, target.size.y);
        lowY = groundY + tipLength * 0.75f;
        highY = groundY + height + tipLength + 0.3f;
    }

    // ---------- Movement helpers ----------

    private void Launch(IEnumerator flight)
    {
        StartCoroutine(TrackFlight(flight));
    }

    private IEnumerator TrackFlight(IEnumerator flight)
    {
        pendingFlights++;
        yield return flight;
        pendingFlights = Mathf.Max(0, pendingFlights - 1);
    }

    private IEnumerator WaitForFlights()
    {
        while (pendingFlights > 0)
            yield return null;
    }

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Dt;
            yield return null;
        }
    }

    /// <summary>
    /// Eases every sword to its spot. With <paramref name="spin"/> = 0 the tips follow <paramref name="aim"/>
    /// (index, position → direction); otherwise the swords spin.
    /// </summary>
    private IEnumerator MoveSwords(List<SilverSword> swords, List<Vector2> targets, float seconds,
        Func<int, Vector2, Vector2> aim, float spin)
    {
        int n = Mathf.Min(swords.Count, targets.Count);
        Vector2[] starts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            if (swords[i] == null)
                continue;
            starts[i] = swords[i].Position;
            if (Mathf.Abs(spin) > 0.01f)
                swords[i].SetSpin(spin);
        }

        float duration = Mathf.Max(0.01f, seconds);
        float t = 0f;
        while (true)
        {
            float dt = Dt;
            t += dt;
            float u = Mathf.Clamp01(t / duration);
            float eased = u * u * (3f - 2f * u);
            for (int i = 0; i < n; i++)
            {
                SilverSword sword = swords[i];
                if (sword == null)
                    continue;

                Vector2 pos = Vector2.Lerp(starts[i], targets[i], eased);
                sword.transform.position = new Vector3(pos.x, pos.y, 0f);
                if (Mathf.Abs(spin) <= 0.01f && aim != null)
                {
                    Vector2 dir = aim(i, pos);
                    if (u >= 1f)
                        sword.PointAt(dir);
                    else
                        sword.TurnToward(dir, 900f * dt);
                }
            }

            if (u >= 1f)
                yield break;
            yield return null;
        }
    }

    private IEnumerator FlyStraight(SilverSword sword, Vector2 dir, float speed, float distance)
    {
        float remaining = Mathf.Max(0f, distance);
        while (remaining > 0.0001f && sword != null)
        {
            float step = Mathf.Min(remaining, speed * Dt);
            sword.transform.position += (Vector3)(dir * step);
            remaining -= step;
            yield return null;
        }
    }

    private static void PlaceRing(List<SilverSword> swords, Vector2 center, float radius, float angle, float step)
    {
        for (int i = 0; i < swords.Count; i++)
        {
            if (swords[i] == null)
                continue;
            Vector2 pos = center + Polar(i * step + angle, radius);
            swords[i].transform.position = new Vector3(pos.x, pos.y, 0f);
        }
    }

    private static void ArmAll(List<SilverSword> swords)
    {
        for (int i = 0; i < swords.Count; i++)
        {
            if (swords[i] != null)
                swords[i].Arm();
        }
    }

    private static void DisarmAll(List<SilverSword> swords)
    {
        for (int i = 0; i < swords.Count; i++)
        {
            if (swords[i] != null)
                swords[i].Disarm();
        }
    }

    private static Vector2 Polar(float degrees, float radius)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
    }

    private List<SilverSword> SwordsByX()
    {
        List<SilverSword> swords = new List<SilverSword>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].sword != null)
                swords.Add(slots[i].sword);
        }

        swords.Sort((a, b) => a.Position.x.CompareTo(b.Position.x));
        return swords;
    }

    // ---------- World queries ----------

    private Bounds OwnerBounds()
    {
        if (ownerBody != null && ownerBody.enabled)
            return ownerBody.bounds;
        return new Bounds(owner != null ? owner.position : transform.position, Vector3.one);
    }

    private Vector2 OwnerCenter() => OwnerBounds().center;

    private Vector2 OwnerHead()
    {
        Bounds b = OwnerBounds();
        return new Vector2(b.center.x, b.max.y);
    }

    /// <summary>First solid floor / platform along <paramref name="dir"/> (characters and triggers ignored).</summary>
    private bool CastGround(Vector2 origin, Vector2 dir, float maxDistance, out float distance)
    {
        distance = maxDistance;
        if (groundMask.value == 0 || maxDistance <= 0f || dir.sqrMagnitude < 0.0001f)
            return false;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundMask);

        RayHits.Clear();
        Physics2D.Raycast(origin, dir.normalized, filter, RayHits, maxDistance);
        float best = float.MaxValue;
        for (int i = 0; i < RayHits.Count; i++)
        {
            Collider2D col = RayHits[i].collider;
            if (col == null || RayHits[i].distance < 0.05f)
                continue;
            if (col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<Boss>() != null)
                continue;
            best = Mathf.Min(best, RayHits[i].distance);
        }

        if (best >= float.MaxValue)
            return false;

        distance = best;
        return true;
    }

    private float GroundBelow(Vector2 point, float fallback)
    {
        return CastGround(point, Vector2.down, 30f, out float d) ? point.y - d : fallback;
    }

    private static Rect ViewOr(Vector2 fallbackCenter)
    {
        Camera cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetComponent<Camera>() : Camera.main;
        if (cam == null || !cam.orthographic)
            return new Rect(fallbackCenter.x - 12f, fallbackCenter.y - 7f, 24f, 14f);

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - halfWidth, c.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
    }

    /// <summary>Closest living boss / common enemy body within <paramref name="radius"/>.</summary>
    public static bool TryFindClosestEnemy(Vector2 from, float radius, out Bounds bounds)
    {
        bounds = default;
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;

        EnemyScratch.Clear();
        Physics2D.OverlapCircle(from, Mathf.Max(0.5f, radius), filter, EnemyScratch);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < EnemyScratch.Count; i++)
        {
            Collider2D col = EnemyScratch[i];
            if (col == null || !col.enabled || EnemyDetectionZone.IsDetectionOnlyCollider(col) ||
                col.GetComponent<AttackHitbox>() != null || col.GetComponentInParent<Projectile>() != null)
                continue;

            Boss boss = col.GetComponentInParent<Boss>();
            bool alive;
            if (boss != null)
            {
                alive = !boss.IsDead;
            }
            else
            {
                ICommonEnemy enemy = col.GetComponentInParent<ICommonEnemy>();
                alive = enemy != null && !enemy.IsDead;
            }

            if (!alive)
                continue;

            float sqr = ((Vector2)col.bounds.center - from).sqrMagnitude;
            if (sqr < best)
            {
                best = sqr;
                bounds = col.bounds;
                found = true;
            }
        }

        EnemyScratch.Clear();
        return found;
    }

    // ---------- Aim lines ----------

    private SpriteRenderer AddAimLine(Vector2 from, Vector2 to, SilverSword reference)
    {
        if (lineSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            lineSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0f, 0.5f), tex.width);
        }

        SpriteRenderer sr = new GameObject("Silver Sword Aim Line").AddComponent<SpriteRenderer>();
        sr.sprite = lineSprite;
        if (reference != null && reference.Renderer != null)
        {
            sr.sharedMaterial = reference.Renderer.sharedMaterial;
            sr.sortingLayerID = reference.Renderer.sortingLayerID;
            sr.sortingOrder = reference.Renderer.sortingOrder - 1;
        }

        sr.color = aimColor;
        Vector2 d = to - from;
        Transform t = sr.transform;
        t.position = new Vector3(from.x, from.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        t.localScale = new Vector3(Mathf.Max(0.01f, d.magnitude), AimLineThickness, 1f);
        aimLines.Add(sr);
        return sr;
    }

    private void RemoveAimLine(SpriteRenderer line)
    {
        if (line == null)
            return;

        aimLines.Remove(line);
        Destroy(line.gameObject);
    }

    private void ClearAimLines()
    {
        for (int i = 0; i < aimLines.Count; i++)
        {
            if (aimLines[i] != null)
                Destroy(aimLines[i].gameObject);
        }

        aimLines.Clear();
    }
}
