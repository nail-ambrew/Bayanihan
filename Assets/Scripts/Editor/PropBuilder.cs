using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fills the open scene's Barangay Mabuhay map with the prop packs (<see cref="PropLibrary"/>), under BarangayMock/Props:
/// <list type="bullet">
/// <item>jeepneys parked at the kerb of the wider streets, stranded half under the flood;</item>
/// <item>loose corrugated sheets leaning on the walls of the shacks;</item>
/// <item>buckets set out on roofs above the water;</item>
/// <item>stacks of hollow blocks and rubble piles beside houses, and barriers closing the roads on the levee;</item>
/// <item>evacuees and rescuers waiting on the hall's porch, and onlookers on the levee.</item>
/// </list>
/// Everything is placed by asking the physics engine where the walls, roofs and floors are, so it fits whatever the map builder
/// made. Nothing goes in the two mission streets, on the boat's start, the hall's landing, a resident or a hazard. Same seed,
/// same props. Called by <see cref="MabuhayGameplayBuilder"/> after the plants; running it again replaces what it placed before.
/// </summary>
public static class PropBuilder
{
    const string MapRootName = "BarangayMock";
    public const string GroupName = "Props";
    /// <summary>Where the flood's surface is at the start of a mission (world y).</summary>
    const float WaterY = 0f;

    [MenuItem("Bayanihan/Props/Place On Open Map", false, 42)]
    static void PlaceFromMenu()
    {
        var settings = BarangayMockBuilder.LoadSettings();
        if (settings == null) { Debug.LogWarning("PropBuilder: no map settings."); return; }
        var scene = SceneManager.GetActiveScene();
        Debug.Log("PropBuilder: " + Build(scene, settings, FoliageBuilder.KeepClearOf(scene)));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    sealed class Context
    {
        public PropLibrary lib;
        public System.Random random;
        public Transform root, group;
        public float groundY;
        public BarangayLayout layout;
        /// <summary>The map's scale; <see cref="region"/> and <see cref="roads"/> are already at it (world metres).</summary>
        public float scale = 1f;
        public Rect region;
        public List<KeepClear> keepClear;
        public List<(MapRoad road, float half)> roads = new List<(MapRoad, float)>();
        public readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        public float R() => (float)random.NextDouble();
        public T Pick<T>(IList<T> list) => list[random.Next(list.Count)];
        public void Count(string what) { counts.TryGetValue(what, out int n); counts[what] = n + 1; }
    }

    public static PropLibrary LoadLibrary() => AssetDatabase.LoadAssetAtPath<PropLibrary>(PropPackImporter.LibraryPath);

    /// <summary>Places the props and returns one line saying how many of each.</summary>
    public static string Build(Scene scene, BarangayMockSettings settings, IList<KeepClear> keepClear)
    {
        var lib = LoadLibrary();
        if (lib == null || lib.jeepneys.Count == 0) return "Props: no library yet (Bayanihan > Props > Import Packs).";
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (root == null) return "Props: no map in the scene.";

        var old = root.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        // Props are placed in the world: the streets and the region are taken from the layout at the map's scale, and the
        // props themselves are people-sized, whatever size the map is drawn at.
        var layout = settings.Layout;
        float scale = Mathf.Max(0.05f, settings.scale);
        var c = new Context
        {
            lib = lib,
            random = new System.Random(lib.seed),
            root = root.transform,
            groundY = root.transform.position.y,
            layout = layout,
            scale = scale,
            region = MabuhayLevelBuilder.Scaled(settings.Region(layout), scale),
            keepClear = new List<KeepClear>(keepClear),
        };
        var widths = settings.library != null ? settings.library.roadWidths : new BarangayLayoutView.RoadWidths();
        foreach (var r in layout.roads)
            if (r.points.Count >= 2)
                c.roads.Add((new MapRoad { osmId = r.osmId, name = r.name, roadClass = r.roadClass, points = r.points.Select(p => p * scale).ToList() },
                             widths.For(r.roadClass) * 0.5f * scale));

        c.group = new GameObject(GroupName).transform;
        c.group.SetParent(root.transform, false);
        c.group.localScale = Vector3.one / scale;        // so a prop's own scale is its size in the world

        var houses = new List<Transform>();
        var buildings = root.transform.Find("Buildings");
        if (buildings != null) foreach (Transform h in buildings) if (h.name.StartsWith("House ")) houses.Add(h);

        PlaceJeepneys(c, Holder(c, "Jeepneys"));
        PlaceRoofSheets(c, Holder(c, "Roof sheets"), houses);
        PlaceRoofBuckets(c, Holder(c, "Buckets"), houses);
        PlaceConcrete(c, Holder(c, "Concrete"), houses);
        PlaceLeveeBarriers(c, Holder(c, "Levee barriers"), settings);
        var people = Holder(c, "People");
        PlacePorchCrowd(c, people);
        PlaceLeveeOnlookers(c, people, settings);

        return "Props: " + string.Join(", ", c.counts.OrderBy(kv => kv.Key).Select(kv => kv.Value + " " + kv.Key)) + ".";
    }

    static Transform Holder(Context c, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(c.group, false);
        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        return t;
    }

    static GameObject Put(Context c, Transform parent, GameObject prefab, Vector3 world, Quaternion rotation, string what, float scale = 1f)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(world, rotation);
        go.transform.localScale = Vector3.one * scale;
        c.Count(what);
        Physics.SyncTransforms();           // the next prop's checks see this one
        return go;
    }

    // ------------------------------------------------------------------ what is where

    static bool IsMissionStreet(MapRoad r) => r.osmId == MabuhayLevelBuilder.BagumbayanId || r.osmId == MabuhayLevelBuilder.MapaladId;

    /// <summary>Clear of the boat's start, the hall, the residents and the hazards, with <paramref name="margin"/> metres to spare.</summary>
    static bool ClearOfMission(Context c, Vector2 p, float margin)
    {
        foreach (var k in c.keepClear) if (Vector2.Distance(k.centre, p) < k.radius + margin) return false;
        return true;
    }

    /// <summary>How far <paramref name="p"/> is from the edge of the nearest road (negative: on it), leaving out <paramref name="except"/>.</summary>
    static float FromRoadEdge(Context c, Vector2 p, MapRoad except = null, bool missionOnly = false)
    {
        float best = float.PositiveInfinity;
        foreach (var (road, half) in c.roads)
        {
            if (road == except || (missionOnly && !IsMissionStreet(road))) continue;
            best = Mathf.Min(best, MapGeometry.DistanceToPolyline(road.points, p, out _) - half);
        }
        return best;
    }

    /// <summary>Anything fixed and solid in this box, other than the ground under it?</summary>
    static bool Blocked(Vector3 centre, Vector3 half, Quaternion rotation)
    {
        foreach (var hit in Physics.OverlapBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            if (hit.attachedRigidbody == null && hit.GetComponent<WaterSurface>() == null) return true;
        return false;
    }

    static bool IsPartOf(Collider hit, Transform model) => hit != null && hit.transform.IsChildOf(model);

    static Bounds DrawnBounds(Transform model)
    {
        var rs = model.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return new Bounds(model.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>
    /// A wall of <paramref name="house"/>, found by looking at it from outside on one of its four sides at <paramref name="height"/>
    /// above the ground: where it is and which way it faces.
    /// </summary>
    static bool FindWall(Context c, Transform house, float height, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        var b = DrawnBounds(house);
        Vector3 centre = new Vector3(b.center.x, c.groundY + height, b.center.z);
        int first = c.random.Next(4);
        for (int k = 0; k < 4; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, (first + k) * 90f, 0f) * house.forward;
            dir.y = 0f;
            dir.Normalize();
            float reach = b.extents.magnitude + 3f;
            var from = centre + dir * reach;
            foreach (var hit in Physics.RaycastAll(from, -dir, reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.attachedRigidbody != null || hit.collider.GetComponent<WaterSurface>() != null) continue;
                if (!IsPartOf(hit.collider, house) || Mathf.Abs(hit.normal.y) > 0.3f) break;     // something else is in front, or not a wall
                point = hit.point;
                normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                return true;
            }
        }
        return false;
    }

    /// <summary>The top of what is solid under (x, z), looking down from <paramref name="fromY"/>.</summary>
    static bool Floor(Vector2 xz, float fromY, out RaycastHit floor)
    {
        foreach (var hit in Physics.RaycastAll(new Vector3(xz.x, fromY, xz.y), Vector3.down, fromY + 20f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.attachedRigidbody != null || hit.collider.GetComponent<WaterSurface>() != null) continue;
            floor = hit;
            return true;
        }
        floor = default;
        return false;
    }

    /// <summary>Room for somebody to stand here: nothing in the way from the knees to above the head.</summary>
    static bool RoomToStand(Vector3 feet) =>
        !Physics.CheckCapsule(feet + Vector3.up * 0.55f, feet + Vector3.up * 1.6f, 0.28f, ~0, QueryTriggerInteraction.Ignore);

    // ------------------------------------------------------------------ jeepneys

    /// <summary>
    /// Parked at the kerb of the streets wide enough for one (7 m and more), stranded where the flood caught them: at least
    /// <see cref="PropLibrary.jeepneyGap"/> apart, away from corners, never in the mission's two streets. The lane left beside
    /// one is 4.5 m or more, so the boat still gets by.
    /// </summary>
    static void PlaceJeepneys(Context c, Transform parent)
    {
        var placed = new List<Vector2>();
        var candidates = c.roads.Where(r => r.half >= 3.5f && !IsMissionStreet(r.road)).ToList();
        // Longest first, so the main roads get them before the side streets.
        candidates = candidates.OrderByDescending(r => Length(r.road.points)).ThenBy(_ => c.random.Next()).ToList();
        int model = 0;

        for (int pass = 0; pass < 3 && placed.Count < c.lib.jeepneyCount; pass++)
            foreach (var (road, half) in candidates)
            {
                if (placed.Count >= c.lib.jeepneyCount) break;
                var pts = road.points;
                float total = Length(pts);
                for (float s = 10f + c.R() * 20f; s < total - 10f && placed.Count < c.lib.jeepneyCount; s += 18f + c.R() * 22f)
                {
                    PointAt(pts, s, out Vector2 at, out Vector2 along);
                    float side = c.random.Next(2) == 0 ? 1f : -1f;
                    Vector2 p = at + MapGeometry.Left(along) * side * (half - 1.35f);
                    if (!c.region.Contains(p)) continue;
                    if (placed.Any(q => Vector2.Distance(q, p) < c.lib.jeepneyGap)) continue;
                    if (!ClearOfMission(c, p, 12f)) continue;
                    if (FromRoadEdge(c, p, road) < 6f) continue;                         // not at a corner: another road is near
                    if (FromRoadEdge(c, p, null, missionOnly: true) < 8f) continue;

                    var prefab = c.lib.jeepneys[model % c.lib.jeepneys.Count];
                    Vector3 size = PlaceholderLibrary.Measure(prefab);
                    float yaw = MapGeometry.Heading(along) + (c.random.Next(2) == 0 ? 0f : 180f) + (c.R() - 0.5f) * 8f;
                    var turn = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 centre = new Vector3(p.x, c.groundY + 0.35f + size.y * 0.5f, p.y);
                    if (Blocked(centre, new Vector3(size.x * 0.5f + 0.2f, size.y * 0.5f, size.z * 0.5f + 0.3f), turn)) continue;

                    // A few have settled to one side as the water got under them.
                    var rotation = turn * Quaternion.Euler(0f, 0f, c.R() < 0.3f ? (c.R() - 0.5f) * 10f : 0f);
                    Put(c, parent, prefab, new Vector3(p.x, c.groundY, p.y), rotation, "jeepneys");
                    placed.Add(p);
                    model++;
                }
            }
    }

    static float Length(IList<Vector2> pts)
    {
        float l = 0f;
        for (int i = 1; i < pts.Count; i++) l += Vector2.Distance(pts[i - 1], pts[i]);
        return l;
    }

    static void PointAt(IList<Vector2> pts, float s, out Vector2 at, out Vector2 along)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            float seg = Vector2.Distance(pts[i - 1], pts[i]);
            if (s <= seg || i == pts.Count - 1)
            {
                along = (pts[i] - pts[i - 1]).normalized;
                at = Vector2.Lerp(pts[i - 1], pts[i], Mathf.Clamp01(s / Mathf.Max(0.001f, seg)));
                return;
            }
            s -= seg;
        }
        at = pts[0];
        along = Vector2.up;
    }

    // ------------------------------------------------------------------ roof sheets, buckets, concrete

    static bool IsShack(Transform house) => house.name.Contains("TinShack") || house.name.Contains("TarpShack") || house.name.Contains("Shanty");

    /// <summary>Two or three spare corrugated sheets leaning on a wall of a shack, standing out of the water.</summary>
    static void PlaceRoofSheets(Context c, Transform parent, List<Transform> houses)
    {
        if (c.lib.roofSheets.Count == 0) return;
        foreach (var house in houses)
        {
            if (!IsShack(house) || c.R() > c.lib.roofSheetShare) continue;
            if (!FindWall(c, house, 1.3f, out Vector3 wall, out Vector3 normal)) continue;
            var p2 = new Vector2(wall.x, wall.z);
            if (!ClearOfMission(c, p2, 4f) || FromRoadEdge(c, p2 + new Vector2(normal.x, normal.z) * 0.6f) < 0.5f) continue;

            Vector3 sideways = Vector3.Cross(Vector3.up, normal);
            int n = 2 + c.random.Next(2);
            float lean = 12f + c.R() * 8f;
            for (int i = 0; i < n; i++)
            {
                // Each sheet stands on its short edge, its face to the street, its top resting against the wall.
                Vector3 up = Quaternion.AngleAxis(-lean, sideways) * Vector3.up;              // the top tips back towards the wall
                Vector3 foot = wall + normal * (0.15f + 1.9f * Mathf.Sin(lean * Mathf.Deg2Rad)) + sideways * ((i - (n - 1) * 0.5f) * 0.55f + (c.R() - 0.5f) * 0.2f);
                foot.y = c.groundY;
                var rotation = Quaternion.LookRotation(up, normal) * Quaternion.Euler(0f, 0f, (c.R() - 0.5f) * 6f);
                Vector3 middle = foot + up * 0.95f;
                Put(c, parent, c.Pick(c.lib.roofSheets), middle, rotation, "roof sheets");
            }
        }
    }

    /// <summary>Buckets set out on a roof, above the water: drinking water, belongings carried up when the flood came.</summary>
    static void PlaceRoofBuckets(Context c, Transform parent, List<Transform> houses)
    {
        if (c.lib.buckets.Count == 0) return;
        foreach (var house in houses)
        {
            if (c.R() > c.lib.roofBucketShare) continue;
            var b = DrawnBounds(house);
            Vector2 centre = new Vector2(b.center.x + (c.R() - 0.5f) * b.size.x * 0.5f, b.center.z + (c.R() - 0.5f) * b.size.z * 0.5f);
            if (!ClearOfMission(c, centre, 2f)) continue;
            int n = 1 + c.random.Next(3);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = centre + new Vector2(c.R() - 0.5f, c.R() - 0.5f) * 1.2f;
                if (!Floor(at, b.max.y + 2f, out var roof) || !IsPartOf(roof.collider, house)) continue;
                if (roof.normal.y < 0.8f || roof.point.y < WaterY + 1.2f) continue;
                Put(c, parent, c.Pick(c.lib.buckets), roof.point, Quaternion.Euler(0f, c.R() * 360f, 0f) * Quaternion.FromToRotation(Vector3.up, roof.normal), "buckets");
            }
        }
    }

    /// <summary>A pallet of hollow blocks for a repair that never started, or the rubble of a wall that came down, beside a house.</summary>
    static void PlaceConcrete(Context c, Transform parent, List<Transform> houses)
    {
        foreach (var house in houses)
        {
            if (c.R() > c.lib.concreteShare) continue;
            if (!FindWall(c, house, 1.5f, out Vector3 wall, out Vector3 normal)) continue;
            bool rubble = c.R() < 0.35f && c.lib.rubble.Count > 0;
            var prefab = rubble ? c.Pick(c.lib.rubble) : c.Pick(c.lib.blockStacks);
            float scale = rubble ? 0.6f + c.R() * 0.15f : 1f;               // a small heap: the pile model is three metres across
            Vector3 size = PlaceholderLibrary.Measure(prefab) * scale;
            Vector3 at = wall + normal * (0.5f + size.z * 0.5f);
            var p2 = new Vector2(at.x, at.z);
            if (!ClearOfMission(c, p2, 4f) || FromRoadEdge(c, p2) < size.magnitude * 0.5f + 0.5f) continue;
            // A heap looks the same from every side, so it is left square to the world: turned, the box physics keeps round its
            // mesh would stand well clear of the stones.
            var turn = rubble ? Quaternion.identity : Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, (c.R() - 0.5f) * 10f, 0f);
            Vector3 boxCentre = new Vector3(at.x, c.groundY + 0.35f + size.y * 0.5f, at.z);
            if (Blocked(boxCentre, new Vector3(size.x * 0.5f, Mathf.Max(0.1f, size.y * 0.5f - 0.2f), size.z * 0.5f), turn)) continue;
            Put(c, parent, prefab, new Vector3(at.x, c.groundY, at.z), turn, rubble ? "rubble piles" : "block stacks", scale);
        }
    }

    /// <summary>Barriers across the roads where they climb onto the levee: closed, the town is under water.</summary>
    static void PlaceLeveeBarriers(Context c, Transform parent, BarangayMockSettings settings)
    {
        if (c.lib.barrier == null) return;
        var crest = Crest(settings, c.layout);
        var spots = new List<(Vector2 at, Vector2 along)>();

        // Where a road runs out to the edge of the map: put the closure on the crest in line with it.
        foreach (var (road, half) in c.roads)
        {
            if (half < 3f) continue;
            foreach (var end in new[] { road.points[0], road.points[road.points.Count - 1] })
            {
                if (!NearestOnCrest(crest, end, out Vector2 on, out Vector2 along) || Vector2.Distance(on, end) > 25f) continue;
                if (spots.Any(s => Vector2.Distance(s.at, on) < 30f)) continue;
                spots.Add((on, along));
            }
        }
        foreach (var (on, along) in spots.OrderBy(_ => c.random.Next()).Take(c.lib.barrierRuns))
        {
            Vector2 outward = OutwardOf(crest, on);
            for (int i = -1; i <= 1; i++)
            {
                Vector2 p = on + along * (i * 2.1f) + outward * 1.6f;
                var turn = Quaternion.Euler(0f, MapGeometry.Heading(along), 0f);
                Vector3 feet = new Vector3(p.x, MabuhayLevelBuilder.LeveeCrestY, p.y);
                if (Blocked(feet + Vector3.up * 0.5f, new Vector3(0.3f, 0.3f, 1f), turn)) continue;
                Put(c, parent, c.lib.barrier, feet, turn, "levee barriers");
            }
        }
    }

    static Rect Crest(BarangayMockSettings settings, BarangayLayout layout)
    {
        var inner = MabuhayLevelBuilder.EdgeAreaWorld(settings, layout);
        float run = MabuhayLevelBuilder.LeveeRun;
        return Rect.MinMaxRect(inner.xMin - run, inner.yMin - run, inner.xMax + run, inner.yMax + run);
    }

    static bool NearestOnCrest(Rect crest, Vector2 p, out Vector2 on, out Vector2 along)
    {
        float dl = Mathf.Abs(p.x - crest.xMin), dr = Mathf.Abs(crest.xMax - p.x), db = Mathf.Abs(p.y - crest.yMin), dt = Mathf.Abs(crest.yMax - p.y);
        float m = Mathf.Min(dl, dr, db, dt);
        if (m == dl) { on = new Vector2(crest.xMin, Mathf.Clamp(p.y, crest.yMin, crest.yMax)); along = Vector2.up; }
        else if (m == dr) { on = new Vector2(crest.xMax, Mathf.Clamp(p.y, crest.yMin, crest.yMax)); along = Vector2.up; }
        else if (m == db) { on = new Vector2(Mathf.Clamp(p.x, crest.xMin, crest.xMax), crest.yMin); along = Vector2.right; }
        else { on = new Vector2(Mathf.Clamp(p.x, crest.xMin, crest.xMax), crest.yMax); along = Vector2.right; }
        return true;
    }

    static Vector2 OutwardOf(Rect crest, Vector2 on)
    {
        if (Mathf.Approximately(on.x, crest.xMin)) return Vector2.left;
        if (Mathf.Approximately(on.x, crest.xMax)) return Vector2.right;
        if (Mathf.Approximately(on.y, crest.yMin)) return Vector2.down;
        return Vector2.up;
    }

    // ------------------------------------------------------------------ people

    /// <summary>
    /// The ones already brought in, and the responders, waiting on whatever dry floor is in front of the hall: anywhere within 14 m
    /// of the landing that is level, above the water and has room to stand, but not on the landing itself (the boat unloads there).
    /// </summary>
    static void PlacePorchCrowd(Context c, Transform parent)
    {
        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        if (evac == null || evac.landingArea == null || c.lib.people.Count == 0) return;
        var landing = evac.landingArea;
        Vector2 l = new Vector2(landing.position.x, landing.position.z);
        Vector2 street = new Vector2(evac.transform.position.x, evac.transform.position.z);

        var spots = new List<Vector3>();
        for (float x = -14f; x <= 14f; x += 0.9f)
            for (float z = -14f; z <= 14f; z += 0.9f)
            {
                Vector2 p = l + new Vector2(x, z);
                Vector3 local = landing.InverseTransformPoint(new Vector3(p.x, landing.position.y, p.y));
                if (Mathf.Abs(local.x) < evac.landingAreaSize.x * 0.5f + 1f && Mathf.Abs(local.z) < evac.landingAreaSize.y * 0.5f + 1.5f) continue;
                if (!Floor(p, 12f, out var floor) || floor.normal.y < 0.9f) continue;
                if (floor.point.y < WaterY + 0.25f || floor.point.y > WaterY + 3f) continue;
                if (!RoomToStand(floor.point)) continue;
                spots.Add(floor.point);
            }

        // Nearest the landing first, and not shoulder to shoulder.
        var taken = new List<Vector3>();
        var crowd = new List<GameObject>(c.lib.rescuers);
        crowd.AddRange(c.lib.people.OrderBy(_ => c.random.Next()));
        foreach (var spot in spots.OrderBy(s => Vector2.Distance(new Vector2(s.x, s.z), l) + c.R() * 2f))
        {
            if (taken.Count >= c.lib.porchPeople || taken.Count >= crowd.Count) break;
            if (taken.Any(t => Vector3.Distance(t, spot) < 0.85f)) continue;
            Vector2 look = street - new Vector2(spot.x, spot.z);
            float yaw = MapGeometry.Heading(look.sqrMagnitude > 0.01f ? look.normalized : Vector2.up) + (c.R() - 0.5f) * 50f;
            Put(c, parent, crowd[taken.Count], spot, Quaternion.Euler(0f, yaw, 0f), "people on dry ground by the hall");
            taken.Add(spot);
        }
    }

    /// <summary>Little groups on the levee's crest watching the flood, along the sides nearest the hall and the boat's start.</summary>
    static void PlaceLeveeOnlookers(Context c, Transform parent, BarangayMockSettings settings)
    {
        if (c.lib.people.Count == 0) return;
        var crest = Crest(settings, c.layout);
        var focus = c.keepClear.Select(k => k.centre).ToList();
        if (focus.Count == 0) focus.Add(crest.center);

        for (int g = 0; g < c.lib.leveeGroups; g++)
        {
            Vector2 target = focus[g % focus.Count] + new Vector2(c.R() - 0.5f, c.R() - 0.5f) * 160f;
            NearestOnCrest(crest, target, out Vector2 on, out Vector2 along);
            Vector2 outward = OutwardOf(crest, on);
            int n = 2 + c.random.Next(3);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = on + along * (i * 0.9f + (c.R() - 0.5f) * 0.4f) + outward * (0.9f + c.R() * 0.8f);
                Vector3 feet = new Vector3(p.x, MabuhayLevelBuilder.LeveeCrestY, p.y);
                if (Floor(p, MabuhayLevelBuilder.LeveeCrestY + 3f, out var floor)) feet.y = floor.point.y;
                if (!RoomToStand(feet)) continue;
                float yaw = MapGeometry.Heading(-outward) + (c.R() - 0.5f) * 60f;
                Put(c, parent, c.Pick(c.lib.people), feet, Quaternion.Euler(0f, yaw, 0f), "onlookers on the levee");
            }
        }
    }
}
