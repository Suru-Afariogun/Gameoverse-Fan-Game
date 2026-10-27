using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Heat Dash Flare VFX — spawned at Spark Spawner when Crashing Heat dash begins (like stun sparks).
/// Follows the anchor for the dash with an X offset, flips with Bass, and sorts behind him.
/// Plays once, stretched to match the dash duration.
/// </summary>
public class BassHeatDashFlare : MonoBehaviour
{
    private Transform follow;
    private SpriteRenderer hostBody;
    private SortingGroup hostGroup;
    private float offsetX;
    private float life;
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    public void Begin(
        Transform anchor,
        float duration,
        SpriteRenderer bassBody,
        SortingGroup bassGroup,
        float worldOffsetX)
    {
        follow = anchor;
        hostBody = bassBody;
        hostGroup = bassGroup;
        offsetX = worldOffsetX;
        life = Mathf.Max(0.05f, duration);
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        SnapToAnchor();
        PlayOnceMatchingDash(life);
        SyncFlipAndSorting();
    }

    private void PlayOnceMatchingDash(float dashDuration)
    {
        if (animator == null)
            return;

        animator.Play(0, 0, 0f);
        animator.Update(0f);

        float clipLength = ResolveClipLength();
        // Stretch (or compress) so one full play covers the dash — no looping.
        animator.speed = clipLength / Mathf.Max(0.05f, dashDuration);
    }

    private float ResolveClipLength()
    {
        if (animator == null)
            return 0.05f;

        AnimatorClipInfo[] infos = animator.GetCurrentAnimatorClipInfo(0);
        if (infos != null && infos.Length > 0 && infos[0].clip != null && infos[0].clip.length > 0.001f)
            return infos[0].clip.length;

        RuntimeAnimatorController rac = animator.runtimeAnimatorController;
        if (rac != null && rac.animationClips != null)
        {
            for (int i = 0; i < rac.animationClips.Length; i++)
            {
                AnimationClip clip = rac.animationClips[i];
                if (clip != null && clip.length > 0.001f)
                    return clip.length;
            }
        }

        return 0.05f;
    }

    private void Update()
    {
        SnapToAnchor();
        SyncFlipAndSorting();

        life -= Time.deltaTime;
        if (life <= 0f)
            Destroy(gameObject);
    }

    private void SnapToAnchor()
    {
        if (follow == null)
            return;

        Vector3 pos = follow.position;
        pos.x += offsetX;
        transform.position = pos;
    }

    private void SyncFlipAndSorting()
    {
        if (spriteRenderer == null)
            return;

        if (hostBody != null)
            spriteRenderer.flipX = hostBody.flipX;

        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }
}
