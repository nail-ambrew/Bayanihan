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
/// with the pick, LAUNCH loads the picked map's scene with its mission in force, and a pick that was never made plays Barangay
/// Mabuhay - the real barangay; the Manila sample map is the other choice. The buttons are pressed as a click would press them.
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

    static string Briefing(string part) => Label(DispatchPath + "/" + part);

    const string DispatchPath = "LoadoutCanvas/SafeArea/Loadout/Dispatch";

    static void PressLaunch()
    {
        var launch = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                           .FirstOrDefault(b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.Contains("LAUNCH"));
        Assert.IsNotNull(launch, "the loadout screen has no launch button");
        launch.onClick.Invoke();
    }

    static bool Highlighted(string id) => GameObject.Find("Map_" + id).GetComponent<Outline>().enabled;

    [UnityTest]
    public IEnumerator It_opens_on_Barangay_Mabuhay_and_offers_both()
    {
        Assert.AreEqual(2, MapChoice.All.Count);
        Assert.IsNotNull(MapButton(MapChoice.MabuhayId));
        Assert.IsNotNull(MapButton(MapChoice.ManilaId));
        Assert.IsTrue(Highlighted(MapChoice.MabuhayId), "Barangay Mabuhay is picked until something else is");
        Assert.IsFalse(Highlighted(MapChoice.ManilaId));
        Assert.IsTrue(MapChoice.IsDefault);

        var mission = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        Assert.AreSame(mission, MissionDefinition.Current, "the loadout puts the default map's mission in force");
        StringAssert.Contains("Barangay Mabuhay, Carmona", Briefing("Text"));
        Assert.AreEqual(LoadoutScreen.Objectives(mission), Briefing("Objectives"), "the objectives are Mabuhay's");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Picking_the_Manila_sample_puts_its_briefing_on_the_screen()
    {
        MapButton(MapChoice.ManilaId).onClick.Invoke();
        yield return null;

        Assert.IsTrue(Highlighted(MapChoice.ManilaId));
        Assert.IsFalse(Highlighted(MapChoice.MabuhayId));
        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current, "the mission in force is Manila's");
        StringAssert.DoesNotContain("Mabuhay", Briefing("Text"));
        Assert.IsTrue(GameObject.Find(DispatchPath) != null);
    }

    [UnityTest]
    public IEnumerator Launching_with_nothing_picked_plays_Barangay_Mabuhay()
    {
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == MapChoice.MabuhayScene, 20f, "the Mabuhay scene");
        yield return null;

        var mission = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        Assert.AreSame(mission, MissionDefinition.Current, "Mabuhay's mission is the one in force");
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.TotalSurvivors);
        Assert.AreEqual(mission.timeLimit, RescueManager.Instance.timeLimit);
    }

    [UnityTest]
    public IEnumerator Launching_after_picking_Manila_plays_the_sample_map_in_the_Gameplay_scene()
    {
        MapButton(MapChoice.ManilaId).onClick.Invoke();
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == "Gameplay", 15f, "the Gameplay scene");
        yield return null;

        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current, "Manila's own mission is the one in force");
        Assert.AreEqual(MissionDefinition.LoadShipped().residents.Count, RescueManager.Instance.TotalSurvivors);
    }

    [UnityTest]
    public IEnumerator The_pick_survives_the_trip_back_to_the_loadout_and_can_be_changed_back_to_Mabuhay()
    {
        MapButton(MapChoice.ManilaId).onClick.Invoke();
        PressLaunch();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == "Gameplay", 15f, "the Gameplay scene");
        yield return null;

        // Back to the loadout screen: it opens on the map that was just played.
        SceneManager.LoadScene(Loadout);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Loadout, 15f, "the loadout screen");
        yield return null;
        Assert.IsTrue(Highlighted(MapChoice.ManilaId), "the loadout should open on the map that was just played");
        StringAssert.DoesNotContain("Mabuhay", Briefing("Text"));

        // And picking Mabuhay puts everything of Mabuhay's back: its briefing, and its mission for the scene to read.
        MapButton(MapChoice.MabuhayId).onClick.Invoke();
        yield return null;
        Assert.IsTrue(Highlighted(MapChoice.MabuhayId));
        StringAssert.Contains("Barangay Mabuhay, Carmona", Briefing("Text"));
        Assert.AreSame(MapChoice.Find(MapChoice.MabuhayId).LoadMission(), MissionDefinition.Current);
    }
}
