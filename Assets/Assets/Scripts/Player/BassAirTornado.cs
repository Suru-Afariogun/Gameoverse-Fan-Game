using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bubble Air tornado — sits inside a Bubble Bullet until pop, then flies 45° upward
/// (away from Bass facing). Catches regular enemies (phasable), lifts them 8 spaces, then kills.
/// Bosses are only nudged 4 spaces and take light damage (no grab).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BassAirTornado : MonoBehaviour
{
    private struct CapturedEnemy
    {
        public Transform Transform;
        public ICommonEnemy Enemy;
        public Rigidbody2D Body;
        public Collider2D[] Colliders;
        public bool WasKinematic;
        public bool WasSimulated;
        public float StartY;
        public Vector3 LocalOffset;
        public Behaviour[] DisabledBehaviours;
    }

    [SerializeField] private float spaceSize = 1f;
    [SerializeField] private float flySpeed = 7.5f;
    [SerializeField] private float carryUpSpaces = 8f;
    [SerializeField] private float bossPushSpaces = 4f;
    [SerializeField] private float bossPushSeconds = 0.22f;
    [SerializeField] private int bossDamage = 2;
    [SerializeField] private float fadeOutSeconds = 0.35f;
    [SerializeField] private float maxFlightSeconds = 4.5f;
    [SerializeField] private float catchRadius = 0.85f;

    private readonly List<CapturedEnemy> captured = new List<CapturedEnemy>(8);
    private readonly HashSet<int> bossHitIds = new HashSet<int>();
    private readonly HashSet<int> capturedIds = new HashSet<int>();
    private readonly List<Collider2D> overlapBuffer = new List<Collider2D>(24);
    private readonly List<Collider2D> ignoredWorld = new List<Collider2D>(64);

    private Collider2D hitCollider;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Transform owner;
    private bool hostileToPlayer;
    private float facingSign = 1f;
    private Vector2 flyDirection;
    private bool released;
    private bool fading;
    private float flightTimer;
    private float fadeTimer;
    private float startAlpha = 1f;

    public bool IsReleased => released;
    public bool IsAlive => !fading || fadeTimer < fadeOutSeconds;

    /// <summary>Remove tornados owned by this root (boss defeat cinema).</summary>
    public static void DestroyOwnedBy(Transform root)
    {
        if (root == null)
            return;

        BassAirTornado[] tornados = FindObjectsByType<BassAirTornado>(FindObjectsSortMode.None);
        for (int i = 0; i < tornados.Length; i++)
        {
            BassAirTornado tornado = tornados[i];
            if (tornado == null || tornado.owner == null)
                continue;
            if (tornado.owner == root || tornado.owner.IsChildOf(root) || root.IsChildOf(tornado.owner))
                Destroy(tornado.gameObject);
        }
    }

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponent<Animator>();

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }
    }

    /// <summary>Parent under the bubble's tornado container (visible, full size).</summary>
    public void ContainInBubble(Transform container, float ownerFacingSign = 1f)
    {
        released = false;
        fading = false;
        transform.SetParent(container, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        ApplyFacing(ownerFacingSign);

        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    /// <summary>Detach from the bubble and fly 45° up away from Bass facing.</summary>
    public void Release(Transform shotOwner, float ownerFacingSign, float worldSpaceSize)
    {
        owner = shotOwner;
        // Boss Bass tornado: no enemy catching, it pushes + hurts the player instead of bosses.
        hostileToPlayer = BassBossHostility.IsHostile(shotOwner);
        spaceSize = Mathf.Max(0.01f, worldSpaceSize);
        ApplyFacing(ownerFacingSign);
        flyDirection = new Vector2(facingSign, 1f).normalized;

        transform.SetParent(null, true);
        released = true;
        fading = false;
        flightTimer = 0f;
        fadeTimer = 0f;

        if (spriteRenderer != null)
            startAlpha = spriteRenderer.color.a;

        if (hitCollider != null)
            hitCollider.enabled = true;

        ApplyWorldPhasing(true);
    }

    /// <summary>Art faces right; flipX when Bass fires left.</summary>
    public void ApplyFacing(float ownerFacingSign)
    {
        facingSign = ownerFacingSign >= 0f ? 1f : -1f;
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer != null)
            spriteRenderer.flipX = facingSign < 0f;
    }

    private void Update()
    {
        if (!released || fading)
        {
            if (fading)
                TickFade();
            return;
        }

        float dt = Time.deltaTime;
        flightTimer += dt;
        transform.position += (Vector3)(flyDirection * flySpeed * dt);

        TickCaptures();
        TryCatchNewTargets();
        TryHitBosses();

        if (flightTimer >= maxFlightSeconds && captured.Count == 0)
            BeginFade();
    }

    private void TickCaptures()
    {
        float killHeight = carryUpSpaces * spaceSize;
        for (int i = captured.Count - 1; i >= 0; i--)
        {
            CapturedEnemy entry = captured[i];
            if (entry.Transform == null || entry.Enemy == null || entry.Enemy.IsDead)
            {
                ReleaseCaptureSlot(i, kill: false);
                continue;
            }

            entry.Transform.position = transform.position + entry.LocalOffset;
            float lifted = entry.Transform.position.y - entry.StartY;
            if (lifted >= killHeight)
            {
                KillEnemy(entry.Enemy);
                ReleaseCaptureSlot(i, kill: true);
            }
        }

        if (captured.Count == 0 && flightTimer > 0.35f && !HasNearbyCatchable())
        {
            // Stay briefly after last kill so the rise reads, then fade.
            if (flightTimer >= Mathf.Min(maxFlightSeconds, 1.25f))
                BeginFade();
        }
    }

    private bool HasNearbyCatchable()
    {
        overlapBuffer.Clear();
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        Physics2D.OverlapCircle(transform.position, catchRadius, filter, overlapBuffer);
        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            if (TryResolveCommonEnemy(overlapBuffer[i], out ICommonEnemy enemy, out _)
                && enemy != null
                && !enemy.IsDead
                && !capturedIds.Contains(((Component)enemy).GetInstanceID()))
            {
                return true;
            }
        }

        return false;
    }

    private void TryCatchNewTargets()
    {
        if (hostileToPlayer)
            return;

        overlapBuffer.Clear();
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        Physics2D.OverlapCircle(transform.position, catchRadius, filter, overlapBuffer);

        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || EnemyDetectionZone.IsDetectionOnlyCollider(col))
                continue;
            if (col.GetComponent<AttackHitbox>() != null)
                continue;

            if (!TryResolveCommonEnemy(col, out ICommonEnemy enemy, out Component host) || enemy == null || enemy.IsDead)
                continue;

            int id = host.GetInstanceID();
            if (!capturedIds.Add(id))
                continue;

            CaptureEnemy(enemy, host);
        }
    }

    private void TryHitBosses()
    {
        overlapBuffer.Clear();
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        Physics2D.OverlapCircle(transform.position, catchRadius, filter, overlapBuffer);

        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null)
                continue;

            if (hostileToPlayer)
            {
                PlayerController hitPlayer = col.GetComponentInParent<PlayerController>();
                if (hitPlayer != null && !hitPlayer.IsDead && bossHitIds.Add(hitPlayer.GetInstanceID()))
                    BassBossHostility.TryHurtPlayer(col, bossDamage, transform);
                continue;
            }

            Boss boss = col.GetComponentInParent<Boss>();
            if (boss == null || boss.IsDead)
                continue;

            int id = boss.GetInstanceID();
            if (!bossHitIds.Add(id))
                continue;

            int applied = FractionalDamage.Consume(boss, bossDamage);
            if (applied > 0)
                boss.TakeDamage(applied, ProjectileShotType.Small);

            GentleKnockback.Apply(boss, facingSign, bossPushSpaces * spaceSize, bossPushSeconds);
        }
    }

    private void CaptureEnemy(ICommonEnemy enemy, Component host)
    {
        Transform t = host.transform;
        Rigidbody2D rb = host.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = host.GetComponentInParent<Rigidbody2D>();

        Collider2D[] cols = host.GetComponentsInChildren<Collider2D>(true);
        Behaviour[] disabled = DisableEnemyDrivers(host.gameObject);

        CapturedEnemy entry = new CapturedEnemy
        {
            Transform = t,
            Enemy = enemy,
            Body = rb,
            Colliders = cols,
            WasKinematic = rb != null && rb.bodyType == RigidbodyType2D.Kinematic,
            WasSimulated = rb == null || rb.simulated,
            StartY = t.position.y,
            LocalOffset = t.position - transform.position,
            DisabledBehaviours = disabled
        };

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
        }

        ApplyEnemyPhasing(entry, true);
        captured.Add(entry);
    }

    private static Behaviour[] DisableEnemyDrivers(GameObject host)
    {
        MonoBehaviour[] all = host.GetComponents<MonoBehaviour>();
        var disabled = new List<Behaviour>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            MonoBehaviour b = all[i];
            if (b == null || !b.enabled)
                continue;
            if (b is GentleKnockback)
                continue;

            // Pause enemy AI / movement while carried; keep Animator elsewhere in children.
            b.enabled = false;
            disabled.Add(b);
        }

        return disabled.ToArray();
    }

    private void ApplyEnemyPhasing(CapturedEnemy entry, bool ignore)
    {
        if (entry.Colliders == null)
            return;

        // Phase vs players.
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int p = 0; p < players.Length; p++)
        {
            if (players[p] == null)
                continue;
            Collider2D[] playerCols = players[p].GetComponentsInChildren<Collider2D>(true);
            IgnoreSolidPairs(entry.Colliders, playerCols, ignore);
        }

        // Phase vs world solids currently overlapping / nearby.
        overlapBuffer.Clear();
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.NoFilter();
        Physics2D.OverlapCircle(transform.position, 6f, filter, overlapBuffer);
        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            Collider2D world = overlapBuffer[i];
            if (world == null || world.isTrigger)
                continue;
            if (world.transform.IsChildOf(entry.Transform))
                continue;

            for (int c = 0; c < entry.Colliders.Length; c++)
            {
                Collider2D a = entry.Colliders[c];
                if (a == null || a.isTrigger)
                    continue;
                Physics2D.IgnoreCollision(a, world, ignore);
                if (ignore)
                    ignoredWorld.Add(world);
            }
        }

        // Phase vs other enemies' solids.
        for (int i = 0; i < captured.Count; i++)
        {
            if (captured[i].Transform == entry.Transform)
                continue;
            IgnoreSolidPairs(entry.Colliders, captured[i].Colliders, ignore);
        }
    }

    private void ApplyWorldPhasing(bool ignore)
    {
        if (hitCollider == null)
            return;

        // Tornado trigger still detects; ignore solid blocking if any non-trigger sneaks in.
        Collider2D[] solids = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        for (int i = 0; i < solids.Length; i++)
        {
            Collider2D c = solids[i];
            if (c == null || c.isTrigger || c == hitCollider)
                continue;
            if (c.transform.IsChildOf(transform))
                continue;
            Physics2D.IgnoreCollision(hitCollider, c, ignore);
        }
    }

    private static void IgnoreSolidPairs(Collider2D[] aCols, Collider2D[] bCols, bool ignore)
    {
        if (aCols == null || bCols == null)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null || a.isTrigger)
                continue;
            for (int j = 0; j < bCols.Length; j++)
            {
                Collider2D b = bCols[j];
                if (b == null || b.isTrigger)
                    continue;
                Physics2D.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private void ReleaseCaptureSlot(int index, bool kill)
    {
        if (index < 0 || index >= captured.Count)
            return;

        CapturedEnemy entry = captured[index];
        captured.RemoveAt(index);

        if (entry.Transform != null)
        {
            int id = entry.Enemy is Component c ? c.GetInstanceID() : entry.Transform.GetInstanceID();
            capturedIds.Remove(id);
            ApplyEnemyPhasing(entry, false);

            if (entry.DisabledBehaviours != null && !kill)
            {
                for (int i = 0; i < entry.DisabledBehaviours.Length; i++)
                {
                    if (entry.DisabledBehaviours[i] != null)
                        entry.DisabledBehaviours[i].enabled = true;
                }
            }

            if (entry.Body != null && !kill)
            {
                entry.Body.bodyType = entry.WasKinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
                entry.Body.simulated = entry.WasSimulated;
            }
        }
    }

    private static void KillEnemy(ICommonEnemy enemy)
    {
        if (enemy == null || enemy.IsDead)
            return;

        if (enemy is IForceKillable killable)
            killable.ForceKill();
        else
            enemy.TakeDamage(Mathf.Max(1, enemy.CurrentHealth) + 999);
    }

    private void BeginFade()
    {
        if (fading)
            return;

        fading = true;
        fadeTimer = 0f;
        if (spriteRenderer != null)
            startAlpha = spriteRenderer.color.a;

        // Finish any remaining captures immediately.
        for (int i = captured.Count - 1; i >= 0; i--)
        {
            if (captured[i].Enemy != null && !captured[i].Enemy.IsDead)
                KillEnemy(captured[i].Enemy);
            ReleaseCaptureSlot(i, kill: true);
        }

        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    private void TickFade()
    {
        fadeTimer += Time.deltaTime;
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

    private static bool TryResolveCommonEnemy(Collider2D other, out ICommonEnemy enemy, out Component host)
    {
        enemy = null;
        host = null;
        if (other == null)
            return false;

        // Bosses are not capturable.
        if (other.GetComponentInParent<Boss>() != null)
            return false;

        enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy == null)
            return false;

        host = enemy as Component;
        return host != null;
    }

    private void OnDestroy()
    {
        for (int i = captured.Count - 1; i >= 0; i--)
            ReleaseCaptureSlot(i, kill: false);
        ApplyWorldPhasing(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        spaceSize = Mathf.Max(0.01f, spaceSize);
        flySpeed = Mathf.Max(0.5f, flySpeed);
        carryUpSpaces = Mathf.Max(0.5f, carryUpSpaces);
        bossPushSpaces = Mathf.Max(0f, bossPushSpaces);
        bossDamage = Mathf.Max(0, bossDamage);
        fadeOutSeconds = Mathf.Max(0.05f, fadeOutSeconds);
        maxFlightSeconds = Mathf.Max(0.5f, maxFlightSeconds);
        catchRadius = Mathf.Max(0.1f, catchRadius);
    }
#endif
}
