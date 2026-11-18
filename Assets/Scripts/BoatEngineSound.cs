using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The outboard: a looped recording on the boat (UiAssets.boatEngine, from "River boat with outboard engine" by nomadas on
/// Freesound, CC0) that idles when somebody is at the helm, opens up with the throttle and the speed, and dies away when
/// nobody is aboard, when a live wire has stalled the engine, or while the game is paused. It is put on every boat as a
/// scene loads, so no scene has to be set up for it.
/// </summary>
[RequireComponent(typeof(SimpleBoat))]
public class BoatEngineSound : MonoBehaviour
{
    const float IdleVolume = 0.25f, FullVolume = 0.9f;
    const float IdlePitch = 0.85f, FullPitch = 1.15f;
    /// <summary>How fast the sound follows the engine (per second): quick enough to answer the throttle, slow enough not to jump.</summary>
    const float Response = 4f;

    SimpleBoat boat;
    AudioSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

    static void Attach()
    {
        foreach (var boat in FindObjectsByType<SimpleBoat>(FindObjectsSortMode.None))
            if (boat.GetComponent<BoatEngineSound>() == null) boat.gameObject.AddComponent<BoatEngineSound>();
    }

    void Awake()
    {
        boat = GetComponent<SimpleBoat>();
        source = gameObject.AddComponent<AudioSource>();
        source.clip = UiAssets.Current.boatEngine;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0.85f;            // mostly in the world, so it is heard from the swimmer too, a little always in the ears
        source.minDistance = 5f;
        source.maxDistance = 70f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.volume = 0f;
        source.pitch = IdlePitch;
        if (source.clip != null)
        {
            source.time = Random.Range(0f, source.clip.length);     // not every run starts on the same splash
            source.Play();
        }
    }

    void Update()
    {
        if (source.clip == null) return;
        bool running = boat.IsPlayerAboard && !boat.IsStalled && Time.timeScale > 0f;
        float speed01 = Mathf.Clamp01(Mathf.Abs(boat.ForwardSpeed) / Mathf.Max(0.1f, boat.maxSpeed));
        float load = Mathf.Max(Mathf.Abs(boat.Throttle), speed01);

        float volume = running ? Mathf.Lerp(IdleVolume, FullVolume, load) * GameSettings.SfxVolume : 0f;
        float pitch = Mathf.Lerp(IdlePitch, FullPitch, speed01 * 0.6f + Mathf.Abs(boat.Throttle) * 0.4f);
        float k = 1f - Mathf.Exp(-Response * Time.unscaledDeltaTime);
        source.volume = Mathf.Lerp(source.volume, volume, k);
        source.pitch = Mathf.Lerp(source.pitch, pitch, k);
    }
}
