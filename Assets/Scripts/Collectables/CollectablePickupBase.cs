using UnityEngine;

/// <summary>
/// Shared burst-out, hover, and player-touch collection for world pickups.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class CollectablePickupBase : MonoBehaviour
{
    private enum BurstPhase
    {
        Rising,
        Descending,
        Hover
    }

    [Header("Burst")]
    [SerializeField] private float burstSpeed = 5.5f;
    [SerializeField] private float burstRiseGravity = 16f;
    [Tooltip("Maximum upward travel from the spawn point (world units).")]
    [SerializeField] private float maxBurstRiseHeight = 2f;
    [SerializeField] private float descentGravity = 4f;
    [SerializeField] private float horizontalDriftDamping = 2.5f;
    [SerializeField] private float hoverHeightAboveGround = 0.5f;
    [SerializeField] private LayerMask groundLayers;

    [Header("Collection")]
    [SerializeField] private Collider2D collectCollider;

    private Vector2 velocity;
    private bool burstActive;
    private bool readyToCollect;
    private bool launchedFromSpawn;
    private float burstOriginY;
    private float hoverY;
    private float activeMaxBurstRiseHeight;
    private BurstPhase burstPhase = BurstPhase.Rising;

    protected virtual void Awake()
    {
        if (collectCollider == null)
            collectCollider = GetComponent<Collider2D>();

        if (collectCollider != null)
        {
            collectCollider.isTrigger = true;
            collectCollider.enabled = false;
        }

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Ground");
    }

    protected virtual void Start()
    {
        if (!launchedFromSpawn)
            SetupAsScenePlaced();
    }

    private void SetupAsScenePlaced()
    {
        burstActive = false;
        burstPhase = BurstPhase.Hover;
        hoverY = transform.position.y;
        velocity = Vector2.zero;
        EnableCollection();
    }

    public void LaunchBurst(Vector2 direction)
    {
        launchedFromSpawn = true;
        burstActive = true;
        burstPhase = BurstPhase.Rising;
        burstOriginY = transform.position.y;
        activeMaxBurstRiseHeight = 0f;
        velocity = direction.sqrMagnitude > 0.0001f ? direction.normalized * burstSpeed : Vector2.up * burstSpeed;
        EnableCollection();
    }

    public static void SpawnSingleBurst(CollectablePickupBase prefab, Vector3 center)
    {
        if (prefab == null)
            return;

        float angleDeg = Random.Range(25f, 155f);
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

        CollectablePickupBase instance = Instantiate(prefab, center, Quaternion.identity);
        instance.LaunchBurst(dir);
    }

    /// <summary>
    /// Straight upward pop (used by Count's Health Up clock), then slow descent to the ground.
    /// </summary>
    public static void SpawnVerticalPop(CollectablePickupBase prefab, Vector3 center, float riseHeight = 1f)
    {
        if (prefab == null)
            return;

        CollectablePickupBase instance = Instantiate(prefab, center, Quaternion.identity);
        instance.LaunchVerticalPop(riseHeight);
    }

    public void LaunchVerticalPop(float riseHeight)
    {
        launchedFromSpawn = true;
        burstActive = true;
        burstPhase = BurstPhase.Rising;
        burstOriginY = transform.position.y;
        activeMaxBurstRiseHeight = Mathf.Max(0.1f, riseHeight);
        velocity = Vector2.up * burstSpeed;
        EnableCollection();
    }

    private void EnableCollection()
    {
        readyToCollect = true;
        if (collectCollider != null)
            collectCollider.enabled = true;
    }

    private void Update()
    {
        if (burstActive)
            TickBurst(Time.deltaTime);
        else if (burstPhase == BurstPhase.Hover)
            MaintainHover();
    }

    private void TickBurst(float dt)
    {
        if (burstPhase == BurstPhase.Rising)
        {
            velocity.y -= burstRiseGravity * dt;
            transform.position += (Vector3)(velocity * dt);

            float peakY = burstOriginY + GetActiveMaxBurstRiseHeight();
            if (transform.position.y >= peakY)
            {
                transform.position = new Vector3(transform.position.x, peakY, transform.position.z);
                BeginDescent();
            }
            else if (velocity.y <= 0f)
            {
                BeginDescent();
            }

            return;
        }

        if (burstPhase == BurstPhase.Descending)
        {
            velocity.y -= descentGravity * dt;
            velocity.x = Mathf.MoveTowards(velocity.x, 0f, horizontalDriftDamping * dt);
            transform.position += (Vector3)(velocity * dt);

            if (!TryBeginHover())
                return;

            burstActive = false;
        }
    }

    private float GetActiveMaxBurstRiseHeight()
    {
        return activeMaxBurstRiseHeight > 0f ? activeMaxBurstRiseHeight : maxBurstRiseHeight;
    }

    private void BeginDescent()
    {
        burstPhase = BurstPhase.Descending;
        velocity = new Vector2(velocity.x * 0.35f, Mathf.Min(velocity.y, 0f));
    }

    private bool TryBeginHover()
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 20f, groundLayers);
        if (!hit.collider)
            return false;

        float targetY = hit.point.y + hoverHeightAboveGround;
        if (transform.position.y > targetY + 0.02f)
            return false;

        hoverY = targetY;
        burstPhase = BurstPhase.Hover;
        transform.position = new Vector3(transform.position.x, hoverY, transform.position.z);
        velocity = Vector2.zero;
        SoundManager.Instance?.PlayItemHitGround();
        return true;
    }

    private void MaintainHover()
    {
        transform.position = new Vector3(transform.position.x, hoverY, transform.position.z);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!readyToCollect)
            return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        if (player == null || player.IsDead)
            return;

        SoundManager.Instance?.PlayCollectItem();
        ApplyPickup(player);
        Destroy(gameObject);
    }

    protected abstract void ApplyPickup(PlayerController player);

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        burstSpeed = Mathf.Max(0.1f, burstSpeed);
        burstRiseGravity = Mathf.Max(0.1f, burstRiseGravity);
        maxBurstRiseHeight = Mathf.Max(0.1f, maxBurstRiseHeight);
        descentGravity = Mathf.Max(0.1f, descentGravity);
        horizontalDriftDamping = Mathf.Max(0.1f, horizontalDriftDamping);
        hoverHeightAboveGround = Mathf.Max(0.05f, hoverHeightAboveGround);

        if (collectCollider == null)
            collectCollider = GetComponent<Collider2D>();
    }
#endif
}
