using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Kit's rocket ship — an upgraded teleport crystal.
/// Parked / hovering: shows the interact symbol in range and Up boards the player. The ship then
/// rises, backs up, and launches up-right off-screen before loading <see cref="destinationScene"/>.
/// Scene start: flies in carrying the Player Spawner, drops the player at the camera center, then
/// leaves (<see cref="TryBeginArrival"/>). After a boss: descends beside the player and waits
/// (<see cref="TrySummonAfterBoss"/>). Scenes without a parked ship load Resources/Kit's Rocket ship.
/// </summary>
[DefaultExecutionOrder(-60)]
[DisallowMultipleComponent]
public sealed class KitRocketShip : MonoBehaviour
{
    public const string ResourceName = "Kit's Rocket ship";

    private enum ShipState
    {
        Parked,
        Hovering,
        Busy
    }

    /// <summary>True from scene start until the arriving ship has flown out of the camera view.</summary>
    public static bool IsArrivalInProgress { get; private set; }

    /// <summary>True while a checkpoint respawn is waiting for the ship to drop the player off.</summary>
    public static bool IsRespawnDeliveryInProgress { get; private set; }

    private static bool warnedMissingPrefab;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsArrivalInProgress = false;
        IsRespawnDeliveryInProgress = false;
        warnedMissingPrefab = false;
    }

    [Header("Travel")]
    [Tooltip("Scene loaded once the ship flies off-screen with the player (like Spawn Crystal's Teleport To Scene).")]
    [SerializeField] private string destinationScene = "Level one";
    [SerializeField] private float fadeOutSeconds = 0.35f;
    [SerializeField] private float fadeInSeconds = 0.35f;

    [Header("Parts (auto-found by name if empty)")]
    [SerializeField] private Transform playerHolder;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform interactRadius;
    [SerializeField] private Transform interactSymbolBox;
    [Tooltip("Interactble Symbol GameVisualEffect prefab (the same one the NPCs use).")]
    [SerializeField] private GameVisualEffect interactSymbolPrefab;
    [SerializeField] [Range(0.1f, 1f)] private float stickUpThreshold = 0.5f;

    [Header("Boarding")]
    [Tooltip("How far above the top of the ship the player's feet reach when jumping in.")]
    [SerializeField] private float jumpHeightAboveShip = 2f;
    [SerializeField] private float boardRunTimeout = 4f;
    [SerializeField] private float seatSnapDuration = 0.12f;

    [Header("Take-off")]
    [SerializeField] private float pauseBeforeTakeOff = 0.25f;
    [SerializeField] private float riseDistance = 1.5f;
    [SerializeField] private float riseSpeed = 1.2f;
    [SerializeField] private float backUpDistance = 2f;
    [SerializeField] private float backUpSpeed = 1.2f;
    [SerializeField] private float pauseBeforeLaunch = 0.2f;
    [Tooltip("Degrees above the horizon, toward the right.")]
    [SerializeField] private float launchAngle = 45f;
    [SerializeField] private float launchStartSpeed = 5f;
    [SerializeField] private float launchSpeed = 14f;
    [SerializeField] private float launchAcceleration = 18f;

    [Header("Scene Arrival")]
    [Tooltip("Gap between the camera's top-left corner and the ship's nearest edge when it appears (x left, y up).")]
    [SerializeField] private Vector2 arrivalStartOffset = new Vector2(-3f, 3f);
    [SerializeField] private float arrivalSpeed = 6f;
    [SerializeField] private float hoverBeforeRelease = 0.35f;
    [SerializeField] private float waitAfterPlayerSpawn = 0.4f;
    [SerializeField] private float departAngle = 45f;
    [SerializeField] private float departSpeed = 12f;
    [Tooltip("Arrival copies despawn (or the HomeTown ship turns back to park) this far outside the view.")]
    [SerializeField] private float despawnDistanceOutsideView = 2f;
    [SerializeField] private float returnToParkSpeed = 7f;

    [Header("After A Boss / Pause Pick-up")]
    [Tooltip("Gap between the player and the ship's nearest edge.")]
    [SerializeField] private float bossLandDistanceFromPlayer = 3f;
    [SerializeField] private float bossHeightAboveGround = 1f;
    [Tooltip("Flight speed from off-screen to the spot beside the player after a boss.")]
    [SerializeField] private float bossApproachSpeed = 6f;
    [Tooltip("Flight speed when swooping in for Pause A → Return to HomeTown.")]
    [SerializeField] private float pickUpApproachSpeed = 9f;

    [Header("Checkpoint Respawn")]
    [Tooltip("Gap between the top of the checkpoint (at the peak of its bob) and the bottom of the ship hovering above it.")]
    [SerializeField] private float checkpointHoverGap = 1f;

    [Header("Hover")]
    [SerializeField] private float hoverBobAmplitude = 0.08f;
    [SerializeField] private float hoverBobSpeed = 2.2f;

    private Rigidbody2D rb;
    private SpriteRenderer body;
    private Animator animator;
    private SortingGroup shipSortingGroup;
    private Collider2D[] solidColliders;
    private bool[] solidCollidersEnabled;
    private CircleCollider2D holderCircle;
    private CircleCollider2D interactCircle;
    private GameVisualEffect symbolInstance;
    private InputActions controls;
    private bool moveUpHeld;
    private bool hasIsFlyingParam;
    private bool hasFlyParam;

    private ShipState state = ShipState.Parked;
    private bool spawnedFromResources;
    private bool ownsArrivalFlag;
    private bool ownsRespawnFlag;
    private bool destinationLocked;
    private bool inFlightMode;
    private bool rbWasSimulated = true;
    private Vector3 parkPosition;
    private bool originalFlipX;
    private int originalSortingLayerId;
    private int originalSortingOrder;
    private Vector3 hoverBase;
    private float hoverTime;
    private float noPlayerTimer;
    private PlayerController playerInRange;
    private int lastPhasedPlayerId = int.MinValue;

    private PlayerController rider;
    private bool riderSeated;
    private Vector3 riderSeatOffset;
    private Vector3 seatBlendFrom;
    private float seatBlend = 1f;
    private bool riderSortingSwapped;
    private int riderOriginalLayerId;
    private int riderOriginalOrder;

    #region Static entry points

    /// <summary>
    /// Scene start: the ship carries the spawner in and drops the player off.
    /// Returns false (caller spawns normally) when no ship is available.
    /// </summary>
    public static bool TryBeginArrival(PlayerSpawner spawner)
    {
        if (spawner == null)
            return false;

        KitRocketShip ship = FindParkedSceneShip();
        bool returnToPark = ship != null;
        if (ship == null)
            ship = SpawnFromResources();

        if (ship == null)
            return false;

        IsArrivalInProgress = true;
        ship.ownsArrivalFlag = true;
        ship.StartCoroutine(ship.ArrivalRoutine(spawner, returnToPark));
        return true;
    }

    /// <summary>
    /// Boss defeated: the ship flies in from off-screen (or over from where it is hovering), stops beside
    /// the player, and waits to be boarded.
    /// </summary>
    public static bool TrySummonAfterBoss(string destination)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
            return false;

        KitRocketShip ship = FindIdleShip() ?? TakeOverFreeShip();
        bool spawnedNow = ship == null;
        if (spawnedNow)
            ship = SpawnFromResources();

        if (ship == null)
            return false;

        // New ships, and ones left hovering at a far checkpoint, enter from the view's corner.
        bool fresh = spawnedNow || ship.IsOutsideView(0f);
        if (!string.IsNullOrWhiteSpace(destination))
            ship.destinationScene = destination.Trim();
        ship.destinationLocked = true;

        ship.StartCoroutine(ship.ApproachPlayerRoutine(player, fresh, autoBoard: false));
        return true;
    }

    /// <summary>
    /// Life-loss respawn with an activated checkpoint: call while the screen is black. After the fade-in the
    /// ship flies beside the checkpoint, drops the player off, and stays hovering there (boardable).
    /// Wait on <see cref="IsRespawnDeliveryInProgress"/>; the new player is <see cref="PlayerSpawner.CurrentPlayer"/>.
    /// </summary>
    public static bool TryBeginCheckpointRespawn(PlayerSpawner spawner)
    {
        if (spawner == null)
            return false;

        CheckPointBot checkpoint = CheckPointBot.LastActivated;
        if (checkpoint == null || !checkpoint.isActiveAndEnabled)
            return false;

        KitRocketShip ship = FindIdleShip() ?? TakeOverFreeShip();
        bool spawnedNow = ship == null;
        if (spawnedNow)
            ship = SpawnFromResources();

        if (ship == null)
            return false;

        if (!ship.destinationLocked && !string.IsNullOrWhiteSpace(spawner.CheckpointShipDestination))
            ship.destinationScene = spawner.CheckpointShipDestination.Trim();

        spawner.ResetSceneForRespawn();
        IsRespawnDeliveryInProgress = true;
        ship.ownsRespawnFlag = true;
        ship.StartCoroutine(ship.CheckpointRespawnRoutine(spawner, checkpoint, spawnedNow));
        return true;
    }

    /// <summary>
    /// Pause A → Return to HomeTown: the ship swoops in beside the player, the player boards it,
    /// and the screen fades only once the ship has flown out of view.
    /// </summary>
    public static bool TryPickUpPlayer(string destination)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead || player.IsRidingVehicle || !player.isActiveAndEnabled)
            return false;

        KitRocketShip ship = FindIdleShip() ?? TakeOverFreeShip();
        bool spawnedNow = ship == null;
        if (spawnedNow)
            ship = SpawnFromResources();

        if (ship == null)
            return false;

        bool fresh = spawnedNow || ship.IsOutsideView(0f);
        string previousDestination = ship.destinationScene;
        if (!string.IsNullOrWhiteSpace(destination))
            ship.destinationScene = destination.Trim();

        if (!ship.CanLoadDestination())
        {
            ship.destinationScene = previousDestination;
            if (spawnedNow)
                Destroy(ship.gameObject);
            return false;
        }

        ship.destinationLocked = true;
        player.SetInputLocked(true);
        player.SetScriptedInvulnerable(true);
        ship.StartCoroutine(ship.ApproachPlayerRoutine(player, fresh, autoBoard: true));
        return true;
    }

    /// <summary>
    /// Bannana Phone: the ship swoops in above the player (its bottom <paramref name="hoverAboveHead"/> over
    /// their head), drops <paramref name="count"/> random Juice Boxes / Hot Dogs onto them, and flies off.
    /// A parked or hovering ship goes back to where it was afterwards.
    /// </summary>
    public static bool TryDeliverSupplies(
        PlayerController player,
        CollectableConsumable juiceBox,
        CollectableConsumable hotDog,
        int count,
        float hoverAboveHead)
    {
        if (player == null || player.IsDead || !player.isActiveAndEnabled)
            return false;

        KitRocketShip ship = FindIdleShip();
        bool spawnedNow = ship == null;
        if (spawnedNow)
            ship = SpawnFromResources();

        if (ship == null)
            return false;

        ship.StartCoroutine(ship.SupplyDropRoutine(player, juiceBox, hotDog, Mathf.Max(0, count), Mathf.Max(0f, hoverAboveHead), spawnedNow));
        return true;
    }

    /// <summary>A ship already in the scene that is free (hovering first, then parked).</summary>
    private static KitRocketShip FindIdleShip()
    {
        KitRocketShip parked = null;
        KitRocketShip[] ships = FindObjectsByType<KitRocketShip>(FindObjectsSortMode.None);
        for (int i = 0; i < ships.Length; i++)
        {
            KitRocketShip ship = ships[i];
            if (ship == null || !ship.isActiveAndEnabled)
                continue;

            if (ship.state == ShipState.Hovering)
                return ship;

            if (ship.state == ShipState.Parked && parked == null)
                parked = ship;
        }

        return parked;
    }

    /// <summary>
    /// No idle ship: take over one that is busy without a rider (flying back to park, mid supply drop, or
    /// left stuck by an interrupted routine) so a boss defeat / respawn / pick-up never comes up empty.
    /// </summary>
    private static KitRocketShip TakeOverFreeShip()
    {
        KitRocketShip[] ships = FindObjectsByType<KitRocketShip>(FindObjectsSortMode.None);
        for (int i = 0; i < ships.Length; i++)
        {
            KitRocketShip ship = ships[i];
            if (ship == null || !ship.isActiveAndEnabled || ship.rider != null)
                continue;

            ship.InterruptCurrentRoutine();
            return ship;
        }

        return null;
    }

    private void InterruptCurrentRoutine()
    {
        StopAllCoroutines();

        if (ownsArrivalFlag)
            FinishArrival(null);

        if (ownsRespawnFlag)
        {
            ownsRespawnFlag = false;
            IsRespawnDeliveryInProgress = false;
        }

        riderSeated = false;
        riderSortingSwapped = false;
        SetShipSorting(originalSortingLayerId, originalSortingOrder);
        if (body != null)
            body.flipX = originalFlipX;
        state = ShipState.Parked;
    }

    private static KitRocketShip FindParkedSceneShip()
    {
        KitRocketShip[] ships = FindObjectsByType<KitRocketShip>(FindObjectsSortMode.None);
        for (int i = 0; i < ships.Length; i++)
        {
            KitRocketShip ship = ships[i];
            if (ship != null && ship.isActiveAndEnabled && !ship.spawnedFromResources && ship.state == ShipState.Parked)
                return ship;
        }

        return null;
    }

    private static KitRocketShip SpawnFromResources()
    {
        KitRocketShip prefab = Resources.Load<KitRocketShip>(ResourceName);
        if (prefab == null)
        {
            if (!warnedMissingPrefab)
            {
                warnedMissingPrefab = true;
                Debug.LogWarning(
                    $"[KitRocketShip] No prefab at Assets/Resources/{ResourceName}.prefab — " +
                    "drag HomeTown's rocket ship into a Resources folder to enable arrivals / post-boss ships.");
            }

            return null;
        }

        KitRocketShip ship = Instantiate(prefab);
        ship.name = ResourceName;
        ship.spawnedFromResources = true;
        return ship;
    }

    #endregion

    #region Unity lifecycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<SpriteRenderer>();
        if (body == null)
            body = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponent<Animator>();
        shipSortingGroup = GetComponent<SortingGroup>();

        CacheParts();
        ConfigureDetectionColliders();
        CacheSolidColliders();
        CacheAnimatorParams();

        parkPosition = transform.position;
        originalFlipX = body != null && body.flipX;
        GetShipSorting(out originalSortingLayerId, out originalSortingOrder);
        rbWasSimulated = rb == null || rb.simulated;

        EnsureInteractSymbol();
        SetSymbolVisible(false);
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputActions();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Up.performed += OnUpPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;
        lastPhasedPlayerId = int.MinValue;
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.PlayerControls.Up.performed -= OnUpPerformed;
            controls.PlayerControls.Movement.performed -= OnMovementPerformed;
            controls.PlayerControls.Movement.canceled -= OnMovementCanceled;
            controls.PlayerControls.Disable();
        }

        SetSymbolVisible(false);
    }

    private void OnDestroy()
    {
        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }

        if (ownsArrivalFlag)
            IsArrivalInProgress = false;

        if (ownsRespawnFlag)
            IsRespawnDeliveryInProgress = false;

        if (rider != null)
        {
            rider.ClearScriptedMove();
            if (riderSeated)
                rider.EndVehicleRide();
            rider.SetScriptedInvulnerable(false);
            rider.SetInputLocked(false);
        }
    }

    private void Update()
    {
        EnsurePhasedWithActivePlayer();

        if (state == ShipState.Hovering)
        {
            hoverTime += Time.deltaTime;
            transform.position = hoverBase + Vector3.up * (Mathf.Sin(hoverTime * hoverBobSpeed * Mathf.PI) * hoverBobAmplitude);
            TickNoPlayerFallback();
        }

        if (state == ShipState.Busy)
        {
            playerInRange = null;
            SetSymbolVisible(false);
            return;
        }

        UpdateRange();
    }

    private void LateUpdate()
    {
        if (!riderSeated || rider == null)
            return;

        Vector3 seat = GetHolderCenter() + riderSeatOffset;
        seat.z = rider.transform.position.z;

        if (seatBlend < 1f)
        {
            seatBlend = Mathf.MoveTowards(seatBlend, 1f, Time.deltaTime / Mathf.Max(0.01f, seatSnapDuration));
            rider.transform.position = Vector3.Lerp(seatBlendFrom, seat, Mathf.SmoothStep(0f, 1f, seatBlend));
        }
        else
        {
            rider.transform.position = seat;
        }
    }

    #endregion

    #region Interaction

    private void UpdateRange()
    {
        PlayerController player = PlayerController.ResolveActive();
        bool inRange = player != null && !player.IsDead && !player.IsRidingVehicle && IsPlayerInInteractRange(player);
        playerInRange = inRange ? player : null;

        bool showPrompt = inRange &&
                          !player.InputLocked &&
                          (DialogueBox.Instance == null || !DialogueBox.Instance.IsOpen) &&
                          !ScreenFade.IsBusy;
        SetSymbolVisible(showPrompt);
    }

    private bool IsPlayerInInteractRange(PlayerController player)
    {
        Vector3 center = GetInteractCenter();
        float radius = GetCircleWorldRadius(interactCircle, 1.5f);

        // Closest point on the body (not the pivot) so a hovering ship can still be reached from the ground.
        Vector3 point = player.transform.position;
        Collider2D bodyCol = player.BodyCollider;
        if (bodyCol != null && bodyCol.enabled)
        {
            Bounds b = bodyCol.bounds;
            point = b.ClosestPoint(new Vector3(center.x, center.y, b.center.z));
        }

        return Vector2.Distance(center, point) <= radius;
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (context.performed)
            TryBoard();
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        if (value.y >= stickUpThreshold)
        {
            if (!moveUpHeld)
            {
                moveUpHeld = true;
                TryBoard();
            }
        }
        else
        {
            moveUpHeld = false;
        }
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        moveUpHeld = false;
    }

    private void TryBoard()
    {
        if (state == ShipState.Busy)
            return;

        PlayerController player = playerInRange;
        if (player == null || player.IsDead || player.InputLocked || player.IsRidingVehicle)
            return;

        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return;

        if (ScreenFade.IsBusy || WarningText.BlocksGameplay || !CanLoadDestination())
            return;

        StartCoroutine(BoardAndLaunchRoutine(player));
    }

    private bool CanLoadDestination()
    {
        string target = destinationScene != null ? destinationScene.Trim() : string.Empty;
        if (string.IsNullOrEmpty(target))
        {
            Debug.LogWarning($"[KitRocketShip] '{name}' has no Destination Scene set.", this);
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(target))
        {
            Debug.LogWarning($"[KitRocketShip] Scene '{target}' is not in Build Settings.", this);
            return false;
        }

        return true;
    }

    #endregion

    #region Boarding + launch

    private IEnumerator BoardAndLaunchRoutine(PlayerController player)
    {
        ShipState resumeState = state;
        state = ShipState.Busy;
        SetSymbolVisible(false);
        rider = player;
        player.SetInputLocked(true);
        player.SetScriptedInvulnerable(true);

        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();

        float landWait = 0f;
        while (IsRiderValid() && !player.IsPhysicallyGrounded && landWait < 2f)
        {
            landWait += Time.deltaTime;
            yield return null;
        }

        float targetX = GetHolderCenter().x;
        float startSide = Mathf.Sign(targetX - GetBodyCenter(player).x);
        float runTime = 0f;
        while (IsRiderValid())
        {
            float dx = targetX - GetBodyCenter(player).x;
            if (Mathf.Abs(dx) <= 0.05f || Mathf.Sign(dx) != startSide || runTime >= boardRunTimeout)
                break;

            player.SetScriptedMove(Mathf.Sign(dx));
            runTime += Time.deltaTime;
            yield return null;
        }

        if (!IsRiderValid())
        {
            AbortBoarding(resumeState);
            yield break;
        }

        player.ClearScriptedMove();
        if (playerBody != null)
        {
            float correction = targetX - GetBodyCenter(player).x;
            playerBody.linearVelocity = new Vector2(0f, playerBody.linearVelocity.y);
            playerBody.position = new Vector2(playerBody.position.x + correction, playerBody.position.y);
        }

        player.SetFacingSign(1f);
        yield return new WaitForFixedUpdate();

        Collider2D bodyCol = player.BodyCollider;
        float feetY = bodyCol != null ? bodyCol.bounds.min.y : player.transform.position.y;
        float jumpHeight = Mathf.Max(0.5f, GetVisualBounds().max.y + jumpHeightAboveShip - feetY);
        player.PerformScriptedJump(Mathf.Sqrt(2f * player.JumpGravity * jumpHeight));
        yield return new WaitForFixedUpdate();

        float airTime = 0f;
        while (IsRiderValid())
        {
            airTime += Time.deltaTime;
            float vy = playerBody != null ? playerBody.linearVelocity.y : 0f;

            // Falling back down: the ship moves in front of the player.
            if (!riderSortingSwapped && vy <= 0f)
                SwapSortingWithRider(player);

            if (riderSortingSwapped && (OverlapsHolder(player) || player.IsPhysicallyGrounded || airTime > 4f))
                break;

            yield return null;
        }

        if (!IsRiderValid())
        {
            AbortBoarding(resumeState);
            yield break;
        }

        if (!riderSortingSwapped)
            SwapSortingWithRider(player);

        SeatRider(player);
        EnterFlightMode();

        yield return WaitSeconds(pauseBeforeTakeOff);
        while (seatBlend < 1f)
            yield return null;

        SetFlyingAnimation(true, playTrigger: true);
        yield return FlyTo(transform.position + Vector3.up * riseDistance, riseSpeed, riseSpeed * 1.5f, 0.4f);
        yield return FlyTo(transform.position + Vector3.left * backUpDistance, backUpSpeed, backUpSpeed * 1.5f, 0.4f);
        yield return WaitSeconds(pauseBeforeLaunch);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(false);

        Vector3 dir = AngleToDirection(launchAngle);
        float speed = launchStartSpeed;
        float elapsed = 0f;
        bool loadRequested = false;
        while (elapsed < 12f)
        {
            float dt = Time.deltaTime;
            speed = Mathf.MoveTowards(speed, launchSpeed, launchAcceleration * dt);
            transform.position += dir * (speed * dt);
            elapsed += dt;

            if (!loadRequested && (IsOutsideView(0f) || elapsed > 6f))
            {
                loadRequested = true;
                LoadDestination();
            }

            yield return null;
        }
    }

    private void LoadDestination()
    {
        string target = destinationScene.Trim();
        if (BossFightDirector.IsBossFightScene(target))
            BossEncounter.PrepareBossFightFromLevelProgress();

        ScreenFade.EnsureExists().LoadScene(target, fadeOutSeconds, fadeInSeconds);
    }

    private void SeatRider(PlayerController player)
    {
        riderSeatOffset = player.transform.position - GetBodyCenter(player);
        player.BeginVehicleRide();
        seatBlendFrom = player.transform.position;
        seatBlend = 0f;
        riderSeated = true;
    }

    private void AbortBoarding(ShipState resumeState)
    {
        if (rider != null)
        {
            rider.ClearScriptedMove();
            rider.SetScriptedInvulnerable(false);
            if (!rider.IsDead)
                rider.SetInputLocked(false);
        }

        RestoreRiderSorting();
        SetShipSorting(originalSortingLayerId, originalSortingOrder);
        rider = null;
        riderSeated = false;
        state = resumeState;
    }

    private bool IsRiderValid()
    {
        return rider != null && rider.isActiveAndEnabled && !rider.IsDead;
    }

    private bool OverlapsHolder(PlayerController player)
    {
        Collider2D bodyCol = player.BodyCollider;
        if (bodyCol == null || !bodyCol.enabled)
            return false;

        Vector3 center = GetHolderCenter();
        Bounds b = bodyCol.bounds;
        Vector3 closest = b.ClosestPoint(new Vector3(center.x, center.y, b.center.z));
        return Vector2.Distance(center, closest) <= GetCircleWorldRadius(holderCircle, 0.75f);
    }

    #endregion

    #region Scene arrival

    private IEnumerator ArrivalRoutine(PlayerSpawner spawner, bool returnToPark)
    {
        state = ShipState.Busy;
        SetSymbolVisible(false);
        EnterFlightMode();

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SnapToTarget(spawner.CameraTrackTransform);

        PlaceAtArrivalStart(spawner);

        float waited = 0f;
        while ((ScreenFade.IsBusy || CurrentFadeAlpha() > 0.001f) && waited < 10f)
        {
            waited += Time.unscaledDeltaTime;
            PlaceAtArrivalStart(spawner);
            yield return null;
        }

        SetFlyingAnimation(true, playTrigger: true);
        yield return FlyTo(ResolveArrivalHoverPosition(spawner), arrivalSpeed, 0f, 2f);
        yield return HoverInPlace(hoverBeforeRelease);

        PlayerController player = spawner.SpawnFromRocketShip(GetSpawnPosition());
        if (player != null)
        {
            player.SetInputLocked(true);
            EnsureShipInFrontOf(player);
        }

        yield return HoverInPlace(waitAfterPlayerSpawn);

        Vector3 dir = AngleToDirection(departAngle);
        float speed = departSpeed * 0.35f;
        float elapsed = 0f;
        bool released = false;
        while (elapsed < 10f)
        {
            float dt = Time.deltaTime;
            speed = Mathf.MoveTowards(speed, departSpeed, departSpeed * 1.5f * dt);
            transform.position += dir * (speed * dt);
            elapsed += dt;

            if (!released && IsOutsideView(0f))
            {
                released = true;
                FinishArrival(player);
            }

            if (IsOutsideView(despawnDistanceOutsideView))
                break;

            yield return null;
        }

        if (!released)
            FinishArrival(player);

        if (!returnToPark)
        {
            Destroy(gameObject);
            yield break;
        }

        if (body != null)
            body.flipX = parkPosition.x < transform.position.x ? !originalFlipX : originalFlipX;

        yield return FlyTo(parkPosition, returnToParkSpeed, 0f, 3f);
        Park();
    }

    private void FinishArrival(PlayerController player)
    {
        ownsArrivalFlag = false;
        IsArrivalInProgress = false;

        if (player == null)
            player = PlayerController.ResolveActive();

        // Boss fights keep the player frozen until their WARNING intro finishes.
        if (player != null && !player.IsDead && !WarningText.BlocksGameplay && !BossFightDirector.IntroPending)
            player.SetInputLocked(false);
    }

    private void PlaceAtArrivalStart(PlayerSpawner spawner)
    {
        PlaceOffscreenAbove(-1f, spawner.CameraTrackTransform.position);
    }

    /// <summary>
    /// Just outside the camera's top-left (<paramref name="side"/> -1) or top-right (+1) corner,
    /// <see cref="arrivalStartOffset"/> away from the ship's nearest edge.
    /// </summary>
    private void PlaceOffscreenAbove(float side, Vector3 fallbackAnchor)
    {
        Bounds visual = GetVisualBounds();
        Vector3 pivotOffset = transform.position - visual.center;
        float gapX = Mathf.Abs(arrivalStartOffset.x) + visual.extents.x;
        float gapY = Mathf.Abs(arrivalStartOffset.y) + visual.extents.y;

        Vector3 center;
        if (TryGetViewRect(out Rect view))
        {
            float x = side < 0f ? view.xMin - gapX : view.xMax + gapX;
            center = new Vector3(x, view.yMax + gapY, 0f);
        }
        else
        {
            center = fallbackAnchor + new Vector3(side * 12f, 8f, 0f);
        }

        transform.position = new Vector3(center.x + pivotOffset.x, center.y + pivotOffset.y, transform.position.z);
    }

    private static float CurrentFadeAlpha()
    {
        return ScreenFade.EnsureExists().CurrentAlpha;
    }

    private Vector3 ResolveArrivalHoverPosition(PlayerSpawner spawner)
    {
        Vector3 spawnOffset = GetSpawnPosition() - transform.position;
        Vector3 fallback = spawner.CameraTrackTransform.position - spawnOffset;
        fallback.z = transform.position.z;

        if (!TryGetViewRect(out Rect view))
            return fallback;

        Vector3 hover = new Vector3(view.center.x, view.center.y, transform.position.z);

        // Never drop the player inside level geometry.
        int ground = LayerMask.GetMask("Ground");
        if (ground != 0 && Physics2D.OverlapCircle(hover + spawnOffset, 0.45f, ground) != null)
            return fallback;

        return hover;
    }

    private Vector3 GetSpawnPosition()
    {
        return playerSpawnPoint != null ? playerSpawnPoint.position : transform.position;
    }

    private void Park()
    {
        if (body != null)
            body.flipX = originalFlipX;

        SetShipSorting(originalSortingLayerId, originalSortingOrder);
        SetFlyingAnimation(false, playTrigger: false);
        ExitFlightMode();
        state = ShipState.Parked;
    }

    #endregion

    #region Post-boss landing / pause pick-up

    /// <summary>
    /// Flies to the spot beside the player (from off-screen on that side when <paramref name="fresh"/>).
    /// Post-boss ships then hover and wait; pause pick-ups board the player right away.
    /// </summary>
    private IEnumerator ApproachPlayerRoutine(PlayerController player, bool fresh, bool autoBoard)
    {
        state = ShipState.Busy;
        SetSymbolVisible(false);
        EnterFlightMode();
        if (body != null)
            body.flipX = originalFlipX;
        SetFlyingAnimation(true, playTrigger: true);

        Vector3 landing = ResolveBossLandingPosition(player);
        if (fresh)
        {
            float side = landing.x >= player.transform.position.x ? 1f : -1f;
            PlaceOffscreenAbove(side, landing);
        }

        if (player != null && player.isActiveAndEnabled)
            EnsureShipInFrontOf(player);

        yield return FlyTo(landing, autoBoard ? pickUpApproachSpeed : bossApproachSpeed, 0f, 2.5f);
        BeginHover();

        if (!autoBoard)
            yield break;

        if (player != null && player.isActiveAndEnabled && !player.IsDead)
        {
            yield return BoardAndLaunchRoutine(player);
            if (state != ShipState.Busy)
                LoadDestination();
        }
        else
        {
            state = ShipState.Busy;
            LoadDestination();
        }
    }

    #endregion

    #region Bannana Phone supply drop

    private const float SupplyDropInterval = 0.25f;

    private IEnumerator SupplyDropRoutine(
        PlayerController player,
        CollectableConsumable juiceBox,
        CollectableConsumable hotDog,
        int count,
        float hoverAboveHead,
        bool spawnedNow)
    {
        ShipState previousState = state;
        Vector3 previousHoverBase = hoverBase;
        bool fresh = spawnedNow || IsOutsideView(0f);

        state = ShipState.Busy;
        SetSymbolVisible(false);
        EnterFlightMode();
        if (body != null)
            body.flipX = originalFlipX;
        SetFlyingAnimation(true, playTrigger: true);

        if (fresh)
            PlaceOffscreenAbove(-1f, player != null ? player.transform.position : transform.position);

        // Swoop in, tracking the player as they move.
        float speed = Mathf.Max(0.5f, pickUpApproachSpeed);
        float timeout = 8f;
        while (timeout > 0f && player != null && !player.IsDead)
        {
            Vector3 target = ResolveSupplyHoverPosition(player, hoverAboveHead);
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude < 0.0025f)
                break;

            timeout -= Time.deltaTime;
            yield return null;
        }

        // Drop the supplies one at a time, staying over the player.
        float nextDrop = 0f;
        int dropped = 0;
        float bobTime = 0f;
        while (dropped < count)
        {
            float dt = Time.deltaTime;
            bobTime += dt;
            if (player != null && !player.IsDead)
            {
                Vector3 target = ResolveSupplyHoverPosition(player, hoverAboveHead)
                    + Vector3.up * (Mathf.Sin(bobTime * hoverBobSpeed * Mathf.PI) * hoverBobAmplitude);
                transform.position = Vector3.MoveTowards(transform.position, target, speed * dt);
            }

            nextDrop -= dt;
            if (nextDrop <= 0f)
            {
                UsableItem.DropSupply(juiceBox, hotDog, GetSpawnPosition() + Vector3.right * Random.Range(-0.3f, 0.3f));
                dropped++;
                nextDrop = SupplyDropInterval;
            }

            yield return null;
        }

        yield return HoverInPlace(0.3f);

        // Fly away like the scene-arrival departure.
        Vector3 dir = AngleToDirection(departAngle);
        float departCurrent = departSpeed * 0.35f;
        float elapsed = 0f;
        while (elapsed < 10f && !IsOutsideView(despawnDistanceOutsideView))
        {
            float dt = Time.deltaTime;
            departCurrent = Mathf.MoveTowards(departCurrent, departSpeed, departSpeed * 1.5f * dt);
            transform.position += dir * (departCurrent * dt);
            elapsed += dt;
            yield return null;
        }

        if (spawnedNow)
        {
            Destroy(gameObject);
            yield break;
        }

        if (previousState == ShipState.Hovering)
        {
            if (body != null)
                body.flipX = previousHoverBase.x < transform.position.x ? !originalFlipX : originalFlipX;
            yield return FlyTo(previousHoverBase, returnToParkSpeed, 0f, 3f);
            if (body != null)
                body.flipX = originalFlipX;
            BeginHover();
            yield break;
        }

        if (body != null)
            body.flipX = parkPosition.x < transform.position.x ? !originalFlipX : originalFlipX;
        yield return FlyTo(parkPosition, returnToParkSpeed, 0f, 3f);
        Park();
    }

    /// <summary>Drop point over the player's center, ship bottom <paramref name="hoverAboveHead"/> above their head.</summary>
    private Vector3 ResolveSupplyHoverPosition(PlayerController player, float hoverAboveHead)
    {
        float pivotToBottom = transform.position.y - GetVisualBounds().min.y;
        float spawnOffsetX = GetSpawnPosition().x - transform.position.x;

        Bounds playerBounds = player.BodyCollider != null && player.BodyCollider.enabled
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);

        return new Vector3(
            playerBounds.center.x - spawnOffsetX,
            playerBounds.max.y + hoverAboveHead + pivotToBottom,
            transform.position.z);
    }

    #endregion

    #region Checkpoint respawn

    private IEnumerator CheckpointRespawnRoutine(PlayerSpawner spawner, CheckPointBot checkpoint, bool startOffscreen)
    {
        state = ShipState.Busy;
        SetSymbolVisible(false);
        EnterFlightMode();
        if (body != null)
            body.flipX = originalFlipX;
        if (startOffscreen)
            PlaceOffscreenAbove(-1f, checkpoint.HomeWorldPosition);

        // Stay out of sight until the respawn fade-in has fully finished (a ship already in view waits in place).
        float waited = 0f;
        while ((ScreenFade.IsBusy || CurrentFadeAlpha() > 0.001f) && waited < 15f)
        {
            waited += Time.unscaledDeltaTime;
            if (IsOutsideView(0f))
                PlaceOffscreenAbove(-1f, checkpoint.HomeWorldPosition);
            yield return null;
        }

        SetFlyingAnimation(true, playTrigger: true);
        yield return FlyTo(ResolveCheckpointHoverPosition(checkpoint), arrivalSpeed, 0f, 2f);
        yield return HoverInPlace(hoverBeforeRelease);

        PlayerController player = spawner.SpawnFromRocketShip(GetSpawnPosition());
        if (player != null)
        {
            player.SetInputLocked(true);
            EnsureShipInFrontOf(player);
        }

        yield return HoverInPlace(waitAfterPlayerSpawn);

        ownsRespawnFlag = false;
        IsRespawnDeliveryInProgress = false;
        BeginHover();
    }

    /// <summary>
    /// Directly above the checkpoint (the player drops straight onto the checkpoint's safe ground),
    /// with the ship's bottom <see cref="checkpointHoverGap"/> above the top of the checkpoint's bob.
    /// </summary>
    private Vector3 ResolveCheckpointHoverPosition(CheckPointBot checkpoint)
    {
        float pivotToBottom = transform.position.y - GetVisualBounds().min.y;
        float spawnOffsetX = GetSpawnPosition().x - transform.position.x;

        Vector3 home = checkpoint.HomeWorldPosition;
        SpriteRenderer checkpointArt = checkpoint.GetComponent<SpriteRenderer>();
        float checkpointTop = home.y + (checkpointArt != null ? checkpointArt.bounds.extents.y : 0.5f) + checkpoint.BobAmplitude;

        return new Vector3(
            home.x - spawnOffsetX,
            checkpointTop + checkpointHoverGap + pivotToBottom,
            transform.position.z);
    }

    #endregion

    #region Landing spot helpers

    private Vector3 ResolveBossLandingPosition(PlayerController player)
    {
        Bounds visual = GetVisualBounds();
        float halfWidth = visual.extents.x;
        float pivotOffsetX = transform.position.x - visual.center.x;
        float pivotToBottom = transform.position.y - visual.min.y;

        Bounds playerBounds = player.BodyCollider != null && player.BodyCollider.enabled
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);
        Vector2 playerCenter = playerBounds.center;

        LayerMask ground = player.GroundLayers;
        if (ground.value == 0)
            ground = LayerMask.GetMask("Ground");

        float centerDistance = playerBounds.extents.x + bossLandDistanceFromPlayer + halfWidth;
        float needed = centerDistance + halfWidth;
        float roomRight = MeasureRoom(playerBounds, 1f, ground, needed + 4f, player);
        float roomLeft = MeasureRoom(playerBounds, -1f, ground, needed + 4f, player);
        float side = roomRight >= roomLeft ? 1f : -1f;
        float room = side > 0f ? roomRight : roomLeft;

        float offset = Mathf.Min(centerDistance, Mathf.Max(0f, room - halfWidth));
        float centerX = playerCenter.x + side * offset;

        float playerGroundY = ProbeGroundY(new Vector2(playerCenter.x, playerCenter.y), ground, player, playerBounds.min.y);
        float groundY = ProbeGroundY(new Vector2(centerX, playerGroundY + 1.5f), ground, player, playerGroundY);
        if (groundY < playerGroundY - 3f)
            groundY = playerGroundY;

        return new Vector3(centerX + pivotOffsetX, groundY + bossHeightAboveGround + pivotToBottom, transform.position.z);
    }

    private static float MeasureRoom(Bounds playerBounds, float dir, LayerMask ground, float maxDistance, PlayerController player)
    {
        float room = maxDistance;
        Vector2 direction = new Vector2(dir, 0f);
        float[] heights = { playerBounds.center.y, playerBounds.min.y + 0.25f };

        for (int h = 0; h < heights.Length; h++)
        {
            Vector2 origin = new Vector2(playerBounds.center.x, heights[h]);
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, maxDistance, ground);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i].collider;
                if (!IsSolidWorldCollider(col, player) || hits[i].distance <= 0.001f)
                    continue;

                room = Mathf.Min(room, hits[i].distance);
            }
        }

        if (CameraFollow.Instance != null && CameraFollow.Instance.TryGetPlayableBounds(out Bounds playable))
        {
            float edge = dir > 0f
                ? playable.max.x - playerBounds.center.x
                : playerBounds.center.x - playable.min.x;
            room = Mathf.Min(room, Mathf.Max(0f, edge));
        }

        return room;
    }

    private static float ProbeGroundY(Vector2 origin, LayerMask ground, PlayerController player, float fallbackY)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, 40f, ground);
        float best = float.NegativeInfinity;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsSolidWorldCollider(hits[i].collider, player, allowOneWayPlatforms: true) || hits[i].distance <= 0.001f)
                continue;

            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                best = hits[i].point.y;
            }
        }

        return float.IsNegativeInfinity(best) ? fallbackY : best;
    }

    private static bool IsSolidWorldCollider(Collider2D col, PlayerController player, bool allowOneWayPlatforms = false)
    {
        if (col == null || col.isTrigger)
            return false;

        if (player != null && col.transform.IsChildOf(player.transform))
            return false;

        if (!allowOneWayPlatforms && (col.usedByEffector || col.GetComponent<PlatformEffector2D>() != null))
            return false;

        return col.GetComponentInParent<KitRocketShip>() == null;
    }

    private void BeginHover()
    {
        hoverBase = transform.position;
        hoverTime = 0f;
        noPlayerTimer = 0f;
        state = ShipState.Hovering;
    }

    /// <summary>
    /// Boss Fight Mode has no respawn after the boss falls: a ship with nobody left to board it
    /// still takes the game to its destination. Levels respawn the player instead.
    /// </summary>
    private void TickNoPlayerFallback()
    {
        if (!BossFightDirector.IsActiveBossFightScene())
            return;

        PlayerController player = PlayerController.ResolveActive();
        if (player != null && !player.IsDead)
        {
            noPlayerTimer = 0f;
            return;
        }

        noPlayerTimer += Time.deltaTime;
        if (noPlayerTimer < 2f || ScreenFade.IsBusy || !CanLoadDestination())
            return;

        state = ShipState.Busy;
        LoadDestination();
    }

    #endregion

    #region Flight helpers

    private IEnumerator FlyTo(Vector3 target, float maxSpeed, float acceleration, float brakeDistance)
    {
        target.z = transform.position.z;
        maxSpeed = Mathf.Max(0.05f, maxSpeed);
        float speed = acceleration > 0f ? 0f : maxSpeed;

        while ((transform.position - target).sqrMagnitude > 0.0001f)
        {
            float dt = Time.deltaTime;
            float distance = Vector2.Distance(transform.position, target);
            if (acceleration > 0f)
                speed = Mathf.MoveTowards(speed, maxSpeed, acceleration * dt);

            float step = speed;
            if (brakeDistance > 0.01f)
                step = Mathf.Min(step, Mathf.Max(maxSpeed * 0.2f, maxSpeed * distance / brakeDistance));

            transform.position = Vector3.MoveTowards(transform.position, target, step * dt);
            yield return null;
        }

        transform.position = target;
    }

    private IEnumerator HoverInPlace(float seconds)
    {
        Vector3 anchor = transform.position;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            transform.position = anchor + Vector3.up * (Mathf.Sin(t * hoverBobSpeed * Mathf.PI) * hoverBobAmplitude);
            yield return null;
        }

        transform.position = anchor;
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    private static Vector3 AngleToDirection(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
    }

    /// <summary>Physics off + solid colliders off while flying / hovering (the ship never pushes anything).</summary>
    private void EnterFlightMode()
    {
        if (inFlightMode)
            return;

        inFlightMode = true;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        for (int i = 0; i < solidColliders.Length; i++)
        {
            if (solidColliders[i] != null)
                solidColliders[i].enabled = false;
        }
    }

    private void ExitFlightMode()
    {
        if (!inFlightMode)
            return;

        inFlightMode = false;
        for (int i = 0; i < solidColliders.Length; i++)
        {
            if (solidColliders[i] != null)
                solidColliders[i].enabled = solidCollidersEnabled[i];
        }

        if (rb != null)
        {
            rb.simulated = rbWasSimulated;
            rb.linearVelocity = Vector2.zero;
        }

        lastPhasedPlayerId = int.MinValue;
        EnsurePhasedWithActivePlayer();
    }

    private void SetFlyingAnimation(bool flying, bool playTrigger)
    {
        if (animator == null)
            return;

        if (hasIsFlyingParam)
            animator.SetBool("IsFlying", flying);
        if (playTrigger && hasFlyParam)
            animator.SetTrigger("Fly");
    }

    private bool IsOutsideView(float margin)
    {
        if (!TryGetViewRect(out Rect view))
            return true;

        Bounds b = GetVisualBounds();
        if (riderSeated && rider != null)
        {
            SpriteRenderer riderRenderer = rider.GetComponentInChildren<SpriteRenderer>();
            if (riderRenderer != null)
                b.Encapsulate(riderRenderer.bounds);
        }

        return b.max.x < view.xMin - margin ||
               b.min.x > view.xMax + margin ||
               b.max.y < view.yMin - margin ||
               b.min.y > view.yMax + margin;
    }

    private static bool TryGetViewRect(out Rect view)
    {
        Camera cam = CameraFollow.Instance != null ? CameraFollow.Instance.GetComponent<Camera>() : Camera.main;
        if (cam == null)
        {
            view = default;
            return false;
        }

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 c = cam.transform.position;
        view = new Rect(c.x - halfWidth, c.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        return true;
    }

    private Bounds GetVisualBounds()
    {
        if (body != null)
            return body.bounds;

        return new Bounds(transform.position, new Vector3(7f, 4f, 0f));
    }

    #endregion

    #region Sorting

    private void SwapSortingWithRider(PlayerController player)
    {
        if (riderSortingSwapped)
            return;

        GetRiderSorting(player, out riderOriginalLayerId, out riderOriginalOrder);
        GetShipSorting(out int shipLayer, out int shipOrder);

        SetRiderSorting(player, shipLayer, shipOrder);
        SetShipSorting(riderOriginalLayerId, riderOriginalOrder);
        riderSortingSwapped = true;
        EnsureShipInFrontOf(player);
    }

    private void RestoreRiderSorting()
    {
        if (!riderSortingSwapped)
            return;

        riderSortingSwapped = false;
        if (rider != null)
            SetRiderSorting(rider, riderOriginalLayerId, riderOriginalOrder);
    }

    private void EnsureShipInFrontOf(PlayerController player)
    {
        GetRiderSorting(player, out int riderLayer, out int riderOrder);
        GetShipSorting(out int shipLayer, out int shipOrder);

        int riderValue = SortingLayer.GetLayerValueFromID(riderLayer);
        int shipValue = SortingLayer.GetLayerValueFromID(shipLayer);
        if (shipValue > riderValue || (shipValue == riderValue && shipOrder > riderOrder))
            return;

        SetShipSorting(riderLayer, riderOrder + 1);
    }

    private void GetShipSorting(out int layerId, out int order)
    {
        if (shipSortingGroup != null)
        {
            layerId = shipSortingGroup.sortingLayerID;
            order = shipSortingGroup.sortingOrder;
            return;
        }

        layerId = body != null ? body.sortingLayerID : 0;
        order = body != null ? body.sortingOrder : 0;
    }

    private void SetShipSorting(int layerId, int order)
    {
        if (shipSortingGroup != null)
        {
            shipSortingGroup.sortingLayerID = layerId;
            shipSortingGroup.sortingOrder = order;
        }
        else if (body != null)
        {
            body.sortingLayerID = layerId;
            body.sortingOrder = order;
        }
    }

    private static void GetRiderSorting(PlayerController player, out int layerId, out int order)
    {
        SortingGroup group = player.EffectSortingGroup;
        if (group != null)
        {
            layerId = group.sortingLayerID;
            order = group.sortingOrder;
            return;
        }

        SpriteRenderer sr = player.GetComponentInChildren<SpriteRenderer>();
        layerId = sr != null ? sr.sortingLayerID : 0;
        order = sr != null ? sr.sortingOrder : 0;
    }

    private static void SetRiderSorting(PlayerController player, int layerId, int order)
    {
        SortingGroup group = player.EffectSortingGroup;
        if (group != null)
        {
            group.sortingLayerID = layerId;
            group.sortingOrder = order;
            return;
        }

        SpriteRenderer sr = player.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingLayerID = layerId;
            sr.sortingOrder = order;
        }
    }

    #endregion

    #region Parts + symbol

    private void CacheParts()
    {
        if (playerHolder == null)
            playerHolder = FindChildNamed("Player Holder");

        if (playerSpawnPoint == null)
            playerSpawnPoint = FindChildNamed("Rocket Ship's Player Spawner") ?? FindChildNamed("Player Spawner");

        if (interactRadius == null)
        {
            interactRadius = FindChildNamed("Interactble radius") ??
                             FindChildNamed("Interact Radius") ??
                             FindChildNamed("Interactable radius");
        }

        if (interactSymbolBox == null)
        {
            interactSymbolBox = FindChildNamed("Interactble symbol") ??
                                FindChildNamed("Interactble Symbol box") ??
                                FindChildNamed("Interactable symbol box");
        }

        holderCircle = playerHolder != null ? playerHolder.GetComponent<CircleCollider2D>() : null;
        interactCircle = interactRadius != null ? interactRadius.GetComponent<CircleCollider2D>() : null;
    }

    /// <summary>Holder + interact radius are detection-only; they must never block the player or rest on ground.</summary>
    private void ConfigureDetectionColliders()
    {
        if (playerHolder != null)
        {
            Collider2D col = playerHolder.GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
        }

        if (interactRadius != null)
        {
            if (interactRadius.GetComponent<NpcInteractZone>() == null)
                interactRadius.gameObject.AddComponent<NpcInteractZone>();

            Collider2D col = interactRadius.GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;

            SpriteRenderer visual = interactRadius.GetComponent<SpriteRenderer>();
            if (visual != null)
                visual.enabled = false;
        }
    }

    private void CacheSolidColliders()
    {
        Collider2D[] all = GetComponentsInChildren<Collider2D>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].isTrigger)
                count++;
        }

        solidColliders = new Collider2D[count];
        solidCollidersEnabled = new bool[count];
        int n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].isTrigger)
                continue;

            solidColliders[n] = all[i];
            solidCollidersEnabled[n] = all[i].enabled;
            n++;
        }
    }

    private void CacheAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == "IsFlying" && parameters[i].type == AnimatorControllerParameterType.Bool)
                hasIsFlyingParam = true;
            else if (parameters[i].name == "Fly" && parameters[i].type == AnimatorControllerParameterType.Trigger)
                hasFlyParam = true;
        }
    }

    private void EnsurePhasedWithActivePlayer()
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
            return;

        int id = player.GetInstanceID();
        if (id == lastPhasedPlayerId)
            return;

        Collider2D[] playerCols = player.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < solidColliders.Length; i++)
        {
            Collider2D mine = solidColliders[i];
            if (mine == null || !mine.enabled)
                continue;

            for (int j = 0; j < playerCols.Length; j++)
            {
                if (playerCols[j] != null && !playerCols[j].isTrigger)
                    Physics2D.IgnoreCollision(mine, playerCols[j], true);
            }
        }

        lastPhasedPlayerId = id;
    }

    private void EnsureInteractSymbol()
    {
        if (symbolInstance != null || interactSymbolBox == null)
            return;

        if (interactSymbolPrefab == null)
            interactSymbolPrefab = NPC.FindInteractSymbolPrefab();

        if (interactSymbolPrefab == null)
        {
            Debug.LogWarning($"[KitRocketShip] '{name}' has no Interactble Symbol prefab assigned.", this);
            return;
        }

        GameVisualEffect spawned = Instantiate(interactSymbolPrefab, interactSymbolBox);
        spawned.name = "Interactble Symbol";
        spawned.transform.localPosition = Vector3.zero;
        spawned.transform.localRotation = Quaternion.identity;

        // The symbol box is stretched on X; cancel that so the symbol keeps its authored proportions.
        Vector3 lossy = interactSymbolBox.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
        float uniform = Mathf.Min(sx, sy);
        spawned.transform.localScale = new Vector3(uniform / sx, uniform / sy, 1f);

        spawned.ResetInteractableBobBase();
        symbolInstance = spawned;
        symbolInstance.gameObject.SetActive(false);
    }

    private void SetSymbolVisible(bool visible)
    {
        if (symbolInstance == null)
        {
            if (!visible)
                return;

            EnsureInteractSymbol();
            if (symbolInstance == null)
                return;
        }

        if (visible && !symbolInstance.gameObject.activeSelf)
        {
            symbolInstance.transform.localPosition = Vector3.zero;
            symbolInstance.ResetInteractableBobBase();
        }

        if (symbolInstance.gameObject.activeSelf != visible)
            symbolInstance.gameObject.SetActive(visible);
    }

    private Vector3 GetHolderCenter()
    {
        if (holderCircle != null)
            return holderCircle.transform.TransformPoint(holderCircle.offset);

        if (playerHolder != null)
            return playerHolder.position;

        return GetVisualBounds().center;
    }

    private Vector3 GetInteractCenter()
    {
        if (interactCircle != null)
            return interactCircle.transform.TransformPoint(interactCircle.offset);

        if (interactRadius != null)
            return interactRadius.position;

        return GetHolderCenter();
    }

    private static float GetCircleWorldRadius(CircleCollider2D circle, float fallback)
    {
        if (circle == null)
            return fallback;

        Vector3 lossy = circle.transform.lossyScale;
        return circle.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y));
    }

    private static Vector3 GetBodyCenter(PlayerController player)
    {
        Collider2D col = player.BodyCollider;
        if (col != null && col.enabled && !player.IsRidingVehicle)
            return col.bounds.center;

        return player.transform.position;
    }

    private Transform FindChildNamed(string objectName)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (string.Equals(child.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        fadeOutSeconds = Mathf.Max(0.01f, fadeOutSeconds);
        fadeInSeconds = Mathf.Max(0.01f, fadeInSeconds);
        jumpHeightAboveShip = Mathf.Max(0f, jumpHeightAboveShip);
        boardRunTimeout = Mathf.Max(0.5f, boardRunTimeout);
        seatSnapDuration = Mathf.Max(0.01f, seatSnapDuration);
        riseSpeed = Mathf.Max(0.05f, riseSpeed);
        backUpSpeed = Mathf.Max(0.05f, backUpSpeed);
        launchSpeed = Mathf.Max(0.5f, launchSpeed);
        launchStartSpeed = Mathf.Clamp(launchStartSpeed, 0.1f, launchSpeed);
        launchAcceleration = Mathf.Max(0f, launchAcceleration);
        arrivalSpeed = Mathf.Max(0.5f, arrivalSpeed);
        departSpeed = Mathf.Max(0.5f, departSpeed);
        despawnDistanceOutsideView = Mathf.Max(0f, despawnDistanceOutsideView);
        returnToParkSpeed = Mathf.Max(0.5f, returnToParkSpeed);
        bossLandDistanceFromPlayer = Mathf.Max(0f, bossLandDistanceFromPlayer);
        bossHeightAboveGround = Mathf.Max(0f, bossHeightAboveGround);
        bossApproachSpeed = Mathf.Max(0.1f, bossApproachSpeed);
        pickUpApproachSpeed = Mathf.Max(0.1f, pickUpApproachSpeed);
        checkpointHoverGap = Mathf.Max(0f, checkpointHoverGap);
        hoverBobAmplitude = Mathf.Max(0f, hoverBobAmplitude);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(GetHolderCenter(), GetCircleWorldRadius(playerHolder != null ? playerHolder.GetComponent<CircleCollider2D>() : null, 0.75f));
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        Gizmos.DrawWireSphere(GetInteractCenter(), GetCircleWorldRadius(interactRadius != null ? interactRadius.GetComponent<CircleCollider2D>() : null, 1.5f));
    }
#endif
}
