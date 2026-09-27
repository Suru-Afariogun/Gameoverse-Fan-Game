using UnityEngine;

/// <summary>
/// Parallax background layer. Put this on each background GameObject.
/// Lower layer = faster camera follow. Higher layer = slower follow.
/// Tracks the camera directly (no catch-up lag) so when the camera stops, layers stop too.
/// </summary>
[DefaultExecutionOrder(100)]
public class Background : MonoBehaviour
{
    [Header("Layer")]
    [Tooltip("Lower numbers follow the camera faster. Higher numbers follow slower.")]
    [SerializeField] private int layerOrder;
    [Tooltip("If enabled, uses this object's SpriteRenderer sorting order as the layer.")]
    [SerializeField] private bool useSpriteSortingOrder = true;

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

    private void Awake()
    {
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
    /// </summary>
    private void EnsureStageBackdropFollowsY()
    {
        if (followY)
            return;

        Transform node = transform;
        while (node != null)
        {
            if (string.Equals(node.name, "Sky Background", System.StringComparison.OrdinalIgnoreCase)
                || node.name.StartsWith("Factory Background", System.StringComparison.Ordinal))
            {
                followY = true;
                return;
            }

            node = node.parent;
        }
    }

    private void LateUpdate()
    {
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
        if (spriteRenderer != null)
            layerOrder = spriteRenderer.sortingOrder;
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
