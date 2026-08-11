using UnityEngine;
using UnityEngine.UI;

public class StartScreen : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private string homeTownSceneName = "HomeTown";

    private bool loading;

    private void Awake()
    {
        if (startButton != null)
            startButton.onClick.AddListener(LoadHomeTown);
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(LoadHomeTown);
    }

    public void LoadHomeTown()
    {
        if (loading)
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
