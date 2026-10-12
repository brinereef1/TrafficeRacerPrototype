
using UnityEngine;

public class TrafficVehicle : MonoBehaviour
{
    [Header("Lane Changing")]
    [SerializeField] private float laneChangeSpeed = 4f;

    [SerializeField] private float minLaneChangeInterval = 2.5f;
    [SerializeField] private float maxLaneChangeInterval = 5f;

    [Range(0f, 1f)]
    [SerializeField] private float laneChangeChance = 0.65f;

    [Header("Following")]
    [SerializeField] private float acceleration = 4f;
    [SerializeField] private float braking = 12f;

    [Header("Position")]
    [SerializeField] private float carHeightOffset = 0.5f;

    [Header("Cleanup")]
    [SerializeField] private float despawnBehindDistance = 40f;
    [SerializeField] private float aheadSafetyMargin = 15f;

    private RoadManager roadManager;
    private TrafficSpawner owner;

    private float trackDistance;
    private float cruiseSpeed;
    private float currentSpeed;
    private float lateralOffset;

    private int currentLane;
    private int previousLane;

    private float nextLaneChangeTime;
    private bool isChangingLane;
    private bool initialized;

    // Public values for other scripts.
    public float TrackDistance => trackDistance;
    public float ForwardSpeed => currentSpeed;
    public int CurrentLane => currentLane;
    public int PreviousLane => previousLane;
    public bool IsChangingLane => isChangingLane;

    private bool isReturningToPool;

    public void ResetForPool()
    {
        initialized = false;
        isChangingLane = false;
        currentSpeed = 0f;
        cruiseSpeed = 0f;
        trackDistance = 0f;
        lateralOffset = 0f;
        currentLane = 0;
        previousLane = 0;
        nextLaneChangeTime = 0f;
        isReturningToPool = false;
    }
    public bool OccupiesLane(int lane)
    {
        // While changing lanes, the car keeps both lanes reserved.
        return currentLane == lane ||
               (isChangingLane && previousLane == lane);
    }

    public void Initialize(
        RoadManager manager,
        TrafficSpawner spawner,
        float startDistance,
        int startLane,
        float speed)
    {
        roadManager = manager;
        owner = spawner;

        trackDistance = startDistance;

        currentLane = Mathf.Clamp(
            startLane, 0, roadManager.LaneCount - 1);

        previousLane = currentLane;

        cruiseSpeed = speed;
        currentSpeed = speed;

        lateralOffset =
            roadManager.GetLaneOffset(currentLane);

        isChangingLane = false;
        initialized = true;

        ScheduleNextLaneChange();
        UpdatePose();
    }

    private void Update()
    {
        if (!initialized || roadManager == null)
            return;

        float deltaTime = Time.deltaTime;

        // Ask the spawner for a safe speed.
        float desiredSpeed = owner != null
            ? owner.GetSafeSpeed(this, cruiseSpeed)
            : cruiseSpeed;

        float speedChangeRate =
            desiredSpeed < currentSpeed ? braking : acceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            desiredSpeed,
            speedChangeRate * deltaTime
        );

        // Move the car forward.
        trackDistance += currentSpeed * deltaTime;

    if (ShouldDespawn())
    {
        ReturnToPool();
        return;
    }

        // Start another lane change only after the current one finishes.
        if (!isChangingLane &&
            Time.time >= nextLaneChangeTime)
        {
            TryChangeLane();
        }

        UpdateLaneMovement(deltaTime);
        UpdatePose();
    }

    private void ReturnToPool()
    {
        if (isReturningToPool)
            return;

        isReturningToPool = true;
        initialized = false;

        if (owner != null)
        {
            owner.ReturnVehicleToPool(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void TryChangeLane()
    {
        if (owner == null || roadManager.LaneCount <= 1)
        {
            ScheduleNextLaneChange();
            return;
        }

        // Sometimes stay in the current lane.
        if (Random.value > laneChangeChance)
        {
            ScheduleNextLaneChange();
            return;
        }

        // Try the left or right lane first.
        int firstDirection =
            Random.value < 0.5f ? -1 : 1;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int direction = attempt == 0
                ? firstDirection
                : -firstDirection;

            int candidateLane = currentLane + direction;

            if (candidateLane < 0 ||
                candidateLane >= roadManager.LaneCount)
            {
                continue;
            }

            // The spawner checks nearby cars and the target lane.
            if (!owner.IsLaneSafeForChange(
                    this, candidateLane))
            {
                continue;
            }

            // Save the current lane before changing.
            previousLane = currentLane;

            // Move into the target lane right away.
            currentLane = candidateLane;
            isChangingLane = true;

            return;
        }

        // No safe lane was available, so wait a bit and try again later.
        nextLaneChangeTime =
            Time.time + Random.Range(0.5f, 1f);
    }

    private void UpdateLaneMovement(float deltaTime)
    {
        if (!isChangingLane)
            return;

        float targetOffset =
            roadManager.GetLaneOffset(currentLane);

        lateralOffset = Mathf.MoveTowards(
            lateralOffset,
            targetOffset,
            laneChangeSpeed * deltaTime
        );

        if (Mathf.Abs(lateralOffset - targetOffset) < 0.05f)
        {
            lateralOffset = targetOffset;
            previousLane = currentLane;
            isChangingLane = false;
            ScheduleNextLaneChange();
        }
    }

    private void ScheduleNextLaneChange()
    {
        float min = Mathf.Max(0.1f, minLaneChangeInterval);
        float max = Mathf.Max(min, maxLaneChangeInterval);

        nextLaneChangeTime =
            Time.time + Random.Range(min, max);
    }

    private bool ShouldDespawn()
    {
        float relativeDistance =
            trackDistance - roadManager.PlayerDistance;

        return relativeDistance < -despawnBehindDistance ||
               relativeDistance >
               roadManager.LookaheadDistance - aheadSafetyMargin;
    }

    private void UpdatePose()
    {
        roadManager.GetPose(
            trackDistance,
            lateralOffset,
            out Vector3 position,
            out Quaternion rotation
        );

        position += Vector3.up * carHeightOffset;

        transform.SetPositionAndRotation(position, rotation);
    }

    private void OnDestroy()
    {
        if (owner != null && !isReturningToPool)
        {
            owner.NotifyVehicleDestroyed(this);
        }
    }
}
