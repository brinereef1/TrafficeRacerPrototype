
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadManager roadManager;

    [Header("Movement")]
    [SerializeField] private float forwardSpeed = 20f;
    [SerializeField] private float laneChangeSpeed = 8f;
    [SerializeField] private float carHeightOffset = 0.5f;

    [Header("Curve Speed")]
    [Tooltip("Amount of speed to reduce while driving through a curve.")]
    [SerializeField] private float curveSpeedReduction = 10f;

    [Tooltip("Distance ahead to check for a change in road direction.")]
    [SerializeField] private float curveCheckDistance = 8f;

    [Tooltip("Minimum road direction change needed to detect a curve.")]
    [SerializeField] private float curveAngleThreshold = 3f;

    [Tooltip("How quickly the car changes speed.")]
    [SerializeField] private float speedChangeRate = 5f;

    
    [Header("Collision Response")]
    [SerializeField, Range(0.1f, 1f)]
    private float speedMultiplierAfterCollision = 0.25f;

    [SerializeField] private float collisionLaneLockDuration = 0.6f;
    [SerializeField] private float collisionRotationRecovery = 25f;
    [SerializeField] private float collisionYawAngle = 3f;
    [SerializeField] private float collisionRollAngle = 5f;

    private float laneInputLockedUntil;
    private float collisionYawOffset;
    private float collisionRollOffset;

    [Header("Side Collision")]
    [SerializeField] private float sideImpactKickSpeed = 3f;
    [SerializeField] private float sideImpactSpringStrength = 30f;
    [SerializeField] private float sideImpactDamping = 9f;
    [SerializeField] private float maxSideImpactOffset = 0.6f;

    private float sideImpactOffset;
    private float sideImpactVelocity;

    public float LateralOffset => lateralOffset;
    private int previousLane;   
    private float currentForwardSpeed;
    private float trackDistance;
    private float lateralOffset;
    private int currentLane;
    
    public float TrackDistance => trackDistance;
    public int CurrentLane => currentLane;
    public float CurrentForwardSpeed => currentForwardSpeed;
    public float DistanceTravelled => trackDistance;

    public bool IsChangingLane =>
        roadManager != null &&
        Mathf.Abs(
            lateralOffset - roadManager.GetLaneOffset(currentLane)
        ) > 0.05f;

    public bool OccupiesLane(int lane)
    {
        if (currentLane == lane)
            return true;

        return IsChangingLane && previousLane == lane;
    }

    private void Start()
    {
        if (roadManager == null)
        {
            Debug.LogError(
                "PlayerController: Assign the RoadManager!",
                this
            );

            enabled = false;
            return;
        }

        // Set the starting distance and lane.
        trackDistance = 0f;

        currentForwardSpeed = 5f;

        currentLane = (roadManager.LaneCount - 1) / 2;
        previousLane = currentLane;
        lateralOffset = roadManager.GetLaneOffset(currentLane);

        // Tell the road where the player starts.
        roadManager.PlayerDistance = trackDistance;

        UpdatePlayerPose();
    }


    private void Update()
    {
        if (Time.time >= laneInputLockedUntil)
        {
            HandleLaneInput();
        }

        // Change the speed if the road is turning.
        float targetSpeed = IsRoadCurving()
            ? Mathf.Max(0f, forwardSpeed - curveSpeedReduction)
            : forwardSpeed;

        // Ease the speed up or down.
        currentForwardSpeed = Mathf.MoveTowards(
            currentForwardSpeed,
            targetSpeed,
            speedChangeRate * Time.deltaTime
        );

        // Move forward along the road.
        trackDistance += currentForwardSpeed * Time.deltaTime;

        // Move toward the chosen lane.
        float targetOffset =
            roadManager.GetLaneOffset(currentLane);

        lateralOffset = Mathf.MoveTowards(
            lateralOffset,
            targetOffset,
            laneChangeSpeed * Time.deltaTime
        );

        // Send the current player position to the road.
        roadManager.PlayerDistance = trackDistance;

        RecoverCollisionRotation(Time.deltaTime);
        UpdatePlayerPose();
    }


    private bool IsRoadCurving()
    {
        // Do not look past the end of the generated road.
        float sampleDistance = Mathf.Min(
            trackDistance + curveCheckDistance,
            roadManager.GeneratedEndDistance - 0.1f
        );

        // Stop if there is no road left to inspect.
        if (sampleDistance <= trackDistance)
            return false;

        // Read the road direction at the current and future points.
        roadManager.GetPose(
            trackDistance,
            0f,
            out _,
            out Quaternion currentRotation
        );

        roadManager.GetPose(
            sampleDistance,
            0f,
            out _,
            out Quaternion futureRotation
        );

        // Ignore any upward slope and only check the road direction.
        Vector3 currentForward = Vector3.ProjectOnPlane(
            currentRotation * Vector3.forward,
            Vector3.up
        ).normalized;

        Vector3 futureForward = Vector3.ProjectOnPlane(
            futureRotation * Vector3.forward,
            Vector3.up
        ).normalized;

        float directionChange = Vector3.Angle(
            currentForward,
            futureForward
        );

        return directionChange >= curveAngleThreshold;
    }

    private void HandleLaneInput()
    {
        int laneBeforeInput = currentLane;
        
        if (Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.LeftArrow))
        {
            currentLane--;
        }

        if (Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.RightArrow))
        {
            currentLane++;
        }

        currentLane = Mathf.Clamp(
            currentLane,
            0,
            roadManager.LaneCount - 1
        );

        if (currentLane != laneBeforeInput)
        {
            previousLane = laneBeforeInput;
        }
    }

    private void UpdatePlayerPose()
    {
        roadManager.GetPose(
            trackDistance,
            lateralOffset + sideImpactOffset,
            out Vector3 position,
            out Quaternion rotation
        );

        position += Vector3.up * carHeightOffset;

        // Apply temporary yaw and body roll on top of road rotation.
        rotation *= Quaternion.Euler(
            0f,
            collisionYawOffset,
            collisionRollOffset
        );

        transform.SetPositionAndRotation(position, rotation);
    }

    public void ApplyTrafficImpact(float sideDirection)
    {
        // If the player was moving into another lane,
        // return them to the previous, safer lane.
        if (IsChangingLane)
        {
            int rejectedLane = currentLane;

            currentLane = previousLane;
            previousLane = rejectedLane;

            // Snap to the safe lane center so the player
            // doesn't continue crossing the other car.
            lateralOffset = roadManager.GetLaneOffset(currentLane);
        }

        // Reduce speed by 75%.
        currentForwardSpeed *= speedMultiplierAfterCollision;

        // Briefly prevent immediate repeated lane changes.
        laneInputLockedUntil =
            Time.time + collisionLaneLockDuration;

        // Small visual impact reaction without sideways displacement.
        collisionYawOffset =
            -Mathf.Sign(sideDirection) * collisionYawAngle;

        collisionRollOffset =
            -Mathf.Sign(sideDirection) * collisionRollAngle;
    }

    public void SeparateFromTraffic(float minimumTrackDistance)
    {
        if (trackDistance >= minimumTrackDistance)
            return;

        trackDistance = minimumTrackDistance;
        roadManager.PlayerDistance = trackDistance;

        UpdatePlayerPose();
    }

    private void RecoverCollisionRotation(float deltaTime)
    {
        collisionYawOffset = Mathf.MoveTowards(
            collisionYawOffset,
            0f,
            collisionRotationRecovery * deltaTime
        );

        collisionRollOffset = Mathf.MoveTowards(
            collisionRollOffset,
            0f,
            collisionRotationRecovery * deltaTime
        );

        // Spring force pulls the lateral offset back toward zero.
        sideImpactVelocity +=
            -sideImpactOffset *
            sideImpactSpringStrength *
            deltaTime;

        // Damping prevents endless oscillation.
        sideImpactVelocity *=
            Mathf.Exp(-sideImpactDamping * deltaTime);

        sideImpactOffset +=
            sideImpactVelocity * deltaTime;

        sideImpactOffset = Mathf.Clamp(
            sideImpactOffset,
            -maxSideImpactOffset,
            maxSideImpactOffset
        );
    }

    public void ApplySideTrafficImpact(
        float lateralDirection,
        float speedMultiplier)
    {
        // Cancel the attempted lane change, but do not snap
        // the car instantly back to the lane center.
        if (IsChangingLane)
        {
            int rejectedLane = currentLane;
            currentLane = previousLane;
            previousLane = rejectedLane;
        }

        // Reduce speed to the requested percentage.
        currentForwardSpeed *= Mathf.Clamp01(speedMultiplier);

        laneInputLockedUntil =
            Time.time + collisionLaneLockDuration;

        // Push sideways, away from the other vehicle.
        sideImpactVelocity +=
            Mathf.Sign(lateralDirection) * sideImpactKickSpeed;

        collisionYawOffset =
            -Mathf.Sign(lateralDirection) * collisionYawAngle;

        collisionRollOffset =
            -Mathf.Sign(lateralDirection) * collisionRollAngle;
    }

}
