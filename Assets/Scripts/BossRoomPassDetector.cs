using System;
using UnityEngine;

/// <summary>
/// Phasable box used by <see cref="BossRoomDoor"/>. Fires when the active player crosses from
/// left to right past this box (X crossing — overlap is not required).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BossRoomPassDetector : MonoBehaviour
{
    [Tooltip("If set, use this world X instead of the box collider edge.")]
    [SerializeField] private bool useCustomCrossingX;
    [SerializeField] private float customCrossingX;
    [Tooltip("Use the collider's left world edge (Min X). Turn off to use the right edge (Max X).")]
    [SerializeField] private bool useLeftWorldEdge = true;

    [Tooltip("If true, only the first left-to-right pass triggers.")]
    [SerializeField] private bool oneShot = true;

    private Collider2D zone;
    private float lastPlayerX;
    private bool hasLastPlayerX;
    private bool hasTriggered;

    public event Action PassedLeftToRight;

    public bool HasTriggered => hasTriggered;

    private void Awake()
    {
        zone = GetComponent<Collider2D>();
        EnsureTriggerCollider();
        RefreshPhaseCollisions();
    }

    private void OnEnable()
    {
        RefreshPhaseCollisions();
    }

    private void Update()
    {
        if (oneShot && hasTriggered)
            return;

        PlayerController player = PlayerController.Active;
        if (player == null)
        {
            hasLastPlayerX = false;
            return;
        }

        float playerX = player.transform.position.x;
        float crossingX = GetCrossingX();
        if (hasLastPlayerX && lastPlayerX < crossingX && playerX >= crossingX)
            TriggerPass();

        lastPlayerX = playerX;
        hasLastPlayerX = true;
    }

    public void ResetDetector()
    {
        hasTriggered = false;
        hasLastPlayerX = false;
    }

    private void TriggerPass()
    {
        if (oneShot && hasTriggered)
            return;

        hasTriggered = true;
        PassedLeftToRight?.Invoke();
    }

    public float GetCrossingX()
    {
        if (useCustomCrossingX)
            return customCrossingX;

        if (zone != null)
            return useLeftWorldEdge ? zone.bounds.min.x : zone.bounds.max.x;

        return transform.position.x;
    }

    public void SetUseLeftWorldEdge(bool useLeftEdge)
    {
        useLeftWorldEdge = useLeftEdge;
    }

    private void EnsureTriggerCollider()
    {
        if (zone == null)
            zone = GetComponent<Collider2D>();

        if (zone != null)
            zone.isTrigger = true;
    }

    /// <summary>
    /// Solid colliders on this detector ignore players so the box can stay phasable.
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponents<Collider2D>();

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player == null)
                continue;

            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));
        }
    }

    private static void IgnoreSolidColliders(Collider2D[] aCols, Collider2D[] bCols)
    {
        if (aCols == null || bCols == null)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null || a.isTrigger)
                continue;

            for (int j = 0; j < bCols.Length; j++)
            {
                Collider2D b = bCols[j];
                if (b == null || b.isTrigger)
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
        float crossingX = useCustomCrossingX ? customCrossingX : col != null ? col.bounds.max.x : transform.position.x;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
        Vector3 bottom = new Vector3(crossingX, transform.position.y - 2f, 0f);
        Vector3 top = new Vector3(crossingX, transform.position.y + 2f, 0f);
        Gizmos.DrawLine(bottom, top);
    }
#endif
}
