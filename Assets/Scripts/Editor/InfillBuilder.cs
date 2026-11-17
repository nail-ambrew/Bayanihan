using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fills the empty land of the Mabuhay map with the building kit (<see cref="BuildingLibrary"/>): houses, row houses and
/// apartments in the inner blocks and side streets, shops and mixed-use units along the main roads and on corners.
///
/// Works on a one-metre grid of the layout (real metres, under the map's root, so the buildings are drawn at the map's scale
/// like its other houses). Off limits: every building, landmark and walled compound already on the map, the streets with a
/// setback, waterways, the mission's places (boat, hall, residents, hazards), and the two protected zones - WalterMart and
/// Macaria Business Center - with a 5 m buffer. Lots are laid along every street, front toward the street, then the inner
/// land is filled facing whichever street is nearest. Storeys keep to 70/20/10 over the map and in each 80 m
/// neighbourhood; about 30 % are commercial. Each lot is checked against what is solid in the scene (poles, walls,
/// houses), and afterwards the boat must still reach every resident, the hall and each walled compound's grounds; where it
/// cannot, the nearest new buildings are taken out again. Same seed, same town.
///
/// Writes Placement.json and Infill_Report.md to Source Assets/Buildings. Under BarangayMock/Infill Buildings.
/// </summary>
public static class InfillBuilder
{
    public const string GroupName = "Infill Buildings";
    const string MapRootName = "BarangayMock";
    const float Cell = 1f;

    /// <summary>The protected zones, by their OpenStreetMap ids: kept exactly as they are.</summary>
    public static readonly (string name, string[] ids)[] Protected =
    {
        ("WalterMart Carmona", new[] { "r10749166", "r10977833" }),
        ("Macaria Business Center", new[] { "r10749164" }),
    };

    sealed class Lot
    {
        public int id;
        public InfillModel model;
        public Vector2 centre;      // layout metres
        public Vector2 front;       // unit, toward the street
        public float scale;
        public string road;
        public bool corner;
        public Vector2 Size => model.footprint * scale;
        public float Yaw => MapGeometry.Heading(front);
        public Transform placed;
    }

    sealed class Grid
    {
        public Rect area; public int w, h; public byte[] cell;          // 0 free, 1 taken
        public Grid(Rect a) { area = a; w = Mathf.CeilToInt(a.width / Cell); h = Mathf.CeilToInt(a.height / Cell); cell = new byte[w * h]; }
        public bool Index(Vector2 p, out int i)
        {
            int x = Mathf.FloorToInt((p.x - area.xMin) / Cell), y = Mathf.FloorToInt((p.y - area.yMin) / Cell);
            i = y * w + x;
            return x >= 0 && y >= 0 && x < w && y < h;
        }
        public Vector2 Centre(int x, int y) => new Vector2(area.xMin + (x + 0.5f) * Cell, area.yMin + (y + 0.5f) * Cell);
    }

    // ------------------------------------------------------------------ the build

    public static string Build(Scene scene, BarangayMockSettings settings, IList<KeepClear> keepClear)
    {
        var lib = BuildingKitImporter.LoadLibrary();
        if (lib == null || lib.models.Count == 0) return "Infill: no building kit yet (Bayanihan > Buildings > Import Building Kit).";
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (root == null) return "Infill: no map in the scene.";
        var old = root.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        var layout = settings.Layout;
        var plib = settings.library;
        float scale = Mathf.Max(0.05f, settings.scale);
        var region = settings.Region(layout);
        var mock = BarangayMockPlanner.Plan(layout, region, plib);
        var net = RoadNetwork.Build(layout.roads, k => plib.roadWidths.For(k));
        var random = new System.Random(lib.seed);
        float R() => (float)random.NextDouble();

        // ---- the grid: what may not be built on
        var g = new Grid(region);
        void Fill(IList<Vector2> ring, float grow = 0f)
        {
            if (ring == null || ring.Count < 3) return;
            var b = MapGeometry.BoundsOf(ring);
            for (float x = b.xMin - grow; x <= b.xMax + grow; x += Cell)
            for (float y = b.yMin - grow; y <= b.yMax + grow; y += Cell)
            {
                var p = new Vector2(x, y);
                if (!g.Index(p, out int i)) continue;
                if (MapGeometry.Contains(ring, p) || (grow > 0f && MapGeometry.DistanceToPolyline(Closed(ring), p, out _) <= grow)) g.cell[i] = 1;
            }
        }
        void Disc(Vector2 c, float r) { for (float x = -r; x <= r; x += Cell) for (float y = -r; y <= r; y += Cell) if (x * x + y * y <= r * r && g.Index(c + new Vector2(x, y), out int i)) g.cell[i] = 1; }
        void Box(Vector2 c, Vector2 half, float yawDeg, float grow) { foreach (var p in RectCells(c, half + Vector2.one * grow, yawDeg)) if (g.Index(p, out int i)) g.cell[i] = 1; }

        // Protected zones, with their buffer: recorded for the report.
        var zones = new List<(string name, List<Vector2> ring, Rect bounds)>();
        foreach (var (name, ids) in Protected)
        {
            var rings = layout.areas.Where(a => ids.Contains(a.osmId)).Select(a => a.outline)
                                    .Concat(layout.landmarks.Where(l => ids.Contains(l.id)).Select(l => l.footprint))
                                    .Where(r => r != null && r.Count >= 3).ToList();
            foreach (var ring in rings) { Fill(ring, lib.protectedBuffer); }
            if (rings.Count > 0)
            {
                var all = rings.SelectMany(r => r).ToList();
                var b = MapGeometry.BoundsOf(all);
                b = Rect.MinMaxRect(b.xMin - lib.protectedBuffer, b.yMin - lib.protectedBuffer, b.xMax + lib.protectedBuffer, b.yMax + lib.protectedBuffer);
                zones.Add((name, MapGeometry.ConvexHull(all), b));
            }
        }
        // Every building already there, every landmark, every walled compound (their grounds are theirs).
        foreach (var b in layout.buildings) if (b.outline != null && b.outline.Count >= 3 && region.Contains(MapGeometry.Centroid(b.outline))) Fill(b.outline, 1f);
        foreach (var l in layout.landmarks) Fill(l.footprint, 1f);
        foreach (var a in layout.areas)
            if (a.kind == AreaKind.Commercial || a.kind == AreaKind.Industrial || a.kind == AreaKind.Institutional || a.kind == AreaKind.Water || a.kind == AreaKind.Cemetery)
                Fill(a.outline, 1f);
        foreach (var p in mock.placements)
        {
            if (p.outline != null && p.outline.Count >= 3) Fill(p.outline, 1f);
            else if (p.kind == PlacementKind.House || p.kind == PlacementKind.Model)
                Box(p.position, new Vector2(p.modelSize.x * p.scale.x, p.modelSize.z * p.scale.z) * 0.5f, p.yaw, 1f);
            else if (p.kind == PlacementKind.Sign || p.kind == PlacementKind.Banner) Disc(p.position, 2f);
        }
        // Streets, with the setback and room for the poles; waterways; the mission's places.
        foreach (var r in layout.roads)
        {
            if (r.points.Count < 2) continue;
            float half = plib.roadWidths.For(r.roadClass) * 0.5f + lib.setback.x;
            var b = MapGeometry.BoundsOf(r.points);
            for (float x = b.xMin - half; x <= b.xMax + half; x += Cell)
            for (float y = b.yMin - half; y <= b.yMax + half; y += Cell)
            {
                var p = new Vector2(x, y);
                if (g.Index(p, out int i) && g.cell[i] == 0 && MapGeometry.DistanceToPolyline(r.points, p, out _) <= half) g.cell[i] = 1;
            }
        }
        foreach (var w in layout.waterways)
        {
            float half = plib.WaterWidth(w.kind) * 0.5f + 2f;
            var b = MapGeometry.BoundsOf(w.points);
            for (float x = b.xMin - half; x <= b.xMax + half; x += Cell)
            for (float y = b.yMin - half; y <= b.yMax + half; y += Cell)
            {
                var p = new Vector2(x, y);
                if (g.Index(p, out int i) && MapGeometry.DistanceToPolyline(w.points, p, out _) <= half) g.cell[i] = 1;
            }
        }
        foreach (var k in keepClear) Disc(k.centre / scale, k.radius / scale + 3f);

        // ---- lots
        var lots = new List<Lot>();
        var counts = new int[4];
        int commercialCount = 0;
        var local = new Dictionary<Vector2Int, int[]>();
        Vector2Int Hood(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 80f), Mathf.FloorToInt(p.y / 80f));

        (MapRoad road, float distance, Vector2 closest, Vector2 dir) Nearest(Vector2 p)
        {
            MapRoad best = null; float bd = float.MaxValue; Vector2 bc = p; Vector2 bdir = Vector2.right;
            foreach (var r in layout.roads)
            {
                if (r.points.Count < 2 || r.roadClass == RoadClass.Footway) continue;
                float d = MapGeometry.DistanceToPolyline(r.points, p, out Vector2 c, out int seg) - plib.roadWidths.For(r.roadClass) * 0.5f;
                if (d < bd) { bd = d; best = r; bc = c; bdir = (r.points[seg + 1] - r.points[seg]).normalized; }
            }
            return (best, bd, bc, bdir);
        }
        bool Corner(Vector2 p) => net.junctions.Any(j => j.arms.Count >= 3 && Vector2.Distance(j.centre, p) < j.radius + 14f);

        int PickStoreys(Vector2 at, bool wantTall)
        {
            var hood = Hood(at);
            if (!local.TryGetValue(hood, out var lc)) local[hood] = lc = new int[4];
            int total = counts[1] + counts[2] + counts[3], localTotal = lc[1] + lc[2] + lc[3];
            int best = 1; float bestScore = float.MinValue;
            for (int s = 1; s <= 3; s++)
            {
                float score = lib.Target(s) * (total + 1) - counts[s] + 0.6f * (lib.Target(s) * (localTotal + 1) - lc[s]);
                if (wantTall && s > 1) score += 0.35f;
                score += R() * 0.05f;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        bool Fits(Vector2 c, Vector2 size, float yaw)
        {
            foreach (var p in RectCells(c, size * 0.5f + Vector2.one * (lib.sideGap.x * 0.5f), yaw))
                if (!g.Index(p, out int i) || g.cell[i] != 0) return false;
            // What is solid in the scene: poles, lamps, walls, the jam, props (a box a little above the ground).
            var world = root.transform.TransformPoint(new Vector3(c.x, 1.5f, c.y));
            var half = new Vector3(size.x * 0.5f * scale, 1.0f * scale, size.y * 0.5f * scale);
            foreach (var hit in Physics.OverlapBox(world, half, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore))
                if (hit.attachedRigidbody == null && hit.GetComponent<WaterSurface>() == null) return false;
            return true;
        }

        Lot TryLot(Vector2 frontPoint, Vector2 front, MapRoad road, bool frontage, float extraBack)
        {
            bool corner = Corner(frontPoint);
            bool main = road != null && road.roadClass >= RoadClass.Tertiary;
            int total = counts[1] + counts[2] + counts[3];
            bool commercial = frontage && (main || corner) && commercialCount < lib.commercialShare * (total + 1) + 3
                              || frontage && road != null && road.roadClass >= RoadClass.Residential && commercialCount < lib.commercialShare * (total + 1) - 4;
            int storeys = PickStoreys(frontPoint, corner && commercial);
            var order = new List<(int s, bool c)> { (storeys, commercial) };
            for (int s = 1; s <= 3; s++) if (s != storeys) order.Add((s, commercial));
            if (!commercial || !frontage) order.Add((storeys, !commercial));
            foreach (var (s, c) in order)
            {
                var pool = lib.models.Where(m => m.storeys == s && m.commercial == c && m.prefab != null).OrderBy(_ => random.Next()).ToList();
                foreach (var m in pool)
                {
                    float sc = 1f + (R() * 2f - 1f) * lib.sizeJitter;
                    var size = m.footprint * sc;
                    var centre = frontPoint + front * -(lib.setback.y * R() * 0.5f + size.y * 0.5f + extraBack);
                    float yaw = MapGeometry.Heading(front);
                    if (!Fits(centre, size, yaw)) continue;
                    var lot = new Lot { id = lots.Count + 1, model = m, centre = centre, front = front, scale = sc, road = road?.osmId ?? "", corner = corner };
                    Box(centre, size * 0.5f, yaw, lib.sideGap.x * 0.5f + R() * (lib.sideGap.y - lib.sideGap.x) * 0.5f);
                    counts[s]++;
                    local[Hood(frontPoint)][s]++;
                    if (c) commercialCount++;
                    lots.Add(lot);
                    return lot;
                }
            }
            return null;
        }

        // 1. Along every street, both sides, front toward it.
        foreach (var r in layout.roads.Where(r => r.points.Count >= 2 && r.roadClass != RoadClass.Footway).OrderByDescending(r => (int)r.roadClass).ThenBy(r => r.osmId, System.StringComparer.Ordinal))
        {
            float half = plib.roadWidths.For(r.roadClass) * 0.5f + lib.setback.x;
            foreach (float side in new[] { 1f, -1f })
            {
                int sinceAlley = 0;
                Walk(r.points, 2f, (at, dir, advance) =>
                {
                    var n = MapGeometry.Left(dir) * side;
                    var frontPoint = at + n * (half + lib.sideGap.y * 0.5f + 0.3f);      // just clear of the setback, with the lot's own margin
                    if (!region.Contains(frontPoint)) return 2f;
                    var near = Nearest(frontPoint + n * 4f);
                    if (near.road != r) return 2f;                          // another street is nearer: its own pass faces the lot to it
                    var lot = TryLot(frontPoint, -n, r, true, 0f);
                    if (lot == null) return 2f;
                    float step = lot.Size.x + lib.sideGap.x + R() * (lib.sideGap.y - lib.sideGap.x);
                    if (++sinceAlley >= lib.lotsPerAlley) { step += lib.alley; sinceAlley = 0; }
                    return step;
                });
            }
        }
        int frontageCount = lots.Count;

        // 2. The inner land, each lot facing the street nearest it (turned square to that street).
        for (float y = region.yMin + 4f; y < region.yMax - 4f; y += 3f)
        for (float x = region.xMin + 4f; x < region.xMax - 4f; x += 3f)
        {
            var p = new Vector2(x, y);
            if (!g.Index(p, out int i) || g.cell[i] != 0) continue;
            var near = Nearest(p);
            if (near.road == null) continue;
            var toRoad = near.closest - p;
            if (toRoad.sqrMagnitude < 0.01f) continue;
            // Square to the street: the street's normal on the lot's side.
            var n = MapGeometry.Left(near.dir);
            if (Vector2.Dot(n, toRoad) < 0f) n = -n;
            var lot = TryLot(p + n * 4f, n, near.road, false, 0f);
        }

        // 3. The storey mix to within 2 % of 70/20/10: swap models in place where a smaller or same-size model fits.
        BalanceStoreys(lots, lib, random, counts);
        BalanceUse(lots, lib, random);

        // ---- the scene
        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);
        GameObjectUtility.SetStaticEditorFlags(group.gameObject, StaticEditorFlags.BatchingStatic);
        foreach (var lot in lots) Place(lot, group);
        Physics.SyncTransforms();

        // ---- the boat must still get to everyone, the hall and the walled compounds' grounds
        int removed = KeepReach(scene, settings, root.transform, lots, keepClear, scale);

        // Taking some out may have moved the mix: balance what is left again, swapping models in place.
        var remaining = lots.Where(l => l.placed != null).ToList();
        BalanceStoreys(remaining, lib, random, counts);
        BalanceUse(remaining, lib, random);
        foreach (var lot in remaining)
            if (PrefabUtility.GetCorrespondingObjectFromSource(lot.placed.gameObject) != lot.model.prefab)
            {
                Object.DestroyImmediate(lot.placed.gameObject);
                Place(lot, group);
            }

        var placed = lots.Where(l => l.placed != null).ToList();
        WriteOutputs(placed, zones, lib, scale, root.transform, frontageCount, removed);
        int n1 = placed.Count(l => l.model.storeys == 1), n2 = placed.Count(l => l.model.storeys == 2), n3 = placed.Count(l => l.model.storeys == 3);
        int nc = placed.Count(l => l.model.commercial);
        return "Infill: " + placed.Count + " buildings (" + Pct(n1, placed.Count) + " one-storey, " + Pct(n2, placed.Count) + " two, " + Pct(n3, placed.Count) +
               " three; " + Pct(nc, placed.Count) + " commercial), " + removed + " taken out again to keep the boat's ways open.";
    }

    static string Pct(int n, int of) => n + " (" + (of == 0 ? 0f : 100f * n / of).ToString("0.0") + " %)";

    static List<Vector2> Closed(IList<Vector2> ring) { var l = new List<Vector2>(ring) { ring[0] }; return l; }

    /// <summary>Cell centres inside a turned rectangle (centre, half size along its own x/z, yaw degrees).</summary>
    static IEnumerable<Vector2> RectCells(Vector2 c, Vector2 half, float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        var fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));       // the model's +Z on the map
        var right = new Vector2(fwd.y, -fwd.x);                     // its +X
        for (float a = -half.x; a <= half.x; a += Cell * 0.7f)
        for (float b = -half.y; b <= half.y; b += Cell * 0.7f)
            yield return c + right * a + fwd * b;
    }

    /// <summary>Walks a line; <paramref name="each"/> returns how far to go to the next stop.</summary>
    static void Walk(List<Vector2> line, float first, System.Func<Vector2, Vector2, float, float> each)
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
            s += Mathf.Max(0.5f, each(Vector2.Lerp(a, b, t), (b - a).normalized, s));
        }
    }

    static void BalanceStoreys(List<Lot> lots, BuildingLibrary lib, System.Random random, int[] counts)
    {
        for (int pass = 0; pass < 400; pass++)
        {
            int total = lots.Count;
            if (total == 0) return;
            int over = 0, under = 0; float worstOver = 0f, worstUnder = 0f;
            for (int s = 1; s <= 3; s++)
            {
                float diff = (float)lots.Count(l => l.model.storeys == s) / total - lib.Target(s);
                if (diff > worstOver) { worstOver = diff; over = s; }
                if (-diff > worstUnder) { worstUnder = -diff; under = s; }
            }
            if (worstOver <= 0.015f && worstUnder <= 0.015f) return;
            // Swap one building of the over-represented height for one of the short height, where it fits its lot.
            bool done = false;
            foreach (var lot in lots.Where(l => l.model.storeys == over).OrderBy(_ => random.Next()))
            {
                var swap = lib.models.Where(m => m.storeys == under && m.commercial == lot.model.commercial && m.prefab != null
                                                 && m.footprint.x * lot.scale <= lot.Size.x + 0.01f && m.footprint.y * lot.scale <= lot.Size.y + 0.01f)
                                     .OrderBy(_ => random.Next()).FirstOrDefault()
                        ?? lib.models.Where(m => m.storeys == under && m.prefab != null
                                                 && m.footprint.x * lot.scale <= lot.Size.x + 0.01f && m.footprint.y * lot.scale <= lot.Size.y + 0.01f)
                                     .OrderBy(_ => random.Next()).FirstOrDefault();
                if (swap == null) continue;
                lot.model = swap;
                done = true;
                break;
            }
            if (!done) return;
        }
    }

    static void BalanceUse(List<Lot> lots, BuildingLibrary lib, System.Random random)
    {
        for (int pass = 0; pass < 400; pass++)
        {
            int total = lots.Count;
            if (total == 0) return;
            float share = (float)lots.Count(l => l.model.commercial) / total;
            if (Mathf.Abs(share - lib.commercialShare) <= 0.03f) return;
            bool toCommercial = share < lib.commercialShare;
            // Commercial goes to corners and main-road lots first; residential back into the inner lots first.
            var candidates = lots.Where(l => l.model.commercial != toCommercial)
                                 .OrderBy(l => toCommercial ? (l.corner ? 0 : 1) : (l.corner ? 1 : 0)).ThenBy(_ => random.Next());
            bool done = false;
            foreach (var lot in candidates)
            {
                var swap = lib.models.Where(m => m.storeys == lot.model.storeys && m.commercial == toCommercial && m.prefab != null
                                                 && m.footprint.x * lot.scale <= lot.Size.x + 0.01f && m.footprint.y * lot.scale <= lot.Size.y + 0.01f)
                                     .OrderBy(_ => random.Next()).FirstOrDefault();
                if (swap == null) continue;
                lot.model = swap;
                done = true;
                break;
            }
            if (!done) return;
        }
    }

    static void Place(Lot lot, Transform group)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(lot.model.prefab, group);
        go.name = "L" + lot.id.ToString("0000") + " " + lot.model.name;
        go.transform.localPosition = new Vector3(lot.centre.x, 0f, lot.centre.y);
        go.transform.localRotation = Quaternion.Euler(0f, lot.Yaw, 0f);
        go.transform.localScale = Vector3.one * lot.scale;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.sharedMaterial = BarangayMockBuilder.SlipperySurface();
        lot.placed = go.transform;
    }

    /// <summary>
    /// Floods the water from the boat, and holds the new buildings to what the boat reached before them: every resident, the
    /// hall, and the open ground of every landmark lot and walled compound (90 % of it, or as much as before). Where one falls
    /// short, the new buildings nearest it are taken out and the water flooded again.
    /// </summary>
    static int KeepReach(Scene scene, BarangayMockSettings settings, Transform root, List<Lot> lots, IList<KeepClear> keepClear, float scale)
    {
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        if (boat == null) return 0;
        var area = MabuhayLevelBuilder.Scaled(settings.Region(settings.Layout), scale);
        area = Rect.MinMaxRect(area.xMin - 20f, area.yMin - 20f, area.xMax + 20f, area.yMax + 20f);

        var points = new List<Vector2>();
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == "Mission Anchors" || go.name == "Residents") foreach (Transform t in go.transform) points.Add(new Vector2(t.position.x, t.position.z));
        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        if (evac != null) points.Add(new Vector2(evac.transform.position.x, evac.transform.position.z));

        // The lots: each landmark model's drawn box and each walled compound's.
        var lotsToKeep = new List<Bounds>();
        foreach (var holder in new[] { root.Find("Landmarks"), root.Find(CompoundWallBuilder.GroupName) })
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
        // As the level test measures it: the open, boat-sized places in the lot's box that the water from the boat reaches.
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

        var group = root.Find(GroupName);
        if (group != null) group.gameObject.SetActive(false);
        var before = Flood();
        var wantPoints = points.Where(t => before.Reached(t) || before.DistanceToReached(t, 10f) <= 8f).ToList();
        var wantLots = new List<(Bounds b, float share)>();
        foreach (var b in lotsToKeep)
        {
            var (roomy, reached) = Measure(before, b);
            if (roomy >= 30) wantLots.Add((b, Mathf.Min(0.93f, (float)reached / roomy - 0.01f)));
        }
        if (group != null) group.gameObject.SetActive(true);

        int removed = 0;
        for (int pass = 0; pass < 10; pass++)
        {
            var water = Flood();
            var lost = new List<(Vector2 at, float reach)>();
            foreach (var t in wantPoints) if (!water.Reached(t) && water.DistanceToReached(t, 10f) > 8f) lost.Add((t, 0f));
            foreach (var (b, share) in wantLots)
            {
                var (roomy, reached) = Measure(water, b);
                if (roomy > 0 && (float)reached / roomy < share) lost.Add((new Vector2(b.center.x, b.center.z), Mathf.Max(b.extents.x, b.extents.z)));
            }
            if (lost.Count == 0) break;
            foreach (var (at, reach) in lost)
                foreach (var lot in lots.Where(l => l.placed != null)
                                        .OrderBy(l => Mathf.Max(0f, Vector2.Distance(new Vector2(l.placed.position.x, l.placed.position.z), at) - reach)).Take(8).ToList())
                {
                    Object.DestroyImmediate(lot.placed.gameObject);
                    lot.placed = null;
                    removed++;
                }
        }
        return removed;
    }

    // ------------------------------------------------------------------ outputs

    const string OutFolder = "../Source Assets/Buildings";

    [System.Serializable] class PlacementEntry { public string lot; public string model; public string category; public int storeys; public float[] position; public float yaw; public float scale; public string nearestRoad; public bool corner; }
    [System.Serializable] class PlacementFile { public int seed; public string units; public string coordinates; public List<PlacementEntry> buildings = new List<PlacementEntry>(); }

    static void WriteOutputs(List<Lot> placed, List<(string name, List<Vector2> ring, Rect bounds)> zones, BuildingLibrary lib, float scale, Transform root, int frontage, int removed)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutFolder));
        Directory.CreateDirectory(folder);
        var file = new PlacementFile
        {
            seed = lib.seed, units = "metres",
            coordinates = "World (Unity scene) metres, x east, y up, z north; the map is drawn at " + scale + "x, so a building's world size is its model size x scale x " + scale + "."
        };
        foreach (var l in placed)
        {
            var p = l.placed.position;
            file.buildings.Add(new PlacementEntry
            {
                lot = "L" + l.id.ToString("0000"), model = l.model.name, category = l.model.commercial ? "commercial" : "residential", storeys = l.model.storeys,
                position = new[] { Round(p.x), Round(p.y), Round(p.z) }, yaw = Round(l.Yaw), scale = Round(l.scale), nearestRoad = l.road, corner = l.corner
            });
        }
        File.WriteAllText(Path.Combine(folder, "Placement.json"), JsonUtility.ToJson(file, true));

        int total = placed.Count;
        var sb = new StringBuilder();
        sb.AppendLine("# Infill report: Barangay Mabuhay");
        sb.AppendLine();
        sb.AppendLine("Seed " + lib.seed + ". " + total + " buildings placed (" + frontage + " street-front lots were laid before the inner blocks; " + removed + " were taken out again to keep the boat's ways open).");
        sb.AppendLine();
        sb.AppendLine("## Storeys (target 70 / 20 / 10, tolerance 2 %)");
        sb.AppendLine();
        sb.AppendLine("| Storeys | Count | Share | Target |");
        sb.AppendLine("|---|---|---|---|");
        for (int s = 1; s <= 3; s++)
        {
            int n = placed.Count(l => l.model.storeys == s);
            sb.AppendLine("| " + s + " | " + n + " | " + (100f * n / Mathf.Max(1, total)).ToString("0.0") + " % | " + (lib.Target(s) * 100f).ToString("0") + " % |");
        }
        sb.AppendLine();
        sb.AppendLine("## Use (target about 70 / 30)");
        sb.AppendLine();
        sb.AppendLine("| Use | Count | Share | 1-storey | 2-storey | 3-storey |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (bool c in new[] { false, true })
        {
            var set = placed.Where(l => l.model.commercial == c).ToList();
            sb.AppendLine("| " + (c ? "Commercial" : "Residential") + " | " + set.Count + " | " + (100f * set.Count / Mathf.Max(1, total)).ToString("0.0") + " % | " +
                          string.Join(" | ", new[] { 1, 2, 3 }.Select(s => (100f * set.Count(l => l.model.storeys == s) / Mathf.Max(1, set.Count)).ToString("0") + " %")) + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Protected zones (kept exactly as they are; nothing placed inside or within the buffer)");
        sb.AppendLine();
        foreach (var z in zones)
        {
            Vector3 a = root.TransformPoint(new Vector3(z.bounds.xMin, 0f, z.bounds.yMin)), b = root.TransformPoint(new Vector3(z.bounds.xMax, 0f, z.bounds.yMax));
            sb.AppendLine("- **" + z.name + "**: outline from the layout plus a " + lib.protectedBuffer + " m buffer (layout metres; " + (lib.protectedBuffer * scale) +
                          " m in the world). Bounding box, layout: x " + z.bounds.xMin.ToString("0.0") + " .. " + z.bounds.xMax.ToString("0.0") + ", y " + z.bounds.yMin.ToString("0.0") + " .. " + z.bounds.yMax.ToString("0.0") +
                          "; world (Unity x, z): " + a.x.ToString("0.0") + " .. " + b.x.ToString("0.0") + ", " + a.z.ToString("0.0") + " .. " + b.z.ToString("0.0") + ".");
        }
        File.WriteAllText(Path.Combine(folder, "Infill_Report.md"), sb.ToString());
    }

    static float Round(float v) => Mathf.Round(v * 100f) / 100f;
}
