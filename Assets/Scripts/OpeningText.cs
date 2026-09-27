using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fan-game disclaimer on the Start Screen "Opening text Canvas".
/// Shows opening text first, fades in "Press start to continue" after a delay,
/// then Start fades the canvas out and unlocks the real Start button (HomeTown).
/// </summary>
[DefaultExecutionOrder(-200)]
public class OpeningText : MonoBehaviour
{
    public static OpeningText Instance { get; private set; }

    /// <summary>True while disclaimer is up — StartScreen must not load HomeTown yet.</summary>
    public static bool IsBlockingStartScreen { get; private set; }

    [Header("References (auto-found if empty)")]
    [SerializeField] private SpriteRenderer openingTextBox;
    [SerializeField] private TextMeshProUGUI openingText;
    [SerializeField] private TextMeshProUGUI startPromptText;
    [SerializeField] private StartScreen startScreen;
    [SerializeField] private Button startScreenButton;

    [Header("Timing")]
    [SerializeField] private float startPromptDelay = 2f;
    [SerializeField] private float fadeInDuration = 0.45f;
    [SerializeField] private float fadeOutDuration = 0.55f;

    [Header("Opening text box aura")]
    [SerializeField] private float auraScale = 1.14f;
    [SerializeField] private Color auraColor = Color.black;

    private InputActions controls;
    private SpriteRenderer auraRenderer;
    private Material auraMaterial;
    private bool canDismiss;
    private bool dismissing;
    private bool finished;
    private readonly List<SpriteRenderer> fadeSprites = new List<SpriteRenderer>();
    private readonly List<Graphic> fadeGraphics = new List<Graphic>();
    private readonly List<Color> spriteBaseColors = new List<Color>();
    private readonly List<Color> graphicBaseColors = new List<Color>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsInScene()
    {
        if (Instance != null)
            return;

        if (!IsStartScreenScene())
            return;

        GameObject canvas = FindOpeningCanvas();
        if (canvas == null)
            return;

        if (canvas.GetComponent<OpeningText>() == null)
            canvas.AddComponent<OpeningText>();
    }

    private static bool IsStartScreenScene()
    {
        string name = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(name) &&
               name.IndexOf("Start", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GameObject FindOpeningCanvas()
    {
        string[] names =
        {
            "Opening text Canvas",
            "Opening Text Canvas",
            "Opening Text"
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

    private void Awake()
    {
        Instance = this;
        IsBlockingStartScreen = true;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResolveReferences();
        CreateSolidBlackAura();
        CacheFadeTargets();
        ApplyOpeningVisibleState();
        LockStartScreenMenu();

        controls = new InputActions();
        controls.PlayerControls.Enable();
        controls.PlayerControls.Start.performed += OnStartPerformed;

        ButtonSpriteManager manager = ButtonSpriteManager.EnsureExists();
        if (manager != null)
            manager.DeviceChanged += OnControlDeviceChanged;
    }

    private void Start()
    {
        if (finished)
            return;

        StartCoroutine(IntroSequence());
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (!finished)
            IsBlockingStartScreen = false;

        if (ButtonSpriteManager.Instance != null)
            ButtonSpriteManager.Instance.DeviceChanged -= OnControlDeviceChanged;

        if (controls != null)
        {
            controls.PlayerControls.Start.performed -= OnStartPerformed;
            controls.PlayerControls.Disable();
            controls.Dispose();
            controls = null;
        }

        if (auraMaterial != null)
        {
            Destroy(auraMaterial);
            auraMaterial = null;
        }
    }

    private void OnControlDeviceChanged(ButtonSpriteManager.ControlDeviceKind device)
    {
        if (dismissing || finished)
            return;

        RefreshStartPromptCopy();
    }

    private void ResolveReferences()
    {
        if (openingTextBox == null)
        {
            Transform box = transform.Find("Opening text box");
            if (box != null)
                openingTextBox = box.GetComponent<SpriteRenderer>();
            if (openingTextBox == null)
                openingTextBox = GetComponentInChildren<SpriteRenderer>(true);
        }

        TextMeshProUGUI[] tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TextMeshProUGUI tmp = tmps[i];
            if (tmp == null)
                continue;

            string t = tmp.text ?? string.Empty;
            bool looksLikePrompt =
                t.IndexOf("Press start", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("press start", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikePrompt)
            {
                if (startPromptText == null)
                    startPromptText = tmp;
            }
            else if (openingText == null)
            {
                openingText = tmp;
            }
        }

        if (startScreen == null)
            startScreen = FindFirstObjectByType<StartScreen>();

        if (startScreenButton == null && startScreen != null)
            startScreenButton = startScreen.StartButton;

        if (startScreenButton == null)
        {
            Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].name == "Button")
                {
                    startScreenButton = buttons[i];
                    break;
                }
            }
        }
    }

    private void CreateSolidBlackAura()
    {
        if (openingTextBox == null || openingTextBox.sprite == null)
            return;

        Transform existing = openingTextBox.transform.Find("OpeningTextAura");
        if (existing != null)
        {
            auraRenderer = existing.GetComponent<SpriteRenderer>();
            if (auraRenderer != null)
            {
                ApplyAuraVisual();
                return;
            }
        }

        GameObject auraGo = new GameObject("OpeningTextAura");
        auraGo.transform.SetParent(openingTextBox.transform, false);
        auraGo.transform.localPosition = Vector3.zero;
        auraGo.transform.localRotation = Quaternion.identity;
        auraGo.transform.localScale = Vector3.one * Mathf.Max(1.01f, auraScale);
        auraGo.transform.SetAsFirstSibling();

        auraRenderer = auraGo.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = openingTextBox.sprite;
        auraRenderer.flipX = openingTextBox.flipX;
        auraRenderer.flipY = openingTextBox.flipY;
        auraRenderer.drawMode = openingTextBox.drawMode;
        auraRenderer.size = openingTextBox.size;
        auraRenderer.sortingLayerID = openingTextBox.sortingLayerID;
        auraRenderer.sortingOrder = openingTextBox.sortingOrder - 1;

        Shader shader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (shader != null)
        {
            auraMaterial = new Material(shader);
            auraRenderer.sharedMaterial = auraMaterial;
        }

        ApplyAuraVisual();
    }

    private void ApplyAuraVisual()
    {
        if (auraRenderer == null)
            return;

        Color c = auraColor;
        c.a = 1f;
        auraRenderer.color = c;
        auraRenderer.enabled = true;
        auraRenderer.gameObject.SetActive(true);
    }

    private void CacheFadeTargets()
    {
        fadeSprites.Clear();
        fadeGraphics.Clear();
        spriteBaseColors.Clear();
        graphicBaseColors.Clear();

        SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null)
                continue;
            fadeSprites.Add(sprites[i]);
            spriteBaseColors.Add(sprites[i].color);
        }

        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null)
                continue;
            fadeGraphics.Add(graphics[i]);
            graphicBaseColors.Add(graphics[i].color);
        }
    }

    private void ApplyOpeningVisibleState()
    {
        // Disclaimer + box + aura fully visible.
        if (openingText != null)
        {
            Color c = openingText.color;
            c.a = 1f;
            openingText.color = c;
            openingText.gameObject.SetActive(true);
        }

        if (openingTextBox != null)
        {
            Color c = openingTextBox.color;
            c.a = 1f;
            openingTextBox.color = c;
            openingTextBox.enabled = true;
            openingTextBox.gameObject.SetActive(true);
        }

        ApplyAuraVisual();

        // Start prompt starts invisible until delay.
        if (startPromptText != null)
        {
            RefreshStartPromptCopy();
            Color c = startPromptText.color;
            c.a = 0f;
            startPromptText.color = c;
            startPromptText.gameObject.SetActive(true);
        }

        // Refresh cached base colors after initial alpha setup.
        for (int i = 0; i < fadeSprites.Count; i++)
        {
            if (fadeSprites[i] != null)
                spriteBaseColors[i] = fadeSprites[i].color;
        }

        for (int i = 0; i < fadeGraphics.Count; i++)
        {
            if (fadeGraphics[i] == null)
                continue;

            Color baseColor = fadeGraphics[i].color;
            if (startPromptText != null && fadeGraphics[i] == startPromptText)
                baseColor.a = 1f; // fade toward full opacity later
            graphicBaseColors[i] = baseColor;
        }
    }

    private void RefreshStartPromptCopy()
    {
        if (startPromptText == null)
            return;

        ButtonSpriteManager manager = ButtonSpriteManager.EnsureExists();
        manager?.SyncDeviceForPrompts();

        bool keyboard = manager == null ||
                        manager.CurrentDevice == ButtonSpriteManager.ControlDeviceKind.Keyboard;

        // Keyboard uses Enter for Start; pads keep "Press start".
        startPromptText.text = keyboard
            ? "Press Enter to continue"
            : "Press start to continue";
    }

    private void LockStartScreenMenu()
    {
        if (startScreen != null)
            startScreen.SetMenuEnabled(false);
        else if (startScreenButton != null)
            startScreenButton.interactable = false;
    }

    private void UnlockStartScreenMenu()
    {
        IsBlockingStartScreen = false;
        finished = true;

        if (startScreen != null)
            startScreen.SetMenuEnabled(true);
        else if (startScreenButton != null)
        {
            startScreenButton.interactable = true;
            startScreenButton.gameObject.SetActive(true);
        }
    }

    private IEnumerator IntroSequence()
    {
        canDismiss = false;
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, startPromptDelay));

        if (dismissing || finished)
            yield break;

        RefreshStartPromptCopy();

        if (startPromptText != null)
            yield return FadeGraphicAlpha(startPromptText, 0f, 1f, fadeInDuration);

        canDismiss = true;
    }

    private void OnStartPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || dismissing || finished || !canDismiss)
            return;

        SoundManager.Instance?.PlayUiConfirm();
        StartCoroutine(DismissSequence());
    }

    private IEnumerator DismissSequence()
    {
        dismissing = true;
        canDismiss = false;

        yield return FadeAllToInvisible(fadeOutDuration);

        HideAndDisableAllOpeningObjects();
        UnlockStartScreenMenu();

        // Keep script alive only long enough to unlock; disable whole canvas.
        gameObject.SetActive(false);
    }

    private IEnumerator FadeGraphicAlpha(Graphic graphic, float from, float to, float duration)
    {
        if (graphic == null)
            yield break;

        float d = Mathf.Max(0.01f, duration);
        float t = 0f;
        Color c = graphic.color;
        c.a = from;
        graphic.color = c;

        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.Clamp01(t / d));
            c = graphic.color;
            c.a = a;
            graphic.color = c;
            yield return null;
        }

        c = graphic.color;
        c.a = to;
        graphic.color = c;
    }

    private IEnumerator FadeAllToInvisible(float duration)
    {
        float[] spriteStartA = new float[fadeSprites.Count];
        float[] graphicStartA = new float[fadeGraphics.Count];

        for (int i = 0; i < fadeSprites.Count; i++)
            spriteStartA[i] = fadeSprites[i] != null ? fadeSprites[i].color.a : 0f;

        for (int i = 0; i < fadeGraphics.Count; i++)
            graphicStartA[i] = fadeGraphics[i] != null ? fadeGraphics[i].color.a : 0f;

        float d = Mathf.Max(0.01f, duration);
        float t = 0f;

        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / d);

            for (int i = 0; i < fadeSprites.Count; i++)
            {
                SpriteRenderer sr = fadeSprites[i];
                if (sr == null)
                    continue;
                Color c = sr.color;
                c.a = Mathf.Lerp(spriteStartA[i], 0f, u);
                sr.color = c;
            }

            for (int i = 0; i < fadeGraphics.Count; i++)
            {
                Graphic g = fadeGraphics[i];
                if (g == null)
                    continue;
                Color c = g.color;
                c.a = Mathf.Lerp(graphicStartA[i], 0f, u);
                g.color = c;
            }

            yield return null;
        }

        for (int i = 0; i < fadeSprites.Count; i++)
        {
            if (fadeSprites[i] == null)
                continue;
            Color c = fadeSprites[i].color;
            c.a = 0f;
            fadeSprites[i].color = c;
        }

        for (int i = 0; i < fadeGraphics.Count; i++)
        {
            if (fadeGraphics[i] == null)
                continue;
            Color c = fadeGraphics[i].color;
            c.a = 0f;
            fadeGraphics[i].color = c;
        }
    }

    private void HideAndDisableAllOpeningObjects()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null)
                child.gameObject.SetActive(false);
        }

        for (int i = 0; i < fadeSprites.Count; i++)
        {
            if (fadeSprites[i] != null)
                fadeSprites[i].enabled = false;
        }

        for (int i = 0; i < fadeGraphics.Count; i++)
        {
            if (fadeGraphics[i] != null)
                fadeGraphics[i].enabled = false;
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = false;

        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = false;
    }
}
