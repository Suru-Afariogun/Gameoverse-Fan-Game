using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full-screen black fade. Survives scene loads. Uses unscaled time (works while paused).
/// Queues a second LoadScene if one is already in progress (never drops the request).
/// </summary>
public class ScreenFade : MonoBehaviour
{
    private static ScreenFade instance;
    private CanvasGroup group;
    private bool busy;
    private string queuedSceneName;
    private float queuedFadeOut = 0.35f;
    private float queuedFadeIn = 0.35f;

    public static bool IsBusy => instance != null && instance.busy;

    public static ScreenFade EnsureExists()
    {
        if (instance != null)
            return instance;

        GameObject go = new GameObject("ScreenFade");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenFade>();
        instance.Build();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        if (group == null)
            Build();
    }

    private void Build()
    {
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        group = gameObject.GetComponent<CanvasGroup>();
        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;

        Transform existing = transform.Find("FadeImage");
        GameObject imgGo = existing != null ? existing.gameObject : new GameObject("FadeImage");
        if (existing == null)
            imgGo.transform.SetParent(transform, false);

        Image img = imgGo.GetComponent<Image>();
        if (img == null)
            img = imgGo.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public IEnumerator FadeToBlack(float duration)
    {
        EnsureGroup();
        float t = 0f;
        float start = group.alpha;
        duration = Mathf.Max(0.01f, duration);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, 1f, t / duration);
            yield return null;
        }

        group.alpha = 1f;
    }

    public IEnumerator FadeFromBlack(float duration)
    {
        EnsureGroup();
        float t = 0f;
        float start = group.alpha;
        duration = Mathf.Max(0.01f, duration);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }

        group.alpha = 0f;
    }

    public void SetBlackImmediate()
    {
        EnsureGroup();
        group.alpha = 1f;
    }

    public void SetClearImmediate()
    {
        EnsureGroup();
        group.alpha = 0f;
    }

    public void LoadScene(string sceneName, float fadeOut = 0.35f, float fadeIn = 0.35f)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        if (busy)
        {
            // Do not drop return-to-HomeTown (or any follow-up load) during an in-progress fade.
            queuedSceneName = sceneName.Trim();
            queuedFadeOut = fadeOut;
            queuedFadeIn = fadeIn;
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneName.Trim(), fadeOut, fadeIn));
    }

    private IEnumerator LoadSceneRoutine(string sceneName, float fadeOut, float fadeIn)
    {
        busy = true;
        Time.timeScale = 1f;
        yield return FadeToBlack(fadeOut);
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (op != null && !op.isDone)
            yield return null;
        yield return null;
        yield return FadeFromBlack(fadeIn);
        busy = false;

        if (!string.IsNullOrWhiteSpace(queuedSceneName))
        {
            string next = queuedSceneName;
            float nextOut = queuedFadeOut;
            float nextIn = queuedFadeIn;
            queuedSceneName = null;
            LoadScene(next, nextOut, nextIn);
        }
    }

    private void EnsureGroup()
    {
        if (group == null)
            Build();
    }
}
