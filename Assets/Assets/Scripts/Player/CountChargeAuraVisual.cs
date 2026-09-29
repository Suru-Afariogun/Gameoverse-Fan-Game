using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dual Override / MM11-style charge particles for Count: sporadic red balls and lines
/// bloom around his silhouette, drift left/right while rising, and fade out in front of the body.
/// Spawned in world space so they can trail behind when Count moves.
/// </summary>
public class CountChargeAuraVisual : MonoBehaviour
{
    private enum AuraPieceKind
    {
        BallSmall,
        LineSmall,
        BallMedium,
        LineMedium
    }

    [Header("Aura Prefabs (Visual effects)")]
    [SerializeField] private GameObject auraBallSmallPrefab;
    [SerializeField] private GameObject auraBallMediumPrefab;
    [SerializeField] private GameObject auraLineSmallPrefab;
    [SerializeField] private GameObject auraLineMediumPrefab;

    [Header("Float Path")]
    [SerializeField] private float riseFromBottom = 1f;
    [SerializeField] private float riseToTop = 3f;
    [SerializeField] private float floatDuration = 1.1f;

    [Header("Spawn Volume (around Count)")]
    [SerializeField] private float spawnSpreadX = 1.35f;
    [Tooltip("Some particles start partway up the body instead of only at the feet.")]
    [SerializeField] private float spawnHeightVariance = 0.85f;
    [Tooltip("End height varies so some pop out higher/lower above him.")]
    [SerializeField] private float endHeightVariance = 0.45f;
    [Tooltip("Outer band of spawnSpreadX treated as the left/right side zones.")]
    [SerializeField] [Range(0.35f, 0.9f)] private float sideZoneStart = 0.5f;
    [Tooltip("Side-band spawns only: minimum height (torso). Never feet level.")]
    [SerializeField] private float sideSpawnMinY = 0f;
    [Tooltip("Side-band spawns only: maximum height before they begin floating up.")]
    [SerializeField] private float sideSpawnMaxY = 1f;

    [Header("Spawn Density")]
    [SerializeField] private int copiesPerTypeAtFullCharge = 4;
    [SerializeField] private int copiesPerTypeAtPartialCharge = 1;
    [SerializeField] private float spawnIntervalFull = 0.04f;
    [SerializeField] private float spawnIntervalPartial = 0.14f;
    [Tooltip("Extra-fast spawn cadence for dedicated left/right side streams.")]
    [SerializeField] private float sideSpawnIntervalFull = 0.018f;
    [SerializeField] private float sideSpawnIntervalPartial = 0.07f;
    [Tooltip("Randomizes when each type fires so they do not stack in one vertical line.")]
    [SerializeField] private float spawnIntervalJitter = 0.05f;
    [Tooltip("Chance to spawn an extra piece on the same frame at full charge.")]
    [SerializeField] [Range(0f, 0.45f)] private float burstSpawnChance = 0.22f;

    [Header("Charge Scaling")]
    [Tooltip("At minimum charge, spawn intervals are multiplied by this (slower).")]
    [SerializeField] private float spawnRateScaleAtMinCharge = 2.4f;
    [Tooltip("At max charge, spawn intervals are multiplied by this (faster).")]
    [SerializeField] private float spawnRateScaleAtMaxCharge = 0.55f;

    [Header("Dissipate")]
    [SerializeField] private float dissipateDuration = 0.45f;

    [Header("Drift / Wobble")]
    [SerializeField] private float horizontalDrift = 0.65f;
    [SerializeField] private float wobbleAmplitude = 0.32f;
    [SerializeField] private float wobbleFrequency = 2.6f;

    [Header("Fade")]
    [SerializeField] private float startAlphaFull = 0.92f;
    [SerializeField] private float startAlphaPartial = 0.55f;
    [Tooltip("Stay fully visible until this fraction of the rise is complete, then fade out.")]
    [SerializeField] [Range(0.5f, 0.98f)] private float fadeStartNormalized = 0.82f;

    private SpriteRenderer bodyRenderer;
    private SortingGroup hostSortingGroup;

    private readonly List<AuraParticle> activeParticles = new List<AuraParticle>(24);
    private readonly float[] nextSpawnAt = new float[4];

    private float nextLeftSideSpawnAt;
    private float nextRightSideSpawnAt;

    private bool visible;
    private bool fullCharge;
    private float activityMultiplier = 1f;
    private float chargeProgress01;
    private bool dissipating;
    private float dissipateEndTime;
    private float dissipateStartMultiplier = 1f;

    private struct AuraParticle
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public AuraPieceKind Kind;
        public Vector3 StartWorldPos;
        public Vector3 EndWorldPos;
        public float DriftX;
        public float WobblePhase;
        public float WobbleStrength;
        public float StartTime;
        public float Duration;
        public float StartAlpha;
        public bool SpawnFlipX;
        public int SortingOrder;
    }

    private void Awake()
    {
        bodyRenderer = GetComponentInParent<SpriteRenderer>();
        PlayerController host = GetComponentInParent<PlayerController>();
        if (host != null)
            hostSortingGroup = host.EffectSortingGroup;

        ResetSpawnState();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        ClearAllParticles();
    }

    private void Update()
    {
        if (!visible && !dissipating)
            return;

        EnsureHostReferences();
        if (visible)
        {
            TrySpawnParticles();
            TrySpawnSideParticles();
        }

        UpdateActiveParticles();
        TickDissipate();
    }

    public void SetVisible(bool show, bool instant = false)
    {
        if (visible == show && !dissipating)
            return;

        if (!show)
        {
            if (!instant && (dissipating || activeParticles.Count > 0))
            {
                BeginDissipate();
                return;
            }

            visible = false;
            dissipating = false;
            dissipateEndTime = 0f;
            fullCharge = false;
            chargeProgress01 = 0f;
            ClearAllParticles();
            ResetSpawnState();
            return;
        }

        dissipating = false;
        dissipateEndTime = 0f;
        visible = show;
        ScheduleInitialSpawns();
    }

    public void BeginDissipate(float duration = -1f)
    {
        if (duration <= 0f)
            duration = dissipateDuration;

        visible = false;
        fullCharge = false;
        chargeProgress01 = 0f;
        ResetSpawnState();

        if (activeParticles.Count == 0)
        {
            dissipating = false;
            ClearAllParticles();
            return;
        }

        dissipating = true;
        dissipateStartMultiplier = activityMultiplier;
        dissipateEndTime = Time.time + Mathf.Max(0.1f, duration);
    }

    public void SetChargeProgress(float progress01)
    {
        chargeProgress01 = Mathf.Clamp01(progress01);
    }

    public void SetFullCharge(bool isFull)
    {
        fullCharge = isFull;
    }

    /// <summary>1 = normal spawn rate. Lower values slow/sparsify aura (hyper speed running out).</summary>
    public void SetActivityMultiplier(float multiplier)
    {
        activityMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
    }

    private void TrySpawnParticles()
    {
        float chargeScale = GetChargeSpawnScale();
        float baseInterval = (fullCharge ? spawnIntervalFull : spawnIntervalPartial) * chargeScale;
        baseInterval /= Mathf.Max(0.05f, activityMultiplier);
        float startAlpha = fullCharge ? startAlphaFull : startAlphaPartial;
        int maxPerType = fullCharge
            ? copiesPerTypeAtFullCharge + Mathf.RoundToInt(chargeProgress01 * 2f)
            : copiesPerTypeAtPartialCharge + (chargeProgress01 > 0.35f ? 1 : 0);

        TrySpawnType(AuraPieceKind.BallSmall, auraBallSmallPrefab, baseInterval, maxPerType, startAlpha, true, forceSide: false);
        TrySpawnType(AuraPieceKind.LineSmall, auraLineSmallPrefab, baseInterval, maxPerType, startAlpha, true, forceSide: false);

        if (!fullCharge)
            return;

        TrySpawnType(AuraPieceKind.BallMedium, auraBallMediumPrefab, baseInterval, maxPerType, startAlpha, true, forceSide: false);
        TrySpawnType(AuraPieceKind.LineMedium, auraLineMediumPrefab, baseInterval, maxPerType, startAlpha, true, forceSide: false);
    }

    private void TrySpawnSideParticles()
    {
        float chargeScale = GetChargeSpawnScale();
        float interval = (fullCharge ? sideSpawnIntervalFull : sideSpawnIntervalPartial) * chargeScale;
        interval /= Mathf.Max(0.05f, activityMultiplier);
        float startAlpha = fullCharge ? startAlphaFull : startAlphaPartial;
        int maxPerType = fullCharge ? copiesPerTypeAtFullCharge : copiesPerTypeAtPartialCharge;

        if (Time.time >= nextLeftSideSpawnAt)
        {
            TrySpawnSideStream(-1, interval, maxPerType, startAlpha);
            nextLeftSideSpawnAt = Time.time + interval + Random.Range(-spawnIntervalJitter * 0.5f, spawnIntervalJitter * 0.5f);
        }

        if (Time.time >= nextRightSideSpawnAt)
        {
            TrySpawnSideStream(1, interval, maxPerType, startAlpha);
            nextRightSideSpawnAt = Time.time + interval + Random.Range(-spawnIntervalJitter * 0.5f, spawnIntervalJitter * 0.5f);
        }
    }

    private void TrySpawnSideStream(int sideSign, float interval, int maxPerType, float startAlpha)
    {
        if (!TryPickSideSpawn(out AuraPieceKind kind, out GameObject prefab, maxPerType, fullCharge))
            return;

        SpawnParticle(kind, prefab, startAlpha, forceSide: true, sideSign: sideSign);

        if (fullCharge && Random.value < burstSpawnChance &&
            TryPickSideSpawn(out kind, out prefab, maxPerType, true))
        {
            SpawnParticle(kind, prefab, startAlpha * Random.Range(0.75f, 1f), forceSide: true, sideSign: sideSign);
        }
    }

    private bool TryPickSideSpawn(out AuraPieceKind kind, out GameObject prefab, int maxPerType, bool allowMedium)
    {
        kind = AuraPieceKind.BallSmall;
        prefab = null;

        int optionCount = allowMedium ? 4 : 2;
        int start = Random.Range(0, optionCount);
        for (int i = 0; i < optionCount; i++)
        {
            AuraPieceKind candidate = (AuraPieceKind)((start + i) % optionCount);
            GameObject candidatePrefab = GetPrefab(candidate);
            if (candidatePrefab == null || CountActiveOfKind(candidate) >= maxPerType)
                continue;

            kind = candidate;
            prefab = candidatePrefab;
            return true;
        }

        return false;
    }

    private void TrySpawnType(
        AuraPieceKind kind,
        GameObject prefab,
        float baseInterval,
        int maxPerType,
        float startAlpha,
        bool allowed,
        bool forceSide)
    {
        if (!allowed || prefab == null)
            return;

        int kindIndex = (int)kind;
        if (Time.time < nextSpawnAt[kindIndex])
            return;

        if (CountActiveOfKind(kind) >= maxPerType)
        {
            nextSpawnAt[kindIndex] = Time.time + Random.Range(0.02f, baseInterval * 0.5f);
            return;
        }

        SpawnParticle(kind, prefab, startAlpha, forceSide, sideSign: 0);
        ScheduleNextSpawn(kindIndex, baseInterval);

        if (fullCharge && Random.value < burstSpawnChance && CountActiveOfKind(kind) < maxPerType)
            SpawnParticle(kind, prefab, startAlpha * Random.Range(0.75f, 1f), forceSide, sideSign: 0);
    }

    private GameObject GetPrefab(AuraPieceKind kind)
    {
        return kind switch
        {
            AuraPieceKind.BallSmall => auraBallSmallPrefab,
            AuraPieceKind.LineSmall => auraLineSmallPrefab,
            AuraPieceKind.BallMedium => auraBallMediumPrefab,
            AuraPieceKind.LineMedium => auraLineMediumPrefab,
            _ => null
        };
    }

    private void ScheduleNextSpawn(int kindIndex, float baseInterval)
    {
        float jitter = Random.Range(-spawnIntervalJitter, spawnIntervalJitter);
        nextSpawnAt[kindIndex] = Time.time + Mathf.Max(0.02f, baseInterval + jitter);
    }

    private void ScheduleInitialSpawns()
    {
        float now = Time.time;
        for (int i = 0; i < nextSpawnAt.Length; i++)
            nextSpawnAt[i] = now + Random.Range(0f, spawnIntervalFull * 0.35f);

        nextLeftSideSpawnAt = now + Random.Range(0f, sideSpawnIntervalFull * 0.25f);
        nextRightSideSpawnAt = now + Random.Range(0f, sideSpawnIntervalFull * 0.25f);
    }

    private void EnsureHostReferences()
    {
        if (bodyRenderer == null)
            bodyRenderer = GetComponentInParent<SpriteRenderer>();

        if (hostSortingGroup == null)
        {
            PlayerController host = GetComponentInParent<PlayerController>();
            if (host != null)
                hostSortingGroup = host.EffectSortingGroup;
        }
    }

    private void SpawnParticle(
        AuraPieceKind kind,
        GameObject prefab,
        float startAlpha,
        bool forceSide,
        int sideSign)
    {
        EnsureHostReferences();

        float spawnX = PickSpawnX(forceSide, sideSign);
        float startY = PickStartY(spawnX, sideSign);
        float endY = Random.Range(riseToTop * 0.2f, riseToTop + endHeightVariance);
        float endX = spawnX + Random.Range(-horizontalDrift, horizontalDrift);

        Vector3 startWorld = LocalOffsetToWorld(new Vector3(spawnX, startY, 0f));
        Vector3 endWorld = LocalOffsetToWorld(new Vector3(endX, endY, 0f));
        bool spawnFlipX = bodyRenderer != null && bodyRenderer.flipX;
        int sortingOrder = ResolveDetachedSortingOrder();

        GameObject instance = Instantiate(prefab, startWorld, Quaternion.identity);
        instance.name = $"{prefab.name}_fx";
        instance.transform.localScale = Vector3.one;
        instance.transform.rotation = Quaternion.identity;

        SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer == null)
            renderer = instance.GetComponentInChildren<SpriteRenderer>();

        ApplyParticleSorting(renderer, spawnFlipX, sortingOrder);

        Color c = renderer != null ? renderer.color : Color.white;
        c.a = startAlpha;
        if (renderer != null)
            renderer.color = c;

        activeParticles.Add(new AuraParticle
        {
            Transform = instance.transform,
            Renderer = renderer,
            Kind = kind,
            StartWorldPos = startWorld,
            EndWorldPos = endWorld,
            DriftX = Random.Range(-horizontalDrift * 0.5f, horizontalDrift * 0.5f),
            WobblePhase = Random.Range(0f, Mathf.PI * 2f),
            WobbleStrength = Random.Range(0.65f, 1.35f),
            StartTime = Time.time,
            Duration = floatDuration * Random.Range(0.65f, 1.35f),
            StartAlpha = startAlpha,
            SpawnFlipX = spawnFlipX,
            SortingOrder = sortingOrder
        });
    }

    private Vector3 LocalOffsetToWorld(Vector3 localOffset)
    {
        localOffset.x *= GetFacingSign();
        return transform.position + localOffset;
    }

    private float GetFacingSign()
    {
        if (bodyRenderer != null)
            return bodyRenderer.flipX ? -1f : 1f;

        return 1f;
    }

    private int ResolveDetachedSortingOrder()
    {
        if (hostSortingGroup != null)
            return hostSortingGroup.sortingOrder + 5;

        if (bodyRenderer != null)
            return bodyRenderer.sortingOrder + 1;

        return 0;
    }

    private void ApplyParticleSorting(SpriteRenderer renderer, bool flipX, int sortingOrder)
    {
        if (renderer == null)
            return;

        if (hostSortingGroup != null)
            renderer.sortingLayerID = hostSortingGroup.sortingLayerID;
        else if (bodyRenderer != null)
            renderer.sortingLayerID = bodyRenderer.sortingLayerID;

        renderer.sortingOrder = sortingOrder;
        renderer.flipX = flipX;
    }

    private float PickSpawnX(bool forceSide, int sideSign)
    {
        float inner = spawnSpreadX * sideZoneStart;

        if (forceSide && sideSign != 0)
        {
            return sideSign < 0
                ? Random.Range(-spawnSpreadX, -inner)
                : Random.Range(inner, spawnSpreadX);
        }

        if (forceSide)
        {
            bool left = Random.value < 0.5f;
            return left
                ? Random.Range(-spawnSpreadX, -inner)
                : Random.Range(inner, spawnSpreadX);
        }

        // General spawns still bias toward the outer bands.
        if (Random.value < 0.72f)
        {
            bool left = Random.value < 0.5f;
            return left
                ? Random.Range(-spawnSpreadX, -inner)
                : Random.Range(inner, spawnSpreadX);
        }

        return Random.Range(-inner, inner);
    }

    private float PickStartY(float spawnX, int sideSign)
    {
        if (IsSideBandSpawn(spawnX, sideSign))
            return Random.Range(sideSpawnMinY, sideSpawnMaxY);

        return Random.Range(-riseFromBottom, -riseFromBottom + spawnHeightVariance);
    }

    private bool IsSideBandSpawn(float spawnX, int sideSign)
    {
        if (sideSign != 0)
            return true;

        float inner = spawnSpreadX * sideZoneStart;
        return Mathf.Abs(spawnX) >= inner;
    }

    private void UpdateActiveParticles()
    {
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            AuraParticle particle = activeParticles[i];
            if (particle.Transform == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            float t = Mathf.Clamp01((Time.time - particle.StartTime) / particle.Duration);
            float eased = EaseOutQuad(t);

            Vector3 pos = Vector3.Lerp(particle.StartWorldPos, particle.EndWorldPos, eased);
            pos.x += particle.DriftX * eased;

            float wobbleFalloff = 1f - eased * 0.65f;
            pos.x += Mathf.Sin(t * Mathf.PI * wobbleFrequency + particle.WobblePhase)
                     * wobbleAmplitude * particle.WobbleStrength * wobbleFalloff;

            particle.Transform.position = pos;
            particle.Transform.rotation = Quaternion.identity;

            if (particle.Renderer != null)
            {
                ApplyParticleSorting(particle.Renderer, particle.SpawnFlipX, particle.SortingOrder);
                Color c = particle.Renderer.color;
                float fadeAlpha = EvaluateFadeAlpha(eased, particle.StartAlpha);
                if (dissipating && dissipateEndTime > 0f)
                {
                    float dissipateT = Mathf.Clamp01((dissipateEndTime - Time.time) / Mathf.Max(0.05f, dissipateDuration));
                    fadeAlpha *= dissipateT * dissipateStartMultiplier;
                }

                c.a = fadeAlpha;
                particle.Renderer.color = c;
            }

            if (t >= 1f)
            {
                Destroy(particle.Transform.gameObject);
                activeParticles.RemoveAt(i);
            }
            else
            {
                activeParticles[i] = particle;
            }
        }
    }

    private int CountActiveOfKind(AuraPieceKind kind)
    {
        int count = 0;
        for (int i = 0; i < activeParticles.Count; i++)
        {
            if (activeParticles[i].Kind == kind)
                count++;
        }

        return count;
    }

    private void ClearAllParticles()
    {
        for (int i = 0; i < activeParticles.Count; i++)
        {
            if (activeParticles[i].Transform != null)
                Destroy(activeParticles[i].Transform.gameObject);
        }

        activeParticles.Clear();
    }

    private void ResetSpawnState()
    {
        for (int i = 0; i < nextSpawnAt.Length; i++)
            nextSpawnAt[i] = float.MaxValue;

        nextLeftSideSpawnAt = float.MaxValue;
        nextRightSideSpawnAt = float.MaxValue;
    }

    private float GetChargeSpawnScale()
    {
        float minScale = Mathf.Max(0.2f, spawnRateScaleAtMaxCharge);
        float maxScale = Mathf.Max(minScale, spawnRateScaleAtMinCharge);
        return Mathf.Lerp(maxScale, minScale, chargeProgress01);
    }

    private void TickDissipate()
    {
        if (!dissipating)
            return;

        if (Time.time >= dissipateEndTime && activeParticles.Count == 0)
        {
            dissipating = false;
            dissipateEndTime = 0f;
            return;
        }

        if (Time.time >= dissipateEndTime + 0.05f)
        {
            ClearAllParticles();
            dissipating = false;
            dissipateEndTime = 0f;
        }
    }

    private float EvaluateFadeAlpha(float travelT, float startAlpha)
    {
        if (travelT <= fadeStartNormalized)
            return startAlpha;

        float fadeT = Mathf.InverseLerp(fadeStartNormalized, 1f, travelT);
        return Mathf.Lerp(startAlpha, 0f, fadeT);
    }

    private static float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t);
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
        sideZoneStart = Mathf.Clamp(sideZoneStart, 0.35f, 0.9f);
        sideSpawnMaxY = Mathf.Max(sideSpawnMinY, sideSpawnMaxY);
        spawnHeightVariance = Mathf.Max(0f, spawnHeightVariance);
        endHeightVariance = Mathf.Max(0f, endHeightVariance);
        spawnIntervalJitter = Mathf.Max(0f, spawnIntervalJitter);
        horizontalDrift = Mathf.Max(0f, horizontalDrift);
        wobbleAmplitude = Mathf.Max(0f, wobbleAmplitude);
        wobbleFrequency = Mathf.Max(0.5f, wobbleFrequency);
        fadeStartNormalized = Mathf.Clamp(fadeStartNormalized, 0.5f, 0.98f);
    }
#endif
}
