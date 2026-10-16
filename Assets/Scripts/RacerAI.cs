using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a PlayerController automatically instead of using the keyboard.
/// It reacts to slower cars in its lane, but is deliberately imperfect:
/// it sometimes never notices a car, reacts late, or skips the side check
/// and cuts into someone, so it crashes like the human player can.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class RacerAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController racer;
    [SerializeField] private TrafficSpawner trafficSpawner;
    [SerializeField] private NitroBoost nitro;

    [Header("Nitro")]
    [SerializeField] private bool useNitro = true;

    [Tooltip("The AI starts boosting once its tank is this full (0..1).")]
    [SerializeField, Range(0f, 1f)] private float aiStartCharge = 0.6f;

    [Header("Driver Skill")]
    [Tooltip("Top speed multiplier for this driver (1 = same as the player).")]
    [SerializeField] private float baseSpeedScale = 1f;

    [Tooltip("Chance to notice a slower car in the lane at all. " +
             "Lower = crashes more often.")]
    [SerializeField, Range(0f, 1f)] private float awareness = 0.85f;

    [Tooltip("Distance at which the driver reacts to a car ahead " +
             "(random value between the two numbers for each car).")]
    [SerializeField] private Vector2 reactionDistance =
        new Vector2(8f, 22f);

    [Tooltip("Chance to check the target lane is free before swerving. " +
             "Lower = cuts into other cars more often.")]
    [SerializeField, Range(0f, 1f)] private float sideCheckAccuracy = 0.8f;

    [Tooltip("Below this gap it is too late to swerve, so the driver brakes.")]
    [SerializeField] private float minimumSwerveGap = 5f;

    [SerializeField] private float laneChangeCooldown = 1f;

    [Tooltip("How often the driver 'thinks' (seconds).")]
    [SerializeField] private float thinkInterval = 0.15f;

    [Header("Perception")]
    [SerializeField] private float lookAheadDistance = 35f;
    [SerializeField, Range(0.2f, 1f)] private float laneOverlapFactor = 0.6f;
    [SerializeField] private float sideCheckAhead = 12f;
    [SerializeField] private float sideCheckBehind = 8f;

    [Tooltip("Distance to keep when stuck behind a slower car.")]
    [SerializeField] private float followDistance = 10f;

    [Header("Rubber Band")]
    [Tooltip("Keeps the AI close to the human. REQUIRED for this road system: " +
             "the road only exists around the human player.")]
    [SerializeField] private bool rubberBandEnabled = true;

    [Tooltip("Distance gap at which the full speed bonus / penalty applies.")]
    [SerializeField] private float rubberBandRange = 40f;
    [SerializeField] private float maxSpeedBoost = 0.12f;
    [SerializeField] private float maxSpeedPenalty = 0.10f;

    [Tooltip("Max distance the AI may lead the human. Keep BELOW the " +
             "traffic spawn distance so the AI always meets traffic.")]
    [SerializeField] private float maxLeadOverPlayer = 50f;

    [Tooltip("Max distance the AI may trail the human. Must stay below " +
             "the road's recycle distance (40).")]
    [SerializeField] private float maxLagBehindPlayer = 30f;

    private struct Snapshot
    {
        public int id;
        public float distance;
        public float lateral;
        public float speed;
    }

    private class Memory
    {
        public bool noticed;
        public float reaction;
        public float lastSeen;
    }

    private readonly List<Snapshot> snapshots = new List<Snapshot>();
    private readonly Dictionary<int, Memory> memory =
        new Dictionary<int, Memory>();
    private readonly List<int> staleKeys = new List<int>();

    private float nextThinkTime;
    private float nextLaneChangeTime;
    private bool obstacleAhead;

    private void Awake()
    {
        if (racer == null)
            racer = GetComponent<PlayerController>();

        if (trafficSpawner == null)
            trafficSpawner = FindFirstObjectByType<TrafficSpawner>();

        if (nitro == null)
            nitro = GetComponent<NitroBoost>();
    }

    private void Start()
    {
        racer.SpeedScale = baseSpeedScale;

        // Desynchronize the AI cars so they don't all think on the same frame.
        nextThinkTime = Time.time + Random.Range(0f, thinkInterval);
    }

    private void Update()
    {
        if (racer == null || racer.Road == null)
            return;

        if (Time.time < nextThinkTime)
            return;

        nextThinkTime = Time.time + Mathf.Max(0.05f, thinkInterval);
        Think();
    }

    private void Think()
    {
        GatherSnapshots();

        float limit = Mathf.Min(UpdateRubberBand(), AvoidTraffic());

        if (limit < float.MaxValue)
            racer.SetSpeedLimit(limit);
        else
            racer.ClearSpeedLimit();

        UpdateNitro();

        PruneMemory();
    }

    // ---------------------------------------------------------------
    // Perception
    // ---------------------------------------------------------------

    /// <summary>
    /// The AI boosts on clear straights only: no slower car ahead,
    /// not in a curve, and not already far ahead of the human.
    /// </summary>
    private void UpdateNitro()
    {
        if (nitro == null)
            return;

        if (!useNitro)
        {
            nitro.AiWantsBoost = false;
            return;
        }

        bool inCurve = Mathf.Abs(racer.DriftSlide) > 0.15f;
        bool clearRoad = !obstacleAhead;

        PlayerController human = PlayerController.HumanPlayer;

        // Don't waste nitro when far ahead: the rubber band would cancel it.
        bool farAhead =
            human != null &&
            human != racer &&
            racer.TrackDistance - human.TrackDistance >
            maxLeadOverPlayer * 0.6f;

        bool safeToBoost = clearRoad && !inCurve && !farAhead;

        if (nitro.AiWantsBoost)
            nitro.AiWantsBoost = safeToBoost && nitro.Charge01 > 0.02f;
        else
            nitro.AiWantsBoost = safeToBoost && nitro.Charge01 >= aiStartCharge;
    }

    private void GatherSnapshots()
    {
        snapshots.Clear();

        if (trafficSpawner != null)
        {
            IReadOnlyList<TrafficVehicle> vehicles =
                trafficSpawner.ActiveVehicles;

            for (int i = 0; i < vehicles.Count; i++)
            {
                TrafficVehicle v = vehicles[i];

                if (v == null || !v.gameObject.activeInHierarchy)
                    continue;

                snapshots.Add(new Snapshot
                {
                    id = v.GetInstanceID(),
                    distance = v.TrackDistance,
                    lateral = v.LateralOffset,
                    speed = v.ForwardSpeed
                });
            }
        }

        foreach (PlayerController other in PlayerController.AllRacers)
        {
            if (other == null || other == racer)
                continue;

            snapshots.Add(new Snapshot
            {
                id = other.GetInstanceID(),
                distance = other.TrackDistance,
                lateral = other.LateralOffset,
                speed = other.CurrentForwardSpeed
            });
        }
    }

    // ---------------------------------------------------------------
    // Rubber banding (keeps the AI inside the generated road)
    // ---------------------------------------------------------------

    private float UpdateRubberBand()
    {
        PlayerController human = PlayerController.HumanPlayer;

        if (!rubberBandEnabled || human == null || human == racer)
        {
            racer.SpeedScale = baseSpeedScale;
            return float.MaxValue;
        }

        // Positive gap = AI is ahead of the human.
        float gap = racer.TrackDistance - human.TrackDistance;

        // t > 0 when the AI is behind (speed up), t < 0 when ahead.
        float t = Mathf.Clamp(
            -gap / Mathf.Max(1f, rubberBandRange), -1f, 1f);

        float bonus = t > 0f
            ? t * maxSpeedBoost
            : t * maxSpeedPenalty;

        racer.SpeedScale = baseSpeedScale * (1f + bonus);

        // Hard window: the road is only built near the human.
        if (gap < -maxLagBehindPlayer)
        {
            racer.ShoveForwardTo(
                human.TrackDistance - maxLagBehindPlayer + 2f);
        }

        if (gap > maxLeadOverPlayer)
        {
            return Mathf.Max(8f, human.CurrentForwardSpeed * 0.9f);
        }

        return float.MaxValue;
    }

    // ---------------------------------------------------------------
    // Decision making
    // ---------------------------------------------------------------

    /// <summary>
    /// Looks for a slower car in our lane. Returns a speed limit
    /// (float.MaxValue = no limit).
    /// </summary>
    private float AvoidTraffic()
    {
        float myDistance = racer.TrackDistance;
        float myLateral = racer.LateralOffset;
        float mySpeed = racer.CurrentForwardSpeed;
        float halfLane = racer.Road.LaneWidth * laneOverlapFactor;

        int obstacleIndex = -1;
        float bestGap = float.MaxValue;

        for (int i = 0; i < snapshots.Count; i++)
        {
            Snapshot s = snapshots[i];
            float gap = s.distance - myDistance;

            if (gap <= 0f || gap > lookAheadDistance)
                continue;

            if (Mathf.Abs(s.lateral - myLateral) > halfLane)
                continue;

            // Only care about cars we are catching up to.
            if (s.speed >= mySpeed - 0.5f)
                continue;

            if (gap < bestGap)
            {
                bestGap = gap;
                obstacleIndex = i;
            }
        }

        obstacleAhead = obstacleIndex >= 0;

        if (obstacleIndex < 0)
            return float.MaxValue;

        Snapshot obstacle = snapshots[obstacleIndex];
        Memory m = GetMemory(obstacle.id);

        // Never noticed this car: drives straight into it.
        if (!m.noticed)
            return float.MaxValue;

        float closing = mySpeed - obstacle.speed;
        float react = m.reaction + closing * 0.5f;

        // Not close enough to care yet.
        if (bestGap > react)
            return float.MaxValue;

        bool canSwerve =
            bestGap >= minimumSwerveGap &&
            Time.time >= nextLaneChangeTime &&
            !racer.IsChangingLane &&
            !racer.IsLaneInputLocked;

        if (canSwerve && TrySwerve(myDistance, mySpeed))
            return float.MaxValue;

        // Could not (or can not) change lane: slow down to follow.
        float brakeGap = followDistance + closing * 0.5f;

        if (bestGap <= brakeGap)
            return Mathf.Max(0f, obstacle.speed * 0.95f);

        return float.MaxValue;
    }

    private bool TrySwerve(float myDistance, float mySpeed)
    {
        int laneCount = racer.Road.LaneCount;
        int myLane = racer.CurrentLane;
        int firstDirection = Random.value < 0.5f ? -1 : 1;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int direction = attempt == 0 ? firstDirection : -firstDirection;
            int lane = myLane + direction;

            if (lane < 0 || lane >= laneCount)
                continue;

            // Imperfect driver: sometimes skips the check entirely.
            bool checkLane = Random.value < sideCheckAccuracy;

            if (checkLane && !IsLaneClear(lane, myDistance, mySpeed))
                continue;

            if (racer.RequestLaneChange(direction))
            {
                nextLaneChangeTime = Time.time + laneChangeCooldown;
                return true;
            }
        }

        return false;
    }

    private bool IsLaneClear(int lane, float myDistance, float mySpeed)
    {
        float targetLateral = racer.Road.GetLaneOffset(lane);
        float halfLane = racer.Road.LaneWidth * laneOverlapFactor;

        for (int i = 0; i < snapshots.Count; i++)
        {
            Snapshot s = snapshots[i];

            if (Mathf.Abs(s.lateral - targetLateral) > halfLane)
                continue;

            float gap = s.distance - myDistance;

            if (gap >= 0f)
            {
                // Car ahead in that lane.
                float closing = Mathf.Max(0f, mySpeed - s.speed);

                if (gap < sideCheckAhead + closing * 0.5f)
                    return false;
            }
            else
            {
                // Car behind in that lane.
                float closing = Mathf.Max(0f, s.speed - mySpeed);

                if (-gap < sideCheckBehind + closing * 0.5f)
                    return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Per-car memory (does this driver notice that car, and when?)
    // ---------------------------------------------------------------

    private Memory GetMemory(int id)
    {
        if (!memory.TryGetValue(id, out Memory m))
        {
            m = new Memory
            {
                noticed = Random.value < awareness,
                reaction = Random.Range(
                    reactionDistance.x,
                    Mathf.Max(reactionDistance.x, reactionDistance.y))
            };

            memory.Add(id, m);
        }

        m.lastSeen = Time.time;
        return m;
    }

    private void PruneMemory()
    {
        if (memory.Count == 0)
            return;

        staleKeys.Clear();

        foreach (KeyValuePair<int, Memory> pair in memory)
        {
            if (Time.time - pair.Value.lastSeen > 3f)
                staleKeys.Add(pair.Key);
        }

        for (int i = 0; i < staleKeys.Count; i++)
            memory.Remove(staleKeys[i]);
    }
}