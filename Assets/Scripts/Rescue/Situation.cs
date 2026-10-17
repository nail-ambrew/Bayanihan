using UnityEngine;

/// <summary>Where a resident is waiting. It decides how they can be reached and how fast they wear down.</summary>
public enum Situation
{
    /// <summary>Adrift in the flood and carried along by the current.</summary>
    OpenWater,
    /// <summary>Holding on to a pole or a fence: not drifting, but cannot hold on for long.</summary>
    Clinging,
    /// <summary>Standing on a roof, too high to be pulled from the water: a line has to reach them.</summary>
    Rooftop,
    /// <summary>Behind debris, cut off from the boat: a line has to reach over it, or somebody has to swim in.</summary>
    Trapped
}

/// <summary>The triage tag. Red is rescued first, Green can wait.</summary>
public enum Triage { Green, Yellow, Red }

/// <summary>How far a resident has got, for the roster and the report.</summary>
public enum ResidentStatus
{
    Waiting,        // not yet reached
    Held,           // a line or a swimmer has them, on their way to the boat
    Aboard,         // on the boat
    Ashore,         // off the boat, waiting to be registered
    Registered      // on the barangay's list: counted as safe
}

public static class TriageStyle
{
    public static Color Colour(Triage t)
    {
        switch (t)
        {
            case Triage.Red:    return new Color(0.93f, 0.22f, 0.20f);
            case Triage.Yellow: return new Color(1.00f, 0.82f, 0.18f);
            default:            return new Color(0.32f, 0.80f, 0.34f);
        }
    }

    public static string Label(Triage t)
    {
        switch (t)
        {
            case Triage.Red:    return "RED";
            case Triage.Yellow: return "YELLOW";
            default:            return "GREEN";
        }
    }
}
