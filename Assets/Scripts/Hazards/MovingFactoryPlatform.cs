using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Factory moving platform: home → −4 → home → +4 → home, waiting at each stop.
/// Jump-through from below; solid when landing/standing on top (with seated hysteresis
/// so stopping at a wait point cannot flicker collision and trap the rider).
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class MovingFactoryPlatform : MonoBehaviour
{
    public enum MoveAxis
    {
        Horizontal,
        Vertical
    }

    private static readonly List<MovingFactoryPlatform> Active = new List<MovingFactoryPlatform>(8);

    [Header("Path")]
    [SerializeField] private MoveAxis axis = MoveAxis.Horizontal;
    [SerializeField] private float travelDistance = 4f;
    [SerializeField] private float waitSeconds = 1f;
    [Tooltip("World units/sec. ScrapNit flight is ~4; keep this slower.")]
    [SerializeField] private float moveSpeed = 2.25f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private Vector2 homePosition;
    private Vector2[] waypoints;
    private int waypointIndex;
    private float waitTimer;
    private bool waiting;
    private readonly Dictionary<int, bool> ignoreStateByPlayerId = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> seatedByPlayerId = new Dictionary<int, bool>();
    private readonly List<PlayerController> playerScratch = new List<PlayerController>(2);

    /// <summary>World-space velocity this physics step (for rider carry).</summary>
    public Vector2 CurrentVelocity { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.linearVelocity = Vector2.zero;

        if (bodyCollider != null)
            bodyCollider.isTrigger = false;

        homePosition = rb.position;
        BuildWaypoints();
        waiting = true;
        waitTimer = 0f;
        waypointIndex = 0;
        CurrentVelocity = Vector2.zero;
    }

    private void Start()
    {
        if (rb != null)
        {
            homePosition = rb.position;
            BuildWaypoints();
        }

        ForceIgnoreAllPlayers(true);
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        CurrentVelocity = Vector2.zero;
        ClearAllIgnores();
        ignoreStateByPlayerId.Clear();
        seatedByPlayerId.Clear();
    }

    private void ForceIgnoreAllPlayers(bool ignore)
    {
        if (bodyCollider == null)
            return;

        PlayerController.CollectActivePlayers(playerScratch);
        for (int i = 0; i < playerScratch.Count; i++)
        {
            PlayerController player = playerScratch[i];
            if (player == null)
                continue;

            int id = player.GetInstanceID();
            ignoreStateByPlayerId[id] = ignore;
            seatedByPlayerId[id] = false;
            PassablePlatformPhasing.SetIgnoredAgainstPlayer(bodyCollider, player, ignore);
        }
    }

    private void BuildWaypoints()
    {
        Vector2 negative = axis == MoveAxis.Horizontal
            ? Vector2.left * travelDistance
            : Vector2.down * travelDistance;
        Vector2 positive = axis == MoveAxis.Horizontal
            ? Vector2.right * travelDistance
            : Vector2.up * travelDistance;

        waypoints = new[]
        {
            homePosition,
            homePosition + negative,
            homePosition,
            homePosition + positive
        };
    }

    private void FixedUpdate()
    {
        float dt = HyperSpeedWorldSlow.IsActive
            ? HyperSpeedWorldSlow.WorldFixedDeltaTime
            : Time.fixedDeltaTime;
        dt = Mathf.Max(0.0001f, dt);

        if (waiting)
        {
            CurrentVelocity = Vector2.zero;
            waitTimer += dt;
            if (waitTimer >= Mathf.Max(0f, waitSeconds))
            {
                waiting = false;
                waitTimer = 0f;
                waypointIndex = (waypointIndex + 1) % waypoints.Length;
            }
        }
        else
        {
            Vector2 current = rb.position;
            Vector2 target = waypoints[waypointIndex];
            Vector2 toTarget = target - current;
            float dist = toTarget.magnitude;
            float step = Mathf.Max(0.05f, moveSpeed) * dt;

            Vector2 next;
            if (dist <= step || dist < 0.001f)
            {
                next = target;
                waiting = true;
                waitTimer = 0f;
                CurrentVelocity = Vector2.zero;
            }
            else
            {
                next = current + toTarget / dist * step;
                // Ride velocity must match real FixedUpdate integration (player uses Time.fixedDeltaTime).
                // Dividing by world-scaled dt made riders slide off during Hyper Speed.
                CurrentVelocity = (next - current) / Time.fixedDeltaTime;
            }

            rb.MovePosition(next);
        }

        RefreshPlayerPhasing();
    }

    private void RefreshPlayerPhasing()
    {
        if (bodyCollider == null)
            return;

        PlayerController.CollectActivePlayers(playerScratch);
        for (int i = 0; i < playerScratch.Count; i++)
        {
            PlayerController player = playerScratch[i];
            if (player == null || player.IsDead)
            {
                if (player != null)
                {
                    int deadId = player.GetInstanceID();
                    seatedByPlayerId[deadId] = false;
                }
                continue;
            }

            int id = player.GetInstanceID();
            bool wasSeated = seatedByPlayerId.TryGetValue(id, out bool seated) && seated;
            bool solid;

            if (wasSeated)
            {
                // Hysteresis: once riding, stay solid through wait stops / micro-sink.
                solid = IsStillSeated(player);
                if (!solid)
                    seatedByPlayerId[id] = false;
                else
                    PassablePlatformPhasing.TryRescueOntoMoving(player, bodyCollider);
            }
            else
            {
                solid = PassablePlatformPhasing.ShouldBeSolidMoving(player, bodyCollider);
                PassablePlatformPhasing.EnsureSeatedOrRescueMoving(player, bodyCollider, ref solid);
                if (solid)
                {
                    seatedByPlayerId[id] = true;
                    PassablePlatformPhasing.TryRescueOntoMoving(player, bodyCollider);
                }
            }

            SetIgnored(id, player, !solid);
        }
    }

    /// <summary>
    /// Looser stay-seated test so pausing at a waypoint cannot un-solid and trap the player,
    /// but feet must still be near this platform (not on a higher slim above it).
    /// GroundCheck may stay beside / same-X while riding.
    /// </summary>
    private bool IsStillSeated(PlayerController player)
    {
        if (player == null || bodyCollider == null)
            return false;

        if (!PassablePlatformPhasing.TryGetGroundCheck(player, out Vector2 center, out float radius))
            return false;

        Bounds plat = bodyCollider.bounds;
        if (!PassablePlatformPhasing.IsMovingHorizontalEligible(center, radius, plat))
            return false;

        if (!PassablePlatformPhasing.TryGetBodyBottom(player, out float feetY, out _))
            return false;

        float top = plat.max.y;
        Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
        float vy = playerRb != null ? playerRb.linearVelocity.y : 0f;

        // On a higher surface above this mover — not riding it.
        if (feetY > top + 0.25f)
            return false;

        // Jumped clear of the top.
        if (vy > 0.35f && feetY > top + 0.08f)
            return false;

        // Fell well below the surface (dropped through / left downward).
        if (feetY < top - 0.65f)
            return false;

        return true;
    }

    private void SetIgnored(int playerId, PlayerController player, bool ignore)
    {
        if (ignoreStateByPlayerId.TryGetValue(playerId, out bool current) && current == ignore)
            return;

        ignoreStateByPlayerId[playerId] = ignore;
        PassablePlatformPhasing.SetIgnoredAgainstPlayer(bodyCollider, player, ignore);
    }

    private void ClearAllIgnores()
    {
        if (bodyCollider == null)
            return;

        PlayerController.CollectActivePlayers(playerScratch);
        for (int i = 0; i < playerScratch.Count; i++)
        {
            if (playerScratch[i] != null)
                PassablePlatformPhasing.SetIgnoredAgainstPlayer(bodyCollider, playerScratch[i], false);
        }
    }

    public bool IsSupporting(PlayerController player)
    {
        if (player == null || bodyCollider == null)
            return false;

        int id = player.GetInstanceID();
        if (seatedByPlayerId.TryGetValue(id, out bool seated) && seated)
            return true;

        return PassablePlatformPhasing.ShouldBeSolidMoving(player, bodyCollider);
    }

    public static bool TryGetRideVelocity(PlayerController player, out Vector2 velocity)
    {
        velocity = Vector2.zero;
        if (player == null)
            return false;

        for (int i = 0; i < Active.Count; i++)
        {
            MovingFactoryPlatform platform = Active[i];
            if (platform == null || !platform.IsSupporting(player))
                continue;

            velocity = platform.CurrentVelocity;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        travelDistance = Mathf.Max(0.1f, travelDistance);
        waitSeconds = Mathf.Max(0f, waitSeconds);
        moveSpeed = Mathf.Max(0.05f, moveSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? (Vector3)homePosition : transform.position;
        Vector3 neg = axis == MoveAxis.Horizontal
            ? home + Vector3.left * travelDistance
            : home + Vector3.down * travelDistance;
        Vector3 pos = axis == MoveAxis.Horizontal
            ? home + Vector3.right * travelDistance
            : home + Vector3.up * travelDistance;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireSphere(home, 0.15f);
        Gizmos.DrawWireSphere(neg, 0.15f);
        Gizmos.DrawWireSphere(pos, 0.15f);
        Gizmos.DrawLine(neg, home);
        Gizmos.DrawLine(home, pos);
    }
#endif
}
