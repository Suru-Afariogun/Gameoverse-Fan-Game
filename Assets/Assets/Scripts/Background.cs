using UnityEngine;

/// <summary>
/// Parallax background layer. Put this on each background GameObject.
/// Lower layer = faster camera follow. Higher layer = slower follow.
/// Tracks the camera directly (no catch-up lag) so when the camera stops, layers stop too.
///
/// Safe with nesting: if a parent and its children both have <see cref="Background"/>,
/// only the child layers move. A parent that also moved would drag children after their
/// own LateUpdate and cancel parallax (common on City "Backgrounds" group roots).
/// </summary>
[DefaultExecutionOrder(100)]
public class Background : MonoBehaviour
{
    [Header("Layer")]
    [Tooltip("Parallax speed index. Lower = follows camera more (closer). Higher = drifts more (farther). Put Background only on each layer piece — not on an empty parent that holds mixed layers.")]
    [SerializeField] private int layerOrder;
    [Tooltip("If enabled, overwrites Layer Order from this object's SpriteRenderer sorting order when that order is 0–20. Turn OFF to use the Layer Order you set in the Inspector.")]
    [SerializeField] private bool useSpriteSortingOrder = false;

    [Header("Parallax")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("How much slower each layer step is. Example: 0.08 → layer 0 = 1.0, layer 1 = 0.92, layer 2 = 0.84...")]
    [SerializeField] private float factorPerLayer = 0.08f;
    [SerializeField] private float minimumParallaxFactor = 0.05f;
    [SerializeField] private bool followX = true;
    [SerializeField] private bool followY = true;

    private float parallaxFactor = 1f;
    private Vector3 startPosition;
    private Vector3 startCameraPosition;
    private bool hasCachedStartPositions;
    /// <summary>False when a child also has Background — that child drives its own transform.</summary>
    private bool drivesOwnTransform = true;

    private void Awake()
    {
        drivesOwnTransform = !HasBackgroundOnDescendant();
        ResolveCamera();
        ResolveLayerOrder();
        parallaxFactor = CalculateParallaxFactor(layerOrder);
    }

    private void Start()
    {
        ResolveCamera();
        EnsureStageBackdropFollowsY();
        CacheStartPositions();
    }

    /// <summary>
    /// Factory / sky segments must scroll vertically with the camera or the view exposes clear color.
    /// City building / cloud props intentionally keep followY off (set by City setup).
    /// </summary>
    private void EnsureStageBackdropFollowsY()
    {
        if (followY)
            return;

        // City building art should stay planted on the Y axis even if parent folders match below.
        if (IsCityBuildingOrCloudPropName(gameObject.name))
            return;

        Transform node = transform;
        while (node != null)
        {
            string n = node.name;
            if (string.IsNullOrEmpty(n))
            {
                node = node.parent;
                continue;
            }

            if (string.Equals(n, "Sky Background", System.StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Factory Background", System.StringComparison.Ordinal)
                || n.StartsWith("City Sky", System.StringComparison.OrdinalIgnoreCase))
            {
                followY = true;
                return;
            }

            node = node.parent;
        }
    }

    private static bool IsCityBuildingOrCloudPropName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        if (objectName.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (objectName.IndexOf("Background parts", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (objectName.IndexOf("Building", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
            objectName.IndexOf("grey", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (objectName.IndexOf("light grey blue", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("light gray blue", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private void LateUpdate()
    {
        if (!drivesOwnTransform)
            return;

        if (cameraTransform == null)
        {
            ResolveCamera();
            if (cameraTransform == null)
                return;

            CacheStartPositions();
        }

        if (!hasCachedStartPositions)
            CacheStartPositions();

        // Direct lock to camera: no SmoothDamp catch-up after the camera stops.
        Vector3 cameraDelta = cameraTransform.position - startCameraPosition;
        Vector3 targetPosition = startPosition;

        if (followX)
            targetPosition.x = startPosition.x + cameraDelta.x * parallaxFactor;
        if (followY)
            targetPosition.y = startPosition.y + cameraDelta.y * parallaxFactor;

        targetPosition.z = transform.position.z;
        transform.position = targetPosition;
    }

    private bool HasBackgroundOnDescendant()
    {
        Background[] nested = GetComponentsInChildren<Background>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            if (nested[i] != null && nested[i] != this)
                return true;
        }

        return false;
    }

    private void ResolveCamera()
    {
        if (cameraTransform != null)
            return;

        if (CameraFollow.Instance != null)
        {
            cameraTransform = CameraFollow.Instance.transform;
            return;
        }

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void ResolveLayerOrder()
    {
        if (!useSpriteSortingOrder)
            return;

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        // Draw-order sorting can be large or negative (City building art). Those values are
        // not parallax layer indices — adopting them makes every piece lock to the camera
        // or clamp to the minimum factor. Only trust small non-negative orders (Level one style).
        int sort = spriteRenderer.sortingOrder;
        if (sort >= 0 && sort <= 20)
            layerOrder = sort;
    }

    private void CacheStartPositions()
    {
        startPosition = transform.position;

        if (cameraTransform != null)
            startCameraPosition = cameraTransform.position;

        hasCachedStartPositions = cameraTransform != null;
    }

    private float CalculateParallaxFactor(int order)
    {
        int safeOrder = Mathf.Max(0, order);
        float factor = 1f - (safeOrder * factorPerLayer);
        return Mathf.Clamp(factor, minimumParallaxFactor, 1f);
    }

    /// <summary>
    /// Sets parallax depth at runtime (lower = closer / follows camera more).
    /// Used by City layer cleanup so Inspector mistakes can be corrected safely.
    /// </summary>
    public void ApplyParallaxLayer(
        int order,
        bool useSortingOrder = false,
        bool followHorizontal = true,
        bool followVertical = true)
    {
        layerOrder = Mathf.Max(0, order);
        useSpriteSortingOrder = useSortingOrder;
        if (useSpriteSortingOrder)
            ResolveLayerOrder();

        followX = followHorizontal;
        followY = followVertical;
        parallaxFactor = CalculateParallaxFactor(layerOrder);
        drivesOwnTransform = !HasBackgroundOnDescendant();
        hasCachedStartPositions = false;
    }

    /// <summary>Stops vertical parallax (e.g. City buildings should not rise with jump camera).</summary>
    public void SetFollowAxes(bool horizontal, bool vertical)
    {
        followX = horizontal;
        followY = vertical;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        factorPerLayer = Mathf.Max(0f, factorPerLayer);
        minimumParallaxFactor = Mathf.Clamp01(minimumParallaxFactor);

        if (Application.isPlaying)
            return;

        ResolveLayerOrder();
        parallaxFactor = CalculateParallaxFactor(layerOrder);
    }
#endif
}
