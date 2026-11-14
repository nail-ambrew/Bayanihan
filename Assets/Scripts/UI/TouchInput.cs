using UnityEngine;

/// <summary>The crew's actions that have an on-screen button.</summary>
public enum TouchAction { Interact, Throw, Flashlight, Radio, PowerCut, Count }

/// <summary>
/// What the on-screen controls are doing this frame. <see cref="TouchControls"/> writes it, <see cref="InputHelper"/>
/// reads it alongside the keyboard, so the boat, the swimmer and the rescue scripts work the same either way.
///
/// A tap is seen for the whole of the frame after the one it arrived in. UI events are handled by the EventSystem at
/// a point in the frame no script can rely on, so this way every script that asks "was it pressed?" in a frame gets
/// the same answer.
/// </summary>
public static class TouchInput
{
    /// <summary>The joystick: x = steer, y = throttle, each -1..1.</summary>
    public static Vector2 Stick;

    static readonly int[] downFrame = new int[(int)TouchAction.Count];
    static readonly bool[] held = new bool[(int)TouchAction.Count];

    static Vector2 pendingLook, frameLook;
    static float pendingZoom, frameZoom;
    static int rolledAt = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        Stick = Vector2.zero;
        for (int i = 0; i < downFrame.Length; i++) { downFrame[i] = -10; held[i] = false; }
        pendingLook = frameLook = Vector2.zero;
        pendingZoom = frameZoom = 0f;
        rolledAt = -1;
    }

    public static void Press(TouchAction a)
    {
        downFrame[(int)a] = Time.frameCount;
        held[(int)a] = true;
    }

    public static void Release(TouchAction a) => held[(int)a] = false;

    public static bool Pressed(TouchAction a) => downFrame[(int)a] + 1 == Time.frameCount;

    public static bool Held(TouchAction a) => held[(int)a];

    /// <summary>Drag on the screen to look round, in pixels.</summary>
    public static void AddLook(Vector2 pixels) => pendingLook += pixels;

    /// <summary>Pinch: positive = fingers apart = zoom in (like scrolling up).</summary>
    public static void AddZoom(float steps) => pendingZoom += steps;

    public static Vector2 Look { get { Roll(); return frameLook; } }
    public static float Zoom { get { Roll(); return frameZoom; } }

    /// <summary>Once per frame, what was gathered last frame becomes this frame's look and zoom.</summary>
    static void Roll()
    {
        if (rolledAt == Time.frameCount) return;
        rolledAt = Time.frameCount;
        frameLook = pendingLook;
        frameZoom = pendingZoom;
        pendingLook = Vector2.zero;
        pendingZoom = 0f;
    }

    /// <summary>Lets go of everything (the controls were hidden, or the game was paused).</summary>
    public static void ReleaseAll()
    {
        Stick = Vector2.zero;
        for (int i = 0; i < held.Length; i++) held[i] = false;
        pendingLook = Vector2.zero;
        pendingZoom = 0f;
    }
}

/// <summary>The pause menu's hold on the game: time stops and the crew's controls do nothing.</summary>
public static class GamePause
{
    public static bool IsPaused { get; private set; }

    static float timeScaleBefore = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsPaused = false; timeScaleBefore = 1f; }

    public static void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        TouchInput.ReleaseAll();
        AudioListener.pause = true;
    }

    public static void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Time.timeScale = timeScaleBefore;
        AudioListener.pause = false;
    }

    /// <summary>Leaving the scene (restart, abort): unpause without restoring a stale time scale.</summary>
    public static void Clear()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }
}
