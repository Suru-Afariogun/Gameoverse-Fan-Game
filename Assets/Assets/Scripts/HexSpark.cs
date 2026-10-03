using UnityEngine;

/// <summary>
/// One of Hex's sparks (from Little Program Harlie): pops out at a random angle, falls and fades.
/// </summary>
public class HexSpark : MonoBehaviour
{
    [Tooltip("How long the spark exists before it is destroyed.")]
    [SerializeField] private float lifetime = 0.7f;
    [Tooltip("How long the fade-out takes at the end of the lifetime.")]
    [SerializeField] private float fadeDuration = 0.45f;
    [SerializeField] private Vector2 speedMin = new Vector2(-2.5f, 1.5f);
    [SerializeField] private Vector2 speedMax = new Vector2(2.5f, 4f);
    [SerializeField] private float gravity = 9f;

    private SpriteRenderer spriteRenderer;
    private Vector2 velocity;
    private float age;
    private float startAlpha = 1f;

    public SpriteRenderer Renderer => spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            startAlpha = spriteRenderer.color.a;

        velocity = new Vector2(Random.Range(speedMin.x, speedMax.x), Random.Range(speedMin.y, speedMax.y));
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        velocity.y -= gravity * dt;
        transform.position += (Vector3)(velocity * dt);

        if (spriteRenderer != null)
        {
            float fadeStart = Mathf.Max(0f, lifetime - fadeDuration);
            float t = fadeDuration <= 0.0001f ? (age >= lifetime ? 1f : 0f) : Mathf.InverseLerp(fadeStart, lifetime, age);
            Color c = spriteRenderer.color;
            c.a = Mathf.Lerp(startAlpha, 0f, t);
            spriteRenderer.color = c;
        }

        if (age >= lifetime)
            Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        lifetime = Mathf.Max(0.05f, lifetime);
        fadeDuration = Mathf.Clamp(fadeDuration, 0f, lifetime);
        gravity = Mathf.Max(0f, gravity);
    }
#endif
}
