using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lines Mabuhay's streets with continuous rows of houses and shops, as the barangay really is: wall to wall, no yards,
/// no lot of their own, fronts on the street. Uses only the commercial and residential packs (the BAY_ models in the
/// placeholder library), and replaces the one-house-per-outline houses the map is first built with.
///
///  - Every street, lane and footpath gets a row on each side, from just off its edge (0.2 to 0.5 m), unit after unit
///    with no gap between them, each turned to face the street it fronts.
///  - Behind each row the land is filled with more rows: the second back to back with the first, then an alley (1.2 m),
///    then two more back to back, and so on until they meet the rows coming from the next street. Rows are laid round
///    all the streets in turn, so a block fills from all sides and meets in the middle.
///  - Main-road fronts (trunk, primary, secondary, tertiary) take the commercial pack; everything else the residential.
///  - Storeys by measured height (up to 4 m one, up to 6.5 m two, above that three; the four-storey Tower is left out):
///    75 / 18 / 7 % over the map and in each 80 m neighbourhood. Never two three-storey units within 25 m, never more
///    than two taller units side by side; main-road fronts weigh the taller ones twice.
///  - No model is used twice within four units of a row. (Not mirrored: a mirrored mesh's collider box no longer
///    matches what is drawn.)
///  - Kept off: streets, waterways, landmark lots and the blocks the map keeps, walled compounds and the protected zones
///    (WalterMart and Macaria Business Center, plus 5 m).
/// Same seed, same town. Units are under BarangayMock/Buildings, named "House row ...", so the level finds roofs on them.
/// </summary>
public static class RowBuilder
{
    public const string NamePrefix = "House row ";
    public const int Seed = 4103;

    public static readonly float[] Target = { 0f, 0.75f, 0.18f, 0.07f };
    public const float OneStoreyMax = 4f, TwoStoreyMax = 6.5f, TallestAllowed = 10f;
    public const float Alley = 1.2f;
    public const float SetbackMin = 0.2f, SetbackMax = 0.5f;
    public const float ThreeStoreySpacing = 25f;
    public const int MaxTallRun = 2;
    public const int NoRepeatWithin = 4;
    public const float ProtectedBuffer = 5f;
    /// <summary>A row unit is not drawn once it is smaller than this share of the screen's height (LODGroup cull).</summary>
    public const float CullBelowScreenHeight = 0.015f;
    const float Cell = 0.5f;
    const int MaxRows = 12;

    /// <summary>One model of the packs, sorted by what it is.</summary>
    public sealed class Model
    {
        public HouseVariant variant;
        public string family;
        public int storeys;
        public bool commercial;
        public Vector2 footprint;     // width along the street (x), depth back from it (z)
        public Vector2 offset;        // where the drawn footprint's middle is from the pivot (x, z): not every model is centred
        public float height;
        public string Name => variant.name;
    }

    sealed class Unit
    {
        public int id;
        public Model model;
        public Vector2 centre, front;          // layout metres; front = the way it faces
        public bool mirror;
        public float depthScale = 1f;          // a taller unit swapped in may be a little shallower (0.8 at least)
        public MapRoad road;
        public bool main, corner;
        public int row;
        public Transform placed;
        public float Yaw => MapGeometry.Heading(front);
    }

    /// <summary>One side of one road, walked once per row.</summary>
    sealed class Walk { public MapRoad road; public float side; public int row; public float offset; public readonly Queue<string> recent = new Queue<string>(); public int tallRun; public int last; public Vector2 lastDir; }

    sealed class Grid
    {
        public Rect area; public int w, h; public int[] cell;     // 0 free, 1 kept off, 2 and up: built, by unit (id + 1)
        public Grid(Rect a) { area = a; w = Mathf.CeilToInt(a.width / Cell); h = Mathf.CeilToInt(a.height / Cell); cell = new int[w * h]; }
        public bool Index(Vector2 p, out int i)
        {
            int x = Mathf.FloorToInt((p.x - area.xMin) / Cell), y = Mathf.FloorToInt((p.y - area.yMin) / Cell);
            i = y * w + x;
            return x >= 0 && y >= 0 && x < w && y < h;
        }
        public bool Free(Vector2 p) => Index(p, out int i) && cell[i] == 0;
        public bool Free(Vector2 p, int ignore) => Index(p, out int i) && (cell[i] == 0 || cell[i] == ignore);
    }

    /// <summary>The packs' models, by storeys and use. Towers (four storeys) are left out.</summary>
    public static List<Model> Models(PlaceholderLibrary lib)
    {
        var list = new List<Model>();
        foreach (var v in lib.houses)
        {
            if (v.prefab == null || string.IsNullOrEmpty(v.name) || !v.name.StartsWith("BAY_")) continue;
            float h = v.size.y;
            if (h <= 0.1f || h > TallestAllowed) continue;
            var parts = v.name.Split('_');
            list.Add(new Model
            {
                variant = v,
                family = parts.Length >= 3 ? parts[1] + "_" + parts[2] : v.name,
                storeys = h <= OneStoreyMax ? 1 : h <= TwoStoreyMax ? 2 : 3,
                commercial = v.name.StartsWith("BAY_F_"),
                footprint = new Vector2(v.size.x, v.size.z),
                offset = DrawnCentre(v.prefab),
                height = h,
            });
        }
        return list;
    }

    /// <summary>The middle of a prefab's drawn parts on the ground, in its own space (x, z).</summary>
    static Vector2 DrawnCentre(GameObject prefab)
    {
        bool any = false; var b = new Bounds();
        var inv = prefab.transform.worldToLocalMatrix;
        foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            var m = inv * f.transform.localToWorldMatrix;
            var mb = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
            }
        }
        return any ? new Vector2(b.center.x, b.center.z) : Vector2.zero;
    }

    public const string SharedMaterialPath = "Assets/Materials/BAY_HousePalette_Shared.mat";
    static Material shared;

    /// <summary>The one material all the pack's houses are drawn with, made once from the first model's own.</summary>
    static Material SharedMaterial(GameObject anyModel)
    {
        if (shared != null) return shared;
        shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
        if (shared != null) return shared;
        var own = anyModel != null ? anyModel.GetComponentInChildren<MeshRenderer>()?.sharedMaterial : null;
        if (own == null) return null;
        shared = new Material(own) { name = "BAY_HousePalette_Shared", enableInstancing = true };
        AssetDatabase.CreateAsset(shared, SharedMaterialPath);
        return shared;
    }

    // ------------------------------------------------------------------ the build

    public static string Build(Scene scene, BarangayMockSettings settings)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BarangayMock");
        if (root == null) return "Rows: no map in the scene.";
        var lib = settings.library;
        lib.MeasureAll();
        var models = Models(lib);
        if (models.Count == 0) return "Rows: the placeholder library has no BAY_ models.";
        var buildings = root.transform.Find("Buildings");

        // The one-house-per-outline houses go: the rows take their place.
        int removedOld = 0;
        if (buildings != null)
            for (int i = buildings.childCount - 1; i >= 0; i--)
            {
                var t = buildings.GetChild(i);
                if (t.name.StartsWith("House ")) { Object.DestroyImmediate(t.gameObject); removedOld++; }
            }

        var layout = settings.Layout;
        var region = settings.Region(layout);
        var mock = BarangayMockPlanner.Plan(layout, region, lib);
        var g = new Grid(region);
        var net = RoadNetwork.Build(layout.roads, k => lib.roadWidths.For(k));
        var zones = KeepOff(g, layout, lib, mock, net);

        var random = new System.Random(Seed);
        float R() => (float)random.NextDouble();
        bool Corner(Vector2 p) => net.junctions.Any(j => j.arms.Count >= 3 && Vector2.Distance(j.centre, p) < j.radius + 8f);

        var units = new List<Unit>();
        var counts = new int[4];
        var hoods = new Dictionary<Vector2Int, int[]>();
        var threes = new List<Vector2>();
        Vector2Int Hood(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 80f), Mathf.FloorToInt(p.y / 80f));

        // Which road a point belongs to: the nearest one by its edge.
        (MapRoad road, float edge) Nearest(Vector2 p)
        {
            MapRoad best = null; float bd = float.MaxValue;
            foreach (var r in layout.roads)
            {
                if (r.points.Count < 2) continue;
                float d = MapGeometry.DistanceToPolyline(r.points, p, out _) - lib.roadWidths.For(r.roadClass) * 0.5f;
                if (d < bd) { bd = d; best = r; }
            }
            return (best, bd);
        }

        (MapRoad road, Vector2 toRoad) NearestWithDir(Vector2 p)
        {
            MapRoad best = null; float bd = float.MaxValue; Vector2 dir = Vector2.up;
            foreach (var r in layout.roads)
            {
                if (r.points.Count < 2) continue;
                float d = MapGeometry.DistanceToPolyline(r.points, p, out Vector2 c, out int seg) - lib.roadWidths.For(r.roadClass) * 0.5f;
                if (d >= bd) continue;
                bd = d; best = r;
                // Square to the street: its normal on this side.
                var along = (r.points[seg + 1] - r.points[seg]).normalized;
                var nrm = MapGeometry.Left(along);
                dir = Vector2.Dot(nrm, c - p) >= 0f ? nrm : -nrm;
            }
            return (best, dir);
        }

        int Storeys(Vector2 at, bool main, int tallRun)
        {
            var hood = Hood(at);
            if (!hoods.TryGetValue(hood, out var lc)) hoods[hood] = lc = new int[4];
            int total = counts[1] + counts[2] + counts[3], local = lc[1] + lc[2] + lc[3];
            int best = 1; float bestScore = float.MinValue;
            for (int s = 1; s <= 3; s++)
            {
                if (s > 1 && tallRun >= MaxTallRun) continue;
                if (s == 3 && threes.Any(p => (p - at).sqrMagnitude < ThreeStoreySpacing * ThreeStoreySpacing)) continue;
                float want = Target[s] * (main && s > 1 ? 2f : 1f);
                float score = want * (total + 1) - counts[s] + 0.6f * (want * (local + 1) - lc[s]) + R() * 0.3f;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        // The unit just placed on the same walk is ignored: the next one stands flush against it, wall to wall, which the
        // half-metre grid would otherwise push a cell along.
        bool Fits(Vector2 centre, Vector2 size, float yaw, int ignore = -99)
        {
            foreach (var p in RectCells(centre, size * 0.5f - Vector2.one * 0.05f, yaw))
                if (!g.Free(p, ignore)) return false;
            return true;
        }
        void Take(Vector2 centre, Vector2 size, float yaw, int owner)
        {
            foreach (var p in RectCells(centre, size * 0.5f, yaw)) if (g.Index(p, out int i) && g.cell[i] == 0) g.cell[i] = owner;
        }

        Unit TryPlace(Walk w, Vector2 at, Vector2 dir, float depthFrom)
        {
            var n = MapGeometry.Left(dir) * w.side;                 // away from the road, into the block
            bool main = w.road.roadClass >= RoadClass.Tertiary && w.row == 0;
            bool facesRoad = w.row % 2 == 0;
            var front = facesRoad ? -n : n;
            bool corner = w.row == 0 && Corner(at);
            int storeys = Storeys(at, main, w.tallRun);
            bool commercial = main;
            var tries = new List<(int s, bool c)> { (storeys, commercial) };
            for (int s = 1; s <= 3; s++) if (s != storeys && (s == 1 || w.tallRun < MaxTallRun)) tries.Add((s, commercial));
            foreach (var (s, c) in tries)
            {
                IEnumerable<Model> pool = models.Where(m => m.storeys == s);
                // Homes take the residential pack; it has no three-storey model, so a three-storey home is the
                // commercial pack's plain ThreeStorey (never its Corner, which stays on the main roads' corners).
                if (c) pool = pool.Where(m => m.commercial && (m.family != "F_Corner" || corner));
                else pool = pool.Where(m => !m.commercial || (s == 3 && m.family == "F_ThreeStorey"));
                if (!c && corner && s == 1) pool = pool.OrderByDescending(m => m.family == "I_SariSari" ? 1 : 0).ThenBy(_ => random.Next());
                else pool = pool.OrderBy(_ => random.Next());
                foreach (var m in pool)
                {
                    if (w.recent.Contains(m.Name)) continue;
                    // The walk point is where this unit starts along the street (the last one's end); a row facing the
                    // road has its front on depthFrom, a back-to-back row its back.
                    var centre = at + dir * (m.footprint.x * 0.5f) + n * (depthFrom + m.footprint.y * 0.5f);
                    float yaw = MapGeometry.Heading(front);
                    // Flush against the last unit only on a straight run: where the street turns, their inner corners would meet.
                    int ignore = Vector2.Angle(dir, w.lastDir) < 4f ? w.last : -99;
                    if (!region.Contains(centre) || !Fits(centre, m.footprint, yaw, ignore)) continue;
                    if (Nearest(centre).road != w.road && w.row == 0) continue;
                    var u = new Unit { id = units.Count + 1, model = m, centre = centre, front = front, mirror = false, road = w.road, main = main, corner = corner, row = w.row };
                    Take(centre, m.footprint, yaw, u.id + 1);
                    w.last = u.id + 1;
                    w.lastDir = dir;
                    units.Add(u);
                    counts[s]++;
                    hoods[Hood(at)][s]++;
                    if (s == 3) threes.Add(centre);
                    w.tallRun = s > 1 ? w.tallRun + 1 : 0;
                    w.recent.Enqueue(m.Name);
                    while (w.recent.Count > NoRepeatWithin) w.recent.Dequeue();
                    return u;
                }
            }
            return null;
        }

        // How deep into the block the next row starts at a point: the first free cell outward from the road.
        float DepthAt(Vector2 at, Vector2 n, float from)
        {
            for (float d = from; d < from + 40f; d += Cell)
                if (g.Free(at + n * d)) return d;
            return -1f;
        }

        var roads = layout.roads.Where(r => r.points.Count >= 2 && r.points.Any(p => region.Contains(p)))
                                .OrderByDescending(r => (int)r.roadClass).ThenBy(r => r.osmId, System.StringComparer.Ordinal).ToList();
        var walks = new List<Walk>();
        foreach (var r in roads)
            foreach (float side in new[] { 1f, -1f })
                walks.Add(new Walk { road = r, side = side, row = 0, offset = lib.roadWidths.For(r.roadClass) * 0.5f });

        int frontage = 0;
        for (int row = 0; row < MaxRows; row++)
        {
            int placedThisRow = 0;
            foreach (var w in walks)
            {
                w.row = row; w.recent.Clear(); w.tallRun = 0; w.last = -99;
                float setback = SetbackMin + R() * (SetbackMax - SetbackMin);
                WalkLine(w.road.points, 0.25f, (at, dir) =>
                {
                    var n = MapGeometry.Left(dir) * w.side;
                    float depth;
                    if (row == 0) depth = w.offset + setback;
                    else
                    {
                        // The back (or front) of what is already there, then an alley before every row that faces the road.
                        depth = DepthAt(at, n, w.offset);
                        if (depth < 0f) return Cell;
                        if (row % 2 == 0) depth += Alley;
                    }
                    var u = TryPlace(w, at, dir, depth);
                    if (u == null) { w.last = -99; return Cell; }
                    placedThisRow++;
                    return u.model.footprint.x;
                });
            }
            if (row == 0) frontage = placedThisRow;
            if (placedThisRow == 0) break;
        }

        // ---- the holes left where ragged rows meet: packed with small one-storey homes facing the nearest street
        int filled = 0;
        var nearCache = new Dictionary<Vector2Int, (MapRoad road, Vector2 toRoad)>();
        var smalls = models.Where(m => m.storeys == 1 && !m.commercial).OrderBy(m => m.footprint.x * m.footprint.y).ToList();
        var homes = models.Where(m => !m.commercial || m.family == "F_ThreeStorey").ToList();
        for (float y = region.yMin + 2f; y < region.yMax - 2f; y += 1f)
        for (float x = region.xMin + 2f; x < region.xMax - 2f; x += 1f)
        {
            var p = new Vector2(x, y);
            if (!g.Free(p)) continue;
            // Room for even the smallest home round here (a 3 m square), else nothing to look up.
            if (!g.Free(p + new Vector2(1.5f, 0f)) || !g.Free(p - new Vector2(1.5f, 0f)) || !g.Free(p + new Vector2(0f, 1.5f)) || !g.Free(p - new Vector2(0f, 1.5f))) continue;
            var key = new Vector2Int(Mathf.FloorToInt(x / 4f), Mathf.FloorToInt(y / 4f));
            if (!nearCache.TryGetValue(key, out var near)) nearCache[key] = near = NearestWithDir(new Vector2((key.x + 0.5f) * 4f, (key.y + 0.5f) * 4f));
            if (near.road == null) continue;
            var front = near.toRoad;
            float yaw = MapGeometry.Heading(front);
            // The same storey mix as the rows; within a height, the smaller homes first so they fit the holes.
            int want = Storeys(p, false, 0);
            var pool = homes.Where(m => m.storeys == want).OrderBy(m => m.footprint.x * m.footprint.y + R() * 6f).Take(12)
                            .Concat(smalls.OrderBy(_ => random.Next()).Take(6));
            foreach (var m in pool)
            {
                var centre = p - front * (m.footprint.y * 0.5f - 0.5f);
                if (!region.Contains(centre) || !Fits(centre, m.footprint, yaw)) continue;
                if (m.storeys == 3 && threes.Any(q => (q - centre).sqrMagnitude < ThreeStoreySpacing * ThreeStoreySpacing)) continue;
                var u = new Unit { id = units.Count + 1, model = m, centre = centre, front = front, mirror = false, road = near.road, row = 99 };
                Take(centre, m.footprint, yaw, u.id + 1);
                units.Add(u); counts[m.storeys]++; filled++;
                var hood = Hood(p); if (!hoods.TryGetValue(hood, out var lc)) hoods[hood] = lc = new int[4]; lc[m.storeys]++;
                if (m.storeys == 3) threes.Add(centre);
                break;
            }
        }

        // ---- the mix: the deeper two- and three-storey models often did not fit where they were wanted, so one-storey
        // units are swapped up where a taller model fits the same spot (same front line), main-road fronts first.
        int swapped = 0;
        int Count(int st) => units.Count(u => u.model.storeys == st);
        foreach (int up in new[] { 3, 2 })
        {
            float goal = up == 3 ? 0.06f : 0.18f;
            var candidates = units.Where(u => u.model.storeys == 1).OrderByDescending(u => u.main ? 1 : 0).ThenBy(_ => random.Next()).ToList();
            foreach (var u in candidates)
            {
                if (Count(up) >= goal * units.Count) break;
                if (up == 3 && threes.Any(q => (q - u.centre).sqrMagnitude < ThreeStoreySpacing * ThreeStoreySpacing)) continue;
                var pool = models.Where(m => m.storeys == up && (u.model.commercial ? m.commercial && m.family != "F_Corner" : (!m.commercial || m.family == "F_ThreeStorey")))
                                 .OrderBy(_ => random.Next()).Take(10);
                float yaw = u.Yaw;
                bool done = false;
                foreach (var m in pool)
                {
                    foreach (float squash in new[] { 1f, 0.9f, 0.8f })
                    {
                        var size = new Vector2(m.footprint.x, m.footprint.y * squash);
                        var centre = u.centre - u.front * ((size.y - u.model.footprint.y) * 0.5f);
                        if (!Fits(centre, size, yaw, u.id + 1)) continue;
                        foreach (var q in RectCells(u.centre, u.model.footprint * 0.5f, yaw)) if (g.Index(q, out int i) && g.cell[i] == u.id + 1) g.cell[i] = 0;
                        Take(centre, size, yaw, u.id + 1);
                        u.model = m; u.centre = centre; u.depthScale = squash;
                        if (up == 3) threes.Add(centre);
                        swapped++;
                        done = true;
                        break;
                    }
                    if (done) break;
                }
            }
        }

        // ---- the scene
        var parent = buildings != null ? buildings : root.transform;
        var surface = settings.colliders ? BarangayMockBuilder.SlipperySurface() : null;
        foreach (var u in units) Place(u, parent, surface, settings.colliders);
        int tidied = Tidy(units);

        int n1 = units.Count(u => u.model.storeys == 1), n2 = units.Count(u => u.model.storeys == 2), n3 = units.Count(u => u.model.storeys == 3);
        int nc = units.Count(u => u.model.commercial);
        string summary = "Rows: " + units.Count + " units (" + Pct(n1, units.Count) + " one-storey, " + Pct(n2, units.Count) + " two, " + Pct(n3, units.Count) +
                         " three; " + Pct(nc, units.Count) + " commercial pack; " + frontage + " on the street fronts, " + filled + " filling the holes, " + swapped + " raised a storey or two for the mix, " + tidied + " dropped by the final check), replacing " + removedOld + " houses.";
        WriteReport(units, zones, summary);
        return summary;
    }

    /// <summary>
    /// The last word, on what was actually placed: a unit standing more than 30 cm inside another (the half-metre grid
    /// lets the odd one through at a bend), or with a corner over a street's surface, is taken out. The same checks as
    /// MabuhayRowsTests.
    /// </summary>
    static int Tidy(List<Unit> units)
    {
        Physics.SyncTransforms();
        var boxes = new List<(Unit u, Vector2 c, Vector2 h, Vector2 r, Vector2 f)>();
        foreach (var u in units)
        {
            var mc = u.placed != null ? u.placed.GetComponentInChildren<MeshCollider>() : null;
            if (mc == null || mc.sharedMesh == null) continue;
            var b = mc.sharedMesh.bounds; var t = mc.transform;
            var c3 = t.TransformPoint(b.center);
            var r3 = t.TransformVector(new Vector3(b.extents.x, 0f, 0f)); var f3 = t.TransformVector(new Vector3(0f, 0f, b.extents.z));
            boxes.Add((u, new Vector2(c3.x, c3.z), new Vector2(new Vector2(r3.x, r3.z).magnitude, new Vector2(f3.x, f3.z).magnitude),
                       new Vector2(r3.x, r3.z).normalized, new Vector2(f3.x, f3.z).normalized));
        }
        var drop = new HashSet<Unit>();
        // On a street: a corner (30 cm in) over a street surface.
        foreach (var x in boxes)
            foreach (var s in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
            {
                var p = x.c + x.r * s.x * (x.h.x - 0.3f) + x.f * s.y * (x.h.y - 0.3f);
                if (Physics.RaycastAll(new Vector3(p.x, 60f, p.y), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore).Any(h => h.collider.name.StartsWith("road_")))
                { drop.Add(x.u); break; }
            }
        // Inside another: the later one goes.
        var cells = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < boxes.Count; i++)
        {
            var k = new Vector2Int(Mathf.FloorToInt(boxes[i].c.x / 10f), Mathf.FloorToInt(boxes[i].c.y / 10f));
            if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
            l.Add(i);
        }
        bool Overlap(int i, int j)
        {
            var a = boxes[i]; var b = boxes[j];
            Vector2 ha = a.h - Vector2.one * 0.3f, hb = b.h - Vector2.one * 0.3f;
            foreach (var axis in new[] { a.r, a.f, b.r, b.f })
            {
                float ra = ha.x * Mathf.Abs(Vector2.Dot(a.r, axis)) + ha.y * Mathf.Abs(Vector2.Dot(a.f, axis));
                float rb = hb.x * Mathf.Abs(Vector2.Dot(b.r, axis)) + hb.y * Mathf.Abs(Vector2.Dot(b.f, axis));
                if (Mathf.Abs(Vector2.Dot(b.c - a.c, axis)) > ra + rb) return false;
            }
            return true;
        }
        for (int i = 0; i < boxes.Count; i++)
        {
            if (drop.Contains(boxes[i].u)) continue;
            var k = new Vector2Int(Mathf.FloorToInt(boxes[i].c.x / 10f), Mathf.FloorToInt(boxes[i].c.y / 10f));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (cells.TryGetValue(k + new Vector2Int(dx, dy), out var l))
                    foreach (int j in l)
                        if (j > i && !drop.Contains(boxes[j].u) && Overlap(i, j)) drop.Add(boxes[j].u);
        }
        foreach (var u in drop) { Object.DestroyImmediate(u.placed.gameObject); u.placed = null; }
        units.RemoveAll(u => drop.Contains(u));
        return drop.Count;
    }

    static string Pct(int n, int of) => n + " (" + (of == 0 ? 0f : 100f * n / of).ToString("0.0") + " %)";

    static void Place(Unit u, Transform parent, PhysicsMaterial surface, bool colliders)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(u.model.variant.prefab, parent);
        go.name = NamePrefix + u.id.ToString("0000") + " (" + u.model.Name + ")";
        // The plan is for the drawn footprint; the pivot goes where that puts it.
        var turn = Quaternion.Euler(0f, u.Yaw, 0f);
        var off = turn * new Vector3(u.model.offset.x, 0f, u.model.offset.y * u.depthScale);
        go.transform.localPosition = new Vector3(u.centre.x - off.x, 0f, u.centre.y - off.z);
        go.transform.localRotation = turn;
        go.transform.localScale = new Vector3(u.mirror ? -1f : 1f, 1f, u.depthScale);
        if (colliders) BarangayMockBuilder.SolidWhereDrawn(go, surface);
        // Every model of both packs carries its own copy of one material (the same shader and the same palette texture,
        // checked pixel for pixel): drawn with one shared copy, static batching merges whole stretches of rows.
        var shared = SharedMaterial(u.model.variant.prefab);
        if (shared != null)
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(_ => shared).ToArray();
        // Not drawn once it is smaller than this share of the screen's height: thousands of houses, most of them far off in
        // the storm's haze. Its collider stays.
        var lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(CullBelowScreenHeight, go.GetComponentsInChildren<Renderer>()) });
        lod.RecalculateBounds();
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        u.placed = go.transform;
    }

    /// <summary>Takes a row unit out, with the props that stand on it or lean on it (buckets, roof sheets, block stacks).</summary>
    static void Remove(Transform unit, Transform map)
    {
        var rs = unit.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(1.6f, 2f, 1.6f));
            var props = map.Find(PropBuilder.GroupName);
            if (props != null)
                foreach (var t in props.GetComponentsInChildren<Transform>(true).Where(t => t != props && t.parent != null && t.parent.parent == props).ToList())
                    if (b.Contains(t.position)) Object.DestroyImmediate(t.gameObject);
        }
        Object.DestroyImmediate(unit.gameObject);
    }

    /// <summary>The storey mix of the row units now in the scene, measured from their models.</summary>
    public static string Census(Scene scene, BarangayMockSettings settings)
    {
        var byName = Models(settings.library).ToDictionary(m => m.Name, m => m);
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BarangayMock");
        if (root == null) return "";
        var counts = new int[4]; int commercial = 0, total = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith(NamePrefix)) continue;
            int open = t.name.LastIndexOf('('), close = t.name.LastIndexOf(')');
            if (open < 0 || close <= open || !byName.TryGetValue(t.name.Substring(open + 1, close - open - 1), out var m)) continue;
            counts[m.storeys]++; total++;
            if (m.commercial) commercial++;
        }
        return "Rows now: " + total + " units, " + Pct(counts[1], total) + " one-storey, " + Pct(counts[2], total) + " two, " + Pct(counts[3], total) + " three; " +
               Pct(commercial, total) + " commercial pack.";
    }

    // ------------------------------------------------------------------ keeping the boat's ways open

    /// <summary>
    /// Run once the mission is laid out: floods the water from the boat and makes sure it still reaches every resident,
    /// the hall and the open ground of the landmark lots and walled compounds as well as it did with no rows at all. Where
    /// it falls short, the row units nearest are taken out (never one a resident stands on) and the water flooded again.
    /// </summary>
    public static int KeepReach(Scene scene, BarangayMockSettings settings)
    {
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BarangayMock");
        if (boat == null || root == null) return 0;
        var rows = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(NamePrefix)).ToList();
        if (rows.Count == 0) return 0;
        float scale = Mathf.Max(0.05f, settings.scale);
        var area = MabuhayLevelBuilder.Scaled(settings.Region(settings.Layout), scale);
        area = Rect.MinMaxRect(area.xMin - 20f, area.yMin - 20f, area.xMax + 20f, area.yMax + 20f);

        // The boat must reach the hall; the residents are judged by a thrown line below (some are meant to be cut off from
        // the boat, behind the jam, and must stay so).
        var points = new List<Vector2>();
        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        if (evac != null) points.Add(new Vector2(evac.transform.position.x, evac.transform.position.z));

        // A unit somebody would be inside (a post on the kerb, someone in the water by a wall) goes; one somebody stands
        // on (a roof) stays.
        int removed = 0;
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name != MabuhayLevelBuilder.AnchorsName && go.name != MabuhayLevelBuilder.PostsName) continue;
            foreach (Transform a in go.transform)
                foreach (var unit in rows.Where(u => u != null).ToList())
                {
                    var rs = unit.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) continue;
                    var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                    bb.Expand(new Vector3(1.2f, 0f, 1.2f));
                    var p = a.position;
                    if (p.x < bb.min.x || p.x > bb.max.x || p.z < bb.min.z || p.z > bb.max.z) continue;
                    if (p.y > 1f) continue;                       // up on something (a roof), not down in the water
                    rows.Remove(unit);
                    Remove(unit, root.transform);
                    removed++;
                }
        }

        // A unit somebody stands on stays, and so do the houses either side of the jam: they are its walls, and opening
        // one would give the boat a way round it.
        var keep = new HashSet<Transform>();
        var jams = Object.FindObjectsByType<DebrisBarrier>(FindObjectsSortMode.None).Select(j => new Vector2(j.transform.position.x, j.transform.position.z)).ToList();
        foreach (var unit in rows)
            if (unit != null && jams.Any(j => Vector2.Distance(j, new Vector2(unit.position.x, unit.position.z)) < 25f)) keep.Add(unit);
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name != MabuhayLevelBuilder.AnchorsName) continue;
            foreach (Transform a in go.transform)
                if (Physics.Raycast(a.position + Vector3.up * 0.5f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var unit = rows.FirstOrDefault(r => hit.collider.transform.IsChildOf(r));
                    if (unit != null) keep.Add(unit);
                }
        }

        var lotsToKeep = new List<Bounds>();
        foreach (var holder in new[] { root.transform.Find("Landmarks"), root.transform.Find(CompoundWallBuilder.GroupName) })
            if (holder != null)
                foreach (Transform t in holder)
                {
                    var rs = t.GetComponentsInChildren<MeshRenderer>();
                    if (rs.Length == 0) continue;
                    var bb = rs[0].bounds;
                    foreach (var r in rs) bb.Encapsulate(r.bounds);
                    if (bb.size.x * bb.size.z > 400f) lotsToKeep.Add(bb);
                }

        BoatReach Flood()
        {
            var water = new BoatReach(area, 1f, BoatReach.DefaultRadius, 0f);
            Physics.SyncTransforms();
            water.Flood(boat.transform.position);
            return water;
        }
        (int roomy, int reached) Measure(BoatReach water, Bounds b)
        {
            int roomy = 0, reached = 0;
            for (float x = b.min.x + 1f; x < b.max.x; x += 2f)
            for (float z = b.min.z + 1f; z < b.max.z; z += 2f)
            {
                var above = new Vector3(x, b.max.y + 1f, z);
                if (Physics.Raycast(above, Vector3.down, above.y - 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var xz = new Vector2(x, z);
                if (!water.IsClear(xz)) continue;
                roomy++;
                if (water.Reached(xz)) reached++;
            }
            return (roomy, reached);
        }

        // What the boat reached with no rows at all.
        foreach (var r in rows) r.gameObject.SetActive(false);
        var before = Flood();
        var wantPoints = points.Where(t => before.Reached(t) || before.DistanceToReached(t, 10f) <= 8f).ToList();
        // Residents: whoever a thrown line could reach (range and plain view) with no rows must still be reachable.
        var residents = new List<Vector3>();
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == MabuhayLevelBuilder.AnchorsName) foreach (Transform t in go.transform) residents.Add(t.position);
        var wantThrow = residents.Where(r => MabuhayLevelBuilder.ThrowableFrom(before, r, out _)).ToList();
        var wantLots = new List<(Bounds b, float share)>();
        foreach (var b in lotsToKeep)
        {
            var (roomy, reached) = Measure(before, b);
            if (roomy >= 30) wantLots.Add((b, Mathf.Min(0.93f, (float)reached / roomy - 0.01f)));
        }
        foreach (var r in rows) r.gameObject.SetActive(true);

        var alive = new List<Transform>(rows);
        for (int pass = 0; pass < 12; pass++)
        {
            var water = Flood();
            var lost = new List<(Vector2 at, float reach)>();
            foreach (var t in wantPoints) if (!water.Reached(t) && water.DistanceToReached(t, 10f) > 8f) lost.Add((t, 0f));
            // With a margin: a throw at the very end of the line's range (11.5 m) is too close to call in play.
            foreach (var r in wantThrow) if (!MabuhayLevelBuilder.ThrowableFrom(water, r, out float far) || far > 9.5f) lost.Add((new Vector2(r.x, r.z), 0f));
            foreach (var (b, share) in wantLots)
            {
                var (roomy, reached) = Measure(water, b);
                if (roomy > 0 && (float)reached / roomy < share) lost.Add((new Vector2(b.center.x, b.center.z), Mathf.Max(b.extents.x, b.extents.z)));
            }
            if (lost.Count == 0) break;
            foreach (var (at, reach) in lost)
                foreach (var unit in alive.Where(u => u != null && !keep.Contains(u))
                                          .OrderBy(u => Mathf.Max(0f, Vector2.Distance(new Vector2(u.position.x, u.position.z), at) - reach)).Take(6).ToList())
                {
                    alive.Remove(unit);
                    Remove(unit, root.transform);
                    removed++;
                }
        }
        return removed;
    }

    // ------------------------------------------------------------------ where rows may not go

    static List<(string name, Rect bounds)> KeepOff(Grid g, BarangayLayout layout, PlaceholderLibrary lib, MockPlan mock, RoadNetwork net)
    {
        void Fill(IList<Vector2> ring, float grow)
        {
            if (ring == null || ring.Count < 3) return;
            var b = MapGeometry.BoundsOf(ring);
            var closed = new List<Vector2>(ring) { ring[0] };
            for (float x = b.xMin - grow; x <= b.xMax + grow; x += Cell)
            for (float y = b.yMin - grow; y <= b.yMax + grow; y += Cell)
            {
                var p = new Vector2(x, y);
                if (!g.Index(p, out int i) || g.cell[i] != 0) continue;
                if (MapGeometry.Contains(ring, p) || (grow > 0f && MapGeometry.DistanceToPolyline(closed, p, out _) <= grow)) g.cell[i] = 1;
            }
        }
        void Line(List<Vector2> line, float half)
        {
            var b = MapGeometry.BoundsOf(line);
            for (float x = b.xMin - half; x <= b.xMax + half; x += Cell)
            for (float y = b.yMin - half; y <= b.yMax + half; y += Cell)
            {
                var p = new Vector2(x, y);
                if (g.Index(p, out int i) && g.cell[i] == 0 && MapGeometry.DistanceToPolyline(line, p, out _) <= half) g.cell[i] = 1;
            }
        }

        var zones = new List<(string, Rect)>();
        foreach (var (name, ids) in InfillBuilder.Protected)
        {
            var rings = layout.areas.Where(a => ids.Contains(a.osmId)).Select(a => a.outline)
                                    .Concat(layout.landmarks.Where(l => ids.Contains(l.id)).Select(l => l.footprint))
                                    .Where(r => r != null && r.Count >= 3).ToList();
            foreach (var ring in rings) Fill(ring, ProtectedBuffer);
            if (rings.Count > 0) zones.Add((name, MapGeometry.BoundsOf(rings.SelectMany(r => r).ToList())));
        }
        foreach (var l in layout.landmarks) Fill(l.footprint, 1f);
        // Walled compounds: commercial and industrial lots, schools, churches, the hospital, water and cemeteries.
        foreach (var a in layout.areas)
            if (a.kind == AreaKind.Commercial || a.kind == AreaKind.Industrial || a.kind == AreaKind.Institutional || a.kind == AreaKind.Water || a.kind == AreaKind.Cemetery)
                Fill(a.outline, 1f);
        // What the map keeps: landmark models, the plain blocks of factories, schools and the like, courts, signs.
        foreach (var p in mock.placements)
        {
            if (p.kind == PlacementKind.House) continue;
            if (p.outline != null && p.outline.Count >= 3) Fill(p.outline, 1f);
            else if (p.kind == PlacementKind.Model)
            {
                var half = new Vector2(p.modelSize.x * p.scale.x, p.modelSize.z * p.scale.z) * 0.5f + Vector2.one;
                foreach (var c in RectCells(p.position, half, p.yaw)) if (g.Index(c, out int i)) g.cell[i] = 1;
            }
            else if (p.kind == PlacementKind.Sign || p.kind == PlacementKind.Banner)
                foreach (var c in RectCells(p.position, Vector2.one * 1.5f, 0f)) if (g.Index(c, out int i)) g.cell[i] = 1;
        }
        foreach (var r in layout.roads) if (r.points.Count >= 2) Line(r.points, lib.roadWidths.For(r.roadClass) * 0.5f + 0.1f);
        // The paved patch at every junction reaches past the streets' own edges at the corners.
        foreach (var j in net.junctions) if (j.pad != null && j.pad.Count >= 3) Fill(j.pad, 0.3f);
        foreach (var w in layout.waterways) if (w.points.Count >= 2) Line(w.points, lib.WaterWidth(w.kind) * 0.5f + 2f);
        return zones;
    }

    /// <summary>Cell centres inside a turned rectangle (centre, half size along its own x and z, yaw in degrees).</summary>
    static IEnumerable<Vector2> RectCells(Vector2 c, Vector2 half, float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        var fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        var right = new Vector2(fwd.y, -fwd.x);
        float step = Cell * 0.7f;
        for (float a = -half.x; a <= half.x + 1e-3f; a += step)
        for (float b = -half.y; b <= half.y + 1e-3f; b += step)
            yield return c + right * a + fwd * b;
    }

    /// <summary>Walks a line; <paramref name="each"/> is given the point and direction and returns how far to go next.</summary>
    static void WalkLine(List<Vector2> line, float first, System.Func<Vector2, Vector2, float> each)
    {
        var lengths = new List<float> { 0f };
        for (int i = 1; i < line.Count; i++) lengths.Add(lengths[i - 1] + Vector2.Distance(line[i - 1], line[i]));
        float total = lengths[lengths.Count - 1];
        for (float s = first; s < total; )
        {
            int seg = 1;
            while (seg < line.Count - 1 && lengths[seg] < s) seg++;
            Vector2 a = line[seg - 1], b = line[seg];
            float t = Mathf.InverseLerp(lengths[seg - 1], lengths[seg], s);
            s += Mathf.Max(Cell * 0.5f, each(Vector2.Lerp(a, b, t), (b - a).normalized));
        }
    }

    // ------------------------------------------------------------------ the report

    const string ReportPath = "../Source Assets/Houses/Rows_Report.md";

    static void WriteReport(List<Unit> units, List<(string name, Rect bounds)> zones, string summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Rows report: Barangay Mabuhay");
        sb.AppendLine();
        sb.AppendLine("Written by RowBuilder (Bayanihan > Build Mabuhay Gameplay Scene). Seed " + Seed + ".");
        sb.AppendLine();
        sb.AppendLine(summary);
        sb.AppendLine();
        sb.AppendLine("## Storeys (target 75 / 18 / 7)");
        sb.AppendLine();
        sb.AppendLine("| Storeys | Count | Share | Target |");
        sb.AppendLine("|---|---|---|---|");
        for (int s = 1; s <= 3; s++)
        {
            int n = units.Count(u => u.model.storeys == s);
            sb.AppendLine("| " + s + " | " + n + " | " + (100f * n / Mathf.Max(1, units.Count)).ToString("0.0") + " % | " + (Target[s] * 100f).ToString("0") + " % |");
        }
        sb.AppendLine();
        sb.AppendLine("## Models used");
        sb.AppendLine();
        sb.AppendLine("| Family | Units | Variants used |");
        sb.AppendLine("|---|---|---|");
        foreach (var f in units.GroupBy(u => u.model.family).OrderBy(f => f.Key))
            sb.AppendLine("| " + f.Key + " | " + f.Count() + " | " + f.Select(u => u.model.Name).Distinct().Count() + " |");
        sb.AppendLine();
        sb.AppendLine("## Kept as they are");
        sb.AppendLine();
        foreach (var z in zones) sb.AppendLine("- " + z.name + " (plus " + ProtectedBuffer + " m)");
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }
}
