using UnityEngine;

/// <summary>
/// Nitro for any racer. Add it next to PlayerController.
/// Human: hold the nitro key. AI: RacerAI sets AiWantsBoost.
///
/// Charges by drifting (and slowly over time), drains while boosting,
/// and is cancelled by crashes and by drifting through curves.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class NitroBoost : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController racer;

    [Header("Input (human only)")]
    [SerializeField] private KeyCode nitroKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode alternativeKey = KeyCode.Space;

    [Header("Boost")]
    [SerializeField] private float maxNitro = 100f;
    [SerializeField] private float startingNitro = 50f;

    [Tooltip("Extra top speed while boosting (units/s).")]
    [SerializeField] private float boostSpeedBonus = 10f;

    [Tooltip("Acceleration multiplier while boosting.")]
    [SerializeField] private float boostAcceleration = 3f;

    [Tooltip("Nitro used per second while boosting.")]
    [SerializeField] private float drainPerSecond = 35f;

    [Tooltip("Nitro needed before a boost can START.")]
    [SerializeField] private float minNitroToStart = 20f;

    [Header("Charging")]
    [SerializeField] private float passiveChargePerSecond = 2f;
    [SerializeField] private float driftChargePerSecond = 22f;
    [SerializeField, Range(0f, 1f)] private float minDriftForCharge = 0.3f;

    [Header("Rules")]
    [Tooltip("Boost is cut while drifting harder than this (0..1).")]
    [SerializeField, Range(0f, 1f)] private float blockedWhileDrifting = 0.25f;

    [Tooltip("Seconds nitro stays disabled after a crash.")]
    [SerializeField] private float lockoutAfterImpact = 1f;

    [Header("Exhaust Flames")]
    [Tooltip("Optional. If empty the local offsets below are used.")]
    [SerializeField] private Transform leftExhaust;
    [SerializeField] private Transform rightExhaust;
    [SerializeField] private Vector3 leftExhaustOffset =
        new Vector3(-0.4f, -0.15f, -2.0f);
    [SerializeField] private Vector3 rightExhaustOffset =
        new Vector3(0.4f, -0.15f, -2.0f);
    [SerializeField] private float flameEmissionRate = 120f;
    [Tooltip("Optional. Falls back to an additive or Sprites/Default material.")]
    [SerializeField] private Material flameMaterial;

    [Header("Audio (optional)")]
    [SerializeField] private AudioClip boostLoop;
    [SerializeField, Range(0f, 1f)] private float boostVolume = 0.7f;

    private float charge;
    private float amount;       // smoothed 0..1 visual intensity
    private bool boosting;

    private ParticleSystem leftFlame;
    private ParticleSystem rightFlame;
    private AudioSource boostSource;

    /// <summary>Set by RacerAI. Ignored for the human player.</summary>
    public bool AiWantsBoost { get; set; }

    public bool IsBoosting => boosting;
    public float Charge01 => maxNitro > 0f ? charge / maxNitro : 0f;

    public void AddCharge(float value)
    {
        charge = Mathf.Clamp(charge + value, 0f, maxNitro);
    }

    private void Awake()
    {
        if (racer == null)
            racer = GetComponent<PlayerController>();

        charge = Mathf.Clamp(startingNitro, 0f, maxNitro);

        leftFlame = CreateFlame(
            "LeftNitroFlame", leftExhaust, leftExhaustOffset);

        rightFlame = CreateFlame(
            "RightNitroFlame", rightExhaust, rightExhaustOffset);

        if (boostLoop != null)
        {
            boostSource = gameObject.AddComponent<AudioSource>();
            boostSource.clip = boostLoop;
            boostSource.loop = true;
            boostSource.playOnAwake = false;
            boostSource.spatialBlend = 0f;
            boostSource.volume = 0f;
        }
    }

    private void OnDisable()
    {
        if (racer != null)
        {
            racer.BoostSpeed = 0f;
            racer.NitroAmount = 0f;
        }
    }

    private void Update()
    {
        if (racer == null)
            return;

        float dt = Time.deltaTime;

        bool wanted = racer.IsPlayerControlled
            ? (Input.GetKey(nitroKey) || Input.GetKey(alternativeKey))
            : AiWantsBoost;

        float slide = Mathf.Abs(racer.DriftSlide);

        bool canBoost =
            racer.TimeSinceImpact >= lockoutAfterImpact &&
            slide < blockedWhileDrifting;

        if (!boosting)
        {
            if (wanted && canBoost && charge >= minNitroToStart)
                boosting = true;
        }
        else if (!wanted || !canBoost || charge <= 0f)
        {
            boosting = false;
        }

        if (boosting)
        {
            charge -= drainPerSecond * dt;
        }
        else
        {
            charge += passiveChargePerSecond * dt;

            // Drifting fills the tank: risk and reward.
            if (slide >= minDriftForCharge)
                charge += driftChargePerSecond * slide * dt;
        }

        charge = Mathf.Clamp(charge, 0f, maxNitro);

        // Smooth visual intensity (used by flames, audio and camera).
        float blendSpeed = boosting ? 4f : 2.5f;

        amount = Mathf.MoveTowards(
            amount, boosting ? 1f : 0f, blendSpeed * dt);

        racer.BoostSpeed = boosting ? boostSpeedBonus : 0f;
        racer.BoostAcceleration = boostAcceleration;
        racer.NitroAmount = amount;

        UpdateFlames();
        UpdateAudio();
    }

    // ---------------------------------------------------------------
    // Effects
    // ---------------------------------------------------------------

    private void UpdateFlames()
    {
        SetFlameRate(leftFlame, amount * flameEmissionRate);
        SetFlameRate(rightFlame, amount * flameEmissionRate);
    }

    private static void SetFlameRate(ParticleSystem ps, float rate)
    {
        if (ps == null)
            return;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;
    }

    private void UpdateAudio()
    {
        if (boostSource == null)
            return;

        boostSource.volume = amount * boostVolume;
        boostSource.pitch = Mathf.Lerp(0.9f, 1.2f, amount);

        if (boostSource.volume > 0.01f && !boostSource.isPlaying)
            boostSource.Play();
        else if (boostSource.volume <= 0.01f && boostSource.isPlaying)
            boostSource.Stop();
    }

    private ParticleSystem CreateFlame(
        string objectName,
        Transform exhaust,
        Vector3 fallbackLocalOffset)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        go.transform.localPosition = exhaust != null
            ? transform.InverseTransformPoint(exhaust.position)
            : fallbackLocalOffset;

        // Cone emits along +Z, so turn it to point backwards.
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.5f, 0.8f, 1f, 1f),
            new Color(1f, 0.6f, 0.2f, 1f));
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 3f;
        shape.radius = 0.04f;

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        ParticleSystem.ColorOverLifetimeModule color =
            ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer psr =
            go.GetComponent<ParticleSystemRenderer>();

        psr.material = flameMaterial != null
            ? flameMaterial
            : GetFallbackMaterial();

        ps.Play();
        return ps;
    }

    private static Material GetFallbackMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        return shader != null ? new Material(shader) : null;
    }
}