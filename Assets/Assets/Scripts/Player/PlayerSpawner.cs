using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns / switches playable characters (Kit, Malice, …).
/// Put one in HomeTown (and boss scenes). Kaboodle can call SwitchCharacter / CycleCharacter.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance { get; private set; }

    public static string SelectedCharacterId { get; private set; } = "Kit";

    private static readonly Dictionary<string, PlayerController> SharedCharacterPrefabs =
        new Dictionary<string, PlayerController>(8, StringComparer.OrdinalIgnoreCase);

    private static bool homeTownFirstLoadDefaultsApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetHomeTownFirstLoadFlag()
    {
        homeTownFirstLoadDefaultsApplied = false;
    }

    [Serializable]
    public class CharacterEntry
    {
        [Tooltip("Must match PlayerController Character Id (Kit, Malice, …).")]
        public string characterId = "Kit";

        [Tooltip("Prefab root that has the character's PlayerController child class.")]
        public PlayerController prefab;
    }

    [Header("Characters")]
    [SerializeField] private CharacterEntry[] characters = new CharacterEntry[]
    {
        new CharacterEntry { characterId = "Kit" }
    };

    [Tooltip("Used on first load if nothing is saved yet.")]
    [SerializeField] private string defaultCharacterId = "Kit";

    [Header("Spawn")]
    [Tooltip("Where new players appear on scene start / RespawnAtSpawnPoint.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("Spawn the selected character when the scene starts.")]
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Destroy any PlayerController already in the scene before the first spawn (recommended if Kit is placed in the scene for editing).")]
    [SerializeField] private bool destroyScenePlayersOnStart = true;

    [Tooltip("Scene start: Kit's rocket ship flies in and drops the player off at the camera center " +
             "(uses the scene's parked ship, else Resources/Kit's Rocket ship). Life-loss respawns always spawn normally.")]
    [SerializeField] private bool arriveByRocketShip = true;
    [Tooltip("If the rocket ship has not delivered the player after this many seconds, spawn normally.")]
    [SerializeField] private float rocketArrivalFallbackSeconds = 20f;
    [Tooltip("Checkpoint respawns: the rocket ship drops the player off beside the last checkpoint and stays there. " +
             "Boarding it flies to this scene.")]
    [SerializeField] private string checkpointShipDestination = "HomeTown";

    public string CheckpointShipDestination => checkpointShipDestination;

    [Header("Switching")]
    [Tooltip("When switching via Kaboodle, keep the current world position/facing instead of teleporting to Spawn Point.")]
    [SerializeField] private bool keepPoseOnSwitch = true;

    [Tooltip("Retarget CameraFollow after spawn/switch.")]
    [SerializeField] private bool retargetCamera = true;
    public PlayerController CurrentPlayer { get; private set; }

    /// <summary>Transform the camera should frame before a player exists (spawn point, else this object).</summary>
    public Transform CameraTrackTransform => spawnPoint != null ? spawnPoint : transform;

    public event Action<PlayerController> OnPlayerSpawned;
    public event Action<string> OnCharacterChanged;
    /// <summary>Fired only from <see cref="RespawnAtSpawnPoint"/> (life-loss respawn), not initial spawn or Kaboodle switches.</summary>
    public event Action<PlayerController> OnPlayerRespawned;

    private bool firePlayerRespawnedEvent;

    private void Awake()
    {
        Instance = this;
        RegisterCharacterPrefabs();

        if (IsHomeTownScene())
            ApplyHomeTownFirstLoadDefaultsIfNeeded();
        else
            EnsureValidSelection();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (!spawnOnStart)
            return;

        if (destroyScenePlayersOnStart)
            DestroyAllScenePlayers();

        if (arriveByRocketShip &&
            PlayerController.ResolveActive() == null &&
            KitRocketShip.TryBeginArrival(this))
        {
            StartCoroutine(RocketArrivalFallback());
            return;
        }

        SpawnSelected(GetSpawnPoseFromPoint());
    }

    /// <summary>Kit's rocket ship releases the player at its spawner (camera center).</summary>
    public PlayerController SpawnFromRocketShip(Vector3 worldPosition)
    {
        return SpawnSelected(new SpawnPose(worldPosition, GetDefaultSpawnFacingSign()));
    }

    /// <summary>
    /// Resets the level for a respawn (common enemies) while the screen is black. Rocket-ship checkpoint
    /// respawns use this, then <see cref="SpawnFromRocketShip"/>, which does not fire <see cref="OnPlayerRespawned"/>.
    /// </summary>
    public void ResetSceneForRespawn()
    {
        SceneEnemyRespawner[] respawners = FindObjectsByType<SceneEnemyRespawner>(FindObjectsSortMode.None);
        for (int i = 0; i < respawners.Length; i++)
        {
            if (respawners[i] != null)
                respawners[i].RespawnAllEnemies();
        }
    }

    private IEnumerator RocketArrivalFallback()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, rocketArrivalFallbackSeconds));
        if (CurrentPlayer == null && PlayerController.ResolveActive() == null)
            SpawnSelected(GetSpawnPoseFromPoint());
    }

    /// <summary>Spawn / replace with the currently selected character at the spawn point.</summary>
    public PlayerController RespawnAtSpawnPoint()
    {
        firePlayerRespawnedEvent = true;
        return SpawnSelected(GetSpawnPoseFromPoint());
    }

    /// <summary>
    /// Select a character by id (e.g. "Kit", "Malice") and spawn them.
    /// Kaboodle character-switch should call this.
    /// </summary>
    public PlayerController SwitchCharacter(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            Debug.LogWarning("[PlayerSpawner] SwitchCharacter called with empty id.");
            return CurrentPlayer;
        }

        if (!HasPrefab(characterId))
        {
            Debug.LogWarning($"[PlayerSpawner] No prefab assigned for '{characterId}'. Add it on the PlayerSpawner.");
            return CurrentPlayer;
        }

        SetSelectedCharacterId(characterId);

        SpawnPose pose = keepPoseOnSwitch ? GetSpawnPoseFromCurrentPlayer() : GetSpawnPoseFromPoint();
        return SpawnSelected(pose);
    }

    /// <summary>Cycle to the next assigned character prefab (handy for testing before Kaboodle UI exists).</summary>
    public PlayerController CycleCharacter()
    {
        if (characters == null || characters.Length == 0)
            return CurrentPlayer;

        int currentIndex = IndexOfCharacter(SelectedCharacterId);
        for (int step = 1; step <= characters.Length; step++)
        {
            int next = (currentIndex + step) % characters.Length;
            CharacterEntry entry = characters[next];
            if (entry == null || entry.prefab == null || string.IsNullOrWhiteSpace(entry.characterId))
                continue;

            return SwitchCharacter(entry.characterId);
        }

        Debug.LogWarning("[PlayerSpawner] CycleCharacter found no valid prefabs.");
        return CurrentPlayer;
    }

    public bool HasPrefab(string characterId)
    {
        return FindEntry(characterId)?.prefab != null;
    }

    public void SetSelectedCharacterId(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return;

        SelectedCharacterId = characterId.Trim();
    }

    /// <summary>Moves the respawn / scene-start spawn point (e.g. level-one boss checkpoint).</summary>
    public void SetSpawnPointWorldPosition(Vector3 worldPosition)
    {
        if (spawnPoint == null)
        {
            GameObject marker = new GameObject("SpawnPoint");
            marker.transform.SetParent(transform, false);
            spawnPoint = marker.transform;
        }

        spawnPoint.position = worldPosition;
    }

    private void RegisterCharacterPrefabs()
    {
        if (characters == null)
            return;

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterEntry entry = characters[i];
            if (entry == null || entry.prefab == null || string.IsNullOrWhiteSpace(entry.characterId))
                continue;

            SharedCharacterPrefabs[entry.characterId.Trim()] = entry.prefab;
        }
    }

    private PlayerController SpawnSelected(SpawnPose pose)
    {
        CharacterEntry entry = FindEntry(SelectedCharacterId);
        if (entry == null || entry.prefab == null)
        {
            // Fall back to default, then first valid entry.
            entry = FindEntry(defaultCharacterId);
            if (entry == null || entry.prefab == null)
                entry = FindFirstValidEntry();

            if (entry == null || entry.prefab == null)
            {
                Debug.LogError("[PlayerSpawner] No character prefabs assigned.");
                return null;
            }

            SetSelectedCharacterId(entry.characterId);
        }

        DestroyCurrentPlayer();
        Projectile.DestroyAllLive();

        PlayerController spawned = Instantiate(entry.prefab, pose.position, Quaternion.identity);
        spawned.gameObject.name = entry.prefab.gameObject.name;
        CurrentPlayer = spawned;

        if (Mathf.Abs(pose.facingSign) > 0.01f)
            spawned.SetFacingSign(pose.facingSign);

        spawned.RefreshPlayerPhaseCollisions();

        // WARNING intro / replay may already be locking the fight — keep the new player frozen.
        if (WarningText.BlocksGameplay)
            spawned.SetInputLocked(true);

        if (retargetCamera && CameraFollow.Instance != null)
        {
            // Life-loss respawn: frame CheckPoint Bot first (BossFightDirector keeps it through fade/WARNING).
            Transform respawnCam =
                firePlayerRespawnedEvent ? CheckPointBot.GetWaitingCameraTrackTarget() : null;
            if (respawnCam != null)
            {
                CameraFollow.Instance.SnapToTarget(respawnCam);
                CameraFollow.Instance.SetFollowTarget(respawnCam);
                CameraFollow.Instance.SetFollowEnabled(true);
            }
            else
            {
                CameraFollow.Instance.SetFollowTarget(spawned.transform);
                if (WarningText.BlocksGameplay)
                    CameraFollow.Instance.SnapToTarget();
            }
        }

        OnPlayerSpawned?.Invoke(spawned);
        OnCharacterChanged?.Invoke(spawned.CharacterId);

        if (firePlayerRespawnedEvent)
        {
            firePlayerRespawnedEvent = false;
            OnPlayerRespawned?.Invoke(spawned);
        }

        return spawned;
    }

    private void DestroyCurrentPlayer()
    {
        PlayerController live = CurrentPlayer != null ? CurrentPlayer : PlayerController.Active;
        CurrentPlayer = null;
        DestroyPlayerObject(live);
    }

    private void DestroyAllScenePlayers()
    {
        PlayerController[] found = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        CurrentPlayer = null;

        for (int i = 0; i < found.Length; i++)
            DestroyPlayerObject(found[i]);
    }

    private static void DestroyPlayerObject(PlayerController player)
    {
        if (player == null)
            return;

        // Disable first so OnDisable clears Active before the new spawn claims it.
        // Avoids a same-frame race with deferred Destroy().
        player.gameObject.SetActive(false);
        Destroy(player.gameObject);
    }

    /// <summary>
    /// First HomeTown visit each session: Kit + Normal attack style.
    /// Later scene loads (boss fights, return trips) keep Kaboodle picks.
    /// </summary>
    private static void ApplyHomeTownFirstLoadDefaultsIfNeeded()
    {
        if (homeTownFirstLoadDefaultsApplied)
            return;

        homeTownFirstLoadDefaultsApplied = true;
        SelectedCharacterId = "Kit";
        PlayerAttackStyle.ResetToNormal();
    }

    private void EnsureValidSelection()
    {
        if (!string.IsNullOrWhiteSpace(SelectedCharacterId) && HasPrefab(SelectedCharacterId))
            return;

        SelectedCharacterId = string.IsNullOrWhiteSpace(defaultCharacterId) ? "Kit" : defaultCharacterId;
    }

    private static bool IsHomeTownScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        return sceneName.IndexOf("HomeTown", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Home Town", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private CharacterEntry FindEntry(string characterId)
    {
        if (characters == null || string.IsNullOrWhiteSpace(characterId))
            return null;

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterEntry entry = characters[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.characterId))
                continue;

            if (string.Equals(entry.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        if (SharedCharacterPrefabs.TryGetValue(characterId.Trim(), out PlayerController sharedPrefab) && sharedPrefab != null)
        {
            return new CharacterEntry
            {
                characterId = characterId.Trim(),
                prefab = sharedPrefab
            };
        }

        return null;
    }

    private CharacterEntry FindFirstValidEntry()
    {
        if (characters == null)
            return null;

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterEntry entry = characters[i];
            if (entry != null && entry.prefab != null && !string.IsNullOrWhiteSpace(entry.characterId))
                return entry;
        }

        return null;
    }

    private int IndexOfCharacter(string characterId)
    {
        if (characters == null)
            return -1;

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterEntry entry = characters[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.characterId))
                continue;

            if (string.Equals(entry.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private SpawnPose GetSpawnPoseFromPoint()
    {
        // Temporary: Boss Fight spawns/respawns face right until facing anims are ready.
        float facing = GetDefaultSpawnFacingSign();

        if (spawnPoint != null)
            return new SpawnPose(spawnPoint.position, facing);

        if (CurrentPlayer != null)
            return new SpawnPose(CurrentPlayer.transform.position, CurrentPlayer.FacingSign);

        if (PlayerController.Active != null)
            return new SpawnPose(PlayerController.Active.transform.position, PlayerController.Active.FacingSign);

        return new SpawnPose(transform.position, facing);
    }

    private static float GetDefaultSpawnFacingSign()
    {
        return BossFightDirector.IsActiveBossFightScene() ? 1f : -1f;
    }

    private SpawnPose GetSpawnPoseFromCurrentPlayer()
    {
        PlayerController live = CurrentPlayer != null ? CurrentPlayer : PlayerController.Active;
        if (live != null)
            return new SpawnPose(live.transform.position, live.FacingSign);

        return GetSpawnPoseFromPoint();
    }

    private struct SpawnPose
    {
        public Vector3 position;
        public float facingSign;

        public SpawnPose(Vector3 position, float facingSign)
        {
            this.position = position;
            this.facingSign = facingSign;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(defaultCharacterId))
            defaultCharacterId = "Kit";

        if (characters == null)
            return;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null)
                continue;

            if (string.IsNullOrWhiteSpace(characters[i].characterId) && characters[i].prefab != null)
                characters[i].characterId = characters[i].prefab.CharacterId;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(pos, 0.25f);
        Gizmos.DrawLine(pos, pos + Vector3.up * 1.2f);
    }
#endif
}
