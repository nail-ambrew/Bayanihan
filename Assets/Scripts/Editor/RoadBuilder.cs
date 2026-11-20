using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The streets of the Mabuhay map, true to the OpenStreetMap layout and dressed with the RoadKit:
/// <list type="bullet">
/// <item><see cref="Surfaces"/>: each street's cross-section (carriageway, sidewalks or gutters, painted lines) swept along
/// its real centre line, stopped short of every junction, and one paved patch where streets meet. Built by
/// <see cref="BarangayMockBuilder"/> in the map's layout metres.</item>
/// <item><see cref="Build"/>: the kit's bridges laid over the map's bridge ways, then utility poles and wires along the
/// streets, lamps on the main roads, stop signs, traffic lights, street-name signs and zebra crossings at the junctions -
/// the parts that stand above the flood, so the streets read from the boat. World-sized, under BarangayMock/Street Furniture.</item>
/// </list>
/// Nothing is put on the two mission streets' lanes, the boat's start, the hall or a resident.
/// </summary>
public static class RoadBuilder
{
    public const string FurnitureName = "Street Furniture";
    const string MapRootName = "BarangayMock";

    // Layout heights above the map's ground: the sides first, the carriageway, the junction patches, then the paint.
    public const float SideY = 0.080f, CarriageY = 0.090f, PadY = 0.094f, MedianY = 0.097f, PaintY = 0.100f;

    // ------------------------------------------------------------------ the surfaces

    /// <summary>Road meshes by what they are made of: "asphalt", "concrete", "sidewalk", "dirt", "white", "yellow".</summary>
    public static Dictionary<string, MapGeometry.MeshBuffers> Surfaces(BarangayLayout layout, PlaceholderLibrary lib, Rect clip)
    {
        var meshes = new Dictionary<string, MapGeometry.MeshBuffers>();
        MapGeometry.MeshBuffers Get(string k) { if (!meshes.TryGetValue(k, out var b)) meshes[k] = b = new MapGeometry.MeshBuffers(); return b; }

        var net = RoadNetwork.Build(layout.roads, c => lib.roadWidths.For(c));
        foreach (var piece in net.pieces.OrderBy(p => p.road.osmId, System.StringComparer.Ordinal))
        {
            if (piece.bridge) continue;                 // the kit's bridge stands there (Build)
            var p = piece.profile;
            float step = 0.0006f * (int)piece.road.roadClass;      // where two streets cross without a shared node, the bigger is on top
            foreach (var part in MapGeometry.ClipPolyline(piece.points, clip))
            {
                if (part.Count < 2) continue;
                if (p.side > 0f) MapGeometry.Ribbon(part, p.Half, SideY + step, Get(p.sidewalks ? "sidewalk" : "concrete"));
                string surface = p.surface == RoadSurface.Dirt ? "dirt" : p.surface == RoadSurface.Concrete ? "concrete" : "asphalt";
                MapGeometry.Ribbon(part, p.carriage * 0.5f, CarriageY + step, Get(surface));
                if (p.median > 0f) MapGeometry.Ribbon(part, p.median * 0.5f, MedianY + step, Get("concrete"));
                Paint(part, p, step, Get("white"), Get("yellow"));
            }
        }
        foreach (var j in net.junctions)
        {
            if (j.pad.Count < 3 || !clip.Contains(j.centre)) continue;
            bool paved = j.biggest >= RoadClass.Residential;
            MapGeometry.Fill(j.pad, PadY, Get(paved ? "asphalt" : j.biggest == RoadClass.Track ? "dirt" : "concrete"), 0.1f);
        }
        return meshes;
    }

    /// <summary>The lines on a carriageway: a dashed or solid centre line, lane dashes on a divided road, edge lines.</summary>
    static void Paint(List<Vector2> line, RoadProfile p, float step, MapGeometry.MeshBuffers white, MapGeometry.MeshBuffers yellow)
    {
        float y = PaintY + step;
        switch (p.centre)
        {
            case CentreLine.WhiteDashed: Dashes(line, 0f, 0.06f, 3f, 3f, y, white); break;
            case CentreLine.YellowSolid:
                MapGeometry.Ribbon(RoadNetwork.Offset(line, 0.12f), 0.06f, y, yellow);
                MapGeometry.Ribbon(RoadNetwork.Offset(line, -0.12f), 0.06f, y, yellow);
                break;
            case CentreLine.LaneDashes:
                float lane = (p.carriage - p.median) * 0.25f;          // two lanes a side: the dash between them
                float off = p.median * 0.5f + lane;
                Dashes(line, off, 0.06f, 3f, 4f, y, white);
                Dashes(line, -off, 0.06f, 3f, 4f, y, white);
                break;
        }
        if (p.edgeLines)
        {
            float e = p.carriage * 0.5f - 0.25f;
            MapGeometry.Ribbon(RoadNetwork.Offset(line, e), 0.06f, y, white);
            MapGeometry.Ribbon(RoadNetwork.Offset(line, -e), 0.06f, y, white);
        }
    }

    static void Dashes(List<Vector2> line, float offset, float halfWidth, float on, float off, float y, MapGeometry.MeshBuffers into)
    {
        var path = offset == 0f ? line : RoadNetwork.Offset(line, offset);
        float length = 0f;
        for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        for (float s = off * 0.5f; s + on <= length; s += on + off)
        {
            var dash = RoadNetwork.Trim(path, s, length - s - on);
            if (dash != null) MapGeometry.Ribbon(dash, halfWidth, y, into);
        }
    }

    // ------------------------------------------------------------------ bridges and what stands along the streets

    sealed class Context
    {
        public RoadKitLibrary kit;
        public Transform group;
        public float scale, roadY;
        public Rect area;
        /// <summary>The whole ground out to the levee (the streets are drawn that far, so their bridges are too).</summary>
        public Rect ground;
        public List<KeepClear> keepClear;
        public RoadNetwork net;
        public System.Random random;
        public readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        public void Count(string what) { counts.TryGetValue(what, out int n); counts[what] = n + 1; }
        public Vector2 W(Vector2 layout) => layout * scale;
    }

    static bool IsMissionStreet(MapRoad r) => r.osmId == MabuhayLevelBuilder.BagumbayanId || r.osmId == MabuhayLevelBuilder.MapaladId;

    /// <summary>Lays the bridges and the street furniture into the open scene's map. Returns one line saying how many of each.</summary>
    public static string Build(Scene scene, BarangayMockSettings settings, IList<KeepClear> keepClear)
    {
        var kit = RoadKitImporter.LoadLibrary();
        if (kit == null || !kit.Ready) return "Street furniture: no RoadKit yet (Bayanihan > Roads > Import RoadKit).";
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (root == null) return "Street furniture: no map in the scene.";
        var old = root.transform.Find(FurnitureName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        var layout = settings.Layout;
        var lib = settings.library;
        float scale = Mathf.Max(0.05f, settings.scale);
        var c = new Context
        {
            kit = kit,
            scale = scale,
            roadY = RoadBuilder.CarriageY * scale,
            area = MabuhayLevelBuilder.Scaled(settings.Region(layout), scale),
            ground = MabuhayLevelBuilder.EdgeAreaWorld(settings, layout),
            keepClear = new List<KeepClear>(keepClear),
            net = RoadNetwork.Build(layout.roads, k => lib.roadWidths.For(k)),
            random = new System.Random(5113),
        };
        c.group = new GameObject(FurnitureName).transform;
        c.group.SetParent(root.transform, false);
        c.group.localScale = Vector3.one / scale;          // a child's position and size are world metres from the map's origin
        GameObjectUtility.SetStaticEditorFlags(c.group.gameObject, StaticEditorFlags.BatchingStatic);

        PlaceBridges(c);
        Physics.SyncTransforms();
        PlacePolesAndLamps(c);
        PlaceJunctionFurniture(c);
        return "Street furniture: " + string.Join(", ", c.counts.OrderBy(kv => kv.Key).Select(kv => kv.Value + " " + kv.Key)) + ".";
    }

    static Transform Holder(Context c, string name)
    {
        var existing = c.group.Find(name);
        if (existing != null) return existing;
        var t = new GameObject(name).transform;
        t.SetParent(c.group, false);
        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        return t;
    }

    static GameObject Put(Context c, string holder, GameObject prefab, Vector2 world, float y, Quaternion rotation, Vector3 scale, string what)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Holder(c, holder));
        go.transform.localPosition = new Vector3(world.x, y, world.y);
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        c.Count(what);
        return go;
    }

    static bool ClearOfMission(Context c, Vector2 world, float margin)
    {
        foreach (var k in c.keepClear) if (Vector2.Distance(k.centre, world) < k.radius + margin) return false;
        return true;
    }

    /// <summary>Anything fixed and solid in this box (world), other than the ground and the streets' own surfaces?</summary>
    static bool Blocked(Context c, Vector2 world, Vector3 half, Quaternion rotation, float lift)
    {
        var centre = new Vector3(world.x, c.group.position.y + c.roadY + lift + half.y, world.y);
        foreach (var hit in Physics.OverlapBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            if (hit.attachedRigidbody == null && hit.GetComponent<WaterSurface>() == null) return true;
        return false;
    }

    // ---- bridges

    /// <summary>The kit bridge for the street's size, stretched along each straight run of a bridge way.</summary>
    static void PlaceBridges(Context c)
    {
        foreach (var piece in c.net.pieces.Where(p => p.bridge))
        {
            var cls = piece.road.roadClass;
            bool foot = cls == RoadClass.Footway;
            var prefab = cls >= RoadClass.Secondary ? c.kit.bridge4Lane : cls >= RoadClass.Residential ? c.kit.bridge2Lane
                       : foot && c.kit.footbridge != null ? c.kit.footbridge : (c.kit.bridgeWood1Lane ?? c.kit.bridge2Lane);
            if (prefab == null) continue;
            Vector3 size = PlaceholderLibrary.Measure(prefab);
            // A footbridge keeps its own width (a walkway, not the path's); the others are as wide as the street.
            float width = foot ? size.x : piece.profile.total * c.scale;
            for (int i = 1; i < piece.points.Count; i++)
            {
                Vector2 a = c.W(piece.points[i - 1]), b = c.W(piece.points[i]);
                float len = Vector2.Distance(a, b);
                if (len < 1f || !c.ground.Contains((a + b) * 0.5f)) continue;
                var rot = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.y - a.y));
                Put(c, "Bridges", prefab, (a + b) * 0.5f, c.roadY, rot, new Vector3(width / Mathf.Max(1f, size.x), 1f, (len + 0.5f) / Mathf.Max(1f, size.z)), "bridge spans");
            }
        }
    }

    // ---- poles, wires, lamps

    static void PlacePolesAndLamps(Context c)
    {
        foreach (var piece in c.net.pieces)
        {
            var r = piece.road;
            if (piece.bridge || IsMissionStreet(r) || r.roadClass < RoadClass.Residential) continue;
            var line = piece.points.Select(c.W).ToList();
            float half = piece.profile.Half * c.scale;

            // Utility poles down the left, wired one to the next.
            Vector2? lastPole = null;
            Walk(line, c.kit.poleGap * 0.5f, c.kit.poleGap, (at, dir) =>
            {
                // On the street's own edge (the sidewalk or the gutter), as Philippine poles stand, with the houses' fronts
                // right behind. The post and its cross-arm are checked apart, so a one-storey roof under the arm is fine.
                Vector2 spot = at + MapGeometry.Left(dir) * (half - 0.35f);
                var rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                if (!c.area.Contains(spot) || !ClearOfMission(c, spot, 5f) || Blocked(c, spot, new Vector3(0.25f, 3.4f, 0.25f), rot, 0.3f)
                    || Blocked(c, spot, new Vector3(1.3f, 0.6f, 0.3f), rot, 6.6f))
                {
                    lastPole = null;
                    return;
                }
                var pole = r.roadClass >= RoadClass.Tertiary && c.kit.utilityPoleConcrete != null ? c.kit.utilityPoleConcrete : c.kit.utilityPole;
                Put(c, "Utility Poles", pole, spot, c.roadY, rot, Vector3.one, "utility poles");
                if (lastPole.HasValue && c.kit.wireSpan != null)
                {
                    Vector2 from = lastPole.Value, d = spot - from;
                    float len = d.magnitude;
                    if (len > 2f && len < c.kit.poleGap * 1.6f && !WireBlocked(c, from, spot))
                        Put(c, "Wires", c.kit.wireSpan, from, c.roadY, Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)), new Vector3(1f, 1f, len / 8f), "wire spans");
                }
                lastPole = spot;
            });

            // Street lamps on the main roads, on the other side, their arms out over the street.
            if (r.roadClass >= RoadClass.Secondary && c.kit.lamp != null)
                Walk(line, c.kit.lampGap * 0.5f, c.kit.lampGap, (at, dir) =>
                {
                    Vector2 outward = -MapGeometry.Left(dir);
                    Vector2 spot = at + outward * (half - 0.3f);
                    var rot = Quaternion.LookRotation(new Vector3(-outward.x, 0f, -outward.y));
                    if (!c.area.Contains(spot) || !ClearOfMission(c, spot, 5f) || Blocked(c, spot, new Vector3(0.3f, 3.5f, 0.3f), rot, 0.3f)) return;
                    Put(c, "Street Lamps", c.kit.lamp, spot, c.roadY, rot, Vector3.one, "street lamps");
                });
        }
    }

    /// <summary>A wire's line between two pole tops crosses nothing solid (a roof, a tree trunk is fine below it).</summary>
    static bool WireBlocked(Context c, Vector2 a, Vector2 b)
    {
        float y = c.group.position.y + c.roadY + 9.9f;
        var from = new Vector3(a.x, y, a.y);
        var to = new Vector3(b.x, y, b.y);
        foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody != null) continue;
            if (hit.collider.transform.IsChildOf(c.group)) continue;           // its own poles
            return true;
        }
        return false;
    }

    /// <summary>Calls <paramref name="each"/> every <paramref name="gap"/> metres along a line, starting <paramref name="first"/> in.</summary>
    static void Walk(List<Vector2> line, float first, float gap, System.Action<Vector2, Vector2> each)
    {
        float run = 0f, next = first;
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 a = line[i - 1], b = line[i];
            float len = Vector2.Distance(a, b);
            if (len < 1e-4f) continue;
            Vector2 dir = (b - a) / len;
            while (next <= run + len)
            {
                each(a + dir * (next - run), dir);
                next += gap;
            }
            run += len;
        }
    }

    // ---- at the junctions

    static void PlaceJunctionFurniture(Context c)
    {
        foreach (var j in c.net.junctions)
        {
            Vector2 centre = c.W(j.centre);
            if (!c.area.Contains(centre) || !ClearOfMission(c, centre, 6f)) continue;
            if (j.arms.Any(a => IsMissionStreet(a.road))) continue;
            float radius = j.radius * c.scale;

            // A corner of an arm: beside the street, just back from the junction, on the arm's right.
            Vector2 Corner((Vector2 direction, MapRoad road) arm, float back)
            {
                float half = (c.net.pieces.FirstOrDefault(p => p.road == arm.road)?.profile.Half ?? 3f) * c.scale;
                return centre + arm.direction * (radius + back) - MapGeometry.Left(arm.direction) * (half + 0.6f);
            }
            bool TryPut(string holder, GameObject prefab, Vector2 spot, Quaternion rot, Vector3 half, string what)
            {
                if (prefab == null || !c.area.Contains(spot) || !ClearOfMission(c, spot, 4f) || Blocked(c, spot, half, rot, 0.3f)) return false;
                Put(c, holder, prefab, spot, c.roadY, rot, Vector3.one, what);
                return true;
            }

            var big = j.arms.Where(a => a.road.roadClass >= RoadClass.Secondary).ToList();
            var minor = j.arms.Where(a => a.road.roadClass >= RoadClass.Residential && a.road.roadClass <= RoadClass.Unclassified).ToList();

            // Traffic lights where two main roads cross.
            if (big.Count >= 3 && big.Select(a => a.road).Distinct().Count() >= 2)
            {
                var arm = big[0];
                TryPut("Traffic Lights", c.kit.trafficLight, Corner(arm, 1.5f), Quaternion.LookRotation(new Vector3(-arm.direction.x, 0f, -arm.direction.y)), new Vector3(0.3f, 2.5f, 0.3f), "traffic lights");
            }
            // A stop sign where a side street meets a bigger road, facing the side street's traffic.
            else if (j.biggest >= RoadClass.Tertiary && minor.Count > 0)
            {
                var arm = minor[0];
                TryPut("Signs", c.kit.stopSign, Corner(arm, 1f), Quaternion.LookRotation(new Vector3(arm.direction.x, 0f, arm.direction.y)), new Vector3(0.3f, 1.2f, 0.2f), "stop signs");
            }

            // The street's name on a corner, where a named street is part of the junction.
            var named = j.arms.FirstOrDefault(a => !string.IsNullOrEmpty(a.road.name) && a.road.roadClass >= RoadClass.Residential);
            if (named.road != null)
            {
                var other = j.arms.FirstOrDefault(a => a.road != named.road);
                if (other.road != null)
                    TryPut("Signs", c.kit.streetNameSign, Corner(other, 0.5f), Quaternion.LookRotation(new Vector3(other.direction.x, 0f, other.direction.y)), new Vector3(0.4f, 1.4f, 0.4f), "street-name signs");
            }

            // Zebra crossings over each arm of a four-way crossing of real streets.
            if (j.arms.Count >= 4 && j.biggest >= RoadClass.Residential && c.kit.crosswalk7 != null)
                foreach (var arm in j.arms)
                {
                    if (arm.road.roadClass < RoadClass.Residential) continue;
                    var piece = c.net.pieces.FirstOrDefault(p => p.road == arm.road);
                    if (piece == null) continue;
                    Vector2 spot = centre + arm.direction * (radius + 1.6f);
                    if (!c.area.Contains(spot) || !ClearOfMission(c, spot, 2f)) continue;
                    float across = piece.profile.carriage * c.scale;
                    // The kit's crossing runs 7 m along its X: turned so X goes across the street.
                    var rot = Quaternion.LookRotation(new Vector3(arm.direction.x, 0f, arm.direction.y));
                    Put(c, "Crossings", c.kit.crosswalk7, spot, c.roadY + 0.02f * c.scale, rot, new Vector3(across / 7f, 1f, 1f), "zebra crossings");
                }
        }
    }
}
