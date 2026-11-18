using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The main menu's moving background: Scene_MenuCinematic, loaded on top of the MainMenu scene
/// (<see cref="MenuBackdrop"/>). A slice of the real Mabuhay map in the storm, five shots on a ~20 s Timeline that
/// loops with a crossfade from the last shot back into the first. With Reduce Motion on it holds on a still frame.
///
/// When it starts it takes over the picture: the MainMenu scene's own camera is switched off, this scene becomes the
/// active one (so its sky, fog and light are used), the crew are spawned at their spots, and the rain follows the
/// quality preset.
/// </summary>
public class MenuCinematic : MonoBehaviour
{
    public static MenuCinematic Current { get; private set; }

    public CutsceneManager manager;
    public TyphoonWeather weather;

    [Header("Sky, fog and ambient light (the map's; set by the builder)")]
    public Material skybox;
    public bool fog = true;
    public Color fogColor = Color.grey;
    public FogMode fogMode = FogMode.ExponentialSquared;
    public float fogDensity = 0.01f;
    public UnityEngine.Rendering.AmbientMode ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
    public float ambientIntensity = 1f;
    public Color ambientLight = Color.grey;
    public Light sun;

    public bool Started { get; private set; }
    Camera[] hidden = new Camera[0];
    (Material sky, bool fog, Color fogColor, FogMode mode, float density, UnityEngine.Rendering.AmbientMode ambient, float intensity, Color light, Light sun) before;

    Scene appliedTo;

    void Awake()
    {
        Current = this;
        appliedTo = SceneManager.GetActiveScene();
        // Our sky and light, applied over the menu scene's (which stays the active scene, so the game's own scene
        // logic is unchanged); put back when we go. Done in Awake: TyphoonWeather copies the sky in its Start.
        before = (RenderSettings.skybox, RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogMode, RenderSettings.fogDensity,
                  RenderSettings.ambientMode, RenderSettings.ambientIntensity, RenderSettings.ambientLight, RenderSettings.sun);
        if (skybox != null) RenderSettings.skybox = skybox;
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientLight = ambientLight;
        if (sun != null) RenderSettings.sun = sun;
        DynamicGI.UpdateEnvironment();
    }

    void Start()
    {

        // The menu scene's own camera would draw over (or under) ours: off while we are here.
        var others = new System.Collections.Generic.List<Camera>();
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (cam.gameObject.scene != gameObject.scene && cam.enabled) { cam.enabled = false; others.Add(cam); }
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener.gameObject.scene != gameObject.scene) listener.enabled = false;
        hidden = others.ToArray();

        foreach (var spot in GetComponentsInChildren<CharacterSpot>(true)) spot.Spawn();
        foreach (var spot in FindObjectsByType<CharacterSpot>(FindObjectsSortMode.None))
            if (spot.gameObject.scene == gameObject.scene) spot.Spawn();

        if (weather != null)
        {
            weather.autoLightning = false;              // strikes on the Timeline's cue only
            weather.rainRateScale = CinematicQuality.Current.rainRate / Mathf.Max(1f, weather.rainMaxRate);
        }
        if (manager != null) manager.Play();
        Started = true;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        foreach (var cam in hidden) if (cam != null) cam.enabled = true;
        if (SceneManager.GetActiveScene() != appliedTo) return;     // leaving the menu: the next scene has its own sky
        RenderSettings.skybox = before.sky;
        RenderSettings.fog = before.fog;
        RenderSettings.fogColor = before.fogColor;
        RenderSettings.fogMode = before.mode;
        RenderSettings.fogDensity = before.density;
        RenderSettings.ambientMode = before.ambient;
        RenderSettings.ambientIntensity = before.intensity;
        RenderSettings.ambientLight = before.light;
        RenderSettings.sun = before.sun;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;
}

/// <summary>Loads the menu cinematic under the main menu, or reports that the still background should be used.</summary>
public static class MenuBackdrop
{
    public const string SceneName = "Scene_MenuCinematic";

    public static bool Available => Application.CanStreamedLevelBeLoaded(SceneName);

    /// <summary>Starts loading it (if it is in the build and not loaded yet). False: use the still background.</summary>
    public static bool Load()
    {
        if (!Available) return false;
        if (SceneManager.GetSceneByName(SceneName).isLoaded) return true;
        return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive) != null;
    }
}
