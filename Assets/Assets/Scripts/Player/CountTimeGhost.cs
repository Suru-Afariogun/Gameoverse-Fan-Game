using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-shot afterimage left along a rewind path (playable Count's Rewind, Boss Count's time tricks).
/// Fades out and destroys itself.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CountTimeGhost : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color startColor;
    private float delay;
    private float duration;
    private float elapsed;

    /// <summary>Spawns a fading copy of <paramref name="sprite"/> drawn just behind the host (body or its sorting group).</summary>
    public static CountTimeGhost Spawn(
        Vector3 position,
        Sprite sprite,
        bool flipX,
        Color color,
        SpriteRenderer hostBody,
        SortingGroup hostGroup,
        float delaySeconds,
        float fadeSeconds)
    {
        if (sprite == null)
            return null;

        var go = new GameObject("CountTimeGhost");
        go.transform.position = position;
        var ghost = go.AddComponent<CountTimeGhost>();
        ghost.spriteRenderer = go.GetComponent<SpriteRenderer>();
        ghost.spriteRenderer.sprite = sprite;
        ghost.spriteRenderer.flipX = flipX;
        if (hostBody != null)
            ghost.spriteRenderer.sharedMaterial = hostBody.sharedMaterial;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(ghost.spriteRenderer, hostGroup, hostBody, -1);

        ghost.startColor = color;
        ghost.delay = Mathf.Max(0f, delaySeconds);
        ghost.duration = Mathf.Max(0.05f, fadeSeconds);
        Color hidden = color;
        hidden.a = ghost.delay > 0f ? 0f : color.a;
        ghost.spriteRenderer.color = hidden;
        return ghost;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed < delay)
            return;

        float t = Mathf.Clamp01((elapsed - delay) / duration);
        Color c = startColor;
        c.a = startColor.a * (1f - t * t);
        spriteRenderer.color = c;
        if (t >= 1f)
            Destroy(gameObject);
    }
}
