using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The sounds and mixer the cutscenes share, kept in Resources/Cutscenes/CutsceneAssets so any scene can reach them.
/// Made and filled by Bayanihan > Cutscenes > Build Audio (CutsceneAudioBuilder). Every clip listed here is a
/// generated PLACEHOLDER (Source Assets/Audio/make_cutscene_audio.py): replace the file, keep its name.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Cutscene Assets", fileName = "CutsceneAssets")]
public class CutsceneAssets : ScriptableObject
{
    public const string ResourcePath = "Cutscenes/CutsceneAssets";

    [Header("Mixer (Music, SFX, Ambience, VO)")]
    public AudioMixer mixer;
    public AudioMixerGroup music, sfx, ambience, vo;

    [Header("Ambience loops")]
    public AudioClip rainLight;
    public AudioClip rainHeavy;
    public AudioClip tinDrips;
    public AudioClip waterRush;
    public AudioClip wind;

    [Header("Effects")]
    public AudioClip[] thunder;
    public AudioClip siren;
    public AudioClip whoosh;
    public AudioClip splash;

    [Header("Loadout foley")]
    public AudioClip packRustle;
    public AudioClip packZip;
    public AudioClip packBuckle;
    public AudioClip packDenied;
    public AudioClip readyStamp;

    [Header("Loadout models (placeholders: Source Assets/Loadout/make_loadout_models.py)")]
    public GameObject backpack;
    [Tooltip("One per item, named Item_<RescueItemId> (Item_Lifebuoy, Item_FirstAid...).")]
    public GameObject[] itemModels;

    public GameObject ItemModel(RescueItemId id)
    {
        if (itemModels == null) return null;
        string want = "Item_" + id;
        foreach (var m in itemModels) if (m != null && m.name == want) return m;
        return null;
    }

    [Header("Voice")]
    public AudioClip radioBulletin;
    [TextArea] public string radioBulletinText =
        "Weather bulletin. Signal number three is raised over Cavite. Residents near the river, prepare to evacuate now.";

    /// <summary>One spoken line of the story intro, found by <see cref="name"/> ("Ben", "Joy", "Captain"...).</summary>
    [System.Serializable]
    public struct NamedClip { public string name; public AudioClip clip; }

    [Tooltip("The story intro's spoken lines (files VO_Intro_<name>); the Timeline's voice track plays them by name.")]
    public NamedClip[] introVoices;

    /// <summary>The intro's spoken line called <paramref name="lineName"/>, or null.</summary>
    public AudioClip IntroVoice(string lineName)
    {
        if (introVoices == null) return null;
        foreach (var v in introVoices) if (v.name == lineName) return v.clip;
        return null;
    }

    [Header("Story intro sounds")]
    [Tooltip("Timber and tin groaning as the water pushes on a house.")]
    public AudioClip creak;
    [Tooltip("A shanty giving way.")]
    public AudioClip collapse;
    public AudioClip dogBark;
    [Tooltip("The power cutting out: a transformer thump and a drop in the hum.")]
    public AudioClip powerOut;

    [Header("Music")]
    [Tooltip("The intro's rising cue: this track, from cueStart. Credited in Credits > Music.")]
    public AudioClip musicCue;
    public float musicCueStart = 0f;
    [Tooltip("A sparse piano-and-strings bed under the tragedy (a placeholder: replace with a composed or licensed piece).")]
    public AudioClip musicBed;

    static CutsceneAssets current;

    /// <summary>The project's cutscene assets, or an empty set (every sound silent) if it has not been built.</summary>
    public static CutsceneAssets Current
    {
        get
        {
            if (current == null) current = Resources.Load<CutsceneAssets>(ResourcePath);
            if (current == null) current = CreateInstance<CutsceneAssets>();
            return current;
        }
    }
}

/// <summary>
/// Cutscene sound: plays through the mixer's Music, SFX, Ambience and VO groups, whose levels follow the Settings
/// sliders (Music for music; Sound Effects for the other three; the master volume is the listener's, on top).
/// </summary>
public static class CutsceneAudio
{
    public enum Bus { Music, Sfx, Ambience, Vo }

    /// <summary>Exposed mixer parameters (decibels).</summary>
    public const string MusicParam = "MusicVolume", SfxParam = "SfxVolume", AmbienceParam = "AmbienceVolume", VoParam = "VoVolume";

    public static AudioMixerGroup Group(Bus bus)
    {
        var a = CutsceneAssets.Current;
        switch (bus)
        {
            case Bus.Music: return a.music;
            case Bus.Ambience: return a.ambience;
            case Bus.Vo: return a.vo;
            default: return a.sfx;
        }
    }

    /// <summary>Sets the mixer's group levels from the Settings sliders.</summary>
    public static void ApplyVolumes()
    {
        var m = CutsceneAssets.Current.mixer;
        if (m == null) return;
        m.SetFloat(MusicParam, ToDecibels(GameSettings.MusicVolume));
        m.SetFloat(SfxParam, ToDecibels(GameSettings.SfxVolume));
        m.SetFloat(AmbienceParam, ToDecibels(GameSettings.SfxVolume));
        m.SetFloat(VoParam, ToDecibels(GameSettings.SfxVolume));
    }

    public static float ToDecibels(float linear) => linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;

    /// <summary>A 2D source routed to a bus, on <paramref name="host"/>.</summary>
    public static AudioSource Source(GameObject host, Bus bus, AudioClip clip = null, bool loop = false, float volume = 1f)
    {
        var s = host.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.loop = loop;
        s.clip = clip;
        s.volume = volume;
        s.outputAudioMixerGroup = Group(bus);
        return s;
    }

    /// <summary>
    /// When nothing routes a source through the mixer (it has not been built), its own volume carries the setting
    /// instead, so the sliders still work.
    /// </summary>
    public static float Fallback(Bus bus) =>
        CutsceneAssets.Current.mixer != null ? 1f : bus == Bus.Music ? GameSettings.MusicVolume : GameSettings.SfxVolume;

    /// <summary>A one-shot on a bus, through a shared 2D source.</summary>
    public static void PlayOneShot(AudioClip clip, Bus bus = Bus.Sfx, float volume = 1f)
    {
        if (clip == null || !Application.isPlaying) return;
        var s = OneShotSource(bus);
        s.PlayOneShot(clip, volume * Fallback(bus));
    }

    static readonly AudioSource[] oneShots = new AudioSource[4];

    static AudioSource OneShotSource(Bus bus)
    {
        int i = (int)bus;
        if (oneShots[i] != null) return oneShots[i];
        var go = new GameObject("CutsceneAudio_" + bus);
        Object.DontDestroyOnLoad(go);
        oneShots[i] = Source(go, bus);
        oneShots[i].ignoreListenerPause = true;
        return oneShots[i];
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < oneShots.Length; i++) oneShots[i] = null;
    }
}
