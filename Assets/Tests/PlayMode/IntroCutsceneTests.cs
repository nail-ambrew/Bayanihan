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
        Assert.That(Intro.manager.Duration, Is.InRange(50.0, 58.0), "about a minute");

        float clock = RescueManager.Instance.TimeLeft;
        yield return WaitSeconds(1.5f);
        Assert.AreEqual(clock, RescueManager.Instance.TimeLeft, 0.0001f, "the mission clock has not started");
        Assert.Less(FloodLevel.Offset, -0.5f, "the streets are still nearly dry at the start");
    }

    [UnityTest]
    public IEnumerator The_story_has_five_beats_in_order()
    {
        yield return LaunchWithIntro();
        var timeline = (TimelineAsset)Intro.manager.Director.playableAsset;
        var beats = timeline.markerTrack.GetMarkers().OfType<SignalEmitter>()
                            .Where(e => e.asset != null && e.asset.name.StartsWith("Beat_"))
                            .OrderBy(e => e.time).Select(e => e.asset.name).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(1, 5).Select(i => "Beat_" + i).ToList(), beats);
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
        var marker = GameObject.Find("RescueBase_Spawn");
        Assert.IsNotNull(marker, "the rescue base marker");
        Assert.Less(Vector3.Distance(Object.FindFirstObjectByType<SimpleBoat>().transform.position, marker.transform.position), 1.5f,
                    "the mission starts at the Barangay Hall, where the film ended");
    }

    /// <summary>Skips the film <paramref name="seconds"/> in and checks the mission is playable at once.</summary>
    static IEnumerator SkipAt(double seconds)
    {
        yield return LaunchWithIntro();
        yield return WaitUntil(() => Intro.manager.Director.time >= seconds, 80f, "the film to reach " + seconds + " s");
        Intro.manager.Skip();
        yield return WaitUntil(() => !CutsceneHold.Active, 2f, "the mission to be released");
        Assert.IsFalse(InputHelper.GameplayLocked, "controls work after a skip at " + seconds + " s");
        Assert.AreEqual(0f, FloodLevel.Offset, 0.0001f, "the flood is at mission level");
        Assert.IsFalse(Intro != null && Intro.CastShown("HomeAll") && Intro.CastShown("Hall"), "the cast is gone");
        foreach (var spot in Object.FindObjectsByType<CharacterSpot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            Assert.IsFalse(spot.gameObject.scene.name == IntroFlow.SceneName && spot.gameObject.activeInHierarchy, spot.name + " is still on screen");
    }

    [UnityTest] public IEnumerator Skip_one_second_in_is_playable() { yield return SkipAt(1.0); }
    [UnityTest] public IEnumerator Skip_in_the_tragedy_is_playable() { yield return SkipAt(30.0); }
    [UnityTest] public IEnumerator Skip_during_the_hope_is_playable() { yield return SkipAt(45.0); }

    [UnityTest]
    public IEnumerator Everyone_in_the_story_stands_on_the_ground_or_a_roof_or_floats_on_the_water()
    {
        yield return LaunchWithIntro();
        var spots = Intro.castRoot.GetComponentsInChildren<CharacterSpot>(true);
        Assert.GreaterOrEqual(spots.Length, 11);
        foreach (var spot in spots)
        {
            Vector3 at = spot.transform.position;
            if (spot.floatOnWater)
            {
                Assert.That(at.y, Is.InRange(-1.6f, 0.6f), spot.name + " is not in the water");
                continue;
            }
            var hits = Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore)
                              .Where(h => h.collider.GetComponentInParent<WaterSurface>() == null && h.collider.GetComponentInParent<SimpleBoat>() == null
                                          && h.collider.GetComponentInParent<Survivor>() == null && h.collider.GetComponentInParent<PlayerRescuer>() == null)
                              .OrderBy(h => h.distance).ToArray();
            Assert.IsNotEmpty(hits, spot.name + " has nothing under it");
            Assert.AreEqual(hits[0].point.y, at.y, 0.35f, spot.name + " is floating or sunk (" + at.y.ToString("0.00") + " against ground at " + hits[0].point.y.ToString("0.00") + ")");
        }
    }

    [UnityTest]
    public IEnumerator SKIP_is_on_screen_with_the_film_and_gone_once_the_mission_starts()
    {
        // SKIP belongs to the film, not to the loadout or the loading (PackingStageTests: none over READY).
        yield return LaunchWithIntro();
        Assert.IsNotNull(Intro.manager.Overlay.Skip, "SKIP with the film");
        Intro.manager.Skip();
        yield return WaitUntil(() => !CutsceneHold.Active, 2f, "the mission to be released");
        yield return WaitSeconds(0.6f);
        Assert.IsNull(Object.FindFirstObjectByType<CutsceneSkipButton>(), "no SKIP once the mission has started");
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
        yield return WaitUntil(() => Intro.manager.Director.time >= length - 1.2, 80f, "the last second");
        Assert.AreEqual(condition, resident.Condition01, 0.0001f, "nobody weakened during the intro");
        Assert.GreaterOrEqual(Intro.BeatsSeen.Count, 5, "all five beats played");
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
