using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RacingHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text distanceText;

    [Tooltip("Optional. Shows the race position, e.g. 2 / 3.")]
    [SerializeField] private TMP_Text positionText;

    [Tooltip("Optional. An Image set to Filled that shows the nitro tank.")]
    [SerializeField] private Image nitroFill;

    private NitroBoost nitro;

    [Header("Display")]
    [SerializeField] private bool displaySpeedInKmh = true;

    private void Awake()
    {
        // Nothing to do: the human player is resolved in Update,
        // because it may not be enabled yet when this runs.
    }

    private void Update()
    {
        // Always show the human player, never an AI car.
        if (playerController == null)
            playerController = PlayerController.HumanPlayer;

        if (playerController == null)
            return;

        UpdateSpeed();
        UpdateDistance();
        UpdatePosition();
        UpdateNitro();
    }

    private void UpdateNitro()
    {
        if (nitroFill == null)
            return;

        if (nitro == null)
            nitro = playerController.GetComponent<NitroBoost>();

        nitroFill.fillAmount = nitro != null ? nitro.Charge01 : 0f;
    }

    private void UpdatePosition()
    {
        if (positionText == null)
            return;

        int rank = 1;
        int total = 0;

        foreach (PlayerController racer in PlayerController.AllRacers)
        {
            if (racer == null)
                continue;

            total++;

            if (racer != playerController &&
                racer.TrackDistance > playerController.TrackDistance)
            {
                rank++;
            }
        }

        positionText.text = $"{rank} / {total}";
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