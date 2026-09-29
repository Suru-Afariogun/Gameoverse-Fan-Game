using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Orange power aura matching the boss crystal attack boost look.
/// </summary>
public class OrangePowerBoostAura : MonoBehaviour
{
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] [Range(0f, 1f)] private float auraAlpha = 0.7f;
    [SerializeField] private Color auraColor = new Color(1f, 0.45f, 0.1f, 1f);
    [SerializeField] private Color auraStrongColor = new Color(0.95f, 0.25f, 0.05f, 1f);

    private SpriteRenderer bodyRenderer;
    private SortingGroup sortingGroup;
    private GameObject auraObject;
    private SpriteRenderer auraRenderer;
    private Material auraMaterial;
    private float flickerPhase;
    private bool active;

    public void Bind(SpriteRenderer body, SortingGroup hostSortingGroup = null)
    {
        bodyRenderer = body;
        sortingGroup = hostSortingGroup;
    }

    public void SetVisible(bool visible)
    {
        active = visible;
        if (!visible)
        {
            flickerPhase = 0f;
            if (auraObject != null)
                auraObject.SetActive(false);
            return;
        }

        EnsureAura();
        if (auraObject != null)
            auraObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (!active)
            return;

        TickAura();
    }

    private void OnDestroy()
    {
        if (auraMaterial != null)
            Destroy(auraMaterial);
    }

    private void EnsureAura()
    {
        if (auraObject != null || bodyRenderer == null)
            return;

        auraObject = new GameObject($"{name}_EnemyCrystalBoostAura");
        auraObject.transform.SetParent(transform, false);
        auraObject.transform.localPosition = Vector3.zero;
        auraObject.transform.localScale = Vector3.one * auraBaseScale;
        auraObject.transform.SetAsFirstSibling();

        auraRenderer = auraObject.AddComponent<SpriteRenderer>();
        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            auraMaterial = new Material(solidShader);
            auraRenderer.sharedMaterial = auraMaterial;
        }
    }

    private void TickAura()
    {
        if (bodyRenderer == null || auraRenderer == null || auraObject == null)
            return;

        auraObject.SetActive(true);
        auraRenderer.sprite = bodyRenderer.sprite;
        auraRenderer.flipX = bodyRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(auraRenderer, bodyRenderer, sortingGroup);

        flickerPhase += Time.deltaTime * 1.35f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(flickerPhase * Mathf.PI * 2f);
        Color c = Color.Lerp(auraColor, auraStrongColor, 0.45f + 0.55f * shimmer);
        c.a = auraAlpha;
        auraRenderer.color = c;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount;
        auraObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }
}
