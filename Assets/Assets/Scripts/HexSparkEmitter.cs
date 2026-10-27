using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hex's sparks (from Little Program Harlie): a burst of three whenever she's hurt, and a charge aura while
/// Streaming is on. The charge aura works like Count's charge particles: sparks bloom around her silhouette,
/// rise while drifting / wobbling left and right, and fade out near the top. Sparse at low charge, dense at
/// full charge (all three spark types). Works on playable Hex and Boss Hex.
/// </summary>
public class HexSparkEmitter : MonoBehaviour
{
    [Tooltip("Hurt bursts spawn from here. Empty = the \"Spark box\" child, or this object.")]
    [SerializeField] private Transform sparkBox;
    [Tooltip("Spark 1 / 2 / 3. A hurt burst spawns one of each; the charge aura uses 1–2 below full charge, all at full.")]
    [SerializeField] private HexSpark[] sparkPrefabs = new HexSpark[3];
    [Tooltip("Sorting order above the host body.")]
    [SerializeField] private int sortingOffset = 5;

    [Header("Charge Aura - Float Path")]
    [SerializeField] private float riseFromBottom = 1f;
    [SerializeField] private float riseToTop = 3f;
    [SerializeField] private float floatDuration = 1.1f;

    [Header("Charge Aura - Spawn Volume (around Hex)")]
    [SerializeField] private float spawnSpreadX = 1.35f;
    [Tooltip("Some sparks start partway up the body instead of only at the feet.")]
    [SerializeField] private float spawnHeightVariance = 0.85f;
    [Tooltip("End height varies so some pop out higher/lower above her.")]
    [SerializeField] private float endHeightVariance = 0.45f;
    [Tooltip("Outer band of Spawn Spread X treated as the left/right side zones.")]
    [SerializeField] [Range(0.35f, 0.9f)] private float sideZoneStart = 0.5f;
    [SerializeField] private float sideSpawnMinY = 0f;
    [SerializeField] private float sideSpawnMaxY = 1f;

    [Header("Charge Aura - Density")]
    [SerializeField] private int copiesPerTypeAtFullCharge = 4;
    [SerializeField] private int copiesPerTypeAtPartialCharge = 1;
    [SerializeField] private float spawnIntervalFull = 0.06f;
    [SerializeField] private float spawnIntervalPartial = 0.18f;
    [SerializeField] private float sideSpawnIntervalFull = 0.025f;
    [SerializeField] private float sideSpawnIntervalPartial = 0.09f;
    [SerializeField] private float spawnIntervalJitter = 0.05f;
    [SerializeField] [Range(0f, 0.45f)] private float burstSpawnChance = 0.22f;
    [Tooltip("At minimum charge, spawn intervals are multiplied by this (slower).")]
    [SerializeField] private float spawnRateScaleAtMinCharge = 2.4f;
    [Tooltip("At max charge, spawn intervals are multiplied by this (faster).")]
    [SerializeField] private float spawnRateScaleAtMaxCharge = 0.55f;

    [Header("Charge Aura - Motion / Fade")]
    [SerializeField] private float dissipateDuration = 0.45f;
    [SerializeField] private float horizontalDrift = 0.65f;
    [SerializeField] private float wobbleAmplitude = 0.32f;
    [SerializeField] private float wobbleFrequency = 2.6f;
    [SerializeField] private float startAlphaFull = 0.92f;
    [SerializeField] private float startAlphaPartial = 0.55f;
    [Tooltip("Stay fully visible until this fraction of the rise is complete, then fade out.")]
    [SerializeField] [Range(0.5f, 0.98f)] private float fadeStartNormalized = 0.82f;

    [Header("Charge Aura - Inward Gather (Bass QuickTime / MM7-style)")]
    [Tooltip("Outer ring radius — keep large enough to read clearly around Bass.")]
    [SerializeField] private float inwardRadiusMin = 3.1f;
    [SerializeField] private float inwardRadiusMax = 3.85f;
    [SerializeField] private float inwardDurationAtMinCharge = 0.55f;
    [SerializeField] private float inwardDurationAtMaxCharge = 0.28f;
    [Tooltip("Seconds between full ring waves at low charge.")]
    [SerializeField] private float inwardWaveIntervalAtMin = 0.55f;
    [Tooltip("Seconds between full ring waves near max charge.")]
    [SerializeField] private float inwardWaveIntervalAtMax = 0.28f;
    [SerializeField] private int inwardRingCountMin = 8;
    [SerializeField] private int inwardRingCountMax = 14;
    [SerializeField] private float inwardStartScale = 1.35f;
    [SerializeField] private float inwardEndScale = 0.2f;
    [Tooltip("Stay bright until this fraction of the inward trip, then fade.")]
    [SerializeField] [Range(0.35f, 0.95f)] private float inwardFadeStartNormalized = 0.72f;

    private struct AuraSpark
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public int Kind;
        public Vector3 StartWorldPos;
        public Vector3 EndWorldPos;
        public float DriftX;
        public float WobblePhase;
        public float WobbleStrength;
        public float StartTime;
        public float Duration;
        public float StartAlpha;
        public bool Inward;
        public float StartScale;
        public float EndScale;
        public Color BaseRgb;
    }

    private PlayerController player;
    private Boss boss;
    private SpriteRenderer body;
    private int lastHealth = -1;

    private readonly List<AuraSpark> auraSparks = new List<AuraSpark>(24);
    private float[] nextSpawnAt = new float[0];
    private float nextLeftSideSpawnAt;
    private float nextRightSideSpawnAt;
    private float nextInwardWaveAt;
    private bool wasStreaming;
    private bool wasFullCharge;
    private bool dissipating;
    private float dissipateEndTime;

    /// <summary>While true, the charge aura spawns sparks around her.</summary>
    public bool Streaming { get; set; }

    /// <summary>0–1. Higher = faster spawning (like Count's charge progress).</summary>
    public float ChargeProgress { get; set; }

    /// <summary>Full charge: denser aura with all three spark types.</summary>
    public bool FullCharge { get; set; }

    /// <summary>
    /// When true, partial charge uses circle→center gather (shrink/fade).
    /// At full charge, switches to Hex's usual rising aura.
    /// </summary>
    public bool InwardGatherUntilFull { get; set; }

    /// <summary>Multiplies spark RGB (alpha still driven by aura). Light pink for Bass QuickTime.</summary>
    public Color Tint { get; set; } = Color.white;

    /// <summary>When false, health loss does not spawn hurt bursts (Bass charge-only use).</summary>
    public bool EnableHurtBursts { get; set; } = true;

    private bool UsesInwardGather => InwardGatherUntilFull && !FullCharge;

    private SortingGroup HostGroup =>
        player != null ? player.EffectSortingGroup : boss != null ? boss.EffectSortingGroup : null;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        boss = GetComponent<Boss>();
        body = GetComponent<SpriteRenderer>();
        ResolveSparkBox();
        ResetSpawnState();
    }

    /// <summary>Hurt-burst / spark origin (Bass uses child "Spark Spawner").</summary>
    public Transform SparkBox
    {
        get
        {
            ResolveSparkBox();
            return sparkBox;
        }
    }

    /// <summary>Hurt-burst / spark origin (Bass uses child "Spark Spawner").</summary>
    public void SetSparkBox(Transform box)
    {
        sparkBox = box;
    }

    private void ResolveSparkBox()
    {
        if (sparkBox != null)
            return;

        sparkBox = transform.Find("Spark box");
        if (sparkBox == null)
            sparkBox = transform.Find("Spark Spawner");
        // Bass.prefab currently has a trailing space in the child name.
        if (sparkBox == null)
            sparkBox = transform.Find("Spark Spawner ");
        if (sparkBox != null)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
                continue;
            string n = child.name;
            if (n != null && n.Trim() == "Spark Spawner")
            {
                sparkBox = child;
                return;
            }
        }
    }

    private void OnEnable()
    {
        if (player != null)
            player.OnHealthChanged += HandleHealthChanged;
        if (boss != null)
            boss.OnHealthChanged += HandleHealthChanged;
    }

    private void Start()
    {
        lastHealth = player != null ? player.CurrentHealth : boss != null ? boss.CurrentHealth : -1;
    }

    private void OnDisable()
    {
        if (player != null)
            player.OnHealthChanged -= HandleHealthChanged;
        if (boss != null)
            boss.OnHealthChanged -= HandleHealthChanged;
        Streaming = false;
        wasStreaming = false;
        dissipating = false;
        ClearAura();
    }

    private void OnDestroy()
    {
        ClearAura();
    }

    /// <summary>Stops the charge aura and removes its sparks right away.</summary>
    public void StopImmediate()
    {
        Streaming = false;
        wasStreaming = false;
        wasFullCharge = false;
        dissipating = false;
        ClearAura();
        ResetSpawnState();
    }

    /// <summary>Assign Hex Spark 1/2/3 prefabs at runtime (Bass QuickTime).</summary>
    public void SetSparkPrefabs(HexSpark[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
            return;

        bool changed = sparkPrefabs == null || sparkPrefabs.Length != prefabs.Length;
        if (!changed)
        {
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (sparkPrefabs[i] != prefabs[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
            return;

        sparkPrefabs = prefabs;
        EnsureSpawnArray();
        // Only reset timers when the prefab list actually changes — calling this every
        // frame used to freeze inward/rising spawns after the first wave.
        if (!Streaming)
            ResetSpawnState();
    }

    private void HandleHealthChanged(int current, int max)
    {
        if (EnableHurtBursts && lastHealth >= 0 && current < lastHealth)
            Burst();
        lastHealth = current;
    }

    private void Update()
    {
        if (Streaming && !wasStreaming)
        {
            dissipating = false;
            ScheduleInitialSpawns();
        }
        else if (!Streaming && wasStreaming)
        {
            BeginDissipate();
        }

        // Entering full charge: stop MM7 ring waves and kick Hex's rising aura schedule.
        if (Streaming && FullCharge && !wasFullCharge && InwardGatherUntilFull)
        {
            ClearInwardAuraSparks();
            ScheduleInitialSpawns();
        }

        wasStreaming = Streaming;
        wasFullCharge = FullCharge;
        if (Streaming)
        {
            if (UsesInwardGather)
            {
                TrySpawnInwardRingWaves();
            }
            else
            {
                TrySpawnAura();
                TrySpawnSideStreams();
            }
        }

        UpdateAuraSparks();

        if (dissipating && Time.time >= dissipateEndTime + 0.05f)
        {
            ClearAura();
            dissipating = false;
        }
    }

    // ---------- Hurt bursts ----------

    public void Burst()
    {
        Burst(sparkBox != null ? sparkBox.position : transform.position);
    }

    public void Burst(Vector3 position)
    {
        if (sparkPrefabs == null)
            return;

        SortingGroup group = HostGroup;
        for (int i = 0; i < sparkPrefabs.Length; i++)
        {
            if (sparkPrefabs[i] == null)
                continue;

            HexSpark spark = Instantiate(sparkPrefabs[i], new Vector3(position.x, position.y, 0f), Quaternion.identity);
            CharacterEffectSorting.ApplyDetachedEffectNearHost(spark.GetComponent<SpriteRenderer>(), group, body, sortingOffset);
        }
    }

    /// <summary>
    /// Fire Hex aura sparks in random directions (boss defeat cinema). Prefabs resolve from this
    /// emitter, any scene HexSparkEmitter, or Resources/Hex Spark 1–3.
    /// </summary>
    public static void BurstRandomDirections(
        Vector3 position,
        int count,
        SortingGroup hostGroup = null,
        SpriteRenderer hostBody = null,
        bool useUnscaledTime = true)
    {
        HexSpark[] prefabs = ResolveSparkPrefabs();
        if (prefabs == null || prefabs.Length == 0 || count <= 0)
            return;

        int spawn = Mathf.Clamp(count, 1, 48);
        for (int i = 0; i < spawn; i++)
        {
            HexSpark prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null)
                continue;

            HexSpark spark = Instantiate(prefab, new Vector3(position.x, position.y, 0f), Quaternion.identity);
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(3.5f, 9.5f);
            Vector2 vel = new Vector2(Mathf.Cos(ang) * speed, Mathf.Sin(ang) * speed + Random.Range(1f, 4f));
            spark.LaunchOutward(vel, useUnscaledTime);
            CharacterEffectSorting.ApplyDetachedEffectNearHost(
                spark.GetComponent<SpriteRenderer>(), hostGroup, hostBody, 6);
        }
    }

    public static HexSpark[] ResolveSparkPrefabs()
    {
        HexSparkEmitter any = Object.FindFirstObjectByType<HexSparkEmitter>(FindObjectsInactive.Include);
        if (any != null && any.sparkPrefabs != null && any.sparkPrefabs.Length > 0)
        {
            for (int i = 0; i < any.sparkPrefabs.Length; i++)
            {
                if (any.sparkPrefabs[i] != null)
                    return any.sparkPrefabs;
            }
        }

        var loaded = new List<HexSpark>(3);
        TryLoadSpark(loaded, "Hex Spark 1");
        TryLoadSpark(loaded, "Hex Spark 2");
        TryLoadSpark(loaded, "Hex Spark 3");
        return loaded.Count > 0 ? loaded.ToArray() : null;
    }

    private static void TryLoadSpark(List<HexSpark> list, string resourceName)
    {
        HexSpark spark = Resources.Load<HexSpark>(resourceName);
        if (spark != null)
            list.Add(spark);
    }

    // ---------- Charge aura ----------

    private int KindCount => sparkPrefabs != null ? sparkPrefabs.Length : 0;

    /// <summary>Below full charge only the first two spark types appear.</summary>
    private int AllowedKinds => FullCharge ? KindCount : Mathf.Min(2, KindCount);

    private void TrySpawnAura()
    {
        EnsureSpawnArray();
        float interval = (FullCharge ? spawnIntervalFull : spawnIntervalPartial) * ChargeSpawnScale();
        float startAlpha = FullCharge ? startAlphaFull : startAlphaPartial;
        float progress = Mathf.Clamp01(ChargeProgress);
        int maxPerType = FullCharge
            ? copiesPerTypeAtFullCharge + Mathf.RoundToInt(progress * 2f)
            : copiesPerTypeAtPartialCharge + (progress > 0.35f ? 1 : 0);

        int allowed = AllowedKinds;
        for (int kind = 0; kind < allowed; kind++)
        {
            if (sparkPrefabs[kind] == null || Time.time < nextSpawnAt[kind])
                continue;

            if (CountActiveOfKind(kind) >= maxPerType)
            {
                nextSpawnAt[kind] = Time.time + Random.Range(0.02f, interval * 0.5f);
                continue;
            }

            SpawnAuraSpark(kind, startAlpha, sideSign: 0);
            nextSpawnAt[kind] = Time.time + Mathf.Max(0.02f, interval + Random.Range(-spawnIntervalJitter, spawnIntervalJitter));

            if (FullCharge && Random.value < burstSpawnChance && CountActiveOfKind(kind) < maxPerType)
                SpawnAuraSpark(kind, startAlpha * Random.Range(0.75f, 1f), sideSign: 0);
        }
    }

    private void TrySpawnSideStreams()
    {
        float interval = (FullCharge ? sideSpawnIntervalFull : sideSpawnIntervalPartial) * ChargeSpawnScale();
        float startAlpha = FullCharge ? startAlphaFull : startAlphaPartial;
        int maxPerType = FullCharge ? copiesPerTypeAtFullCharge : copiesPerTypeAtPartialCharge;
        float halfJitter = spawnIntervalJitter * 0.5f;

        if (Time.time >= nextLeftSideSpawnAt)
        {
            TrySpawnSideSpark(-1, maxPerType, startAlpha);
            nextLeftSideSpawnAt = Time.time + interval + Random.Range(-halfJitter, halfJitter);
        }

        if (Time.time >= nextRightSideSpawnAt)
        {
            TrySpawnSideSpark(1, maxPerType, startAlpha);
            nextRightSideSpawnAt = Time.time + interval + Random.Range(-halfJitter, halfJitter);
        }
    }

    private void TrySpawnSideSpark(int sideSign, int maxPerType, float startAlpha)
    {
        if (!TryPickKind(maxPerType, out int kind))
            return;

        SpawnAuraSpark(kind, startAlpha, sideSign);
        if (FullCharge && Random.value < burstSpawnChance && TryPickKind(maxPerType, out kind))
            SpawnAuraSpark(kind, startAlpha * Random.Range(0.75f, 1f), sideSign);
    }

    private bool TryPickKind(int maxPerType, out int kind)
    {
        kind = 0;
        int allowed = AllowedKinds;
        if (allowed <= 0)
            return false;

        int start = Random.Range(0, allowed);
        for (int i = 0; i < allowed; i++)
        {
            int candidate = (start + i) % allowed;
            if (sparkPrefabs[candidate] == null || CountActiveOfKind(candidate) >= maxPerType)
                continue;

            kind = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mega Man 7-style pre-max charge: a full ring of sparks appears around Bass and
    /// rushes to the center together, then the wave repeats until full charge.
    /// </summary>
    private void TrySpawnInwardRingWaves()
    {
        if (KindCount <= 0 || AllowedKinds <= 0 || Time.time < nextInwardWaveAt)
            return;

        float progress = Mathf.Clamp01(ChargeProgress);
        float startAlpha = Mathf.Lerp(Mathf.Max(0.75f, startAlphaPartial), startAlphaFull, progress);
        SpawnInwardRingWave(startAlpha);

        float interval = Mathf.Lerp(inwardWaveIntervalAtMin, inwardWaveIntervalAtMax, progress);
        nextInwardWaveAt = Time.time + Mathf.Max(0.08f, interval + Random.Range(-spawnIntervalJitter * 0.35f, spawnIntervalJitter * 0.35f));
    }

    private void SpawnInwardRingWave(float startAlpha)
    {
        float progress = Mathf.Clamp01(ChargeProgress);
        int count = Mathf.RoundToInt(Mathf.Lerp(inwardRingCountMin, inwardRingCountMax, progress));
        count = Mathf.Clamp(count, 6, 20);

        // One shared radius/duration so the ring reads as a single converging pulse.
        float radius = Mathf.Lerp(inwardRadiusMin, inwardRadiusMax, Random.value);
        float duration = Mathf.Lerp(inwardDurationAtMinCharge, inwardDurationAtMaxCharge, progress)
                         * Random.Range(0.95f, 1.05f);
        float baseAngle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 center = transform.position;
        center.z = 0f;

        for (int i = 0; i < count; i++)
        {
            int kind = i % AllowedKinds;
            if (sparkPrefabs[kind] == null)
                continue;

            float angle = baseAngle + (Mathf.PI * 2f * i) / count;
            Vector3 startWorld = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            Vector2 jitter = Random.insideUnitCircle * 0.04f;
            Vector3 endWorld = center + new Vector3(jitter.x, jitter.y, 0f);

            PushAuraSpark(
                kind,
                startWorld,
                endWorld,
                startAlpha * Random.Range(0.9f, 1f),
                duration,
                inward: true,
                driftX: 0f,
                wobblePhase: 0f,
                wobbleStrength: 0f,
                startScale: inwardStartScale * Random.Range(0.95f, 1.1f),
                endScale: inwardEndScale);
        }
    }

    private void ClearInwardAuraSparks()
    {
        for (int i = auraSparks.Count - 1; i >= 0; i--)
        {
            if (!auraSparks[i].Inward)
                continue;

            if (auraSparks[i].Transform != null)
                Destroy(auraSparks[i].Transform.gameObject);
            auraSparks.RemoveAt(i);
        }

        nextInwardWaveAt = float.MaxValue;
    }

    private void SpawnAuraSpark(int kind, float startAlpha, int sideSign)
    {
        HexSpark prefab = sparkPrefabs[kind];
        if (prefab == null)
            return;

        float spawnX = PickSpawnX(sideSign);
        float startY = IsSideBand(spawnX, sideSign)
            ? Random.Range(sideSpawnMinY, sideSpawnMaxY)
            : Random.Range(-riseFromBottom, -riseFromBottom + spawnHeightVariance);
        float endY = Random.Range(riseToTop * 0.2f, riseToTop + endHeightVariance);
        float endX = spawnX + Random.Range(-horizontalDrift, horizontalDrift);

        Vector3 startWorld = transform.position + new Vector3(spawnX, startY, 0f);
        Vector3 endWorld = transform.position + new Vector3(endX, endY, 0f);
        startWorld.z = 0f;
        endWorld.z = 0f;

        PushAuraSpark(
            kind,
            startWorld,
            endWorld,
            startAlpha,
            floatDuration * Random.Range(0.65f, 1.35f),
            inward: false,
            driftX: Random.Range(-horizontalDrift * 0.5f, horizontalDrift * 0.5f),
            wobblePhase: Random.Range(0f, Mathf.PI * 2f),
            wobbleStrength: Random.Range(0.65f, 1.35f),
            startScale: 1f,
            endScale: 1f);
    }

    private void PushAuraSpark(
        int kind,
        Vector3 startWorld,
        Vector3 endWorld,
        float startAlpha,
        float duration,
        bool inward,
        float driftX,
        float wobblePhase,
        float wobbleStrength,
        float startScale,
        float endScale)
    {
        HexSpark prefab = sparkPrefabs[kind];
        if (prefab == null)
            return;

        HexSpark spark = Instantiate(prefab, startWorld, Quaternion.identity);
        // The aura drives the spark; its own pop-and-fall motion stays off.
        spark.enabled = false;
        SpriteRenderer renderer = spark.Renderer != null ? spark.Renderer : spark.GetComponent<SpriteRenderer>();
        Color baseRgb = Color.white;
        if (renderer != null)
        {
            CharacterEffectSorting.ApplyDetachedEffectNearHost(renderer, HostGroup, body, sortingOffset);
            baseRgb = renderer.color;
            baseRgb.a = 1f;
            renderer.color = ApplyTint(baseRgb, startAlpha);
        }

        // Keep authored prefab scale (Bass Spark is smaller than Hex's) as the 1.0 baseline.
        float prefabScale = Mathf.Abs(spark.transform.localScale.x);
        if (prefabScale < 0.01f)
            prefabScale = 1f;
        float scaledStart = Mathf.Max(0.05f, startScale) * prefabScale;
        float scaledEnd = Mathf.Max(0.02f, endScale) * prefabScale;
        spark.transform.localScale = Vector3.one * scaledStart;

        auraSparks.Add(new AuraSpark
        {
            Transform = spark.transform,
            Renderer = renderer,
            Kind = kind,
            StartWorldPos = startWorld,
            EndWorldPos = endWorld,
            DriftX = driftX,
            WobblePhase = wobblePhase,
            WobbleStrength = wobbleStrength,
            StartTime = Time.time,
            Duration = Mathf.Max(0.08f, duration),
            StartAlpha = startAlpha,
            Inward = inward,
            StartScale = scaledStart,
            EndScale = scaledEnd,
            BaseRgb = baseRgb
        });
    }

    private Color ApplyTint(Color baseColor, float alpha)
    {
        Color tint = Tint.a <= 0.001f ? Color.white : Tint;
        // White tint keeps Hex's authored yellow. Any other tint (Bass light pink)
        // replaces RGB so the aura reads clearly instead of multiplying into yellow.
        bool useTintColor = tint.r < 0.98f || tint.g < 0.98f || tint.b < 0.98f;
        if (!useTintColor)
            return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

        return new Color(tint.r, tint.g, tint.b, alpha);
    }

    private float PickSpawnX(int sideSign)
    {
        float inner = spawnSpreadX * sideZoneStart;
        if (sideSign != 0)
            return sideSign < 0 ? Random.Range(-spawnSpreadX, -inner) : Random.Range(inner, spawnSpreadX);

        // General spawns still bias toward the outer bands.
        if (Random.value < 0.72f)
            return Random.value < 0.5f ? Random.Range(-spawnSpreadX, -inner) : Random.Range(inner, spawnSpreadX);

        return Random.Range(-inner, inner);
    }

    private bool IsSideBand(float spawnX, int sideSign)
    {
        return sideSign != 0 || Mathf.Abs(spawnX) >= spawnSpreadX * sideZoneStart;
    }

    private void UpdateAuraSparks()
    {
        Vector3 followCenter = transform.position;
        followCenter.z = 0f;

        for (int i = auraSparks.Count - 1; i >= 0; i--)
        {
            AuraSpark spark = auraSparks[i];
            if (spark.Transform == null)
            {
                auraSparks.RemoveAt(i);
                continue;
            }

            float t = Mathf.Clamp01((Time.time - spark.StartTime) / spark.Duration);
            float eased = 1f - (1f - t) * (1f - t);

            Vector3 start = spark.StartWorldPos;
            Vector3 end = spark.EndWorldPos;
            if (spark.Inward)
            {
                // Keep the gather aimed at Bass as he moves.
                Vector3 startOffset = spark.StartWorldPos - spark.EndWorldPos;
                end = followCenter;
                start = followCenter + startOffset;
            }

            Vector3 pos = Vector3.Lerp(start, end, eased);
            if (!spark.Inward)
            {
                pos.x += spark.DriftX * eased;
                float wobbleFalloff = 1f - eased * 0.65f;
                pos.x += Mathf.Sin(t * Mathf.PI * wobbleFrequency + spark.WobblePhase)
                         * wobbleAmplitude * spark.WobbleStrength * wobbleFalloff;
            }

            spark.Transform.position = pos;

            if (spark.Inward)
            {
                float scale = Mathf.Lerp(spark.StartScale, spark.EndScale, eased);
                spark.Transform.localScale = Vector3.one * Mathf.Max(0.02f, scale);
            }

            if (spark.Renderer != null)
            {
                float alpha;
                if (spark.Inward)
                {
                    // Stay bright on the ring, fade only near the center (MM7-readable).
                    alpha = t <= inwardFadeStartNormalized
                        ? spark.StartAlpha
                        : Mathf.Lerp(spark.StartAlpha, 0f, Mathf.InverseLerp(inwardFadeStartNormalized, 1f, t));
                }
                else
                {
                    alpha = t <= fadeStartNormalized
                        ? spark.StartAlpha
                        : Mathf.Lerp(spark.StartAlpha, 0f, Mathf.InverseLerp(fadeStartNormalized, 1f, t));
                }

                if (dissipating)
                    alpha *= Mathf.Clamp01((dissipateEndTime - Time.time) / Mathf.Max(0.05f, dissipateDuration));

                spark.Renderer.color = ApplyTint(spark.BaseRgb, alpha);
            }

            if (t >= 1f)
            {
                Destroy(spark.Transform.gameObject);
                auraSparks.RemoveAt(i);
            }
        }
    }

    private void BeginDissipate()
    {
        ResetSpawnState();
        if (auraSparks.Count == 0)
        {
            dissipating = false;
            return;
        }

        dissipating = true;
        dissipateEndTime = Time.time + Mathf.Max(0.1f, dissipateDuration);
    }

    private int CountActiveOfKind(int kind)
    {
        int count = 0;
        for (int i = 0; i < auraSparks.Count; i++)
        {
            if (auraSparks[i].Kind == kind)
                count++;
        }

        return count;
    }

    private float ChargeSpawnScale()
    {
        float minScale = Mathf.Max(0.2f, spawnRateScaleAtMaxCharge);
        float maxScale = Mathf.Max(minScale, spawnRateScaleAtMinCharge);
        return Mathf.Lerp(maxScale, minScale, Mathf.Clamp01(ChargeProgress));
    }

    private void EnsureSpawnArray()
    {
        if (nextSpawnAt.Length != KindCount)
        {
            nextSpawnAt = new float[KindCount];
            ScheduleInitialSpawns();
        }
    }

    private void ScheduleInitialSpawns()
    {
        if (nextSpawnAt.Length != KindCount)
            nextSpawnAt = new float[KindCount];

        float now = Time.time;
        for (int i = 0; i < nextSpawnAt.Length; i++)
            nextSpawnAt[i] = now + Random.Range(0f, spawnIntervalFull * 0.35f);
        nextLeftSideSpawnAt = now + Random.Range(0f, sideSpawnIntervalFull * 0.25f);
        nextRightSideSpawnAt = now + Random.Range(0f, sideSpawnIntervalFull * 0.25f);
        nextInwardWaveAt = now;
    }

    private void ResetSpawnState()
    {
        if (nextSpawnAt.Length != KindCount)
            nextSpawnAt = new float[KindCount];

        for (int i = 0; i < nextSpawnAt.Length; i++)
            nextSpawnAt[i] = float.MaxValue;
        nextLeftSideSpawnAt = float.MaxValue;
        nextRightSideSpawnAt = float.MaxValue;
        nextInwardWaveAt = float.MaxValue;
        wasFullCharge = false;
    }

    private void ClearAura()
    {
        for (int i = 0; i < auraSparks.Count; i++)
        {
            if (auraSparks[i].Transform != null)
                Destroy(auraSparks[i].Transform.gameObject);
        }

        auraSparks.Clear();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        copiesPerTypeAtFullCharge = Mathf.Clamp(copiesPerTypeAtFullCharge, 1, 8);
        copiesPerTypeAtPartialCharge = Mathf.Clamp(copiesPerTypeAtPartialCharge, 1, 3);
        riseFromBottom = Mathf.Max(0f, riseFromBottom);
        riseToTop = Mathf.Max(0f, riseToTop);
        floatDuration = Mathf.Max(0.15f, floatDuration);
        spawnIntervalFull = Mathf.Max(0.02f, spawnIntervalFull);
        spawnIntervalPartial = Mathf.Max(0.05f, spawnIntervalPartial);
        sideSpawnIntervalFull = Mathf.Max(0.01f, sideSpawnIntervalFull);
        sideSpawnIntervalPartial = Mathf.Max(0.03f, sideSpawnIntervalPartial);
        spawnSpreadX = Mathf.Max(0.1f, spawnSpreadX);
        sideSpawnMaxY = Mathf.Max(sideSpawnMinY, sideSpawnMaxY);
        spawnHeightVariance = Mathf.Max(0f, spawnHeightVariance);
        endHeightVariance = Mathf.Max(0f, endHeightVariance);
        spawnIntervalJitter = Mathf.Max(0f, spawnIntervalJitter);
        horizontalDrift = Mathf.Max(0f, horizontalDrift);
        wobbleAmplitude = Mathf.Max(0f, wobbleAmplitude);
        wobbleFrequency = Mathf.Max(0.5f, wobbleFrequency);
        dissipateDuration = Mathf.Max(0.1f, dissipateDuration);
        inwardRadiusMin = Mathf.Max(0.5f, inwardRadiusMin);
        inwardRadiusMax = Mathf.Max(inwardRadiusMin, inwardRadiusMax);
        inwardDurationAtMinCharge = Mathf.Max(0.12f, inwardDurationAtMinCharge);
        inwardDurationAtMaxCharge = Mathf.Max(0.08f, inwardDurationAtMaxCharge);
        inwardWaveIntervalAtMin = Mathf.Max(0.12f, inwardWaveIntervalAtMin);
        inwardWaveIntervalAtMax = Mathf.Max(0.08f, inwardWaveIntervalAtMax);
        inwardRingCountMin = Mathf.Clamp(inwardRingCountMin, 4, 20);
        inwardRingCountMax = Mathf.Clamp(inwardRingCountMax, inwardRingCountMin, 24);
        inwardStartScale = Mathf.Max(0.1f, inwardStartScale);
        inwardEndScale = Mathf.Clamp(inwardEndScale, 0.02f, inwardStartScale);
        inwardFadeStartNormalized = Mathf.Clamp(inwardFadeStartNormalized, 0.35f, 0.95f);
    }
#endif
}
