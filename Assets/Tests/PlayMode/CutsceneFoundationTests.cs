using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using static GameplayTestHelpers;

/// <summary>
/// The pieces every cutscene stands on: the SKIP button and its keys, the 0.3 s fade, the watchdog, the hold on the
/// mission while an intro plays, the HUD fading in after it, subtitles, the flood level, the lightning flash limit and
/// the three cinematic settings. Cutscenes here are small Timelines made in the test.
/// </summary>
public class CutsceneFoundationTests
{
    static int sceneCount;
    Keyboard keyboard;
    Gamepad gamepad;

    [UnitySetUp]
    public IEnumerator Prepare()
    {
        GameSettings.Reset();
        // A clean, empty scene: a mission left loaded by another test would end its clock and pause time mid-test.
        GamePause.Clear();
        Time.timeScale = 1f;
        Application.runInBackground = true;
        var empty = SceneManager.CreateScene("CutsceneTest_" + (++sceneCount));
        SceneManager.SetActiveScene(empty);
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var s = SceneManager.GetSceneAt(i);
            // Never the test runner's own scene (InitTestScene...): it would stop the run.
            if (s != empty && !s.name.StartsWith("InitTestScene")) yield return SceneManager.UnloadSceneAsync(s);
        }
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("CutsceneTestKeyboard");
        gamepad = InputSystem.AddDevice<Gamepad>("CutsceneTestGamepad");
        keyboard.MakeCurrent();
        gamepad.MakeCurrent();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (gamepad != null) InputSystem.RemoveDevice(gamepad);
        CutsceneHold.Release();
        GamePause.Clear();
        Time.timeScale = 1f;
        FloodLevel.Set(0f);
        ScreenFade.Clear();
        GameSettings.Reset();
        foreach (var m in Object.FindObjectsByType<CutsceneManager>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
        yield return null;
    }

    // ------------------------------------------------------------------ helpers
    static TimelineAsset Timeline(double seconds)
    {
        var t = ScriptableObject.CreateInstance<TimelineAsset>();
        var subs = t.CreateTrack<SubtitleTrack>(null, "Subtitles");
        var clip = subs.CreateClip<SubtitleClip>();
        clip.start = 0.1;
        clip.duration = seconds - 0.2;
        ((SubtitleClip)clip.asset).line = "Weather bulletin.";
        t.fixedDuration = seconds;
        t.durationMode = TimelineAsset.DurationMode.FixedLength;
        return t;
    }

    static CutsceneManager Cutscene(double seconds, bool loop = false)
    {
        var go = new GameObject("TestCutscene");
        go.SetActive(false);
        var director = go.AddComponent<PlayableDirector>();
        director.playableAsset = Timeline(seconds);
        var m = go.AddComponent<CutsceneManager>();
        m.cutsceneName = "Cut_Test";
        m.loop = loop;
        go.AddComponent<CutsceneCueReceiver>();
        go.SetActive(true);
        return m;
    }

    /// <summary>Past the SKIP button's grace for keys held over from the launch.</summary>
    static IEnumerator Settle()
    {
        for (int i = 0; i <= CutsceneSkipButton.KeyGraceFrames + 1; i++) yield return null;
        yield return WaitSeconds(CutsceneSkipButton.KeyGraceSeconds + 0.05f);
    }

    IEnumerator Tap(InputDevice device, ButtonControl button)
    {
        if (device is Keyboard)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(((KeyControl)button).keyCode));
        else
        {
            var state = new GamepadState();
            state = state.WithButton(button == gamepad.startButton ? GamepadButton.Start : GamepadButton.East);
            InputSystem.QueueStateEvent(gamepad, state);
        }
        yield return null;
        if (device is Keyboard) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        else InputSystem.QueueStateEvent(gamepad, new GamepadState());
        yield return null;
    }

    // ------------------------------------------------------------------ settings
    [Test]
    public void The_cinematic_settings_default_to_intro_on_motion_full_and_subtitles_on()
    {
        Assert.IsTrue(GameSettings.PlayIntro);
        Assert.IsFalse(GameSettings.ReduceMotion);
        Assert.IsTrue(GameSettings.Subtitles);
        GameSettings.PlayIntro = false;
        GameSettings.ReduceMotion = true;
        GameSettings.Subtitles = false;
        Assert.IsFalse(GameSettings.PlayIntro);
        Assert.IsTrue(GameSettings.ReduceMotion);
        Assert.IsFalse(GameSettings.Subtitles);
    }

    [Test]
    public void Lightning_never_flashes_more_than_three_times_a_second()
    {
        int allowed = 0;
        for (int i = 0; i < 10; i++) if (FlashLimiter.Allow()) allowed++;
        Assert.LessOrEqual(allowed, FlashLimiter.MaxPerSecond);
        Assert.LessOrEqual(FlashLimiter.InLastSecond, 3);
    }

    [UnityTest]
    public IEnumerator Reduce_motion_allows_no_flashes()
    {
        yield return WaitSeconds(1.05f);                 // let earlier flashes age out
        GameSettings.ReduceMotion = true;
        Assert.IsFalse(FlashLimiter.Allow());
    }

    // ------------------------------------------------------------------ the button
    [UnityTest]
    public IEnumerator The_skip_button_is_on_screen_from_the_first_frame_bottom_right()
    {
        var m = Cutscene(5);
        yield return null;                                 // Start has run: this is the cutscene's first frame
        Assert.IsNotNull(m.Overlay, "overlay");
        var skip = m.Overlay.Skip;
        Assert.IsNotNull(skip, "SKIP button");
        Assert.IsTrue(skip.isActiveAndEnabled, "visible");
        var corners = new Vector3[4];
        skip.Rect.GetWorldCorners(corners);
        Assert.Greater(corners[2].x, Screen.width * 0.75f, "on the right");
        Assert.Less(corners[0].y, Screen.height * 0.25f, "at the bottom");
        Assert.IsTrue(m.Overlay.SubtitleShown == null || m.Overlay.SubtitleShown.Length > 0);
    }

    [UnityTest]
    public IEnumerator Hovering_the_button_brightens_it_and_grows_it_to_105_percent()
    {
        var m = Cutscene(5);
        yield return null;
        var skip = m.Overlay.Skip;
        skip.SetHover(true);
        yield return WaitSeconds(0.25f);
        Assert.IsTrue(skip.Lit);
        Assert.AreEqual(CutsceneSkipButton.Style.HoverScale, skip.Scale, 0.005f);
    }

    [UnityTest]
    public IEnumerator Clicking_skip_fades_to_black_in_a_third_of_a_second_then_finishes()
    {
        var m = Cutscene(5);
        bool? skipped = null;
        m.Finished += s => skipped = s;
        yield return null;
        float at = Time.unscaledTime;
        m.Overlay.Skip.Press();
        yield return WaitUntil(() => m.IsFinished, 2f, "the cutscene to finish");
        float took = Time.unscaledTime - at;
        Assert.AreEqual(true, skipped);
        Assert.AreEqual("skip", m.EndReason);
        Assert.That(took, Is.InRange(CutsceneManager.SkipFadeSeconds - 0.05f, CutsceneManager.SkipFadeSeconds + 0.4f), "fade time");
        Assert.Greater(ScreenFade.Alpha, 0.95f, "black at the cut");
        Assert.Less(AudioListener.volume, 0.05f, "the sound faded with it");
        ScreenFade.In(0.1f);
        yield return WaitSeconds(0.2f);
        Assert.AreEqual(GameSettings.Volume, AudioListener.volume, 0.01f, "sound back after the fade in");
    }

    static readonly string[] Keys = { "escape", "space", "enter" };

    [UnityTest]
    public IEnumerator Esc_space_and_enter_each_skip([ValueSource(nameof(Keys))] string key)
    {
        var m = Cutscene(5);
        yield return Settle();
        var control = key == "escape" ? keyboard.escapeKey : key == "space" ? keyboard.spaceKey : keyboard.enterKey;
        yield return Tap(keyboard, control);
        yield return WaitUntil(() => m.IsFinished, 2f, "a skip by " + key);
        Assert.IsTrue(m.WasSkipped);
    }

    [UnityTest]
    public IEnumerator Gamepad_start_and_b_each_skip([Values(true, false)] bool start)
    {
        var m = Cutscene(5);
        yield return Settle();
        yield return Tap(gamepad, start ? gamepad.startButton : gamepad.buttonEast);
        yield return WaitUntil(() => m.IsFinished, 2f, "a skip by the gamepad");
        Assert.IsTrue(m.WasSkipped);
    }

    [UnityTest]
    public IEnumerator A_key_still_down_from_the_launch_does_not_skip()
    {
        var m = Cutscene(5);
        yield return Tap(keyboard, keyboard.enterKey);     // in the first frames: the press that launched it
        yield return WaitSeconds(0.5f);
        Assert.IsFalse(m.IsFinished || m.Skipping);
    }

    // ------------------------------------------------------------------ ending
    [UnityTest]
    public IEnumerator A_cutscene_played_through_finishes_on_its_own_without_a_fade()
    {
        var m = Cutscene(1.0);
        bool? skipped = null;
        m.Finished += s => skipped = s;
        yield return WaitUntil(() => m.IsFinished, 3f, "the end");
        Assert.AreEqual(false, skipped);
        Assert.AreEqual("end", m.EndReason);
        Assert.Less(ScreenFade.Alpha, 0.05f);
    }

    [UnityTest]
    public IEnumerator The_watchdog_ends_a_stalled_cutscene()
    {
        var m = Cutscene(30);
        m.stallSeconds = 0.5f;
        yield return null;
        m.Director.Pause();                                // something broke: the Timeline stopped moving
        yield return WaitUntil(() => m.IsFinished, 3f, "the watchdog");
        Assert.AreEqual("stall", m.EndReason);
    }

    [UnityTest]
    public IEnumerator Subtitles_show_the_spoken_line_and_hide_when_turned_off()
    {
        var m = Cutscene(3);
        yield return WaitSeconds(0.3f);
        Assert.AreEqual("Weather bulletin.", m.Overlay.SubtitleShown);
        Object.Destroy(m.gameObject);
        yield return null;

        GameSettings.Subtitles = false;
        var quiet = Cutscene(3);
        yield return WaitSeconds(0.3f);
        Assert.IsNull(quiet.Overlay.SubtitleShown);
    }

    [UnityTest]
    public IEnumerator The_letterbox_slides_in_to_two_point_three_nine_to_one()
    {
        var m = Cutscene(3);
        yield return WaitSeconds(CutsceneOverlay.BarSlideSeconds + 0.1f);
        Assert.AreEqual(1f, m.Overlay.Bars, 0.001f);
    }

    [UnityTest]
    public IEnumerator Signal_emitters_reach_the_cue_receiver()
    {
        var m = Cutscene(2);
        var timeline = (TimelineAsset)m.Director.playableAsset;
        var signal = ScriptableObject.CreateInstance<SignalAsset>();
        signal.name = "Cue_TitleHit";
        timeline.CreateMarkerTrack();
        timeline.markerTrack.CreateMarker<SignalEmitter>(0.2).asset = signal;
        m.Director.RebuildGraph();
        m.Director.Play();
        yield return WaitSeconds(0.5f);
        Assert.Contains("Cue_TitleHit", m.GetComponent<CutsceneCueReceiver>().Received);
        Assert.IsTrue(m.Overlay.TitleShowing, "the title card is up");
    }

    [UnityTest]
    public IEnumerator A_looping_cutscene_holds_still_with_reduce_motion()
    {
        GameSettings.ReduceMotion = true;
        var m = Cutscene(4, loop: true);
        yield return WaitSeconds(0.5f);
        Assert.AreEqual(0.0, m.Director.time, 0.001, "held on its still frame");
        Assert.IsNull(m.Overlay.Skip, "a background has no SKIP");
        GameSettings.ReduceMotion = false;
        CinematicMotion.Changed();
        yield return WaitSeconds(0.3f);
        Assert.Greater(m.Director.time, 0.1, "plays again");
    }

    // ------------------------------------------------------------------ the hold on the mission
    [UnityTest]
    public IEnumerator While_held_the_clock_residents_flood_and_controls_wait_and_the_hud_is_hidden()
    {
        yield return LoadGameplay();
        CutsceneHold.Begin();
        yield return null;
        var manager = RescueManager.Instance;
        float clock = manager.TimeLeft;
        var resident = Object.FindFirstObjectByType<SurvivorCondition>();
        resident.SetCondition(0.8f);
        float rise = FloodRise.Instance != null ? FloodRise.Instance.Rise : 0f;

        yield return WaitSeconds(1f);
        Assert.AreEqual(clock, manager.TimeLeft, 0.0001f, "the clock waits");
        Assert.AreEqual(0.8f, resident.Condition01, 0.0001f, "nobody weakens");
        if (FloodRise.Instance != null) Assert.AreEqual(rise, FloodRise.Instance.Rise, 0.0001f, "the water waits");
        Assert.IsTrue(InputHelper.GameplayLocked, "controls do nothing");
        Assert.AreEqual(0f, GameHud.Instance.Alpha, 0.001f, "no HUD over the cutscene");
        Assert.Less(manager.Elapsed, 0.1f, "the mission's own time has not started");

        CutsceneHold.Release();
        yield return WaitSeconds(GameHud.FadeInSeconds + 0.1f);
        Assert.Less(manager.TimeLeft, clock, "the clock runs");
        Assert.IsFalse(InputHelper.GameplayLocked);
        Assert.AreEqual(1f, GameHud.Instance.Alpha, 0.001f, "the HUD has faded in");
    }

    [UnityTest]
    public IEnumerator Esc_during_the_hold_does_not_open_the_pause_menu()
    {
        yield return LoadGameplay();
        CutsceneHold.Begin();
        yield return null;
        yield return Tap(keyboard, keyboard.escapeKey);
        Assert.IsFalse(GamePause.IsPaused);
    }

    [UnityTest]
    public IEnumerator The_flood_level_offsets_the_water_from_its_start()
    {
        yield return LoadGameplay();
        var water = WaterSurface.Instance;
        yield return null;
        float start = water.transform.position.y;
        FloodLevel.Set(-0.5f);
        yield return null;
        yield return null;
        Assert.AreEqual(start - 0.5f, water.transform.position.y, 0.01f);
        FloodLevel.Set(0f);
        yield return null;
        yield return null;
        Assert.AreEqual(start, water.transform.position.y, 0.01f);
    }
}
