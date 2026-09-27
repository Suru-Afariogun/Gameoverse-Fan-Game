using UnityEngine;

/// <summary>
/// Touch to teleport the player to another scene with the usual black fade.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SpawnCrystal : MonoBehaviour
{
    [Header("Teleport")]
    [Tooltip("Scene name exactly as listed in Build Settings (e.g. Level one, Boss Fight Mode).")]
    [SerializeField] private string teleportToScene = "Level one";
    [SerializeField] private float fadeOutSeconds = 0.35f;
    [SerializeField] private float fadeInSeconds = 0.35f;

    private bool transitionStarted;
    private bool hiddenUntilReveal;

    public bool IsHiddenUntilReveal => hiddenUntilReveal;

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    /// <summary>Keep the crystal out of the level until the copy-bot boss is defeated.</summary>
    public void HideUntilBossDefeated()
    {
        hiddenUntilReveal = true;
        transitionStarted = false;
        gameObject.SetActive(false);
    }

    /// <summary>Spawn in at the boss death spot (fresh instance or scene override).</summary>
    public void SpawnAt(Vector3 worldPosition)
    {
        hiddenUntilReveal = false;
        transitionStarted = false;
        transform.position = worldPosition;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.enabled = true;
    }

    /// <summary>Alias for <see cref="SpawnAt"/> — used when a hidden scene crystal is reused.</summary>
    public void RevealAt(Vector3 worldPosition)
    {
        SpawnAt(worldPosition);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hiddenUntilReveal || transitionStarted || string.IsNullOrWhiteSpace(teleportToScene))
            return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        if (player == null || player.IsDead)
            return;

        transitionStarted = true;

        string targetScene = teleportToScene.Trim();
        if (BossFightDirector.IsBossFightScene(targetScene))
            BossEncounter.PrepareBossFightFromLevelProgress();

        ScreenFade.EnsureExists().LoadScene(targetScene, fadeOutSeconds, fadeInSeconds);
    }
}
