using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs in boss scenes: return to HomeTown when the boss dies, or when the player
/// loses their last life. Mid-fight deaths with lives left use a Mega Man-style
/// fade / respawn (boss+crystal HP reset, controls locked until fade clears).
/// Place one in Boss Fight Mode (or it auto-adds via sceneLoaded).
/// </summary>
public class BossFightDirector : MonoBehaviour
{
    [SerializeField] private string homeTownSceneName = "HomeTown";
    [SerializeField] private float fadeOutSeconds = 0.45f;
    [SerializeField] private float fadeInSeconds = 0.45f;
    [SerializeField] private float returnDelayAfterBossDeath = 0.75f;
    [SerializeField] private float respawnDelayAfterDeath = 0.35f;
    [SerializeField] private float cameraCatchUpTimeout = 2.5f;
    [SerializeField] private float cameraCatchUpDistance = 0.65f;

    private bool flowBusy;
    private PlayerController watchedPlayer;
    private bool bossDeathHandled;
    private bool playerDeathHandled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= EnsureOnSceneLoaded;
        SceneManager.sceneLoaded += EnsureOnSceneLoaded;
    }

    private static void EnsureOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene(scene.name);
    }

    private static void EnsureForScene(string sceneName)
    {
        if (!IsBossFightScene(sceneName))
            return;

        if (FindFirstObjectByType<BossSpawner>() == null)
        {
            GameObject spawnerGo = new GameObject("BossSpawner");
            spawnerGo.AddComponent<BossSpawner>();
        }

        if (FindFirstObjectByType<BossFightDirector>() == null)
        {
            GameObject go = new GameObject("BossFightDirector");
            go.AddComponent<BossFightDirector>();
        }

        EnsureCrystalComponent();
    }

    private static void EnsureCrystalComponent()
    {
        Crystal existing = FindFirstObjectByType<Crystal>();
        if (existing != null)
            return;

        GameObject crystalGo = GameObject.Find("Crystal");
        if (crystalGo != null)
            crystalGo.AddComponent<Crystal>();
    }

    public static bool IsBossFightScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        return sceneName.IndexOf("Boss Fight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("BossFight", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsActiveBossFightScene()
    {
        return IsBossFightScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Temporary: Boss Fight spawn/respawn faces left until facing anims are ready.</summary>
    public static float GetDefaultBossFacingSign()
    {
        return -1f;
    }

    private void OnEnable()
    {
        bossDeathHandled = false;
        playerDeathHandled = false;
        flowBusy = false;
        BindBosses();
        BindPlayer();
    }

    private void OnDisable()
    {
        UnbindPlayer();
        UnbindBosses();
        StopAllCoroutines();
    }

    private void Start()
    {
        StartCoroutine(RebindAfterSceneReady());
        StartCoroutine(IntroWarningGate());
    }

    /// <summary>
    /// First load: hold player + boss until WARNING finishes (if one exists).
    /// </summary>
    private IEnumerator IntroWarningGate()
    {
        ApplyIntroLock();

        // Let spawners run their Start() and spawn the player.
        yield return null;
        ApplyIntroLock();

        WarningText warning = WarningText.FindInScene(includeInactive: true);
        if (warning == null)
        {
            ReleaseIntroLock();
            yield break;
        }

        if (!warning.IsPlaying && !warning.HasCompletedAtLeastOnce)
            warning.Play();

        while (warning != null && !warning.HasCompletedAtLeastOnce)
        {
            ApplyIntroLock();
            yield return null;
            if (warning == null)
                warning = WarningText.FindInScene(includeInactive: true);
        }

        // WarningText unlocks on finish; keep a clean release for anything spawned late.
        ReleaseIntroLock();

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(true);
    }

    private static void ApplyIntroLock()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].SetInputLocked(true);
        }

        SetAllBossesCombatPaused(true);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(false);
    }

    private static void ReleaseIntroLock()
    {
        // Only release if no warning is still blocking (respawn warning may overlap).
        if (WarningText.BlocksGameplay)
            return;

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].SetInputLocked(false);
        }

        SetAllBossesCombatPaused(false);
    }

    private void Update()
    {
        // Belt-and-suspenders: catch deaths even if an event subscription was missed.
        if (!flowBusy && !bossDeathHandled)
        {
            Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                if (bosses[i] != null && bosses[i].IsDead)
                {
                    HandleBossDied();
                    break;
                }
            }
        }

        if (!flowBusy && !playerDeathHandled && watchedPlayer != null && watchedPlayer.IsDead)
            HandlePlayerDied();
    }

    private IEnumerator RebindAfterSceneReady()
    {
        yield return null;
        BindBosses();
        BindPlayer();
        yield return null;
        BindBosses();
        BindPlayer();
    }

    private void BindBosses()
    {
        UnbindBosses();
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].OnDied += HandleBossDied;
        }
    }

    private void UnbindBosses()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].OnDied -= HandleBossDied;
        }
    }

    private void BindPlayer()
    {
        UnbindPlayer();

        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned += HandlePlayerSpawned;

        WatchPlayer(FindActivePlayer());
    }

    private void UnbindPlayer()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned -= HandlePlayerSpawned;

        if (watchedPlayer != null)
        {
            watchedPlayer.OnDied -= HandlePlayerDied;
            watchedPlayer = null;
        }
    }

    private void HandlePlayerSpawned(PlayerController player)
    {
        // Mid-fight life-loss flow owns re-binding after respawn.
        if (flowBusy)
            return;

        playerDeathHandled = false;
        WatchPlayer(player);
    }

    private void WatchPlayer(PlayerController player)
    {
        if (watchedPlayer != null)
            watchedPlayer.OnDied -= HandlePlayerDied;

        watchedPlayer = player;
        if (watchedPlayer != null)
            watchedPlayer.OnDied += HandlePlayerDied;
    }

    private void HandleBossDied()
    {
        if (flowBusy || bossDeathHandled)
            return;

        Boss defeated = FindDefeatedBoss();

        // Crystal still has HP → drain it and respawn the boss instead of ending the fight.
        if (Crystal.Instance != null &&
            defeated != null &&
            Crystal.Instance.TryHandleBossDeath(defeated))
        {
            flowBusy = true;
            StartCoroutine(CrystalBossRespawnFlow(defeated));
            return;
        }

        bossDeathHandled = true;
        flowBusy = true;
        StartCoroutine(ReturnHomeAfterDelay(returnDelayAfterBossDeath));
    }

    private IEnumerator CrystalBossRespawnFlow(Boss defeated)
    {
        if (Crystal.Instance != null)
            yield return Crystal.Instance.RespawnBossAfterDeath(defeated);

        // If the death penalty emptied the crystal during/after the wait, go home.
        if (Crystal.Instance == null || !Crystal.Instance.AllowsBossRespawn)
        {
            bossDeathHandled = true;
            ReturnToHomeTown();
            yield break;
        }

        flowBusy = false;
        bossDeathHandled = false;
        BindBosses();
    }

    private static Boss FindDefeatedBoss()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null && bosses[i].IsDead)
                return bosses[i];
        }

        return bosses.Length > 0 ? bosses[0] : null;
    }

    private void HandlePlayerDied()
    {
        if (flowBusy || playerDeathHandled)
            return;

        playerDeathHandled = true;
        flowBusy = true;

        if (watchedPlayer != null)
            watchedPlayer.SetInputLocked(true);

        SetAllBossesCombatPaused(true);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(false);

        PlayerInventory inv = PlayerInventory.Instance;
        if (inv != null)
            inv.TryLoseLife(1);

        int livesLeft = inv != null ? inv.LivesCount : 0;
        if (livesLeft <= 0)
        {
            if (inv != null)
                inv.ResetLivesToStarting();
            StartCoroutine(ReturnHomeAfterDelay(respawnDelayAfterDeath));
            return;
        }

        StartCoroutine(LifeLossRespawnFlow());
    }

    private IEnumerator LifeLossRespawnFlow()
    {
        // Wait until death balls have traveled 8 spaces before fading.
        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        ScreenFade fade = ScreenFade.EnsureExists();
        // Fade first — do not move player or boss until the screen is fully black.
        yield return StartCoroutine(fade.FadeToBlack(fadeOutSeconds));
        fade.SetBlackImmediate();
        yield return StartCoroutine(fade.WaitUntilFullyBlack());

        // Screen is fully black: now reset fight state and respawn both at spawn points.
        ResetBossAndCrystalForLifeLoss();

        PlayerController player = null;
        if (PlayerSpawner.Instance != null)
            player = PlayerSpawner.Instance.RespawnAtSpawnPoint();
        else
        {
            player = FindActivePlayer();
            if (player != null)
                player.SetHealth(player.MaxHealth);
        }

        if (player != null)
        {
            player.SetInputLocked(true);
            WatchPlayer(player);
        }

        // While black: point camera at the temporary track point (not the player).
        // Track that through fade-in + WARNING, then switch back to the player.
        yield return null;
        yield return null;

        Transform tempTrack = FindTemporaryTrackPoint();
        if (CameraFollow.Instance != null)
        {
            if (tempTrack != null)
            {
                CameraFollow.Instance.SnapToTarget(tempTrack);
                CameraFollow.Instance.SetFollowTarget(tempTrack);
                CameraFollow.Instance.SetFollowEnabled(true);
            }
            else
            {
                CameraFollow.Instance.SetFollowEnabled(false);
                if (player != null)
                    CameraFollow.Instance.SnapToTarget(player.transform);
            }
        }

        fade.SetBlackImmediate();
        yield return null;
        yield return StartCoroutine(fade.FadeFromBlack(fadeInSeconds));

        // Keep tracking the temp point through warning (do not freeze camera).
        yield return PlayWarningAfterRespawn();

        if (CameraFollow.Instance != null)
        {
            if (player != null)
                CameraFollow.Instance.SetFollowTarget(player.transform);
            CameraFollow.Instance.BeginRespawnCatchUp();
        }

        if (player != null)
            player.SetInputLocked(false);

        SetAllBossesCombatPaused(false);

        playerDeathHandled = false;
        flowBusy = false;
        BindPlayer();
    }

    private static IEnumerator PlayWarningAfterRespawn()
    {
        WarningText warning = WarningText.FindInScene(includeInactive: true);
        if (warning == null)
            yield break;

        // Camera is already tracking the temporary point — don't freeze it.
        yield return warning.PlayAndWait(freezeCamera: false);
    }

    private static Transform FindTemporaryTrackPoint()
    {
        GameObject found = GameObject.Find("temporary track point");
        if (found != null)
            return found.transform;

        // Case-insensitive fallback in case the name differs slightly.
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null &&
                string.Equals(all[i].name, "temporary track point", StringComparison.OrdinalIgnoreCase))
                return all[i];
        }

        return null;
    }

    private static void ResetBossAndCrystalForLifeLoss()
    {
        if (Crystal.Instance != null)
            Crystal.Instance.ResetToFull();

        Vector3 bossSpawn = ResolveBossRetrySpawnPosition();

        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            // Same idea as the player: full reset at spawn so the retry is fair.
            boss.ReviveFull(bossSpawn);
            boss.SetCombatPaused(true);
        }
    }

    private static Vector3 ResolveBossRetrySpawnPosition()
    {
        BossSpawner spawner = FindFirstObjectByType<BossSpawner>();
        if (spawner != null)
            return spawner.GetSpawnWorldPosition();

        if (Crystal.Instance != null)
        {
            // Fallback if a crystal respawn point exists but BossSpawner does not.
            Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
            if (bosses.Length > 0 && bosses[0] != null)
                return bosses[0].transform.position;
        }

        Boss any = FindFirstObjectByType<Boss>();
        return any != null ? any.transform.position : Vector3.zero;
    }

    private static void SetAllBossesCombatPaused(bool paused)
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].SetCombatPaused(paused);
        }
    }

    private IEnumerator ReturnHomeAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        // Wait until Mega Man death balls have traveled far enough from the center.
        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(true);

        ReturnToHomeTown();
    }

    private void ReturnToHomeTown()
    {
        string scene = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName;
        ScreenFade.EnsureExists().LoadScene(scene, fadeOutSeconds, fadeInSeconds);
        StartCoroutine(HardFallbackLoad(scene));
    }

    private IEnumerator HardFallbackLoad(string scene)
    {
        // If fade/queue somehow stalls, force the load so the player is never stuck.
        yield return new WaitForSecondsRealtime(Mathf.Max(1.25f, fadeOutSeconds + 1f));
        if (IsBossFightScene(SceneManager.GetActiveScene().name))
            SceneManager.LoadScene(scene);
    }

    private static PlayerController FindActivePlayer()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return FindFirstObjectByType<PlayerController>();
    }
}
