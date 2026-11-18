using UnityEngine;

/// <summary>
/// The shared flood level for cutscenes and gameplay: metres added to the flood water's starting height. 0 in play (the
/// mission's own climb is <see cref="FloodRise"/>, on top of this). The intro starts it below the streets and brings it
/// up to 0 as the story goes, so it ends exactly where the mission begins; a Timeline drives it with a
/// <see cref="FloodLevelTrack"/>, and anything else can call <see cref="Set"/>.
///
/// With a <see cref="FloodRise"/> in the scene, that applies the offset each frame; without one (the menu cinematic),
/// <see cref="Set"/> moves the <see cref="WaterSurface"/> itself.
/// </summary>
public static class FloodLevel
{
    /// <summary>The intro's lowest water: just over the ground (y = -1) when the flood starts at y = 0.</summary>
    public const float Dry = -0.92f;

    /// <summary>Metres above (or below) the flood's starting height.</summary>
    public static float Offset { get; private set; }

    // What Set has moved the water by directly, when nothing else applies the offset.
    static WaterSurface movedWater;
    static float moved;

    public static void Set(float offset)
    {
        Offset = offset;
        if (!Application.isPlaying || FloodRise.Instance != null) return;
        var water = WaterSurface.Instance;
        if (water == null) return;
        if (water != movedWater) { movedWater = water; moved = 0f; }
        var p = water.transform.position;
        p.y += offset - moved;
        moved = offset;
        water.transform.position = p;
    }

    /// <summary>
    /// For <see cref="FloodRise"/> starting up: how far <see cref="Set"/> has already moved the water, so its base height
    /// can be found. From then on FloodRise applies the offset.
    /// </summary>
    public static float TakeOver()
    {
        float m = movedWater != null && movedWater == WaterSurface.Instance ? moved : 0f;
        movedWater = null;
        moved = 0f;
        return m;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Offset = 0f;
        movedWater = null;
        moved = 0f;
    }
}
