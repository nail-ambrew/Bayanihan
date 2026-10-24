using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Which map is played: the registry of maps, what picking one does to the mission in force, and the guarantee that never picking
/// leaves the game as it was. MapChoice is static, so every test puts it back.
/// </summary>
public class MapChoiceTests
{
    [SetUp]
    public void Start_from_the_defaults() => MapChoice.Reset();

    [TearDown]
    public void Put_the_defaults_back() => MapChoice.Reset();

    [Test]
    public void Manila_is_first_and_is_what_is_played_if_nothing_is_picked()
    {
        Assert.AreEqual(MapChoice.ManilaId, MapChoice.All[0].id);
        Assert.AreEqual(MapChoice.ManilaId, MapChoice.Selected.id);
        Assert.IsTrue(MapChoice.IsDefault);
        Assert.AreEqual("Gameplay", MapChoice.All[0].sceneName, "the default map is still the Gameplay scene");
    }

    [Test]
    public void Each_map_has_an_id_a_name_a_place_and_a_scene_of_its_own()
    {
        Assert.GreaterOrEqual(MapChoice.All.Count, 2);
        Assert.AreEqual(MapChoice.All.Count, MapChoice.All.Select(m => m.id).Distinct().Count(), "ids are unique");
        Assert.AreEqual(MapChoice.All.Count, MapChoice.All.Select(m => m.sceneName).Distinct().Count(), "each map has a scene of its own");
        foreach (var m in MapChoice.All)
        {
            Assert.IsFalse(string.IsNullOrEmpty(m.title), m.id + " has no title");
            Assert.IsFalse(string.IsNullOrEmpty(m.place), m.id + " has no place");
            Assert.IsFalse(string.IsNullOrEmpty(m.sceneName), m.id + " has no scene");
        }
    }

    [Test]
    public void Every_map_names_a_scene_that_is_in_build_settings()
    {
        // LoadScene by name, and R on the report (which reloads by build index), only work for a scene that is in the build.
        var inBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToList();
        foreach (var m in MapChoice.All)
            Assert.Contains(m.sceneName, inBuild, "scene '" + m.sceneName + "' for the " + m.id + " map is not in Build Settings (File > Build Profiles > Scene List)");
        Assert.Contains("Loadout", inBuild);
    }

    [Test]
    public void Every_map_has_its_mission_asset()
    {
        foreach (var m in MapChoice.All)
        {
            var mission = m.LoadMission();
            Assert.IsNotNull(mission, "the " + m.id + " map has no mission asset");
            Assert.Greater(mission.residents.Count, 0, m.id + " mission has nobody on the roster");
        }
    }

    [Test]
    public void Picking_a_map_makes_its_mission_the_one_in_force_and_picking_Manila_puts_the_shipped_one_back()
    {
        Assert.IsTrue(MapChoice.Select(MapChoice.MabuhayId));
        Assert.AreEqual(MapChoice.MabuhayId, MapChoice.Selected.id);
        Assert.IsFalse(MapChoice.IsDefault);
        Assert.AreSame(MapChoice.Find(MapChoice.MabuhayId).LoadMission(), MissionDefinition.Current);
        StringAssert.Contains("Mabuhay", MissionDefinition.Current.title);

        Assert.IsTrue(MapChoice.Select(MapChoice.ManilaId));
        Assert.IsTrue(MapChoice.IsDefault);
        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current);
    }

    [Test]
    public void Picking_a_map_that_does_not_exist_changes_nothing()
    {
        MapChoice.Select(MapChoice.MabuhayId);
        var mission = MissionDefinition.Current;

        Assert.IsFalse(MapChoice.Select("atlantis"));
        Assert.AreEqual(MapChoice.MabuhayId, MapChoice.Selected.id);
        Assert.AreSame(mission, MissionDefinition.Current);
        Assert.IsNull(MapChoice.Find("atlantis"));
    }

    [Test]
    public void A_scene_is_found_for_its_map_and_a_stranger_is_not()
    {
        Assert.AreEqual(MapChoice.MabuhayId, MapChoice.ForScene(MapChoice.MabuhayScene).id);
        Assert.AreEqual(MapChoice.ManilaId, MapChoice.ForScene("Gameplay").id);
        Assert.IsNull(MapChoice.ForScene("Loadout"));
    }

    [Test]
    public void Reset_goes_back_to_Manila_and_its_mission()
    {
        MapChoice.Select(MapChoice.MabuhayId);
        MapChoice.Reset();
        Assert.IsTrue(MapChoice.IsDefault);
        Assert.AreSame(MissionDefinition.LoadShipped(), MissionDefinition.Current);
    }

    [Test]
    public void The_Mabuhay_mission_is_kept_where_LoadShipped_does_not_look()
    {
        // MissionDefinition.LoadShipped takes the first asset in Resources/Missions, and that has to stay Manila's.
        var mission = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        string path = AssetDatabase.GetAssetPath(mission);
        StringAssert.DoesNotContain("/Missions/", path, "a second mission asset in Resources/Missions would be picked up as the shipped one");
        StringAssert.Contains("Resources/Maps/", path);
        Assert.AreNotSame(MissionDefinition.LoadShipped(), mission);
        StringAssert.DoesNotContain("Mabuhay", MissionDefinition.LoadShipped().title);
    }
}
