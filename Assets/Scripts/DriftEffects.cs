using UnityEngine;

/// <summary>
/// Skid marks, tire smoke and a tire-screech loop while the player drifts.
/// Add this to the same object as PlayerController.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class DriftEffects : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;

    [Header("Rear Wheels")]
    [Tooltip("Optional. If empty, the offsets below are used.")]
    [SerializeField] private Transform leftRearWheel;
    [SerializeField] private Transform rightRearWheel;

    [Tooltip("Local position of the left rear tire contact point (y = ground).")]
    [SerializeField] private Vector3 leftWheelOffset =
        new Vector3(-0.85f, -0.47f, -1.4f);

    [SerializeField] private Vector3 rightWheelOffset =
        new Vector3(0.85f, -0.47f, -1.4f);

    [Header("Activation")]
    [Tooltip("Drift amount (0..1) needed before effects start.")]
    [SerializeField, Range(0f, 1f)] private float effectStartSlide = 0.3f;
    [SerializeField] private float minSpeed = 4f;

    [Header("Skid Marks")]
    [SerializeField] private bool useSkidMarks = true;
    [SerializeField] private float skidFadeTime = 5f;
    [SerializeField] private float skidWidth = 0.25f;
    [Range(0f, 1f)]
    [SerializeField] private float skidOpacity = 0.55f;
    [Tooltip("Optional. Falls back to Sprites/Default.")]
    [SerializeField] private Material skidMaterial;

    [Header("Tire Smoke")]
    [SerializeField] private bool useSmoke = true;
    [SerializeField] private float maxEmissionRate = 35f;
    [Tooltip("Optional soft round material looks much better.")]
    [SerializeField] private Material smokeMaterial;

    [Header("Audio (optional)")]
    [SerializeField] private AudioClip driftLoop;
    [SerializeField, Range(0f, 1f)] private float driftVolume = 0.7f;
    [SerializeField] private float audioFadeSpeed = 6f;

    private class Wheel
    {
        public TrailRenderer trail;
        public ParticleSystem smoke;
    }

    private readonly Wheel[] wheels = new Wheel[2];
    private AudioSource driftSource;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();

        wheels[0] = CreateWheel(
            "LeftRearDrift", leftRearWheel, leftWheelOffset);

        wheels[1] = CreateWheel(
            "RightRearDrift", rightRearWheel, rightWheelOffset);

        if (driftLoop != null)
        {
            driftSource = gameObject.AddComponent<AudioSource>();
            driftSource.clip = driftLoop;
            driftSource.loop = true;
            driftSource.playOnAwake = false;
            driftSource.spatialBlend = 0f;
            driftSource.volume = 0f;
        }
    }

    private void Update()
    {
        if (player == null)
            return;

        float slide = Mathf.Abs(player.DriftSlide);

        bool active =
            slide >= effectStartSlide &&
            player.CurrentForwardSpeed >= minSpeed;

        float intensity = active
            ? Mathf.InverseLerp(effectStartSlide, 1f, slide)
            : 0f;

        foreach (Wheel wheel in wheels)
        {
            if (wheel.trail != null)
                wheel.trail.emitting = active && useSkidMarks;

            if (wheel.smoke != null)
            {
                ParticleSystem.EmissionModule emission =
                    wheel.smoke.emission;

                emission.rateOverTime =
                    useSmoke ? intensity * maxEmissionRate : 0f;
            }
        }

        UpdateAudio(intensity);
    }

    private void UpdateAudio(float intensity)
    {
        if (driftSource == null)
            return;

        float targetVolume = intensity * driftVolume;

        driftSource.volume = Mathf.MoveTowards(
            driftSource.volume,
            targetVolume,
            audioFadeSpeed * Time.unscaledDeltaTime);

        driftSource.pitch = Mathf.Lerp(0.9f, 1.15f, intensity);

        if (driftSource.volume > 0.01f && !driftSource.isPlaying)
            driftSource.Play();
        else if (driftSource.volume <= 0.01f && driftSource.isPlaying)
            driftSource.Stop();
    }

    // ---------------------------------------------------------------
    // Setup
    // ---------------------------------------------------------------

    private Wheel CreateWheel(
        string objectName,
        Transform wheelTransform,
        Vector3 fallbackLocalOffset)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        // Use the assigned wheel's position, but keep the ground height.
        Vector3 local = wheelTransform != null
            ? transform.InverseTransformPoint(wheelTransform.position)
            : fallbackLocalOffset;

        local.y = fallbackLocalOffset.y;

        go.transform.localPosition = local;

        // Z axis points down so the skid ribbon lies flat on the road.
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Wheel wheel = new Wheel();
        wheel.trail = CreateTrail(go);
        wheel.smoke = CreateSmoke(go);

        return wheel;
    }

    private TrailRenderer CreateTrail(GameObject go)
    {
        TrailRenderer trail = go.AddComponent<TrailRenderer>();

        trail.time = Mathf.Max(0.5f, skidFadeTime);
        trail.startWidth = skidWidth;
        trail.endWidth = skidWidth;
        trail.minVertexDistance = 0.2f;
        trail.alignment = LineAlignment.TransformZ;
        trail.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.black, 0f),
                new GradientColorKey(Color.black, 1f)
            },
            new[]
            {
                new GradientAlphaKey(skidOpacity, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        trail.colorGradient = gradient;
        trail.material = skidMaterial != null
            ? skidMaterial
            : GetFallbackMaterial();

        return trail;
    }

    private ParticleSystem CreateSmoke(GameObject go)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.gravityModifier = -0.03f;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 0f),
                new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.35f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        ParticleSystem.ColorOverLifetimeModule color =
            ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));

        ParticleSystemRenderer psr =
            go.GetComponent<ParticleSystemRenderer>();

        psr.material = smokeMaterial != null
            ? smokeMaterial
            : GetFallbackMaterial();

        ps.Play();
        return ps;
    }

    private static Material GetFallbackMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        return shader != null ? new Material(shader) : null;
    }
}