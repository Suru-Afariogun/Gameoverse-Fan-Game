using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class StartScreen : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private string homeTownSceneName = "HomeTown";
    [Tooltip("Added to the button's current scale when hovered or selected (1 = double size).")]
    [SerializeField] private float hoverScaleAdd = 1f;
    [SerializeField] private float scaleLerpSpeed = 12f;

    private InputActions controls;
    private bool loading;
    private bool menuEnabled;
    private RectTransform buttonRect;
    private Vector3 buttonBaseScale = Vector3.one;
    private bool pointerOverButton;
    private bool buttonSelected;

    public Button StartButton => startButton;

    private void Awake()
    {
        // Locked while disclaimer is present; OpeningText unlocks after fade-out.
        menuEnabled = false;

        if (startButton != null)
        {
            startButton.onClick.AddListener(LoadHomeTown);
            buttonRect = startButton.transform as RectTransform;
            if (buttonRect != null)
                buttonBaseScale = buttonRect.localScale;
            startButton.interactable = false;
        }

        EnsureEventSystem();

        controls = new InputActions();
        controls.PlayerControls.Enable();
        controls.PlayerControls.Start.performed += OnStartPerformed;
        controls.PlayerControls.Select.performed += OnSelectPerformed;
    }

    private void Start()
    {
        if (OpeningText.Instance != null || OpeningText.IsBlockingStartScreen)
            SetMenuEnabled(false);
        else
            SetMenuEnabled(true);
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(LoadHomeTown);

        if (controls != null)
        {
            controls.PlayerControls.Start.performed -= OnStartPerformed;
            controls.PlayerControls.Select.performed -= OnSelectPerformed;
            controls.PlayerControls.Disable();
            controls.Dispose();
        }
    }

    /// <summary>
    /// OpeningText disables the HomeTown Start button until the disclaimer fades out.
    /// </summary>
    public void SetMenuEnabled(bool enabled)
    {
        menuEnabled = enabled;

        if (startButton != null)
        {
            startButton.interactable = enabled;
            if (enabled)
                startButton.gameObject.SetActive(true);
        }

        if (enabled)
            SelectStartButton();
    }

    private void Update()
    {
        if (!menuEnabled)
            return;

        TickButtonHoverSelect();
        TickButtonScale();
    }

    private void TickButtonHoverSelect()
    {
        pointerOverButton = false;
        buttonSelected = false;
        if (startButton == null)
            return;

        pointerOverButton = IsPointerOverStartButton();

        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == startButton.gameObject)
            buttonSelected = true;
    }

    private bool IsPointerOverStartButton()
    {
        if (startButton == null)
            return false;

        EventSystem es = EventSystem.current;
        if (es != null && es.IsPointerOverGameObject())
        {
            PointerEventData pointer = new PointerEventData(es)
            {
                position = Mouse.current != null
                    ? Mouse.current.position.ReadValue()
                    : (Vector2)Input.mousePosition
            };

            var results = new System.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(pointer, results);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject == startButton.gameObject ||
                    results[i].gameObject.transform.IsChildOf(startButton.transform))
                    return true;
            }
        }

        return false;
    }

    private void TickButtonScale()
    {
        if (buttonRect == null)
            return;

        float add = (pointerOverButton || buttonSelected) ? Mathf.Max(0f, hoverScaleAdd) : 0f;
        Vector3 target = buttonBaseScale + Vector3.one * add;
        buttonRect.localScale = Vector3.Lerp(
            buttonRect.localScale,
            target,
            1f - Mathf.Exp(-Mathf.Max(0.01f, scaleLerpSpeed) * Time.unscaledDeltaTime));
    }

    private void OnStartPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || loading || !menuEnabled || OpeningText.IsBlockingStartScreen)
            return;

        SoundManager.Instance?.PlayUiConfirm();
        LoadHomeTown();
    }

    private void OnSelectPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || loading)
            return;

        // Allow quit even during disclaimer.
        SoundManager.Instance?.PlayUiConfirm();
        QuitGame();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private void SelectStartButton()
    {
        if (startButton == null)
            return;

        EventSystem es = EventSystem.current;
        if (es == null)
            return;

        es.SetSelectedGameObject(startButton.gameObject);
    }

    public void LoadHomeTown()
    {
        if (loading || !menuEnabled || OpeningText.IsBlockingStartScreen)
            return;

        loading = true;
        ScreenFade.EnsureExists().LoadScene(homeTownSceneName);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
