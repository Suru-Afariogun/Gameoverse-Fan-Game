using System;
using UnityEngine;

/// <summary>
/// Spawns / switches playable characters (Kit, Malice, …).
/// Put one in HomeTown (and boss scenes). Kaboodle can call SwitchCharacter / CycleCharacter.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance { get; private set; }

    public static string SelectedCharacterId { get; private set; } = "Kit";

    const string PrefsKey = "Gameoverse_SelectedCharacterId";

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

    [Tooltip("Remember last selected character between Play Mode sessions / scenes.")]
    [SerializeField] private bool persistSelection = true;

    [Header("Spawn")]
    [Tooltip("Where new players appear on scene start / RespawnAtSpawnPoint.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("Spawn the selected character when the scene starts.")]
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Destroy any PlayerController already in the scene before the first spawn (recommended if Kit is placed in the scene for editing).")]
    [SerializeField] private bool destroyScenePlayersOnStart = true;

    [Header("Switching")]
    [Tooltip("When switching via Kaboodle, keep the current world position/facing instead of teleporting to Spawn Point.")]
    [SerializeField] private bool keepPoseOnSwitch = true;

    [Tooltip("Retarget CameraFollow after spawn/switch.")]
    [SerializeField] private bool retargetCamera = true;

    public PlayerController CurrentPlayer { get; private set; }

    public event Action<PlayerController> OnPlayerSpawned;
    public event Action<string> OnCharacterChanged;

    private void Awake()
    {
        Instance = this;
        LoadSelection();
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

        SpawnSelected(GetSpawnPoseFromPoint());
    }

    /// <summary>Spawn / replace with the currently selected character at the spawn point.</summary>
    public PlayerController RespawnAtSpawnPoint()
    {
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
        if (persistSelection)
            PlayerPrefs.SetString(PrefsKey, SelectedCharacterId);
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

        PlayerController spawned = Instantiate(entry.prefab, pose.position, Quaternion.identity);
        spawned.gameObject.name = entry.prefab.gameObject.name;
        CurrentPlayer = spawned;

        if (Mathf.Abs(pose.facingSign) > 0.01f)
            spawned.SetFacingSign(pose.facingSign);

        spawned.RefreshPlayerPhaseCollisions();

        if (retargetCamera && CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowTarget(spawned.transform);

        OnPlayerSpawned?.Invoke(spawned);
        OnCharacterChanged?.Invoke(spawned.CharacterId);
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

    private void LoadSelection()
    {
        if (persistSelection && PlayerPrefs.HasKey(PrefsKey))
        {
            string saved = PlayerPrefs.GetString(PrefsKey, defaultCharacterId);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                SelectedCharacterId = saved;
                return;
            }
        }

        SelectedCharacterId = string.IsNullOrWhiteSpace(defaultCharacterId) ? "Kit" : defaultCharacterId;
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
        if (spawnPoint != null)
            return new SpawnPose(spawnPoint.position, -1f);

        if (CurrentPlayer != null)
            return new SpawnPose(CurrentPlayer.transform.position, CurrentPlayer.FacingSign);

        if (PlayerController.Active != null)
            return new SpawnPose(PlayerController.Active.transform.position, PlayerController.Active.FacingSign);

        return new SpawnPose(transform.position, -1f);
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
