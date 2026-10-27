using UnityEngine;

/// <summary>
/// Keeps Screen Space - Camera HUD canvases (player life bar, boss life bar, pause menus)
/// visible at any Main Camera orthographic size.
///
/// Root cause: LifeBar / BossLifeBar / Pause SpriteRenderer children were authored with
/// local Z ≈ -6942. Canvas scale grows with orthographicSize, so that Z pushes the art
/// behind the camera when Size is raised (e.g. 7 → 9) and the HUD disappears.
/// Fix: keep those canvases bound to Main Camera, and pin HUD child local Z to 0 so
/// they stay on the canvas plane no matter the Size.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-250)]
public sealed class HudCanvasCameraSync : MonoBehaviour
{
    private const float MinNearClip = 0.01f;
    private const float PreferredPlaneDistance = 100f;
    private const float DeepLocalZThreshold = 1f;

    private Camera cam;
    private float lastOrthoSize = float.NaN;
    private float lastNear;
    private float lastFar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureOnMainCamera()
    {
        Camera main = Camera.main;
        if (main == null)
            return;

        if (main.GetComponent<HudCanvasCameraSync>() == null)
            main.gameObject.AddComponent<HudCanvasCameraSync>();

        SyncAllHudCanvases(main);
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;

        SanitizeCameraClipPlanes(cam);
        SyncAllHudCanvases(cam);
        CacheCameraState();
    }

    private void OnEnable()
    {
        SanitizeCameraClipPlanes(cam != null ? cam : Camera.main);
        SyncAllHudCanvases(cam != null ? cam : Camera.main);
        CacheCameraState();
    }

    private void Start()
    {
        SyncAllHudCanvases(cam != null ? cam : Camera.main);
        CacheCameraState();
    }

    private void LateUpdate()
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return;

        bool clipFixed = SanitizeCameraClipPlanes(cam);
        bool frustumChanged =
            clipFixed ||
            !Mathf.Approximately(cam.orthographicSize, lastOrthoSize) ||
            !Mathf.Approximately(cam.nearClipPlane, lastNear) ||
            !Mathf.Approximately(cam.farClipPlane, lastFar);

        if (!frustumChanged)
            return;

        SyncAllHudCanvases(cam);
        CacheCameraState();
    }

    private void CacheCameraState()
    {
        if (cam == null)
            return;

        lastOrthoSize = cam.orthographicSize;
        lastNear = cam.nearClipPlane;
        lastFar = cam.farClipPlane;
    }

    public static bool SanitizeCameraClipPlanes(Camera camera)
    {
        if (camera == null)
            return false;

        bool changed = false;
        if (camera.nearClipPlane < MinNearClip)
        {
            camera.nearClipPlane = MinNearClip;
            changed = true;
        }

        if (camera.farClipPlane <= camera.nearClipPlane + PreferredPlaneDistance + 1f)
        {
            camera.farClipPlane = camera.nearClipPlane + Mathf.Max(1000f, PreferredPlaneDistance + 100f);
            changed = true;
        }

        return changed;
    }

    public static void SyncAllHudCanvases(Camera camera)
    {
        if (camera == null)
            return;

        SanitizeCameraClipPlanes(camera);

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !IsGameHudCanvas(canvas))
                continue;

            BindCanvasToCamera(canvas, camera);
        }

        Canvas.ForceUpdateCanvases();
    }

    public static void BindCanvasToCamera(Canvas canvas, Camera camera)
    {
        if (canvas == null)
            return;

        if (camera == null)
            camera = Camera.main;

        if (canvas.renderMode == RenderMode.WorldSpace)
            return;

        if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
            canvas.renderMode = RenderMode.ScreenSpaceCamera;

        if (camera != null)
        {
            SanitizeCameraClipPlanes(camera);
            canvas.worldCamera = camera;

            float near = Mathf.Max(MinNearClip, camera.nearClipPlane);
            float far = Mathf.Max(near + PreferredPlaneDistance + 1f, camera.farClipPlane);
            float plane = PreferredPlaneDistance;
            if (plane >= far)
                plane = Mathf.Max(near + 0.05f, far - 0.05f);
            if (plane <= near)
                plane = Mathf.Lerp(near, far, 0.5f);

            canvas.planeDistance = plane;

            // Critical: deep local Z * canvasScale (which grows with ortho Size) sends
            // SpriteRenderer HUD behind the camera. Keep children on the canvas plane.
            FlattenHudChildDepth(canvas.transform);

            Canvas.ForceUpdateCanvases();
            Transform t = canvas.transform;
            if (t != null && t.localScale.sqrMagnitude < 1e-10f)
            {
                bool wasEnabled = canvas.enabled;
                canvas.enabled = false;
                canvas.enabled = wasEnabled;
                Canvas.ForceUpdateCanvases();
            }
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
    }

    /// <summary>
    /// Zero out authored deep local Z under HUD canvases (LifeBar ≈ -6942, Pause container, etc.).
    /// XY layout is left alone.
    /// </summary>
    private static void FlattenHudChildDepth(Transform root)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
            FlattenHudChildDepthRecursive(root.GetChild(i));
    }

    private static void FlattenHudChildDepthRecursive(Transform node)
    {
        if (node == null)
            return;

        Vector3 local = node.localPosition;
        if (Mathf.Abs(local.z) > DeepLocalZThreshold)
        {
            local.z = 0f;
            node.localPosition = local;
        }

        for (int i = 0; i < node.childCount; i++)
            FlattenHudChildDepthRecursive(node.GetChild(i));
    }

    private static bool IsGameHudCanvas(Canvas canvas)
    {
        if (canvas == null)
            return false;

        if (!canvas.isRootCanvas)
            return false;

        string n = canvas.gameObject.name;
        if (string.IsNullOrEmpty(n))
            return false;

        if (n.IndexOf("Player Hud", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Boss Health", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Pause Canvas", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Paue Canvas", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Warning Canvas", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }
}
