using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static GameplayTestHelpers;

/// <summary>
/// Choosing where to play on the real loadout screen: the strip under the cards offers both maps, the briefing and dispatch change
/// with the pick, LAUNCH loads the picked map's scene with its mission in force, and a pick that was never made leaves the game
/// exactly as it was - Manila, in the Gameplay scene. The buttons are pressed as a click would press them.
/// </summary>
public class MapChoiceFlowTests
{
    const string Loadout = "Loadout";

    [UnitySetUp]
    public IEnumerator Open_the_loadout_screen()
    {
        Application.runInBackground = true;
        Time.timeScale = 1f;
        MapChoice.Reset();
        LoadoutState.Clear();
        yield return SceneManager.LoadSceneAsync(Loadout, LoadSceneMode.Single);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    static Button MapButton(string id)
    {
        var go = GameObject.Find("Map_" + id);
        Assert.IsNotNull(go, "the loadout screen has no button for the '" + id + "' map");
        return go.GetComponent<Button>();
    }

    static string Label(string path)
    {
        var go = GameObject.Find(path);
        Assert.IsNotNull(go, "the loadout screen has no '" + path + "'");
        return go.GetComponent<Text>().text;
    }

    static string Briefing(string part) => Label("LoadoutCanvas/BoatPanel/Briefing/" + part);

    static void PressLaunch()
    {
        var launch = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                           .FirstOrDefault(b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.Contains("LAUNCH"));
        Assert.IsNotNull(launch, "the loadout screen has no launch button");
        launch.onClick.Invoke();
    }

    static bool Highlighted(string id) => GameObject.Find("Map_" + id).GetComponent<Outline>().enabled;

    [UnityTest]
    public IEnumerator It_opens_on_the_Manila_map_and_offers_both()
    {
        Assert.AreEqual(2, MapChoice.All.Count);
        Assert.IsNotNull(MapButton(MapChoice.ManilaId));
        Assert.IsNotNull(MapButton(MapChoice.MabuhayId));
        Assert.IsTrue(Highlighted(MapChoice.ManilaId), "Manila is picked until something else is");
        Assert.IsFalse(Highlighted(MapChoice.MabuhayId));
        Assert.IsTrue(MapChoice.IsDefault);
        StringAssert.DoesNotContain("Mabuhay", Briefing("Text"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Picking_Mabuhay_puts_its_briefing_and_dispatch_on_the_screen()
    {
        MapButton(MapChoice.MabuhayId).onClick.Invoke();
        yield return null;

        var mission = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        Assert.IsTrue(Highlighted(MapChoice.MabuhayId));
        Assert.IsFalse(Highlighted(MapChoice.ManilaId));
        Assert.AreSame(mission, MissionDefinition.Current, "the mission in force is the picked map's");

        Assert.AreEqual(mission.title, Briefing("Title"));
        StringAssert.Contains("Barangay Mabuhay, Carmona", Briefing("Text"));
        StringAssert.Contains(mission.residents.Count + " residents", Briefing("Meta"));
        Assert.AreEqual(mission.Intel(), Label("LoadoutCanvas/Dispatch/Text"), "dispatch tells the crew what is out there on this map");
        Assert.IsTrue(GameObject.Find("LoadoutCanvas/Dispatch") != null);
    }

    [UnityTest]
    public IEnumerator Launching_with_nothing_picked_plays_Manila_as_it_always_has()
    {
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == "Gameplay", 15f, "the Gameplay scene");
        yield return null;

        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current, "Manila's own mission is the one in force");
        Assert.AreEqual(MissionDefinition.LoadShipped().residents.Count, RescueManager.Instance.TotalSurvivors);
    }

    [UnityTest]
    public IEnumerator Launching_after_picking_Mabuhay_loads_the_Mabuhay_scene_and_plays_its_mission()
    {
        MapButton(MapChoice.MabuhayId).onClick.Invoke();
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == MapChoice.MabuhayScene, 20f, "the Mabuhay scene");
        yield return null;

        var mission = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        Assert.AreSame(mission, MissionDefinition.Current);
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.TotalSurvivors);
        Assert.AreEqual(mission.timeLimit, RescueManager.Instance.timeLimit);
    }

    [UnityTest]
    public IEnumerator The_pick_survives_the_trip_back_to_the_loadout_and_can_be_changed_back_to_Manila()
    {
        MapButton(MapChoice.MabuhayId).onClick.Invoke();
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == MapChoice.MabuhayScene, 20f, "the Mabuhay scene");
        yield return null;

        // Esc on the report goes back to the loadout screen: it opens on the map that was just played.
        SceneManager.LoadScene(Loadout);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Loadout, 15f, "the loadout screen");
        yield return null;
        Assert.IsTrue(Highlighted(MapChoice.MabuhayId), "the loadout should open on the map that was just played");
        StringAssert.Contains("Barangay Mabuhay, Carmona", Briefing("Text"));

        // And picking Manila puts everything of Manila's back: its briefing, and its mission for the scene to read.
        MapButton(MapChoice.ManilaId).onClick.Invoke();
        yield return null;
        Assert.IsTrue(Highlighted(MapChoice.ManilaId));
        StringAssert.DoesNotContain("Mabuhay", Briefing("Text"));
        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current, "Manila's mission is in force again");

        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == "Gameplay", 15f, "the Gameplay scene");
        yield return null;
        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current, "Gameplay plays Manila's mission, not the one from the last map");
        Assert.AreEqual(MissionDefinition.LoadShipped().residents.Count, RescueManager.Instance.TotalSurvivors);
    }
}
