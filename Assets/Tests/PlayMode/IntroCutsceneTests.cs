using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using static GameplayTestHelpers;

/// <summary>
/// The flood intro over the real Mabuhay map: it starts over a held mission with SKIP on screen from its first frame,
/// tells the story in seven beats inside 20-25 s, and hands the mission back (skipped or played through) with the
/// camera, storm, rain and flood exactly where play begins, the clock running and the HUD faded in.
/// </summary>
public class IntroCutsceneTests
{
    [UnitySetUp]
    public IEnumerator Prepare()
    {
        GameSettings.Reset();
        GamePause.Clear();
        Time.timeScale = 1f;
        Application.runInBackground = true;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        IntroFlow.Disarm();
        CutsceneHold.Release();
        FloodLevel.Set(0f);
        ScreenFade.Clear();
        GamePause.Clear();
        GameSettings.Reset();
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    /// <summary>LAUNCH into Mabuhay with the intro on, as the loadout screen does.</summary>
    static IEnumerator LaunchWithIntro()
    {
        Assert.IsTrue(IntroFlow.ShouldPlay(MapChoice.MabuhayScene), "the intro is in the build and on");
        IntroFlow.Arm();
        yield return MabuhayTestHelpers.LoadMabuhay();
        yield return WaitUntil(() => IntroCutscene.Current != null && IntroCutscene.Current.Started, 20f, "the intro to start");
    }

    static IntroCutscene Intro => IntroCutscene.Current;

    [UnityTest]
    public IEnumerator The_intro_starts_over_a_held_mission_with_skip_on_screen()
    {
        yield return LaunchWithIntro();
        Assert.IsTrue(CutsceneHold.Active, "the mission waits");
        Assert.IsTrue(InputHelper.GameplayLocked, "no controls");
        var skip = Intro.manager.Overlay.Skip;
        Assert.IsNotNull(skip, "SKIP");
        Assert.IsTrue(skip.isActiveAndEnabled, "SKIP is on screen from the first frame");
        Assert.That(Intro.manager.Duration, Is.InRange(20.0, 25.0), "20-25 seconds");

        float clock = RescueManager.Instance.TimeLeft;
        yield return WaitSeconds(1.5f);
        Assert.AreEqual(clock, RescueManager.Instance.TimeLeft, 0.0001f, "the mission clock has not started");
        Assert.Less(FloodLevel.Offset, -0.5f, "the streets are still nearly dry at the start");
    }

    [UnityTest]
    public IEnumerator The_story_has_seven_beats_in_order()
    {
        yield return LaunchWithIntro();
        var timeline = (TimelineAsset)Intro.manager.Director.playableAsset;
        var beats = timeline.markerTrack.GetMarkers().OfType<SignalEmitter>()
                            .Where(e => e.asset != null && e.asset.name.StartsWith("Beat_"))
                            .OrderBy(e => e.time).Select(e => e.asset.name).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(1, 7).Select(i => "Beat_" + i).ToList(), beats);
        foreach (var e in timeline.markerTrack.GetMarkers().OfType<SignalEmitter>())
            Assert.IsNotNull(e.asset, "every cue has its signal");
        foreach (var track in timeline.GetOutputTracks())
            if (track is CinemachineTrack || track is StormTrack)
                Assert.IsNotNull(Intro.manager.Director.GetGenericBinding(track), track.name + " is bound to the map");
    }

    [UnityTest]
    public IEnumerator Skipping_hands_the_mission_over_at_once()
    {
        yield return LaunchWithIntro();
        var weather = Object.FindFirstObjectByType<TyphoonWeather>();
        yield return WaitSeconds(2f);
        float clock = RescueManager.Instance.TimeLeft;

        float at = Time.unscaledTime;
        Intro.manager.Skip();
        yield return WaitUntil(() => !CutsceneHold.Active, 2f, "the mission to be released");
        Assert.Less(Time.unscaledTime - at, 0.5f, "live within half a second of SKIP");

        Assert.AreEqual(0f, FloodLevel.Offset, 0.0001f, "the flood is at mission level");
        Assert.IsTrue(weather.autoLightning, "the mission's own lightning is back");
        Assert.AreEqual(1f, weather.rainRateScale, 0.0001f, "the mission's own rain");
        Assert.IsFalse(InputHelper.GameplayLocked, "controls work");

        yield return WaitSeconds(GameHud.FadeInSeconds + 0.4f);
        Assert.Less(RescueManager.Instance.TimeLeft, clock, "the clock runs");
        Assert.AreEqual(1f, GameHud.Instance.Alpha, 0.001f, "the HUD has faded in");
        Assert.Less(ScreenFade.Alpha, 0.05f, "out of the black");
        Assert.IsFalse(SceneManager.GetSceneByName(IntroFlow.SceneName).isLoaded, "the intro scene is gone");
        var brain = Camera.main.GetComponent<CinemachineBrain>();
        Assert.AreNotEqual(IntroFlow.SceneName, ((Component)brain.ActiveVirtualCamera).gameObject.scene.name, "the gameplay camera is live");
    }

    [UnityTest]
    public IEnumerator Played_through_it_ends_on_the_gameplay_camera_without_a_jump()
    {
        yield return LaunchWithIntro();
        var cam = Camera.main.transform;
        var weather = Object.FindFirstObjectByType<TyphoonWeather>();
        var resident = Object.FindObjectsByType<SurvivorCondition>(FindObjectsSortMode.None).First();
        float condition = resident.Condition01;
        double length = Intro.manager.Duration;

        // Up to the last second.
        yield return WaitUntil(() => Intro.manager.Director.time >= length - 1.2, 40f, "the last second");
        Assert.AreEqual(condition, resident.Condition01, 0.0001f, "nobody weakened during the intro");
        Assert.GreaterOrEqual(Intro.BeatsSeen.Count, 7, "all seven beats played");
        CollectionAssert.AreEqual(Intro.BeatsSeen.OrderBy(b => b).ToList(), Intro.BeatsSeen, "in order");

        // Across the hand-over and a second after it, the camera moves smoothly: no cut, no pop.
        var jumps = new List<float>();
        Vector3 last = cam.position;
        float until = Time.unscaledTime + 2.5f;
        while (Time.unscaledTime < until)
        {
            yield return null;
            jumps.Add(Vector3.Distance(last, cam.position));
            last = cam.position;
        }
        Assert.IsFalse(CutsceneHold.Active, "the mission has started");
        Assert.AreEqual("end", IntroCutscene.Current == null ? "end" : Intro.manager.EndReason);
        Assert.Less(jumps.Max(), 0.6f, "the camera never jumps at the hand-over");
        Assert.AreEqual(0f, FloodLevel.Offset, 0.0001f);
        Assert.AreEqual(weather.intensity, weather.CurrentIntensity, 0.02f, "the storm is the mission's");
    }

    [UnityTest]
    public IEnumerator The_debris_beat_floats_many_kinds_under_the_cap()
    {
        yield return LaunchWithIntro();
        var pool = Intro.debris;
        Assert.IsNotNull(pool);
        Assert.LessOrEqual(pool.Cap, 40, "never more than 40");
        Assert.GreaterOrEqual(pool.KindCount, 12, "at least twelve kinds");
        Assert.AreEqual(0, pool.Active, "hidden until their beat");
        pool.Release();
        yield return WaitSeconds(pool.releaseOver + 0.5f);
        Assert.AreEqual(pool.Cap, pool.Active, "all let go");
        Assert.LessOrEqual(pool.Active, CinematicQuality.Current.debrisCap);
    }

    [Test]
    public void With_play_intro_off_or_on_manila_launch_goes_straight_to_the_mission()
    {
        GameSettings.PlayIntro = false;
        Assert.IsFalse(IntroFlow.ShouldPlay(MapChoice.MabuhayScene));
        GameSettings.PlayIntro = true;
        Assert.IsFalse(IntroFlow.ShouldPlay(MapChoice.ManilaScene));
    }

    [UnityTest]
    public IEnumerator If_the_intro_never_starts_the_mission_starts_anyway()
    {
        yield return MabuhayTestHelpers.LoadMabuhay();
        CutsceneHold.Begin();
        IntroFlow.Abort("a test");
        yield return null;
        Assert.IsFalse(CutsceneHold.Active);
        Assert.IsFalse(InputHelper.GameplayLocked);
    }
}
