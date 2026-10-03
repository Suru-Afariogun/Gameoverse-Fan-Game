using UnityEngine;

/// <summary>
/// Added at runtime to a launched hand <see cref="Projectile"/> (Boss Count's Quick Man throw).
/// Flies straight for a set distance, then turns back and homes on the thrower until it reaches him.
/// </summary>
[RequireComponent(typeof(Projectile))]
public class CountBoomerangHand : MonoBehaviour
{
    private Projectile projectile;
    private Transform thrower;
    private float outDistance;
    private float turnDegreesPerSecond;
    private float traveled;
    private bool returning;
    private Vector3 lastPosition;

    public static CountBoomerangHand Attach(Projectile shot, Transform thrower, float outDistance, float turnDegreesPerSecond)
    {
        if (shot == null)
            return null;

        CountBoomerangHand hand = shot.gameObject.AddComponent<CountBoomerangHand>();
        hand.projectile = shot;
        hand.thrower = thrower;
        hand.outDistance = Mathf.Max(0.5f, outDistance);
        hand.turnDegreesPerSecond = Mathf.Max(30f, turnDegreesPerSecond);
        hand.lastPosition = shot.transform.position;
        return hand;
    }

    private void FixedUpdate()
    {
        if (projectile == null || !projectile.IsLaunched || projectile.IsResolvingHit)
            return;

        Vector3 pos = transform.position;
        traveled += Vector2.Distance(pos, lastPosition);
        lastPosition = pos;

        if (!returning)
        {
            if (traveled >= outDistance)
                returning = true;
            return;
        }

        if (thrower == null)
            return;

        Vector2 toThrower = (Vector2)(thrower.position - pos);
        if (toThrower.magnitude <= 0.6f)
        {
            Destroy(gameObject);
            return;
        }

        float maxStep = turnDegreesPerSecond * Mathf.Deg2Rad * HyperSpeedWorldSlow.WorldFixedDeltaTime;
        Vector3 turned = Vector3.RotateTowards(projectile.Direction, toThrower.normalized, maxStep, 0f);
        projectile.SetDirection(turned);
    }
}
