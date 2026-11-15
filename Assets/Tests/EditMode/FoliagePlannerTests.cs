using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The foliage planted over Barangay Mabuhay: plenty of it, of many kinds, each where it belongs, and never where it
/// would get in the way: not in a house, not on a street (the boat's lanes), not in a clear zone, not under the flood.
/// </summary>
public class FoliagePlannerTests
{
    const string SettingsPath = "Assets/Settings/Maps/BarangayMockSettings_Gameplay.asset";
    const string LibraryPath = "Assets/Settings/Maps/FoliageLibrary.asset";

    static BarangayMockSettings settings;
    static FoliageLibrary flora;
    static BarangayLayout layout;
    static Rect region;
    static MockPlan mock;
    static FoliagePlan plan;
    static readonly KeepClear Clear = new KeepClear(new Vector2(-80f, -18f), 20f);

    [OneTimeSetUp]
    public void PlanTheMap()
    {
        settings = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(SettingsPath);
        flora = AssetDatabase.LoadAssetAtPath<FoliageLibrary>(LibraryPath);
        Assert.IsNotNull(settings, "the Mabuhay map settings");
        Assert.IsNotNull(flora, "the foliage library (Bayanihan > Foliage > Import Pack)");
        layout = settings.Layout;
        settings.library.MeasureAll();
        region = settings.Region(layout);
        mock = BarangayMockPlanner.Plan(layout, region, settings.library);
        plan = FoliagePlanner.Plan(layout, region, mock, settings.library, flora, new[] { Clear });
    }

    [Test]
    public void The_barangay_is_planted_thickly_with_every_kind_of_plant()
    {
        Assert.Greater(plan.placements.Count, 1500, "abundant");
        foreach (var g in new[] { FoliageGroup.CanopyTree, FoliageGroup.SmallTree, FoliageGroup.Bamboo, FoliageGroup.Banana,
                                  FoliageGroup.Bush, FoliageGroup.Vine, FoliageGroup.TallGrass })
            Assert.Greater(plan.Count(g), 20, g + " is planted");
        Assert.GreaterOrEqual(plan.placements.Select(p => p.species).Distinct().Count(), 25, "many species, not a few repeated");
    }

    [Test]
    public void Plants_grow_along_roads_in_yards_on_open_lots_and_creek_banks_and_against_walls()
    {
        foreach (var h in new[] { FoliageHabitat.Roadside, FoliageHabitat.Yard, FoliageHabitat.OpenLot, FoliageHabitat.Bank, FoliageHabitat.Wall })
            Assert.Greater(plan.Count(h), 10, h.ToString());
    }

    [Test]
    public void Nothing_stands_inside_a_building()
    {
        foreach (var p in plan.placements)
        {
            if (p.habitat == FoliageHabitat.Wall) continue;          // a vine curtain hangs on a wall, just outside it
            foreach (var b in layout.buildings)
                if (b.outline.Count >= 3) Assert.IsFalse(MapGeometry.Contains(b.outline, p.position), Name(p) + " is inside building " + b.osmId);
        }
    }

    [Test]
    public void Nothing_stands_on_a_street_the_boat_uses()
    {
        foreach (var p in plan.placements)
            foreach (var r in layout.roads)
            {
                float half = settings.library.roadWidths.For(r.roadClass) * 0.5f;
                float d = MapGeometry.DistanceToPolyline(r.points, p.position, out _);
                Assert.Greater(d, half, Name(p) + " is on " + (string.IsNullOrEmpty(r.name) ? r.osmId : r.name));
            }
    }

    [Test]
    public void Nothing_stands_in_a_clear_zone()
    {
        foreach (var p in plan.placements)
            Assert.Greater(Vector2.Distance(p.position, Clear.centre), Clear.radius - 0.75f, Name(p) + " is in the clear zone");
    }

    [Test]
    public void Nothing_short_enough_to_drown_is_planted_standing()
    {
        foreach (var p in plan.placements)
            Assert.GreaterOrEqual(flora.species[p.species].size.y, flora.minStandingHeight, Name(p) + " would be under the flood");
    }

    [Test]
    public void No_two_plants_grow_out_of_the_same_spot()
    {
        var points = plan.placements.Select(p => p.position).ToList();
        var grid = new Dictionary<Vector2Int, List<Vector2>>();
        foreach (var q in points)
        {
            var k = new Vector2Int(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y));
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (grid.TryGetValue(k + new Vector2Int(dx, dy), out var near))
                    foreach (var o in near) Assert.Greater(Vector2.Distance(o, q), 0.7f, "two plants at " + q);
            if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<Vector2>();
            list.Add(q);
        }
    }

    [Test]
    public void The_same_layout_and_seed_plant_the_same_map()
    {
        var again = FoliagePlanner.Plan(layout, region, mock, settings.library, flora, new[] { Clear });
        Assert.AreEqual(plan.placements.Count, again.placements.Count);
        for (int i = 0; i < plan.placements.Count; i++)
        {
            Assert.AreEqual(plan.placements[i].species, again.placements[i].species);
            Assert.AreEqual(plan.placements[i].position, again.placements[i].position);
        }
    }

    [Test]
    public void Trees_have_a_trunk_the_boat_bumps_into_but_eyes_and_lines_see_past()
    {
        foreach (var s in flora.species.Where(s => s.group == FoliageGroup.CanopyTree || s.group == FoliageGroup.SmallTree))
        {
            var trunk = s.prefab.GetComponentsInChildren<CapsuleCollider>(true).FirstOrDefault();
            Assert.IsNotNull(trunk, s.code + " has a trunk collider");
            Assert.AreEqual(2, trunk.gameObject.layer, s.code + "'s trunk is on Ignore Raycast, so sight lines pass it");
        }
        foreach (var s in flora.species.Where(s => s.group == FoliageGroup.TallGrass || s.group == FoliageGroup.Bush))
            Assert.IsEmpty(s.prefab.GetComponentsInChildren<Collider>(true), s.code + " has nothing solid: the boat goes through grass");
    }

    static string Name(FoliagePlacement p) => flora.species[p.species].code + " at " + p.position;
}
