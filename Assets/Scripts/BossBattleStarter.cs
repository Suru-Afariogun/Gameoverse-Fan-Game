using UnityEngine;

/// <summary>
/// Level gate: when the player passes this volume from any direction, asks
/// <see cref="BossSpawner"/> to spawn the level boss. If no starter exists in a scene,
/// <see cref="BossSpawner"/> spawns immediately on load instead.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class BossBattleStarter : MonoBehaviour
{
    [Tooltip("Only the first pass spawns the boss.")]
    [SerializeField] private bool oneShot = true;

    [Tooltip("Optional. Auto-finds BossSpawner in the scene when left empty.")]
    [SerializeField] private BossSpawner bossSpawner;

    [Header("Pass Detection")]
    [Tooltip("Also treat crossing this object's X (either direction) as a pass.")]
    [SerializeField] private bool detectXCrossing = true;

    private Collider2D zone;
    private bool hasTriggered;
    private float lastPlayerX;
    private bool hasLastPlayerX;

    public bool HasTriggered => hasTriggered;

    private void Awake()
    {
        zone = GetComponent<Collider2D>();
        EnsureTriggerCollider();
        DeferredEnemyPhaseRefresh.Request();

        if (bossSpawner == null)
            bossSpawner = FindFirstObjectByType<BossSpawner>();
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
    }

    private void Start()
    {
        if (bossSpawner == null)
            bossSpawner = FindFirstObjectByType<BossSpawner>();
    }

    private void Update()
    {
        if (oneShot && hasTriggered)
            return;

        if (!detectXCrossing)
            return;

        PlayerController player = PlayerController.Active;
        if (player == null || player.IsDead)
        {
            hasLastPlayerX = false;
            return;
        }

        float playerX = player.transform.position.x;
        float crossingX = GetCrossingX();

        if (hasLastPlayerX)
        {
            bool crossedRight = lastPlayerX < crossingX && playerX >= crossingX;
            bool crossedLeft = lastPlayerX > crossingX && playerX <= crossingX;
            if (crossedRight || crossedLeft)
                TryStartBossBattle();
        }

        lastPlayerX = playerX;
        hasLastPlayerX = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (oneShot && hasTriggered)
            return;

        if (other == null)
            return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        if (player == null || player.IsDead)
            return;

        TryStartBossBattle();
    }

    public void TryStartBossBattle()
    {
        if (oneShot && hasTriggered)
            return;

        hasTriggered = true;

        if (bossSpawner == null)
            bossSpawner = FindFirstObjectByType<BossSpawner>();

        if (bossSpawner == null)
        {
            Debug.LogWarning("[BossBattleStarter] No BossSpawner in this scene — cannot spawn boss.");
            return;
        }

        // Tutorial / Level one Copy Bot pipeline owns the spawn (same WARNING → assemble → HP fill).
        if (LevelOneBossEncounter.TryStartFromBattleStarter())
            return;

        bossSpawner.SpawnSelectedBossNow();
    }

    public void ResetStarter()
    {
        hasTriggered = false;
        hasLastPlayerX = false;
    }

    private float GetCrossingX()
    {
        if (zone != null)
            return zone.bounds.center.x;

        return transform.position.x;
    }

    private void EnsureTriggerCollider()
    {
        if (zone == null)
            zone = GetComponent<Collider2D>();

        if (zone != null)
            zone.isTrigger = true;
    }

    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreAllColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));
    }

    private static void IgnoreAllColliders(Collider2D[] aCols, Collider2D[] bCols)
    {
        if (aCols == null || bCols == null)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null)
                continue;

            for (int j = 0; j < bCols.Length; j++)
            {
                Collider2D b = bCols[j];
                if (b == null)
                    continue;

                Physics2D.IgnoreCollision(a, b, true);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D col = zone != null ? zone : GetComponent<Collider2D>();
        float x = col != null ? col.bounds.center.x : transform.position.x;
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
        Gizmos.DrawLine(new Vector3(x, transform.position.y - 3f, 0f), new Vector3(x, transform.position.y + 3f, 0f));
        if (col != null)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
#endif
}
