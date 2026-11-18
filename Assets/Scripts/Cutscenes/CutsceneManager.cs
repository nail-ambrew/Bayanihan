using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

/// <summary>
/// Plays one cutscene: a <see cref="PlayableDirector"/> with its Timeline, the overlay (letterbox, subtitles, title, SKIP)
/// and the rules every cutscene keeps:
/// <list type="bullet">
/// <item>SKIP (button, Esc, Space, Enter, gamepad Start/B) fades to black in 0.3 s with the sound, then finishes.</item>
/// <item>A watchdog finishes it if the director stops moving for <see cref="stallSeconds"/>, or runs past its length by
/// <see cref="overrunSeconds"/>: the player is never stuck in a cutscene.</item>
/// <item><see cref="Finished"/> is raised exactly once, however it ends (with whether it was skipped).</item>
/// </list>
/// A looping cutscene (the menu background) never finishes; with Reduce Motion it holds on <see cref="stillFrame"/>.
/// </summary>
[RequireComponent(typeof(PlayableDirector))]
public class CutsceneManager : MonoBehaviour
{
    public const float SkipFadeSeconds = 0.3f;

    [Tooltip("For logs and tests, e.g. Cut_Intro.")]
    public string cutsceneName = "Cutscene";
    public bool skippable = true;
    public bool letterbox = true;
    [Tooltip("Loop forever (the menu background). A looping cutscene has no SKIP and never finishes.")]
    public bool loop;
    [Tooltip("With Reduce Motion on, a looping cutscene holds on this time (seconds) instead of playing.")]
    public double stillFrame = 0.0;
    public bool playOnStart = true;
    [Tooltip("Finish if the Timeline has not moved for this long (seconds, real time).")]
    public float stallSeconds = 3f;
    [Tooltip("Finish if it is still playing this long after its end (seconds, real time).")]
    public float overrunSeconds = 5f;
    [Tooltip("Overlay sorting order (the loading screen is 1000).")]
    public int overlayOrder = 900;

    [Serializable] public class FinishedEvent : UnityEvent<bool> { }
    [Tooltip("Raised once when it ends; true if it was skipped (or the watchdog ended it).")]
    public FinishedEvent onFinished = new FinishedEvent();
    public event Action<bool> Finished;

    /// <summary>The cutscene playing now (the last one started), or null.</summary>
    public static CutsceneManager Playing { get; private set; }

    public PlayableDirector Director { get; private set; }
    public CutsceneOverlay Overlay { get; private set; }
    public bool IsFinished { get; private set; }
    public bool WasSkipped { get; private set; }
    /// <summary>Why it ended: "end", "skip", "stall" or "overrun".</summary>
    public string EndReason { get; private set; }
    public bool Skipping { get; private set; }

    float startedAt;
    double lastTime;
    float stillFor;
    bool started;

    void Awake()
    {
        Director = GetComponent<PlayableDirector>();
        Director.playOnAwake = false;
        Director.timeUpdateMode = DirectorUpdateMode.GameTime;
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    public void Play()
    {
        if (started) return;
        started = true;
        Playing = this;
        startedAt = Time.unscaledTime;
        CutsceneAudio.ApplyVolumes();
        Overlay = CutsceneOverlay.Create(cutsceneName, letterbox, skippable && !loop, Skip, overlayOrder);
        Director.extrapolationMode = loop ? DirectorWrapMode.Loop : DirectorWrapMode.None;
        Director.stopped += OnStopped;
        Director.Play();
        lastTime = Director.time;
        if (loop) { CinematicMotion.ReduceMotionChanged += ApplyMotion; ApplyMotion(); }
    }

    /// <summary>With Reduce Motion on, a loop holds still on its chosen frame; off, it plays.</summary>
    void ApplyMotion()
    {
        if (Director == null || !loop) return;
        if (GameSettings.ReduceMotion)
        {
            Director.time = stillFrame;
            Director.Evaluate();
            Director.Pause();
        }
        else if (Director.state != PlayState.Playing) Director.Resume();
    }

    /// <summary>The length the watchdog allows for (the Timeline's own length).</summary>
    public double Duration => Director != null && Director.playableAsset != null ? Director.duration : 0.0;

    /// <summary>A looping cutscene paused for Reduce Motion is meant to be still.</summary>
    public bool HeldStill => loop && GameSettings.ReduceMotion;

    void Update()
    {
        if (!started || IsFinished || Skipping) return;
        if (loop) return;                              // nothing to finish

        double t = Director.time;
        if (Math.Abs(t - lastTime) > 1e-5) { lastTime = t; stillFor = 0f; }
        else stillFor += Time.unscaledDeltaTime;

        if (stillFor > stallSeconds)
        {
            Debug.LogWarning("CutsceneManager: " + cutsceneName + " stalled at " + t.ToString("0.00") + " s; ending it.");
            StartCoroutine(SkipRoutine("stall"));
        }
        else if (Time.unscaledTime - startedAt > Duration + overrunSeconds)
        {
            Debug.LogWarning("CutsceneManager: " + cutsceneName + " ran over its " + Duration.ToString("0.0") + " s; ending it.");
            StartCoroutine(SkipRoutine("overrun"));
        }
        else if (Director.state != PlayState.Playing && t >= Duration - 0.02 && Duration > 0)
            Finish(false, "end");
    }

    void OnStopped(PlayableDirector d)
    {
        // The director stops itself at the end of the Timeline (extrapolation None) and rewinds to 0, so its time can't
        // tell us it got there; any stop we did not make ourselves (Finish stops it after IsFinished is set) is the end.
        if (!gameObject.activeInHierarchy || !gameObject.scene.isLoaded) return;      // being unloaded, not ending
        if (!IsFinished && !Skipping && !loop) Finish(false, "end");
    }

    /// <summary>SKIP: a 0.3 s fade to black (with the sound), then <see cref="Finished"/>.</summary>
    public void Skip()
    {
        if (IsFinished || Skipping || loop) return;
        StartCoroutine(SkipRoutine("skip"));
    }

    IEnumerator SkipRoutine(string reason)
    {
        Skipping = true;
        yield return ScreenFade.Out(SkipFadeSeconds);
        Finish(true, reason);
    }

    void Finish(bool skipped, string reason)
    {
        if (IsFinished) return;
        IsFinished = true;
        WasSkipped = skipped;
        EndReason = reason;
        Director.stopped -= OnStopped;
        if (Director.state == PlayState.Playing) Director.Stop();
        if (Overlay != null) Overlay.gameObject.SetActive(false);
        Debug.Log("CutsceneManager: " + cutsceneName + " finished (" + reason + ") after " +
                  (Time.unscaledTime - startedAt).ToString("0.0") + " s.");
        try { Finished?.Invoke(skipped); onFinished.Invoke(skipped); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // Unloaded or switched off mid-way: that is not the cutscene ending.
    void OnDisable()
    {
        if (Director != null) Director.stopped -= OnStopped;
    }

    void OnDestroy()
    {
        if (Director != null) Director.stopped -= OnStopped;
        CinematicMotion.ReduceMotionChanged -= ApplyMotion;
        if (Overlay != null) Destroy(Overlay.gameObject);
        if (Playing == this) Playing = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Playing = null;
}
