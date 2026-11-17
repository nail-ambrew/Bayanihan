using System.Collections.Generic;
using UnityEngine;

/// <summary>What a road's surface is made of.</summary>
public enum RoadSurface { Asphalt, Concrete, Dirt }

/// <summary>The painted line down a road's middle.</summary>
public enum CentreLine { None, WhiteDashed, YellowSolid, LaneDashes }

/// <summary>
/// A road's cross-section, after the RoadKit's: a carriageway, and either side of it a sidewalk or a concrete gutter. All
/// in layout metres; the total is the road's width in the placeholder library, so houses, plants and props keep the
/// clearances they were laid out with.
/// </summary>
public struct RoadProfile
{
    public float total;
    public float carriage;          // asphalt (or concrete, or dirt) width
    public float side;              // each side: sidewalk or gutter
    public float median;            // a concrete strip down the middle of a divided road
    public RoadSurface surface;
    public bool sidewalks;          // the sides are sidewalks (else a concrete gutter)
    public CentreLine centre;
    public bool edgeLines;

    public float Half => total * 0.5f;

    /// <summary>A narrow street drawn as an asphalt road with a centre line and edge lines, like the others.</summary>
    public static RoadProfile Asphalt(float total) => new RoadProfile
    {
        total = total, carriage = total - 1f, side = 0.5f, surface = RoadSurface.Asphalt,
        sidewalks = false, centre = CentreLine.WhiteDashed, edgeLines = true
    };

    /// <summary>Streets the map draws as asphalt roads although OpenStreetMap calls them service lanes (by OSM id).</summary>
    public static readonly HashSet<string> AsphaltLanes = new HashSet<string> { "w936561212" };   // the lane in front of the four houses east of the Iglesia Ni Cristo

    public static RoadProfile For(RoadClass c, float total)
    {
        var p = new RoadProfile { total = total, surface = RoadSurface.Asphalt };
        switch (c)
        {
            case RoadClass.Trunk:
                p.side = 0.5f; p.median = 1f; p.centre = CentreLine.LaneDashes; p.edgeLines = true; break;
            case RoadClass.Primary:
            case RoadClass.Secondary:
                p.side = Mathf.Min(2f, total * 0.18f); p.sidewalks = true; p.centre = CentreLine.YellowSolid; p.edgeLines = true; break;
            case RoadClass.Tertiary:
                p.side = Mathf.Min(1f, total * 0.12f); p.sidewalks = true; p.centre = CentreLine.WhiteDashed; break;
            case RoadClass.Unclassified:
            case RoadClass.Residential:
                p.side = 0.5f; p.centre = CentreLine.WhiteDashed; break;
            case RoadClass.Service:
                p.side = 0f; p.surface = RoadSurface.Concrete; break;
            case RoadClass.Track:
                p.side = 0f; p.surface = RoadSurface.Dirt; break;
            default:
                p.side = 0f; p.surface = RoadSurface.Concrete; break;
        }
        p.carriage = Mathf.Max(1f, total - 2f * p.side);
        return p;
    }
}

/// <summary>
/// The streets as a network: where they meet, and the pieces of each street between its junctions. Junctions are the map's
/// shared nodes (OpenStreetMap joins roads at a node they both have). Each street stops short of a junction by the
/// junction's size, and the junction itself is one paved patch, so crossings are clean instead of strips laid over strips.
/// Pure data: the editor's <c>RoadBuilder</c> turns it into meshes.
/// </summary>
public class RoadNetwork
{
    public class Junction
    {
        public Vector2 centre;
        /// <summary>How far each street stops short of the centre.</summary>
        public float radius;
        /// <summary>Directions the streets leave in, and the road each belongs to.</summary>
        public readonly List<(Vector2 direction, MapRoad road)> arms = new List<(Vector2, MapRoad)>();
        /// <summary>The paved patch: counter-clockwise.</summary>
        public List<Vector2> pad = new List<Vector2>();
        public RoadClass biggest = RoadClass.Footway;
        public RoadClass smallest = RoadClass.Trunk;
    }

    public class Piece
    {
        public MapRoad road;
        public RoadProfile profile;
        public List<Vector2> points;
        public bool bridge;
    }

    public readonly List<Junction> junctions = new List<Junction>();
    public readonly List<Piece> pieces = new List<Piece>();

    static long Key(Vector2 p) => ((long)Mathf.RoundToInt(p.x * 20f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 20f);

    /// <param name="width">A road class's total width (layout metres).</param>
    public static RoadNetwork Build(IList<MapRoad> roads, System.Func<RoadClass, float> width)
    {
        var net = new RoadNetwork();

        // Every node, and the roads through it.
        var at = new Dictionary<long, List<(MapRoad road, int index)>>();
        foreach (var r in roads)
        {
            if (r.points == null || r.points.Count < 2) continue;
            for (int i = 0; i < r.points.Count; i++)
            {
                long k = Key(r.points[i]);
                if (!at.TryGetValue(k, out var list)) at[k] = list = new List<(MapRoad, int)>();
                list.Add((r, i));
            }
        }

        var junctionAt = new Dictionary<long, Junction>();
        foreach (var kv in at)
        {
            if (kv.Value.Count < 2) continue;
            var j = new Junction { centre = kv.Value[0].road.points[kv.Value[0].index] };
            foreach (var (road, i) in kv.Value)
            {
                var pts = road.points;
                if (i > 0) j.arms.Add(((pts[i - 1] - pts[i]).normalized, road));
                if (i < pts.Count - 1) j.arms.Add(((pts[i + 1] - pts[i]).normalized, road));
                float half = width(road.roadClass) * 0.5f;
                j.radius = Mathf.Max(j.radius, half);
                if (road.roadClass > j.biggest) j.biggest = road.roadClass;
                if (road.roadClass < j.smallest) j.smallest = road.roadClass;
            }
            if (j.arms.Count < 3) continue;           // two ends meeting in a line is not a crossing: the street runs on
            j.radius += 0.5f;
            j.pad = Pad(j, width);
            junctionAt[kv.Key] = j;
            net.junctions.Add(j);
        }

        // Each road, cut at its junctions and stopped short of them.
        foreach (var r in roads)
        {
            if (r.points == null || r.points.Count < 2) continue;
            var profile = RoadProfile.AsphaltLanes.Contains(r.osmId) ? RoadProfile.Asphalt(width(r.roadClass)) : RoadProfile.For(r.roadClass, width(r.roadClass));
            var current = new List<Vector2> { r.points[0] };
            float trimStart = junctionAt.TryGetValue(Key(r.points[0]), out var j0) ? j0.radius : 0f;
            for (int i = 1; i < r.points.Count; i++)
            {
                current.Add(r.points[i]);
                bool isJunction = junctionAt.TryGetValue(Key(r.points[i]), out var j);
                if (isJunction || i == r.points.Count - 1)
                {
                    var trimmed = Trim(current, trimStart, isJunction ? j.radius : 0f);
                    if (trimmed != null) net.pieces.Add(new Piece { road = r, profile = profile, points = trimmed, bridge = r.bridge });
                    current = new List<Vector2> { r.points[i] };
                    trimStart = isJunction ? j.radius : 0f;
                }
            }
        }
        return net;
    }

    /// <summary>The patch where streets meet: round each arm's end, its two corners, and the hull of them all.</summary>
    static List<Vector2> Pad(Junction j, System.Func<RoadClass, float> width)
    {
        var corners = new List<Vector2>();
        foreach (var (d, road) in j.arms)
        {
            float half = width(road.roadClass) * 0.5f;
            Vector2 end = j.centre + d * j.radius, left = MapGeometry.Left(d) * half;
            corners.Add(end + left);
            corners.Add(end - left);
        }
        var hull = MapGeometry.ConvexHull(corners);
        return MapGeometry.CounterClockwise(hull);
    }

    /// <summary>A line with <paramref name="fromStart"/> metres taken off its start and <paramref name="fromEnd"/> off its end; null if nothing is left.</summary>
    public static List<Vector2> Trim(List<Vector2> line, float fromStart, float fromEnd)
    {
        float length = 0f;
        for (int i = 1; i < line.Count; i++) length += Vector2.Distance(line[i - 1], line[i]);
        if (length - fromStart - fromEnd < 0.5f) return null;
        return Between(line, fromStart, length - fromEnd);
    }

    static List<Vector2> Between(List<Vector2> line, float a, float b)
    {
        var result = new List<Vector2>();
        float run = 0f;
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 p = line[i - 1], q = line[i];
            float len = Vector2.Distance(p, q);
            if (len < 1e-5f) continue;
            float s0 = run, s1 = run + len;
            if (s1 >= a && s0 <= b)
            {
                float t0 = Mathf.Clamp01((a - s0) / len), t1 = Mathf.Clamp01((b - s0) / len);
                Vector2 start = Vector2.Lerp(p, q, t0), end = Vector2.Lerp(p, q, t1);
                if (result.Count == 0 || (result[result.Count - 1] - start).sqrMagnitude > 1e-6f) result.Add(start);
                if ((end - start).sqrMagnitude > 1e-6f) result.Add(end);
            }
            run = s1;
        }
        return result.Count >= 2 ? result : null;
    }

    /// <summary>The line moved sideways by <paramref name="offset"/> metres (positive = left), corners mitred.</summary>
    public static List<Vector2> Offset(IList<Vector2> line, float offset)
    {
        var r = new List<Vector2>(line.Count);
        int n = line.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 nPrev = MapGeometry.Left((i > 0 ? line[i] - line[i - 1] : line[1] - line[0]).normalized);
            Vector2 nNext = i < n - 1 ? MapGeometry.Left((line[i + 1] - line[i]).normalized) : nPrev;
            Vector2 m = nPrev + nNext;
            float len = offset;
            if (m.sqrMagnitude < 1e-4f) m = nPrev;
            else { m.Normalize(); len = offset / Mathf.Max(Vector2.Dot(m, nPrev), 0.5f); }
            r.Add(line[i] + m * len);
        }
        return r;
    }
}
