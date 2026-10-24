using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// What goes where on the placeholder map: that a house is the one that fits its plot and faces the street, that a
/// long frontage becomes a row, that a landmark takes over the building it stands in, that grounds hold their
/// buildings, that every building ends up as exactly one thing, and that planning twice gives the same plan. The
/// models here are only sizes, so none of it needs a prefab or a scene.
/// </summary>
public class BarangayMockPlannerTests
{
    const string MainRoad = "tertiary road along y = 0";

    // ------------------------------------------------------------------ a small made-up barangay

    static HouseVariant House(string name, HouseType type, int storeys, float width, float height, float depth) =>
        new HouseVariant { name = name, type = type, storeys = storeys, size = new Vector3(width, height, depth) };

    static PlaceholderModel Model(string name, float width, float height, float depth) =>
        new PlaceholderModel { name = name, size = new Vector3(width, height, depth) };

    /// <summary>The sizes of the real placeholder models, without the models: measured from the slum map, hall and school.</summary>
    static PlaceholderLibrary Library()
    {
        var lib = ScriptableObject.CreateInstance<PlaceholderLibrary>();
        lib.houses = new List<HouseVariant>
        {
            House("shop", HouseType.Shop, 1, 4.1f, 3.4f, 5.0f),
            House("two", HouseType.TwoStorey, 2, 3.9f, 5.8f, 5.6f),
            House("three", HouseType.ThreeStorey, 3, 4.5f, 8.4f, 6.3f),
            House("tower", HouseType.Tower, 5, 7.0f, 11.8f, 7.2f)
        };
        lib.landmarks = new List<LandmarkStyle>
        {
            new LandmarkStyle { category = LandmarkCategory.BarangayHall, placement = LandmarkPlacement.Prefab, blockHeight = 10f,
                                model = Model("hall", 25.9f, 13.4f, 15.3f) },
            new LandmarkStyle { category = LandmarkCategory.School, placement = LandmarkPlacement.Parts, blockHeight = 8f,
                                parts = new List<PlaceholderModel> { Model("A", 40.9f, 15.7f, 10.4f), Model("B", 30.8f, 12.5f, 19.8f), Model("C", 18.6f, 8.8f, 14.9f) } },
            new LandmarkStyle { category = LandmarkCategory.Bank, placement = LandmarkPlacement.ShopHouse, blockHeight = 7f },
            new LandmarkStyle { category = LandmarkCategory.Court, placement = LandmarkPlacement.Court },
            new LandmarkStyle { category = LandmarkCategory.Market, placement = LandmarkPlacement.Block, blockHeight = 10f }
        };
        return lib;
    }

    static MapRoad Road(string id, RoadClass roadClass, float x0, float y0, float x1, float y1) =>
        new MapRoad { osmId = id, roadClass = roadClass, points = new List<Vector2> { new Vector2(x0, y0), new Vector2(x1, y1) } };

    /// <summary>A tertiary road along y = 0 and a residential street along y = 100, each 400 m long.</summary>
    static BarangayLayout Layout()
    {
        var layout = ScriptableObject.CreateInstance<BarangayLayout>();
        layout.roads = new List<MapRoad>
        {
            Road("main", RoadClass.Tertiary, -200f, 0f, 200f, 0f),
            Road("side", RoadClass.Residential, -200f, 100f, 200f, 100f)
        };
        return layout;
    }

    static List<Vector2> Rectangle(float cx, float cy, float w, float h) => new List<Vector2>
    {
        new Vector2(cx - w / 2, cy - h / 2), new Vector2(cx + w / 2, cy - h / 2), new Vector2(cx + w / 2, cy + h / 2), new Vector2(cx - w / 2, cy + h / 2)
    };

    static MapBuilding Plot(string id, float cx, float cy, float w, float h, BuildingKind kind = BuildingKind.House) =>
        new MapBuilding { osmId = id, kind = kind, outline = Rectangle(cx, cy, w, h) };

    static MapLandmark Landmark(string id, string name, LandmarkCategory category, float x, float y, float w = 0f, float h = 0f) =>
        new MapLandmark
        {
            id = id, name = name, category = category, positionKnown = true, position = new Vector2(x, y),
            footprint = w > 0f ? Rectangle(x, y, w, h) : new List<Vector2>()
        };

    static readonly Rect Everywhere = new Rect(-500f, -500f, 1000f, 1000f);

    static MockPlan Run(BarangayLayout layout, PlaceholderLibrary lib = null) =>
        BarangayMockPlanner.Plan(layout, Everywhere, lib ?? Library());

    static IEnumerable<MockPlacement> Of(MockPlan plan, string id) => plan.placements.Where(p => p.sourceId == id);

    // ------------------------------------------------------------------ houses

    [Test]
    public void A_plot_that_suits_a_two_storey_house_gets_one_facing_the_street()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("w1", 0f, 110f, 4f, 5.6f));       // beside the residential street at y = 100
        var plan = Run(layout);

        var house = Of(plan, "w1").Single();
        Assert.AreEqual(PlacementKind.House, house.kind);
        Assert.AreEqual("two", house.modelName);
        Assert.AreEqual("two", plan.fates["w1"]);
        Assert.AreEqual(0f, house.position.x, 0.01f);
        Assert.AreEqual(110f, house.position.y, 0.01f);

        Vector3 front = Quaternion.Euler(0f, house.yaw, 0f) * Vector3.forward;
        Assert.AreEqual(0f, front.x, 0.01f, "the front of the house points at the street");
        Assert.AreEqual(-1f, front.z, 0.01f, "the street is to the south");
        Assert.AreEqual(4f / 3.9f, house.scale.x, 0.01f, "stretched to the plot's frontage");
        Assert.AreEqual(1f, house.scale.z, 0.01f);
    }

    [Test]
    public void A_house_turns_to_whichever_street_is_nearest()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("south", 0f, 90f, 4f, 5.6f));     // 10 m south of the street at y = 100... and the road at 0 is further
        layout.buildings.Add(Plot("north", 0f, 110f, 4f, 5.6f));
        var plan = Run(layout);
        Assert.AreEqual(1f, (Quaternion.Euler(0f, Of(plan, "south").Single().yaw, 0f) * Vector3.forward).z, 0.01f, "faces north to the street");
        Assert.AreEqual(-1f, (Quaternion.Euler(0f, Of(plan, "north").Single().yaw, 0f) * Vector3.forward).z, 0.01f, "faces south to the street");
    }

    [Test]
    public void Shops_are_for_main_roads_and_houses_for_side_streets()
    {
        var layout = Layout();
        for (int i = 0; i < 20; i++)
        {
            layout.buildings.Add(Plot("m" + i, -190f + i * 20f, 10f, 4.1f, 5f));       // 20 plots beside the tertiary road
            layout.buildings.Add(Plot("s" + i, -190f + i * 20f, 110f, 4.1f, 5f));      // 20 identical plots beside the residential street
        }
        var plan = Run(layout);
        int onMain = plan.placements.Count(p => p.kind == PlacementKind.House && p.sourceId[0] == 'm' && p.modelName == "shop");
        int onSide = plan.placements.Count(p => p.kind == PlacementKind.House && p.sourceId[0] == 's' && p.modelName == "shop");
        Assert.GreaterOrEqual(onMain, 15, "shops on the main road, of 20 plots");
        Assert.AreEqual(0, onSide, "a shop on a side street");
    }

    [Test]
    public void A_long_frontage_becomes_a_row_of_houses_side_by_side()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("row", 0f, 110f, 20f, 5.6f));
        var plan = Run(layout);

        var houses = Of(plan, "row").ToList();
        Assert.AreEqual(4, houses.Count, "twenty metres at 4.5 m a house");
        Assert.AreEqual("4 houses in a row", plan.fates["row"]);
        CollectionAssert.AreEqual(new[] { -7.5f, -2.5f, 2.5f, 7.5f }, houses.Select(h => Mathf.Round(h.position.x * 100f) / 100f).OrderBy(x => x).ToArray());
        foreach (var h in houses)
        {
            Assert.AreEqual(110f, h.position.y, 0.01f);
            Assert.AreEqual(PlacementKind.House, h.kind);
        }
    }

    [Test]
    public void The_plan_never_stretches_a_house_past_the_limits()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("wide", 0f, 110f, 7f, 5.6f));
        layout.buildings.Add(Plot("deep", 60f, 110f, 4f, 30f));
        var lib = Library();
        var plan = Run(layout, lib);
        foreach (var p in plan.placements.Where(p => p.kind == PlacementKind.House))
        {
            Assert.That(p.scale.x, Is.InRange(lib.minStretch - 1e-4f, lib.maxStretch + 1e-4f), p.sourceId + " width scale");
            Assert.That(p.scale.z, Is.InRange(lib.minStretch - 1e-4f, lib.maxStretch + 1e-4f), p.sourceId + " depth scale");
            Assert.That(p.scale.y, Is.InRange(0.9f - 1e-4f, 1.25f + 1e-4f), p.sourceId + " height scale");
        }
    }

    // ------------------------------------------------------------------ everything else

    [Test]
    public void Big_buildings_and_factories_become_blocks_and_tiny_ones_become_sheds()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("factory", 0f, 50f, 15f, 20f, BuildingKind.Industrial));
        layout.buildings.Add(Plot("bighouse", 60f, 50f, 20f, 20f));
        layout.buildings.Add(Plot("shed", 120f, 50f, 3f, 3f));
        layout.buildings.Add(Plot("roof", 150f, 50f, 6f, 6f, BuildingKind.Roof));
        var plan = Run(layout);

        var factory = Of(plan, "factory").Single();
        Assert.AreEqual(PlacementKind.Block, factory.kind);
        Assert.AreEqual(BarangayMockPlanner.BlockColour(BuildingKind.Industrial), factory.colour);
        Assert.AreEqual(9f, factory.height, 0.5f);
        Assert.AreEqual(PlacementKind.Block, Of(plan, "bighouse").Single().kind, "a house over 300 m2 is not a house");
        Assert.AreEqual(PlacementKind.Shed, Of(plan, "shed").Single().kind);
        Assert.AreEqual(PlacementKind.Canopy, Of(plan, "roof").Single().kind);
        Assert.AreEqual(4f, Of(plan, "roof").Single().height);
    }

    [Test]
    public void A_building_with_known_storeys_is_that_tall()
    {
        var layout = Layout();
        var b = Plot("tall", 0f, 50f, 15f, 20f, BuildingKind.Commercial);
        b.levels = 4;
        layout.buildings.Add(b);
        Assert.AreEqual(4 * 3.2f + 0.4f, Of(Run(layout), "tall").Single().height, 1e-3f);
    }

    [Test]
    public void Without_any_houses_in_the_library_every_house_is_a_block_and_the_plan_says_so()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("h", 0f, 110f, 4f, 5.6f));
        var lib = Library();
        lib.houses.Clear();
        var plan = Run(layout, lib);
        Assert.AreEqual(PlacementKind.Block, Of(plan, "h").Single().kind);
        Assert.IsTrue(plan.notes.Any(n => n.Contains("no houses")));
    }

    // ------------------------------------------------------------------ landmarks

    [Test]
    public void The_hall_gets_the_hall_model_when_it_fits_and_a_block_when_it_does_not()
    {
        var layout = Layout();
        layout.landmarks.Add(Landmark("hall", "Mabuhay Barangay Hall", LandmarkCategory.BarangayHall, 0f, 20f, 18f, 11.6f));
        layout.landmarks.Add(Landmark("hall2", "Barangay 3 Hall", LandmarkCategory.BarangayHall, 100f, 20f, 12f, 6f));
        var plan = Run(layout);

        var big = Of(plan, "hall").Single(p => p.kind == PlacementKind.Model);
        Assert.AreEqual("hall", big.modelName);
        Assert.AreEqual(18f / 25.9f, big.scale.x, 0.01f);
        Assert.AreEqual(11.6f / 15.3f, big.scale.z, 0.01f);

        var small = Of(plan, "hall2").Single(p => p.kind != PlacementKind.Sign);
        Assert.AreEqual(PlacementKind.Block, small.kind, "12 x 6 m is too small to scale the hall down to");
        Assert.AreEqual(10f, small.height);
    }

    [Test]
    public void A_school_site_is_grounds_with_its_buildings_as_school_blocks()
    {
        var layout = Layout();
        layout.landmarks.Add(Landmark("school", "Mabuhay Elementary School", LandmarkCategory.School, 0f, -60f, 64f, 77f));
        for (int i = 0; i < 5; i++) layout.buildings.Add(Plot("class" + i, -20f + i * 10f, -60f, 8f, 9f));
        layout.buildings.Add(Plot("elsewhere", 0f, 110f, 4f, 5.6f));
        var plan = Run(layout);

        Assert.AreEqual(1, Of(plan, "school").Count(p => p.kind == PlacementKind.Grounds), "one slab for the whole site");
        var blocks = plan.placements.Where(p => p.kind == PlacementKind.Model).ToList();
        Assert.AreEqual(5, blocks.Count);
        foreach (var b in blocks) Assert.AreEqual("C", b.modelName, "the compact block is the one that fits a small building");
        for (int i = 0; i < 5; i++) StringAssert.StartsWith("Mabuhay Elementary School", plan.fates["class" + i]);
        Assert.AreEqual("two", plan.fates["elsewhere"], "a house outside the grounds is still a house");
    }

    [Test]
    public void A_landmark_site_holding_one_big_building_is_grounds_with_a_block_in_it()
    {
        var layout = Layout();
        layout.landmarks.Add(Landmark("mall", "WalterMart", LandmarkCategory.Market, 0f, -60f, 200f, 180f));
        layout.buildings.Add(Plot("warehouse", 0f, -60f, 100f, 75f, BuildingKind.Warehouse));
        var plan = Run(layout);
        Assert.AreEqual(1, Of(plan, "mall").Count(p => p.kind == PlacementKind.Grounds));
        Assert.AreEqual(PlacementKind.Block, Of(plan, "warehouse").Single().kind);
        StringAssert.StartsWith("WalterMart", plan.fates["warehouse"]);
    }

    [Test]
    public void A_landmark_that_fills_its_own_footprint_replaces_the_buildings_inside_it()
    {
        var layout = Layout();
        // The building (28 x 28) fills most of the landmark's 30 x 30 outline, so the outline is the building, not grounds.
        layout.landmarks.Add(Landmark("clinic", "Clinic", LandmarkCategory.Market, 0f, 50f, 30f, 30f));
        layout.buildings.Add(Plot("inside", 0f, 50f, 28f, 28f));
        var plan = Run(layout);
        Assert.AreEqual(0, Of(plan, "inside").Count(), "the building is drawn by the landmark, not twice");
        StringAssert.StartsWith("replaced by", plan.fates["inside"]);
    }

    [Test]
    public void A_shop_in_a_small_building_turns_it_into_a_shop_house()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("unit", 0f, 10f, 6f, 7f));
        layout.landmarks.Add(Landmark("bank", "BDO", LandmarkCategory.Bank, 0f, 10f));
        var plan = Run(layout);

        var shop = Of(plan, "bank").Single(p => p.kind == PlacementKind.Model);
        Assert.AreEqual("shop", shop.modelName);
        Assert.IsFalse(plan.placements.Any(p => p.sourceId == "unit"), "the building is the shop, not a house as well");
        StringAssert.StartsWith("landmark: BDO", plan.fates["unit"]);
        var sign = Of(plan, "bank").Single(p => p.kind == PlacementKind.Sign);
        Assert.AreEqual("BDO", sign.label);
        Assert.Greater(sign.height, 3.4f, "the sign is above the roof");
    }

    [Test]
    public void A_shop_on_the_pavement_still_finds_the_building_behind_it()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("unit", 0f, 10f, 6f, 7f));
        layout.landmarks.Add(Landmark("bank", "BDO", LandmarkCategory.Bank, 0f, 5f));       // 1.5 m south of the building's edge
        var plan = Run(layout);
        StringAssert.StartsWith("landmark", plan.fates["unit"]);
    }

    [Test]
    public void Two_shops_in_one_big_building_share_one_block_and_stack_their_signs()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("mall", 0f, 50f, 30f, 40f, BuildingKind.Commercial));
        layout.landmarks.Add(Landmark("a", "Mercury Drug", LandmarkCategory.Bank, -5f, 50f));
        layout.landmarks.Add(Landmark("b", "BDO", LandmarkCategory.Bank, 5f, 50f));
        var plan = Run(layout);

        Assert.AreEqual(1, plan.placements.Count(p => p.kind == PlacementKind.Block), "one block for the building");
        var signs = plan.placements.Where(p => p.kind == PlacementKind.Sign).OrderBy(p => p.label).ToList();
        Assert.AreEqual(2, signs.Count);
        Assert.AreNotEqual(signs[0].height, signs[1].height, "the signs would be on top of each other");
        Assert.IsTrue(plan.notes.Any(n => n.Contains("same building")));
    }

    [Test]
    public void A_shop_with_no_building_stands_alone_and_clears_the_houses_under_it()
    {
        var layout = Layout();
        layout.buildings.Add(Plot("under", 50f, 60f, 1.5f, 1.5f));      // a scrap of a building: too small to be a shop's home
        layout.landmarks.Add(Landmark("bank", "BDO", LandmarkCategory.Bank, 50f, 60f));
        var plan = Run(layout);

        var shop = Of(plan, "bank").Single(p => p.kind == PlacementKind.Model);
        Assert.AreEqual("shop", shop.modelName);
        Assert.AreEqual(50f, shop.position.x, 0.01f);
        Assert.AreEqual(60f, shop.position.y, 0.01f);
        Assert.AreEqual(1, plan.fates.Count);
        StringAssert.StartsWith("replaced by", plan.fates["under"]);
        Assert.IsTrue(plan.notes.Any(n => n.Contains("no building")));
    }

    [Test]
    public void A_court_is_a_slab_with_an_open_roof()
    {
        var layout = Layout();
        layout.landmarks.Add(Landmark("court", "Court", LandmarkCategory.Court, 0f, 30f, 28f, 15f));
        var court = Of(Run(layout), "court").Single(p => p.kind == PlacementKind.Court);
        Assert.AreEqual(4, court.outline.Count);
        Assert.Greater(court.height, 4f);
    }

    [Test]
    public void An_evacuation_site_gets_a_banner_that_says_whether_it_is_confirmed()
    {
        var layout = Layout();
        var candidate = Landmark("a", "Hall A", LandmarkCategory.Court, 0f, 30f, 28f, 15f);
        candidate.evacuationRole = EvacuationRole.Candidate;
        var confirmed = Landmark("b", "Hall B", LandmarkCategory.Court, 100f, 30f, 28f, 15f);
        confirmed.evacuationRole = EvacuationRole.Designated;
        confirmed.verified = true;
        layout.landmarks.Add(candidate);
        layout.landmarks.Add(confirmed);
        layout.landmarks.Add(Landmark("c", "Not a shelter", LandmarkCategory.Court, 200f, 30f, 28f, 15f));
        var plan = Run(layout);

        var a = Of(plan, "a").Single(p => p.kind == PlacementKind.Banner);
        var b = Of(plan, "b").Single(p => p.kind == PlacementKind.Banner);
        StringAssert.Contains("unconfirmed", a.label);
        StringAssert.DoesNotContain("unconfirmed", b.label);
        Assert.AreNotEqual(a.colour, b.colour, "orange for a candidate, green once confirmed");
        Assert.AreEqual(0, Of(plan, "c").Count(p => p.kind == PlacementKind.Banner));
    }

    [Test]
    public void A_landmark_nobody_has_located_is_noted_and_left_off_the_map()
    {
        var layout = Layout();
        layout.landmarks.Add(new MapLandmark { id = "x-pawn", name = "M Lhuillier pawnshop", category = LandmarkCategory.Pawnshop, positionKnown = false });
        var plan = Run(layout);
        Assert.AreEqual(0, plan.placements.Count);
        Assert.IsTrue(plan.notes.Any(n => n.Contains("M Lhuillier") && n.Contains("no position")));
    }

    // ------------------------------------------------------------------ the whole plan

    [Test]
    public void Every_building_in_the_region_has_exactly_one_fate_and_none_outside_it_is_planned()
    {
        var layout = Layout();
        var kinds = new[] { BuildingKind.House, BuildingKind.Industrial, BuildingKind.Roof, BuildingKind.Generic, BuildingKind.Retail };
        for (int i = 0; i < 100; i++)
            layout.buildings.Add(Plot("b" + i, -190f + (i % 20) * 19f, 10f + (i / 20) * 25f, 3f + (i % 7) * 2.5f, 3f + (i % 5) * 3f, kinds[i % kinds.Length]));
        layout.buildings.Add(Plot("far", 450f, 10f, 5f, 5f));
        layout.landmarks.Add(Landmark("bank", "BDO", LandmarkCategory.Bank, -190f, 10f));

        var plan = BarangayMockPlanner.Plan(layout, new Rect(-250f, -50f, 500f, 400f), Library());
        Assert.AreEqual(100, plan.fates.Count, "one fate for each building in the region");
        Assert.IsFalse(plan.fates.ContainsKey("far"), "a building outside the region");
        foreach (var b in layout.buildings.Where(b => b.osmId != "far"))
            Assert.IsTrue(plan.fates.ContainsKey(b.osmId), b.osmId + " has no fate");
    }

    static string Dump(MockPlan plan) => string.Join("\n", plan.placements.Select(p => string.Join("|", new object[]
    {
        p.kind, p.sourceId, p.modelName, p.position.ToString("F3"), p.yaw.ToString("F2"), p.scale.ToString("F3"),
        p.height.ToString("F2"), p.label, p.outline == null ? 0 : p.outline.Count
    })));

    [Test]
    public void Planning_twice_gives_the_same_plan_whatever_order_the_buildings_are_listed_in()
    {
        var layout = Layout();
        for (int i = 0; i < 60; i++)
            layout.buildings.Add(Plot("b" + i, -190f + (i % 15) * 25f, 10f + (i / 15) * 30f, 4f + (i % 6) * 1.5f, 5f + (i % 4) * 2f));
        layout.landmarks.Add(Landmark("hall", "Hall", LandmarkCategory.BarangayHall, 0f, 200f, 18f, 11.6f));
        var first = Dump(Run(layout));
        Assert.AreEqual(first, Dump(Run(layout)), "the same input");

        layout.buildings.Reverse();
        Assert.AreEqual(first, Dump(Run(layout)), "the buildings listed the other way round");
    }

    // ------------------------------------------------------------------ the layout that ships

    BarangayLayout Shipped()
    {
        var layout = BarangayLayout.LoadShipped();
        Assert.IsNotNull(layout, "there is no layout in Resources/Layouts. Run Bayanihan > Rebuild Barangay Layout.");
        return layout;
    }

    [Test]
    public void Every_building_in_the_shipped_focus_area_has_exactly_one_fate()
    {
        var layout = Shipped();
        var plan = BarangayMockPlanner.Plan(layout, layout.focusArea, Library());
        var ids = new HashSet<string>(layout.BuildingsIn(layout.focusArea).Select(b => b.osmId));
        Assert.Greater(ids.Count, 700);
        CollectionAssert.AreEquivalent(ids, plan.fates.Keys, "every building, and nothing else, is accounted for");
        foreach (var p in plan.placements.Where(p => p.kind == PlacementKind.House))
            Assert.IsTrue(ids.Contains(p.sourceId), p.sourceId + " is not in the focus area");
    }

    [Test]
    public void The_shipped_hall_is_fitted_and_the_small_hall_next_door_is_a_block()
    {
        var layout = Shipped();
        var plan = BarangayMockPlanner.Plan(layout, layout.focusArea, Library());
        Assert.IsTrue(plan.placements.Any(p => p.kind == PlacementKind.Model && p.sourceId == "w620109194" && p.modelName == "hall"),
                      "Mabuhay Barangay Hall should get the hall model");
        Assert.IsTrue(plan.placements.Any(p => p.kind == PlacementKind.Block && p.sourceId == "w1198902261"),
                      "Barangay 3 Hall is too small for it and should be a block");
    }

    [Test]
    public void The_shipped_school_and_the_shipped_mall_are_grounds()
    {
        var layout = Shipped();
        var plan = BarangayMockPlanner.Plan(layout, layout.focusArea, Library());
        Assert.IsTrue(plan.placements.Any(p => p.kind == PlacementKind.Grounds && p.sourceId == "w984143589"), "Mabuhay Elementary School");
        Assert.IsTrue(plan.placements.Any(p => p.kind == PlacementKind.Grounds && p.sourceId == "r10749166"), "WalterMart");

        // Every building whose middle is inside the school's outline belongs to the school, and nothing else does.
        var school = layout.Find("w984143589");
        int inGrounds = layout.BuildingsIn(layout.focusArea).Count(b => MapGeometry.Contains(school.footprint, MapGeometry.Centroid(b.outline)));
        Assert.Greater(inGrounds, 3, "the school has several buildings");
        Assert.AreEqual(inGrounds, plan.fates.Count(f => f.Value.StartsWith("Mabuhay Elementary School")), "the buildings in the school grounds");
    }

    [Test]
    public void The_shipped_plan_has_one_sign_for_each_landmark_in_the_region_and_a_banner_for_each_site()
    {
        var layout = Shipped();
        var plan = BarangayMockPlanner.Plan(layout, layout.focusArea, Library());
        int landmarks = layout.landmarks.Count(l => l.positionKnown && layout.focusArea.Contains(l.position));
        Assert.AreEqual(landmarks, plan.Count(PlacementKind.Sign));
        int sites = layout.EvacuationSites.Count(l => l.positionKnown && layout.focusArea.Contains(l.position));
        Assert.AreEqual(sites, plan.Count(PlacementKind.Banner));
        Assert.Greater(sites, 1);
    }

    [Test]
    public void Planning_the_shipped_focus_area_twice_gives_the_same_plan()
    {
        var layout = Shipped();
        var lib = Library();
        Assert.AreEqual(Dump(BarangayMockPlanner.Plan(layout, layout.focusArea, lib)), Dump(BarangayMockPlanner.Plan(layout, layout.focusArea, lib)));
    }
}
