using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The flat geometry behind the placeholder map: areas and middles of outlines, the rectangle that best fits a
/// building, the nearest point on a road, ear-clipped fills, walls that face outwards, road strips with mitred corners,
/// clipping to a region, and a hash that is the same on every machine. All of it works on plain numbers, so none of it
/// needs a scene.
/// </summary>
public class MapGeometryTests
{
    static List<Vector2> Box(float x, float y, float w, float h) =>
        new List<Vector2> { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };

    /// <summary>An L: 6 m along the bottom, 6 m up the left, arms 2 m thick. Area 20, one reflex corner.</summary>
    static List<Vector2> LShape() => new List<Vector2>
    {
        new Vector2(0, 0), new Vector2(6, 0), new Vector2(6, 2), new Vector2(2, 2), new Vector2(2, 6), new Vector2(0, 6)
    };

    /// <summary>A comb with one slot cut in from the top: 9 x 6 less a 2 x 4 slot. Area 46.</summary>
    static List<Vector2> Comb() => new List<Vector2>
    {
        new Vector2(0, 0), new Vector2(9, 0), new Vector2(9, 6), new Vector2(7, 6),
        new Vector2(7, 2), new Vector2(5, 2), new Vector2(5, 6), new Vector2(0, 6)
    };

    static float TriangleArea(IList<Vector2> ring, List<int> tris)
    {
        float sum = 0f;
        for (int i = 0; i < tris.Count; i += 3)
            sum += 0.5f * MapGeometry.Cross(ring[tris[i + 1]] - ring[tris[i]], ring[tris[i + 2]] - ring[tris[i]]);
        return sum;
    }

    // ------------------------------------------------------------------ outlines

    [Test]
    public void An_outline_has_a_positive_area_going_counter_clockwise_and_a_negative_one_clockwise()
    {
        var box = Box(0, 0, 4, 3);
        Assert.AreEqual(12f, MapGeometry.SignedArea(box), 1e-4f);
        box.Reverse();
        Assert.AreEqual(-12f, MapGeometry.SignedArea(box), 1e-4f);
        Assert.AreEqual(20f, MapGeometry.SignedArea(LShape()), 1e-4f);
        Assert.AreEqual(0f, MapGeometry.SignedArea(new List<Vector2> { Vector2.zero, Vector2.one }), "two points are not an outline");
    }

    [Test]
    public void Area_and_middle_stay_exact_for_a_small_building_far_from_the_origin()
    {
        // Mabuhay's far west is about 3.8 km from the hall. A 6 x 5 m building there must still measure 30 m2.
        var far = Box(-3800.3f, -2000.2f, 6f, 5f);
        Assert.AreEqual(30f, MapGeometry.SignedArea(far), 0.05f);
        Vector2 c = MapGeometry.Centroid(far);
        Assert.AreEqual(-3797.3f, c.x, 0.02f);
        Assert.AreEqual(-1997.7f, c.y, 0.02f);
    }

    [Test]
    public void The_middle_of_an_L_is_pulled_towards_its_long_arms_and_a_flat_outline_gives_the_average()
    {
        Vector2 c = MapGeometry.Centroid(LShape());
        // The 6 x 2 bottom arm (area 12) is centred on (3, 1) and the 2 x 4 upright arm (area 8) on (1, 4).
        Assert.AreEqual(2.2f, c.x, 0.01f);          // (12 * 3 + 8 * 1) / 20
        Assert.AreEqual(2.2f, c.y, 0.01f);          // (12 * 1 + 8 * 4) / 20
        var flat = new List<Vector2> { new Vector2(0, 0), new Vector2(2, 0), new Vector2(4, 0) };
        Assert.AreEqual(new Vector2(2f, 0f), MapGeometry.Centroid(flat));
    }

    [Test]
    public void A_point_is_inside_an_L_but_not_in_its_notch()
    {
        var l = LShape();
        Assert.IsTrue(MapGeometry.Contains(l, new Vector2(1, 1)));
        Assert.IsTrue(MapGeometry.Contains(l, new Vector2(5, 1)));
        Assert.IsTrue(MapGeometry.Contains(l, new Vector2(1, 5)));
        Assert.IsFalse(MapGeometry.Contains(l, new Vector2(4, 4)), "the notch");
        Assert.IsFalse(MapGeometry.Contains(null, Vector2.zero));
    }

    [Test]
    public void A_clockwise_outline_is_turned_round_and_a_counter_clockwise_one_is_left_alone()
    {
        var cw = LShape(); cw.Reverse();
        Assert.Greater(MapGeometry.SignedArea(MapGeometry.CounterClockwise(cw)), 0f);
        var ccw = LShape();
        CollectionAssert.AreEqual(ccw, MapGeometry.CounterClockwise(ccw));
    }

    // ------------------------------------------------------------------ rectangles

    [Test]
    public void The_best_rectangle_round_a_tilted_box_is_the_box()
    {
        float a = 30f * Mathf.Deg2Rad;
        Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), v = MapGeometry.Left(u), centre = new Vector2(5f, 7f);
        var tilted = new List<Vector2>
        {
            centre - u * 5f - v * 2f, centre + u * 5f - v * 2f, centre + u * 5f + v * 2f, centre - u * 5f + v * 2f
        };
        var r = MapGeometry.MinimumRectangle(tilted);
        Assert.AreEqual(10f, r.sizeU, 0.01f, "the long side");
        Assert.AreEqual(4f, r.sizeV, 0.01f, "the short side");
        Assert.AreEqual(1f, Mathf.Abs(Vector2.Dot(r.axisU, u)), 0.001f, "U runs along the long side");
        Assert.AreEqual(centre.x, r.centre.x, 0.01f);
        Assert.AreEqual(centre.y, r.centre.y, 0.01f);
        Assert.AreEqual(0f, Vector2.Dot(r.axisU, r.axisV), 0.001f);
    }

    [Test]
    public void U_is_always_the_long_side_even_when_the_outline_is_taller_than_it_is_wide()
    {
        var r = MapGeometry.MinimumRectangle(Box(0, 0, 3, 8));
        Assert.AreEqual(8f, r.sizeU, 0.01f);
        Assert.AreEqual(3f, r.sizeV, 0.01f);
        Assert.AreEqual(1f, Mathf.Abs(r.axisU.y), 0.001f, "U runs north to south here");
    }

    [Test]
    public void The_rectangle_round_an_L_is_no_bigger_than_the_upright_box_and_no_smaller_than_the_L()
    {
        var r = MapGeometry.MinimumRectangle(LShape());
        Assert.GreaterOrEqual(r.Area, 20f - 0.01f);
        Assert.LessOrEqual(r.Area, 36f + 0.01f);
        foreach (var corner in LShape()) Assert.IsTrue(r.Contains(corner, 0.01f), corner + " is outside its own rectangle");
        Assert.AreEqual(4, r.Corners().Count);
        Assert.Greater(MapGeometry.SignedArea(r.Corners()), 0f, "corners run counter-clockwise");
    }

    [Test]
    public void The_rectangle_round_the_same_outline_is_always_the_same()
    {
        var a = MapGeometry.MinimumRectangle(Comb());
        var b = MapGeometry.MinimumRectangle(Comb());
        Assert.AreEqual(a.centre, b.centre);
        Assert.AreEqual(a.axisU, b.axisU);
        Assert.AreEqual(a.sizeU, b.sizeU);
    }

    // ------------------------------------------------------------------ lines

    [Test]
    public void The_nearest_point_on_a_road_and_the_stretch_it_is_on_are_found()
    {
        var road = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) };

        float d = MapGeometry.DistanceToPolyline(road, new Vector2(5, 3), out Vector2 closest, out int segment);
        Assert.AreEqual(3f, d, 1e-4f);
        Assert.AreEqual(new Vector2(5f, 0f), closest);
        Assert.AreEqual(0, segment);

        d = MapGeometry.DistanceToPolyline(road, new Vector2(13, 6), out closest, out segment);
        Assert.AreEqual(3f, d, 1e-4f);
        Assert.AreEqual(10f, closest.x, 1e-4f);
        Assert.AreEqual(6f, closest.y, 1e-4f);
        Assert.AreEqual(1, segment);

        d = MapGeometry.DistanceToPolyline(road, new Vector2(-4, -3), out closest);
        Assert.AreEqual(5f, d, 1e-4f, "past the end it is the distance to the end");
        Assert.AreEqual(Vector2.zero, closest);
    }

    // ------------------------------------------------------------------ fills

    [Test]
    public void An_L_is_filled_with_four_triangles_that_add_up_to_its_area_and_all_run_counter_clockwise()
    {
        var l = LShape();
        var tris = new List<int>();
        Assert.IsTrue(MapGeometry.Triangulate(l, tris), "a plain L has no trouble");
        Assert.AreEqual(4 * 3, tris.Count, "six corners make four triangles");
        Assert.AreEqual(20f, TriangleArea(l, tris), 1e-3f);
        for (int i = 0; i < tris.Count; i += 3)
            Assert.Greater(MapGeometry.Cross(l[tris[i + 1]] - l[tris[i]], l[tris[i + 2]] - l[tris[i]]), 0f, "triangle " + i / 3 + " runs the wrong way");
    }

    [Test]
    public void A_comb_and_a_clockwise_outline_fill_to_the_same_area()
    {
        var comb = Comb();
        var tris = new List<int>();
        MapGeometry.Triangulate(comb, tris);
        Assert.AreEqual(6 * 3, tris.Count);
        Assert.AreEqual(46f, TriangleArea(comb, tris), 1e-3f);

        var cw = Comb(); cw.Reverse();
        tris.Clear();
        Assert.IsTrue(MapGeometry.Triangulate(cw, tris));
        Assert.AreEqual(46f, Mathf.Abs(TriangleArea(cw, tris)), 1e-3f);
        for (int i = 0; i < tris.Count; i += 3)
            Assert.Greater(MapGeometry.Cross(cw[tris[i + 1]] - cw[tris[i]], cw[tris[i + 2]] - cw[tris[i]]), 0f, "each triangle is still counter-clockwise");
    }

    [Test]
    public void A_point_in_the_middle_of_a_straight_edge_does_not_spoil_the_fill()
    {
        var box = new List<Vector2> { new Vector2(0, 0), new Vector2(2, 0), new Vector2(4, 0), new Vector2(4, 3), new Vector2(0, 3) };
        var tris = new List<int>();
        Assert.IsTrue(MapGeometry.Triangulate(box, tris));
        Assert.AreEqual(12f, TriangleArea(box, tris), 1e-3f);
        for (int i = 0; i < tris.Count; i++) Assert.AreNotEqual(1, tris[i], "the flat point should be dropped, not used as a corner");
    }

    [Test]
    public void An_outline_that_crosses_itself_still_finishes_and_says_so()
    {
        var bowtie = new List<Vector2> { new Vector2(0, 0), new Vector2(4, 4), new Vector2(4, 0), new Vector2(0, 4) };
        var tris = new List<int>();
        Assert.DoesNotThrow(() => MapGeometry.Triangulate(bowtie, tris));
        Assert.IsTrue(tris.Count % 3 == 0);
    }

    [Test]
    public void A_flat_fill_faces_up()
    {
        var buffers = new MapGeometry.MeshBuffers();
        MapGeometry.Fill(LShape(), 0.5f, buffers);
        Assert.AreEqual(12, buffers.triangles.Count);
        float up = 0f;
        for (int i = 0; i < buffers.triangles.Count; i += 3)
        {
            Vector3 a = buffers.vertices[buffers.triangles[i]], b = buffers.vertices[buffers.triangles[i + 1]], c = buffers.vertices[buffers.triangles[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            Assert.Greater(n.y, 0f, "a Unity front face is the one the normal points out of");
            up += 0.5f * n.y;
            Assert.AreEqual(0.5f, a.y, 1e-5f);
        }
        Assert.AreEqual(20f, up, 1e-3f);
    }

    // ------------------------------------------------------------------ walls

    [Test]
    public void A_block_has_four_walls_and_a_roof_and_every_face_looks_the_way_its_normal_says()
    {
        var buffers = new MapGeometry.MeshBuffers();
        MapGeometry.Extrude(Box(0, 0, 4, 3), 0f, 5f, buffers);
        Assert.AreEqual(4 * 4 + 4, buffers.VertexCount, "four wall quads and a roof");
        Assert.AreEqual((4 * 2 + 2) * 3, buffers.triangles.Count);

        float roof = 0f, walls = 0f;
        for (int i = 0; i < buffers.triangles.Count; i += 3)
        {
            int ia = buffers.triangles[i], ib = buffers.triangles[i + 1], ic = buffers.triangles[i + 2];
            Vector3 n = Vector3.Cross(buffers.vertices[ib] - buffers.vertices[ia], buffers.vertices[ic] - buffers.vertices[ia]);
            float area = 0.5f * n.magnitude;
            Assert.Greater(Vector3.Dot(n.normalized, buffers.normals[ia]), 0.999f, "triangle " + i / 3 + " faces away from its normal");
            if (buffers.normals[ia].y > 0.5f) roof += area; else walls += area;
        }
        Assert.AreEqual(12f, roof, 1e-3f, "roof area");
        Assert.AreEqual(14f * 5f, walls, 1e-3f, "wall area: the perimeter times the height");
    }

    [Test]
    public void A_wall_faces_away_from_the_inside_whichever_way_round_the_outline_was_given()
    {
        foreach (bool reversed in new[] { false, true })
        {
            var ring = Box(0, 0, 4, 3);
            if (reversed) ring.Reverse();
            var buffers = new MapGeometry.MeshBuffers();
            MapGeometry.Extrude(ring, 0f, 2f, buffers);
            Vector3 middle = new Vector3(2f, 1f, 1.5f);
            for (int i = 0; i < buffers.VertexCount; i++)
            {
                if (buffers.normals[i].y > 0.5f) continue;
                Assert.Greater(Vector3.Dot(buffers.normals[i], buffers.vertices[i] - middle), 0f, "a wall faces into the building (reversed " + reversed + ")");
            }
        }
    }

    // ------------------------------------------------------------------ road strips

    [Test]
    public void A_straight_road_is_a_strip_of_the_right_width_facing_up()
    {
        var buffers = new MapGeometry.MeshBuffers();
        MapGeometry.Ribbon(new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0) }, 3f, 0.1f, buffers);
        float area = 0f;
        for (int i = 0; i < buffers.triangles.Count; i += 3)
        {
            Vector3 a = buffers.vertices[buffers.triangles[i]], b = buffers.vertices[buffers.triangles[i + 1]], c = buffers.vertices[buffers.triangles[i + 2]];
            float n = Vector3.Cross(b - a, c - a).y;
            Assert.Greater(n, 0f, "the strip faces down");
            area += 0.5f * n;
        }
        Assert.AreEqual(60f, area, 1e-3f, "ten metres long and six wide");
        foreach (var v in buffers.vertices) Assert.AreEqual(0.1f, v.y, 1e-5f);
    }

    [Test]
    public void A_right_angle_in_a_road_is_mitred_so_the_edges_stay_the_same_distance_from_the_middle()
    {
        var buffers = new MapGeometry.MeshBuffers();
        MapGeometry.Ribbon(new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, 2f, 0f, buffers);
        Assert.AreEqual(6, buffers.VertexCount, "a left and a right edge point at each of three points");

        // The road heads east then turns north. Its left edge turns inside the corner, its right edge outside it.
        Vector3 left = buffers.vertices[2], right = buffers.vertices[3];
        Assert.AreEqual(8f, left.x, 0.01f); Assert.AreEqual(2f, left.z, 0.01f);
        Assert.AreEqual(12f, right.x, 0.01f); Assert.AreEqual(-2f, right.z, 0.01f);
    }

    [Test]
    public void A_road_that_doubles_back_on_itself_does_not_spike()
    {
        var buffers = new MapGeometry.MeshBuffers();
        var hairpin = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 0.01f) };
        MapGeometry.Ribbon(hairpin, 2f, 0f, buffers);
        foreach (var v in buffers.vertices)
        {
            float away = MapGeometry.DistanceToPolyline(hairpin, new Vector2(v.x, v.z), out _);
            Assert.LessOrEqual(away, 4.001f, "a hairpin spikes out further than twice the half-width");
        }
    }

    [Test]
    public void A_mesh_over_65000_vertices_gets_32_bit_indices()
    {
        var buffers = new MapGeometry.MeshBuffers();
        for (int i = 0; i < 65100; i += 3)
        {
            int a = buffers.Add(Vector3.zero, Vector3.up, Vector2.zero), b = buffers.Add(Vector3.right, Vector3.up, Vector2.zero), c = buffers.Add(Vector3.forward, Vector3.up, Vector2.zero);
            buffers.Triangle(a, c, b);
        }
        var mesh = buffers.ToMesh("many");
        Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, mesh.indexFormat);
        Assert.AreEqual(buffers.VertexCount, mesh.vertexCount);
        Object.DestroyImmediate(mesh);
    }

    // ------------------------------------------------------------------ clipping

    [Test]
    public void A_road_is_cut_where_it_leaves_the_region_and_kept_in_two_pieces_when_it_comes_back()
    {
        var region = new Rect(0, 0, 10, 10);

        var through = MapGeometry.ClipPolyline(new List<Vector2> { new Vector2(-5, 5), new Vector2(15, 5) }, region);
        Assert.AreEqual(1, through.Count);
        Assert.AreEqual(new Vector2(0f, 5f), through[0][0]);
        Assert.AreEqual(new Vector2(10f, 5f), through[0][1]);

        var loop = MapGeometry.ClipPolyline(new List<Vector2>
        {
            new Vector2(-5, 5), new Vector2(5, 5), new Vector2(5, 15), new Vector2(8, 15), new Vector2(8, 5)
        }, region);
        Assert.AreEqual(2, loop.Count, "it left at the top and came back");
        Assert.AreEqual(3, loop[0].Count);
        Assert.AreEqual(new Vector2(5f, 10f), loop[0][2]);
        Assert.AreEqual(new Vector2(8f, 10f), loop[1][0]);

        Assert.AreEqual(0, MapGeometry.ClipPolyline(new List<Vector2> { new Vector2(20, 0), new Vector2(30, 5) }, region).Count);
    }

    [Test]
    public void An_outline_is_cut_to_the_region()
    {
        var region = new Rect(0, 0, 10, 10);
        Assert.AreEqual(25f, MapGeometry.SignedArea(MapGeometry.ClipPolygon(Box(-5, -5, 10, 10), region)), 1e-3f, "a corner of it is inside");
        Assert.AreEqual(12f, MapGeometry.SignedArea(MapGeometry.ClipPolygon(Box(2, 2, 4, 3), region)), 1e-3f, "all of it is inside");
        Assert.AreEqual(0, MapGeometry.ClipPolygon(Box(20, 20, 4, 3), region).Count, "none of it is inside");
    }

    // ------------------------------------------------------------------ the same answer every time

    [Test]
    public void The_hash_of_a_name_is_the_same_on_every_machine()
    {
        // Published FNV-1a test values.
        Assert.AreEqual(0x811C9DC5u, MapGeometry.StableHash(""));
        Assert.AreEqual(0xE40C292Cu, MapGeometry.StableHash("a"));
        Assert.AreEqual(0xBF9CF968u, MapGeometry.StableHash("foobar"));
        Assert.AreEqual(MapGeometry.StableHash("w620109194"), MapGeometry.StableHash("w620109194"));
        Assert.AreNotEqual(MapGeometry.StableHash("w620109194"), MapGeometry.StableHash("w620109195"));
    }

    [Test]
    public void A_repeatable_number_is_at_least_nought_and_under_one()
    {
        foreach (var id in new[] { "", "a", "w1", "w620109194", "r10749165", "Mabuhay" })
            Assert.That(MapGeometry.Unit(id), Is.InRange(0f, 0.99999994f));
        Assert.AreNotEqual(MapGeometry.Unit("w1"), MapGeometry.Unit("w2"));
    }

    [Test]
    public void A_heading_turns_a_model_that_faces_plus_z_towards_a_map_direction()
    {
        Assert.AreEqual(0f, MapGeometry.Heading(new Vector2(0, 1)), 1e-4f, "north");
        Assert.AreEqual(90f, MapGeometry.Heading(new Vector2(1, 0)), 1e-4f, "east");
        Assert.AreEqual(-90f, MapGeometry.Heading(new Vector2(-1, 0)), 1e-4f, "west");
        Assert.AreEqual(180f, Mathf.Abs(MapGeometry.Heading(new Vector2(0, -1))), 1e-4f, "south");

        // Turning +Z by the heading about +Y lands on the direction.
        Vector2 d = new Vector2(3, 4).normalized;
        Vector3 turned = Quaternion.Euler(0f, MapGeometry.Heading(d), 0f) * Vector3.forward;
        Assert.AreEqual(d.x, turned.x, 1e-4f);
        Assert.AreEqual(d.y, turned.z, 1e-4f);
    }
}
