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
    [Tooltip("How hard the car brakes when hit (units/s per second).")]
    [SerializeField] private float impactDecelRate = 60f;

    [Tooltip("Speed the soft separation shove moves the car (units/s).")]
    [SerializeField] private float separationShoveSpeed = 30f;

    [SerializeField] private float collisionLaneLockDuration = 0.6f;

    [Tooltip("Lane return is faster right after an impact (bounce-off feel).")]
    [SerializeField] private float impactLaneReturnMultiplier = 1.5f;

    [SerializeField] private VehicleImpactBody impactBody =
        new VehicleImpactBody();

    private float laneInputLockedUntil;
    private float forcedSpeedTarget = -1f;
    private float shoveTargetDistance = float.MinValue;

    private int previousLane;
    private float currentForwardSpeed;
    private float trackDistance;
    private float lateralOffset;
    private int currentLane;

    public float LateralOffset => lateralOffset;
    public float LaneChangeSpeed => laneChangeSpeed;
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

        trackDistance = 0f;
        currentForwardSpeed = 5f;

        currentLane = (roadManager.LaneCount - 1) / 2;
        previousLane = currentLane;
        lateralOffset = roadManager.GetLaneOffset(currentLane);

        roadManager.PlayerDistance = trackDistance;

        UpdatePlayerPose();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (Time.time >= laneInputLockedUntil)
        {
            HandleLaneInput();
        }

        // Speed: either a hard impact deceleration, or normal driving.
        if (forcedSpeedTarget >= 0f)
        {
            currentForwardSpeed = Mathf.MoveTowards(
                currentForwardSpeed,
                forcedSpeedTarget,
                impactDecelRate * dt
            );

            if (Mathf.Approximately(
                    currentForwardSpeed, forcedSpeedTarget))
            {
                forcedSpeedTarget = -1f;
            }
        }
        else
        {
            float targetSpeed = IsRoadCurving()
                ? Mathf.Max(0f, forwardSpeed - curveSpeedReduction)
                : forwardSpeed;

            currentForwardSpeed = Mathf.MoveTowards(
                currentForwardSpeed,
                targetSpeed,
                speedChangeRate * dt
            );
        }

        trackDistance += currentForwardSpeed * dt;

        // Soft separation: shove the car forward instead of teleporting.
        if (shoveTargetDistance > trackDistance)
        {
            trackDistance = Mathf.MoveTowards(
                trackDistance,
                shoveTargetDistance,
                separationShoveSpeed * dt
            );
        }

        // Move toward the chosen lane.
        float targetOffset = roadManager.GetLaneOffset(currentLane);

        float laneSpeed = laneChangeSpeed;

        if (Time.time < laneInputLockedUntil)
            laneSpeed *= impactLaneReturnMultiplier;

        lateralOffset = Mathf.MoveTowards(
            lateralOffset,
            targetOffset,
            laneSpeed * dt
        );

        roadManager.PlayerDistance = trackDistance;

        impactBody.Tick(dt);
        UpdatePlayerPose();
    }

    private bool IsRoadCurving()
    {
        float sampleDistance = Mathf.Min(
            trackDistance + curveCheckDistance,
            roadManager.GeneratedEndDistance - 0.1f
        );

        if (sampleDistance <= trackDistance)
            return false;

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
            lateralOffset + impactBody.Lateral,
            out Vector3 position,
            out Quaternion rotation
        );

        position += Vector3.up * carHeightOffset;
        rotation *= impactBody.RotationOffset;

        transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// Single entry point for every traffic collision.
    /// </summary>
    /// <param name="info">Impact description (severity, direction...).</param>
    /// <param name="speedAfterImpact">Forward speed the car ends up with.</param>
    public void ApplyImpact(ImpactInfo info, float speedAfterImpact)
    {
        // Cancel the attempted lane change (no snapping - the car
        // bounces back smoothly with the spring + faster lane return).
        if (IsChangingLane)
        {
            int rejectedLane = currentLane;
            currentLane = previousLane;
            previousLane = rejectedLane;
        }

        speedAfterImpact = Mathf.Max(0f, speedAfterImpact);

        if (speedAfterImpact < currentForwardSpeed)
        {
            // Brake hard over a few frames instead of an instant drop.
            forcedSpeedTarget = speedAfterImpact;
        }
        else
        {
            // Hit from behind: instant forward jolt.
            currentForwardSpeed = speedAfterImpact;
            forcedSpeedTarget = -1f;
        }

        laneInputLockedUntil =
            Time.time + collisionLaneLockDuration;

        impactBody.ApplyImpact(info);
    }

    /// <summary>Smoothly shoves the car forward to this track distance.</summary>
    public void ShoveForwardTo(float minimumTrackDistance)
    {
        shoveTargetDistance =
            Mathf.Max(shoveTargetDistance, minimumTrackDistance);
    }
}