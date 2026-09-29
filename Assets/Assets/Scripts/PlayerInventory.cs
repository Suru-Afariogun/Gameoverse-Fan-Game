using System;
using UnityEngine;

/// <summary>
/// Shared run inventory: Juice, Hotdogs, Lives, Crystals.
/// Persists across scenes via DontDestroyOnLoad; crystals are also saved across game sessions.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    const string PrefsJuice = "Gameoverse_Juice";
    const string PrefsHotdogs = "Gameoverse_Hotdogs";
    const string PrefsLives = "Gameoverse_Lives";
    const string PrefsCrystals = "Gameoverse_Crystals";

    public const int HotDogCrystalCost = 50;
    public const int JuiceBoxCrystalCost = 20;

    [Header("Defaults")]
    [SerializeField] private int startingLives = 3;
    [SerializeField] private int startingJuice;
    [SerializeField] private int startingHotdogs;
    [SerializeField] private int startingCrystals;
    [SerializeField] private bool persistBetweenSessions;

    [Header("Item Effects")]
    [SerializeField] private int juiceHealAmount = 2;
    [SerializeField] private int hotdogHealAmount = 4;

    private int juiceCount;
    private int hotdogCount;
    private int livesCount;
    private int crystalCount;

    public int JuiceCount => juiceCount;
    public int HotdogCount => hotdogCount;
    public int LivesCount => livesCount;
    public int CrystalCount => crystalCount;
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

        // Crystals are the upgrade-shop currency, so they are always saved permanently.
        crystalCount = Mathf.Max(0, PlayerPrefs.GetInt(PrefsCrystals, startingCrystals));

        NotifyChanged();
    }

    private void Save()
    {
        PlayerPrefs.SetInt(PrefsCrystals, crystalCount);

        if (persistBetweenSessions)
        {
            PlayerPrefs.SetInt(PrefsJuice, juiceCount);
            PlayerPrefs.SetInt(PrefsHotdogs, hotdogCount);
            PlayerPrefs.SetInt(PrefsLives, livesCount);
        }

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

    public void AddCrystals(int amount = 1)
    {
        if (amount <= 0)
            return;
        crystalCount += amount;
        NotifyChanged();
    }

    public bool TrySpendCrystals(int amount)
    {
        if (amount <= 0 || crystalCount < amount)
            return false;

        crystalCount -= amount;
        NotifyChanged();
        return true;
    }

    /// <summary>Future shop hook: buy a Hot Dog for 50 crystals.</summary>
    public bool TryBuyHotDogWithCrystals()
    {
        if (!TrySpendCrystals(HotDogCrystalCost))
            return false;

        AddHotdogs(1);
        return true;
    }

    /// <summary>Future shop hook: buy a Juice Box for 20 crystals.</summary>
    public bool TryBuyJuiceBoxWithCrystals()
    {
        if (!TrySpendCrystals(JuiceBoxCrystalCost))
            return false;

        AddJuice(1);
        return true;
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
        return PlayerController.ResolveActive();
    }
}
