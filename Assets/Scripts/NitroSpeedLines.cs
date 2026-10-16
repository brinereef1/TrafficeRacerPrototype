using UnityEngine;

/// <summary>
/// Speed streaks that rush past the edges of the screen while boosting.
/// CameraFollow adds this to the camera automatically.
/// </summary>
public class NitroSpeedLines : MonoBehaviour
{
    [Tooltip("Optional. Falls back to Sprites/Default.")]
    [SerializeField] private Material lineMaterial;

    [SerializeField] private float maxEmissionRate = 90f;
    [SerializeField] private float ringRadius = 7f;
    [SerializeField] private float distanceAhead = 20f;
    [SerializeField] private float lineSpeed = 80f;
    [SerializeField, Range(0f, 1f)] private float maxOpacity = 0.35f;

    private ParticleSystem lines;

    private void Awake()
    {
        Build();
    }

    /// <summary>0 = off, 1 = full nitro.</summary>
    public void SetAmount(float amount)
    {
        if (lines == null)
            return;

        amount = Mathf.Clamp01(amount);

        ParticleSystem.EmissionModule emission = lines.emission;
        emission.rateOverTime = amount * maxEmissionRate;

        ParticleSystem.MainModule main = lines.main;
        main.startColor = new Color(
            1f, 1f, 1f, maxOpacity * Mathf.Clamp01(amount * 1.5f));
    }

    private void Build()
    {
        GameObject go = new GameObject("NitroSpeedLines");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, distanceAhead);
        go.transform.localRotation = Quaternion.identity;

        lines = go.AddComponent<ParticleSystem>();
        lines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = lines.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 0.28f;
        main.startSpeed = 0f;
        main.startSize = 0.06f;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = lines.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        // Ring near the edge of the view.
        ParticleSystem.ShapeModule shape = lines.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = ringRadius;
        shape.radiusThickness = 0.15f;

        // Rush toward the camera.
        ParticleSystem.VelocityOverLifetimeModule velocity =
            lines.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(-lineSpeed);

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0f, 1f)
            });

        ParticleSystem.ColorOverLifetimeModule color =
            lines.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystemRenderer psr =
            go.GetComponent<ParticleSystemRenderer>();

        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.velocityScale = 0.05f;
        psr.lengthScale = 1f;
        psr.cameraVelocityScale = 0f;
        psr.material = lineMaterial != null
            ? lineMaterial
            : GetFallbackMaterial();

        lines.Play();
    }

    private static Material GetFallbackMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        return shader != null ? new Material(shader) : null;
    }
}