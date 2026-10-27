using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One Crash Bomb explosion puff — damages once, then fades with its clip.</summary>
[RequireComponent(typeof(Collider2D))]
public class BassCrashBombExplosion : MonoBehaviour
{
    [SerializeField] private float damage = 1f;
    [SerializeField] private float knockbackSpaces = 3f;
    [SerializeField] private float spaceSize = 1f;
    [SerializeField] private float lifetime = 0.35f;
    [SerializeField] private string playStateName = "Crash Bomb Explosion";
    [SerializeField] private GameVisualEffect stunnedEffectPrefab;

    private Collider2D hitCollider;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private SortingGroup hostGroup;
    private SpriteRenderer hostBody;
    private bool armed;
    private bool hostileToPlayer;
    private float age;
    private readonly Collider2D[] overlaps = new Collider2D[16];
    private readonly System.Collections.Generic.HashSet<int> hitIds = new System.Collections.Generic.HashSet<int>();

    public void ConfigureHostSorting(SortingGroup group, SpriteRenderer body)
    {
        hostGroup = group;
        hostBody = body;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    /// <summary>Boss Bass explosions hurt the player instead of bosses / enemies.</summary>
    public void SetHostile(bool hostile)
    {
        hostileToPlayer = hostile;
    }

    public void Arm(float dmg, float knockSpaces, float worldSpace, GameVisualEffect stunnedPrefab)
    {
        damage = dmg;
        knockbackSpaces = knockSpaces;
        spaceSize = worldSpace;
        stunnedEffectPrefab = stunnedPrefab;
        armed = true;
        age = 0f;

        if (animator == null)
            animator = GetComponent<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }

        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);

        if (animator != null && !string.IsNullOrEmpty(playStateName)
            && animator.HasState(0, Animator.StringToHash(playStateName)))
            animator.Play(playStateName, 0, 0f);
    }

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (!armed)
            return;

        age += Time.deltaTime;
        DealHits();

        if (age >= lifetime)
            Destroy(gameObject);
    }

    private void DealHits()
    {
        if (hitCollider == null)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        int count = Physics2D.OverlapCollider(hitCollider, filter, overlaps);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlaps[i];
            if (other == null)
                continue;

            Component key = hostileToPlayer
                ? other.GetComponentInParent<PlayerController>() as Component
                : other.GetComponentInParent<Boss>() as Component
                    ?? other.GetComponentInParent<ICommonEnemy>() as Component;
            if (key == null)
                continue;
            if (!hitIds.Add(key.GetInstanceID()))
                continue;

            BassCrashHeatCombat.ApplyExplosionHit(
                other, damage, knockbackSpaces, spaceSize, transform, stunnedEffectPrefab, hostileToPlayer);
        }
    }
}
