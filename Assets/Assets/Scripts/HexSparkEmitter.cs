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
    }

    private PlayerController player;
    private Boss boss;
    private SpriteRenderer body;
    private int lastHealth = -1;

    private readonly List<AuraSpark> auraSparks = new List<AuraSpark>(24);
    private float[] nextSpawnAt = new float[0];
    private float nextLeftSideSpawnAt;
    private float nextRightSideSpawnAt;
    private bool wasStreaming;
    private bool dissipating;
    private float dissipateEndTime;

    /// <summary>While true, the charge aura spawns sparks around her.</summary>
    public bool Streaming { get; set; }

    /// <summary>0–1. Higher = faster spawning (like Count's charge progress).</summary>
    public float ChargeProgress { get; set; }

    /// <summary>Full charge: denser aura with all three spark types.</summary>
    public bool FullCharge { get; set; }

    private SortingGroup HostGroup =>
        player != null ? player.EffectSortingGroup : boss != null ? boss.EffectSortingGroup : null;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        boss = GetComponent<Boss>();
        body = GetComponent<SpriteRenderer>();
        if (sparkBox == null)
            sparkBox = transform.Find("Spark box");
        ResetSpawnState();
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
        dissipating = false;
        ClearAura();
    }

    private void HandleHealthChanged(int current, int max)
    {
        if (lastHealth >= 0 && current < lastHealth)
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

        wasStreaming = Streaming;
        if (Streaming)
        {
            TrySpawnAura();
            TrySpawnSideStreams();
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

        HexSpark spark = Instantiate(prefab, startWorld, Quaternion.identity);
        // The aura drives the spark; its own pop-and-fall motion stays off.
        spark.enabled = false;
        SpriteRenderer renderer = spark.Renderer != null ? spark.Renderer : spark.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            CharacterEffectSorting.ApplyDetachedEffectNearHost(renderer, HostGroup, body, sortingOffset);
            Color c = renderer.color;
            c.a = startAlpha;
            renderer.color = c;
        }

        auraSparks.Add(new AuraSpark
        {
            Transform = spark.transform,
            Renderer = renderer,
            Kind = kind,
            StartWorldPos = startWorld,
            EndWorldPos = endWorld,
            DriftX = Random.Range(-horizontalDrift * 0.5f, horizontalDrift * 0.5f),
            WobblePhase = Random.Range(0f, Mathf.PI * 2f),
            WobbleStrength = Random.Range(0.65f, 1.35f),
            StartTime = Time.time,
            Duration = floatDuration * Random.Range(0.65f, 1.35f),
            StartAlpha = startAlpha
        });
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

            Vector3 pos = Vector3.Lerp(spark.StartWorldPos, spark.EndWorldPos, eased);
            pos.x += spark.DriftX * eased;
            float wobbleFalloff = 1f - eased * 0.65f;
            pos.x += Mathf.Sin(t * Mathf.PI * wobbleFrequency + spark.WobblePhase)
                     * wobbleAmplitude * spark.WobbleStrength * wobbleFalloff;
            spark.Transform.position = pos;

            if (spark.Renderer != null)
            {
                float alpha = t <= fadeStartNormalized
                    ? spark.StartAlpha
                    : Mathf.Lerp(spark.StartAlpha, 0f, Mathf.InverseLerp(fadeStartNormalized, 1f, t));
                if (dissipating)
                    alpha *= Mathf.Clamp01((dissipateEndTime - Time.time) / Mathf.Max(0.05f, dissipateDuration));

                Color c = spark.Renderer.color;
                c.a = alpha;
                spark.Renderer.color = c;
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
    }

    private void ResetSpawnState()
    {
        if (nextSpawnAt.Length != KindCount)
            nextSpawnAt = new float[KindCount];

        for (int i = 0; i < nextSpawnAt.Length; i++)
            nextSpawnAt[i] = float.MaxValue;
        nextLeftSideSpawnAt = float.MaxValue;
        nextRightSideSpawnAt = float.MaxValue;
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
    }
#endif
}
