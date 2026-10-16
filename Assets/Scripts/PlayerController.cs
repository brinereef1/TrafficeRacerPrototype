using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadManager roadManager;

    [Header("Racer")]
    [Tooltip("ON = keyboard (human player). OFF = driven by a RacerAI component.")]
    [SerializeField] private bool isPlayerControlled = true;

    [Tooltip("Starting lane (0 = leftmost). -1 = center lane.")]
    [SerializeField] private int startLane = -1;

    [Tooltip("Starting distance along the road.")]
    [SerializeField] private float startDistance = 0f;

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

    [Tooltip("Braking strength when an AI speed limit applies (e.g. following traffic).")]
    [SerializeField] private float limitedBrakeRate = 12f;

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

    [Header("Curve Drift")]
    [Tooltip("Curve angle (degrees over the check distance) that gives a full drift.")]
    [SerializeField] private float driftFullAngle = 10f;

    [Tooltip("Extra yaw (degrees) the nose points into the curve at full drift.")]
    [SerializeField] private float maxDriftYaw = 22f;

    [Tooltip("Body roll (degrees) leaning to the outside of the curve.")]
    [SerializeField] private float maxDriftRoll = 7f;

    [Tooltip("How far the car slides wide toward the outside of the curve.")]
    [SerializeField] private float maxDriftLateral = 0.45f;

    [Tooltip("How fast the drift builds up (full drift per second).")]
    [SerializeField] private float driftEnterRate = 3f;

    [Tooltip("How fast the car straightens out after the curve.")]
    [SerializeField] private float driftExitRate = 2f;

    [Tooltip("Small counter-steer wiggle while sliding (degrees).")]
    [SerializeField] private float driftWobbleAngle = 1.5f;
    [SerializeField] private float driftWobbleSpeed = 2.6f;

    [Tooltip("How much of the drift yaw the camera follows (0 = none, 1 = all).")]
    [SerializeField, Range(0f, 1f)] private float cameraDriftFollow = 0.25f;

    private float driftSlide;          // -1 (left curve) .. +1 (right curve)
    private float driftYaw;
    private float driftRoll;
    private float driftLateral;
    private float driftBlockedUntil;
    private Quaternion roadRotation = Quaternion.identity;

    private float speedScale = 1f;
    private float externalSpeedLimit = float.MaxValue;
    private float lastImpactTime = -100f;

    /// <summary>Every active racer (human + AI).</summary>
    public static readonly List<PlayerController> AllRacers =
        new List<PlayerController>();

    /// <summary>The keyboard-controlled racer (camera / HUD target).</summary>
    public static PlayerController HumanPlayer { get; private set; }

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

    public bool IsPlayerControlled => isPlayerControlled;
    public RoadManager Road => roadManager;
    public bool IsLaneInputLocked => Time.time < laneInputLockedUntil;

    // Nitro (set by NitroBoost).
    /// <summary>0..1 smoothed nitro intensity (camera effects read this).</summary>
    public float NitroAmount { get; set; }

    /// <summary>Extra top speed added while boosting.</summary>
    public float BoostSpeed { get; set; }

    /// <summary>Acceleration multiplier while boosting.</summary>
    public float BoostAcceleration { get; set; } = 1f;

    public float TimeSinceImpact => Time.time - lastImpactTime;
    public float CruiseSpeed => forwardSpeed * speedScale;

    /// <summary>Multiplier on top speed (AI skill / rubber banding).</summary>
    public float SpeedScale
    {
        get => speedScale;
        set => speedScale = Mathf.Max(0.1f, value);
    }

    /// <summary>Caps the target speed (AI following slower traffic).</summary>
    public void SetSpeedLimit(float limit) => externalSpeedLimit = limit;
    public void ClearSpeedLimit() => externalSpeedLimit = float.MaxValue;

    /// <summary>
    /// Same as pressing the left (-1) or right (+1) key.
    /// Used by RacerAI. Returns false if blocked (crash lock, edge of road).
    /// </summary>
    public bool RequestLaneChange(int direction)
    {
        if (roadManager == null || Time.time < laneInputLockedUntil)
            return false;

        int target = Mathf.Clamp(
            currentLane + direction, 0, roadManager.LaneCount - 1);

        if (target == currentLane)
            return false;

        previousLane = currentLane;
        currentLane = target;
        return true;
    }
    public float TrackDistance => trackDistance;
    public int CurrentLane => currentLane;
    public float CurrentForwardSpeed => currentForwardSpeed;
    public float DistanceTravelled => trackDistance;

    /// <summary>-1..+1. Sign = curve direction, magnitude = drift amount.</summary>
    public float DriftSlide => driftSlide;

    /// <summary>Road heading the camera should follow (mostly ignores drift yaw).</summary>
    public Quaternion CameraRotation =>
        roadRotation * Quaternion.Euler(0f, driftYaw * cameraDriftFollow, 0f);

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

    private void OnEnable()
    {
        if (!AllRacers.Contains(this))
            AllRacers.Add(this);

        if (isPlayerControlled)
            HumanPlayer = this;
    }

    private void OnDisable()
    {
        AllRacers.Remove(this);

        if (HumanPlayer == this)
            HumanPlayer = null;
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

        trackDistance = startDistance;
        currentForwardSpeed = 5f;

        currentLane = startLane >= 0
            ? Mathf.Clamp(startLane, 0, roadManager.LaneCount - 1)
            : (roadManager.LaneCount - 1) / 2;

        previousLane = currentLane;
        lateralOffset = roadManager.GetLaneOffset(currentLane);

        // Only the human drives the road generation.
        if (isPlayerControlled)
            roadManager.PlayerDistance = trackDistance;

        UpdatePlayerPose();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (isPlayerControlled && Time.time >= laneInputLockedUntil)
        {
            HandleLaneInput();
        }

        // Signed curve angle: + = right curve, - = left curve.
        float curveAngle = GetCurveAngle();
        UpdateDrift(curveAngle, dt);

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
            float cruise = forwardSpeed * speedScale;

            float targetSpeed =
                Mathf.Abs(curveAngle) >= curveAngleThreshold
                ? Mathf.Max(0f, cruise - curveSpeedReduction)
                : cruise;

            // Nitro adds extra top speed.
            targetSpeed += BoostSpeed;

            // AI may cap the speed (following slower traffic).
            bool limited = externalSpeedLimit < targetSpeed;

            if (limited)
                targetSpeed = Mathf.Max(0f, externalSpeedLimit);

            float rate =
                (limited && targetSpeed < currentForwardSpeed)
                    ? limitedBrakeRate
                    : speedChangeRate *
                      (BoostSpeed > 0.01f ? BoostAcceleration : 1f);

            currentForwardSpeed = Mathf.MoveTowards(
                currentForwardSpeed,
                targetSpeed,
                rate * dt
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

        if (isPlayerControlled)
            roadManager.PlayerDistance = trackDistance;

        impactBody.Tick(dt);
        UpdatePlayerPose();
    }

    /// <summary>
    /// Signed change of road direction ahead (degrees).
    /// Positive = curving right, negative = curving left, 0 = no road to check.
    /// </summary>
    private float GetCurveAngle()
    {
        float sampleDistance = Mathf.Min(
            trackDistance + curveCheckDistance,
            roadManager.GeneratedEndDistance - 0.1f
        );

        if (sampleDistance <= trackDistance)
            return 0f;

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

        // Positive when the road bends to the right.
        return Vector3.SignedAngle(
            currentForward,
            futureForward,
            Vector3.up
        );
    }

    private void UpdateDrift(float curveAngle, float dt)
    {
        float absAngle = Mathf.Abs(curveAngle);
        bool inCurve =
            absAngle >= curveAngleThreshold &&
            Time.time >= driftBlockedUntil;

        float target = 0f;

        if (inCurve)
        {
            // Sharper curve = bigger slide (never less than half).
            float strength = Mathf.Lerp(
                0.5f,
                1f,
                Mathf.InverseLerp(
                    curveAngleThreshold,
                    Mathf.Max(curveAngleThreshold + 0.1f, driftFullAngle),
                    absAngle
                )
            );

            // Slower car = smaller slide.
            float speedFactor = Mathf.Lerp(
                0.6f,
                1f,
                Mathf.InverseLerp(
                    0f,
                    Mathf.Max(0.1f, forwardSpeed),
                    currentForwardSpeed
                )
            );

            target = Mathf.Sign(curveAngle) * strength * speedFactor;
        }

        float rate = Mathf.Abs(target) > Mathf.Abs(driftSlide)
            ? driftEnterRate
            : driftExitRate;

        driftSlide = Mathf.MoveTowards(driftSlide, target, rate * dt);

        float amount = Mathf.Abs(driftSlide);

        // Counter-steer wiggle makes the slide feel alive.
        float wobble =
            Mathf.Sin(Time.time * driftWobbleSpeed) *
            driftWobbleAngle * amount;

        // Right curve: nose points further right, body leans left
        // (outside), and the car slides wide to the left.
        driftYaw = driftSlide * maxDriftYaw + wobble;
        driftRoll = driftSlide * maxDriftRoll;
        driftLateral = -driftSlide * maxDriftLateral;
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
            lateralOffset + impactBody.Lateral + driftLateral,
            out Vector3 position,
            out Quaternion rotation
        );

        // The camera follows this pure road heading.
        roadRotation = rotation;

        position += Vector3.up * carHeightOffset;

        rotation *=
            Quaternion.Euler(0f, driftYaw, driftRoll) *
            impactBody.RotationOffset;

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
        // Only reject the lane change once per impact. If the lock is
        // already active the rejection already happened, and swapping
        // again would send the car back into the other vehicle.
        if (IsChangingLane && Time.time >= laneInputLockedUntil)
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

        lastImpactTime = Time.time;

        // A crash breaks the drift; don't restart it right away.
        driftSlide *= 0.35f;
        driftBlockedUntil = Time.time + 0.5f;
    }

    /// <summary>
    /// Keeps lane changes blocked while touching another vehicle.
    /// If a lane change is still running (the impact was missed or
    /// ignored by a cooldown), it is cancelled once.
    /// </summary>
    public void ExtendLaneLock(float seconds)
    {
        if (roadManager == null)
            return;

        if (Time.time >= laneInputLockedUntil &&
            IsChangingLane &&
            currentLane != previousLane)
        {
            int rejectedLane = currentLane;
            currentLane = previousLane;
            previousLane = rejectedLane;
        }

        laneInputLockedUntil = Mathf.Max(
            laneInputLockedUntil,
            Time.time + Mathf.Max(0f, seconds));
    }

    /// <summary>Smoothly shoves the car forward to this track distance.</summary>
    public void ShoveForwardTo(float minimumTrackDistance)
    {
        shoveTargetDistance =
            Mathf.Max(shoveTargetDistance, minimumTrackDistance);
    }
}