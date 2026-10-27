using UnityEngine;

/// <summary>
/// MM2 Flash / Time Stopper sparkle — sits at a viewport-relative position and twinkles via Animator.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BassFlashTimeStar : MonoBehaviour
{
    private Camera followCamera;
    private Vector2 viewportPos;
    private float depth;
    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public void Place(Camera cam, Vector2 viewport, float zDepth)
    {
        followCamera = cam;
        viewportPos = viewport;
        depth = zDepth;
        SnapToViewport();
        EnsureAnimating();
    }

    /// <summary>Stagger the Flash Time Star loop so stars don't blink in sync.</summary>
    public void PlayTwinkle(float normalizedTime)
    {
        EnsureAnimating();
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        animator.enabled = true;
        animator.speed = 1f;
        animator.Play("Flash Time Star", 0, Mathf.Repeat(normalizedTime, 1f));
        animator.Update(0f);
    }

    private void EnsureAnimating()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            return;

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
        if (!animator.enabled)
            animator.enabled = true;
    }

    private void LateUpdate()
    {
        SnapToViewport();
    }

    private void SnapToViewport()
    {
        if (followCamera == null)
            followCamera = Camera.main;
        if (followCamera == null)
            return;

        Vector3 world = followCamera.ViewportToWorldPoint(new Vector3(viewportPos.x, viewportPos.y, depth));
        transform.position = world;
    }
}
