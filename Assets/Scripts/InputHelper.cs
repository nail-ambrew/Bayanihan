using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Small wrapper so the game works whether your project uses the new Input System
/// (Unity 6 default) or the old Input Manager.
/// </summary>
public static class InputHelper
{
    /// <summary>
    /// The mission is over, so the crew's keys do nothing: the report is on its way (or on the screen) and a press of
    /// E must not take somebody aboard behind it. R and Esc on the report screen, and looking round, still work.
    /// </summary>
    public static bool GameplayLocked =>
        RescueManager.Instance != null && RescueManager.Instance.GameOver;

    /// <summary>x = steer (A/D or arrows), y = throttle (W/S or arrows)</summary>
    public static Vector2 Move()
    {
        if (GameplayLocked) return Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f)
                - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f)
                - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    /// <summary>E key pressed this frame (rescue / interact).</summary>
    public static bool InteractPressed()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

    /// <summary>E key held down (treating an injured resident).</summary>
    public static bool InteractHeld()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
        return Input.GetKey(KeyCode.E);
#endif
    }

    /// <summary>F key pressed this frame (flashlight).</summary>
    public static bool FlashlightPressed()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    /// <summary>Q key pressed this frame (radio).</summary>
    public static bool RadioPressed()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    /// <summary>T key pressed this frame (throw a line to somebody in reach).</summary>
    public static bool ThrowPressed()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }

    /// <summary>C key pressed this frame (radio for a power cut).</summary>
    public static bool PowerCutPressed()
    {
        if (GameplayLocked) return false;
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.C);
#endif
    }

    /// <summary>R key pressed this frame (restart).</summary>
    public static bool RestartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    /// <summary>Mouse movement this frame, in pixels. Used to look around while swimming.</summary>
    public static Vector2 LookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
    }

    /// <summary>Scroll wheel this frame. Positive = scroll up.</summary>
    public static float ZoomDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.GetAxisRaw("Mouse ScrollWheel");
#endif
    }

    /// <summary>Escape pressed this frame (back to the loadout screen).</summary>
    public static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}