using System.Collections.Generic;
using UnityEngine;

public enum ClockworkPlatformColor
{
    Green = 0,
    Red = 1
}

/// <summary>
/// Green / red platforms that trade places on a shared clock: while one color is solid, the other is a
/// see-through ghost with no collision. Each phase lasts <see cref="ClockworkPlatformClock.PhaseSeconds"/>,
/// ticking every second and tocking as they switch (only audible while one is on screen).
/// </summary>
[DisallowMultipleComponent]
public class ClockworkPlatform : MonoBehaviour
{
    [SerializeField] private ClockworkPlatformColor platformColor = ClockworkPlatformColor.Green;
    [Tooltip("Sprite alpha multiplier while this platform's color is switched off.")]
    [Range(0f, 1f)]
    [SerializeField] private float ghostAlpha = 0.25f;

    private static readonly List<ClockworkPlatform> Active = new List<ClockworkPlatform>();
    private static PhysicsMaterial2D sharedFrictionlessMaterial;

    private Collider2D[] solidColliders;
    private SpriteRenderer[] renderers;
    private float[] baseAlphas;

    public static IReadOnlyList<ClockworkPlatform> All => Active;
    public ClockworkPlatformColor PlatformColor => platformColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active.Clear();
        sharedFrictionlessMaterial = null;
    }

    private void Awake()
    {
        if (TryGetComponent(out Rigidbody2D body))
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0)
            gameObject.layer = ground;

        if (sharedFrictionlessMaterial == null)
            sharedFrictionlessMaterial = new PhysicsMaterial2D("ClockworkPlatformFrictionless") { friction = 0f, bounciness = 0f };

        List<Collider2D> solids = new List<Collider2D>(2);
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>(true))
        {
            if (col == null || col.isTrigger)
                continue;

            col.sharedMaterial = sharedFrictionlessMaterial;
            solids.Add(col);
        }
        solidColliders = solids.ToArray();

        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseAlphas = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseAlphas[i] = renderers[i].color.a;
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);

        ClockworkPlatformClock clock = ClockworkPlatformClock.Ensure();
        ApplySolid(clock.ActiveColor == platformColor);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    public void ApplySolid(bool solid)
    {
        if (solidColliders != null)
        {
            for (int i = 0; i < solidColliders.Length; i++)
            {
                if (solidColliders[i] != null)
                    solidColliders[i].enabled = solid;
            }
        }

        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
                continue;

            Color c = sr.color;
            c.a = solid ? baseAlphas[i] : baseAlphas[i] * ghostAlpha;
            sr.color = c;
        }
    }

    public bool IntersectsView(Rect view)
    {
        if (renderers == null)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
                continue;

            Bounds b = sr.bounds;
            if (b.max.x >= view.xMin && b.min.x <= view.xMax && b.max.y >= view.yMin && b.min.y <= view.yMax)
                return true;
        }

        return false;
    }
}

/// <summary>One per scene; drives every <see cref="ClockworkPlatform"/> in lockstep.</summary>
public sealed class ClockworkPlatformClock : MonoBehaviour
{
    public const int PhaseSeconds = 6;

    private static ClockworkPlatformClock instance;

    private float secondTimer;
    private int secondsIntoPhase;

    public ClockworkPlatformColor ActiveColor { get; private set; } = ClockworkPlatformColor.Green;

    public static ClockworkPlatformClock Ensure()
    {
        if (instance == null)
            instance = new GameObject("Clockwork Platform Clock").AddComponent<ClockworkPlatformClock>();

        return instance;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (ClockworkPlatform.All.Count == 0)
            return;

        secondTimer += Time.deltaTime;
        while (secondTimer >= 1f)
        {
            secondTimer -= 1f;
            secondsIntoPhase++;

            if (secondsIntoPhase < PhaseSeconds)
            {
                if (AnyPlatformOnScreen())
                    SoundManager.Instance?.PlayClockworkTic();
                continue;
            }

            secondsIntoPhase = 0;
            if (AnyPlatformOnScreen())
                SoundManager.Instance?.PlayClockworkToc();

            ActiveColor = ActiveColor == ClockworkPlatformColor.Green ? ClockworkPlatformColor.Red : ClockworkPlatformColor.Green;
            IReadOnlyList<ClockworkPlatform> platforms = ClockworkPlatform.All;
            for (int i = 0; i < platforms.Count; i++)
            {
                if (platforms[i] != null)
                    platforms[i].ApplySolid(platforms[i].PlatformColor == ActiveColor);
            }
        }
    }

    private static bool AnyPlatformOnScreen()
    {
        Camera cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetComponent<Camera>() : Camera.main;
        if (cam == null)
            return false;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 c = cam.transform.position;
        Rect view = new Rect(c.x - halfWidth, c.y - halfHeight, halfWidth * 2f, halfHeight * 2f);

        IReadOnlyList<ClockworkPlatform> platforms = ClockworkPlatform.All;
        for (int i = 0; i < platforms.Count; i++)
        {
            if (platforms[i] != null && platforms[i].IntersectsView(view))
                return true;
        }

        return false;
    }
}
