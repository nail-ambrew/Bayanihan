using System;
using UnityEngine;

/// <summary>
/// The mission waits while the intro cutscene plays over it. While held, the mission clock, residents' condition and the
/// rising flood stand still, the crew's controls do nothing (<see cref="InputHelper.GameplayLocked"/>) and the HUD is
/// hidden. <see cref="Release"/> starts the mission; the HUD then fades in.
///
/// A hold never outlasts <see cref="MaxSeconds"/>: if a cutscene breaks and never releases it, the mission starts anyway,
/// so the player is never stuck.
/// </summary>
public static class CutsceneHold
{
    /// <summary>The longest a hold can last, whatever happens to the cutscene (seconds, real time).</summary>
    public const float MaxSeconds = 60f;

    static bool active;
    static float beganAt;

    /// <summary>A cutscene is playing over the mission.</summary>
    public static bool Active
    {
        get
        {
            if (active && Time.unscaledTime - beganAt > MaxSeconds)
            {
                Debug.LogWarning("CutsceneHold: still held after " + MaxSeconds + " s; starting the mission.");
                Release();
            }
            return active;
        }
    }

    /// <summary>Real time the last hold was released, or -1.</summary>
    public static float ReleasedAt { get; private set; } = -1f;

    /// <summary>Raised once when the hold is released (the mission starts).</summary>
    public static event Action Released;

    public static void Begin()
    {
        active = true;
        beganAt = Time.unscaledTime;
    }

    public static void Release()
    {
        if (!active) return;
        active = false;
        ReleasedAt = Time.unscaledTime;
        Released?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        active = false;
        ReleasedAt = -1f;
        Released = null;
    }
}
