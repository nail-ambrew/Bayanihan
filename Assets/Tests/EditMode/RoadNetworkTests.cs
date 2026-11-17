using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The streets as a network: junctions found where roads share a node, each street stopped short of them, one paved patch
/// where they meet, and the RoadKit cross-section of each class fitted into the road's width.
/// </summary>
public class RoadNetworkTests
{
    static MapRoad Road(string id, RoadClass c, params Vector2[] pts) => new MapRoad { osmId = id, roadClass = c, points = pts.ToList() };
    static float Width(RoadClass c) => c >= RoadClass.Secondary ? 11f : c == RoadClass.Residential ? 7f : 5f;

    static float Length(List<Vector2> line)
    {
        float l = 0f;
        for (int i = 1; i < line.Count; i++) l += Vector2.Distance(line[i - 1], line[i]);
        return l;
    }

    [Test]
    public void A_crossroads_is_one_junction_with_one_patch_and_four_trimmed_arms()
    {
        var roads = new List<MapRoad>
        {
            Road("a", RoadClass.Residential, new Vector2(-50, 0), Vector2.zero, new Vector2(50, 0)),
            Road("b", RoadClass.Residential, new Vector2(0, -50), Vector2.zero, new Vector2(0, 50)),
        };
        var net = RoadNetwork.Build(roads, Width);

        Assert.AreEqual(1, net.junctions.Count, "one crossing");
        var j = net.junctions[0];
        Assert.AreEqual(4, j.arms.Count);
        Assert.GreaterOrEqual(j.pad.Count, 4, "a paved patch");
        Assert.IsTrue(MapGeometry.Contains(j.pad, Vector2.zero), "the patch covers the crossing");

        Assert.AreEqual(4, net.pieces.Count, "each street is cut in two at the crossing");
        foreach (var p in net.pieces)
        {
            Assert.AreEqual(50f - j.radius, Length(p.points), 0.01f, "each arm stops at the patch");
            Assert.Greater(p.points.Min(q => q.magnitude), j.radius - 0.01f, "nothing of a street lies under the patch");
        }
    }

    [Test]
    public void A_side_street_ending_on_a_main_road_is_a_T_junction()
    {
        var roads = new List<MapRoad>
        {
            Road("main", RoadClass.Secondary, new Vector2(-60, 0), Vector2.zero, new Vector2(60, 0)),
            Road("side", RoadClass.Residential, Vector2.zero, new Vector2(0, 40)),
        };
        var net = RoadNetwork.Build(roads, Width);
        Assert.AreEqual(1, net.junctions.Count);
        Assert.AreEqual(3, net.junctions[0].arms.Count);
        Assert.AreEqual(RoadClass.Secondary, net.junctions[0].biggest);
        Assert.AreEqual(5.5f + 0.5f, net.junctions[0].radius, 1e-3f, "sized by the widest street");
        Assert.AreEqual(3, net.pieces.Count);
    }

    [Test]
    public void Two_streets_meeting_end_to_end_run_on_without_a_junction()
    {
        var roads = new List<MapRoad>
        {
            Road("a", RoadClass.Residential, new Vector2(-30, 0), Vector2.zero),
            Road("b", RoadClass.Residential, Vector2.zero, new Vector2(30, 5)),
        };
        var net = RoadNetwork.Build(roads, Width);
        Assert.AreEqual(0, net.junctions.Count);
        Assert.AreEqual(30f, Length(net.pieces[0].points), 0.01f, "not trimmed");
    }

    [Test]
    public void Each_class_has_its_kit_cross_section_inside_its_width()
    {
        foreach (RoadClass c in System.Enum.GetValues(typeof(RoadClass)))
        {
            float w = Width(c);
            var p = RoadProfile.For(c, w);
            Assert.AreEqual(w, p.carriage + 2f * p.side, 1e-3f, c + ": carriageway and sides fill the road exactly");
            Assert.Greater(p.carriage, 0.9f, c + " has a carriageway");
        }
        Assert.IsTrue(RoadProfile.For(RoadClass.Secondary, 11f).sidewalks, "main roads have sidewalks");
        Assert.AreEqual(CentreLine.YellowSolid, RoadProfile.For(RoadClass.Secondary, 11f).centre);
        Assert.AreEqual(CentreLine.WhiteDashed, RoadProfile.For(RoadClass.Residential, 7f).centre);
        Assert.AreEqual(RoadSurface.Dirt, RoadProfile.For(RoadClass.Track, 4f).surface);
        Assert.Greater(RoadProfile.For(RoadClass.Trunk, 14f).median, 0f, "the trunk road is divided");
    }

    [Test]
    public void Trimming_and_offsetting_keep_to_the_line()
    {
        var line = new List<Vector2> { Vector2.zero, new Vector2(10, 0), new Vector2(10, 10) };
        var t = RoadNetwork.Trim(line, 2f, 3f);
        Assert.AreEqual(15f, Length(t), 1e-3f);
        Assert.AreEqual(new Vector2(2, 0), t[0]);
        Assert.AreEqual(new Vector2(10, 7), t[t.Count - 1]);
        Assert.IsNull(RoadNetwork.Trim(line, 10f, 10f), "nothing left");

        var off = RoadNetwork.Offset(line, 1f);
        Assert.Less(Vector2.Distance(new Vector2(0, 1), off[0]), 1e-4f, "left of an eastward line is north");
        Assert.Less(Vector2.Distance(new Vector2(9, 1), off[1]), 1e-4f, "the corner is mitred");
    }
}
