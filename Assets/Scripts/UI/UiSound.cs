using UnityEngine;

/// <summary>What happened on screen, for <see cref="UiSound.Play"/>.</summary>
public enum UiSoundKind
{
    /// <summary>No sound.</summary>
    None,
    /// <summary>A button pressed.</summary>
    Click,
    /// <summary>The pointer moved onto a menu line.</summary>
    Hover,
    /// <summary>A menu line chosen: a page or a screen opens.</summary>
    Open,
    /// <summary>The back arrow: a page or a card closes.</summary>
    Back,
    /// <summary>One of a row of choices picked (Low / Medium / High, a map).</summary>
    Toggle,
    /// <summary>A slider moved.</summary>
    Tick,
    /// <summary>A question or notice comes up over the screen.</summary>
    Question,
    /// <summary>Something could not be done: a failed sign-in, a full loadout.</summary>
    Error,
    /// <summary>A go-ahead: LAUNCH.</summary>
    Confirm,
    /// <summary>An item packed into a loadout slot.</summary>
    PackIn,
    /// <summary>An item taken out of the loadout.</summary>
    PackOut,
    /// <summary>The pause menu opens.</summary>
    PauseOpen,
    /// <summary>The pause menu closes.</summary>
    PauseClose
}

/// <summary>
/// The sounds of the menus and the HUD: one 2D audio source that lives across scene loads and keeps playing while the game
/// is paused. The clips are slots in <see cref="UiAssets"/> (the Kenney Interface Sounds pack, CC0); an empty slot is silent.
/// Loudness follows the master volume in Settings, as everything else does.
/// </summary>
public static class UiSound
{
    /// <summary>Under the music (0.45): a press is felt, not heard over everything. Hover and slider ticks play at 60% of this.</summary>
    const float Volume = 0.35f;
    /// <summary>The same sound is not started again sooner than this (seconds): a slider dragged, a hover swept along a menu.</summary>
    const float Gap = 0.06f;

    static AudioSource source;
    static UiSoundKind lastKind = UiSoundKind.None;
    static float lastAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { source = null; lastKind = UiSoundKind.None; lastAt = -1f; }

    public static void Play(UiSoundKind kind)
    {
        if (kind == UiSoundKind.None || !Application.isPlaying) return;
        var clip = ClipFor(kind);
        if (clip == null) return;
        float now = Time.unscaledTime;
        if (kind == lastKind && now - lastAt < Gap) return;
        lastKind = kind;
        lastAt = now;
        float volume = (kind == UiSoundKind.Hover || kind == UiSoundKind.Tick ? Volume * 0.6f : Volume) * GameSettings.SfxVolume;
        if (volume > 0f) Source().PlayOneShot(clip, volume);
    }

    static AudioClip ClipFor(UiSoundKind kind)
    {
        var a = UiAssets.Current;
        switch (kind)
        {
            case UiSoundKind.Click:      return a.clickSound;
            case UiSoundKind.Hover:      return a.hoverSound;
            case UiSoundKind.Open:       return a.openSound;
            case UiSoundKind.Back:       return a.backSound;
            case UiSoundKind.Toggle:     return a.toggleSound;
            case UiSoundKind.Tick:       return a.tickSound;
            case UiSoundKind.Question:   return a.questionSound;
            case UiSoundKind.Error:      return a.errorSound;
            case UiSoundKind.Confirm:    return a.confirmSound;
            case UiSoundKind.PackIn:     return a.packInSound;
            case UiSoundKind.PackOut:    return a.packOutSound;
            case UiSoundKind.PauseOpen:  return a.pauseOpenSound;
            case UiSoundKind.PauseClose: return a.pauseCloseSound;
            default:                     return null;
        }
    }

    static AudioSource Source()
    {
        if (source != null) return source;
        var go = new GameObject("UiSound");
        Object.DontDestroyOnLoad(go);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;               // 2D: the menus are not somewhere in the world
        source.ignoreListenerPause = true;      // the pause menu is heard while the game is paused
        source.priority = 32;
        return source;
    }
}
