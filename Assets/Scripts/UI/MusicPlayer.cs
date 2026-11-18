using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The background music: the menu track on the sign-in, main menu and loadout screens, the gameplay track on a mission (any
/// scene with a <see cref="RescueManager"/>). It lives across scene loads, so moving between menus does not restart the
/// track, and it crossfades when the kind of scene changes. The tracks are <see cref="UiAssets.menuMusic"/> and
/// <see cref="UiAssets.gameplayMusic"/>; loudness follows the master volume in Settings.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    const float Volume = 0.45f;
    const float FadeSeconds = 1.5f;

    static MusicPlayer instance;
    AudioSource[] decks;
    int live;
    Coroutine fading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; duck = duckTarget = duckRate = 1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (instance != null) return;
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MusicPlayer>();
    }

    void Awake()
    {
        decks = new AudioSource[2];
        for (int i = 0; i < 2; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            s.priority = 0;                     // never the voice that is dropped
            s.volume = 0f;
            decks[i] = s;
        }
        SceneManager.sceneLoaded += OnSceneLoaded;
        PlayFor(SceneManager.GetActiveScene());
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) PlayFor(scene);
    }

    /// <summary>The track for a scene: the gameplay one if it holds a mission, the menu one otherwise.</summary>
    public static AudioClip TrackFor(bool mission) => mission ? UiAssets.Current.gameplayMusic : UiAssets.Current.menuMusic;

    /// <summary>The track playing now, or null.</summary>
    public static AudioClip Playing => instance != null && instance.decks[instance.live].isPlaying ? instance.decks[instance.live].clip : null;

    void PlayFor(Scene scene)
    {
        bool mission = false;
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<RescueManager>(true) != null) { mission = true; break; }
        CrossfadeTo(TrackFor(mission));
    }

    void CrossfadeTo(AudioClip clip)
    {
        var current = decks[live];
        if (clip == current.clip && current.isPlaying) return;        // the same kind of scene: carry on where it was
        if (fading != null) StopCoroutine(fading);
        int next = 1 - live;
        var incoming = decks[next];
        incoming.Stop();
        incoming.clip = clip;
        incoming.volume = 0f;
        if (clip != null) incoming.Play();
        live = next;
        fading = StartCoroutine(Fade(incoming, current));
    }

    /// <summary>How loud the music plays: its own level under the Music setting (the master volume is on top of both),
    /// lowered while a cutscene plays its own score.</summary>
    static float Target => Volume * GameSettings.MusicVolume * duck;

    static float duck = 1f, duckTarget = 1f, duckRate = 1f;

    /// <summary>
    /// Lowers the background music to <paramref name="level"/> (0..1 of its usual loudness) over
    /// <paramref name="seconds"/>, for a cutscene with its own score. <see cref="Unduck"/> brings it back.
    /// </summary>
    public static void Duck(float level, float seconds = 0.5f)
    {
        duckTarget = Mathf.Clamp01(level);
        duckRate = 1f / Mathf.Max(0.01f, seconds);
    }

    public static void Unduck(float seconds = 1f) => Duck(1f, seconds);

    /// <summary>The ducking level now (1 = not ducked). For tests.</summary>
    public static float DuckLevel => duck;

    // The Music setting is heard at once, as its slider moves.
    void Update()
    {
        duck = Mathf.MoveTowards(duck, duckTarget, duckRate * Time.unscaledDeltaTime);
        if (fading == null && decks[live].clip != null) decks[live].volume = Target;
    }

    IEnumerator Fade(AudioSource incoming, AudioSource outgoing)
    {
        float startOut = outgoing.volume;
        for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
        {
            float k = t / FadeSeconds;
            incoming.volume = Mathf.Lerp(0f, Target, k);
            outgoing.volume = Mathf.Lerp(startOut, 0f, k);
            yield return null;
        }
        incoming.volume = incoming.clip != null ? Target : 0f;
        outgoing.volume = 0f;
        outgoing.Stop();
        fading = null;
    }
}
