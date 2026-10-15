using UnityEngine;

/// <summary>
/// Describes one collision from the point of view of ONE vehicle.
/// </summary>
public struct ImpactInfo
{
    /// <summary>0..1 - how violent the hit is (based on closing speed).</summary>
    public float severity;

    /// <summary>Closing speed in m/s.</summary>
    public float closingSpeed;

    /// <summary>Road-space direction THIS vehicle is shoved: -1 = left, +1 = right.</summary>
    public float pushDirection;

    /// <summary>
    /// Where this vehicle was hit along its length:
    /// +1 = at the front, -1 = at the rear, 0 = middle.
    /// </summary>
    public float longitudinal;

    public bool isSide;
    public Vector3 worldPoint;
    public Vector3 worldVelocity;
}

/// <summary>
/// Underdamped spring-damper that animates a vehicle body after a hit.
/// Because it is underdamped, the car overshoots and wobbles back
/// (like real suspension) instead of easing linearly.
/// </summary>
[System.Serializable]
public class VehicleImpactBody
{
    [Header("Spring")]
    [SerializeField] private float stiffness = 90f;
    [Tooltip("Lower = more wobble. ~9 gives two or three visible oscillations.")]
    [SerializeField] private float damping = 9f;

    [Header("Limits")]
    [SerializeField] private float maxLateral = 1.0f;
    [SerializeField] private float maxYaw = 22f;
    [SerializeField] private float maxRoll = 12f;
    [SerializeField] private float maxPitch = 7f;

    [Header("Kick Strength (at severity 1)")]
    [SerializeField] private float lateralKick = 7f;   // m/s
    [SerializeField] private float yawKick = 220f;     // deg/s
    [SerializeField] private float rollKick = 150f;    // deg/s
    [SerializeField] private float pitchKick = 90f;    // deg/s

    private float lateral, yaw, roll, pitch;
    private float lateralVel, yawVel, rollVel, pitchVel;

    public float Lateral => lateral;
    public Quaternion RotationOffset => Quaternion.Euler(pitch, yaw, roll);

    public void Reset()
    {
        lateral = yaw = roll = pitch = 0f;
        lateralVel = yawVel = rollVel = pitchVel = 0f;
    }

    public void ApplyImpact(ImpactInfo info)
    {
        float scale = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(info.severity));
        float dir = info.pushDirection >= 0f ? 1f : -1f;
        float longi = Mathf.Clamp(info.longitudinal, -1f, 1f);

        // Shoved sideways, away from the other car.
        lateralVel += dir * lateralKick * scale;

        // Hit at the front -> nose swings in the push direction,
        // hit at the rear -> tail swings, so the nose goes the other way.
        float yawSign = longi >= 0f ? 1f : -1f;
        float yawAmount = 0.5f + 0.5f * Mathf.Abs(longi);
        yawVel += dir * yawSign * yawAmount * yawKick * scale;

        // Body leans against the push direction (inertia).
        rollVel += dir * rollKick * scale;

        // Front hit = nose dives, rear hit = nose lifts.
        pitchVel += longi * pitchKick * scale;
    }

    public void Tick(float deltaTime)
    {
        float dt = Mathf.Min(deltaTime, 0.033f);

        if (dt <= 0f)
            return;

        Step(ref lateral, ref lateralVel, maxLateral, dt);
        Step(ref yaw, ref yawVel, maxYaw, dt);
        Step(ref roll, ref rollVel, maxRoll, dt);
        Step(ref pitch, ref pitchVel, maxPitch, dt);
    }

    private void Step(ref float x, ref float v, float limit, float dt)
    {
        v += (-stiffness * x - damping * v) * dt;
        x += v * dt;

        if (Mathf.Abs(x) > limit)
        {
            x = Mathf.Sign(x) * limit;

            if (v * x > 0f)
                v = 0f;
        }
    }
}