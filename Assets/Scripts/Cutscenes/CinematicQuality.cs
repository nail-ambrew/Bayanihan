using UnityEngine;

/// <summary>
/// How much the cinematics draw, from the GRAPHICS setting (Low, Medium, High). Change the numbers here to retune a
/// preset; every cutscene reads <see cref="Current"/> when it starts.
/// </summary>
public struct CinematicQuality
{
    /// <summary>Rain particles per second at full storm (TyphoonWeather's own rate is 3500).</summary>
    public float rainRate;
    /// <summary>Floating debris pieces alive at once in the intro.</summary>
    public int debrisCap;
    /// <summary>Depth of field on close-ups.</summary>
    public bool depthOfField;
    /// <summary>Film grain and chromatic aberration.</summary>
    public bool filmEffects;
    /// <summary>Camera far clip (metres): less of the map drawn behind the subject.</summary>
    public float farClip;
    /// <summary>Particles per effect (spray, foam, sparks) are multiplied by this.</summary>
    public float effectScale;
    /// <summary>Grass and undergrowth draw distance during a cinematic (the map's foliage is most of its draw calls).</summary>
    public float foliageDistance;

    public static readonly CinematicQuality Low = new CinematicQuality
    { rainRate = 2500f, debrisCap = 16, depthOfField = false, filmEffects = false, farClip = 220f, effectScale = 0.4f, foliageDistance = 0.3f };

    public static readonly CinematicQuality Medium = new CinematicQuality
    { rainRate = 5000f, debrisCap = 28, depthOfField = true, filmEffects = false, farClip = 320f, effectScale = 0.7f, foliageDistance = 0.4f };

    public static readonly CinematicQuality High = new CinematicQuality
    { rainRate = 8000f, debrisCap = 40, depthOfField = true, filmEffects = true, farClip = 450f, effectScale = 1f, foliageDistance = 0.5f };

    public static CinematicQuality For(int setting) => setting <= 0 ? Low : setting == 1 ? Medium : High;

    public static CinematicQuality Current => For(GameSettings.Quality);
}
