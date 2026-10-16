using System.Collections;
using UnityEngine;

/// <summary>
/// Plays the "juice" of a collision: hit-stop + slow-motion recovery,
/// camera shake, sparks, smoke and sound.
/// PlayerCollision creates this automatically if it is not in the scene.
/// </summary>
public class CollisionFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CameraFollow cameraFollow;

    [Header("Hit Stop / Slow Motion")]
    [SerializeField] private bool useHitStop = true;
    [SerializeField] private float minSeverityForHitStop = 0.35f;
    [SerializeField] private float hitStopTimeScale = 0.05f;
    [SerializeField] private float hitStopDuration = 0.07f;
    [SerializeField] private float slowMoTimeScale = 0.4f;
    [SerializeField] private float slowMoRecoverTime = 0.35f;

    [Header("Particles")]
    [Tooltip("Optional. Falls back to Sprites/Default.")]
    [SerializeField] private Material sparkMaterial;
    [Tooltip("Optional soft round texture material looks much better.")]
    [SerializeField] private Material smokeMaterial;
    [SerializeField] private int minSparks = 10;
    [SerializeField] private int maxSparks = 45;

    [Header("Audio (optional)")]
    [SerializeField] private AudioClip[] lightHitClips;
    [SerializeField] private AudioClip[] heavyHitClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

    private ParticleSystem sparks;
    private ParticleSystem smoke;
    private AudioSource audioSource;
    private Coroutine hitStopRoutine;
    private float lastSetTimeScale;

    private void Awake()
    {
        if (cameraFollow == null)
            cameraFollow = FindFirstObjectByType<CameraFollow>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        sparks = CreateSystem(
            "Sparks", 300, true, sparkMaterial, 1.6f,
            MakeGradient(
                new Color(1f, 0.95f, 0.6f, 1f),
                new Color(1f, 0.4f, 0.05f, 0f)),
            AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        smoke = CreateSystem(
            "Smoke", 100, false, smokeMaterial, -0.05f,
            MakeGradient(
                new Color(0.75f, 0.75f, 0.75f, 0.35f),
                new Color(0.5f, 0.5f, 0.5f, 0f)),
            AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
    }

    private void OnDisable()
    {
        if (hitStopRoutine != null)
        {
            Time.timeScale = 1f;
            hitStopRoutine = null;
        }
    }

    /// <param name="playerView">
    /// True when the human is involved: camera shake and hit-stop apply.
    /// False for AI-only crashes: sparks, smoke and a quieter sound only.
    /// </param>
    public void Play(ImpactInfo info, bool playerView = true)
    {
        float severity = Mathf.Clamp01(info.severity);

        if (playerView && cameraFollow != null)
            cameraFollow.AddImpact(severity, info.pushDirection, info.isSide);

        EmitSparks(info.worldPoint, info.worldVelocity, severity);
        EmitSmoke(info.worldPoint, info.worldVelocity, severity);

        if (info.isSide)
            StartCoroutine(ScrapeRoutine(info));

        PlaySound(playerView ? severity : severity * 0.5f);

        if (playerView &&
            useHitStop &&
            severity >= minSeverityForHitStop &&
            hitStopRoutine == null &&
            Time.timeScale > 0.5f)
        {
            hitStopRoutine = StartCoroutine(HitStopRoutine(severity));
        }
    }

    // ---------------------------------------------------------------
    // Hit stop
    // ---------------------------------------------------------------

    private IEnumerator HitStopRoutine(float severity)
    {
        float hold = hitStopDuration * Mathf.Lerp(0.6f, 1.4f, severity);

        lastSetTimeScale = hitStopTimeScale;
        Time.timeScale = lastSetTimeScale;

        yield return new WaitForSecondsRealtime(hold);

        float elapsed = 0f;
        float recover = Mathf.Max(0.05f, slowMoRecoverTime);

        while (elapsed < recover)
        {
            // Someone else (pause / game over) changed the time scale.
            if (Mathf.Abs(Time.timeScale - lastSetTimeScale) > 0.01f)
            {
                hitStopRoutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / recover);

            lastSetTimeScale = Mathf.Lerp(slowMoTimeScale, 1f, k * k);
            Time.timeScale = lastSetTimeScale;

            yield return null;
        }

        Time.timeScale = 1f;
        hitStopRoutine = null;
    }

    // ---------------------------------------------------------------
    // Particles
    // ---------------------------------------------------------------

    private IEnumerator ScrapeRoutine(ImpactInfo info)
    {
        Vector3 point = info.worldPoint;

        for (int i = 0; i < 4; i++)
        {
            yield return new WaitForSeconds(0.06f);

            point += info.worldVelocity * 0.06f;
            EmitSparks(point, info.worldVelocity, info.severity * 0.6f);
        }
    }

    private void EmitSparks(Vector3 point, Vector3 velocity, float severity)
    {
        if (sparks == null)
            return;

        int count = Mathf.RoundToInt(
            Mathf.Lerp(minSparks, maxSparks, severity));

        ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.8f + 0.1f;

            p.position = point + Random.insideUnitSphere * 0.15f;
            p.velocity =
                velocity * 0.7f +
                dir * Random.Range(3f, 9f) * (0.6f + severity);
            p.startLifetime = Random.Range(0.25f, 0.6f);
            p.startSize = Random.Range(0.04f, 0.1f);
            p.startColor = Color.Lerp(
                new Color(1f, 0.9f, 0.4f),
                new Color(1f, 0.5f, 0.1f),
                Random.value);

            sparks.Emit(p, 1);
        }
    }

    private void EmitSmoke(Vector3 point, Vector3 velocity, float severity)
    {
        if (smoke == null)
            return;

        int count = 3 + Mathf.RoundToInt(severity * 6f);

        ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            p.position = point + Random.insideUnitSphere * 0.3f;
            p.velocity =
                velocity * 0.3f +
                Vector3.up * Random.Range(0.5f, 1.5f) +
                Random.insideUnitSphere * 0.6f;
            p.startLifetime = Random.Range(0.8f, 1.4f);
            p.startSize = Random.Range(1.2f, 2.2f);
            p.startColor = new Color(0.75f, 0.75f, 0.75f, 0.35f);

            smoke.Emit(p, 1);
        }
    }

    private ParticleSystem CreateSystem(
        string objectName,
        int maxParticles,
        bool stretched,
        Material material,
        float gravity,
        Gradient colorOverLife,
        AnimationCurve sizeOverLife)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(colorOverLife);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLife);

        ParticleSystemRenderer psr =
            go.GetComponent<ParticleSystemRenderer>();

        psr.material = material != null ? material : GetFallbackMaterial();

        if (stretched)
        {
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.06f;
            psr.lengthScale = 1.5f;
        }

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

    private static Gradient MakeGradient(Color start, Color end)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(end.a, 1f)
            });
        return g;
    }

    // ---------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------

    private void PlaySound(float severity)
    {
        AudioClip[] bank =
            (severity > 0.5f && heavyHitClips != null && heavyHitClips.Length > 0)
                ? heavyHitClips
                : lightHitClips;

        if (bank == null || bank.Length == 0)
            return;

        audioSource.pitch = Random.Range(0.92f, 1.08f);
        audioSource.PlayOneShot(
            bank[Random.Range(0, bank.Length)],
            volume * Mathf.Lerp(0.5f, 1f, severity));
    }
}