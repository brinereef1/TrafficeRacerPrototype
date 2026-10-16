using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Position")]
    [SerializeField] private Vector3 offset =
        new Vector3(0f, 5f, -9f);

    [Header("Camera Settings")]
    [SerializeField] private float positionSmoothTime = 0.18f;
    [SerializeField] private float rotationSmoothSpeed = 8f;
    [SerializeField] private float lookAheadDistance = 10f;
    [SerializeField] private float lookHeight = 1.5f;

    [Header("Impact Shake")]
    [SerializeField] private float maxShakeOffset = 0.35f;
    [SerializeField] private float maxShakeAngle = 3f;
    [SerializeField] private float shakeFrequency = 25f;
    [Tooltip("How fast the shake fades (trauma per second).")]
    [SerializeField] private float traumaDecay = 1.8f;

    [Header("Impact Recoil")]
    [SerializeField] private float recoilStrength = 3f;
    [SerializeField] private float recoilStiffness = 60f;
    [SerializeField] private float recoilDamping = 8f;

    [Header("Impact FOV Kick")]
    [SerializeField] private float fovKick = 7f;
    [SerializeField] private float fovRecovery = 4f;

    [Header("Drift")]
    [Tooltip("Camera roll (degrees) at full drift.")]
    [SerializeField] private float driftCameraRoll = 2.5f;
    [Tooltip("Extra field of view (degrees) at full drift.")]
    [SerializeField] private float driftFovBoost = 5f;

    [Header("Nitro")]
    [Tooltip("Extra field of view at full nitro (degrees).")]
    [SerializeField] private float nitroFovBoost = 14f;

    [Tooltip("Camera drops back by this much at full nitro (stretch feel).")]
    [SerializeField] private float nitroPullBack = 1.8f;

    [Tooltip("Camera lowers slightly at full nitro.")]
    [SerializeField] private float nitroLower = 0.3f;

    [Tooltip("Constant light vibration at full nitro (0..1).")]
    [SerializeField, Range(0f, 1f)] private float nitroShake = 0.15f;

    [Tooltip("How fast the camera blends in and out of the nitro look.")]
    [SerializeField] private float nitroBlendSpeed = 3f;

    [SerializeField] private bool useSpeedLines = true;

    private PlayerController player;
    private NitroSpeedLines speedLines;
    private float nitroAmount;

    private Vector3 smoothVelocity;
    private Vector3 smoothPosition;
    private Quaternion smoothRotation;
    private bool initialized;

    private Camera cam;
    private float baseFov;
    private float fovBoost;

    private float trauma;
    private Vector3 recoilOffset;
    private Vector3 recoilVelocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (cam != null)
            baseFov = cam.fieldOfView;

        if (target != null)
            player = target.GetComponentInParent<PlayerController>();

        if (useSpeedLines)
        {
            speedLines = GetComponent<NitroSpeedLines>();

            if (speedLines == null)
                speedLines = gameObject.AddComponent<NitroSpeedLines>();
        }
    }

    /// <summary>Called by CollisionFeedback when the player is hit.</summary>
    public void AddImpact(float severity, float pushDirection, bool isSide)
    {
        severity = Mathf.Clamp01(severity);

        trauma = Mathf.Min(
            1f,
            trauma + Mathf.Lerp(0.35f, 0.9f, severity));

        // The camera lags against the push, and jolts back on frontal hits.
        recoilVelocity += new Vector3(
            -pushDirection * recoilStrength * severity * (isSide ? 1f : 0.4f),
            0f,
            -recoilStrength * severity * (isSide ? 0.3f : 1f));

        fovBoost = Mathf.Max(fovBoost, fovKick * severity);
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (!initialized)
        {
            smoothPosition = transform.position;
            smoothRotation = transform.rotation;
            initialized = true;
        }

        // Follow the road heading, so the car's drift angle stays
        // visible on screen instead of being cancelled by the camera.
        Quaternion followRotation = player != null
            ? player.CameraRotation
            : target.rotation;

        // Blend into the nitro look (uses real time so it stays smooth
        // even during the crash slow-motion).
        float nitroTarget = player != null ? player.NitroAmount : 0f;

        nitroAmount = Mathf.MoveTowards(
            nitroAmount,
            nitroTarget,
            nitroBlendSpeed * Time.unscaledDeltaTime);

        // The camera drops back and slightly lower for a stretched feel.
        Vector3 dynamicOffset = offset + new Vector3(
            0f,
            -nitroLower * nitroAmount,
            -nitroPullBack * nitroAmount);

        // Keep the camera behind and above the player.
        Vector3 desiredPosition =
            target.position + followRotation * dynamicOffset;

        smoothPosition = Vector3.SmoothDamp(
            smoothPosition,
            desiredPosition,
            ref smoothVelocity,
            positionSmoothTime
        );

        // Look toward the road ahead.
        Vector3 lookTarget =
            target.position
            + (followRotation * Vector3.forward) * lookAheadDistance
            + Vector3.up * lookHeight;

        Vector3 direction = lookTarget - smoothPosition;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(direction, Vector3.up);

            smoothRotation = Quaternion.Slerp(
                smoothRotation,
                desiredRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }

        // Impact effects use unscaled time so they stay snappy
        // during the hit-stop slow motion.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);

        // Recoil spring.
        recoilVelocity +=
            (-recoilStiffness * recoilOffset
             - recoilDamping * recoilVelocity) * dt;
        recoilOffset += recoilVelocity * dt;

        Vector3 finalPosition =
            smoothPosition + smoothRotation * recoilOffset;

        // Horizon tilts a little while drifting.
        float driftSlide = player != null ? player.DriftSlide : 0f;

        Quaternion finalRotation =
            smoothRotation *
            Quaternion.Euler(0f, 0f, -driftSlide * driftCameraRoll);

        // Perlin shake, squared trauma feels more natural.
        trauma = Mathf.MoveTowards(trauma, 0f, traumaDecay * dt);
        float shake = Mathf.Max(
            trauma * trauma,
            nitroAmount * nitroShake);

        if (shake > 0.0001f)
        {
            float t = Time.unscaledTime * shakeFrequency;

            Vector3 noise = new Vector3(
                Noise(t, 0f), Noise(t, 17f), Noise(t, 31f));

            finalPosition +=
                smoothRotation * (noise * maxShakeOffset * shake);

            finalRotation = finalRotation * Quaternion.Euler(
                Noise(t, 47f) * maxShakeAngle * shake,
                Noise(t, 59f) * maxShakeAngle * shake,
                Noise(t, 71f) * maxShakeAngle * 1.5f * shake);
        }

        transform.SetPositionAndRotation(finalPosition, finalRotation);

        if (cam != null)
        {
            fovBoost = Mathf.Lerp(fovBoost, 0f, fovRecovery * dt);
            cam.fieldOfView =
                baseFov +
                fovBoost +
                Mathf.Abs(driftSlide) * driftFovBoost +
                nitroAmount * nitroFovBoost;

            if (speedLines != null)
                speedLines.SetAmount(nitroAmount);
        }
    }

    private static float Noise(float t, float seed)
    {
        return Mathf.PerlinNoise(t, seed) * 2f - 1f;
    }
}