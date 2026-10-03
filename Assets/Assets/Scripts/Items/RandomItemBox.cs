using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Breakable box: takes damage from shots and melee, then bursts in a smoke explosion (same motion as
/// the Blocker Bot death smoke) and pops out one random item from <see cref="itemPool"/>. Never respawns.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RandomItemBox : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 4;
    [Tooltip("On: shots / players pass through the box (like the boss crystal). Off: solid box.")]
    [SerializeField] private bool phaseThrough = true;

    [Header("Hit Flash")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.55f, 0.55f, 1f);
    [SerializeField] private float hitFlashSeconds = 0.08f;
    [SerializeField] private float hitShakeDistance = 0.08f;

    [Header("Drops (one picked at random, equal odds)")]
    [Tooltip("Scene items or prefabs. Scene items are copied at start, so the originals stay collectable.")]
    [SerializeField] private List<CollectablePickupBase> itemPool = new List<CollectablePickupBase>();

    [Header("Explosion Smoke (Blocker Bot style)")]
    [Tooltip("Particle templates; scene objects here are hidden at start and cloned on break.")]
    [SerializeField] private GameObject[] explosionParticles;
    [SerializeField] private int smokeCopiesPerParticle = 2;
    [SerializeField] private float smokeScatterRadius = 0.55f;
    [SerializeField] private float smokeRiseDistance = 4.5f;
    [SerializeField] private float smokeRiseSpeed = 3.2f;

    private readonly List<CollectablePickupBase> dropTemplates = new List<CollectablePickupBase>();
    private int currentHealth;
    private Collider2D boxCollider;
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;
    private Vector3 restLocalPosition;
    private Coroutine hitFlashRoutine;

    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        currentHealth = Mathf.Max(1, maxHealth);
        boxCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
        restLocalPosition = transform.localPosition;

        if (boxCollider != null)
            boxCollider.isTrigger = phaseThrough;

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.simulated = true;

        CacheDropTemplates();

        if (explosionParticles != null)
        {
            for (int i = 0; i < explosionParticles.Length; i++)
            {
                GameObject particle = explosionParticles[i];
                if (particle != null && particle.scene.IsValid())
                    particle.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Copies scene-placed pool items under a hidden holder (Awake doesn't run under an inactive parent),
    /// so collecting a placed original never empties the box's pool.
    /// </summary>
    private void CacheDropTemplates()
    {
        dropTemplates.Clear();
        Transform holder = null;
        for (int i = 0; i < itemPool.Count; i++)
        {
            CollectablePickupBase item = itemPool[i];
            if (item == null)
                continue;

            if (!item.gameObject.scene.IsValid())
            {
                dropTemplates.Add(item);
                continue;
            }

            if (holder == null)
            {
                GameObject holderObject = new GameObject($"{name} Drop Templates");
                holderObject.SetActive(false);
                holder = holderObject.transform;
            }

            CollectablePickupBase copy = Instantiate(item, holder);
            copy.name = item.name;
            dropTemplates.Add(copy);
        }
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (IsDead)
        {
            Break();
            return;
        }

        SoundManager.Instance?.PlayCrystalHit();
        if (hitFlashRoutine != null)
            StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlash());
    }

    private IEnumerator HitFlash()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = hitFlashColor;

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, hitFlashSeconds);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float side = Mathf.Sin(elapsed / duration * Mathf.PI * 4f);
            transform.localPosition = restLocalPosition + Vector3.right * (side * hitShakeDistance);
            yield return null;
        }

        transform.localPosition = restLocalPosition;
        if (spriteRenderer != null)
            spriteRenderer.color = baseColor;
        hitFlashRoutine = null;
    }

    private void Break()
    {
        transform.localPosition = restLocalPosition;
        Vector3 center = boxCollider != null ? boxCollider.bounds.center : transform.position;

        VisualEffects.SpawnBlockerSmokeDeath(
            explosionParticles,
            center,
            Mathf.Max(1, smokeCopiesPerParticle),
            Mathf.Max(0f, smokeScatterRadius),
            Mathf.Max(0f, smokeRiseDistance),
            Mathf.Max(0.1f, smokeRiseSpeed),
            spriteRenderer);
        SoundManager.Instance?.PlayEnemyExplosion();

        SpawnRandomDrop(center);
        Destroy(gameObject);
    }

    private void SpawnRandomDrop(Vector3 center)
    {
        if (dropTemplates.Count == 0)
            return;

        CollectablePickupBase template = dropTemplates[Random.Range(0, dropTemplates.Count)];
        if (template == null)
            return;

        CollectablePickupBase drop = Instantiate(template, new Vector3(center.x, center.y, template.transform.position.z), template.transform.rotation);
        drop.name = template.name;

        float angle = Random.Range(65f, 115f) * Mathf.Deg2Rad;
        drop.LaunchBurst(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
        SoundManager.Instance?.PlayItemDrop();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        hitFlashSeconds = Mathf.Max(0.01f, hitFlashSeconds);
        hitShakeDistance = Mathf.Max(0f, hitShakeDistance);
        smokeCopiesPerParticle = Mathf.Max(1, smokeCopiesPerParticle);
        smokeScatterRadius = Mathf.Max(0f, smokeScatterRadius);
        smokeRiseDistance = Mathf.Max(0f, smokeRiseDistance);
        smokeRiseSpeed = Mathf.Max(0.1f, smokeRiseSpeed);
    }
#endif
}
