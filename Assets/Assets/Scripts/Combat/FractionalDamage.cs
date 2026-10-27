using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Banks fractional projectile damage against int-HP targets (e.g. Bass purple buster at 0.5).
/// Whole points are applied when the residual crosses 1; leftovers stay on the target.
/// </summary>
public static class FractionalDamage
{
    private static readonly Dictionary<int, float> ResidualByInstanceId = new Dictionary<int, float>(64);

    /// <summary>
    /// Adds <paramref name="amount"/> to the target's residual and returns whole HP to apply now.
    /// </summary>
    public static int Consume(Component target, float amount)
    {
        if (target == null || amount <= 0f)
            return 0;

        int id = target.GetInstanceID();
        ResidualByInstanceId.TryGetValue(id, out float residual);
        residual += amount;

        int whole = Mathf.FloorToInt(residual);
        residual -= whole;

        if (residual > 0.0001f)
            ResidualByInstanceId[id] = residual;
        else
            ResidualByInstanceId.Remove(id);

        return whole;
    }

    public static void Clear(Component target)
    {
        if (target == null)
            return;
        ResidualByInstanceId.Remove(target.GetInstanceID());
    }
}
