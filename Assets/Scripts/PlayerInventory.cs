using System;
using UnityEngine;

/// <summary>
/// Shared run inventory: Juice, Hotdogs, Lives.
/// Persists across scenes via DontDestroyOnLoad.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    const string PrefsJuice = "Gameoverse_Juice";
    const string PrefsHotdogs = "Gameoverse_Hotdogs";
    const string PrefsLives = "Gameoverse_Lives";

    [Header("Defaults")]
    [SerializeField] private int startingLives = 3;
    [SerializeField] private int startingJuice;
    [SerializeField] private int startingHotdogs;
    [SerializeField] private bool persistBetweenSessions;

    [Header("Item Effects")]
    [SerializeField] private int juiceHealAmount = 2;
    [SerializeField] private int hotdogHealAmount = 4;

    private int juiceCount;
    private int hotdogCount;
    private int livesCount;

    public int JuiceCount => juiceCount;
    public int HotdogCount => hotdogCount;
    public int LivesCount => livesCount;
    public int JuiceHealAmount => juiceHealAmount;

    public event Action OnInventoryChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject("PlayerInventory");
        go.AddComponent<PlayerInventory>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadOrInit();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LoadOrInit()
    {
        if (persistBetweenSessions && PlayerPrefs.HasKey(PrefsLives))
        {
            juiceCount = Mathf.Max(0, PlayerPrefs.GetInt(PrefsJuice, startingJuice));
            hotdogCount = Mathf.Max(0, PlayerPrefs.GetInt(PrefsHotdogs, startingHotdogs));
            livesCount = Mathf.Max(0, PlayerPrefs.GetInt(PrefsLives, startingLives));
        }
        else
        {
            juiceCount = Mathf.Max(0, startingJuice);
            hotdogCount = Mathf.Max(0, startingHotdogs);
            livesCount = Mathf.Max(0, startingLives);
        }

        NotifyChanged();
    }

    private void Save()
    {
        if (!persistBetweenSessions)
            return;

        PlayerPrefs.SetInt(PrefsJuice, juiceCount);
        PlayerPrefs.SetInt(PrefsHotdogs, hotdogCount);
        PlayerPrefs.SetInt(PrefsLives, livesCount);
        PlayerPrefs.Save();
    }

    private void NotifyChanged()
    {
        Save();
        OnInventoryChanged?.Invoke();
    }

    public void AddJuice(int amount = 1)
    {
        if (amount <= 0)
            return;
        juiceCount += amount;
        NotifyChanged();
    }

    public void AddHotdogs(int amount = 1)
    {
        if (amount <= 0)
            return;
        hotdogCount += amount;
        NotifyChanged();
    }

    public void AddLife(int amount = 1)
    {
        if (amount <= 0)
            return;
        livesCount += amount;
        NotifyChanged();
    }

    public void ResetLivesToStarting()
    {
        livesCount = Mathf.Max(0, startingLives);
        NotifyChanged();
    }

    /// <summary>Call when the player dies and loses a life (death system later).</summary>
    public bool TryLoseLife(int amount = 1)
    {
        if (amount <= 0 || livesCount <= 0)
            return false;

        livesCount = Mathf.Max(0, livesCount - amount);
        NotifyChanged();
        return true;
    }

    public bool TryUseJuice()
    {
        if (juiceCount <= 0)
            return false;

        PlayerController player = GetActivePlayer();
        if (player == null || player.CurrentHealth <= 0)
            return false;

        if (player.CurrentHealth >= player.MaxHealth)
            return false;

        juiceCount--;
        player.Heal(Mathf.Max(1, juiceHealAmount));
        NotifyChanged();
        return true;
    }

    public bool TryUseHotdog()
    {
        if (hotdogCount <= 0)
            return false;

        PlayerController player = GetActivePlayer();
        if (player == null || player.CurrentHealth <= 0)
            return false;

        if (player.CurrentHealth >= player.MaxHealth)
            return false;

        hotdogCount--;
        player.Heal(Mathf.Max(1, hotdogHealAmount));
        NotifyChanged();
        return true;
    }

    private static PlayerController GetActivePlayer()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return FindFirstObjectByType<PlayerController>();
    }
}
