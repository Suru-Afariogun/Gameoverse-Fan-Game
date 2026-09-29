using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slim factory platform: standard Unity one-way PlatformEffector2D (land from above,
/// jump through from below). Down+Jump briefly ignores collision to drop through.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class SlimFactoryPlatform : MonoBehaviour
{
    [SerializeField] private float dropThroughSeconds = 0.45f;
    [SerializeField] [Range(1f, 180f)] private float surfaceArc = 170f;

    private static readonly List<SlimFactoryPlatform> Active = new List<SlimFactoryPlatform>(16);

    private Collider2D platformCollider;
    private PlatformEffector2D effector;
    private readonly Dictionary<int, float> dropThroughUntilByPlayerId = new Dictionary<int, float>();
    private readonly List<PlayerController> playerScratch = new List<PlayerController>(2);

    public Collider2D PlatformCollider => platformCollider;

    public static bool IsDroppingThrough(PlayerController player)
    {
        if (player == null)
            return false;

        int id = player.GetInstanceID();
        for (int i = 0; i < Active.Count; i++)
        {
            SlimFactoryPlatform platform = Active[i];
            if (platform == null)
                continue;
            // Presence means still dropping (timer may be extended until feet clear in Up-hyper).
            if (platform.dropThroughUntilByPlayerId.ContainsKey(id))
                return true;
        }

        return false;
    }

    private void Awake()
    {
        platformCollider = GetComponent<Collider2D>();
        if (platformCollider != null)
        {
            platformCollider.isTrigger = false;
            platformCollider.usedByEffector = true;
        }

        effector = GetComponent<PlatformEffector2D>();
        if (effector == null)
            effector = gameObject.AddComponent<PlatformEffector2D>();

        ConfigureEffector();
    }

    private void ConfigureEffector()
    {
        if (effector == null)
            return;

        effector.enabled = true;
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.surfaceArc = Mathf.Clamp(surfaceArc, 1f, 180f);
        effector.useSideFriction = false;
        effector.useSideBounce = false;
        effector.rotationalOffset = 0f;
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        ClearAllDropIgnores();
        dropThroughUntilByPlayerId.Clear();
    }

    private void FixedUpdate()
    {
        if (platformCollider == null || dropThroughUntilByPlayerId.Count == 0)
            return;

        float now = Time.time;
        PlayerController.CollectActivePlayers(playerScratch);
        List<int> expired = null;

        foreach (KeyValuePair<int, float> pair in dropThroughUntilByPlayerId)
        {
            PlayerController owner = null;
            for (int p = 0; p < playerScratch.Count; p++)
            {
                if (playerScratch[p] != null && playerScratch[p].GetInstanceID() == pair.Key)
                {
                    owner = playerScratch[p];
                    break;
                }
            }

            // Up-hyper Count falls slowly — keep ignoring until feet clear below the platform,
            // even after the base drop-through timer.
            if (owner != null
                && PassablePlatformPhasing.CountUpHyperIgnoresSlimGrace()
                && PassablePlatformPhasing.TryGetBodyBottom(owner, out float feetY, out _))
            {
                float clearBelow = platformCollider.bounds.min.y - 0.12f;
                if (feetY > clearBelow)
                    continue;
            }

            if (now < pair.Value)
                continue;

            if (expired == null)
                expired = new List<int>(4);
            expired.Add(pair.Key);
        }

        if (expired == null)
            return;

        for (int i = 0; i < expired.Count; i++)
        {
            int id = expired[i];
            dropThroughUntilByPlayerId.Remove(id);

            for (int p = 0; p < playerScratch.Count; p++)
            {
                if (playerScratch[p] != null && playerScratch[p].GetInstanceID() == id)
                {
                    PassablePlatformPhasing.SetIgnoredAgainstPlayer(platformCollider, playerScratch[p], false);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Down + Jump while standing on this slim → fall through briefly.
    /// Uses body↔platform horizontal overlap so edges of long platforms work, not only the center.
    /// </summary>
    public static bool TryDropThrough(PlayerController player)
    {
        if (player == null || player.IsDead)
            return false;

        if (!PassablePlatformPhasing.TryGetBodyBottom(player, out float feetY, out Collider2D body))
            return false;

        if (!PassablePlatformPhasing.TryGetGroundCheck(player, out Vector2 probe, out float radius))
            return false;

        Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
        float bodyMinX = body != null ? body.bounds.min.x : probe.x - radius;
        float bodyMaxX = body != null ? body.bounds.max.x : probe.x + radius;

        for (int i = 0; i < Active.Count; i++)
        {
            SlimFactoryPlatform platform = Active[i];
            if (platform == null || platform.platformCollider == null)
                continue;

            Bounds b = platform.platformCollider.bounds;

            // Any horizontal overlap between the player body and the platform surface.
            if (bodyMaxX < b.min.x || bodyMinX > b.max.x)
                continue;

            float top = b.max.y;
            // Standing on / near the top face (tolerant for thin one-way platforms).
            if (feetY < top - 0.45f || feetY > top + 0.35f)
                continue;
            if (probe.y + radius < top - 0.35f)
                continue;

            int id = player.GetInstanceID();
            float dropSeconds = Mathf.Max(0.05f, platform.dropThroughSeconds);
            // Up-hyper Count needs a longer ignore window while he eases through the slab.
            if (PassablePlatformPhasing.CountUpHyperIgnoresSlimGrace())
                dropSeconds = Mathf.Max(dropSeconds, 1.35f);

            platform.dropThroughUntilByPlayerId[id] = Time.time + dropSeconds;
            PassablePlatformPhasing.SetIgnoredAgainstPlayer(platform.platformCollider, player, true);

            if (playerRb != null)
                playerRb.linearVelocity = new Vector2(
                    playerRb.linearVelocity.x,
                    Mathf.Min(playerRb.linearVelocity.y, -1.5f));

            return true;
        }

        return false;
    }

    private void ClearAllDropIgnores()
    {
        if (platformCollider == null)
            return;

        PlayerController.CollectActivePlayers(playerScratch);
        for (int i = 0; i < playerScratch.Count; i++)
        {
            if (playerScratch[i] != null)
                PassablePlatformPhasing.SetIgnoredAgainstPlayer(platformCollider, playerScratch[i], false);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        dropThroughSeconds = Mathf.Max(0.05f, dropThroughSeconds);
        surfaceArc = Mathf.Clamp(surfaceArc, 1f, 180f);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = false;
            col.usedByEffector = true;
        }

        PlatformEffector2D pe = GetComponent<PlatformEffector2D>();
        if (pe != null)
        {
            pe.useOneWay = true;
            pe.surfaceArc = surfaceArc;
        }
    }
#endif
}
