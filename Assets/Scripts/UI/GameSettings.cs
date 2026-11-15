using UnityEngine;

/// <summary>Where the touch joystick goes.</summary>
public enum ControlSide { JoystickLeft, JoystickRight }

/// <summary>When the on-screen controls are drawn.</summary>
public enum TouchMode { Auto, On, Off }

/// <summary>
/// The player's settings, kept in PlayerPrefs so they survive a restart: sound, graphics, how fast the camera turns,
/// and how the touch controls are laid out. Applied as soon as the game starts and again whenever one changes.
/// </summary>
public static class GameSettings
{
    const string VolumeKey = "settings.volume";
    const string QualityKey = "settings.quality";
    const string SensitivityKey = "settings.lookSensitivity";
    const string SideKey = "settings.controlSide";
    const string TouchKey = "settings.touchMode";

    public const float MinSensitivity = 0.25f, MaxSensitivity = 2.5f;

    /// <summary>Master volume, 0..1.</summary>
    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); Apply(); }
    }

    /// <summary>0 Low, 1 Medium, 2 High: picked from the project's quality levels, lowest to highest.</summary>
    public static int Quality
    {
        get => PlayerPrefs.GetInt(QualityKey, DefaultQuality);
        set { PlayerPrefs.SetInt(QualityKey, Mathf.Clamp(value, 0, 2)); Apply(); }
    }

    /// <summary>How fast dragging or the mouse turns the camera. 1 = as tuned.</summary>
    public static float LookSensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
        set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
    }

    public static ControlSide Side
    {
        get => (ControlSide)PlayerPrefs.GetInt(SideKey, (int)ControlSide.JoystickLeft);
        set => PlayerPrefs.SetInt(SideKey, (int)value);
    }

    public static TouchMode Touch
    {
        get => (TouchMode)PlayerPrefs.GetInt(TouchKey, (int)TouchMode.Auto);
        set => PlayerPrefs.SetInt(TouchKey, (int)value);
    }

    /// <summary>Phones and tablets start on Medium, everything else on High.</summary>
    static int DefaultQuality => Application.isMobilePlatform ? 1 : 2;

    /// <summary>Whether the on-screen joystick and buttons should be drawn now.</summary>
    public static bool ShowTouchControls
    {
        get
        {
            switch (Touch)
            {
                case TouchMode.On: return true;
                case TouchMode.Off: return false;
                default: return Application.isMobilePlatform || HasTouchscreen();
            }
        }
    }

    static bool HasTouchscreen()
    {
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Touchscreen.current != null;
#else
        return Input.touchSupported;
#endif
    }

    /// <summary>The project's quality level for Low, Medium or High.</summary>
    public static int QualityLevelFor(int choice, int levels)
    {
        if (levels <= 1) return 0;
        choice = Mathf.Clamp(choice, 0, 2);
        return Mathf.RoundToInt(choice / 2f * (levels - 1));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Apply()
    {
        AudioListener.volume = Volume;
        int target = QualityLevelFor(Quality, QualitySettings.names.Length);
        if (QualitySettings.GetQualityLevel() != target) QualitySettings.SetQualityLevel(target, true);
        PlayerPrefs.Save();
    }

    /// <summary>Back to the defaults (for tests, and a "reset" a future settings page could offer).</summary>
    public static void Reset()
    {
        PlayerPrefs.DeleteKey(VolumeKey);
        PlayerPrefs.DeleteKey(QualityKey);
        PlayerPrefs.DeleteKey(SensitivityKey);
        PlayerPrefs.DeleteKey(SideKey);
        PlayerPrefs.DeleteKey(TouchKey);
    }
}
