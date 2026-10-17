using System.Collections;
using UnityEngine;

/// <summary>
/// One-stop typhoon atmosphere: falling rain that follows the viewer, darker sky and light, fog,
/// lightning flashes with thunder, and rougher seas (it scales the WaterSurface waves).
/// Put this on an empty GameObject called "Weather". Everything is built in code - no assets needed
/// (rain sound / thunder clips are optional).
/// </summary>
public class TyphoonWeather : MonoBehaviour
{
    [Header("Storm strength")]
    [Range(0f, 1f)] public float intensity = 0.7f;
    [Tooltip("0 = constant storm. Above 0 = the storm grows from Start Intensity up to Intensity over this many seconds (nice for a mission that gets worse).")]
    public float buildUpSeconds = 0f;
    [Range(0f, 1f)] public float startIntensity = 0.2f;

    [Header("Rain")]
    [Tooltip("What the rain follows. Leave empty to follow the Main Camera, so it rains on the player whether " +
             "they are driving the boat or swimming away from it.")]
    public Transform followTarget;
    public float rainMaxRate = 3500f;      // particles per second at full intensity
    public float rainArea = 60f;           // size of the rain box around the target (metres)
    public float rainHeight = 20f;         // how high above the target it spawns
    public float rainSpeed = 30f;
    public Color rainColor = new Color(0.78f, 0.86f, 1f, 0.4f);
    [Tooltip("Optional. Leave empty to use a simple built-in unlit material.")]
    public Material rainMaterial;
    [Header("Wind")]
    [Range(0f, 360f)] public float windDirection = 45f;
    [Tooltip("How far the rain leans at full intensity (degrees)")]
    [Range(0f, 45f)] public float windTilt = 18f;

    [Header("Sky, light & fog")]
    public Light sun;                                               // auto-found if empty
    [Range(0.05f, 1f)] public float sunIntensityAtMax = 0.35f;      // multiplier on the original brightness
    public Color stormLightColor = new Color(0.6f, 0.68f, 0.78f);
    public Color stormFogColor = new Color(0.42f, 0.5f, 0.58f);
    public float fogDensityAtMax = 0.018f;
    [Range(0.1f, 1f)] public float skyExposureAtMax = 0.45f;        // only if your skybox has an _Exposure property

    [Header("Rough sea")]
    [Tooltip("Wave height multiplier at full intensity")]
    public float waveAmplitudeAtMax = 2f;
    public float waveSpeedAtMax = 1.5f;
    [Tooltip("Flood current multiplier at full intensity (needs a FloodCurrent in the scene)")]
    public float currentAtMax = 1.5f;

    [Header("Lightning")]
    [Range(0f, 1f)] public float lightningThreshold = 0.5f;   // storm must be at least this strong
    public Vector2 lightningInterval = new Vector2(6f, 16f);  // seconds between strikes (at full intensity)
    public float flashBrightness = 3f;

    [Header("Audio (optional)")]
    public AudioClip rainLoop;
    [Range(0f, 1f)] public float rainVolume = 0.6f;
    public AudioClip[] thunderClips;

    [Header("Loadout")]
    [Tooltip("With the Kapote (raincoat) packed, fog and rain are scaled by this (0.45 = 55% less visibility penalty).")]
    [Range(0.1f, 1f)] public float raincoatVisibilityFactor = 0.45f;

    // --- internals ---
    float current;
    float elapsed;
    float flash;

    ParticleSystem rain;
    Transform rainTf;
    Material builtRainMaterial;
    Camera cam;
    AudioSource loopSource, oneShotSource;

    float baseSunIntensity, baseAmbient;
    Color baseSunColor, baseFogColor;
    bool baseFog;
    float baseFogDensity;
    FogMode baseFogMode;
    Material skyMat;
    float baseSkyExposure;

    float[] baseAmp, baseSpeed;

    void Start()
    {
        // Kapote: everyone in the boat sees further through the rain
        if (LoadoutState.Has(RescueItemId.Raincoat))
        {
            fogDensityAtMax *= raincoatVisibilityFactor;
            rainColor.a *= 0.6f;
        }

        // Sun
        if (sun == null) sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) { baseSunIntensity = sun.intensity; baseSunColor = sun.color; }

        // Ambient / fog originals
        baseAmbient = RenderSettings.ambientIntensity;
        baseFog = RenderSettings.fog;
        baseFogDensity = RenderSettings.fogDensity;
        baseFogColor = RenderSettings.fogColor;
        baseFogMode = RenderSettings.fogMode;

        // Skybox: work on a copy so the asset isn't modified
        if (RenderSettings.skybox != null)
        {
            skyMat = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyMat;
            if (skyMat.HasProperty("_Exposure")) baseSkyExposure = skyMat.GetFloat("_Exposure");
        }

        // Waves
        if (WaterSurface.Instance != null)
        {
            var w = WaterSurface.Instance.waves;
            baseAmp = new float[w.Length];
            baseSpeed = new float[w.Length];
            for (int i = 0; i < w.Length; i++) { baseAmp[i] = w[i].amplitude; baseSpeed[i] = w[i].speed; }
        }

        BuildRain();
        BuildAudio();
        current = buildUpSeconds > 0f ? startIntensity : intensity;

        StartCoroutine(LightningRoutine());
    }

    void OnDestroy()
    {
        // Both are copies made in Start; nothing else owns them.
        if (skyMat != null) Destroy(skyMat);
        if (builtRainMaterial != null) Destroy(builtRainMaterial);
    }

    /// <summary>Where the rain box is centred: the override if one is set, otherwise wherever the player is looking from.</summary>
    Transform RainAnchor()
    {
        if (followTarget != null) return followTarget;
        if (cam == null) cam = Camera.main;
        return cam != null ? cam.transform : transform;
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        // Storm strength
        if (buildUpSeconds > 0f)
            current = Mathf.Lerp(startIntensity, intensity, Mathf.Clamp01(elapsed / buildUpSeconds));
        else
            current = Mathf.MoveTowards(current, intensity, Time.deltaTime * 0.5f);

        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 6f);

        UpdateRain();
        UpdateAtmosphere();
        UpdateWaves();
        UpdateAudio();
    }

    // ------------------------------------------------------------------ rain
    void BuildRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        rainTf = go.transform;

        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rain.main;
        main.duration = 5f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = (rainHeight + 6f) / rainSpeed;
        main.startSpeed = rainSpeed;
        main.startSize = 0.06f;
        main.startColor = rainColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 10000;

        var em = rain.emission;
        em.rateOverTime = 0f;

        var shape = rain.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(rainArea, rainArea, 1f);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 6f;
        r.velocityScale = 0.02f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        if (rainMaterial != null) r.sharedMaterial = rainMaterial;
        else
        {
            var sh = Shader.Find("Sprites/Default"); // simple unlit alpha shader, works in URP
            if (sh != null)
            {
                builtRainMaterial = new Material(sh);
                r.sharedMaterial = builtRainMaterial;
            }
        }

        rain.Play();
    }

    void UpdateRain()
    {
        if (rain == null) return;

        var em = rain.emission;
        em.rateOverTime = current < 0.02f ? 0f : rainMaxRate * current;

        // follow the viewer, lean with the wind
        rainTf.position = RainAnchor().position + Vector3.up * rainHeight;
        float tilt = windTilt * current;
        rainTf.rotation = Quaternion.Euler(0f, windDirection, 0f) * Quaternion.Euler(90f - tilt, 0f, 0f);
    }

    // ------------------------------------------------------------------ sky / light / fog
    void UpdateAtmosphere()
    {
        float f = flash;

        if (sun != null)
        {
            float dim = Mathf.Lerp(1f, sunIntensityAtMax, current);
            sun.intensity = baseSunIntensity * dim + flash * flashBrightness;
            sun.color = Color.Lerp(Color.Lerp(baseSunColor, stormLightColor, current), Color.white, f * 0.7f);
        }

        RenderSettings.ambientIntensity = Mathf.Lerp(baseAmbient, baseAmbient * 0.6f, current) + flash * 0.8f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Lerp(baseFog ? baseFogDensity : 0f, fogDensityAtMax, current);
        RenderSettings.fogColor = Color.Lerp(Color.Lerp(baseFogColor, stormFogColor, current), Color.white, f * 0.6f);

        if (skyMat != null && skyMat.HasProperty("_Exposure"))
            skyMat.SetFloat("_Exposure", Mathf.Lerp(baseSkyExposure, baseSkyExposure * skyExposureAtMax, current) + flash * 1.5f);
    }

    // ------------------------------------------------------------------ waves
    void UpdateWaves()
    {
        // A harder storm drives the flood harder
        if (FloodCurrent.Instance != null)
            FloodCurrent.Instance.StormScale = Mathf.Lerp(1f, currentAtMax, current);

        if (WaterSurface.Instance == null || baseAmp == null) return;
        var w = WaterSurface.Instance.waves;
        float amp = Mathf.Lerp(1f, waveAmplitudeAtMax, current);
        float spd = Mathf.Lerp(1f, waveSpeedAtMax, current);
        for (int i = 0; i < w.Length && i < baseAmp.Length; i++)
        {
            w[i].amplitude = baseAmp[i] * amp;
            w[i].speed = baseSpeed[i] * spd;
        }
    }

    // ------------------------------------------------------------------ lightning
    IEnumerator LightningRoutine()
    {
        while (true)
        {
            // stronger storm = more frequent strikes
            float scale = Mathf.Lerp(1.6f, 0.7f, current);
            yield return new WaitForSeconds(Random.Range(lightningInterval.x, lightningInterval.y) * scale);

            if (current < lightningThreshold) continue;

            int flickers = Random.Range(1, 4);
            for (int i = 0; i < flickers; i++)
            {
                flash = 1f;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
                flash = 0.2f;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }

            // thunder arrives a moment after the light
            if (thunderClips != null && thunderClips.Length > 0 && oneShotSource != null)
            {
                yield return new WaitForSeconds(Random.Range(0.4f, 2.5f));
                oneShotSource.PlayOneShot(thunderClips[Random.Range(0, thunderClips.Length)], Random.Range(0.6f, 1f));
            }
        }
    }

    // ------------------------------------------------------------------ audio
    void BuildAudio()
    {
        oneShotSource = gameObject.AddComponent<AudioSource>();
        oneShotSource.spatialBlend = 0f;
        oneShotSource.playOnAwake = false;

        if (rainLoop != null)
        {
            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.clip = rainLoop;
            loopSource.loop = true;
            loopSource.spatialBlend = 0f;
            loopSource.volume = 0f;
            loopSource.Play();
        }
    }

    void UpdateAudio()
    {
        if (loopSource != null) loopSource.volume = rainVolume * current;
    }

    /// <summary>Change the storm at runtime (e.g. from a mission event).</summary>
    public void SetIntensity(float value) { intensity = Mathf.Clamp01(value); buildUpSeconds = 0f; }
}