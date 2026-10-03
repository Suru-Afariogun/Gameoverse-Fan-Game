using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Town NPC: phases through the player, shows an interact symbol in range,
/// and opens the shared DialogueBox when the player presses Up / stick up.
/// Interact radius is detection-only (same rules as enemy detection radii).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class NPC : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string speakerName;
    [Tooltip("Kit / default dialogue. Edit freely in the Inspector.")]
    [TextArea(2, 6)]
    [SerializeField] private string[] dialoguePages =
    {
        "Hello! This is dummy page 1 for testing.",
        "This is dummy page 2. Press Confirm to continue.",
        "Last dummy page. Confirm again to close."
    };
    [Tooltip("Shown when talking to Malice. Leave empty to fall back to Kit/default.")]
    [TextArea(2, 6)]
    [SerializeField] private string[] dialoguePagesMalice;
    [Tooltip("Shown when talking to Count. Leave empty to fall back to Kit/default.")]
    [TextArea(2, 6)]
    [SerializeField] private string[] dialoguePagesCount;
    [Tooltip("Shown when talking to Harlie. Leave empty to fall back to Kit/default.")]
    [TextArea(2, 6)]
    [SerializeField] private string[] dialoguePagesHarlie;
    [Tooltip("Shown when talking to Hex. Leave empty to fall back to Harlie, then Kit/default.")]
    [TextArea(2, 6)]
    [SerializeField] private string[] dialoguePagesHex;

    [Header("Interact")]
    [SerializeField] private Transform interactRadius;
    [SerializeField] private Collider2D interactCollider;
    [SerializeField] private Transform interactableSymbolBox;
    [Tooltip("Interactable Symbol GameVisualEffect prefab (type InteractableSymbol).")]
    [SerializeField] private GameVisualEffect interactSymbolPrefab;
    [SerializeField] private GameVisualEffect interactSymbolInstance;
    [SerializeField] [Range(0.1f, 1f)] private float stickUpThreshold = 0.5f;

    [Header("Shop (optional)")]
    [Tooltip("Opens as soon as this NPC finishes speaking. Auto-found on this object if empty.")]
    [SerializeField] private UpgradeShop shopAfterDialogue;

    [Header("Facing")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Authored art faces left by default. flipX turns on when facing right.")]
    [SerializeField] private bool spriteFacesLeft = true;

    [Header("Ground (for future animations)")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayers;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private CircleCollider2D interactCircle;
    private InputActions controls;
    private PlayerController playerInRange;
    private bool moveUpHeld;
    private int lastPhasedPlayerId = int.MinValue;
    private bool naturalFlipX;
    private bool hasCachedNaturalFacing;
    private bool facingPlayerForDialogue;

    public bool PlayerInRange => playerInRange != null;
    private bool IsShopOpen => shopAfterDialogue != null && shopAfterDialogue.IsOpen;
    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (shopAfterDialogue == null)
            shopAfterDialogue = GetComponent<UpgradeShop>();

        CacheChildren();
        ConfigureBody();
        ConfigureInteractRadius();
        EnsureInteractSymbol();
        SetSymbolVisible(false);
        CacheNaturalFacing();

        if (string.IsNullOrWhiteSpace(speakerName))
            speakerName = gameObject.name;
    }

    private void Start()
    {
        CacheNaturalFacing();
        RefreshPhaseCollisions();
        HideSceneSymbolTemplates();
        DeferredEnemyPhaseRefresh.Request();
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
        RefreshPhaseCollisions();
        DeferredEnemyPhaseRefresh.Request();
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
        lastPhasedPlayerId = int.MinValue;
        RestoreNaturalFacing();
    }

    private void OnDestroy()
    {
        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    private void Update()
    {
        UpdateGrounded();
        UpdateRange();
        EnsurePhasedWithActivePlayer();

        if (facingPlayerForDialogue &&
            playerInRange != null &&
            (IsShopOpen || (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)))
        {
            FacePlayerForDialogue(playerInRange);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null)
            return;

        // Safety net on scene re-entry: if phasing was missed, force it on contact.
        if (BelongsToPlayer(collision.collider))
            IgnoreAgainstCollider(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null)
            return;

        if (BelongsToPlayer(collision.collider))
            IgnoreAgainstCollider(collision.collider);
    }

    /// <summary>
    /// Re-apply player IgnoreCollision for every NPC in the loaded scenes.
    /// Called from the deferred phase batch after the player exists.
    /// </summary>
    public static void RefreshAllPhaseCollisions()
    {
        NPC[] npcs = Object.FindObjectsByType<NPC>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null)
                npcs[i].RefreshPhaseCollisions();
        }
    }

    /// <summary>
    /// Body phases through players. Interact radius stays a trigger (range uses distance).
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        RefreshBodyPlayerPhasing();
        ConfigureInteractRadius();
    }

    private void EnsurePhasedWithActivePlayer()
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
            return;

        int id = player.GetInstanceID();
        if (id == lastPhasedPlayerId)
            return;

        RefreshBodyPlayerPhasing();
    }

    private void RefreshBodyPlayerPhasing()
    {
        if (bodyCollider == null)
            return;

        PlayerController player = PlayerController.ResolveActive();
        if (player == null)
        {
            // Player may spawn a frame later on HomeTown re-entry.
            lastPhasedPlayerId = int.MinValue;
            return;
        }

        Collider2D[] playerCols = player.GetComponentsInChildren<Collider2D>(true);
        for (int c = 0; c < playerCols.Length; c++)
        {
            Collider2D other = playerCols[c];
            if (other == null || other.isTrigger)
                continue;

            Physics2D.IgnoreCollision(bodyCollider, other, true);
        }

        lastPhasedPlayerId = player.GetInstanceID();
    }

    private void IgnoreAgainstCollider(Collider2D other)
    {
        if (bodyCollider == null || other == null || other.isTrigger)
            return;

        Physics2D.IgnoreCollision(bodyCollider, other, true);

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
            lastPhasedPlayerId = player.GetInstanceID();
    }

    private static bool BelongsToPlayer(Collider2D col)
    {
        return col != null && col.GetComponentInParent<PlayerController>() != null;
    }

    private void CacheChildren()
    {
        if (interactRadius == null)
        {
            interactRadius =
                FindChildNamed(transform, "Interact Radius") ??
                FindChildNamed(transform, "Interact radius") ??
                FindChildNamed(transform, "Interactble radius");
        }

        if (interactRadius != null && interactCollider == null)
            interactCollider = interactRadius.GetComponent<Collider2D>();

        if (interactCollider != null)
            interactCircle = interactCollider as CircleCollider2D;

        if (interactableSymbolBox == null)
        {
            interactableSymbolBox =
                FindChildNamed(transform, "Interactble Symbol box") ??
                FindChildNamed(transform, "Interactable symbol box") ??
                FindChildNamed(transform, "Interactable Symbol box");
        }

        if (groundCheck == null)
            groundCheck = FindChildNamed(transform, "Ground Check");

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Ground");
    }

    private void ConfigureBody()
    {
        if (rb != null)
        {
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (bodyCollider != null)
            bodyCollider.isTrigger = false;
    }

    private void ConfigureInteractRadius()
    {
        if (interactRadius == null)
            return;

        if (interactRadius.GetComponent<NpcInteractZone>() == null)
            interactRadius.gameObject.AddComponent<NpcInteractZone>();

        SpriteRenderer visual = interactRadius.GetComponent<SpriteRenderer>();
        if (visual != null)
            visual.enabled = false;

        if (interactCollider == null)
            return;

        // Detection only — never a solid world collider. Keep enabled so radius reads stay valid.
        interactCollider.isTrigger = true;
        interactCollider.enabled = true;
        interactCircle = interactCollider as CircleCollider2D;
    }

    private void EnsureInteractSymbol()
    {
        if (interactSymbolInstance != null || interactableSymbolBox == null)
            return;

        if (interactSymbolPrefab == null)
            interactSymbolPrefab = FindInteractSymbolPrefab();

        if (interactSymbolPrefab == null)
        {
            Debug.LogWarning($"[NPC] '{name}' has no Interactble Symbol prefab assigned.", this);
            return;
        }

        GameVisualEffect spawned = Instantiate(interactSymbolPrefab, interactableSymbolBox);
        spawned.name = "Interactble Symbol";
        spawned.transform.localPosition = Vector3.zero;
        spawned.transform.localRotation = Quaternion.identity;
        spawned.transform.localScale = Vector3.one;
        spawned.ResetInteractableBobBase();
        interactSymbolInstance = spawned;
        interactSymbolInstance.gameObject.SetActive(false);
    }

    internal static GameVisualEffect FindInteractSymbolPrefab()
    {
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab Interactble Symbol");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            GameVisualEffect fx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameVisualEffect>(path);
            if (fx != null && fx.IsInteractableSymbolType)
                return fx;
        }
#endif
        GameVisualEffect[] all = FindObjectsByType<GameVisualEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            GameVisualEffect fx = all[i];
            if (fx != null && fx.IsInteractableSymbolType && fx.gameObject.scene.IsValid())
                return fx;
        }

        return null;
    }

    private static void HideSceneSymbolTemplates()
    {
        HideIfExists("Exclamation mark symbol");

        GameVisualEffect[] all = FindObjectsByType<GameVisualEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            GameVisualEffect fx = all[i];
            if (fx == null || !fx.IsInteractableSymbolType)
                continue;

            if (fx.transform.parent == null)
                fx.gameObject.SetActive(false);
        }
    }

    private static void HideIfExists(string objectName)
    {
        GameObject go = GameObject.Find(objectName);
        if (go != null)
            go.SetActive(false);
    }

    private void SetSymbolVisible(bool visible)
    {
        if (interactSymbolInstance == null)
            EnsureInteractSymbol();

        if (interactSymbolInstance == null)
            return;

        if (visible && !interactSymbolInstance.gameObject.activeSelf)
        {
            interactSymbolInstance.transform.localPosition = Vector3.zero;
            interactSymbolInstance.ResetInteractableBobBase();
        }

        interactSymbolInstance.gameObject.SetActive(visible);
    }

    private void UpdateGrounded()
    {
        if (groundCheck == null)
        {
            IsGrounded = false;
            return;
        }

        IsGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayers);
    }

    private void UpdateRange()
    {
        PlayerController nearest = FindPlayerInInteractRadius();
        bool inRange = nearest != null && !nearest.IsDead;

        if (!inRange)
        {
            playerInRange = null;
            SetSymbolVisible(false);
            return;
        }

        playerInRange = nearest;
        bool showPrompt = (!DialogueBox.Instance || !DialogueBox.Instance.IsOpen) && !IsShopOpen;
        SetSymbolVisible(showPrompt);
    }

    private PlayerController FindPlayerInInteractRadius()
    {
        Vector2 center = interactRadius != null
            ? (Vector2)interactRadius.position
            : (Vector2)transform.position;

        float radius = GetInteractRadiusWorld();
        if (radius <= 0.01f)
            return null;

        PlayerController player = PlayerController.ResolveActive();
        if (player == null || player.IsDead)
            return null;

        float dist = Vector2.Distance(center, player.transform.position);
        return dist <= radius ? player : null;
    }

    private float GetInteractRadiusWorld()
    {
        if (interactCircle != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(interactCircle.transform.lossyScale.x),
                Mathf.Abs(interactCircle.transform.lossyScale.y));
            return interactCircle.radius * scale;
        }

        if (interactCollider != null)
            return Mathf.Max(interactCollider.bounds.extents.x, interactCollider.bounds.extents.y);

        return 0f;
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        TryBeginDialogue();
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        if (value.y >= stickUpThreshold)
        {
            if (!moveUpHeld)
            {
                moveUpHeld = true;
                TryBeginDialogue();
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

    private void TryBeginDialogue()
    {
        if (playerInRange == null || playerInRange.IsDead)
            return;

        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return;

        if (IsShopOpen || playerInRange.InputLocked)
            return;

        DialogueBox box = DialogueBox.Instance;
        if (box == null)
            box = FindFirstObjectByType<DialogueBox>();

        if (box == null)
        {
            Debug.LogWarning("[NPC] No DialogueBox found in the scene.");
            return;
        }

        SetSymbolVisible(false);
        FacePlayerForDialogue(playerInRange);
        box.Open(speakerName, ResolveDialoguePages(playerInRange), playerInRange, OnDialogueClosed);
    }

    private string[] ResolveDialoguePages(PlayerController player)
    {
        string id = player != null ? player.CharacterId : string.Empty;
        if (string.Equals(id, "Malice", System.StringComparison.OrdinalIgnoreCase) &&
            dialoguePagesMalice != null && dialoguePagesMalice.Length > 0)
            return dialoguePagesMalice;

        if (string.Equals(id, "Count", System.StringComparison.OrdinalIgnoreCase) &&
            dialoguePagesCount != null && dialoguePagesCount.Length > 0)
            return dialoguePagesCount;

        if (string.Equals(id, "Hex", System.StringComparison.OrdinalIgnoreCase) &&
            dialoguePagesHex != null && dialoguePagesHex.Length > 0)
            return dialoguePagesHex;

        bool harlieStyle = string.Equals(id, "Harlie", System.StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(id, "Hex", System.StringComparison.OrdinalIgnoreCase);
        if (harlieStyle && dialoguePagesHarlie != null && dialoguePagesHarlie.Length > 0)
            return dialoguePagesHarlie;

        // Kit and any unknown / unset character use the default pages.
        return dialoguePages;
    }

    private void OnDialogueClosed()
    {
        if (shopAfterDialogue != null && playerInRange != null && !playerInRange.IsDead)
        {
            shopAfterDialogue.Open(playerInRange, OnShopClosed);
            return;
        }

        OnShopClosed();
    }

    private void OnShopClosed()
    {
        RestoreNaturalFacing();
        if (playerInRange != null)
            SetSymbolVisible(true);
    }

    private void CacheNaturalFacing()
    {
        if (spriteRenderer == null)
            return;

        naturalFlipX = spriteRenderer.flipX;
        hasCachedNaturalFacing = true;
    }

    private void FacePlayerForDialogue(PlayerController player)
    {
        if (spriteRenderer == null || player == null)
            return;

        if (!hasCachedNaturalFacing)
            CacheNaturalFacing();

        float deltaX = player.transform.position.x - transform.position.x;
        if (Mathf.Abs(deltaX) < 0.01f)
            return;

        float facingSign = Mathf.Sign(deltaX);
        spriteRenderer.flipX = spriteFacesLeft ? facingSign > 0f : facingSign < 0f;
        facingPlayerForDialogue = true;
    }

    private void RestoreNaturalFacing()
    {
        if (!facingPlayerForDialogue || spriteRenderer == null || !hasCachedNaturalFacing)
        {
            facingPlayerForDialogue = false;
            return;
        }

        spriteRenderer.flipX = naturalFlipX;
        facingPlayerForDialogue = false;
    }

    private static Transform FindChildNamed(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        stickUpThreshold = Mathf.Clamp(stickUpThreshold, 0.1f, 1f);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);

        if (interactCollider != null)
            interactCollider.isTrigger = true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = interactRadius != null ? interactRadius.position : transform.position;
        float radius = 1f;
        if (interactCircle != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(interactCircle.transform.lossyScale.x),
                Mathf.Abs(interactCircle.transform.lossyScale.y));
            radius = interactCircle.radius * scale;
        }
        else if (interactCollider != null)
        {
            radius = Mathf.Max(interactCollider.bounds.extents.x, interactCollider.bounds.extents.y);
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        Gizmos.DrawWireSphere(center, radius);

        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
#endif
}
