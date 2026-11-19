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
    const string ColorKey = "settings.colorGrade";
    const string ViewKey = "settings.swimView";
    const string MusicKey = "settings.musicVolume";
    const string SfxKey = "settings.sfxVolume";
    const string IntroKey = "settings.playIntro";
    const string MotionKey = "settings.reduceMotion";
    const string SubtitlesKey = "settings.subtitles";

    public const float MinSensitivity = 0.25f, MaxSensitivity = 2.5f;

    /// <summary>Master volume, 0..1: everything, on top of the music and sound effects volumes.</summary>
    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); Apply(); }
    }

    /// <summary>The background music (MusicPlayer), 0..1, under the master volume. Heard at once.</summary>
    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicKey, 1f);
        set => PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
    }

    /// <summary>Every sound effect, 0..1, under the master volume: the interface, the boat's engine, the rain and thunder.</summary>
    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(SfxKey, 1f);
        set => PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
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

    /// <summary>The look of the mission, an index into <see cref="ColorGrade.Names"/>: Default, Vibrant or Stormy.</summary>
    public static int ColorGrade
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 0), 0, global::ColorGrade.Names.Length - 1);
        set
        {
            PlayerPrefs.SetInt(ColorKey, Mathf.Clamp(value, 0, global::ColorGrade.Names.Length - 1));
            global::ColorGrade.ApplyToScene();
        }
    }

    /// <summary>How the swimmer is seen: from their own eyes, or from behind and above with the whole body in view. V switches it in play.</summary>
    public static PlayerRescuer.PovMode SwimView
    {
        get => PlayerPrefs.GetInt(ViewKey, (int)PlayerRescuer.PovMode.FirstPerson) == (int)PlayerRescuer.PovMode.ThirdPerson
            ? PlayerRescuer.PovMode.ThirdPerson : PlayerRescuer.PovMode.FirstPerson;
        set => PlayerPrefs.SetInt(ViewKey, (int)value);
    }

    /// <summary>Play the flood intro cutscene before a Mabuhay mission. On by default; it can always be skipped.</summary>
    public static bool PlayIntro
    {
        get => PlayerPrefs.GetInt(IntroKey, 1) == 1;
        set => PlayerPrefs.SetInt(IntroKey, value ? 1 : 0);
    }

    /// <summary>
    /// Calmer cinematics: the menu background holds on a still frame, and cutscenes drop camera shake, lightning flashes
    /// and chromatic aberration. Off by default.
    /// </summary>
    public static bool ReduceMotion
    {
        get => PlayerPrefs.GetInt(MotionKey, 0) == 1;
        set => PlayerPrefs.SetInt(MotionKey, value ? 1 : 0);
    }

    /// <summary>Subtitles for spoken lines in cutscenes (the radio bulletin). On by default.</summary>
    public static bool Subtitles
    {
        get => PlayerPrefs.GetInt(SubtitlesKey, 1) == 1;
        set => PlayerPrefs.SetInt(SubtitlesKey, value ? 1 : 0);
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
        GameplayQuality.Apply();
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
        PlayerPrefs.DeleteKey(ColorKey);
        PlayerPrefs.DeleteKey(MusicKey);
        PlayerPrefs.DeleteKey(SfxKey);
        PlayerPrefs.DeleteKey(ViewKey);
        PlayerPrefs.DeleteKey(IntroKey);
        PlayerPrefs.DeleteKey(MotionKey);
        PlayerPrefs.DeleteKey(SubtitlesKey);
    }
}
