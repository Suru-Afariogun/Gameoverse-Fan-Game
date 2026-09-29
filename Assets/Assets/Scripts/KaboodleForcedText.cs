using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// HomeTown nudge canvas "Speak to Kaboodle forced text Canvas".
/// Shows on first spawn and when walking away from Kaboodle.
/// Hides when walking toward Kaboodle or when opening Kaboodle.
/// Stops forever after Kit + Malice tutorials are both finished.
/// </summary>
public class KaboodleForcedText : MonoBehaviour
{
    public static KaboodleForcedText Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsInScene()
    {
        if (Instance != null)
            return;

        if (!IsHomeTownScene())
            return;

        GameObject canvas = FindForcedTextCanvas();
        if (canvas == null)
            return;

        if (canvas.GetComponent<KaboodleForcedText>() == null)
            canvas.AddComponent<KaboodleForcedText>();
    }

    private static bool IsHomeTownScene()
    {
        string name = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(name) &&
               name.IndexOf("HomeTown", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GameObject FindForcedTextCanvas()
    {
        string[] names =
        {
            "Speak to Kaboodle forced text Canvas",
            "Kaboodle Forced Text",
            "Speak to Kaboodle Forced Text"
        };

        for (int n = 0; n < names.Length; n++)
        {
            GameObject go = GameObject.Find(names[n]);
            if (go != null)
                return go;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            for (int n = 0; n < names.Length; n++)
            {
                GameObject found = FindByNameRecursive(roots[i].transform, names[n]);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static GameObject FindByNameRecursive(Transform root, string name)
    {
        if (root == null)
            return null;

        if (root.name == name)
            return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindByNameRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    [Header("References")]
    [Tooltip("Optional. If empty, all children of this object are shown/hidden together.")]
    [SerializeField] private GameObject visibilityRoot;
    [SerializeField] private Kaboodle kaboodle;

    [Header("Away From Kaboodle")]
    [Tooltip("How strongly the player must move (stick or velocity) to count as walking.")]
    [SerializeField] private float moveThreshold = 0.2f;
    [Tooltip("Ignore tiny X separation from Kaboodle when deciding toward/away.")]
    [SerializeField] private float minSideDistanceX = 0.15f;

    private bool visible;
    private bool hasAppliedVisibility;
    private bool showedOnFirstSpawn;
    private bool wasKaboodleOpen;
    private Canvas[] canvases;
    private SpriteRenderer[] spriteRenderers;
    private Graphic[] graphics;
    private Behaviour[] raycasters;

    private void Awake()
    {
        Instance = this;

        // Keep this GameObject active forever so Update can show/hide again.
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (kaboodle == null)
            kaboodle = FindFirstObjectByType<Kaboodle>();

        CacheVisuals();
        FixCanvasSetup();
        SetVisible(false);

        if (KaboodleTutorialProgress.BothTutorialsDone())
        {
            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned += OnPlayerSpawned;
    }

    private void Start()
    {
        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnPlayerSpawned -= OnPlayerSpawned;
            PlayerSpawner.Instance.OnPlayerSpawned += OnPlayerSpawned;

            if (!showedOnFirstSpawn &&
                PlayerSpawner.Instance.CurrentPlayer != null &&
                !KaboodleTutorialProgress.BothTutorialsDone())
            {
                ShowForFirstSpawn();
            }
        }
    }

    private void OnDisable()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned -= OnPlayerSpawned;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void CacheVisuals()
    {
        Transform root = visibilityRoot != null ? visibilityRoot.transform : transform;
        canvases = root.GetComponentsInChildren<Canvas>(true);
        spriteRenderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        graphics = root.GetComponentsInChildren<Graphic>(true);
        raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);
    }

    private void FixCanvasSetup()
    {
        if (canvases == null)
            return;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null)
                continue;

            if (c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera == null)
            {
                if (Camera.main != null)
                    c.worldCamera = Camera.main;
                else
                    c.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (c.sortingOrder < 40)
                c.sortingOrder = 45;
        }
    }

    private void Update()
    {
        if (KaboodleTutorialProgress.BothTutorialsDone())
        {
            SetVisible(false);
            enabled = false;
            return;
        }

        if (kaboodle == null)
            kaboodle = FindFirstObjectByType<Kaboodle>();

        bool kaboodleOpen = kaboodle != null && kaboodle.IsBoxOpen;
        if (kaboodleOpen)
        {
            SetVisible(false);
            wasKaboodleOpen = true;
            return;
        }

        if (wasKaboodleOpen)
        {
            wasKaboodleOpen = false;
            SetVisible(false);
        }

        int walk = GetWalkRelativeToKaboodle();
        if (walk > 0)
            SetVisible(true);
        else if (walk < 0)
            SetVisible(false);
    }

    private void OnPlayerSpawned(PlayerController player)
    {
        if (showedOnFirstSpawn || KaboodleTutorialProgress.BothTutorialsDone())
            return;

        ShowForFirstSpawn();
    }

    private void ShowForFirstSpawn()
    {
        showedOnFirstSpawn = true;
        if (kaboodle != null && kaboodle.IsBoxOpen)
            return;

        SetVisible(true);
    }

    /// <summary>
    /// +1 walking away from Kaboodle, -1 walking toward, 0 idle / unknown.
    /// </summary>
    private int GetWalkRelativeToKaboodle()
    {
        if (kaboodle == null)
            return 0;

        PlayerController player = PlayerController.Active;
        if (player == null || player.IsDead || player.InputLocked)
            return 0;

        float dx = player.transform.position.x - kaboodle.transform.position.x;
        if (Mathf.Abs(dx) < minSideDistanceX)
        {
            // On top of Kaboodle: any clear horizontal move toward center already arrived —
            // treat left/right by move only as toward if closing the tiny gap.
            float moveNear = ResolveMoveX(player);
            if (Mathf.Abs(moveNear) < moveThreshold)
                return 0;

            // Moving toward Kaboodle's X.
            if (Mathf.Sign(moveNear) != Mathf.Sign(dx) && Mathf.Abs(dx) > 0.001f)
                return -1;
            return 0;
        }

        float side = Mathf.Sign(dx); // +1 = player is right of Kaboodle
        float moveX = ResolveMoveX(player);
        if (Mathf.Abs(moveX) < moveThreshold)
            return 0;

        // Same sign as side = further away. Opposite = toward Kaboodle.
        return Mathf.Sign(moveX) == side ? 1 : -1;
    }

    private float ResolveMoveX(PlayerController player)
    {
        float moveX = player.MoveInput.x;
        if (Mathf.Abs(moveX) >= moveThreshold)
            return moveX;

        return player.Velocity.x;
    }

    /// <summary>
    /// Hides/shows the entire visual (UI + SpriteRenderers + children), without
    /// deactivating this GameObject so Update keeps running.
    /// </summary>
    private void SetVisible(bool show)
    {
        if (hasAppliedVisibility && visible == show)
            return;

        hasAppliedVisibility = true;
        visible = show;

        if (canvases == null || spriteRenderers == null || graphics == null)
            CacheVisuals();

        // 1) Children hold the art/text — toggle them as a block.
        if (visibilityRoot != null && visibilityRoot != gameObject)
        {
            if (visibilityRoot.activeSelf != show)
                visibilityRoot.SetActive(show);
        }
        else
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.gameObject.activeSelf != show)
                    child.gameObject.SetActive(show);
            }
        }

        // 2) Gate every visual component (SpriteRenderers still draw if only Canvas is disabled).
        SetBehavioursEnabled(canvases, show);
        SetBehavioursEnabled(raycasters, show);
        SetSpriteRenderersEnabled(spriteRenderers, show);
        SetGraphicsEnabled(graphics, show);
    }

    private static void SetBehavioursEnabled(Behaviour[] behaviours, bool enabled)
    {
        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
                behaviours[i].enabled = enabled;
        }
    }

    private static void SetSpriteRenderersEnabled(SpriteRenderer[] renderers, bool enabled)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    private static void SetGraphicsEnabled(Graphic[] graphics, bool enabled)
    {
        if (graphics == null)
            return;

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].enabled = enabled;
        }
    }

    /// <summary>Called when a character finishes their Kaboodle tutorial.</summary>
    public void NotifyTutorialProgressChanged()
    {
        if (!KaboodleTutorialProgress.BothTutorialsDone())
            return;

        SetVisible(false);
        enabled = false;
    }
}
