using UnityEngine;

/// <summary>
/// Tiny bubble remnant that bursts outward from a popped Bubble Bullet, then falls and fades.
/// Use the authored "Bubble bits" prefab (Test Room art), not a scaled Bubble Bullet.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BassBubbleBit : MonoBehaviour
{
    [SerializeField] private float gravity = 16f;
    [SerializeField] private float lifetime = 0.55f;
    [SerializeField] private float fadeStartNormalized = 0.45f;

    private SpriteRenderer spriteRenderer;
    private Vector2 velocity;
    private float age;
    private float startAlpha = 1f;
    private bool launched;

    public void Launch(Vector2 initialVelocity, int sortingOrder = -1)
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            startAlpha = spriteRenderer.color.a;
            if (sortingOrder >= 0)
                spriteRenderer.sortingOrder = sortingOrder;
        }

        velocity = initialVelocity;
        age = 0f;
        launched = true;
    }

    private void Update()
    {
        if (!launched)
            return;

        float dt = Time.deltaTime;
        age += dt;
        velocity.y -= gravity * dt;
        transform.position += (Vector3)(velocity * dt);

        float life = Mathf.Max(0.05f, lifetime);
        float t = Mathf.Clamp01(age / life);
        if (spriteRenderer != null)
        {
            float fadeT = t <= fadeStartNormalized
                ? 0f
                : Mathf.InverseLerp(fadeStartNormalized, 1f, t);
            Color c = spriteRenderer.color;
            c.a = Mathf.Lerp(startAlpha, 0f, fadeT);
            spriteRenderer.color = c;
        }

        if (t >= 1f)
            Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        gravity = Mathf.Max(0f, gravity);
        lifetime = Mathf.Max(0.05f, lifetime);
        fadeStartNormalized = Mathf.Clamp01(fadeStartNormalized);
    }
#endif
}
