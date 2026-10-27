using UnityEngine;

/// <summary>
/// Bass QuickTime boomerang — Quick Man style: fly out, hover 0.5s, reverse along the same path,
/// then fade out. Full player time so it still moves during Time Stop.
/// </summary>
[RequireComponent(typeof(Projectile))]
public class BassQuickBoomerang : MonoBehaviour
{
    private enum Phase
    {
        Outbound,
        Hover,
        Return,
        Fading
    }

    [SerializeField] private float spinDegreesPerSecond = 720f;
    [SerializeField] private float hoverSeconds = 0.5f;
    [SerializeField] private float fadeOutSeconds = 0.28f;

    private Projectile projectile;
    private SpriteRenderer spriteRenderer;
    private Vector2 fireDirection;
    private float moveSpeed;
    private float outDistance;
    private float traveled;
    private float hoverTimer;
    private float fadeTimer;
    private float startAlpha = 1f;
    private Vector3 lastPosition;
    private Phase phase;
    private bool launched;
    private BassPlayerController bassOwner;
    private float bossAbilityScale = 1f;
    private Vector3 baseLocalScale = Vector3.one;
    private bool capturedBaseScale;

    public Projectile Projectile => projectile != null ? projectile : GetComponent<Projectile>();

    private void Awake()
    {
        projectile = GetComponent<Projectile>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        CaptureBaseScale();
    }

    /// <summary>Tracks Double Gear + ability: +30% size while DG is active.</summary>
    public void ConfigureDoubleGearBoosts(BassPlayerController bass)
    {
        bassOwner = bass;
        bossAbilityScale = 1f;
        CaptureBaseScale();
        ApplyDoubleGearScale();
    }

    /// <summary>Boss Bass Double Gear + Dual: same +30% size without a playable Bass owner.</summary>
    public void ConfigureBossAbilityBoosts(float scale)
    {
        bassOwner = null;
        bossAbilityScale = Mathf.Max(0.1f, scale);
        CaptureBaseScale();
        ApplyDoubleGearScale();
    }

    private void CaptureBaseScale()
    {
        if (capturedBaseScale)
            return;
        baseLocalScale = transform.localScale;
        if (baseLocalScale.sqrMagnitude < 0.0001f)
            baseLocalScale = Vector3.one;
        capturedBaseScale = true;
    }

    private void ApplyDoubleGearScale()
    {
        float s = bassOwner != null ? bassOwner.GetAbilityProjectileScale() : bossAbilityScale;
        transform.localScale = baseLocalScale * s;
    }

    public void Launch(
        Vector2 dir,
        float speed,
        float damage,
        float outDistanceSpaces,
        float spaceSize,
        Transform owner)
    {
        if (projectile == null)
            projectile = GetComponent<Projectile>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        fireDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        moveSpeed = Mathf.Max(0.1f, speed);
        outDistance = Mathf.Max(0.5f, outDistanceSpaces * Mathf.Max(0.01f, spaceSize));
        traveled = 0f;
        hoverTimer = 0f;
        fadeTimer = 0f;
        phase = Phase.Outbound;
        launched = true;

        if (spriteRenderer != null)
            startAlpha = spriteRenderer.color.a;

        projectile.ConfigurePierceAndContinue(0.16f);
        // Out + hover + return + fade budget.
        float life = Mathf.Max(4f, (outDistance * 2.2f) / moveSpeed + hoverSeconds + fadeOutSeconds + 0.5f);
        projectile.Launch(fireDirection, moveSpeed, damage, life, owner, environmentSpawnGraceSeconds: 0.05f);
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (!launched || projectile == null)
            return;

        ApplyDoubleGearScale();

        float dt = Time.deltaTime;
        transform.Rotate(0f, 0f, spinDegreesPerSecond * dt, Space.Self);

        if (phase == Phase.Hover)
        {
            hoverTimer += dt;
            if (hoverTimer >= hoverSeconds)
                BeginReturn();
            return;
        }

        if (phase == Phase.Fading)
        {
            fadeTimer += dt;
            float t = Mathf.Clamp01(fadeTimer / Mathf.Max(0.05f, fadeOutSeconds));
            if (spriteRenderer != null)
            {
                Color c = spriteRenderer.color;
                c.a = Mathf.Lerp(startAlpha, 0f, t);
                spriteRenderer.color = c;
            }

            if (t >= 1f)
                Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (!launched || projectile == null || !projectile.IsLaunched || projectile.IsResolvingHit)
            return;
        if (phase == Phase.Hover || phase == Phase.Fading)
            return;

        Vector3 pos = transform.position;
        traveled += Vector2.Distance(pos, lastPosition);
        lastPosition = pos;

        if (phase == Phase.Outbound)
        {
            if (traveled >= outDistance)
                BeginHover();
            return;
        }

        if (phase == Phase.Return && traveled >= outDistance)
            BeginFadeOut();
    }

    private void BeginHover()
    {
        phase = Phase.Hover;
        hoverTimer = 0f;
        traveled = 0f;
        projectile.SetSpeed(0f);
    }

    private void BeginReturn()
    {
        phase = Phase.Return;
        traveled = 0f;
        lastPosition = transform.position;
        projectile.SetSpeed(moveSpeed);
        projectile.SetDirection(-fireDirection);
    }

    private void BeginFadeOut()
    {
        phase = Phase.Fading;
        fadeTimer = 0f;
        projectile.SetSpeed(0f);
        if (spriteRenderer != null)
            startAlpha = spriteRenderer.color.a;
    }
}
