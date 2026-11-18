using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Photosensitivity guard for lightning and other full-screen flashes: at most <see cref="MaxPerSecond"/> flashes in any
/// one second, and none with Reduce Motion on. Everything that flashes the screen asks <see cref="Allow"/> first.
/// </summary>
public static class FlashLimiter
{
    public const int MaxPerSecond = 3;

    static readonly Queue<float> recent = new Queue<float>();

    /// <summary>True if a flash may start now; it is then counted.</summary>
    public static bool Allow()
    {
        if (GameSettings.ReduceMotion) return false;
        float now = Time.unscaledTime;
        while (recent.Count > 0 && now - recent.Peek() >= 1f) recent.Dequeue();
        if (recent.Count >= MaxPerSecond) return false;
        recent.Enqueue(now);
        return true;
    }

    /// <summary>Flashes allowed in the last second.</summary>
    public static int InLastSecond
    {
        get
        {
            float now = Time.unscaledTime;
            int n = 0;
            foreach (var t in recent) if (now - t < 1f) n++;
            return n;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => recent.Clear();
}

/// <summary>Tells running cinematics that the Reduce Motion setting changed, so the menu loop can hold or resume.</summary>
public static class CinematicMotion
{
    public static event Action ReduceMotionChanged;

    public static void Changed() => ReduceMotionChanged?.Invoke();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ReduceMotionChanged = null;
}
