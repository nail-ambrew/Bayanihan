using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// The looks a player can pick for the mission in Settings, applied to the global post-processing Volume of whatever
/// scene is loaded (the gameplay maps have one; the menus do not, so they are left alone). Each look sets the same
/// overrides, so switching between them in the pause menu changes the picture at once and leaves nothing behind.
/// </summary>
public static class ColorGrade
{
    /// <summary>What the Settings page offers, in order. Index 0 is the default.</summary>
    public static readonly string[] Names = { "DEFAULT", "VIBRANT", "STORMY" };

    struct Look
    {
        public float exposure, contrast, saturation, temperature, vignette;
        public Color filter;
    }

    static readonly Look[] looks =
    {
        // DEFAULT: the storm with its colour back: brighter, livelier, a little warmer than the grey storm light.
        new Look { exposure = 0.3f, contrast = 18f, saturation = 22f, temperature = 8f, vignette = 0.25f, filter = new Color(1f, 0.975f, 0.94f) },
        // VIBRANT: bolder colour and contrast, for small screens and bright rooms.
        new Look { exposure = 0.35f, contrast = 26f, saturation = 45f, temperature = 6f, vignette = 0.2f, filter = new Color(1f, 0.985f, 0.96f) },
        // STORMY: the original grey, darker typhoon mood.
        new Look { exposure = -0.4f, contrast = 0f, saturation = -25f, temperature = 0f, vignette = 0.2f, filter = Color.white },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyToScene();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToScene();

    /// <summary>Puts the chosen look on every global Volume that is loaded. Safe to call when there is none.</summary>
    public static void ApplyToScene()
    {
        if (!Application.isPlaying) return;      // edits the Volumes' runtime copies, never the profile assets
        var look = looks[GameSettings.ColorGrade];
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (volume.isGlobal && (volume.HasInstantiatedProfile() || volume.sharedProfile != null))
                Apply(volume.profile, look);      // .profile is the Volume's own runtime copy
    }

    static void Apply(VolumeProfile profile, Look look)
    {
        if (!profile.TryGet<ColorAdjustments>(out var colour)) colour = profile.Add<ColorAdjustments>();
        colour.active = true;
        colour.postExposure.Override(look.exposure);
        colour.contrast.Override(look.contrast);
        colour.saturation.Override(look.saturation);
        colour.colorFilter.Override(look.filter);

        if (!profile.TryGet<WhiteBalance>(out var balance)) balance = profile.Add<WhiteBalance>();
        balance.active = true;
        balance.temperature.Override(look.temperature);

        if (!profile.TryGet<Vignette>(out var vignette)) vignette = profile.Add<Vignette>();
        vignette.active = true;
        vignette.intensity.Override(look.vignette);
    }
}
