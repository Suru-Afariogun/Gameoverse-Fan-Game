using UnityEngine;

/// <summary>
/// Scene marker: the camera center cannot go below this object's world Y.
/// Independent from Background bounds — a failsafe for levels with irregular backdrop art.
/// Attach this to your "Lowest Point" GameObject in the scene.
/// </summary>
public sealed class LowestPoint : MonoBehaviour
{
    public static LowestPoint Active { get; private set; }

    /// <summary>World Y the camera center is not allowed to go below.</summary>
    public float FloorY => transform.position.y;

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 p = transform.position;
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
        Gizmos.DrawLine(new Vector3(p.x - 40f, p.y, p.z), new Vector3(p.x + 40f, p.y, p.z));
        Gizmos.DrawWireSphere(p, 0.25f);
    }
#endif
}
