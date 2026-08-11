using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared sorting for charge aura / afterimages / slash ghosts.
/// Uses a <see cref="SortingGroup"/> so FX stay behind the character body, while the whole
/// character (body + FX) still sorts as one unit against background props.
/// </summary>
public static class CharacterEffectSorting
{
    /// <summary>In-group order for the character body (in front of aura / trails).</summary>
    public const int BodyOrderInGroup = 10;

    /// <summary>In-group order for charge aura (behind body, above trail ghosts).</summary>
    public const int AuraOrderInGroup = 0;

    /// <summary>
    /// Ensures a Sorting Group on the character root. Captures the body's world sort into the
    /// group once, then keeps the body in front within the group.
    /// </summary>
    public static SortingGroup EnsureHostSortingGroup(Component hostRoot, SpriteRenderer body)
    {
        if (hostRoot == null)
            return null;

        SortingGroup group = hostRoot.GetComponent<SortingGroup>();
        if (group == null)
        {
            int layerId = body != null ? body.sortingLayerID : 0;
            int worldOrder = body != null ? body.sortingOrder : 0;

            group = hostRoot.gameObject.AddComponent<SortingGroup>();
            group.sortingLayerID = layerId;
            group.sortingOrder = worldOrder;

            if (body != null)
                body.sortingOrder = BodyOrderInGroup;
        }
        else if (body != null)
        {
            body.sortingLayerID = group.sortingLayerID;
            if (body.sortingOrder < BodyOrderInGroup)
                body.sortingOrder = BodyOrderInGroup;
        }

        return group;
    }

    /// <summary>
    /// Charge aura (parented under the character): behind the body, inside the Sorting Group.
    /// </summary>
    public static void ApplyAuraBehindBody(SpriteRenderer aura, SpriteRenderer body, SortingGroup group)
    {
        if (aura == null)
            return;

        if (group != null)
        {
            aura.sortingLayerID = group.sortingLayerID;
            aura.sortingOrder = AuraOrderInGroup;
            if (body != null)
                body.sortingOrder = BodyOrderInGroup;
            return;
        }

        // Fallback without a group: one step behind the body (may still clip under some props).
        if (body == null)
            return;

        aura.sortingLayerID = body.sortingLayerID;
        aura.sortingOrder = body.sortingOrder - 1;
    }

    /// <summary>
    /// Afterimages / slash ghosts: behind the body (and usually behind aura) inside the group,
    /// or just behind the body if unparented / no group.
    /// </summary>
    public static void ApplyTrailBehindBody(
        SpriteRenderer trail,
        SpriteRenderer body,
        SortingGroup group,
        int trailIndex = 0)
    {
        if (trail == null)
            return;

        int index = Mathf.Max(0, trailIndex);

        if (group != null)
        {
            trail.sortingLayerID = group.sortingLayerID;
            // Below aura (0): -1, -2, -3… still part of the character group vs backgrounds.
            trail.sortingOrder = AuraOrderInGroup - 1 - index;
            if (body != null)
                body.sortingOrder = BodyOrderInGroup;
            return;
        }

        if (body == null)
            return;

        trail.sortingLayerID = body.sortingLayerID;
        trail.sortingOrder = body.sortingOrder - 1 - index;
    }
}
