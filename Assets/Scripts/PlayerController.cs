
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
        HandleLaneInput();

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

        // Update the player pose.
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
            lateralOffset,
            out Vector3 position,
            out Quaternion rotation
        );

        // Lift the car above the road.
        position += Vector3.up * carHeightOffset;

        transform.SetPositionAndRotation(position, rotation);
    }
}
