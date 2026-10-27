using System.Collections;
using UnityEngine;

/// <summary>
/// Reusable Midnight + Rewind Rush choreography for Count-family bosses.
/// Midnight: drastic player/world slow while Count stays full-speed (or boosts), chases/attacks,
/// and spawns clock hands at real-time pace. Hands only crawl after they launch.
/// </summary>
public static class CountSignatureTimeModule
{
    public const float DefaultDrasticSlowScale = 0.07f;

    public interface IHost
    {
        IEnumerator ClockWindup(float seconds, int tickPairs);
        void BeginDrasticTimeSlow(float worldScale);
        void EndDrasticTimeSlow();
        void BeginTimeTintOnly();
        void EndTimeTintOnly();
        Bounds PlayerAimBounds();
        bool HasLongHandPrefab { get; }
        void SpawnOrbitHand(float angleDegrees, float scale);
        void ClearOrbitHands();
        void LaunchOrbitHand(int index, float speed, int damage);
        int OrbitHandCount { get; }
        void SetOrbitCenter(Vector3 center, float radius, float degreesPerSecond);
        void PlayClockTick(int index);
        void FacePlayer();
        void PlayShootAnim();
        void StopMotion();
        /// <summary>Chase / shoot during Midnight while hands spawn. Boss stays full-speed.</summary>
        void TickMidnightCombat(float dt);
        /// <summary>Optionally kick a temporary self-speed boost during Midnight.</summary>
        void MaybeRollMidnightSpeedBurst();
        void BeginMidnightPhase();
        void EndMidnightPhase();
        /// <summary>
        /// Escape lane for this Midnight wave (world degrees, same basis as SpawnOrbitHand).
        /// Count should pressure the opposite side so the player can flee through the gap.
        /// </summary>
        void SetMidnightEscapeOpening(float escapeAngleDegrees, Vector2 orbitCenter);
        float BossDt { get; }
        IEnumerator WaitRaw(float seconds);
        IEnumerator WaitScaled(float seconds);
        int ScaleDamage(int baseDamage);
        IEnumerator RushTelegraph(float seconds);
        IEnumerator PerformRush(float dir, float distance, float speed, int damage, System.Collections.Generic.List<Vector2> pathRecorder);
        void TeleportTo(Vector2 position);
        Rigidbody2D Body { get; }
        float DxToPlayer();
        float FacingSign { get; }
    }

    public static IEnumerator Midnight(
        IHost host,
        float windupSeconds,
        float radius,
        float handScale,
        float handSpawnGap,
        float frozenSeconds,
        float fireDelay,
        float fireGap,
        float handSpeed,
        int handDamage,
        float drasticSlowScale,
        float speedMul = 1f)
    {
        if (host == null || !host.HasLongHandPrefab)
            yield break;

        float mul = Mathf.Max(0.25f, speedMul);
        // Midnight should feel snappy — windup is short and spawn gaps are near-instant.
        float windup = Mathf.Max(0.05f, windupSeconds * 0.45f) / mul;
        yield return host.ClockWindup(windup, 2);

        Bounds aim = host.PlayerAimBounds();
        host.BeginMidnightPhase();
        host.BeginDrasticTimeSlow(drasticSlowScale);
        host.SetOrbitCenter(aim.center, Mathf.Max(1f, radius), 0f);
        host.MaybeRollMidnightSpeedBurst();

        // One clear escape lane (center hour ± neighbors). Opposite side stays filled so Count
        // can pressure there — player has a fair path out.
        int gapHour = Random.Range(0, 12);
        int gapLeft = (gapHour + 11) % 12;
        int gapRight = (gapHour + 1) % 12;
        float escapeAngleDeg = 90f - 30f * gapHour;
        host.SetMidnightEscapeOpening(escapeAngleDeg, aim.center);

        // Spawn clock hands at real-time pace (WaitRaw uses host full-speed dt while he owns the slow).
        float spawnGap = Mathf.Max(0.008f, handSpawnGap * 0.35f) / mul;
        float phaseElapsed = 0f;
        for (int hour = 0; hour < 12; hour++)
        {
            if (hour == gapHour || hour == gapLeft || hour == gapRight)
                continue;

            host.SpawnOrbitHand(90f - 30f * hour, handScale);
            host.PlayClockTick(hour);
            float waited = 0f;
            while (waited < spawnGap)
            {
                float dt = host.BossDt;
                waited += dt;
                phaseElapsed += dt;
                host.TickMidnightCombat(dt);
                if (Random.value < 0.04f)
                    host.MaybeRollMidnightSpeedBurst();
                yield return null;
            }
        }

        float hold = Mathf.Max(0.05f, frozenSeconds * 0.55f) / mul;
        while (phaseElapsed < hold)
        {
            float dt = host.BossDt;
            phaseElapsed += dt;
            host.TickMidnightCombat(dt);
            if (Random.value < 0.035f)
                host.MaybeRollMidnightSpeedBurst();
            yield return null;
        }

        // Keep world slow through the fire so launched hands crawl; Count still moves/attacks.
        float delay = Mathf.Max(0f, fireDelay * 0.4f) / mul;
        float delayT = 0f;
        while (delayT < delay)
        {
            float dt = host.BossDt;
            delayT += dt;
            host.TickMidnightCombat(dt);
            yield return null;
        }

        float gap = Mathf.Max(0.01f, fireGap * 0.55f) / mul;
        for (int i = 0; i < host.OrbitHandCount; i++)
        {
            host.LaunchOrbitHand(i, handSpeed, host.ScaleDamage(handDamage));
            host.PlayClockTick(i);
            if (i % 3 == 0)
                host.PlayShootAnim();

            float g = 0f;
            while (g < gap)
            {
                float dt = host.BossDt;
                g += dt;
                host.TickMidnightCombat(dt);
                yield return null;
            }
        }

        host.ClearOrbitHands();
        host.EndDrasticTimeSlow();
        host.EndMidnightPhase();
        host.StopMotion();
        // Hard gets a longer post-wave window so the player can slip the escape lane.
        float endHold = Mathf.Max(0.05f, DifficultyTuning.BossMidnightPostWaveHold());
        yield return host.WaitRaw(endHold);
    }

    /// <summary>Hard mode: five Midnights back-to-back, each faster, then caller runs Rewind Rush.</summary>
    public static IEnumerator MidnightChain(
        IHost host,
        float windupSeconds,
        float radius,
        float handScale,
        float handSpawnGap,
        float frozenSeconds,
        float fireDelay,
        float fireGap,
        float handSpeed,
        int handDamage,
        float drasticSlowScale,
        int repeats = 5)
    {
        int count = Mathf.Max(1, repeats);
        for (int i = 0; i < count; i++)
        {
            float speedMul = 1.15f + i * 0.4f;
            yield return Midnight(
                host, windupSeconds, radius, handScale, handSpawnGap, frozenSeconds,
                fireDelay, fireGap, handSpeed, handDamage, drasticSlowScale, speedMul);
        }
    }

    public static IEnumerator RewindRush(
        IHost host,
        int dashes,
        float rushSpeed,
        float rushPause,
        float rewindPlaybackSeconds,
        int rushDamage,
        float rushOvershoot,
        float rushMinDistance,
        float rushMaxDistance,
        float rushWindupSeconds)
    {
        if (host == null || host.Body == null)
            yield break;

        yield return host.RushTelegraph(rushWindupSeconds);

        var path = new System.Collections.Generic.List<Vector2>(64);
        var dashLengths = new System.Collections.Generic.List<float>(Mathf.Max(1, dashes));
        int count = Mathf.Max(1, dashes);
        for (int i = 0; i < count; i++)
        {
            float dx = host.DxToPlayer();
            float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : host.FacingSign;
            float distance = Mathf.Clamp(Mathf.Abs(dx) + rushOvershoot, rushMinDistance, rushMaxDistance);
            float x0 = host.Body.position.x;
            yield return host.PerformRush(dir, distance, rushSpeed, host.ScaleDamage(rushDamage), path);
            dashLengths.Add(host.Body.position.x - x0);
            yield return host.RushTelegraph(rushPause);
        }

        host.BeginTimeTintOnly();
        SoundManager.Instance?.PlayClockworkToc();
        float playback = Mathf.Max(0.1f, rewindPlaybackSeconds);
        float t = 0f;
        int samples = path.Count;
        while (t < playback && samples > 0)
        {
            t += host.BossDt;
            int index = Mathf.Clamp(
                Mathf.RoundToInt((1f - Mathf.Clamp01(t / playback)) * (samples - 1)), 0, samples - 1);
            host.TeleportTo(path[index]);
            yield return null;
        }

        if (samples > 0)
            host.TeleportTo(path[0]);
        host.EndTimeTintOnly();

        yield return host.RushTelegraph(rushPause * 1.5f);
        for (int i = 0; i < dashLengths.Count; i++)
        {
            float length = dashLengths[i];
            if (Mathf.Abs(length) < 0.2f)
                continue;

            yield return host.PerformRush(
                -Mathf.Sign(length), Mathf.Abs(length), rushSpeed, host.ScaleDamage(rushDamage), null);
            if (i < dashLengths.Count - 1)
                yield return host.RushTelegraph(rushPause);
        }

        yield return host.WaitScaled(0.15f);
    }
}
