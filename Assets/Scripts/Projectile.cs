using UnityEngine;

public enum ProjectileShotType
{
    Small,
    Medium,
    Big
}

/// <summary>
/// Configurable projectile for Kit (and later characters).
/// Set Shot Type in the Inspector on each prefab: Small, Medium, or Big.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [Header("Shot Type")]
    [Tooltip("Choose Small, Medium, or Big on each projectile prefab.")]
    [SerializeField] private ProjectileShotType shotType = ProjectileShotType.Small;

    [Header("Movement")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private bool rotateToDirection = true;

    [Header("Combat")]
    [SerializeField] private int damage = 1;
    [SerializeField] private bool destroyOnHit = true;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Optional Defaults By Type")]
    [Tooltip("If enabled, changing Shot Type in the Inspector fills in suggested speed/damage.")]
    [SerializeField] private bool applySuggestedStatsWhenTypeChanges = true;

    public ProjectileShotType ShotType => shotType;
    public int Damage => damage;
    public float Speed => speed;

    private Vector2 direction = Vector2.right;
    private Transform owner;
    private bool launched;
    private ProjectileShotType lastValidatedType;

    /// <summary>
    /// Launch using this prefab's Inspector speed/damage.
    /// </summary>
    public void Launch(Vector2 dir, Transform shotOwner)
    {
        Launch(dir, speed, damage, shotOwner);
    }

    /// <summary>
    /// Launch with optional runtime overrides (Kit can pass its own values if desired).
    /// </summary>
    public void Launch(Vector2 dir, float moveSpeed, int dmg, Transform shotOwner)
    {
        direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        speed = moveSpeed;
        damage = dmg;
        owner = shotOwner;
        launched = true;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        Destroy(gameObject, Mathf.Max(0.05f, lifetime));
    }

    private void Update()
    {
        if (!launched)
            return;

        transform.position += (Vector3)(direction * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched)
            return;

        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
            return;

        // Spread / overlapping shots must not cancel each other.
        if (other.GetComponentInParent<Projectile>() != null)
            return;

        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;

        // Melee / grapple AttackBox colliders are tools — never deal body damage through them.
        // Only playable Malice can dissipate (destroy) projectiles with an active slash/grapple box.
        AttackHitbox tool = other.GetComponent<AttackHitbox>();
        if (tool == null)
            tool = other.GetComponentInParent<AttackHitbox>();

        if (tool != null)
        {
            PlayerController toolPlayer = tool.Owner;
            if (toolPlayer == null)
                toolPlayer = tool.GetComponentInParent<PlayerController>();

            if (toolPlayer != null)
            {
                if (tool.IsActive && toolPlayer is MalicePlayerController)
                    Destroy(gameObject);
                return;
            }

            // Boss AttackBox tool — ignore for body damage; Boss Malice slash can deflect / dissipate.
            Boss toolBoss = tool.OwnerBoss;
            if (toolBoss == null)
                toolBoss = tool.GetComponentInParent<Boss>();
            if (toolBoss != null && tool.gameObject != toolBoss.gameObject)
            {
                if (tool.IsActive &&
                    toolBoss is BossMalice bossMalice &&
                    bossMalice.CanDeflectProjectilesWithSlash())
                {
                    if (shotType == ProjectileShotType.Small)
                    {
                        ReflectTowardShooter(bossMalice.transform);
                        return;
                    }

                    if (shotType == ProjectileShotType.Medium)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    // Big shots are too strong — pass through the slash and keep flying.
                }

                return;
            }
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable != null && !damageable.IsDead)
        {
            // Boss-owned shots never damage the crystal.
            if (damageable is Crystal &&
                owner != null &&
                owner.GetComponentInParent<Boss>() != null)
            {
                if (destroyOnHit)
                    Destroy(gameObject);
                return;
            }

            if (damageable is Boss boss)
                boss.TakeDamage(damage, shotType);
            else if (damageable is PlayerController player)
                player.TakeDamage(damage, owner != null ? owner : transform);
            else
                damageable.TakeDamage(damage);
        }

        if (destroyOnHit)
            Destroy(gameObject);
    }

    /// <summary>
    /// Send this shot back at its original owner (or Active player), now owned by the deflector.
    /// </summary>
    private void ReflectTowardShooter(Transform newOwner)
    {
        Vector2 newDir = -direction;

        if (owner != null)
        {
            Vector2 toOwner = (Vector2)owner.position - (Vector2)transform.position;
            if (toOwner.sqrMagnitude > 0.0001f)
                newDir = toOwner.normalized;
        }
        else if (PlayerController.Active != null)
        {
            Vector2 toPlayer = (Vector2)PlayerController.Active.transform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f)
                newDir = toPlayer.normalized;
        }

        owner = newOwner;
        direction = newDir.sqrMagnitude > 0.0001f ? newDir.normalized : Vector2.left;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void ApplySuggestedStatsForType(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Small:
                speed = 12f;
                damage = 1;
                lifetime = 3f;
                break;
            case ProjectileShotType.Medium:
                speed = 11f;
                damage = 2;
                lifetime = 3.25f;
                break;
            case ProjectileShotType.Big:
                speed = 10f;
                damage = 4;
                lifetime = 3.5f;
                break;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        lifetime = Mathf.Max(0.05f, lifetime);
        speed = Mathf.Max(0.1f, speed);
        damage = Mathf.Max(0, damage);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;

        if (applySuggestedStatsWhenTypeChanges && shotType != lastValidatedType)
        {
            ApplySuggestedStatsForType(shotType);
            lastValidatedType = shotType;
        }
    }

    private void Reset()
    {
        ApplySuggestedStatsForType(shotType);
        lastValidatedType = shotType;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }
#endif
}
