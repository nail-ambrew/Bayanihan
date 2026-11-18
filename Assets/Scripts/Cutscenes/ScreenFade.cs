using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A full-screen black that lives across scene loads: a skipped cutscene fades to it, and the next screen fades out of it.
/// Drawn under the loading screen (sorting 999 vs 1000), so a load still shows its tip. The fade can carry the sound
/// with it (the listener volume, back to the master volume afterwards).
/// </summary>
public class ScreenFade : MonoBehaviour
{
    static ScreenFade instance;
    Image black;
    Coroutine running;
    bool soundDown;

    /// <summary>How black the screen is now, 0..1.</summary>
    public static float Alpha => instance != null && instance.black != null ? instance.black.color.a : 0f;

    static ScreenFade Get()
    {
        if (instance != null) return instance;
        UiKit.NewCanvas("ScreenFade", 999, out var canvas);
        DontDestroyOnLoad(canvas.gameObject);
        instance = canvas.gameObject.AddComponent<ScreenFade>();
        instance.black = UiKit.Image("Black", canvas.transform, new Color(0, 0, 0, 0));
        UiKit.Stretch(instance.black.rectTransform);
        instance.black.raycastTarget = false;
        return instance;
    }

    /// <summary>To black over <paramref name="seconds"/> (real time); the sound fades out with it if asked.</summary>
    public static Coroutine Out(float seconds, bool withSound = true) => Get().Run(1f, seconds, withSound);

    /// <summary>Out of black over <paramref name="seconds"/>; the sound comes back with it.</summary>
    public static Coroutine In(float seconds) => Get().Run(0f, seconds, true);

    /// <summary>Clear at once (and the sound back), e.g. when a test or the failsafe ends things early.</summary>
    public static void Clear()
    {
        if (instance == null) return;
        if (instance.running != null) instance.StopCoroutine(instance.running);
        instance.running = null;
        instance.black.color = new Color(0, 0, 0, 0);
        instance.black.raycastTarget = false;
        if (instance.soundDown) { AudioListener.volume = GameSettings.Volume; instance.soundDown = false; }
    }

    Coroutine Run(float target, float seconds, bool withSound)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Fade(target, seconds, withSound));
        return running;
    }

    IEnumerator Fade(float target, float seconds, bool withSound)
    {
        float from = black.color.a;
        float fromVolume = AudioListener.volume;
        float toVolume = target > 0.5f ? 0f : GameSettings.Volume;
        if (withSound && target > 0.5f) soundDown = true;
        black.raycastTarget = target > 0.5f;               // nothing can be pressed through the black
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            black.color = new Color(0, 0, 0, Mathf.Lerp(from, target, k));
            if (withSound || soundDown) AudioListener.volume = Mathf.Lerp(fromVolume, toVolume, k);
            yield return null;
        }
        black.color = new Color(0, 0, 0, target);
        if (withSound || soundDown) AudioListener.volume = toVolume;
        if (target < 0.5f) soundDown = false;
        running = null;
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            if (soundDown) AudioListener.volume = GameSettings.Volume;
            instance = null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
}
