using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// The cinematics as built (Bayanihan > Cutscenes > Build ...): the Timelines have the right tracks, lengths and beats,
/// every camera clip and signal resolves, the scenes' references are all set, they are in the build, and every
/// placeholder sound and model is in its slot. Nothing here plays; PlayMode tests do that.
/// </summary>
public class CutsceneAssetTests
{
    static TimelineAsset Load(string path)
    {
        var t = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
        Assert.IsNotNull(t, path + " (run its builder)");
        return t;
    }

    [Test]
    public void The_intro_timeline_is_twenty_to_twenty_five_seconds_with_seven_beats_in_order()
    {
        var t = Load(IntroCutsceneBuilder.TimelinePath);
        Assert.That(t.duration, Is.InRange(20.0, 25.0));
        var tracks = t.GetOutputTracks().ToList();
        Assert.IsTrue(tracks.OfType<CinemachineTrack>().Any(), "cameras");
        Assert.IsTrue(tracks.OfType<FloodLevelTrack>().Any(), "flood level");
        Assert.IsTrue(tracks.OfType<StormTrack>().Any(), "storm");
        Assert.IsTrue(tracks.OfType<SubtitleTrack>().Any(), "subtitles");
        Assert.IsTrue(tracks.OfType<AudioTrack>().Any(), "sound");

        var emitters = t.markerTrack.GetMarkers().OfType<SignalEmitter>().ToList();
        Assert.IsTrue(emitters.All(e => e.asset != null), "every signal emitter has its signal");
        var beats = emitters.Where(e => e.asset.name.StartsWith("Beat_")).OrderBy(e => e.time).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(1, 7).Select(i => "Beat_" + i), beats.Select(b => b.asset.name));
        for (int i = 0; i < beats.Count; i++)
            Assert.AreEqual(IntroCutsceneBuilder.Beats[i].start, beats[i].time, 0.05, "beat " + (i + 1) + " starts on its table time");

        // the flood ends at mission level, so the hand-over has no jump
        var flood = tracks.OfType<FloodLevelTrack>().First().GetClips().OrderBy(c => c.end).Last();
        Assert.AreEqual(0f, ((FloodLevelClip)flood.asset).to, 1e-4f);
        Assert.AreEqual(t.duration, flood.end, 0.01, "and holds there to the end");
    }

    [Test]
    public void The_menu_timeline_loops_about_every_twenty_seconds_through_five_shots()
    {
        var t = Load(MenuCinematicBuilder.TimelinePath);
        Assert.That(t.duration, Is.InRange(18.0, 22.0));
        var shots = t.GetOutputTracks().OfType<CinemachineTrack>().Single().GetClips().ToList();
        Assert.AreEqual(MenuCinematicBuilder.Shots.Length + 1, shots.Count, "five shots and the loop-in");
        Assert.AreEqual(t.duration, shots.Max(c => c.end), 0.01, "a camera to the very end");
    }

    [Test]
    public void The_intro_scene_has_every_reference_and_every_camera_clip_resolves()
    {
        var scene = EditorSceneManager.OpenScene(IntroCutsceneBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            var intro = Find<IntroCutscene>(scene);
            Assert.IsNotNull(intro);
            Assert.IsNotNull(intro.manager, "manager");
            Assert.IsNotNull(intro.cues, "cues");
            Assert.IsNotNull(intro.settle, "the last shot");
            Assert.IsNotNull(intro.debris, "debris pool");
            Assert.IsNotNull(intro.siren, "siren");
            Assert.IsNotNull(intro.look, "look volume");
            Assert.IsNotNull(intro.childSpot, "child spot");
            Assert.IsTrue(intro.shots.Length >= 7 && intro.shots.All(s => s != null), "shots");
            Assert.IsTrue(intro.debris.extraKinds.Length >= 6 && intro.debris.extraKinds.All(k => k != null), "six kinds of rubbish");
            AssertShotsResolve(intro.manager.GetComponent<PlayableDirector>());
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void The_menu_scene_is_bound_to_its_own_camera_and_storm()
    {
        var scene = EditorSceneManager.OpenScene(MenuCinematicBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            var menu = Find<MenuCinematic>(scene);
            Assert.IsNotNull(menu);
            Assert.IsNotNull(menu.manager);
            Assert.IsNotNull(menu.weather);
            Assert.IsTrue(menu.manager.loop);
            var director = menu.manager.GetComponent<PlayableDirector>();
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks())
                if (track is CinemachineTrack || track is StormTrack)
                    Assert.IsNotNull(director.GetGenericBinding(track), track.name + " is bound");
            AssertShotsResolve(director);
            Assert.GreaterOrEqual(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterSpot>()).Count(), 7, "the crew");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void Both_cutscene_scenes_are_in_the_build()
    {
        var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
        CollectionAssert.Contains(paths, IntroCutsceneBuilder.ScenePath);
        CollectionAssert.Contains(paths, MenuCinematicBuilder.ScenePath);
    }

    [Test]
    public void Every_placeholder_sound_and_model_is_in_its_slot()
    {
        var a = Resources.Load<CutsceneAssets>(CutsceneAssets.ResourcePath);
        Assert.IsNotNull(a, "Resources/" + CutsceneAssets.ResourcePath + " (Bayanihan > Cutscenes > Build Audio)");
        Assert.IsNotNull(a.mixer, "mixer");
        Assert.IsNotNull(a.music); Assert.IsNotNull(a.sfx); Assert.IsNotNull(a.ambience); Assert.IsNotNull(a.vo);
        foreach (var p in new[] { CutsceneAudio.MusicParam, CutsceneAudio.SfxParam, CutsceneAudio.AmbienceParam, CutsceneAudio.VoParam })
            Assert.IsTrue(a.mixer.GetFloat(p, out _), p + " is exposed");
        foreach (var clip in new Object[] { a.rainLight, a.rainHeavy, a.tinDrips, a.waterRush, a.wind, a.siren, a.whoosh, a.splash,
                                             a.packRustle, a.packZip, a.packBuckle, a.packDenied, a.readyStamp, a.radioBulletin, a.musicCue })
            Assert.IsNotNull(clip);
        Assert.GreaterOrEqual(a.thunder.Length, 2);
        Assert.IsNotNull(a.backpack, "the dry-bag");
        foreach (RescueItemId id in System.Enum.GetValues(typeof(RescueItemId)))
            Assert.IsNotNull(a.ItemModel(id), "a model for " + id);
    }

    [Test]
    public void The_lightning_limit_and_quality_presets_keep_their_promises()
    {
        Assert.AreEqual(3, FlashLimiter.MaxPerSecond);
        Assert.LessOrEqual(CinematicQuality.High.debrisCap, 40);
        Assert.That(CinematicQuality.High.rainRate, Is.InRange(5000f, 8000f));
        Assert.Less(CinematicQuality.Low.rainRate, CinematicQuality.Medium.rainRate);
        Assert.Less(CinematicQuality.Medium.rainRate, CinematicQuality.High.rainRate);
    }

    static void AssertShotsResolve(PlayableDirector director)
    {
        var cm = ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<CinemachineTrack>().Single();
        foreach (var clip in cm.GetClips())
        {
            var shot = (CinemachineShot)clip.asset;
            var cam = director.GetReferenceValue(shot.VirtualCamera.exposedName, out bool valid);
            Assert.IsTrue(valid && cam != null, clip.displayName + " has its camera");
        }
    }

    static T Find<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault();
}
