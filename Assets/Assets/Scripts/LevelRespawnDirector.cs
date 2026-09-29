using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Level scenes (any scene with a Player Spawner except HomeTown and Boss Fight scenes):
/// life-loss respawn at the last checkpoint (Kit's rocket ship delivers the player when a checkpoint is
/// active), game over back to HomeTown, and the rocket ship when a level boss is defeated.
/// Copy Bot fights keep their own death flow and ship (<see cref="LevelOneBossEncounter"/>).
/// Auto-added on scene load; place one manually to change the Inspector settings.
/// </summary>
public sealed class LevelRespawnDirector : MonoBehaviour
{
    [SerializeField] private string homeTownSceneName = "HomeTown";
    [Tooltip("Where the rocket ship flies after a level boss (not the Copy Bot) is defeated.")]
    [SerializeField] private string levelBossShipDestination = "HomeTown";
    [SerializeField] private float fadeOutSeconds = 0.45f;
    [SerializeField] private float fadeInSeconds = 0.45f;

    private bool flowBusy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= EnsureOnSceneLoaded;
        SceneManager.sceneLoaded += EnsureOnSceneLoaded;
    }

    private static void EnsureOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsLevelScene(scene.name))
            return;

        if (FindFirstObjectByType<PlayerSpawner>() == null || FindFirstObjectByType<LevelRespawnDirector>() != null)
            return;

        new GameObject("LevelRespawnDirector").AddComponent<LevelRespawnDirector>();
    }

    public static bool IsLevelScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || BossFightDirector.IsBossFightScene(sceneName))
            return false;

        return sceneName.IndexOf("HomeTown", StringComparison.OrdinalIgnoreCase) < 0 &&
               sceneName.IndexOf("Home Town", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private void OnEnable()
    {
        Boss.AnyBossDied -= HandleBossDied;
        Boss.AnyBossDied += HandleBossDied;
    }

    private void OnDisable()
    {
        Boss.AnyBossDied -= HandleBossDied;
        StopAllCoroutines();
        flowBusy = false;
    }

    private void Update()
    {
        if (flowBusy)
            return;

        PlayerController player = PlayerController.ResolveActive();
        if (player == null || !player.IsDead)
            return;

        // Mid Copy Bot fight: the encounter runs its own reset (door, starter, boss despawn).
        if (LevelOneBossEncounter.ShouldSuppressDirectorPlayerDeath())
            return;

        StartCoroutine(PlayerDeathFlow(player));
    }

    private IEnumerator PlayerDeathFlow(PlayerController deadPlayer)
    {
        flowBusy = true;
        if (deadPlayer != null)
            deadPlayer.SetInputLocked(true);

        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        ScreenFade fade = ScreenFade.EnsureExists();
        yield return fade.FadeToBlack(fadeOutSeconds);
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

            SoundManager.Instance?.PlayGameOver();
            string home = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName.Trim();
            fade.LoadScene(home, fadeOutSeconds, fadeInSeconds);
            yield break;
        }

        PlayerSpawner spawner = PlayerSpawner.Instance;
        bool shipRespawn = KitRocketShip.TryBeginCheckpointRespawn(spawner);

        PlayerController player = null;
        if (!shipRespawn && spawner != null)
            player = spawner.RespawnAtSpawnPoint();

        if (player != null)
            player.SetInputLocked(true);

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
        yield return fade.FadeFromBlack(fadeInSeconds);

        if (shipRespawn)
        {
            while (KitRocketShip.IsRespawnDeliveryInProgress)
                yield return null;

            player = spawner != null ? spawner.CurrentPlayer : null;
            if (player == null && spawner != null)
                player = spawner.RespawnAtSpawnPoint();

            if (player != null)
                player.SetInputLocked(true);
        }

        if (CameraFollow.Instance != null && player != null)
        {
            CameraFollow.Instance.SetFollowTarget(player.transform);
            CameraFollow.Instance.BeginRespawnCatchUp();
        }

        if (player != null)
            player.SetInputLocked(false);

        flowBusy = false;
    }

    private void HandleBossDied(Boss boss)
    {
        if (boss == null || boss.IsCopyBotDecoy || boss.IsMainCopyBot || !isActiveAndEnabled)
            return;

        if (AnyOtherBossAlive(boss))
            return;

        KitRocketShip.TrySummonAfterBoss(levelBossShipDestination);
    }

    private static bool AnyOtherBossAlive(Boss defeated)
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss != null && boss != defeated && boss.isActiveAndEnabled && !boss.IsDead && !boss.IsCopyBotDecoy)
                return true;
        }

        return false;
    }
}
