using System.Collections;
using UnityEngine;

public class PlayerCollision : MonoBehaviour
{
    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private RectTransform gameOverImage;

    [Header("Zoom Animation")]
    [SerializeField] private float zoomDuration = 0.6f;
    [SerializeField] private float startScale = 0.1f;
    
    private bool hasCrashed;

    private void Awake()
    {
        // Hide the game over panel at the start.
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasCrashed)
            return;

        // Check if the collision hit a traffic car.
        TrafficVehicle traffic =
            other.GetComponentInParent<TrafficVehicle>();

        if (traffic == null)
            return;

        hasCrashed = true;
        
        Debug.Log("GAME OVER! Player collided with traffic.");

        // Pause the game.
        Time.timeScale = 0f;

        if (gameOverPanel == null || gameOverImage == null)
        {
            Debug.LogWarning(
                "PlayerCollision: Assign Game Over Panel " +
                "and Game Over Image in the Inspector.",
                this
            );

            return;
        }

        // Show the panel and start the zoom effect small.
        gameOverPanel.SetActive(true);
        gameOverImage.localScale =
            Vector3.one * startScale;

        // Play the game over zoom while the game is paused.
        StartCoroutine(ZoomInGameOver());
    }

    private IEnumerator ZoomInGameOver()
    {
        float elapsed = 0f;

        while (elapsed < zoomDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(
                elapsed / zoomDuration
            );

            // Ease the scale out smoothly.
            float easedProgress =
                1f - Mathf.Pow(1f - progress, 3f);

            float scale = Mathf.Lerp(
                startScale,
                1f,
                easedProgress
            );

            gameOverImage.localScale =
                Vector3.one * scale;

            yield return null;
        }

        // Keep the image at full size at the end.
        gameOverImage.localScale = Vector3.one;
    }
}