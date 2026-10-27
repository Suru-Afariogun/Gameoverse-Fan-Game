using UnityEngine;

/// <summary>
/// Bass Metal Leaf blade — MM2-style spinning cutter. Uses <see cref="Projectile"/> for movement/hits,
/// then spins in place and despawns past the camera by a margin (spaces).
/// </summary>
[RequireComponent(typeof(Projectile))]
public class BassMetalBlade : MonoBehaviour
{
    [SerializeField] private float spinDegreesPerSecond = 720f;
    [Tooltip("Despawn once this many spaces past the camera viewport edge.")]
    [SerializeField] private float cameraMarginSpaces = 5f;
    [SerializeField] private float spaceSize = 1f;

    private Projectile projectile;
    private bool launched;
    private BassPlayerController bassOwner;
    private float bossAbilityScale = 1f;
    private Vector3 baseLocalScale = Vector3.one;
    private bool capturedBaseScale;

    public Projectile Projectile => projectile != null ? projectile : GetComponent<Projectile>();

    private void Awake()
    {
        projectile = GetComponent<Projectile>();
        CaptureBaseScale();
    }

    public void Launch(Vector2 dir, float speed, float damage, Transform owner)
    {
        if (projectile == null)
            projectile = GetComponent<Projectile>();
        projectile.Launch(dir, speed, damage, owner);
        launched = true;
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

    private void Update()
    {
        if (!launched || projectile == null || projectile.IsResolvingHit)
            return;

        ApplyDoubleGearScale();

        float dt = Time.deltaTime;
        if (HyperSpeedWorldSlow.IsActive && HyperSpeedWorldSlow.SlowsPlayer)
            dt = HyperSpeedWorldSlow.WorldDeltaTime;

        transform.Rotate(0f, 0f, spinDegreesPerSecond * dt, Space.Self);

        if (IsFarPastCamera())
            projectile.ClashAndDespawn();
    }

    private bool IsFarPastCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;

        float margin = Mathf.Max(0f, cameraMarginSpaces) * Mathf.Max(0.01f, spaceSize);
        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        // Outside 0..1 viewport by margin converted roughly via world units on ortho camera.
        if (cam.orthographic)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 c = cam.transform.position;
            Vector3 p = transform.position;
            return p.x < c.x - halfW - margin
                || p.x > c.x + halfW + margin
                || p.y < c.y - halfH - margin
                || p.y > c.y + halfH + margin;
        }

        return vp.z < 0f
            || vp.x < -0.15f || vp.x > 1.15f
            || vp.y < -0.15f || vp.y > 1.15f;
    }
}
