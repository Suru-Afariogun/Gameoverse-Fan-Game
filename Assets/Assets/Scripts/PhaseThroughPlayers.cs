using UnityEngine;

/// <summary>
/// Town prop helper: solid colliders on this object (and children) ignore player solids
/// so the player can walk through while the prop still rests on ground / world geo.
/// </summary>
public sealed class PhaseThroughPlayers : MonoBehaviour
{
    private void Start()
    {
        RefreshPhaseCollisions();
    }

    private void OnEnable()
    {
        RefreshPhaseCollisions();
    }

    /// <summary>
    /// Solid body colliders ignore player solid colliders (phase through).
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

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
}
