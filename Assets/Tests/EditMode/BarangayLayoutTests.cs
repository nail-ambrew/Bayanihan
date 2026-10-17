using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The barangay layout: that real places turn into metres and game space the right way round and at the right size;
/// and that the layout that ships is Barangay Mabuhay as it really is - the right shape and size, the hall where the
/// hall is, the main roads on it, the places that are in Mabuhay inside its boundary and the neighbours outside, and
/// every evacuation site saying where the claim comes from, never confirmed without being checked.
/// </summary>
public class BarangayLayoutTests
{
    // Places the tests know for certain (OpenStreetMap ids).
    const string Hall = "w620109194";                 // Mabuhay Barangay Hall, the origin
    const string MabuhayElementary = "w984143589";
    const string Hospital = "r10749165";              // Carmona Hospital and Medical Center
    const string CityHall = "w145889365";             // Carmona City Hall, in the Poblacion to the east
    const string FireStation = "n10861505930";        // Carmona Fire Station, in the Poblacion to the east

    static BarangayLayout At(double latitude, double longitude, float scale = 0.15f)
    {
        var layout = ScriptableObject.CreateInstance<BarangayLayout>();
        layout.originLatitude = latitude;
        layout.originLongitude = longitude;
        layout.scale = scale;
        return layout;
    }

    // ------------------------------------------------------------------ real places to metres to the game

    [Test]
    public void One_degree_is_about_110_6_km_north_and_107_9_km_east_here()
    {
        BarangayLayout.MetresPerDegree(14.3116, out double east, out double north);
        Assert.That(north, Is.EqualTo(110642).Within(50), "metres in a degree of latitude at Carmona");
        Assert.That(east, Is.EqualTo(107888).Within(50), "metres in a degree of longitude at Carmona");
    }

    [Test]
    public void Latitude_and_longitude_turn_into_metres_from_the_origin_and_back()
    {
        var layout = At(14.3116367, 121.0499152);
        Assert.That(layout.ToLocalMetres(14.3116367, 121.0499152).magnitude, Is.LessThan(0.001f), "the origin is (0, 0)");

        Vector2 north = layout.ToLocalMetres(14.3126367, 121.0499152);
        Assert.That(north.x, Is.EqualTo(0f).Within(0.01f));
        Assert.That(north.y, Is.EqualTo(110.64f).Within(0.05f), "a thousandth of a degree north is about 110.6 m");

        Vector2 east = layout.ToLocalMetres(14.3116367, 121.0509152);
        Assert.That(east.x, Is.EqualTo(107.89f).Within(0.05f), "a thousandth of a degree east is about 107.9 m");

        layout.ToLatLon(new Vector2(-1234.5f, 678.9f), out double lat, out double lon);
        Vector2 back = layout.ToLocalMetres(lat, lon);
        Assert.That(back.x, Is.EqualTo(-1234.5f).Within(0.01f));
        Assert.That(back.y, Is.EqualTo(678.9f).Within(0.01f));
    }

    [Test]
    public void In_the_game_east_is_plus_x_and_north_is_plus_z_and_the_scale_applies()
    {
        var layout = At(14.31, 121.05, scale: 0.5f);
        Vector3 world = layout.ToWorld(new Vector2(100f, 40f), 2f);
        Assert.AreEqual(new Vector3(50f, 2f, 20f), world);
        Assert.AreEqual(new Vector2(100f, 40f), layout.ToMetres(world));
        Assert.AreEqual(new Vector3(100f, 0f, 40f), BarangayLayout.ToWorld(new Vector2(100f, 40f), 0f, 1f), "full size");
    }

    [Test]
    public void The_boundary_test_handles_a_concave_outline()
    {
        // An L: the notch at the top right is outside.
        var rings = new List<MapRing>
        {
            new MapRing { points = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 4),
                                                       new Vector2(4, 4), new Vector2(4, 10), new Vector2(0, 10) } }
        };
        Assert.IsTrue(BarangayLayout.Contains(rings, new Vector2(2, 2)));
        Assert.IsTrue(BarangayLayout.Contains(rings, new Vector2(8, 2)));
        Assert.IsTrue(BarangayLayout.Contains(rings, new Vector2(2, 8)));
        Assert.IsFalse(BarangayLayout.Contains(rings, new Vector2(8, 8)), "the notch");
        Assert.IsFalse(BarangayLayout.Contains(rings, new Vector2(-1, 5)));
        Assert.IsFalse(BarangayLayout.Contains(null, Vector2.zero));
    }

    [Test]
    public void A_place_without_a_name_is_called_by_what_it_is()
    {
        Assert.AreEqual("Water supply", new MapLandmark { category = LandmarkCategory.WaterSupply }.Label);
        Assert.AreEqual("Court", new MapLandmark { category = LandmarkCategory.Court }.Label);
        Assert.AreEqual("Mabuhay Barangay Hall", new MapLandmark { name = "Mabuhay Barangay Hall" }.Label);
    }

    // ------------------------------------------------------------------ the layout that ships

    BarangayLayout Shipped()
    {
        var layout = BarangayLayout.LoadShipped();
        Assert.IsNotNull(layout, "there is no layout in Resources/Layouts. Run Bayanihan > Rebuild Barangay Layout.");
        return layout;
    }

    MapLandmark Place(BarangayLayout layout, string id)
    {
        var l = layout.Find(id);
        Assert.IsNotNull(l, id + " is not on the map");
        Assert.IsTrue(l.positionKnown, l.Label + " has no position");
        return l;
    }

    [Test]
    public void The_shipped_layout_is_Barangay_Mabuhay_in_the_City_of_Carmona()
    {
        var layout = Shipped();
        Assert.AreEqual("Mabuhay", layout.barangay);
        StringAssert.Contains("Carmona", layout.city);
        Assert.AreEqual("Cavite", layout.province);
        Assert.AreEqual("0402104009", layout.psgc, "the PSGC code of Barangay Mabuhay");
        Assert.AreEqual(12714, layout.population, "PSA 2020 census");
    }

    [Test]
    public void The_boundary_is_about_4_3_by_2_4_km_and_shrinks_to_a_level()
    {
        var layout = Shipped();
        Assert.Greater(layout.boundary.Count, 0, "no boundary");
        Assert.Greater(layout.boundary[0].points.Count, 50, "a boundary that coarse is not the real one");
        Rect b = layout.Bounds;
        Assert.That(b.width, Is.InRange(4000f, 4500f), "east to west, in real metres");
        Assert.That(b.height, Is.InRange(2200f, 2600f), "north to south, in real metres");
        Assert.That(b.width * layout.scale, Is.InRange(500f, 750f),
                    "the shipped scale (0.15) puts the barangay in about 640 m; if you changed it on purpose, change this");
    }

    [Test]
    public void The_barangay_hall_is_the_origin_and_is_inside()
    {
        var layout = Shipped();
        var hall = Place(layout, Hall);
        Assert.AreEqual(LandmarkCategory.BarangayHall, hall.category);
        Assert.That(hall.position.magnitude, Is.LessThan(1f), "the hall is (0, 0)");
        Assert.IsTrue(hall.insideBarangay);
        Assert.IsTrue(layout.Contains(Vector2.zero));
        StringAssert.Contains("Barangay Hall", layout.originName);
    }

    [Test]
    public void Places_in_Mabuhay_are_inside_and_the_Poblacion_is_outside_to_the_east()
    {
        var layout = Shipped();
        foreach (var id in new[] { Hall, MabuhayElementary, Hospital })
        {
            var l = Place(layout, id);
            Assert.IsTrue(l.insideBarangay, l.Label + " is in Mabuhay but the map puts it outside");
            Assert.IsTrue(layout.Contains(l.position));
        }
        foreach (var id in new[] { CityHall, FireStation })
        {
            var l = Place(layout, id);
            Assert.IsFalse(l.insideBarangay, l.Label + " is in the Poblacion but the map puts it inside Mabuhay");
            Assert.Greater(l.position.x, layout.Bounds.xMax, l.Label + " should be east of the barangay");
        }
    }

    [Test]
    public void The_hall_is_about_145_m_from_Mabuhay_Elementary_School()
    {
        var layout = Shipped();
        Vector2 hall = Place(layout, Hall).position, school = Place(layout, MabuhayElementary).position;
        float real = Vector2.Distance(hall, school);
        Assert.That(real, Is.EqualTo(145f).Within(5f), "real metres between the two buildings' centres");
        Assert.That(school.y, Is.LessThan(hall.y), "the school is south of the hall");
        float game = Vector3.Distance(layout.ToWorld(hall), layout.ToWorld(school));
        Assert.That(game, Is.EqualTo(real * layout.scale).Within(0.01f), "and the game shrinks it by the scale");
    }

    [Test]
    public void The_main_roads_and_the_river_are_on_the_map()
    {
        var layout = Shipped();
        Assert.IsTrue(layout.RoadsNamed("Governor's Drive").Any(r => r.roadClass == RoadClass.Trunk), "Governor's Drive, the national highway");
        foreach (var name in new[] { "Sugar Road", "Loyola", "San Lazaro" })
            Assert.IsTrue(layout.RoadsNamed(name).Any(), name + " is not on the map");
        Assert.Greater(layout.roads.Count(r => r.roadClass == RoadClass.Residential), 100, "the subdivision streets are missing");
        Assert.IsTrue(layout.waterways.Any(w => w.kind == WaterwayKind.River && w.name == "Carmona River"), "the Carmona River is missing");
        Assert.IsTrue(layout.roads.All(r => r.points.Count >= 2), "a road needs at least two points");
    }

    [Test]
    public void Ids_are_unique()
    {
        var layout = Shipped();
        Assert.AreEqual(layout.landmarks.Count, layout.landmarks.Select(l => l.id).Distinct().Count(), "landmark ids");
        Assert.AreEqual(layout.roads.Count, layout.roads.Select(r => r.osmId).Distinct().Count(), "road ids");
        Assert.AreEqual(layout.waterways.Count, layout.waterways.Select(w => w.osmId).Distinct().Count(), "waterway ids");
    }

    [Test]
    public void Every_evacuation_site_says_where_the_claim_comes_from()
    {
        var sites = Shipped().EvacuationSites.ToList();
        Assert.GreaterOrEqual(sites.Count, 3, "too few evacuation sites to plan a rescue around");
        foreach (var s in sites)
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.source), s.Label + " is an evacuation site with no source");
    }

    [Test]
    public void Nothing_is_designated_unless_it_has_been_verified()
    {
        foreach (var s in Shipped().EvacuationSites)
        {
            if (s.evacuationRole == EvacuationRole.Designated)
                Assert.IsTrue(s.verified, s.Label + " is designated but nobody has confirmed it");
            if (!s.verified)
                Assert.AreEqual(EvacuationRole.Candidate, s.evacuationRole, s.Label + " is unverified, so it can only be a candidate");
        }
    }

    [Test]
    public void At_least_one_evacuation_site_is_in_Mabuhay_and_on_the_map()
    {
        var layout = Shipped();
        Assert.IsTrue(layout.EvacuationSites.Any(s => s.positionKnown && s.insideBarangay), "nowhere in Mabuhay to take people");
        Assert.IsTrue(layout.EvacuationSites.Any(s => s.category == LandmarkCategory.School), "no school among the sites");
    }

    [Test]
    public void Nothing_strays_far_from_the_barangay()
    {
        // Mabuhay runs about 3.9 km west and 2 km south of the hall; the Poblacion is up to about 1.7 km east.
        var layout = Shipped();
        var area = Rect.MinMaxRect(-4600f, -2700f, 2000f, 1300f);
        var points = layout.roads.SelectMany(r => r.points)
            .Concat(layout.waterways.SelectMany(w => w.points))
            .Concat(layout.areas.SelectMany(a => a.outline))
            .Concat(layout.landmarks.Where(l => l.positionKnown).Select(l => l.position))
            .Concat(layout.boundary.SelectMany(r => r.points));
        foreach (var p in points)
            Assert.IsTrue(area.Contains(p), "a point at " + p + " is far outside the barangay");
    }

    [Test]
    public void The_focus_area_holds_the_hall_and_the_school()
    {
        var layout = Shipped();
        Assert.IsTrue(layout.focusArea.Contains(Place(layout, Hall).position));
        Assert.IsTrue(layout.focusArea.Contains(Place(layout, MabuhayElementary).position));
        Assert.That(layout.focusArea.width, Is.InRange(300f, 1200f), "the focus area is a neighbourhood, not the barangay");
    }

    [Test]
    public void The_map_credits_OpenStreetMap()
    {
        var layout = Shipped();
        StringAssert.Contains("OpenStreetMap", layout.attribution);
        StringAssert.Contains("ODbL", layout.licence);
        Assert.IsFalse(string.IsNullOrEmpty(layout.snapshotDate), "when the data was taken");
    }
}
