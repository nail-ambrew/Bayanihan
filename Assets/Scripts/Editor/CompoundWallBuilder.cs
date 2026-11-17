using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Walls round the private compounds of the Mabuhay map: schools, churches, the hospital and clinics, the water works,
/// petrol stations, and the commercial, industrial and residential (subdivision) lots the layout outlines. A wall is a hollow-block wall a metre and a bit above the flood; commercial and industrial
/// lots get a lower wall with a metal fence on it.
///
/// A wall opens wherever a street or driveway crosses it, every compound has at least two ways in, and nothing is walled
/// across a street, a house, the hall, a resident or the boat's start. Afterwards the boat must still reach every resident
/// and most of each compound's open ground; where it cannot, a gate is opened on the side the water comes from.
/// World-sized, under BarangayMock/Compound Walls; each wall is solid where it is drawn.
/// </summary>
public static class CompoundWallBuilder
{
    public const string GroupName = "Compound Walls";
    const string MapRootName = "BarangayMock";
    const float PieceLength = 3f, Thickness = 0.22f;
    const float WallHeight = 2.4f, LowWallHeight = 1.2f;      // above the ground, which is a metre under the water

    enum Style { Wall, FencedLowWall }

    sealed class Compound
    {
        public string id, name;
        public Style style;
        public List<Vector2> outline;                          // world, counter-clockwise
        public readonly List<(Vector2 a, Vector2 b)> pieces = new List<(Vector2, Vector2)>();
        public int gates;
    }

    static readonly LandmarkCategory[] Walled =
    {
        LandmarkCategory.School, LandmarkCategory.Hospital, LandmarkCategory.HealthCentre, LandmarkCategory.Church,
        LandmarkCategory.WaterSupply, LandmarkCategory.Fuel, LandmarkCategory.Government, LandmarkCategory.Police,
        LandmarkCategory.FireStation
    };

    public static string Build(Scene scene, BarangayMockSettings settings, IList<KeepClear> keepClear)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (root == null) return "Walls: no map in the scene.";
        var old = root.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        var layout = settings.Layout;
        var lib = settings.library;
        float scale = Mathf.Max(0.05f, settings.scale);
        var area = MabuhayLevelBuilder.Scaled(settings.Region(layout), scale);
        var roads = layout.roads.Where(r => r.points.Count >= 2)
                                .Select(r => (line: r.points.Select(p => p * scale).ToList(), half: lib.roadWidths.For(r.roadClass) * 0.5f * scale, road: r))
                                .ToList();

        // ---- the outlines
        var compounds = new List<Compound>();
        var seen = new HashSet<string>();
        void AddOutline(string id, string name, IList<Vector2> ring, Style style)
        {
            if (ring == null || ring.Count < 3 || !seen.Add(id)) return;
            var world = MapGeometry.CounterClockwise(ring.Select(p => p * scale).ToList());
            if (Mathf.Abs(MapGeometry.SignedArea(world)) < 120f) return;            // a hut, not a compound
            if (!area.Overlaps(MapGeometry.BoundsOf(world))) return;
            compounds.Add(new Compound { id = id, name = string.IsNullOrEmpty(name) ? id : name, style = style, outline = world });
        }
        foreach (var l in layout.landmarks)
            if (System.Array.IndexOf(Walled, l.category) >= 0) AddOutline(l.id, l.name, l.footprint, Style.Wall);
        foreach (var a in layout.areas)
            switch (a.kind)
            {
                case AreaKind.Institutional: AddOutline(a.osmId, a.name, a.outline, Style.Wall); break;
                case AreaKind.Residential: AddOutline(a.osmId, a.name, a.outline, Style.Wall); break;
                case AreaKind.Commercial:
                case AreaKind.Industrial: AddOutline(a.osmId, a.name, a.outline, Style.FencedLowWall); break;
            }

        // ---- the pieces, with the streets and driveways left open
        var keep = new List<KeepClear>(keepClear);
        bool OnARoad(Vector2 p, float extra) => roads.Any(r => MapGeometry.DistanceToPolyline(r.line, p, out _) < r.half + extra);
        bool NearMission(Vector2 p) => keep.Any(k => Vector2.Distance(k.centre, p) < k.radius + 4f);

        foreach (var cpd in compounds)
        {
            int n = cpd.outline.Count;
            bool inGap = false;                                 // counting gaps, not the pieces left out of them
            for (int e = 0; e < n; e++)
            {
                Vector2 p0 = cpd.outline[e], p1 = cpd.outline[(e + 1) % n];
                float len = Vector2.Distance(p0, p1);
                if (len < 0.5f) continue;
                int count = Mathf.Max(1, Mathf.CeilToInt(len / PieceLength));
                for (int k = 0; k < count; k++)
                {
                    Vector2 a = Vector2.Lerp(p0, p1, (float)k / count), b = Vector2.Lerp(p0, p1, (float)(k + 1) / count);
                    Vector2 mid = (a + b) * 0.5f;
                    if (!area.Contains(mid)) continue;
                    if (OnARoad(mid, 1.2f) || OnARoad(a, 0.8f) || OnARoad(b, 0.8f))                       // a street or driveway goes in here
                    {
                        if (!inGap) cpd.gates++;
                        inGap = true;
                        continue;
                    }
                    inGap = false;
                    if (NearMission(mid)) continue;
                    cpd.pieces.Add((a, b));
                }
            }
        }

        // ---- every compound has two ways in: a gate on the side nearest a street, then the next nearest
        foreach (var cpd in compounds.Where(x => x.pieces.Count > 0))
        {
            for (int g = cpd.gates; g < 2 && cpd.pieces.Count > 3; g++)
            {
                var best = cpd.pieces.OrderBy(p => roads.Min(r => MapGeometry.DistanceToPolyline(r.line, (p.a + p.b) * 0.5f, out _) - r.half)).First();
                OpenGate(cpd, (best.a + best.b) * 0.5f, 4.5f);
                cpd.gates++;
            }
        }

        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);
        group.localScale = Vector3.one / scale;
        var kit = RoadKitImporter.LoadLibrary();
        var mats = Materials(kit);
        var slippery = BarangayMockBuilder.SlipperySurface();
        float groundLocal = 0f;     // the map's ground, in the group's (world-sized) units
        int dropped = 0;

        // Pieces that would stand in a house or a landmark (an outline drawn round a building) are left out.
        foreach (var cpd in compounds)
        {
            var kept = new List<(Vector2, Vector2)>();
            foreach (var (a, b) in cpd.pieces)
            {
                Vector2 mid = (a + b) * 0.5f, d = b - a;
                var rot = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y));
                var centre = new Vector3(mid.x, root.transform.position.y + 0.5f + WallHeight * 0.4f, mid.y);
                bool blocked = Physics.OverlapBox(centre, new Vector3(Thickness, WallHeight * 0.35f, d.magnitude * 0.45f), rot, ~0, QueryTriggerInteraction.Ignore)
                                      .Any(h => h.attachedRigidbody == null && h.GetComponent<WaterSurface>() == null);
                if (blocked) { dropped++; continue; }
                kept.Add((a, b));
            }
            cpd.pieces.Clear();
            cpd.pieces.AddRange(kept);
        }

        int Draw()
        {
            foreach (Transform t in group.Cast<Transform>().ToList()) Object.DestroyImmediate(t.gameObject);
            int drawn = 0;
            foreach (var cpd in compounds.Where(x => x.pieces.Count > 0))
            {
                var holder = new GameObject(cpd.name).transform;
                holder.SetParent(group, false);
                drawn += DrawCompound(cpd, holder, groundLocal, mats, kit, slippery);
            }
            Physics.SyncTransforms();
            return drawn;
        }
        int pieces = Draw();

        // ---- the boat must still reach every resident, and most of each compound's open ground
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        int opened = 0;
        if (boat != null)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var water = new BoatReach(Rect.MinMaxRect(area.xMin - 20f, area.yMin - 20f, area.xMax + 20f, area.yMax + 20f), 1f, BoatReach.DefaultRadius, 0f);
                water.Flood(boat.transform.position);
                bool changed = false;

                foreach (var k in keep)
                    if (!water.Reached(k.centre) && water.DistanceToReached(k.centre, 6f) > 5f)
                        foreach (var cpd in compounds.Where(x => x.pieces.Count > 0 && Near(x, k.centre, 40f)))
                        {
                            OpenGate(cpd, ClosestPiece(cpd, k.centre), 6f);
                            changed = true; opened++;
                        }

                foreach (var cpd in compounds.Where(x => x.pieces.Count > 0 && x.outline.Count >= 3))
                {
                    var inside = OpenGroundInside(cpd, water);
                    if (inside.total < 20 || inside.reached >= inside.total * 0.9f) continue;
                    // A gate on the wall nearest the water the boat already reaches.
                    var piece = cpd.pieces.OrderBy(p => water.DistanceToReached((p.a + p.b) * 0.5f, 60f)).First();
                    OpenGate(cpd, (piece.a + piece.b) * 0.5f, 6f);
                    changed = true; opened++;
                }
                if (!changed) break;
                pieces = Draw();
            }
        }

        return "Walls: " + pieces + " m of wall round " + compounds.Count(x => x.pieces.Count > 0) + " compounds (" +
               compounds.Sum(x => x.gates) + " openings for streets and gates, " + opened + " opened for the boat, " + dropped + " pieces left out where a building stands).";
    }

    static bool Near(Compound c, Vector2 p, float d) => c.pieces.Any(x => Vector2.Distance((x.a + x.b) * 0.5f, p) < d);

    static Vector2 ClosestPiece(Compound c, Vector2 p)
    {
        var best = c.pieces.OrderBy(x => Vector2.Distance((x.a + x.b) * 0.5f, p)).First();
        return (best.a + best.b) * 0.5f;
    }

    static void OpenGate(Compound c, Vector2 at, float halfWidth)
    {
        c.pieces.RemoveAll(x => MapGeometry.DistanceToPolyline(new[] { x.a, x.b }, at, out _) < halfWidth);
    }

    static (int total, int reached) OpenGroundInside(Compound c, BoatReach water)
    {
        var b = MapGeometry.BoundsOf(c.outline);
        int total = 0, reached = 0;
        float step = Mathf.Max(2f, Mathf.Sqrt(b.width * b.height / 400f));
        for (float x = b.xMin + step * 0.5f; x < b.xMax; x += step)
        for (float y = b.yMin + step * 0.5f; y < b.yMax; y += step)
        {
            var p = new Vector2(x, y);
            if (!MapGeometry.Contains(c.outline, p) || !water.InArea(p) || !water.IsClear(p)) continue;
            total++;
            if (water.Reached(p)) reached++;
        }
        return (total, reached);
    }

    // ------------------------------------------------------------------ drawing

    static Dictionary<string, Material> Materials(RoadKitLibrary kit)
    {
        var m = new Dictionary<string, Material>();
        Material Lit(string name, Color colour)
        {
            const string folder = "Assets/Materials/Placeholders";
            PlaceholderBaker.EnsureFolder(folder);
            string path = folder + "/Wall_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
        m["wall"] = Lit("HollowBlock", new Color(0.70f, 0.69f, 0.64f));
        m["cap"] = Lit("Cap", new Color(0.58f, 0.57f, 0.53f));
        return m;
    }

    /// <summary>Draws a compound's pieces as one mesh per material, solid where drawn; fence panels on a fenced wall.</summary>
    static int DrawCompound(Compound c, Transform holder, float groundY, Dictionary<string, Material> mats, RoadKitLibrary kit, PhysicsMaterial slippery)
    {
        var wall = new MapGeometry.MeshBuffers();
        var cap = new MapGeometry.MeshBuffers();
        float height = c.style == Style.Wall ? WallHeight : LowWallHeight;
        int metres = 0;
        foreach (var (a, b) in c.pieces)
        {
            Box(wall, a, b, Thickness, groundY, height);
            Box(cap, a, b, Thickness + 0.1f, groundY + height, 0.08f);
            Vector2 d = (b - a).normalized;
            Box(wall, a - d * 0.18f, a + d * 0.18f, 0.34f, groundY, height + 0.12f);      // a post at each joint
            metres += Mathf.RoundToInt(Vector2.Distance(a, b));
        }
        Mesh(holder, "Wall", wall, mats["wall"], slippery, true);
        Mesh(holder, "Cap", cap, mats["cap"], slippery, false);

        if (c.style == Style.FencedLowWall && kit != null && kit.metalFence != null)
        {
            Vector3 size = PlaceholderLibrary.Measure(kit.metalFence);
            foreach (var (a, b) in c.pieces)
            {
                Vector2 d = b - a;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(kit.metalFence, holder);
                go.transform.localPosition = new Vector3((a.x + b.x) * 0.5f, groundY + height + 0.08f, (a.y + b.y) * 0.5f);
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(-d.y, 0f, d.x));           // the panel runs along its X
                go.transform.localScale = new Vector3(d.magnitude / Mathf.Max(0.5f, size.x), 1f, 1f);
            }
        }
        return metres;
    }

    static void Mesh(Transform holder, string name, MapGeometry.MeshBuffers b, Material m, PhysicsMaterial slippery, bool solid)
    {
        if (b.IsEmpty) return;
        var go = new GameObject(name);
        go.transform.SetParent(holder, false);
        var mesh = b.ToMesh(holder.name + "_" + name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        if (solid)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.sharedMaterial = slippery;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
    }

    /// <summary>A box along a-b, <paramref name="thick"/> wide, from y up <paramref name="height"/>: four sides and a top.</summary>
    static void Box(MapGeometry.MeshBuffers into, Vector2 a, Vector2 b, float thick, float y, float height)
    {
        Vector2 d = (b - a).normalized, n = MapGeometry.Left(d) * (thick * 0.5f);
        var ring = new List<Vector2> { a - n, b - n, b + n, a + n };
        MapGeometry.Extrude(ring, y, height, into);
    }
}
