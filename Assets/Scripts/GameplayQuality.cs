using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// How much the missions draw, from the GRAPHICS setting (Low, Medium, High). Each phone build has only one Unity quality
/// level (Mobile) and each PC build only one (PC), so the setting does its work here, at run time: a shorter view and
/// thicker fog, fewer shadows, a harder house cull and less grass and undergrowth on Low. High changes nothing, so the PC
/// picture is the one the map was built for. Change the numbers here to retune a preset (see "Running on phones" in
/// Docs/Project/Overview.md for what each one costs).
/// </summary>
public struct GameplayQuality
{
    /// <summary>Camera far clip in metres; 0 leaves the camera's own.</summary>
    public float farClip;
    /// <summary>The least fog density (exponential squared) while the far clip is short, so the map fades out before it is cut off; 0 = the weather's own.</summary>
    public float fogFloor;
    /// <summary>Whether the gameplay camera draws shadows.</summary>
    public bool shadows;
    /// <summary>The LOD bias (0 = the quality level's own: 1 on phones, 2 on PC). Lower drops the row houses and other LOD-culled objects out nearer.</summary>
    public float lodBias;
    /// <summary>Multiplies every grass and undergrowth draw distance.</summary>
    public float foliageDistance;
    /// <summary>The share of grass tufts and plants drawn (0..1); the rest are skipped evenly, not by area.</summary>
    public float foliageDensity;
    /// <summary>Whether the gameplay camera may use MSAA.</summary>
    public bool msaa;

    public static readonly GameplayQuality Low = new GameplayQuality
    { farClip = 140f, fogFloor = 0.0125f, shadows = false, lodBias = 0.3f, foliageDistance = 0.25f, foliageDensity = 0.2f, msaa = false };

    public static readonly GameplayQuality Medium = new GameplayQuality
    { farClip = 280f, fogFloor = 0.0062f, shadows = true, lodBias = 0.7f, foliageDistance = 0.6f, foliageDensity = 0.7f, msaa = true };

    public static readonly GameplayQuality High = new GameplayQuality
    { farClip = 0f, fogFloor = 0f, shadows = true, lodBias = 0f, foliageDistance = 1f, foliageDensity = 1f, msaa = true };

    public static GameplayQuality For(int setting) => setting <= 0 ? Low : setting == 1 ? Medium : High;

    public static GameplayQuality Current => For(GameSettings.Quality);

    // ------------------------------------------------------------------ applying it

    static GameplayQuality applied = High;
    static float baseLodBias = -1f;
    static int baseLevel = -1;
    static readonly HashSet<Camera> shadowsOff = new HashSet<Camera>();
    static readonly HashSet<Camera> msaaOff = new HashSet<Camera>();
    static readonly Dictionary<Camera, (float own, float set)> farOwn = new Dictionary<Camera, (float own, float set)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        applied = High;
        baseLodBias = -1f;
        baseLevel = -1;
        shadowsOff.Clear();
        msaaOff.Clear();
        farOwn.Clear();
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        Application.onBeforeRender -= BeforeRender;
        Application.onBeforeRender += BeforeRender;
        Application.quitting -= Restore;
        Application.quitting += Restore;
    }

    /// <summary>Puts the current preset into effect (GameSettings.Apply calls this when the game starts and when the setting changes).</summary>
    public static void Apply() => Apply(Current);

    /// <summary>Puts a preset into effect: the global parts now, the per-camera parts as each camera renders.</summary>
    public static void Apply(GameplayQuality q)
    {
        applied = q;
        // Only in play: QualitySettings changed in the Editor are saved with the project. Even in Play mode they outlive
        // it, so the level's own bias is remembered (again whenever the level changes) and put back on quit.
        if (!Application.isPlaying) return;
        int level = QualitySettings.GetQualityLevel();
        if (baseLodBias < 0f || level != baseLevel) { baseLodBias = QualitySettings.lodBias; baseLevel = level; }
        QualitySettings.lodBias = q.lodBias > 0f ? q.lodBias : baseLodBias;
        InstancedFoliage.QualityScale = q.foliageDistance;
        InstancedFoliage.DensityScale = q.foliageDensity;
    }

    /// <summary>The least fog density the mission's weather may set now (0 = no floor): the preset's, in a mission.</summary>
    public static float FogFloor => RescueManager.Instance != null ? applied.fogFloor : 0f;

    /// <summary>The preset in effect now.</summary>
    public static GameplayQuality Applied => applied;

    static void Restore()
    {
        if (baseLodBias >= 0f && QualitySettings.GetQualityLevel() == baseLevel) QualitySettings.lodBias = baseLodBias;
        baseLodBias = -1f;
        InstancedFoliage.QualityScale = 1f;
        InstancedFoliage.DensityScale = 1f;
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        Application.onBeforeRender -= BeforeRender;
    }

    // The fog is read for the frame before any camera starts (too late in BeforeCamera). The weather applies the floor
    // itself (TyphoonWeather); this covers a mission without weather, just before the frame is drawn.
    static void BeforeRender()
    {
        var q = applied;
        if (q.fogFloor > 0f && RescueManager.Instance != null && RenderSettings.fog
            && RenderSettings.fogMode == FogMode.ExponentialSquared && RenderSettings.fogDensity < q.fogFloor)
            RenderSettings.fogDensity = q.fogFloor;
    }

    // Only the game's main camera in a mission: the menu, the loadout stage and the Scene view keep their own settings,
    // and the cinematics have CinematicQuality. Cinemachine writes the lens every LateUpdate, so the clamp runs every frame.
    static void BeforeCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType != CameraType.Game || RescueManager.Instance == null || !cam.CompareTag("MainCamera")) return;
        var q = applied;

        // Cinemachine sets the lens again every frame. A camera without it would keep the clamped value, so the clip it had
        // is remembered while it is clamped, to give it back when the setting goes up.
        float own = cam.farClipPlane;
        if (farOwn.TryGetValue(cam, out var was) && Mathf.Approximately(cam.farClipPlane, was.set)) own = was.own;
        float target = q.farClip > 0f ? Mathf.Min(own, q.farClip) : own;
        cam.farClipPlane = target;
        if (target < own) farOwn[cam] = (own, target); else farOwn.Remove(cam);

        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            if (!q.shadows && data.renderShadows) { data.renderShadows = false; shadowsOff.Add(cam); }
            else if (q.shadows && shadowsOff.Remove(cam)) data.renderShadows = true;
        }
        if (!q.msaa && cam.allowMSAA) { cam.allowMSAA = false; msaaOff.Add(cam); }
        else if (q.msaa && msaaOff.Remove(cam)) cam.allowMSAA = true;
    }
}
