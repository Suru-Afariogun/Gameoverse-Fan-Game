using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level camera / playable barrier marker — upgraded <see cref="LowestPoint"/>.
/// Place <see cref="Axis.Horizontal"/> edges for top &amp; bottom, <see cref="Axis.Vertical"/> for left &amp; right.
/// When a scene has no Background bounds, these define the camera and playable box.
/// With a Background, they still act as failsafe clamps (same idea as Lowest Point).
/// </summary>
public sealed class SceneEdge : MonoBehaviour
{
    public enum Axis
    {
        Horizontal,
        Vertical
    }

    private static readonly List<SceneEdge> Active = new List<SceneEdge>(8);

    [SerializeField] private Axis axis = Axis.Horizontal;

    public Axis EdgeAxis => axis;
    public Vector3 WorldPosition => transform.position;

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.RefreshPlayableBounds();
    }

    private void OnDisable()
    {
        Active.Remove(this);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.RefreshPlayableBounds();
    }

    private void OnValidate()
    {
        // Prefer axis from the object name when dropped in-scene.
        string n = gameObject != null ? gameObject.name : null;
        if (string.IsNullOrEmpty(n))
            return;

        if (n.IndexOf("Vertical", System.StringComparison.OrdinalIgnoreCase) >= 0)
            axis = Axis.Vertical;
        else if (n.IndexOf("Horizontal", System.StringComparison.OrdinalIgnoreCase) >= 0)
            axis = Axis.Horizontal;
    }

    /// <summary>
    /// Camera-center limits from all active edges.
    /// Horizontal → floor (min Y) / ceiling (max Y). Vertical → left (min X) / right (max X).
    /// A single edge on an axis only sets the min side (floor / left), matching Lowest Point.
    /// </summary>
    public static void GetCameraLimits(
        out bool hasFloor, out float floorY,
        out bool hasCeiling, out float ceilingY,
        out bool hasLeft, out float leftX,
        out bool hasRight, out float rightX)
    {
        hasFloor = hasCeiling = hasLeft = hasRight = false;
        floorY = ceilingY = leftX = rightX = 0f;

        float minHY = float.PositiveInfinity;
        float maxHY = float.NegativeInfinity;
        int horizontalCount = 0;

        float minVX = float.PositiveInfinity;
        float maxVX = float.NegativeInfinity;
        int verticalCount = 0;

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            SceneEdge edge = Active[i];
            if (edge == null)
            {
                Active.RemoveAt(i);
                continue;
            }

            Vector3 p = edge.transform.position;
            if (edge.axis == Axis.Horizontal)
            {
                horizontalCount++;
                if (p.y < minHY) minHY = p.y;
                if (p.y > maxHY) maxHY = p.y;
            }
            else
            {
                verticalCount++;
                if (p.x < minVX) minVX = p.x;
                if (p.x > maxVX) maxVX = p.x;
            }
        }

        if (horizontalCount >= 1)
        {
            hasFloor = true;
            floorY = minHY;
            if (horizontalCount >= 2 && maxHY > minHY + 0.001f)
            {
                hasCeiling = true;
                ceilingY = maxHY;
            }
        }

        if (verticalCount >= 1)
        {
            hasLeft = true;
            leftX = minVX;
            if (verticalCount >= 2 && maxVX > minVX + 0.001f)
            {
                hasRight = true;
                rightX = maxVX;
            }
        }
    }

    /// <summary>
    /// Builds a playable / camera world box from scene edges when Background bounds are missing.
    /// Missing sides use a large extent so only placed edges actually constrain.
    /// </summary>
    public static bool TryBuildFallbackBounds(out Bounds bounds)
    {
        GetCameraLimits(
            out bool hasFloor, out float floorY,
            out bool hasCeiling, out float ceilingY,
            out bool hasLeft, out float leftX,
            out bool hasRight, out float rightX);

        if (!hasFloor && !hasCeiling && !hasLeft && !hasRight)
        {
            bounds = default;
            return false;
        }

        const float Huge = 5000f;
        float minX = hasLeft ? leftX : -Huge;
        float maxX = hasRight ? rightX : Huge;
        float minY = hasFloor ? floorY : -Huge;
        float maxY = hasCeiling ? ceilingY : Huge;

        if (minX > maxX)
        {
            float mid = (minX + maxX) * 0.5f;
            minX = maxX = mid;
        }

        if (minY > maxY)
        {
            float mid = (minY + maxY) * 0.5f;
            minY = maxY = mid;
        }

        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        Vector3 size = new Vector3(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxY - minY), 1f);
        bounds = new Bounds(center, size);
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 p = transform.position;
        if (axis == Axis.Horizontal)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            Gizmos.DrawLine(new Vector3(p.x - 50f, p.y, p.z), new Vector3(p.x + 50f, p.y, p.z));
        }
        else
        {
            Gizmos.color = new Color(0.3f, 0.75f, 1f, 0.9f);
            Gizmos.DrawLine(new Vector3(p.x, p.y - 50f, p.z), new Vector3(p.x, p.y + 50f, p.z));
        }

        Gizmos.DrawWireSphere(p, 0.3f);
    }
#endif
}
