using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// The ground of Barangay Mabuhay: no bare patch anywhere nothing is built, every street of the map has its surface, every
/// bridge way its bridge, the street furniture keeps off the mission's streets, the compounds are walled with ways in, and the
/// flood is see-through.
/// </summary>
public class MabuhayGroundTests
{
    Transform map;
    float scale;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.3f);
        map = GameObject.Find("BarangayMock").transform;
        scale = map.localScale.x;
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    static BarangayLayout Layout() => BarangayLayout.LoadShipped();

    /// <summary>The map's region in world metres, from the drawn ground.</summary>
    Rect World()
    {
        // The ground is in chunks (TerrainBuilder): the region is all of them together.
        var all = map.Find(MapGroups.Ground).GetComponentsInChildren<Renderer>();
        var b = all[0].bounds;
        foreach (var r in all) b.Encapsulate(r.bounds);
        return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    [UnityTest]
    public IEnumerator No_bare_ground_where_nothing_is_built()
    {
        var grass = Object.FindFirstObjectByType<GrassField>();
        var undergrowth = Object.FindFirstObjectByType<InstancedFoliage>();
        Assert.IsNotNull(grass, "the map has its low grass");
        Assert.Greater(grass.Count, 10000);
        Assert.IsNotNull(undergrowth);

        // Every point of bare ground (nothing above it but the ground itself) has grass within 6 m.
        var area = World();
        int open = 0, covered = 0;
        var misses = new List<Vector2>();
        for (float x = area.xMin + 20f; x < area.xMax - 20f; x += 12f)
        for (float z = area.yMin + 20f; z < area.yMax - 20f; z += 12f)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 60f, z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0 || hits.Any(h => h.collider.name != "Ground")) continue;     // built on, paved, walled, a tree...
            open++;
            var p = new Vector2(x, z);
            if (grass.AnyWithin(p, 6f) || undergrowth.CountWithin(p, 6f) > 0) covered++;
            else if (misses.Count < 10) misses.Add(p);
        }
        Assert.Greater(open, 500, "there is open ground to check");
        Assert.GreaterOrEqual(covered, open * 0.95f, "bare ground: " + (open - covered) + " of " + open + " open points have nothing growing within 6 m, e.g. " + string.Join(" ", misses));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Every_street_of_the_map_has_its_surface_and_every_bridge_its_bridge()
    {
        var area = World();
        var bridges = map.Find(MapGroups.StreetFurniture + "/Bridges");
        Assert.IsNotNull(bridges, "the bridges are laid");
        int checkedRoads = 0, surfaced = 0, bridgeWays = 0, bridged = 0;
        var missing = new List<string>();
        foreach (var r in Layout().roads)
        {
            if (r.points.Count < 2) continue;
            // The middle of the street's longest segment, in the world.
            int best = 1;
            for (int i = 2; i < r.points.Count; i++)
                if (Vector2.Distance(r.points[i - 1], r.points[i]) > Vector2.Distance(r.points[best - 1], r.points[best])) best = i;
            Vector2 mid = (r.points[best - 1] + r.points[best]) * 0.5f * scale;
            if (!area.Contains(mid) || Vector2.Distance(r.points[best - 1], r.points[best]) < 4f) continue;

            if (r.bridge)
            {
                bridgeWays++;
                if (bridges.Cast<Transform>().Any(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), mid) < 14f)) bridged++;
                continue;
            }
            checkedRoads++;
            var hits = Physics.RaycastAll(new Vector3(mid.x, 60f, mid.y), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Any(h => h.collider.name.StartsWith("road_") || h.collider.transform.parent?.name == "Roads")) surfaced++;
            else if (missing.Count < 8) missing.Add(r.osmId + " " + r.roadClass);
        }
        Assert.Greater(checkedRoads, 100);
        Assert.GreaterOrEqual(surfaced, checkedRoads * 0.97f, "streets with no surface under them: " + string.Join(", ", missing));
        Assert.Greater(bridgeWays, 0, "the map has bridge ways");
        Assert.AreEqual(bridgeWays, bridged, "every bridge way has a bridge");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_streets_read_from_the_boat_and_keep_off_the_mission()
    {
        var furniture = map.Find(MapGroups.StreetFurniture);
        var poles = furniture.Find("Utility Poles");
        // At real size (MabuhayGameplayBuilder.MapScale 1) the streets are half the length they were at 2x, so about half the poles.
        Assert.Greater(poles.childCount, 60, "utility poles along the streets");
        Assert.Greater(furniture.Find("Wires").childCount, 30, "wired one to the next");
        Assert.Greater(furniture.Find("Street Lamps").childCount, 10, "lamps on the main roads");
        foreach (Transform pole in poles)
            Assert.Greater(pole.GetComponentInChildren<Renderer>().bounds.max.y, 5f, pole.name + " stands well above the flood");

        // Nothing stands in the two mission streets' lanes.
        var layout = Layout();
        foreach (var id in new[] { "w668009058", "w145889435" })
        {
            var street = layout.roads.First(r => r.osmId == id);
            var line = street.points.Select(p => p * scale).ToList();
            float half = 3f * scale;
            foreach (Transform holder in furniture)
            {
                if (holder.name == "Crossings" || holder.name == "Bridges") continue;
                foreach (Transform t in holder)
                    Assert.Greater(MapGeometry.DistanceToPolyline(line, new Vector2(t.position.x, t.position.z), out _), half,
                                   t.name + " stands in " + street.name);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Compounds_are_walled_with_ways_in_and_the_boat_still_gets_everywhere()
    {
        var walls = map.Find(MapGroups.CompoundWalls);
        Assert.IsNotNull(walls);
        var names = walls.Cast<Transform>().Select(t => t.name).ToList();
        Assert.GreaterOrEqual(names.Count, 5, "walls round the compounds: " + string.Join(", ", names));
        foreach (var expected in new[] { "Mabuhay Elementary School", "Iglesia Ni Cristo", "WalterMart" })
            Assert.IsTrue(names.Any(n => n.Contains(expected)), expected + " is walled");

        foreach (Transform compound in walls)
        {
            var wall = compound.Find("Wall");
            Assert.IsNotNull(wall, compound.name + " has a wall");
            var r = wall.GetComponent<Renderer>();
            Assert.Greater(r.bounds.max.y, 0.2f, compound.name + "'s wall stands above the flood");
            Assert.IsNotNull(wall.GetComponent<MeshCollider>(), compound.name + "'s wall is solid where it is drawn");
        }

        // Every resident and the hall can still be reached by water from the boat (walls have ways in).
        var boat = Boat();
        var area = World();
        var water = new BoatReach(area, 1f, BoatReach.DefaultRadius, 0f);
        Physics.SyncTransforms();
        water.Flood(boat.transform.position);
        Vector2 hall = new Vector2(Hall().transform.position.x, Hall().transform.position.z);
        Assert.IsTrue(water.Reached(hall) || water.DistanceToReached(hall, 8f) < 8f, "the hall is reached");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_flood_is_murky_but_see_through()
    {
        var water = WaterSurface.Instance.GetComponent<MeshRenderer>().sharedMaterial;
        Assert.AreEqual(1f, water.GetFloat("_Surface"), "transparent");
        Assert.Less(water.GetColor("_BaseColor").a, 0.9f, "you can see a little way down");
        Assert.Greater(water.GetColor("_BaseColor").a, 0.4f, "but it is still murky");
        Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, water.renderQueue);
        yield return null;
    }
}
