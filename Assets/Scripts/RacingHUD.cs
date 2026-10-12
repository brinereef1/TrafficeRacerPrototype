using TMPro;
using UnityEngine;

public class RacingHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text distanceText;

    [Header("Display")]
    [SerializeField] private bool displaySpeedInKmh = true;

    private void Awake()
    {
        // Find the player if it was not assigned in the Inspector.
        if (playerController == null)
        {
            playerController =
                FindFirstObjectByType<PlayerController>();
        }
    }

    private void Update()
    {
        if (playerController == null)
            return;

        UpdateSpeed();
        UpdateDistance();
    }

    private void UpdateSpeed()
    {
        float speed = playerController.CurrentForwardSpeed;

        // Convert from Unity units to km/h when needed.
        if (displaySpeedInKmh)
        {
            speed *= 3.6f;
            speedText.text = $"{speed:0} km/h";
        }
        else
        {
            speedText.text = $"{speed:0.0} m/s";
        }
    }

    private void UpdateDistance()
    {
        float distance = playerController.DistanceTravelled;

        distanceText.text =
            $"{Mathf.FloorToInt(distance):N0} m";
    }
}