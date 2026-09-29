using UnityEngine;

/// <summary>
/// Shared solid/phase rules for slim + moving factory platforms (Kirby-style):
/// solid only when landing on / standing on the top face — never while ascending through
/// or overlapping the underside / interior.
/// Grace lip catches use the top 30% of the GroundCheck (not just the tip) and a small soft lift.
/// Moving platforms use a quicker solidify path plus trap rescue (soft push / snap above).
/// </summary>
public static class PassablePlatformPhasing
{
    /// <summary>How far below the top the feet may be and still count as a lip land.</summary>
    public const float LandWindow = 0.4f;

    /// <summary>Max snap-up distance when catching the lip while falling (keep subtle).</summary>
    public const float SoftLiftMax = 0.12f;

    public const float SoftLiftSkin = 0.012f;

    /// <summary>
    /// Fraction of the GroundCheck radius measured down from the tip that must reach the surface
    /// for a grace catch (0.3 = top 30% of the check, not only the tip).
    /// </summary>
    public const float GroundCheckGraceTopFraction = 0.3f;

    /// <summary>Moving platforms solidify sooner (deeper feet still count as a land).</summary>
    public const float MovingLandWindow = 0.58f;

    /// <summary>Subtle push-up when lightly trapped inside a mover after solidify.</summary>
    public const float MovingSoftLiftMax = 0.28f;

    /// <summary>Hard snap-to-top when deeply embedded in a mover (trap rescue).</summary>
    public const float MovingSnapLiftMax = 1.15f;

    /// <summary>GroundCheck may sit this far past the left/right edge and still solidify.</summary>
    public const float MovingSideMargin = 0.45f;

    /// <summary>Extra X slop around platform center for "almost same X" boarding.</summary>
    public const float MovingSameXSlop = 0.35f;

    /// <summary>
    /// Count Up-hyper falls so slowly that slim-platform soft-lift / grace can bounce him
    /// back up mid drop-through. Skip those catches only in that mode.
    /// </summary>
    public static bool CountUpHyperIgnoresSlimGrace()
    {
        return HyperSpeedWorldSlow.IsActive && HyperSpeedWorldSlow.SlowsPlayer;
    }

    public static bool TryGetGroundCheck(PlayerController player, out Vector2 center, out float radius)
    {
        center = default;
        radius = 0.12f;
        if (player == null)
            return false;

        radius = Mathf.Max(0.01f, player.GroundCheckRadius);
        Transform gc = player.GroundCheckTransform;
        if (gc != null)
        {
            center = gc.position;
            return true;
        }

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        center = rb != null ? rb.position : (Vector2)player.transform.position;
        return true;
    }

    /// <summary>
    /// Y of the grace probe: tip minus (fraction × radius), so the top N% of the GroundCheck
    /// must clear the platform top — not a single-pixel tip graze.
    /// </summary>
    public static float GetGroundCheckGraceProbeY(Vector2 center, float radius)
    {
        float r = Mathf.Max(0.01f, radius);
        float fromTip = Mathf.Clamp01(GroundCheckGraceTopFraction) * r;
        return center.y + r - fromTip;
    }

    public static bool TryGetBodyBottom(PlayerController player, out float feetY, out Collider2D body)
    {
        feetY = 0f;
        body = null;
        if (player == null)
            return false;

        body = player.GetComponent<Collider2D>();
        if (body == null)
            body = player.GetComponentInChildren<Collider2D>();

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            feetY = body.bounds.min.y;
            return true;
        }

        if (rb != null)
        {
            feetY = rb.position.y - 0.5f;
            return true;
        }

        feetY = player.transform.position.y;
        return true;
    }

    /// <summary>
    /// Solid only for a real top landing/stand. Ascending through, deep interior overlap,
    /// or standing on a higher surface above this platform stays phasable.
    /// </summary>
    public static bool ShouldBeSolid(PlayerController player, Collider2D platform)
    {
        if (player == null || platform == null)
            return false;

        if (!TryGetGroundCheck(player, out Vector2 center, out float radius))
            return false;

        Bounds plat = platform.bounds;
        if (center.x < plat.min.x - radius || center.x > plat.max.x + radius)
            return false;

        if (!TryGetBodyBottom(player, out float feetY, out _))
            return false;

        float top = plat.max.y;
        float graceY = GetGroundCheckGraceProbeY(center, radius);

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        float vy = rb != null ? rb.linearVelocity.y : 0f;

        // Jumping up through: stay phasable until the feet are already on/above the top.
        if (vy > 0.08f && feetY < top - 0.04f)
            return false;

        // Standing on something clearly higher (e.g. slim above a mover).
        if (feetY > top + 0.25f)
            return false;

        // Deep inside the volume — keep phasing.
        if (feetY < top - LandWindow)
            return false;

        // Grace / land: top 30% of GroundCheck at the surface (not tip-only), or feet already in band.
        // Up-hyper Count: no soft-lift grace — must clearly clear the lip (drop-through friendly).
        bool graceReachedTop = graceY >= top - 0.02f;
        bool feetInLandBand = feetY <= top + 0.2f && feetY >= top - LandWindow;
        if (CountUpHyperIgnoresSlimGrace())
        {
            if (!graceReachedTop)
                return false;
        }
        else if (!graceReachedTop && !(vy <= 0.08f && feetInLandBand && graceY >= top - SoftLiftMax))
        {
            return false;
        }

        return feetInLandBand;
    }

    /// <summary>
    /// Quicker solidify for moving platforms: wider land window, tip-friendly grace,
    /// and GroundCheck may be above, beside, or nearly same-X as the platform.
    /// </summary>
    public static bool ShouldBeSolidMoving(PlayerController player, Collider2D platform)
    {
        if (player == null || platform == null)
            return false;

        if (!TryGetGroundCheck(player, out Vector2 center, out float radius))
            return false;

        Bounds plat = platform.bounds;
        if (!IsMovingHorizontalEligible(center, radius, plat))
            return false;

        if (!TryGetBodyBottom(player, out float feetY, out _))
            return false;

        float top = plat.max.y;
        float tipY = center.y + radius;
        float graceY = GetGroundCheckGraceProbeY(center, radius);

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        float vy = rb != null ? rb.linearVelocity.y : 0f;

        // Jumping hard up through from well below: stay phasable.
        if (vy > 0.12f && feetY < top - 0.1f && tipY < top - 0.02f)
            return false;

        if (feetY > top + 0.25f)
            return false;

        bool sideOrSameX = IsMovingSideOrSameX(center, radius, plat);
        bool feetInLandBand = feetY <= top + 0.28f && feetY >= top - MovingLandWindow;

        // Side / same-X board: GroundCheck next to or aligned with the platform body.
        if (sideOrSameX &&
            center.y >= plat.min.y - radius &&
            center.y <= top + radius * 1.25f &&
            vy <= 0.2f &&
            feetY < top + 0.28f &&
            feetY >= plat.min.y - 0.05f)
            return true;

        if (!feetInLandBand)
            return false;

        // Quicker than slim: tip near top, or relaxed top-30% grace while falling/standing.
        bool tipNearTop = tipY >= top - 0.1f;
        bool graceNearTop = graceY >= top - 0.06f;
        if (tipNearTop || graceNearTop)
            return true;

        return vy <= 0.1f && graceY >= top - MovingSoftLiftMax;
    }

    /// <summary>
    /// GroundCheck is over the top, beside either edge, or nearly on the platform's X axis.
    /// </summary>
    public static bool IsMovingHorizontalEligible(Vector2 groundCheckCenter, float radius, Bounds plat)
    {
        float r = Mathf.Max(0.01f, radius);
        float minX = plat.min.x - r - MovingSideMargin;
        float maxX = plat.max.x + r + MovingSideMargin;
        if (groundCheckCenter.x >= minX && groundCheckCenter.x <= maxX)
            return true;

        float sameXLimit = plat.extents.x + r + MovingSameXSlop;
        return Mathf.Abs(groundCheckCenter.x - plat.center.x) <= sameXLimit;
    }

    public static bool IsMovingSideOrSameX(Vector2 groundCheckCenter, float radius, Bounds plat)
    {
        float r = Mathf.Max(0.01f, radius);
        bool overlappingTopX = groundCheckCenter.x >= plat.min.x - r && groundCheckCenter.x <= plat.max.x + r;
        bool beside =
            (groundCheckCenter.x < plat.min.x - r && groundCheckCenter.x >= plat.min.x - r - MovingSideMargin) ||
            (groundCheckCenter.x > plat.max.x + r && groundCheckCenter.x <= plat.max.x + r + MovingSideMargin);
        bool almostSameX = Mathf.Abs(groundCheckCenter.x - plat.center.x) <= plat.extents.x + MovingSameXSlop;
        return overlappingTopX || beside || almostSameX;
    }

    public static bool GroundCheckAboveOrOnTop(Vector2 groundCheckCenter, float radius, Bounds platformBounds)
    {
        float graceY = GetGroundCheckGraceProbeY(groundCheckCenter, radius);
        return graceY >= platformBounds.max.y - 0.02f
               && groundCheckCenter.x >= platformBounds.min.x - radius
               && groundCheckCenter.x <= platformBounds.max.x + radius;
    }

    public static bool CircleIntersectsAabb(Vector2 center, float radius, Bounds b)
    {
        float closestX = Mathf.Clamp(center.x, b.min.x, b.max.x);
        float closestY = Mathf.Clamp(center.y, b.min.y, b.max.y);
        float dx = center.x - closestX;
        float dy = center.y - closestY;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>
    /// Subtle lip catch: small upward nudge only when the top 30% of GroundCheck has reached the surface.
    /// </summary>
    public static void TrySoftLiftOnto(PlayerController player, Collider2D platform)
    {
        if (player == null || platform == null || player.IsDead)
            return;

        // Count Up-hyper: never soft-lift — slow falls were getting pushed back onto slim tops.
        if (CountUpHyperIgnoresSlimGrace())
            return;

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb == null)
            return;

        // Never soft-lift while jumping upward — that embeds / pins mid-body.
        if (rb.linearVelocity.y > 0.08f)
            return;

        if (!TryGetGroundCheck(player, out Vector2 center, out float radius))
            return;

        Bounds plat = platform.bounds;
        if (center.x < plat.min.x - radius || center.x > plat.max.x + radius)
            return;

        if (!TryGetBodyBottom(player, out float currentBottom, out _))
            return;

        float top = plat.max.y;
        float graceY = GetGroundCheckGraceProbeY(center, radius);
        // Top 30% of GroundCheck must have reached the lip — tip-only grazes do nothing.
        if (graceY < top - 0.01f)
            return;

        float targetBottom = top + SoftLiftSkin;
        float lift = targetBottom - currentBottom;
        if (lift <= 0.001f || lift > SoftLiftMax)
            return;

        rb.MovePosition(rb.position + Vector2.up * lift);
        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(rb.linearVelocity.y, -1.25f));
    }

    /// <summary>
    /// Moving-platform lip / trap rescue: soft push for light embeds, snap above for deep traps.
    /// </summary>
    public static void TryRescueOntoMoving(PlayerController player, Collider2D platform)
    {
        if (player == null || platform == null || player.IsDead)
            return;

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb == null)
            return;

        if (rb.linearVelocity.y > 0.2f)
            return;

        if (!TryGetBodyBottom(player, out float currentBottom, out _))
            return;

        float top = platform.bounds.max.y;
        float targetBottom = top + SoftLiftSkin;
        float lift = targetBottom - currentBottom;
        if (lift <= 0.001f)
            return;

        if (lift > MovingSnapLiftMax)
            return;

        rb.MovePosition(rb.position + Vector2.up * lift);

        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
    }

    /// <summary>
    /// Before enabling collision, if the body is slightly below the top, snap onto it
    /// so Physics2D does not resolve an embed into a stuck side push.
    /// </summary>
    public static void EnsureSeatedOrKeepPhasing(
        PlayerController player,
        Collider2D platform,
        ref bool shouldBeSolid)
    {
        if (!shouldBeSolid || player == null || platform == null)
            return;

        if (!TryGetBodyBottom(player, out float feetY, out _))
            return;

        float top = platform.bounds.max.y;
        if (feetY >= top - 0.01f)
            return;

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null && rb.linearVelocity.y <= 0.08f && feetY >= top - SoftLiftMax)
        {
            if (CountUpHyperIgnoresSlimGrace())
            {
                // Stay phasable instead of soft-lifting during Up-hyper drop-through.
                shouldBeSolid = false;
                return;
            }

            TrySoftLiftOnto(player, platform);
            return;
        }

        shouldBeSolid = false;
    }

    /// <summary>
    /// Moving platforms: solidify sooner, and rescue (soft / snap) instead of aborting when trapped.
    /// </summary>
    public static void EnsureSeatedOrRescueMoving(
        PlayerController player,
        Collider2D platform,
        ref bool shouldBeSolid)
    {
        if (!shouldBeSolid || player == null || platform == null)
            return;

        if (!TryGetBodyBottom(player, out float feetY, out _))
            return;

        float top = platform.bounds.max.y;
        if (feetY >= top - 0.01f)
            return;

        float depth = top - feetY;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        float vy = rb != null ? rb.linearVelocity.y : 0f;

        if (vy > 0.25f && depth > MovingSoftLiftMax)
        {
            shouldBeSolid = false;
            return;
        }

        if (depth <= MovingSnapLiftMax)
        {
            TryRescueOntoMoving(player, platform);
            return;
        }

        shouldBeSolid = false;
    }

    public static void SetIgnoredAgainstPlayer(Collider2D platform, PlayerController player, bool ignore)
    {
        if (platform == null || player == null)
            return;

        Collider2D[] cols = player.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];
            if (col == null)
                continue;

            // Only solid body colliders — ignore triggers (hurtboxes) to avoid wasteful pairs.
            if (col.isTrigger)
                continue;

            Physics2D.IgnoreCollision(platform, col, ignore);
        }
    }
}
