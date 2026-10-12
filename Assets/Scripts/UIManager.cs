
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject racingHUDPanel;

    [Header("Title Animation")]
    [SerializeField] private RectTransform titleImage;
    [SerializeField] private float minTitleScale = 0.95f;
    [SerializeField] private float maxTitleScale = 1.05f;

    [Tooltip("Duration of one complete zoom in and out cycle.")]
    [SerializeField] private float titlePulseDuration = 2f;

    private static bool bypassStartPanelOnNextLoad;

    private bool previousGameplayActive;
    private bool hasSyncedGameplayState;
    private Coroutine titleAnimationCoroutine;

    private void Awake()
    {
        bool bypassStartPanel = bypassStartPanelOnNextLoad;
        bypassStartPanelOnNextLoad = false;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (startPanel != null)
            startPanel.SetActive(!bypassStartPanel);

        // The first launch starts paused. A restart starts immediately.
        Time.timeScale = bypassStartPanel ? 1f : 0f;
    }

    private void Start()
    {
        // Run the title pulse only while the menu is visible.
        if (startPanel != null &&
            startPanel.activeInHierarchy &&
            titleImage != null)
        {
            titleAnimationCoroutine =
                StartCoroutine(AnimateTitle());
        }
    }

    private IEnumerator AnimateTitle()
    {
        float elapsed = 0f;

        while (startPanel != null &&
               startPanel.activeInHierarchy &&
               titleImage != null)
        {
            elapsed += Time.unscaledDeltaTime;

            float cycleDuration =
                Mathf.Max(0.01f, titlePulseDuration);

            float pulse = (
                1f - Mathf.Cos(
                    2f * Mathf.PI * elapsed / cycleDuration
                )
            ) * 0.5f;

            float scale = Mathf.Lerp(
                minTitleScale,
                maxTitleScale,
                pulse
            );

            titleImage.localScale = Vector3.one * scale;

            yield return null;
        }

        if (titleImage != null)
            titleImage.localScale = Vector3.one;

        titleAnimationCoroutine = null;
    }

    public void StartGame()
    {
        StopTitleAnimation();

        if (startPanel != null)
            startPanel.SetActive(false);

        Time.timeScale = 1f;
    }

    private void StopTitleAnimation()
    {
        if (titleAnimationCoroutine != null)
        {
            StopCoroutine(titleAnimationCoroutine);
            titleAnimationCoroutine = null;
        }

        if (titleImage != null)
            titleImage.localScale = Vector3.one;
    }

    public void RestartGame()
    {
        // Skip the start menu on the next restart.
        bypassStartPanelOnNextLoad = true;

        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    private void LateUpdate()
    {
        bool gameplayActive =
            (startPanel == null ||
             !startPanel.activeInHierarchy) &&
            (gameOverPanel == null ||
             !gameOverPanel.activeInHierarchy);

        // Show the HUD only while the player is playing.
        if (racingHUDPanel != null &&
            racingHUDPanel.activeSelf != gameplayActive)
        {
            racingHUDPanel.SetActive(gameplayActive);
        }

        // Track when the play state changes.
        if (!hasSyncedGameplayState ||
            gameplayActive != previousGameplayActive)
        {
            previousGameplayActive = gameplayActive;
            hasSyncedGameplayState = true;
        }
    }

    public void ExitGame()
    {
#if UNITY_EDITOR
        // Stop the editor while in play mode.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // Quit the game in a build.
        Application.Quit();
#endif
    }
}
