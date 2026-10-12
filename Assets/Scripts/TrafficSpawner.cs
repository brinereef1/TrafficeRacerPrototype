
using System.Collections.Generic;
using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadManager roadManager;
    [SerializeField] private GameObject[] trafficPrefabs;
    [SerializeField] private PlayerController playerController;

    [Header("Spawning")]
    [SerializeField] private float spawnAheadDistance = 70f;
    [SerializeField] private float minSpawnInterval = 1.5f;
    [SerializeField] private float maxSpawnInterval = 2.5f;
    [SerializeField] private int maxActiveVehicles = 8;

    [Header("Object Pool")]
    [SerializeField] private int initialPoolSizePerPrefab = 2;
    [SerializeField] private bool expandPoolIfNeeded = true;

    [Header("Traffic Speeds")]
    [SerializeField] private float minTrafficSpeed = 12f;
    [SerializeField] private float maxTrafficSpeed = 24f;

    [Header("Lane Safety")]
    [SerializeField] private float minimumGapAhead = 12f;
    [SerializeField] private float minimumGapBehind = 10f;
    [SerializeField] private float closingSpeedBuffer = 0.75f;

    [Header("Following Distance")]
    [SerializeField] private float minimumFollowingDistance = 10f;
    [SerializeField] private float followingCheckDistance = 24f;

    [Header("Position")]
    [SerializeField] private float carHeightOffset = 0.5f;

    private readonly List<TrafficVehicle> activeVehicles =
        new List<TrafficVehicle>();

    private readonly Dictionary<GameObject, Queue<TrafficVehicle>>
        pools = new Dictionary<GameObject, Queue<TrafficVehicle>>();

    private readonly Dictionary<TrafficVehicle, GameObject>
        prefabLookup = new Dictionary<TrafficVehicle, GameObject>();

    private float spawnTimer;

    private void Start()
    {
        if (roadManager == null)
        {
            Debug.LogError("TrafficSpawner: RoadManager is missing.", this);
            enabled = false;
            return;
        }

        if (trafficPrefabs == null || trafficPrefabs.Length == 0)
        {
            Debug.LogError("TrafficSpawner: Add traffic prefabs.", this);
            enabled = false;
            return;
        }

        if (playerController == null)
        {
            playerController = FindFirstObjectByType<PlayerController>();
        }

        InitializePools();
        ScheduleNextSpawn();
    }

    private void InitializePools()
    {
        foreach (GameObject prefab in trafficPrefabs)
        {
            if (prefab == null || pools.ContainsKey(prefab))
                continue;

            pools.Add(prefab, new Queue<TrafficVehicle>());

            for (int i = 0; i < initialPoolSizePerPrefab; i++)
            {
                CreatePooledVehicle(prefab);
            }
        }
    }

    private TrafficVehicle CreatePooledVehicle(GameObject prefab)
    {
        GameObject instance = Instantiate(prefab, transform);
        instance.name = prefab.name + " (Pooled)";
        instance.SetActive(false);

        TrafficVehicle vehicle =
            instance.GetComponent<TrafficVehicle>();

        if (vehicle == null)
        {
            Debug.LogError(
                $"Traffic prefab '{prefab.name}' needs TrafficVehicle.",
                prefab
            );

            Destroy(instance);
            return null;
        }

        prefabLookup[vehicle] = prefab;
        pools[prefab].Enqueue(vehicle);

        return vehicle;
    }

    private void Update()
    {
        spawnTimer -= Time.deltaTime;

        if (spawnTimer > 0f)
            return;

        ScheduleNextSpawn();

        if (CountActiveVehicles() >= maxActiveVehicles)
            return;

        SpawnVehicle();
    }

    private void SpawnVehicle()
    {
        float spawnDistance =
            roadManager.PlayerDistance + spawnAheadDistance;

        if (spawnDistance >= roadManager.GeneratedEndDistance - 1f)
            return;

        float speed = Random.Range(
            Mathf.Min(minTrafficSpeed, maxTrafficSpeed),
            Mathf.Max(minTrafficSpeed, maxTrafficSpeed)
        );

        if (!TryFindSafeSpawnLane(
                spawnDistance, speed, out int lane))
            return;

        GameObject prefab = trafficPrefabs[
            Random.Range(0, trafficPrefabs.Length)
        ];

        if (prefab == null || !pools.ContainsKey(prefab))
            return;

        Queue<TrafficVehicle> pool = pools[prefab];

        // Remove any destroyed cars from the queue.
        while (pool.Count > 0 && pool.Peek() == null)
            pool.Dequeue();

        TrafficVehicle vehicle = null;

        if (pool.Count > 0)
        {
            vehicle = pool.Dequeue();
        }
        else if (expandPoolIfNeeded)
        {
            // Create a new car when the pool is empty.
            vehicle = CreatePooledVehicle(prefab);

            if (vehicle != null)
            {
                // Remove it from the queue because it is being used now.
                RemoveFromPoolQueue(prefab, vehicle);
            }
        }

        if (vehicle == null)
            return;

        roadManager.GetPose(
            spawnDistance,
            roadManager.GetLaneOffset(lane),
            out Vector3 position,
            out Quaternion rotation
        );

        position += Vector3.up * carHeightOffset;

        vehicle.transform.SetPositionAndRotation(position, rotation);

        // Turn it on before setting its state.
        vehicle.gameObject.SetActive(true);

        vehicle.Initialize(
            roadManager,
            this,
            spawnDistance,
            lane,
            speed
        );

        if (!activeVehicles.Contains(vehicle))
            activeVehicles.Add(vehicle);
    }

    private bool IsPlayerBesideVehicle(
        TrafficVehicle vehicle,
        int targetLane)
    {
        if (playerController == null || vehicle == null)
            return false;

        // Check the distance between the player and this car.
        float distanceGap = Mathf.Abs(
            playerController.TrackDistance -
            vehicle.TrackDistance
        );

        float safetyDistance = Mathf.Max(
            minimumGapAhead,
            minimumGapBehind
        );

        // Only block a lane change when the cars are close.
        if (distanceGap > safetyDistance)
            return false;

        // Block the lane if the player is in the current or target lane.
        return playerController.OccupiesLane(vehicle.CurrentLane)
            || playerController.OccupiesLane(targetLane);
    }

    private void RemoveFromPoolQueue(
        GameObject prefab,
        TrafficVehicle vehicle)
    {
        Queue<TrafficVehicle> oldQueue = pools[prefab];
        Queue<TrafficVehicle> newQueue =
            new Queue<TrafficVehicle>();

        while (oldQueue.Count > 0)
        {
            TrafficVehicle item = oldQueue.Dequeue();

            if (item != vehicle)
                newQueue.Enqueue(item);
        }

        pools[prefab] = newQueue;
    }

    public void ReturnVehicleToPool(TrafficVehicle vehicle)
    {
        if (vehicle == null)
            return;

        activeVehicles.Remove(vehicle);

        if (!prefabLookup.TryGetValue(vehicle, out GameObject prefab))
        {
            vehicle.gameObject.SetActive(false);
            return;
        }

        vehicle.ResetForPool();
        vehicle.gameObject.SetActive(false);

        // Prevent the same car from being queued twice.
        if (!pools[prefab].Contains(vehicle))
            pools[prefab].Enqueue(vehicle);
    }

    public void NotifyVehicleDestroyed(TrafficVehicle vehicle)
    {
        if (vehicle == null)
            return;

        activeVehicles.Remove(vehicle);
        prefabLookup.Remove(vehicle);
    }

    private bool TryFindSafeSpawnLane(
        float spawnDistance,
        float speed,
        out int selectedLane)
    {
        int laneCount = roadManager.LaneCount;

        if (laneCount <= 0)
        {
            selectedLane = -1;
            return false;
        }

        int firstLane = Random.Range(0, laneCount);

        for (int attempt = 0; attempt < laneCount; attempt++)
        {
            int lane = (firstLane + attempt) % laneCount;

            if (IsSpawnPositionSafe(lane, spawnDistance, speed))
            {
                selectedLane = lane;
                return true;
            }
        }

        selectedLane = -1;
        return false;
    }

    public bool IsLaneSafeForChange(
        TrafficVehicle requester,
        int targetLane)
    {
        if (IsPlayerBesideVehicle(requester, targetLane))
        {
            return false;
        }
            
        if (requester == null ||
            targetLane < 0 ||
            targetLane >= roadManager.LaneCount)
            return false;

        foreach (TrafficVehicle other in activeVehicles)
        {
            if (other == null || other == requester)
                continue;

            if (!other.OccupiesLane(targetLane))
                continue;

            if (!HasSafeGap(
                    other.TrackDistance,
                    requester.TrackDistance,
                    requester.ForwardSpeed,
                    other.ForwardSpeed))
                return false;
        }

        return true;
    }

    private bool IsSpawnPositionSafe(
        int lane,
        float spawnDistance,
        float spawnSpeed)
    {
        foreach (TrafficVehicle other in activeVehicles)
        {
            if (other == null || !other.OccupiesLane(lane))
                continue;

            if (!HasSafeGap(
                    other.TrackDistance,
                    spawnDistance,
                    spawnSpeed,
                    other.ForwardSpeed))
                return false;
        }

        return true;
    }

    private bool HasSafeGap(
        float otherDistance,
        float candidateDistance,
        float candidateSpeed,
        float otherSpeed)
    {
        float difference = otherDistance - candidateDistance;

        float requiredGap;

        if (difference >= 0f)
        {
            float closingSpeed =
                Mathf.Max(0f, candidateSpeed - otherSpeed);

            requiredGap =
                minimumGapAhead +
                closingSpeed * closingSpeedBuffer;
        }
        else
        {
            float closingSpeed =
                Mathf.Max(0f, otherSpeed - candidateSpeed);

            requiredGap =
                minimumGapBehind +
                closingSpeed * closingSpeedBuffer;
        }

        return Mathf.Abs(difference) >= requiredGap;
    }

    public float GetSafeSpeed(
        TrafficVehicle requester,
        float desiredSpeed)
    {
        float safeSpeed = desiredSpeed;

        foreach (TrafficVehicle other in activeVehicles)
        {
            if (other == null || other == requester)
                continue;

            bool sharesLane =
                other.OccupiesLane(requester.CurrentLane) ||
                (requester.IsChangingLane &&
                 other.OccupiesLane(requester.PreviousLane));

            if (!sharesLane)
                continue;

            float gap =
                other.TrackDistance - requester.TrackDistance;

            if (gap <= 0f || gap >= followingCheckDistance)
                continue;

            float minDistance = Mathf.Min(
                minimumFollowingDistance,
                followingCheckDistance - 0.01f
            );

            float blend = Mathf.InverseLerp(
                minDistance,
                followingCheckDistance,
                gap
            );

            float permittedSpeed = Mathf.Lerp(
                other.ForwardSpeed,
                desiredSpeed,
                blend
            );

            safeSpeed = Mathf.Min(safeSpeed, permittedSpeed);
        }

        return Mathf.Max(0f, safeSpeed);
    }

    private int CountActiveVehicles()
    {
        activeVehicles.RemoveAll(vehicle => vehicle == null);
        return activeVehicles.Count;
    }

    private void ScheduleNextSpawn()
    {
        float min = Mathf.Max(0.1f, minSpawnInterval);
        float max = Mathf.Max(min, maxSpawnInterval);

        spawnTimer = Random.Range(min, max);
    }
}
