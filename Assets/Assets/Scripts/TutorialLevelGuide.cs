using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Moves the Tutorial level guide can ask for (reported by the player controllers and item carrier).</summary>
public enum TutorialAction
{
    Jump,
    AirJump,
    DropThrough,
    WallJump,
    Attack,
    AirAttack,
    AimShot,
    ChargeAttack,
    Hover,
    Dive,
    GrapplePull,
    GrappleGrab,
    DiveStab,
    VBots,
    Pogo,
    ItemPickup,
    ItemUse
}

/// <summary>
/// Tutorial level: on each character's first <see cref="GuidedVisits"/> visits, Kaboodle gives short
/// instructions in the dialogue box. A page only advances once the player does what it says. The game keeps
/// running underneath, and the box fades while it covers an enemy, an enemy shot or the player.
/// Auto-added on scene load; uses Resources/<see cref="BoxResourceName"/> for the box visuals.
/// </summary>
public sealed class TutorialLevelGuide : MonoBehaviour
{
    public const int GuidedVisits = 2;
    private const string VisitsKeyPrefix = "Gameoverse_TutorialLevelVisits_";
    private const string BoxResourceName = "Tutorial Dialogue Box";

    [SerializeField] private string speakerName = "Kaboodle";
    [SerializeField] private float charactersPerSecond = 42f;
    [Tooltip("Pause after a step is done before the next page starts typing.")]
    [SerializeField] private float pageAdvanceDelay = 0.4f;
    [SerializeField] private float finalPageSeconds = 4f;
    [Tooltip("Box opacity while it covers an enemy, an enemy shot or the player.")]
    [SerializeField] private float coveredAlpha = 0.3f;
    [SerializeField] private float fadeSpeed = 10f;
    [Tooltip("Gap between the top of the screen and the box, in canvas units.")]
    [SerializeField] private float topMargin = 12f;
    [Tooltip("Seconds of walking needed for the first page.")]
    [SerializeField] private float moveSecondsRequired = 0.4f;

    private sealed class Step
    {
        public string Text;
        public TutorialAction? Action;
        public Func<bool> Done;
        public Func<bool> Skip;
    }

    private readonly List<Step> steps = new List<Step>();
    private readonly List<Bounds> coverBounds = new List<Bounds>();
    private readonly Vector3[] corners = new Vector3[4];

    private GameObject boxInstance;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform boxImage;
    private TMP_Text nameText;
    private TMP_Text bodyText;

    private int stepIndex = -1;
    private bool stepComplete;
    private bool finished;
    private float moveSeconds;
    private float nextItemSourceCheck;
    private bool itemSourcesLeft = true;
    private string guidedCharacterId;
    private bool closing;
    private bool covering;
    private float nextCoverCheck;
    private Coroutine typingRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= EnsureOnSceneLoaded;
        SceneManager.sceneLoaded += EnsureOnSceneLoaded;
    }

    private static void EnsureOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsTutorialScene(scene.name) || FindFirstObjectByType<TutorialLevelGuide>() != null)
            return;

        new GameObject("Tutorial Level Guide").AddComponent<TutorialLevelGuide>();
    }

    public static bool IsTutorialScene(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) &&
               sceneName.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static int GetVisits(string characterId) => PlayerPrefs.GetInt(VisitsKeyPrefix + characterId.Trim(), 0);

    private void OnEnable()
    {
        PlayerController.TutorialActionPerformed += HandleAction;
    }

    private void OnDisable()
    {
        PlayerController.TutorialActionPerformed -= HandleAction;
        if (ButtonSpriteManager.Instance != null)
            ButtonSpriteManager.Instance.DeviceChanged -= OnControlDeviceChanged;
        SoundManager.Instance?.EndDialogueTyping();
    }

    private IEnumerator Start()
    {
        PlayerController player = null;
        while (player == null || KitRocketShip.IsArrivalInProgress || player.InputLocked)
        {
            yield return null;
            player = PlayerController.ResolveActive();
        }

        string characterId = string.IsNullOrWhiteSpace(player.CharacterId) ? "Kit" : player.CharacterId.Trim();
        int visits = GetVisits(characterId);
        if (visits >= GuidedVisits || !CreateBox())
        {
            Destroy(gameObject);
            yield break;
        }

        PlayerPrefs.SetInt(VisitsKeyPrefix + characterId, visits + 1);
        PlayerPrefs.Save();

        guidedCharacterId = characterId;
        BuildSteps(characterId);
        ButtonSpriteManager.EnsureExists().DeviceChanged += OnControlDeviceChanged;
        ShowStep(0);
    }

    private void OnControlDeviceChanged(ButtonSpriteManager.ControlDeviceKind device)
    {
        if (finished || string.IsNullOrEmpty(guidedCharacterId))
            return;

        BuildSteps(guidedCharacterId, syncDevice: false);
        if (stepIndex < 0 || stepIndex >= steps.Count || bodyText == null)
            return;

        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
            SoundManager.Instance?.EndDialogueTyping();
        }

        bodyText.text = steps[stepIndex].Text;
        bodyText.maxVisibleCharacters = int.MaxValue;
    }

    private void Update()
    {
        if (boxInstance == null)
            return;

        UpdateCoverFade();

        if (finished || stepComplete || stepIndex < 0 || stepIndex >= steps.Count)
            return;

        Step step = steps[stepIndex];
        if ((step.Done != null && step.Done()) || (step.Skip != null && step.Skip()))
            CompleteStep();
    }

    private void HandleAction(PlayerController player, TutorialAction action)
    {
        if (finished || stepComplete || stepIndex < 0 || stepIndex >= steps.Count)
            return;

        if (player != PlayerController.ResolveActive())
            return;

        if (steps[stepIndex].Action == action)
            CompleteStep();
    }

    private void CompleteStep()
    {
        stepComplete = true;
        SoundManager.Instance?.PlayUiConfirm();
        StartCoroutine(AdvanceAfterDelay());
    }

    private IEnumerator AdvanceAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, pageAdvanceDelay));
        ShowStep(stepIndex + 1);
    }

    private void ShowStep(int index)
    {
        stepIndex = index;
        stepComplete = false;
        moveSeconds = 0f;

        if (index < steps.Count)
        {
            TypeText(steps[index].Text);
            return;
        }

        finished = true;
        TypeText("Great job! You're ready. Now make it through the factory!");
        StartCoroutine(CloseAfterDelay());
    }

    private IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.5f, finalPageSeconds));

        closing = true;
        float alpha = canvasGroup != null ? canvasGroup.alpha : 0f;
        while (canvasGroup != null && alpha > 0f)
        {
            alpha -= Time.deltaTime * 3f;
            canvasGroup.alpha = Mathf.Max(0f, alpha);
            yield return null;
        }

        if (boxInstance != null)
            Destroy(boxInstance);
        Destroy(gameObject);
    }

    // ---------- Steps ----------

    /// <summary>Same button phrasing as Kaboodle's tutorial, for the device the player is using right now.</summary>
    private void BuildSteps(string characterId, bool syncDevice = true)
    {
        steps.Clear();
        ButtonSpriteManager b = ButtonSpriteManager.EnsureExists();
        if (syncDevice)
            b.SyncDeviceForPrompts();

        bool kit = Is(characterId, "Kit");
        bool count = Is(characterId, "Count");
        bool malice = Is(characterId, "Malice");
        bool hex = Is(characterId, "Hex");
        bool harlieLike = hex || Is(characterId, "Harlie");

        Add($"To move, {b.FormatMoveHorizontal()}.", () => moveSeconds >= moveSecondsRequired);
        Add($"To jump, {b.FormatPress("Jump")}.", TutorialAction.Jump);
        if (!count)
            Add($"To double jump, {b.FormatPress("Jump")} again in the air.", TutorialAction.AirJump);
        if (kit)
            Add($"To hover, double jump, then {b.FormatHold("Jump")}.", TutorialAction.Hover);
        Add($"To dash, {b.FormatPress("Dash")}.", () => Player != null && Player.IsDashing);
        Add($"To air dash, jump, then {b.FormatPress("Dash")} in the air.",
            () => Player != null && Player.IsDashing && !Player.IsPhysicallyGrounded);
        Add($"To dash jump, {b.FormatPress("Jump")} while dashing.", () => Player != null && Player.IsDashJumping);

        if (kit || count)
        {
            Add($"To shoot, {b.FormatPress("Attack")}.", TutorialAction.Attack);
            Add($"To shoot diagonally up, {b.FormatAimUp()} and {b.FormatPress("Attack")}.", TutorialAction.AimShot);
            Add($"For a charged shot, {b.FormatHold("Attack")}, then let go.", TutorialAction.ChargeAttack);
        }
        else if (malice)
        {
            Add($"To slash, {b.FormatPress("Attack")}.", TutorialAction.Attack);
            Add($"To air slash, {b.FormatPress("Attack")} in the air.", TutorialAction.AirAttack);
            Add($"To dive, air slash, then {b.FormatAimDown()}.", TutorialAction.Dive);
            Add($"To throw your Grapple Arm, {b.FormatHold("Attack")}, then let go.", TutorialAction.ChargeAttack);
            Add($"To pull yourself to your Grapple Arm, {b.FormatAimUp()} while it's out.", TutorialAction.GrapplePull);
            Add($"To grab enemies, {b.FormatAimDown()} as you let go of {b.FormatButton("Attack")}.", TutorialAction.GrappleGrab);
        }
        else if (harlieLike)
        {
            Add($"To swing your sword, {b.FormatPress("Attack")}.", TutorialAction.Attack);
            Add($"To air slash, {b.FormatPress("Attack")} in the air.", TutorialAction.AirAttack);
            Add($"For a charge attack, {b.FormatHold("Attack")}, then let go.", TutorialAction.ChargeAttack);
            if (hex)
            {
                Add($"To dive stab, jump, then {b.FormatAimDown()} and {b.FormatPress("Attack")}.", TutorialAction.DiveStab);
                Add($"To call your V-Bots, {b.FormatAimUp()} and {b.FormatPress("Attack")}.", TutorialAction.VBots);
            }
            Add("To pogo, fall onto an enemy with your sword out.", TutorialAction.Pogo);
        }

        if (count)
        {
            Add($"To start Hyper Speed, stand on the ground, {b.FormatAimDown()} and {b.FormatHold("Dash")}.",
                () => Player is CountPlayerController c && c.IsHyperSpeedActive);
            Add($"To stop Hyper Speed, {b.FormatAimDown()} and {b.FormatHold("Dash")} again.",
                () => Player is CountPlayerController c && !c.IsHyperSpeedActive);
        }

        Add($"To drop through a thin platform, stand on it, {b.FormatAimDown()} and {b.FormatPress("Jump")}.",
            TutorialAction.DropThrough);
        Add($"To wall jump, slide down a wall and {b.FormatPress("Jump")}.", TutorialAction.WallJump);

        Add("To carry an item, break an item box and touch the item.", TutorialAction.ItemPickup,
            () => HeldItemCarrier.HeldCount > 0, () => !ItemSourcesLeft());
        Add($"To use your item, {b.FormatPress("Use Item")}.", TutorialAction.ItemUse,
            null, () => HeldItemCarrier.HeldCount == 0 && !ItemSourcesLeft());
    }

    private void Add(string text, TutorialAction action, Func<bool> done = null, Func<bool> skip = null)
    {
        steps.Add(new Step { Text = text, Action = action, Done = done, Skip = skip });
    }

    private void Add(string text, Func<bool> done)
    {
        steps.Add(new Step { Text = text, Done = done });
    }

    private static PlayerController Player => PlayerController.ResolveActive();

    private static bool Is(string characterId, string expected) =>
        string.Equals(characterId, expected, StringComparison.OrdinalIgnoreCase);

    private void LateUpdate()
    {
        if (finished || stepIndex != 0)
            return;

        PlayerController player = Player;
        if (player != null && !player.InputLocked && Mathf.Abs(player.MoveInput.x) > 0.4f &&
            Mathf.Abs(player.Velocity.x) > 0.5f)
            moveSeconds += Time.deltaTime;
    }

    /// <summary>False once every item box is broken and no usable item is lying around.</summary>
    private bool ItemSourcesLeft()
    {
        if (Time.time < nextItemSourceCheck)
            return itemSourcesLeft;

        nextItemSourceCheck = Time.time + 1f;
        itemSourcesLeft = FindFirstObjectByType<RandomItemBox>() != null || FindFirstObjectByType<UsableItem>() != null;
        return itemSourcesLeft;
    }

    // ---------- Box ----------

    private bool CreateBox()
    {
        GameObject prefab = Resources.Load<GameObject>(BoxResourceName);
        if (prefab == null)
        {
            Debug.LogWarning($"[TutorialLevelGuide] Missing Resources/{BoxResourceName} prefab.");
            return false;
        }

        boxInstance = Instantiate(prefab);
        canvas = boxInstance.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Destroy(boxInstance);
            return false;
        }

        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            canvas.worldCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        canvasGroup = canvas.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 1f;

        nameText = FindText("Name of speaker");
        bodyText = FindText("Dialouge of Speaker");
        Transform image = FindDeep(canvas.transform, "Image");
        boxImage = image as RectTransform;

        if (nameText != null)
            nameText.text = speakerName;
        if (bodyText != null)
        {
            bodyText.text = string.Empty;
            bodyText.fontSizeMax = bodyText.fontSize;
            bodyText.fontSizeMin = Mathf.Min(18f, bodyText.fontSize);
            bodyText.enableAutoSizing = true;
        }

        Canvas.ForceUpdateCanvases();
        MoveBoxToTop();
        return bodyText != null;
    }

    /// <summary>The NPC box sits mid-screen; during play it goes to the top so it covers less of the action.</summary>
    private void MoveBoxToTop()
    {
        RectTransform canvasRect = canvas.transform as RectTransform;
        if (boxImage == null || canvasRect == null)
            return;

        float boxHalfHeight = boxImage.rect.height * Mathf.Abs(boxImage.localScale.y) * 0.5f;
        float targetY = canvasRect.rect.height * 0.5f - topMargin - boxHalfHeight;
        float shift = targetY - boxImage.anchoredPosition.y;
        for (int i = 0; i < canvas.transform.childCount; i++)
        {
            if (canvas.transform.GetChild(i) is RectTransform child)
                child.anchoredPosition += new Vector2(0f, shift);
        }
    }

    private TMP_Text FindText(string objectName)
    {
        Transform t = FindDeep(canvas.transform, objectName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null)
            return null;
        if (string.Equals(root.name, objectName, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void TypeText(string text)
    {
        if (typingRoutine != null)
            StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeRoutine(text));
    }

    /// <summary>Reveals characters with maxVisibleCharacters so rich-text button colors never show as raw tags.</summary>
    private IEnumerator TypeRoutine(string text)
    {
        if (bodyText == null)
            yield break;

        bodyText.text = text;
        bodyText.maxVisibleCharacters = 0;
        bodyText.ForceMeshUpdate();
        int total = bodyText.textInfo.characterCount;

        SoundManager.Instance?.BeginDialogueTyping();
        float delay = 1f / Mathf.Max(1f, charactersPerSecond);
        for (int i = 1; i <= total; i++)
        {
            bodyText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(delay);
        }

        bodyText.maxVisibleCharacters = int.MaxValue;
        SoundManager.Instance?.EndDialogueTyping();
        typingRoutine = null;
    }

    // ---------- Fade while covering enemies ----------

    private void UpdateCoverFade()
    {
        if (canvasGroup == null || closing)
            return;

        if (Time.unscaledTime >= nextCoverCheck)
        {
            nextCoverCheck = Time.unscaledTime + 0.1f;
            covering = BoxCoversAnything();
        }

        float target = covering ? Mathf.Clamp01(coveredAlpha) : 1f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, Time.unscaledDeltaTime * Mathf.Max(0.1f, fadeSpeed));
    }

    private bool BoxCoversAnything()
    {
        Camera worldCamera = Camera.main;
        if (boxImage == null || worldCamera == null)
            return false;

        boxImage.GetWorldCorners(corners);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        Rect boxRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        CollectCoverBounds();
        for (int i = 0; i < coverBounds.Count; i++)
        {
            Bounds bounds = coverBounds[i];
            Vector2 min = worldCamera.WorldToScreenPoint(bounds.min);
            Vector2 max = worldCamera.WorldToScreenPoint(bounds.max);
            Rect r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            if (r.Overlaps(boxRect))
                return true;
        }

        return false;
    }

    private void CollectCoverBounds()
    {
        coverBounds.Clear();

        PlayerController player = Player;
        if (player != null && player.BodyCollider != null)
            coverBounds.Add(player.BodyCollider.bounds);

        AddEnemies(EnemyTypeCache.Crankies);
        AddEnemies(EnemyTypeCache.Lasers);
        AddEnemies(EnemyTypeCache.Blockers);
        AddEnemies(EnemyTypeCache.Tankers);
        AddEnemies(EnemyTypeCache.Scraps);

        Boss[] bosses = EnemyTypeCache.Bosses;
        if (bosses != null)
        {
            for (int i = 0; i < bosses.Length; i++)
            {
                Boss boss = bosses[i];
                if (boss != null && boss.isActiveAndEnabled && !boss.IsDead)
                    AddBounds(boss);
            }
        }

        Projectile[] shots = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < shots.Length; i++)
        {
            Projectile shot = shots[i];
            if (shot != null && shot.Owner != null && shot.Owner.GetComponentInParent<PlayerController>() == null)
                AddBounds(shot);
        }
    }

    private void AddEnemies<T>(T[] enemies) where T : MonoBehaviour
    {
        if (enemies == null)
            return;

        for (int i = 0; i < enemies.Length; i++)
        {
            T enemy = enemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;
            if (enemy is ICommonEnemy common && common.CurrentHealth <= 0)
                continue;

            AddBounds(enemy);
        }
    }

    private void AddBounds(Component c)
    {
        Collider2D col = c.GetComponent<Collider2D>();
        if (col != null && col.enabled)
        {
            coverBounds.Add(col.bounds);
            return;
        }

        Renderer r = c.GetComponentInChildren<Renderer>();
        if (r != null && r.enabled)
            coverBounds.Add(r.bounds);
    }
}
