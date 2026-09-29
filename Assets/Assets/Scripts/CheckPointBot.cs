using UnityEngine;

/// <summary>
/// Scene-local checkpoint: one-shot detect → move PlayerSpawner spawn, open eye, confetti sparks.
/// Undamageable, fully phasable. Bob ±1 on Y. Resets automatically when the scene reloads.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class CheckPointBot : MonoBehaviour
{
    private static readonly int EyeClosedHash = Animator.StringToHash("EyeClosed");
    private static readonly int EyeOpenHash = Animator.StringToHash("EyeOpen");
    private static readonly int CheckPointPassedHash = Animator.StringToHash("CheckPoint passed");
    private static readonly int EyeClosedStateHash = Animator.StringToHash("CheckPoint Bot Eye closed");
    private static readonly int EyeOpenedStateHash = Animator.StringToHash("CheckPoint Bot Eye opened");

    [Header("Bob")]
    [SerializeField] private float bobAmplitude = 1f;
    [SerializeField] private float bobCyclesPerSecond = 0.55f;

    [Header("Checkpoint")]
    [Tooltip("Spawn point is home position + this X offset (spaces).")]
    [SerializeField] private float spawnOffsetX = -1f;

    [Header("Detection")]
    [SerializeField] private Transform detectionCenter;
    [SerializeField] private Collider2D detectionCollider;
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Sparks")]
    [SerializeField] private GameVisualEffect sparkPrefab;
    [SerializeField] private Transform sparkSpawnPoint;
    [SerializeField] private int sparkCount = 10;
    [SerializeField] private float sparkSpeed = 8f;
    [SerializeField] private float sparkMaxHeight = 2f;
    [SerializeField] private float sparkLifetime = 1.5f;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private Vector2 homePosition;
    private float bobPhase;
    private bool activated;
    private PlayerController targetPlayer;

    /// <summary>Most recently activated checkpoint in this scene (for respawn camera framing).</summary>
    public static CheckPointBot LastActivated { get; private set; }

    public bool HasActivated => activated;
    public Vector3 HomeWorldPosition => homePosition;
    public float BobAmplitude => bobAmplitude;

    /// <summary>Transform to frame while black / fade-in after a life-loss respawn.</summary>
    public static Transform GetRespawnCameraTrackTarget()
    {
        return LastActivated != null ? LastActivated.transform : null;
    }

    /// <summary>
    /// Camera target while waiting for the player: last checkpoint, else Player Spawner.
    /// </summary>
    public static Transform GetWaitingCameraTrackTarget()
    {
        Transform checkpoint = GetRespawnCameraTrackTarget();
        if (checkpoint != null)
            return checkpoint;

        if (PlayerSpawner.Instance != null)
            return PlayerSpawner.Instance.CameraTrackTransform;

        return null;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        CacheChildReferences();
        ConfigureBody();
        ConfigureUtilityColliders();

        homePosition = rb != null ? rb.position : (Vector2)transform.position;
        activated = false;
        SetEyeClosedVisual();
    }

    private void Start()
    {
        HideSceneTemplates();
        DeferredEnemyPhaseRefresh.Request();
        ApplyWorldPosition();
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
    }

    private void Update()
    {
        float dt = HyperSpeedWorldSlow.WorldDeltaTime;
        ResolveTargetPlayer();
        UpdateBob(dt);
        TryActivateFromDetection();
    }

    private void FixedUpdate()
    {
        ApplyWorldPosition();
    }

    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreAllColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));

        // Phase through common enemies / bosses so nothing snags on the bot.
        IgnoreCachedFamily(myCols, EnemyTypeCache.Crankies);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Lasers);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Blockers);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Tankers);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Scraps);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Bosses);

        Projectile[] shots = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < shots.Length; i++)
        {
            if (shots[i] == null)
                continue;
            IgnoreAllColliders(myCols, shots[i].GetComponentsInChildren<Collider2D>(true));
        }

        RefreshDetectionColliderPhasing();
    }

    private void TryActivateFromDetection()
    {
        if (activated)
            return;

        if (!IsPlayerInsideDetection())
            return;

        Activate();
    }

    private void Activate()
    {
        if (activated)
            return;

        activated = true;
        LastActivated = this;

        Vector3 spawnPos = new Vector3(homePosition.x + spawnOffsetX, homePosition.y, 0f);
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.SetSpawnPointWorldPosition(spawnPos);

        OpenEyeVisual();
        BurstSparks();
    }

    private void OnDestroy()
    {
        if (LastActivated == this)
            LastActivated = null;
    }

    private void BurstSparks()
    {
        Vector3 origin = sparkSpawnPoint != null ? sparkSpawnPoint.position : transform.position;
        VisualEffects.SpawnCheckPointBotConfettiSparks(
            sparkPrefab,
            origin,
            Mathf.Max(1, sparkCount),
            Mathf.Max(0.1f, sparkSpeed),
            Mathf.Max(0.1f, sparkMaxHeight),
            Mathf.Max(0.1f, sparkLifetime),
            spriteRenderer);
    }

    private bool IsPlayerInsideDetection()
    {
        if (targetPlayer == null || targetPlayer.IsDead)
            return false;

        if (detectionCollider != null)
        {
            Collider2D playerCol = targetPlayer.GetComponent<Collider2D>();
            if (playerCol == null)
                playerCol = targetPlayer.GetComponentInChildren<Collider2D>();

            if (playerCol != null)
            {
                if (playerLayers != ~0 &&
                    ((1 << playerCol.gameObject.layer) & playerLayers) == 0)
                    return false;

                return detectionCollider.bounds.Intersects(playerCol.bounds);
            }

            return detectionCollider.bounds.Contains(targetPlayer.transform.position);
        }

        Vector2 center = detectionCenter != null ? detectionCenter.position : transform.position;
        return Vector2.Distance(center, targetPlayer.transform.position) <= 3f;
    }

    private void UpdateBob(float dt)
    {
        bobPhase += dt * Mathf.PI * 2f * Mathf.Max(0.05f, bobCyclesPerSecond);
    }

    private void ApplyWorldPosition()
    {
        float bob = Mathf.Sin(bobPhase) * bobAmplitude;
        Vector2 world = new Vector2(homePosition.x, homePosition.y + bob);
        if (rb != null)
            rb.MovePosition(world);
        else
            transform.position = world;
    }

    private void SetEyeClosedVisual()
    {
        if (animator == null)
            return;

        animator.SetBool(EyeClosedHash, true);
        animator.SetBool(EyeOpenHash, false);
        if (animator.HasState(0, EyeClosedStateHash))
            animator.Play(EyeClosedStateHash, 0, 0f);
    }

    private void OpenEyeVisual()
    {
        if (animator == null)
            return;

        animator.SetBool(EyeClosedHash, false);
        animator.SetBool(EyeOpenHash, true);
        animator.ResetTrigger(CheckPointPassedHash);
        animator.SetTrigger(CheckPointPassedHash);

        if (animator.HasState(0, EyeOpenedStateHash))
            animator.Play(EyeOpenedStateHash, 0, 0f);
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
    }

    private void CacheChildReferences()
    {
        if (detectionCenter == null)
        {
            Transform found = transform.Find("Detection radius");
            if (found == null)
                found = transform.Find("Detection Radius");
            if (found != null)
                detectionCenter = found;
        }

        if (detectionCenter != null && detectionCollider == null)
            detectionCollider = detectionCenter.GetComponent<Collider2D>();

        if (sparkSpawnPoint == null)
        {
            Transform found = transform.Find("Spark Spawn Box");
            if (found == null)
                found = transform.Find("Spark spawn box");
            if (found != null)
                sparkSpawnPoint = found;
        }
    }

    private void ConfigureBody()
    {
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearVelocity = Vector2.zero;
        }

        // Trigger body: nothing solid-blocks, and shots won't treat it as environment.
        if (bodyCollider != null)
            bodyCollider.isTrigger = true;
    }

    private void ConfigureUtilityColliders()
    {
        if (detectionCenter != null)
        {
            if (detectionCenter.GetComponent<EnemyDetectionZone>() == null)
                detectionCenter.gameObject.AddComponent<EnemyDetectionZone>();

            SpriteRenderer detectionVisual = detectionCenter.GetComponent<SpriteRenderer>();
            if (detectionVisual != null)
                detectionVisual.enabled = false;
        }

        if (detectionCollider != null)
            detectionCollider.isTrigger = true;
    }

    private void HideSceneTemplates()
    {
        HideIfSceneObject(sparkPrefab != null ? sparkPrefab.gameObject : null);
    }

    private static void HideIfSceneObject(GameObject go)
    {
        if (go == null || !go.scene.IsValid())
            return;

        go.SetActive(false);
    }

    private void RefreshDetectionColliderPhasing()
    {
        if (detectionCenter == null)
            return;

        EnemyDetectionZone zone = detectionCenter.GetComponent<EnemyDetectionZone>();
        if (zone == null)
            zone = detectionCenter.gameObject.AddComponent<EnemyDetectionZone>();

        if (!DeferredEnemyPhaseRefresh.IsBatchRunning)
            zone.RefreshPhaseCollisions();
    }

    private void IgnoreCachedFamily<T>(Collider2D[] myCols, T[] others) where T : Component
    {
        if (others == null)
            return;

        for (int i = 0; i < others.Length; i++)
        {
            if (others[i] == null)
                continue;
            IgnoreAllColliders(myCols, others[i].GetComponentsInChildren<Collider2D>(true));
        }
    }

    private static void IgnoreAllColliders(Collider2D[] aCols, Collider2D[] bCols)
    {
        if (aCols == null || bCols == null)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null)
                continue;

            for (int j = 0; j < bCols.Length; j++)
            {
                Collider2D b = bCols[j];
                if (b == null)
                    continue;

                Physics2D.IgnoreCollision(a, b, true);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        bobAmplitude = Mathf.Max(0f, bobAmplitude);
        bobCyclesPerSecond = Mathf.Max(0.05f, bobCyclesPerSecond);
        sparkCount = Mathf.Max(1, sparkCount);
        sparkSpeed = Mathf.Max(0.1f, sparkSpeed);
        sparkMaxHeight = Mathf.Max(0.1f, sparkMaxHeight);
        sparkLifetime = Mathf.Max(0.1f, sparkLifetime);
    }
#endif
}
