using UnityEngine;

public class PlayerCollision : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [Tooltip("Created automatically if left empty and not found in the scene.")]
    [SerializeField] private CollisionFeedback feedback;

    [Header("Separation")]
    [Tooltip("Desired distance between cars after a rear impact.")]
    [SerializeField] private float minimumTrackGap = 5.5f;
    [SerializeField] private float extraForwardPush = 1.5f;
    [SerializeField] private float maxTrackGapForSeparation = 8f;
    [SerializeField] private float collisionCooldown = 0.6f;

    [Header("Severity")]
    [Tooltip("Closing speed (m/s) that counts as a 100% severe hit.")]
    [SerializeField] private float maxClosingSpeed = 20f;
    [SerializeField, Range(0f, 1f)] private float minimumSeverity = 0.25f;

    [Header("Side Impact")]
    [SerializeField] private float sideImpactTrackDistance = 3.5f;
    [Tooltip("Share of speed kept after a light side swipe.")]
    [SerializeField, Range(0.1f, 1f)] private float sideSpeedKeepLight = 0.85f;
    [Tooltip("Share of speed kept after a heavy side swipe.")]
    [SerializeField, Range(0.1f, 1f)] private float sideSpeedKeepHeavy = 0.55f;

    [Header("Rear-End Momentum")]
    [Tooltip("0 = player keeps speed, 1 = player drops to the traffic's speed.")]
    [SerializeField, Range(0f, 1f)] private float rearSpeedTransfer = 0.75f;
    [SerializeField, Range(0.1f, 1f)] private float rearSpeedKeepHeavy = 0.6f;
    [Tooltip("Share of the closing speed passed on to the car that was hit.")]
    [SerializeField, Range(0f, 1f)] private float trafficMomentumTransfer = 0.7f;
    [Tooltip("Share of the closing speed gained when the player is hit from behind.")]
    [SerializeField, Range(0f, 1f)] private float playerMomentumGain = 0.5f;

    private float nextCollisionTime;

    private void Awake()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (feedback == null)
            feedback = FindFirstObjectByType<CollisionFeedback>();

        if (feedback == null)
        {
            feedback = new GameObject("CollisionFeedback")
                .AddComponent<CollisionFeedback>();
        }
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

        float playerSpeed = playerController.CurrentForwardSpeed;
        float trafficSpeed = traffic.ForwardSpeed;

        Vector3 contactPoint = GetContactPoint(other, traffic);
        Vector3 worldVelocity = transform.forward * playerSpeed;

        float pushDirection = GetPlayerPushDirection(traffic);

        bool isSideImpact =
            playerController.IsChangingLane &&
            Mathf.Abs(trackGap) <= sideImpactTrackDistance;

        if (isSideImpact)
        {
            HandleSideImpact(
                traffic, trackGap, playerSpeed, trafficSpeed,
                pushDirection, contactPoint, worldVelocity);
            return;
        }

        HandleRearImpact(
            traffic, trackGap, playerSpeed, trafficSpeed,
            pushDirection, contactPoint, worldVelocity);
    }

    private void HandleSideImpact(
        TrafficVehicle traffic,
        float trackGap,
        float playerSpeed,
        float trafficSpeed,
        float pushDirection,
        Vector3 contactPoint,
        Vector3 worldVelocity)
    {
        // Closing speed = lateral approach combined with speed difference.
        float lateralClosing = playerController.LaneChangeSpeed;
        float forwardRelative = Mathf.Abs(playerSpeed - trafficSpeed);
        float closing = Mathf.Sqrt(
            lateralClosing * lateralClosing +
            forwardRelative * forwardRelative);

        float severity = ComputeSeverity(closing);

        // Contact is slightly in front of or behind the car's middle.
        float longitudinal = trackGap >= 0f ? 0.4f : -0.4f;

        ImpactInfo playerInfo = new ImpactInfo
        {
            severity = severity,
            closingSpeed = closing,
            pushDirection = pushDirection,
            longitudinal = longitudinal,
            isSide = true,
            worldPoint = contactPoint,
            worldVelocity = worldVelocity
        };

        ImpactInfo trafficInfo = playerInfo;
        trafficInfo.pushDirection = -pushDirection;
        trafficInfo.longitudinal = -longitudinal;

        float keep = Mathf.Lerp(
            sideSpeedKeepLight, sideSpeedKeepHeavy, severity);

        playerController.ApplyImpact(playerInfo, playerSpeed * keep);

        traffic.ApplyImpact(
            trafficInfo,
            0f,
            Mathf.Lerp(0.95f, 0.75f, severity));

        feedback.Play(playerInfo);
    }

    private void HandleRearImpact(
        TrafficVehicle traffic,
        float trackGap,
        float playerSpeed,
        float trafficSpeed,
        float pushDirection,
        Vector3 contactPoint,
        Vector3 worldVelocity)
    {
        bool trafficAhead = trackGap >= 0f;
        bool withinSeparationRange =
            Mathf.Abs(trackGap) <= maxTrackGapForSeparation;

        float closing = trafficAhead
            ? Mathf.Max(0f, playerSpeed - trafficSpeed)
            : Mathf.Max(0f, trafficSpeed - playerSpeed);

        float severity = ComputeSeverity(closing);

        ImpactInfo playerInfo = new ImpactInfo
        {
            severity = severity,
            closingSpeed = closing,
            pushDirection = pushDirection,
            longitudinal = trafficAhead ? 1f : -1f,
            isSide = false,
            worldPoint = contactPoint,
            worldVelocity = worldVelocity
        };

        ImpactInfo trafficInfo = playerInfo;
        trafficInfo.pushDirection = -pushDirection;
        trafficInfo.longitudinal = -playerInfo.longitudinal;

        if (trafficAhead)
        {
            // Player runs into the back of a car:
            // speeds converge (inelastic), traffic gets a forward kick.
            float newSpeed =
                Mathf.Lerp(playerSpeed, trafficSpeed, rearSpeedTransfer) *
                Mathf.Lerp(0.9f, rearSpeedKeepHeavy, severity);

            playerController.ApplyImpact(
                playerInfo, Mathf.Min(playerSpeed, newSpeed));

            traffic.ApplyImpact(
                trafficInfo,
                closing * trafficMomentumTransfer,
                1f);

            if (withinSeparationRange)
            {
                traffic.ShoveForwardTo(
                    playerController.TrackDistance +
                    minimumTrackGap +
                    extraForwardPush);
            }
        }
        else
        {
            // Traffic rams the player from behind:
            // player is jolted forward, traffic loses speed.
            playerController.ApplyImpact(
                playerInfo,
                playerSpeed + closing * playerMomentumGain);

            traffic.ApplyImpact(
                trafficInfo,
                0f,
                Mathf.Lerp(0.9f, 0.6f, severity));

            if (withinSeparationRange)
            {
                playerController.ShoveForwardTo(
                    traffic.TrackDistance + minimumTrackGap);
            }
        }

        feedback.Play(playerInfo);
    }

    private float ComputeSeverity(float closingSpeed)
    {
        float severity =
            Mathf.Clamp01(closingSpeed / Mathf.Max(0.1f, maxClosingSpeed));

        return Mathf.Max(severity, minimumSeverity);
    }

    /// <summary>Which way (-1 left / +1 right) the player is shoved.</summary>
    private float GetPlayerPushDirection(TrafficVehicle traffic)
    {
        float difference =
            playerController.LateralOffset - traffic.LateralOffset;

        if (Mathf.Abs(difference) > 0.15f)
            return Mathf.Sign(difference);

        Vector3 local =
            transform.InverseTransformPoint(traffic.transform.position);

        if (Mathf.Abs(local.x) > 0.05f)
            return local.x >= 0f ? -1f : 1f;

        return Random.value < 0.5f ? -1f : 1f;
    }

    private Vector3 GetContactPoint(Collider other, TrafficVehicle traffic)
    {
        Vector3 midpoint =
            (transform.position + traffic.transform.position) * 0.5f;

        bool supportsClosestPoint =
            other is BoxCollider ||
            other is SphereCollider ||
            other is CapsuleCollider ||
            (other is MeshCollider mesh && mesh.convex);

        if (!supportsClosestPoint)
            return midpoint;

        Vector3 point = other.ClosestPoint(transform.position);

        return (point - transform.position).sqrMagnitude < 0.0001f
            ? midpoint
            : point;
    }
}