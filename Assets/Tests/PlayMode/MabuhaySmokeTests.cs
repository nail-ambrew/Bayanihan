using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// Loads the Barangay Mabuhay mission scene and lets it run, as GameplaySmokeTests does for Manila. Any error or exception logged
/// while it does fails the test: these are the "it broke on load" and "the level cannot be finished" mistakes that are otherwise
/// found by whoever plays it next.
/// </summary>
public class MabuhaySmokeTests
{
    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scene_runs_for_a_few_seconds_without_errors()
    {
        yield return WaitSeconds(3f);

        Assert.IsNotNull(WaterSurface.Instance, "no WaterSurface in the scene");
        Assert.IsNotNull(RescueManager.Instance, "no RescueManager in the scene");
        Assert.AreEqual(10, RescueManager.Instance.TotalSurvivors, "the roster is ten residents");
        Assert.IsNotNull(Object.FindFirstObjectByType<SimpleBoat>(), "no boat");
        Assert.IsNotNull(Object.FindFirstObjectByType<PlayerRescuer>(), "no player");
        Assert.IsNotNull(Object.FindFirstObjectByType<EvacuationCenter>(), "no evacuation centre");
        Assert.IsNotNull(Object.FindFirstObjectByType<FloodCurrent>(), "no current");
        Assert.IsFalse(RescueManager.Instance.GameOver, "the mission ended by itself");
    }

    [UnityTest]
    public IEnumerator It_plays_the_Mabuhay_mission_and_not_the_one_for_Manila()
    {
        yield return WaitSeconds(0.3f);

        var mission = MissionDefinition.Current;
        Assert.AreSame(MapChoice.Find(MapChoice.MabuhayId).LoadMission(), mission, "the scene's mission is the one for this map");
        Assert.AreNotSame(MissionDefinition.LoadShipped(), mission, "and not the one that ships for Manila");
        StringAssert.Contains("Mabuhay", mission.title);
        StringAssert.Contains("Barangay Mabuhay, Carmona", mission.briefing);
        Assert.AreEqual(mission.timeLimit, RescueManager.Instance.timeLimit, "the clock is the mission's");
        Assert.AreEqual(mission.residents.Count, Survivor.All.Count);
    }

    [UnityTest]
    public IEnumerator The_map_is_in_build_settings_so_R_on_the_report_can_reload_it()
    {
        yield return null;
        Assert.GreaterOrEqual(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex, 0,
            "Assets/Scenes/Maps/MabuhayGameplay.unity is not in Build Settings: R on the report would reload scene -1");
        Assert.IsTrue(Application.CanStreamedLevelBeLoaded(RescueManager.Instance.loadoutSceneName), "Esc on the report goes back to the loadout screen");
    }

    [UnityTest]
    public IEnumerator The_boat_floats_at_the_waterline_clear_of_the_ground()
    {
        yield return WaitSeconds(3f);

        var boat = Boat();
        var buoyancy = boat.GetComponent<Buoyancy>();
        float water = WaterSurface.Instance.GetHeight(boat.transform.position);

        Assert.AreEqual(water, boat.transform.position.y, 0.5f, "the boat is not at the water's surface");
        Assert.Greater(buoyancy.Submerged, 0.15f, "the boat is not in the water");
        Assert.Less(buoyancy.Submerged, 0.95f, "the boat is sunk");
        Assert.Greater(boat.Hull.bounds.min.y, -1f + 0.3f, "the hull is resting on the map's ground");
    }

    [UnityTest]
    public IEnumerator The_boat_starts_clear_of_everything_solid()
    {
        yield return new WaitForFixedUpdate();

        var boat = Boat();
        var box = (BoxCollider)boat.Hull;
        var hits = Physics.OverlapBox(box.bounds.center, box.size * 0.5f, boat.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
            Assert.IsTrue(hit.attachedRigidbody != null, "'" + hit.name + "' is touching the boat at its start");
    }

    [UnityTest]
    public IEnumerator Boat_fits_in_the_drop_off_it_has_to_reach()
    {
        yield return new WaitForFixedUpdate();

        var evac = Hall();
        var boat = Boat();

        // The boat unloads once its centre is well inside the zone, so its hull has to fit at the zone's centre.
        Bounds zone = evac.GetComponent<Collider>().bounds;
        Vector3 centre = zone.center;
        centre.y = WaterSurface.Instance.transform.position.y + 0.2f;

        var hullBox = boat.Hull as BoxCollider;
        Vector3 half = hullBox != null ? hullBox.size * 0.5f : new Vector3(1.3f, 0.15f, 2.4f);
        var turned = evac.transform.rotation;                       // the zone is laid along the street, so the boat lies that way

        foreach (var hit in Physics.OverlapBox(centre, half, turned, ~0, QueryTriggerInteraction.Ignore))
        {
            bool floats = hit.attachedRigidbody != null;            // the boat itself, the swimmer, debris
            Assert.IsTrue(floats, "'" + hit.name + "' is in the way: the boat cannot get into the drop-off zone");
        }
    }

    [UnityTest]
    public IEnumerator Radio_has_somebody_left_to_find_at_the_start()
    {
        yield return WaitSeconds(1f);

        int found = Survivor.All.Count(s => s.Discovered);
        Assert.Greater(Survivor.All.Count, 0);
        Assert.Less(found, Survivor.All.Count,
            "every resident already counts as found before the boat has moved, so the Radyo has nobody to point at");
    }

    [UnityTest]
    public IEnumerator Water_is_centred_on_whoever_is_looking()
    {
        yield return WaitSeconds(0.5f);

        var water = WaterSurface.Instance;
        var cam = Camera.main;
        Assert.IsNotNull(cam, "no Main Camera");

        Vector3 offset = water.transform.position - cam.transform.position;
        offset.y = 0f;
        Assert.Less(offset.magnitude, water.size * 0.25f, "the animated water has been left behind by the camera");
    }

    [UnityTest]
    public IEnumerator The_map_is_a_metre_under_the_water_and_has_colliders()
    {
        yield return null;

        var map = GameObject.Find("BarangayMock");
        Assert.IsNotNull(map, "the scene has no map");
        Assert.AreEqual(-1f, map.transform.position.y, 1e-4f, "the ground is a metre under the water, as in the Manila scene");
        Assert.AreEqual(1f, map.transform.localScale.x, 1e-4f, "the map is at real size");

        int boxes = map.GetComponentsInChildren<BoxCollider>().Length;
        int meshes = map.GetComponentsInChildren<MeshCollider>().Length;
        Assert.Greater(boxes, 500, "the houses have colliders");
        Assert.Greater(meshes, 5, "the ground, the roads and the plain blocks have colliders");

        // Nothing on the map has friction: a hull that runs into a wall glances off it instead of being held there.
        foreach (var c in map.GetComponentsInChildren<Collider>().Take(200))
            Assert.IsNotNull(c.sharedMaterial, c.name + " has no surface");
        Assert.AreEqual(0f, map.GetComponentInChildren<BoxCollider>().sharedMaterial.dynamicFriction, 1e-4f);
    }

    [UnityTest]
    public IEnumerator The_flood_runs_away_from_the_hall()
    {
        yield return null;

        var current = Object.FindFirstObjectByType<FloodCurrent>();
        var hall = Hall().transform.position;
        var start = Boat().transform.position;

        // The boat takes everyone upstream, as in Manila: the water runs from the hall towards where the boat starts.
        Vector3 towardsHall = new Vector3(hall.x - start.x, 0f, hall.z - start.z).normalized;
        Assert.Less(Vector3.Dot(current.Heading, towardsHall), -0.7f, "the flood should run away from the hall, not towards it");
    }
}
