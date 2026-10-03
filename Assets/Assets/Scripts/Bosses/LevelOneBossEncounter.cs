using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Level one / Tutorial copy-bot encounter:
/// Level one: door close → WARNING → copy-bot spawn → HP bar fill → fight.
/// Tutorial: Boss Battle Starter pass → same spawn pipeline.
/// </summary>
[DefaultExecutionOrder(-900)]
public class LevelOneBossEncounter : MonoBehaviour
{
    public static LevelOneBossEncounter Instance { get; private set; }

    [Header("References (auto-find if empty)")]
    [SerializeField] private BossSpawner bossSpawner;
    [SerializeField] private BossRoomDoor bossRoomDoor;
    [SerializeField] private WarningText warningText;
    [SerializeField] private BossLifeBar bossLifeBar;
    [SerializeField] private GameObject bossHealthUiRoot;
    [Tooltip("Spawned at the copy-bot death spot (Level one has no scene-placed spawner crystal).")]
    [SerializeField] private SpawnCrystal spawnerCrystalPrefab;
    [Tooltip("Optional override: reveal this scene object instead of spawning from prefab.")]
    [SerializeField] private SpawnCrystal sceneSpawnerCrystalOverride;
    [Tooltip("Kit's rocket ship lands after the Copy Bot is beaten (Level one + Tutorial) and flies here. " +
             "Falls back to the spawner crystal if the ship prefab is missing.")]
    [SerializeField] private string copyBotRocketDestination = "Boss Fight Mode";
    [Tooltip("Keeps calling the ship this long if it can't come right away (player mid-revive, ship busy). " +
             "After that Level one drops the spawner crystal; Tutorial fades straight to the destination.")]
    [SerializeField] private float copyBotShipRetrySeconds = 5f;

    [Header("Copy Bot")]
    [SerializeField] private int copyBotMaxHealth = 40;
    [SerializeField] private float energyBallRecallDistance = 9f;
    [SerializeField] private float respawnDelayBeforeRecall = 0.35f;

    [Header("Health Bar Intro")]
    [SerializeField] private int healthBarIntroStart = 1;
    [SerializeField] private float healthBarIntroDuration = 1.35f;

    [Header("Player Death Reset")]
    [SerializeField] private float deathFadeOutSeconds = 0.45f;
    [SerializeField] private float deathFadeInSeconds = 0.45f;
    [SerializeField] private float checkpointYOffsetBelowCloseBox = 1.75f;

    private bool encounterStarted;
    private bool encounterFinished;
    private bool copyBotDefeatHandled;
    private Boss activeCopyBoss;
    private PlayerController watchedPlayer;
    private bool playerDeathFlowBusy;
    private Coroutine encounterFlowRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapInCopyBotScenes()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!IsCopyBotEncounterScene(scene.name))
            return;

        if (FindFirstObjectByType<LevelOneBossEncounter>() != null)
            return;

        BossSpawner spawner = FindFirstObjectByType<BossSpawner>();
        if (spawner == null)
            return;

        spawner.gameObject.AddComponent<LevelOneBossEncounter>();
    }

    public static bool IsLevelOneScene(string sceneName)
    {
        return string.Equals(sceneName, "Level one", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTutorialScene(string sceneName)
    {
        return sceneName != null &&
               sceneName.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Level one and Tutorial use the Copy Bot encounter pipeline.</summary>
    public static bool IsCopyBotEncounterScene(string sceneName)
    {
        return IsLevelOneScene(sceneName) || IsTutorialScene(sceneName);
    }

    /// <summary>
    /// When true, <see cref="BossFightDirector"/> must not run its own life-loss boss revive.
    /// </summary>
    public static bool ShouldSuppressDirectorPlayerDeath()
    {
        LevelOneBossEncounter encounter = Instance;
        if (encounter == null || !encounter.isActiveAndEnabled)
            return false;

        return (encounter.encounterStarted || encounter.playerDeathFlowBusy) &&
               !encounter.copyBotDefeatHandled;
    }

    /// <summary>
    /// Tutorial (and any copy-bot scene with a battle starter): begin the Copy Bot intro instead of
    /// spawning a Kaboodle boss. Returns true when this encounter consumed the start.
    /// </summary>
    public static bool TryStartFromBattleStarter()
    {
        if (!IsCopyBotEncounterScene(SceneManager.GetActiveScene().name))
            return false;

        LevelOneBossEncounter encounter = Instance ?? FindFirstObjectByType<LevelOneBossEncounter>();
        if (encounter == null)
            return false;

        encounter.enabled = true;
        return encounter.BeginEncounterFromStarter();
    }

    /// <summary>
    /// Copy-bot defeat: Level one spawns the spawner crystal; Tutorial just clears fight UI.
    /// </summary>
    public static bool TryConsumeCopyBotDefeat(Boss defeated)
    {
        if (!IsCopyBotEncounterScene(SceneManager.GetActiveScene().name))
            return false;

        if (defeated == null || !defeated.IsMainCopyBot || defeated.IsCopyBotDecoy)
            return false;

        LevelOneBossEncounter encounter = Instance ?? FindFirstObjectByType<LevelOneBossEncounter>();
        if (encounter == null)
            return false;

        if (encounter.copyBotDefeatHandled)
            return true;

        encounter.SpawnSpawnerCrystalAtBoss(defeated);
        return true;
    }

    private void Awake()
    {
        if (!IsCopyBotEncounterScene(SceneManager.GetActiveScene().name))
        {
            enabled = false;
            return;
        }

        Instance = this;
        AutoBind();
        PrepareCopyBotScene();
        BindDoorEvents();
    }

    private void OnEnable()
    {
        if (!IsCopyBotEncounterScene(SceneManager.GetActiveScene().name))
            return;

        AutoBind();
        BindDoorEvents();
    }

    private void OnDestroy()
    {
        UnbindPlayer();
        UnbindCopyBoss();
        UnbindDoorEvents();

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (playerDeathFlowBusy || copyBotDefeatHandled)
            return;

        if (watchedPlayer == null || !watchedPlayer.IsDead)
            return;

        if (!encounterStarted)
            return;

        StartPlayerDeathResetFlow();
    }

    private void AutoBind()
    {
        if (bossSpawner == null)
            bossSpawner = GetComponent<BossSpawner>();

        if (bossSpawner == null)
            bossSpawner = FindFirstObjectByType<BossSpawner>();

        if (bossRoomDoor == null)
            bossRoomDoor = FindFirstObjectByType<BossRoomDoor>(FindObjectsInactive.Include);

        if (warningText == null)
            warningText = WarningText.FindInScene(includeInactive: true);

        if (bossLifeBar == null)
            bossLifeBar = FindFirstObjectByType<BossLifeBar>(FindObjectsInactive.Include);

        if (bossHealthUiRoot == null)
        {
            // GameObject.Find skips inactive objects — search including inactive for Tutorial start hide.
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null &&
                    string.Equals(transforms[i].name, "Boss Health Stats", StringComparison.OrdinalIgnoreCase))
                {
                    bossHealthUiRoot = transforms[i].gameObject;
                    break;
                }
            }

            if (bossHealthUiRoot == null && bossLifeBar != null)
                bossHealthUiRoot = bossLifeBar.transform.root.gameObject;
        }
    }

    private void Start()
    {
        AutoBind();
        BindDoorEvents();
        BindPlayer();
        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnPlayerSpawned -= HandlePlayerSpawned;
            PlayerSpawner.Instance.OnPlayerSpawned += HandlePlayerSpawned;
        }
    }

    private void OnDisable()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned -= HandlePlayerSpawned;

        UnbindPlayer();
    }

    private void HandlePlayerSpawned(PlayerController player)
    {
        WatchPlayer(player);
    }

    private void BindPlayer()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            WatchPlayer(PlayerSpawner.Instance.CurrentPlayer);
        else
        {
            PlayerController found = FindFirstObjectByType<PlayerController>();
            if (found != null)
                WatchPlayer(found);
        }
    }

    private void WatchPlayer(PlayerController player)
    {
        if (player == null)
            return;

        if (watchedPlayer == player)
            return;

        UnbindPlayer();
        watchedPlayer = player;
    }

    private void UnbindPlayer()
    {
        watchedPlayer = null;
    }

    private void StartPlayerDeathResetFlow()
    {
        if (playerDeathFlowBusy)
            return;

        playerDeathFlowBusy = true;
        StartCoroutine(PlayerDeathResetFlow());
    }

    private void PrepareCopyBotScene()
    {
        if (bossSpawner != null)
            bossSpawner.DeferAutoSpawn();

        SetBossHealthUiVisible(false);
        if (bossLifeBar != null)
            bossLifeBar.SetBossOverride(null);
    }

    private void BindDoorEvents()
    {
        if (bossRoomDoor == null)
            return;

        bossRoomDoor.EnsureDetectorsReady();
        bossRoomDoor.DoorOpened -= HandleDoorOpened;
        bossRoomDoor.DoorOpened += HandleDoorOpened;
        bossRoomDoor.DoorClosed -= HandleDoorClosed;
        bossRoomDoor.DoorClosed += HandleDoorClosed;
    }

    private void UnbindDoorEvents()
    {
        if (bossRoomDoor == null)
            return;

        bossRoomDoor.DoorOpened -= HandleDoorOpened;
        bossRoomDoor.DoorClosed -= HandleDoorClosed;
    }

    private void HandleDoorOpened()
    {
        SoundManager.Instance?.FadeOutLevelOneMusic();
    }

    private void HandleDoorClosed()
    {
        BeginEncounterFromGate();
    }

    private bool BeginEncounterFromStarter()
    {
        return BeginEncounterFromGate();
    }

    private bool BeginEncounterFromGate()
    {
        if (encounterStarted || encounterFinished)
            return true;

        AutoBind();
        encounterStarted = true;
        SoundManager.Instance?.PlayLevelOneCopyBotBossMusic();
        if (encounterFlowRoutine != null)
            StopCoroutine(encounterFlowRoutine);
        encounterFlowRoutine = StartCoroutine(RunEncounterFlow());
        return true;
    }

    private IEnumerator PlayerDeathResetFlow()
    {
        LockPlayerInput(true);
        SetAllBossesCombatPaused(true);

        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        ScreenFade fade = ScreenFade.EnsureExists();
        yield return fade.FadeToBlack(deathFadeOutSeconds);
        fade.SetBlackImmediate();
        yield return fade.WaitUntilFullyBlack();

        Projectile.DestroyAllLive();

        PlayerInventory inv = PlayerInventory.Instance;
        if (inv != null)
            inv.TryLoseLife(1);

        int livesLeft = inv != null ? inv.LivesCount : 1;
        if (livesLeft <= 0)
        {
            if (inv != null)
                inv.ResetLivesToStarting();

            string home = "HomeTown";
            fade.LoadScene(home, deathFadeOutSeconds, deathFadeInSeconds);
            playerDeathFlowBusy = false;
            yield break;
        }

        ResetEncounterForPlayerDeath();

        // Activated checkpoint: Kit's rocket ship drops the player off after the fade-in.
        bool shipRespawn = KitRocketShip.TryBeginCheckpointRespawn(PlayerSpawner.Instance);

        PlayerController player = null;
        if (!shipRespawn)
        {
            if (PlayerSpawner.Instance != null)
                player = PlayerSpawner.Instance.RespawnAtSpawnPoint();
            else
            {
                player = FindFirstObjectByType<PlayerController>();
                if (player != null)
                    player.SetHealth(player.MaxHealth);
            }
        }

        if (player != null)
        {
            player.SetInputLocked(true);
            WatchPlayer(player);
        }

        yield return null;
        Transform waitingTrack = CheckPointBot.GetWaitingCameraTrackTarget();
        if (CameraFollow.Instance != null)
        {
            if (waitingTrack != null)
            {
                CameraFollow.Instance.SnapToTarget(waitingTrack);
                CameraFollow.Instance.SetFollowTarget(waitingTrack);
                CameraFollow.Instance.SetFollowEnabled(true);
            }
            else if (player != null)
            {
                CameraFollow.Instance.ForceFollowPlayer(player.transform);
            }
        }

        fade.SetBlackImmediate();
        yield return fade.FadeFromBlack(deathFadeInSeconds);

        if (shipRespawn)
        {
            while (KitRocketShip.IsRespawnDeliveryInProgress)
                yield return null;

            player = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.CurrentPlayer : null;
            if (player == null && PlayerSpawner.Instance != null)
                player = PlayerSpawner.Instance.RespawnAtSpawnPoint();

            if (player != null)
            {
                player.SetInputLocked(true);
                WatchPlayer(player);
            }
        }

        if (CameraFollow.Instance != null && player != null)
        {
            CameraFollow.Instance.SetFollowTarget(player.transform);
            CameraFollow.Instance.BeginRespawnCatchUp();
        }

        if (player != null)
            player.SetInputLocked(false);

        LockPlayerInput(false);
        playerDeathFlowBusy = false;
    }

    /// <summary>
    /// Boss-room retry: open door / reset starter, despawn copy-bot, clear UI.
    /// </summary>
    public void ResetEncounterForPlayerDeath()
    {
        if (encounterFlowRoutine != null)
        {
            StopCoroutine(encounterFlowRoutine);
            encounterFlowRoutine = null;
        }

        encounterStarted = false;
        encounterFinished = false;

        DestroyAllCopyBots();
        UnbindCopyBoss();

        if (bossRoomDoor != null)
            bossRoomDoor.ResetToOpen();

        ResetAllBattleStarters();

        SetBossHealthUiVisible(false);
        if (bossLifeBar != null)
            bossLifeBar.SetBossOverride(null);

        if (sceneSpawnerCrystalOverride != null)
            sceneSpawnerCrystalOverride.HideUntilBossDefeated();

        SoundManager.Instance?.StopLevelOneCopyBotBossMusic();

        ApplyBossRoomCheckpointSpawn();
    }

    private static void ResetAllBattleStarters()
    {
        BossBattleStarter[] starters = FindObjectsByType<BossBattleStarter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < starters.Length; i++)
        {
            if (starters[i] != null)
                starters[i].ResetStarter();
        }
    }

    private void ApplyBossRoomCheckpointSpawn()
    {
        if (bossRoomDoor == null || bossRoomDoor.CloseDetector == null)
            return;

        Vector3 checkpoint = bossRoomDoor.CloseDetector.transform.position;
        checkpoint.y -= checkpointYOffsetBelowCloseBox;

        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.SetSpawnPointWorldPosition(checkpoint);
    }

    private static void DestroyAllCopyBots()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            // Copy bots + any stray level boss from an old starter spawn.
            if (boss.IsCopyBot || IsCopyBotEncounterScene(SceneManager.GetActiveScene().name))
                Destroy(boss.gameObject);
        }
    }

    private IEnumerator RunEncounterFlow()
    {
        LockPlayerInput(true);
        SetAllBossesCombatPaused(true);

        if (warningText != null)
            yield return warningText.PlayAndWait(freezeCamera: true);
        else
            yield return null;

        LockPlayerInput(true);
        SetAllBossesCombatPaused(true);

        Boss copyBoss = bossSpawner != null
            ? bossSpawner.SpawnCopyBotForPlayer(copyBotMaxHealth)
            : null;

        if (copyBoss == null)
        {
            Debug.LogError(
                "[LevelOneBossEncounter] Failed to spawn copy-bot boss. " +
                "Check Boss Spawner prefab entries and that a player character is selected.",
                bossSpawner);
            encounterFinished = true;
            encounterStarted = false;
            ResetAllBattleStarters();
            LockPlayerInput(false);
            yield break;
        }

        BindCopyBoss(copyBoss);
        CopyBossAppearance.Apply(copyBoss);
        copyBoss.RefreshPhaseCollisionsWithCopyBots();
        copyBoss.SetCombatPaused(true);
        VisualEffects.SetHostSpritesVisible(copyBoss.gameObject, false);

        yield return PlayAssemblyIntro(copyBoss);

        VisualEffects.SetHostSpritesVisible(copyBoss.gameObject, true);
        copyBoss.SetCombatPaused(true);

        SetBossHealthUiVisible(true);
        if (bossLifeBar != null)
        {
            bossLifeBar.SetBossOverride(copyBoss);
            yield return bossLifeBar.PlayIntroCountUp(copyBoss, healthBarIntroStart, copyBoss.MaxHealth, healthBarIntroDuration);
        }
        else
            yield return new WaitForSecondsRealtime(healthBarIntroDuration);

        copyBoss.SetCombatPaused(false);
        LockPlayerInput(false);
        encounterFinished = true;
    }

    private void BindCopyBoss(Boss boss)
    {
        UnbindCopyBoss();
        activeCopyBoss = boss;

        if (activeCopyBoss != null)
            activeCopyBoss.OnDied += HandleCopyBossDied;
    }

    private void HandleCopyBossDied()
    {
        if (activeCopyBoss == null || !activeCopyBoss.IsMainCopyBot || activeCopyBoss.IsCopyBotDecoy)
            return;

        SpawnSpawnerCrystalAtBoss(activeCopyBoss);
    }

    private void UnbindCopyBoss()
    {
        if (activeCopyBoss != null)
            activeCopyBoss.OnDied -= HandleCopyBossDied;

        activeCopyBoss = null;
    }

    private void SpawnSpawnerCrystalAtBoss(Boss defeated)
    {
        if (defeated == null || !defeated.IsMainCopyBot || defeated.IsCopyBotDecoy)
            return;

        if (copyBotDefeatHandled)
            return;

        Vector3 spawnPosition = defeated.transform.position;
        Collider2D body = defeated.GetComponent<Collider2D>();
        if (body != null)
            spawnPosition = body.bounds.center;

        copyBotDefeatHandled = true;
        SetBossHealthUiVisible(false);
        if (bossLifeBar != null)
            bossLifeBar.SetBossOverride(null);
        SoundManager.Instance?.StopLevelOneCopyBotBossMusic();
        UnbindCopyBoss();

        // Kit's rocket ship replaces the spawner crystal.
        if (!KitRocketShip.TrySummonAfterBoss(copyBotRocketDestination))
            StartCoroutine(RetryCopyBotShip(spawnPosition));
    }

    private IEnumerator RetryCopyBotShip(Vector3 crystalPosition)
    {
        float waited = 0f;
        while (waited < copyBotShipRetrySeconds)
        {
            yield return null;
            waited += Time.deltaTime;
            if (KitRocketShip.TrySummonAfterBoss(copyBotRocketDestination))
                yield break;
        }

        if (IsLevelOneScene(SceneManager.GetActiveScene().name) && SpawnFallbackCrystal(crystalPosition))
            yield break;

        string target = copyBotRocketDestination != null ? copyBotRocketDestination.Trim() : string.Empty;
        if (string.IsNullOrEmpty(target) || !Application.CanStreamedLevelBeLoaded(target))
        {
            Debug.LogError($"[LevelOneBossEncounter] The rocket ship never came and '{target}' can't be loaded.", this);
            yield break;
        }

        Debug.LogWarning($"[LevelOneBossEncounter] The rocket ship never came — going straight to '{target}'.", this);
        if (BossFightDirector.IsBossFightScene(target))
            BossEncounter.PrepareBossFightFromLevelProgress();
        ScreenFade.EnsureExists().LoadScene(target, deathFadeOutSeconds, deathFadeInSeconds);
    }

    private bool SpawnFallbackCrystal(Vector3 spawnPosition)
    {
        SpawnCrystal spawned = null;

        if (sceneSpawnerCrystalOverride != null)
        {
            spawned = sceneSpawnerCrystalOverride;
            spawned.SpawnAt(spawnPosition);
        }
        else if (spawnerCrystalPrefab != null)
        {
            spawned = Instantiate(spawnerCrystalPrefab, spawnPosition, Quaternion.identity);
            spawned.name = spawnerCrystalPrefab.name;
            spawned.SpawnAt(spawnPosition);
        }
        else
        {
            Debug.LogError(
                "[LevelOneBossEncounter] Cannot spawn Spawner Crystal — assign Spawner Crystal Prefab " +
                "on Boss Spawner → Level One Boss Encounter.",
                this);
            return false;
        }

        return true;
    }

    private IEnumerator PlayAssemblyIntro(Boss boss)
    {
        GameVisualEffect ballPrefab = boss.DeathEnergyBallPrefab;
        if (ballPrefab == null)
            yield break;

        DeathEnergyBallBurst burst = VisualEffects.PlayDeathEnergyBalls(ballPrefab, boss, hideHost: true);
        if (burst == null)
            yield break;

        burst.EnableCrystalRecall(energyBallRecallDistance);

        if (respawnDelayBeforeRecall > 0f)
            yield return new WaitForSecondsRealtime(respawnDelayBeforeRecall);

        yield return VisualEffects.WaitForDeathBallsCrystalRecall(burst, energyBallRecallDistance);

        if (burst != null)
            Destroy(burst.gameObject);
    }

    private void SetBossHealthUiVisible(bool visible)
    {
        if (bossHealthUiRoot == null && bossLifeBar != null)
            bossHealthUiRoot = bossLifeBar.transform.root.gameObject;

        if (bossHealthUiRoot != null)
            bossHealthUiRoot.SetActive(visible);
    }

    private static void LockPlayerInput(bool locked)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            player.SetInputLocked(locked);
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
}
