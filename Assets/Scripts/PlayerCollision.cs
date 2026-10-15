
using UnityEngine;

public class PlayerCollision : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;

    [Header("Collision Separation")]
    [Tooltip("Desired distance between cars after an impact.")]
    [SerializeField] private float minimumTrackGap = 5.5f;

    [Tooltip("Extra distance to shove a vehicle hit from behind.")]
    [SerializeField] private float extraForwardPush = 1.5f;

    [Tooltip("Maximum track-distance difference for resolving contact.")]
    [SerializeField] private float maxTrackGapForSeparation = 8f;

    [SerializeField] private float collisionCooldown = 0.6f;

    private float nextCollisionTime;

    [Header("Side Impact Detection")]
    [SerializeField] private float sideImpactTrackDistance = 3.5f;

    [SerializeField, Range(0.1f, 1f)]
    private float sideImpactSpeedMultiplier = 0.25f;

    private void Awake()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time < nextCollisionTime)
            return;

        TrafficVehicle traffic =
            other.GetComponentInParent<TrafficVehicle>();

        if (traffic == null || playerController == null)
            return;

        nextCollisionTime =
            Time.time + Mathf.Max(0.1f, collisionCooldown);

        float trackGap =
            traffic.TrackDistance - playerController.TrackDistance;

        // SIDE IMPACT:
        // The player is attempting to enter another vehicle's lane
        // while the cars are side by side.
        bool isSideImpact =
            playerController.IsChangingLane &&
            Mathf.Abs(trackGap) <= sideImpactTrackDistance;

        if (isSideImpact)
        {
            // Positive means the player is farther to the right
            // in road-space coordinates.
            float lateralDifference =
                playerController.LateralOffset -
                traffic.LateralOffset;

            float playerPushDirection;

            if (Mathf.Abs(lateralDifference) > 0.05f)
            {
                playerPushDirection = Mathf.Sign(lateralDifference);
            }
            else
            {
                // Fallback if the cars have nearly identical
                // lateral positions.
                Vector3 trafficLocalPosition =
                    transform.InverseTransformPoint(
                        traffic.transform.position
                    );

                playerPushDirection =
                    trafficLocalPosition.x >= 0f ? -1f : 1f;
            }

            // Player returns toward their original lane and
            // both cars recoil in opposite directions.
            playerController.ApplySideTrafficImpact(
                playerPushDirection,
                sideImpactSpeedMultiplier
            );

            traffic.ApplySideImpact(
                -playerPushDirection,
                sideImpactSpeedMultiplier
            );

            Debug.Log("Side impact: both vehicles pushed apart.");
            return;
        }

        // REAR-END / FRONT-BACK IMPACT:
        // Keep the existing forward-push behavior.
        Vector3 awayFromTraffic =
            transform.position - traffic.transform.position;

        awayFromTraffic.y = 0f;

        float rearImpactDirection =
            Vector3.Dot(awayFromTraffic, transform.right) >= 0f
                ? 1f
                : -1f;

        playerController.ApplyTrafficImpact(rearImpactDirection);

        traffic.ApplyCollisionVisual(-rearImpactDirection);

        if (trackGap >= 0f &&
            trackGap <= maxTrackGapForSeparation)
        {
            traffic.PushForwardAfterPlayerImpact(
                playerController.TrackDistance,
                minimumTrackGap,
                extraForwardPush
            );
        }
        else if (trackGap < 0f &&
                Mathf.Abs(trackGap) <= maxTrackGapForSeparation)
        {
            playerController.SeparateFromTraffic(
                traffic.TrackDistance + minimumTrackGap
            );

            traffic.SeparateBehindPlayer(
                playerController.TrackDistance,
                minimumTrackGap
            );
        }

        Debug.Log("Rear-end collision response applied.");
    }
}
