using UnityEngine;

/// <summary>
/// Smooth camera follow for the active player.
/// Softly eases in when first locking onto a target (no hard snap).
/// When the player is faster than normal (dash, charge boost, etc.), looks a bit ahead
/// and loosens follow so high speed stays pleasant instead of jarring.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-50)]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("If no target is assigned, follows PlayerController.Active.")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Follow Smoothness")]
    [Tooltip("Normal follow lag. Higher = smoother / floatier.")]
    [SerializeField] private float smoothTime = 0.45f;
    [Tooltip("Extra-soft follow used when the camera first finds or switches targets.")]
    [SerializeField] private float acquireSmoothTime = 1.1f;
    [Tooltip("How long the soft acquire blend lasts before returning to normal smoothTime.")]
    [SerializeField] private float acquireBlendDuration = 1.35f;
    [Tooltip("Caps how fast the camera can catch up (prevents hard snaps).")]
    [SerializeField] private float maxFollowSpeed = 18f;
    [SerializeField] private bool followX = true;
    [SerializeField] private bool followY = true;
    [Tooltip("If true, never snaps instantly — always eases toward the player.")]
    [SerializeField] private bool softAcquireOnly = true;

    [Header("Speed Look-Ahead")]
    [Tooltip("How far ahead (world units) the camera shifts when the player is above normal speed.")]
    [SerializeField] private float lookAheadDistance = 0.5f;
    [Tooltip("How gently the look-ahead slides in/out (higher = softer).")]
    [SerializeField] private float lookAheadSmoothTime = 0.4f;
    [Tooltip("Player horizontal speed must exceed normal move speed by this much to count as 'fast'.")]
    [SerializeField] private float speedBoostThreshold = 0.35f;
    [Tooltip("Multiplies follow smoothTime while the player is fast (more lag = less jarring).")]
    [SerializeField] private float fastSmoothTimeMultiplier = 1.4f;
    [Tooltip("Higher catch-up cap while fast so the camera does not fall forever behind.")]
    [SerializeField] private float fastMaxFollowSpeed = 26f;
    [Tooltip("How quickly follow smoothness eases between normal and fast settings.")]
    [SerializeField] private float fastBlendSmoothTime = 0.35f;

    [Header("Bounds (optional)")]
    [Tooltip("Usually your Background SpriteRenderer. Camera stays inside these bounds.")]
    [SerializeField] private SpriteRenderer background;
    [SerializeField] private bool autoFindBackground = true;
    [SerializeField] private string backgroundObjectName = "Background";
    [SerializeField] private float boundsPadding = 0f;

    private Camera cam;
    private Vector3 velocity;
    private Bounds bounds;
    private bool hasBounds;
    private Transform trackedTarget;
    private float acquireTimer;
    private bool followEnabled = true;

    private float lookAheadX;
    private float lookAheadVelocity;
    private float fastBlend;
    private float fastBlendVelocity;
    private bool catchUpBoost;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        ResolveBackground();
    }

    private void Start()
    {
        ResolveBackground();
        ResolveTarget();
        // Do not hard-snap on start — soft acquire handles the first catch-up.
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (!followEnabled)
            return;

        if (target == null && autoFindPlayer)
            ResolveTarget();

        if (target == null)
            return;

        if (trackedTarget != target)
            BeginSoftAcquire(target);

        if (!hasBounds && autoFindBackground)
            ResolveBackground();

        float desiredLookAhead = 0f;
        bool playerIsFast = TryGetPlayerSpeedState(out float lookAheadSign);
        if (playerIsFast)
            desiredLookAhead = lookAheadDistance * lookAheadSign;

        lookAheadX = Mathf.SmoothDamp(lookAheadX, desiredLookAhead, ref lookAheadVelocity, lookAheadSmoothTime);

        float targetFastBlend = playerIsFast ? 1f : 0f;
        fastBlend = Mathf.SmoothDamp(fastBlend, targetFastBlend, ref fastBlendVelocity, fastBlendSmoothTime);

        Vector3 desired = target.position + offset;
        desired.x += lookAheadX;
        desired = GetClampedPosition(desired);

        if (!followX) desired.x = transform.position.x;
        if (!followY) desired.y = transform.position.y;
        desired.z = offset.z;

        float currentSmooth = GetCurrentSmoothTime();
        currentSmooth *= Mathf.Lerp(1f, fastSmoothTimeMultiplier, fastBlend);

        float currentMaxSpeed = Mathf.Lerp(maxFollowSpeed, fastMaxFollowSpeed, fastBlend);

        if (catchUpBoost)
        {
            currentSmooth = Mathf.Min(currentSmooth, 0.12f);
            currentMaxSpeed = Mathf.Max(currentMaxSpeed, 42f);
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desired,
            ref velocity,
            currentSmooth,
            currentMaxSpeed);

        if (acquireTimer > 0f)
            acquireTimer -= Time.deltaTime;

        if (catchUpBoost && HasCaughtUpToTarget(0.55f))
            catchUpBoost = false;
    }

    /// <summary>
    /// True when the tracked player is moving meaningfully faster than their normal walk speed
    /// (dash, dash-jump, charge boost, etc.). lookAheadSign is the horizontal lead direction.
    /// </summary>
    private bool TryGetPlayerSpeedState(out float lookAheadSign)
    {
        lookAheadSign = 0f;

        PlayerController player = null;
        if (PlayerController.Active != null &&
            (target == null || target == PlayerController.Active.transform))
        {
            player = PlayerController.Active;
        }
        else if (target != null)
        {
            player = target.GetComponent<PlayerController>();
            if (player == null)
                player = target.GetComponentInParent<PlayerController>();
        }

        if (player == null)
            return false;

        float normal = Mathf.Max(0.01f, player.NormalMoveSpeed);
        float vx = player.Velocity.x;
        float absVx = Mathf.Abs(vx);

        bool boosted =
            player.IsDashing ||
            player.IsDashJumping ||
            absVx > normal + speedBoostThreshold;

        if (!boosted)
            return false;

        if (absVx > 0.05f)
            lookAheadSign = Mathf.Sign(vx);
        else
            lookAheadSign = player.FacingSign;

        return Mathf.Abs(lookAheadSign) > 0.01f;
    }

    public void SetFollowTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            target = null;
            trackedTarget = null;
            return;
        }

        target = newTarget;
        BeginSoftAcquire(newTarget);
    }

    /// <summary>
    /// When false, camera holds still (used during life-loss fade-to-black).
    /// </summary>
    public void SetFollowEnabled(bool enabled)
    {
        followEnabled = enabled;
        if (!enabled)
        {
            velocity = Vector3.zero;
            lookAheadVelocity = 0f;
            fastBlendVelocity = 0f;
            catchUpBoost = false;
        }
    }

    public bool FollowEnabled => followEnabled;

    /// <summary>
    /// Instantly places the camera on the target without enabling follow/tracking.
    /// Used while the screen is black so the reveal is framed correctly.
    /// </summary>
    public void SnapToTarget(Transform newTarget = null)
    {
        if (newTarget != null)
        {
            target = newTarget;
            trackedTarget = newTarget;
        }

        if (target == null)
            return;

        if (!hasBounds && autoFindBackground)
            ResolveBackground();

        Vector3 desired = target.position + offset;
        desired = GetClampedPosition(desired);
        if (!followX) desired.x = transform.position.x;
        if (!followY) desired.y = transform.position.y;
        desired.z = offset.z;

        transform.position = desired;
        velocity = Vector3.zero;
        lookAheadX = 0f;
        lookAheadVelocity = 0f;
        fastBlendVelocity = 0f;
        acquireTimer = 0f;
        catchUpBoost = false;
    }

    /// <summary>
    /// Speeds follow briefly after a mid-fight respawn / warning finishes.
    /// </summary>
    public void BeginRespawnCatchUp()
    {
        followEnabled = true;
        catchUpBoost = true;
        acquireTimer = 0f;
        velocity = Vector3.zero;
        lookAheadVelocity = 0f;
        lookAheadX = 0f;
    }

    /// <summary>
    /// True when the camera is close enough to the follow target that a fade-in feels settled.
    /// </summary>
    public bool HasCaughtUpToTarget(float maxDistance = 0.65f)
    {
        if (target == null)
            return true;

        Vector3 desired = target.position + offset;
        desired = GetClampedPosition(desired);
        if (!followX) desired.x = transform.position.x;
        if (!followY) desired.y = transform.position.y;

        Vector2 delta = new Vector2(desired.x - transform.position.x, desired.y - transform.position.y);
        return delta.sqrMagnitude <= maxDistance * maxDistance;
    }

    public void SetBackground(SpriteRenderer backgroundRenderer)
    {
        background = backgroundRenderer;
        CacheBounds();
    }

    /// <summary>
    /// World rectangle the camera is confined to (cached background bounds).
    /// Characters should stay inside this so they cannot dash into the void.
    /// </summary>
    public bool TryGetPlayableBounds(out Bounds playable)
    {
        if (!hasBounds)
            ResolveBackground();

        playable = bounds;
        return hasBounds;
    }

    /// <summary>
    /// Keeps a body inside the same background edges the camera cannot cross.
    /// Zeros outward velocity on contact so dashes / pulls do not push through.
    /// </summary>
    public bool ClampRigidbodyToPlayableBounds(Rigidbody2D body, Collider2D bodyCollider = null, float extraPadding = 0f)
    {
        if (body == null || !TryGetPlayableBounds(out Bounds playable))
            return false;

        float padX = Mathf.Max(0f, extraPadding);
        float padY = Mathf.Max(0f, extraPadding);
        if (bodyCollider != null)
        {
            Bounds b = bodyCollider.bounds;
            padX += b.extents.x;
            padY += b.extents.y;
        }

        float minX = playable.min.x + padX;
        float maxX = playable.max.x - padX;
        float minY = playable.min.y + padY;
        float maxY = playable.max.y - padY;

        Vector2 pos = body.position;
        Vector2 vel = body.linearVelocity;
        bool clamped = false;

        if (minX <= maxX)
        {
            if (pos.x < minX)
            {
                pos.x = minX;
                if (vel.x < 0f)
                    vel.x = 0f;
                clamped = true;
            }
            else if (pos.x > maxX)
            {
                pos.x = maxX;
                if (vel.x > 0f)
                    vel.x = 0f;
                clamped = true;
            }
        }

        if (minY <= maxY)
        {
            if (pos.y < minY)
            {
                pos.y = minY;
                if (vel.y < 0f)
                    vel.y = 0f;
                clamped = true;
            }
            else if (pos.y > maxY)
            {
                pos.y = maxY;
                if (vel.y > 0f)
                    vel.y = 0f;
                clamped = true;
            }
        }

        if (!clamped)
            return false;

        body.position = pos;
        body.linearVelocity = vel;
        return true;
    }

    private void BeginSoftAcquire(Transform newTarget)
    {
        trackedTarget = newTarget;
        acquireTimer = acquireBlendDuration;
        // Kill leftover velocity so the handoff starts gently.
        velocity = Vector3.zero;
        lookAheadVelocity = 0f;
        fastBlendVelocity = 0f;

        if (!softAcquireOnly && target != null)
            transform.position = GetClampedPosition(target.position + offset);
    }

    private float GetCurrentSmoothTime()
    {
        if (acquireTimer <= 0f || acquireBlendDuration <= 0.01f)
            return smoothTime;

        // Start very soft, ease into normal follow.
        float t = 1f - Mathf.Clamp01(acquireTimer / acquireBlendDuration);
        // Smoothstep for an even gentler settle.
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(acquireSmoothTime, smoothTime, t);
    }

    private void ResolveTarget()
    {
        if (target != null)
            return;

        if (!autoFindPlayer)
            return;

        Transform found = null;

        if (PlayerController.Active != null)
            found = PlayerController.Active.transform;
        else
        {
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
                found = tagged.transform;
        }

        if (found == null)
            return;

        target = found;
        BeginSoftAcquire(found);
    }

    private void ResolveBackground()
    {
        if (background == null && autoFindBackground)
        {
            GameObject found = GameObject.Find(backgroundObjectName);
            if (found != null)
                background = found.GetComponent<SpriteRenderer>();
        }

        CacheBounds();
    }

    private void CacheBounds()
    {
        hasBounds = background != null;
        if (hasBounds)
            bounds = background.bounds;
    }

    private Vector3 GetClampedPosition(Vector3 desired)
    {
        if (!hasBounds || cam == null || !cam.orthographic)
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = bounds.min.x + halfWidth + boundsPadding;
        float maxX = bounds.max.x - halfWidth - boundsPadding;
        float minY = bounds.min.y + halfHeight + boundsPadding;
        float maxY = bounds.max.y - halfHeight - boundsPadding;

        if (minX > maxX)
            desired.x = bounds.center.x;
        else
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        if (minY > maxY)
            desired.y = bounds.center.y;
        else
            desired.y = Mathf.Clamp(desired.y, minY, maxY);

        return desired;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        smoothTime = Mathf.Max(0.01f, smoothTime);
        acquireSmoothTime = Mathf.Max(smoothTime, acquireSmoothTime);
        acquireBlendDuration = Mathf.Max(0.05f, acquireBlendDuration);
        maxFollowSpeed = Mathf.Max(0.5f, maxFollowSpeed);
        fastMaxFollowSpeed = Mathf.Max(maxFollowSpeed, fastMaxFollowSpeed);
        lookAheadDistance = Mathf.Max(0f, lookAheadDistance);
        lookAheadSmoothTime = Mathf.Max(0.01f, lookAheadSmoothTime);
        speedBoostThreshold = Mathf.Max(0f, speedBoostThreshold);
        fastSmoothTimeMultiplier = Mathf.Max(1f, fastSmoothTimeMultiplier);
        fastBlendSmoothTime = Mathf.Max(0.01f, fastBlendSmoothTime);
        boundsPadding = Mathf.Max(0f, boundsPadding);
    }
#endif
}
