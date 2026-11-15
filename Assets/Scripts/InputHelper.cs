using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The crew's controls, from the keyboard and mouse or the on-screen touch controls (<see cref="TouchInput"/>),
/// whichever is being used. Works with the new Input System (Unity 6 default) or the old Input Manager.
/// </summary>
public static class InputHelper
{
    /// <summary>
    /// The crew's controls do nothing: the mission is over (the report is on its way, and a press of E must not take
    /// somebody aboard behind it) or the game is paused. Looking round, and R and Esc on the report, still work.
    /// </summary>
    public static bool GameplayLocked =>
        GamePause.IsPaused || (RescueManager.Instance != null && RescueManager.Instance.GameOver);

    /// <summary>The on-screen controls are showing, so prompts name the buttons rather than keys.</summary>
    public static bool TouchActive => GameSettings.ShowTouchControls;

    /// <summary>What to call a control in a prompt: the key on a keyboard ("E"), the button on a phone ("RESCUE").</summary>
    public static string Key(string key, string button) => TouchActive ? button : key;

    /// <summary>"Press E" or "Tap RESCUE".</summary>
    public static string Press(string key, string button) => TouchActive ? "Tap " + button : "Press " + key;

    /// <summary>"Hold E" or "Hold RESCUE".</summary>
    public static string Hold(string key, string button) => "Hold " + Key(key, button);

    /// <summary>x = steer (A/D, arrows or the joystick), y = throttle (W/S, arrows or the joystick).</summary>
    public static Vector2 Move()
    {
        if (GameplayLocked) return Vector2.zero;
        Vector2 keys = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            keys.x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            keys.y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
        }
#else
        keys = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        return keys.sqrMagnitude > 0.0001f ? keys : Vector2.ClampMagnitude(TouchInput.Stick, 1f);
    }

    /// <summary>E or RESCUE pressed this frame (rescue, board, step off, take hold).</summary>
    public static bool InteractPressed() => Pressed(TouchAction.Interact, KeyE);

    /// <summary>E or RESCUE held down (treating an injured resident).</summary>
    public static bool InteractHeld()
    {
        if (GameplayLocked) return false;
        if (TouchInput.Held(TouchAction.Interact)) return true;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
        return Input.GetKey(KeyCode.E);
#endif
    }

    /// <summary>F or the flashlight button.</summary>
    public static bool FlashlightPressed() => Pressed(TouchAction.Flashlight, KeyF);

    /// <summary>Q or the radio button (find the nearest resident).</summary>
    public static bool RadioPressed() => Pressed(TouchAction.Radio, KeyQ);

    /// <summary>T or the throw button (throw a line to somebody in reach).</summary>
    public static bool ThrowPressed() => Pressed(TouchAction.Throw, KeyT);

    /// <summary>C or the power-cut button (radio for a live wire to be cut).</summary>
    public static bool PowerCutPressed() => Pressed(TouchAction.PowerCut, KeyC);

    /// <summary>R pressed this frame (restart, on the report).</summary>
    public static bool RestartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    /// <summary>Mouse movement and touch drags this frame, in pixels, scaled by the look-sensitivity setting.</summary>
    public static Vector2 LookDelta()
    {
        Vector2 d = TouchInput.Look;
#if ENABLE_INPUT_SYSTEM
        // On a touch screen the mouse device mirrors the first finger, so only a real mouse counts.
        if (Mouse.current != null && !TouchActive) d += Mouse.current.delta.ReadValue();
#else
        if (!TouchActive) d += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        return d * GameSettings.LookSensitivity;
    }

    /// <summary>Scroll wheel or pinch this frame. Positive = zoom in.</summary>
    public static float ZoomDelta()
    {
        float z = TouchInput.Zoom;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) z += Mouse.current.scroll.ReadValue().y / 120f;
#else
        z += Input.GetAxisRaw("Mouse ScrollWheel");
#endif
        return z;
    }

    /// <summary>Escape pressed this frame. On Android the system Back button arrives as Escape.</summary>
    public static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ------------------------------------------------------------------ keys
    const int KeyE = 0, KeyF = 1, KeyQ = 2, KeyT = 3, KeyC = 4;

    static bool Pressed(TouchAction action, int key)
    {
        if (GameplayLocked) return false;
        if (TouchInput.Pressed(action)) return true;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return false;
        switch (key)
        {
            case KeyE: return k.eKey.wasPressedThisFrame;
            case KeyF: return k.fKey.wasPressedThisFrame;
            case KeyQ: return k.qKey.wasPressedThisFrame;
            case KeyT: return k.tKey.wasPressedThisFrame;
            default:   return k.cKey.wasPressedThisFrame;
        }
#else
        switch (key)
        {
            case KeyE: return Input.GetKeyDown(KeyCode.E);
            case KeyF: return Input.GetKeyDown(KeyCode.F);
            case KeyQ: return Input.GetKeyDown(KeyCode.Q);
            case KeyT: return Input.GetKeyDown(KeyCode.T);
            default:   return Input.GetKeyDown(KeyCode.C);
        }
#endif
    }
}
