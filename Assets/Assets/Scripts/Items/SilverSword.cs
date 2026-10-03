using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One Summonable Silver Sword. A <see cref="SilverSwordSquad"/> moves it; the sword only aims, spins and hits.
/// While armed it phases through everything and hits each target once per strike.
/// Player-owned swords hit bosses / enemies / crystals / boxes; boss-owned swords hit the player.
/// </summary>
public class SilverSword : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Direction the blade tip points in the art, in degrees (-90 = down, 90 = up, 0 = right, 180 = left).")]
    [SerializeField] private float tipAngle = -90f;
    [Tooltip("Hit area as a fraction of the sprite size (in the art's own axes).")]
    [SerializeField] private Vector2 hitboxScale = new Vector2(0.6f, 0.9f);
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private int sortingOffset = 4;

    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(24);
    private readonly HashSet<int> hitIds = new HashSet<int>();

    private Transform owner;
    private bool ownerIsPlayer;
    private int damage;
    private bool armed;
    private float spinDegreesPerSecond;
    private Color baseColor = Color.white;

    public SpriteRenderer Renderer => spriteRenderer;
    public bool Armed => armed;
    public Vector2 Position => transform.position;

    /// <summary>World direction the blade tip is pointing.</summary>
    public Vector2 TipDirection
    {
        get
        {
            float rad = (transform.eulerAngles.z + tipAngle) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }

    /// <summary>World distance from the sword's center to its tip.</summary>
    public float TipLength => ArtExtent(tipAngle);

    /// <summary>Half the blade's width (across the tip direction).</summary>
    public float HalfThickness => ArtExtent(tipAngle + 90f);

    public Vector2 TipPosition => Position + TipDirection * TipLength;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;

        // Swords never collide with anything; hits are overlap checks.
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;
    }

    public void Init(Transform summoner, int hitDamage, SortingGroup hostGroup, SpriteRenderer hostRenderer)
    {
        owner = summoner;
        ownerIsPlayer = summoner != null && summoner.GetComponentInParent<PlayerController>() != null;
        damage = Mathf.Max(0, hitDamage);
        if (spriteRenderer != null)
            CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, hostGroup, hostRenderer, sortingOffset);
    }

    public void PointAt(Vector2 direction)
    {
        spinDegreesPerSecond = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.Euler(0f, 0f, AngleFor(direction));
    }

    /// <summary>Turns the tip toward <paramref name="direction"/> by at most <paramref name="maxDegrees"/>.</summary>
    public void TurnToward(Vector2 direction, float maxDegrees)
    {
        spinDegreesPerSecond = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        float z = Mathf.MoveTowardsAngle(transform.eulerAngles.z, AngleFor(direction), Mathf.Max(0f, maxDegrees));
        transform.rotation = Quaternion.Euler(0f, 0f, z);
    }

    public void SetSpin(float degreesPerSecond)
    {
        spinDegreesPerSecond = degreesPerSecond;
    }

    public void Arm()
    {
        armed = true;
        hitIds.Clear();
    }

    public void Disarm()
    {
        armed = false;
    }

    public void SetAlpha(float alpha)
    {
        if (spriteRenderer == null)
            return;

        Color c = baseColor;
        c.a = baseColor.a * Mathf.Clamp01(alpha);
        spriteRenderer.color = c;
    }

    private float AngleFor(Vector2 direction)
    {
        return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - tipAngle;
    }

    private float ArtExtent(float artAngle)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return 0.5f;

        Vector3 ext = spriteRenderer.sprite.bounds.extents;
        Vector3 scale = transform.lossyScale;
        float rad = artAngle * Mathf.Deg2Rad;
        return Mathf.Abs(Mathf.Cos(rad)) * ext.x * Mathf.Abs(scale.x) + Mathf.Abs(Mathf.Sin(rad)) * ext.y * Mathf.Abs(scale.y);
    }

    private void Update()
    {
        if (Mathf.Abs(spinDegreesPerSecond) > 0.01f)
        {
            float dt = Time.deltaTime * SilverSwordSquad.TimeScaleFor(ownerIsPlayer);
            transform.Rotate(0f, 0f, spinDegreesPerSecond * dt);
        }
    }

    private void LateUpdate()
    {
        if (armed)
            DetectHits();
    }

    // ---------- Hits ----------

    private void DetectHits()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null || damage <= 0)
            return;

        Bounds local = spriteRenderer.sprite.bounds;
        Vector3 scale = transform.lossyScale;
        Vector2 size = new Vector2(
            local.size.x * Mathf.Abs(scale.x) * hitboxScale.x,
            local.size.y * Mathf.Abs(scale.y) * hitboxScale.y);
        if (size.x <= 0.001f || size.y <= 0.001f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(hitLayers);

        Overlaps.Clear();
        Physics2D.OverlapBox(transform.TransformPoint(local.center), size, transform.eulerAngles.z, filter, Overlaps);
        for (int i = 0; i < Overlaps.Count; i++)
            TryHit(Overlaps[i]);
    }

    private void TryHit(Collider2D other)
    {
        if (other == null)
            return;
        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
            return;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other) || other.GetComponent<AttackHitbox>() != null)
            return;
        if (other.GetComponentInParent<Projectile>() != null || other.GetComponentInParent<MaliceSlashProjectile>() != null ||
            other.GetComponentInParent<SilverSword>() != null)
            return;

        if (!ownerIsPlayer)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && !player.IsDead && hitIds.Add(player.GetInstanceID()))
                player.TakeDamage(damage, owner != null ? owner : transform);
            return;
        }

        Crystal crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            if (!crystal.IsDead && hitIds.Add(crystal.GetInstanceID()))
                crystal.TakeDamage(damage);
            return;
        }

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (!boss.IsDead && hitIds.Add(boss.GetInstanceID()))
                boss.TakeDamage(damage);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy is Component enemyComponent)
        {
            if (!enemy.IsDead && hitIds.Add(enemyComponent.GetInstanceID()))
                enemy.TakeDamage(damage);
            return;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable is Component damageableComponent && !damageable.IsDead &&
            hitIds.Add(damageableComponent.GetInstanceID()))
            damageable.TakeDamage(damage);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        hitboxScale = new Vector2(Mathf.Clamp(hitboxScale.x, 0.05f, 1.5f), Mathf.Clamp(hitboxScale.y, 0.05f, 1.5f));
    }
#endif
}
