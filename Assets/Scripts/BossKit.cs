using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss version of Kit. Uses the same Animator parameters/triggers as Kit.controller.
/// PLACEHOLDER chase + blaster pattern — ask before replacing with final boss AI.
/// </summary>
public class BossKit : Boss
{
    private enum PlaceholderState
    {
        Reposition,
        ShootBurst,
        Recover
    }

    [Header("Boss Kit - Placeholder AI (temporary)")]
    [Tooltip("Remove / replace when final Boss Kit patterns are ready.")]
    [SerializeField] private bool usePlaceholderAi = true;

    [Header("Spacing")]
    [SerializeField] private float preferredDistance = 5.5f;
    [SerializeField] private float distanceTolerance = 1.1f;
    [SerializeField] private float maxChaseDistance = 10f;

    [Header("Shooting")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Projectile smallProjectilePrefab;
    [SerializeField] private Projectile mediumProjectilePrefab;
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float timeBetweenShots = 0.22f;
    [SerializeField] private float shootAnimDuration = 0.28f;
    [SerializeField] private float recoverDuration = 0.7f;
    [SerializeField] private float shootRange = 9f;
    [Tooltip("Every Nth shot in a burst uses the medium projectile (0 = never).")]
    [SerializeField] private int mediumShotEvery = 3;
    [SerializeField] private float aimUpThreshold = 0.45f;

    [Header("Boss Kit - VFX Prefabs")]
    [Tooltip("Small Buster Blast prefab for small shots.")]
    [SerializeField] private VisualEffect busterBlastSmallPrefab;
    [Tooltip("Medium + big Buster Blast prefab.")]
    [FormerlySerializedAs("busterBlastMediumPrefab")]
    [SerializeField] private VisualEffect busterBlastMediumBigPrefab;

    private PlaceholderState state = PlaceholderState.Reposition;
    private float stateTimer;
    private int shotsLeft;
    private float nextShotTime;
    private float shootAnimTimer;
    private bool shootAnimActive;

    protected override void Awake()
    {
        SetBossId("BossKit");
        base.Awake();

        if (firePoint == null)
        {
            Transform found = transform.Find("FirePoint");
            if (found != null)
                firePoint = found;
        }
    }

    protected override void OnHitStunStarted()
    {
        CancelAttackImmediate();
        state = PlaceholderState.Reposition;
        stateTimer = 0f;
        base.OnHitStunStarted();
    }

    protected override void HandleBossUpdate()
    {
        if (!usePlaceholderAi)
            return;

        if (shootAnimTimer > 0f)
        {
            shootAnimTimer -= Time.deltaTime;
            if (shootAnimTimer <= 0f)
            {
                shootAnimTimer = 0f;
                shootAnimActive = false;
            }
        }

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            StopHorizontal();
            CancelAttackImmediate();
            state = PlaceholderState.Reposition;
            return;
        }

        FaceToward(player.transform);
        UpdateAimFlag(player);

        switch (state)
        {
            case PlaceholderState.Reposition:
                TickReposition(player);
                break;
            case PlaceholderState.ShootBurst:
                TickShootBurst(player);
                break;
            case PlaceholderState.Recover:
                TickRecover();
                break;
        }
    }

    protected override void HandleBossFixedUpdate()
    {
        if (!usePlaceholderAi)
            return;

        if (state != PlaceholderState.Reposition || isAttacking)
        {
            StopHorizontal();
            return;
        }

        PlayerController player = FindPlayer();
        if (player == null)
        {
            StopHorizontal();
            return;
        }

        float dx = player.transform.position.x - transform.position.x;
        float dist = Mathf.Abs(dx);
        float minDist = preferredDistance - distanceTolerance;
        float maxDist = preferredDistance + distanceTolerance;

        if (dist > maxChaseDistance)
            MoveHorizontal(Mathf.Sign(dx));
        else if (dist > maxDist)
            MoveHorizontal(Mathf.Sign(dx));
        else if (dist < minDist)
            MoveHorizontal(-Mathf.Sign(dx));
        else
            StopHorizontal();
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        animator.SetBool("IsShooting", shootAnimActive);
        animator.SetBool("IsAttacking", false);
    }

    private void TickReposition(PlayerController player)
    {
        float dist = Mathf.Abs(player.transform.position.x - transform.position.x);
        bool inBand = dist >= preferredDistance - distanceTolerance &&
                      dist <= preferredDistance + distanceTolerance;

        if ((inBand || dist <= shootRange) && isGrounded)
            BeginShootBurst();
    }

    private void BeginShootBurst()
    {
        StopHorizontal();
        state = PlaceholderState.ShootBurst;
        shotsLeft = Mathf.Max(1, shotsPerBurst);
        nextShotTime = 0f;
        isAttacking = true;
    }

    private void TickShootBurst(PlayerController player)
    {
        StopHorizontal();

        if (Time.time < nextShotTime)
            return;

        if (shotsLeft <= 0)
        {
            isAttacking = false;
            state = PlaceholderState.Recover;
            stateTimer = recoverDuration;
            return;
        }

        FireAt(player);
        shotsLeft--;
        nextShotTime = Time.time + Mathf.Max(0.05f, timeBetweenShots);

        if (shotsLeft <= 0)
        {
            isAttacking = false;
            state = PlaceholderState.Recover;
            stateTimer = recoverDuration;
        }
    }

    private void TickRecover()
    {
        StopHorizontal();
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f)
            return;

        state = PlaceholderState.Reposition;
    }

    private void FireAt(PlayerController player)
    {
        if (player == null)
            return;

        int shotIndex = shotsPerBurst - shotsLeft;
        bool useMedium = mediumShotEvery > 0 &&
                         mediumProjectilePrefab != null &&
                         (shotIndex + 1) % mediumShotEvery == 0;

        Projectile prefab = useMedium ? mediumProjectilePrefab : smallProjectilePrefab;
        if (prefab == null)
            prefab = smallProjectilePrefab;
        if (prefab == null)
            return;

        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)transform.position + new Vector2(facingSign * 0.6f, 0.2f);

        Vector2 toPlayer = (Vector2)player.transform.position - origin;
        Vector2 dir;
        if (Mathf.Abs(toPlayer.y) >= aimUpThreshold && toPlayer.y > 0f)
            dir = new Vector2(Mathf.Sign(facingSign) * 0.35f, 1f).normalized;
        else
            dir = new Vector2(facingSign, 0f);

        if (dir.sqrMagnitude < 0.001f)
            dir = new Vector2(facingSign, 0f);

        Projectile shot = Instantiate(prefab, origin, Quaternion.identity);
        shot.Launch(dir, transform);

        Transform muzzle = firePoint != null ? firePoint : transform;
        VisualEffect blastPrefab = VisualEffects.ResolveBusterBlastPrefab(
            shot.ShotType,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
        VisualEffects.SpawnBusterBlast(blastPrefab, muzzle, shot);

        shootAnimActive = true;
        shootAnimTimer = shootAnimDuration;
        if (animator != null)
        {
            if (aimUp)
                animator.SetTrigger("ShootUp");
            else
                animator.SetTrigger("Shoot");
        }
    }

    private void UpdateAimFlag(PlayerController player)
    {
        if (player == null)
        {
            aimUp = false;
            return;
        }

        float dy = player.transform.position.y - transform.position.y;
        aimUp = dy >= aimUpThreshold;
    }

    private void CancelAttackImmediate()
    {
        isAttacking = false;
        shotsLeft = 0;
        shootAnimActive = false;
        shootAnimTimer = 0f;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        preferredDistance = Mathf.Max(1f, preferredDistance);
        distanceTolerance = Mathf.Max(0.1f, distanceTolerance);
        maxChaseDistance = Mathf.Max(preferredDistance, maxChaseDistance);
        shotsPerBurst = Mathf.Max(1, shotsPerBurst);
        timeBetweenShots = Mathf.Max(0.05f, timeBetweenShots);
        shootAnimDuration = Mathf.Max(0.05f, shootAnimDuration);
        recoverDuration = Mathf.Max(0.05f, recoverDuration);
        shootRange = Mathf.Max(1f, shootRange);
        mediumShotEvery = Mathf.Max(0, mediumShotEvery);
        aimUpThreshold = Mathf.Max(0.05f, aimUpThreshold);
    }
#endif
}
