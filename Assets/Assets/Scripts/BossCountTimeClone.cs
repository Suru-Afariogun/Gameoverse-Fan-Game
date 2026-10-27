using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Boss Count afterimage clone: stationary, fires Short Hands at the player (playable Count clone parity).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BossCountTimeClone : MonoBehaviour, IDamageable
{
    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(32);

    private BossCount owner;
    private Projectile shotPrefab;
    private SpriteRenderer spriteRenderer;
    private BoxCollider2D hurtbox;
    private Color baseColor;
    private int health;
    private float shootInterval;
    private float searchRadius;
    private float nextVolleyAt;
    private float fadeOutStartedAt = -1f;
    private bool artFacesLeft = true;

    private const float FadeOutSeconds = 0.2f;

    public bool IsDead => health <= 0;

    /// <summary>Immediate remove of every clone belonging to this boss (defeat cinema start).</summary>
    public static void DestroyAllForOwner(BossCount boss)
    {
        if (boss == null)
            return;

        BossCountTimeClone[] clones = FindObjectsByType<BossCountTimeClone>(FindObjectsSortMode.None);
        for (int i = 0; i < clones.Length; i++)
        {
            if (clones[i] != null && clones[i].owner == boss)
                Destroy(clones[i].gameObject);
        }
    }

    public static BossCountTimeClone Spawn(
        BossCount owner,
        Vector3 position,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile shotPrefab,
        int damage,
        float shootInterval,
        float searchRadius)
    {
        if (owner == null || sprite == null || shotPrefab == null)
            return null;

        var go = new GameObject($"{owner.name}_TimeClone");
        go.transform.position = position;
        var clone = go.AddComponent<BossCountTimeClone>();
        clone.Init(owner, sprite, flipX, tint, shotPrefab, damage, shootInterval, searchRadius);
        return clone;
    }

    private void Init(
        BossCount cloneOwner,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile prefab,
        int hp,
        float interval,
        float radius)
    {
        owner = cloneOwner;
        shotPrefab = prefab;
        health = Mathf.Max(1, hp);
        shootInterval = Mathf.Max(0.15f, interval);
        searchRadius = Mathf.Max(2f, radius);
        baseColor = tint;
        nextVolleyAt = Time.time + shootInterval * 0.4f;

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.flipX = flipX;
        spriteRenderer.color = tint;

        SpriteRenderer body = owner.GetComponent<SpriteRenderer>();
        SortingGroup group = owner.EffectSortingGroup;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, group, body, -1);

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        hurtbox = gameObject.AddComponent<BoxCollider2D>();
        hurtbox.isTrigger = true;
        hurtbox.size = new Vector2(0.9f, 1.4f);
    }

    private void Update()
    {
        if (fadeOutStartedAt >= 0f)
        {
            float u = (Time.time - fadeOutStartedAt) / FadeOutSeconds;
            if (spriteRenderer != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a, 0f, Mathf.Clamp01(u));
                spriteRenderer.color = c;
            }

            if (u >= 1f)
                Destroy(gameObject);
            return;
        }

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        if (Time.time < nextVolleyAt)
            return;

        nextVolleyAt = Time.time + shootInterval;
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
            return;

        Vector2 aim = (Vector2)(player.transform.position - transform.position);
        if (aim.sqrMagnitude < 0.0001f)
            aim = Vector2.left;
        aim.Normalize();
        Face(aim.x);
        FireShot(aim);
    }

    private void FireShot(Vector2 dir)
    {
        if (shotPrefab == null || owner == null)
            return;

        Projectile shot = Instantiate(shotPrefab, transform.position + (Vector3)(dir * 0.5f), Quaternion.identity);
        shot.Launch(dir, shotPrefab.Speed, shotPrefab.Damage, owner.transform);
        SoundManager.Instance?.PlayCountFireRapid();
    }

    private void Face(float x)
    {
        if (spriteRenderer == null || Mathf.Abs(x) < 0.01f)
            return;
        bool right = x > 0f;
        spriteRenderer.flipX = artFacesLeft ? right : !right;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;
        health -= amount;
        if (health <= 0)
            BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        if (fadeOutStartedAt >= 0f)
            return;
        fadeOutStartedAt = Time.time;
        health = 0;
    }
}
