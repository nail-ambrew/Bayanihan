using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives the Mabuhay map the real lie of its land. The rest of the build lays everything out on flat ground a metre under
/// the flood; this runs last and:
///  1. makes the height field (<see cref="MapHeightField"/>) from the real elevation (Docs/Maps/mabuhay-elevation.json,
///     SRTM 30 m), smoothed, taken relative to the hall and scaled for play, then held at the flat level along the
///     mission's streets (so the rescue route keeps its depth) and raised under candidate evacuation sites outside the
///     route (so they stay dry at the flood's highest);
///  2. replaces the flat ground with a chunked ground mesh that follows it;
///  3. drapes the flat layers (lot tints, streams, streets and their paint, junctions, plain blocks, walls, wires) over it,
///     vertex by vertex;
///  4. lifts every standing thing (houses, landmarks, poles, signs, trees, props, the mission's posts) by the ground
///     under it. A house stands at the highest ground under its footprint, on a concrete plinth down to the lowest, so
///     nothing floats and no floor is sunk. Something resting on another thing (a bucket on a roof) rises with what it
///     rests on;
///  5. lifts the undergrowth and grass, and raises the levee's crest above the highest ground at the map's edge.
/// The flood stays a flat plane at y 0, so the low ground floods deep and the high ground stays dry.
/// </summary>
public static class TerrainBuilder
{
    public const string ElevationPath = "Docs/Maps/mabuhay-elevation.json";
    public const string FieldName = "MabuhayHeights.asset";

    // How the real ground is turned into the game's: smoothed (SRTM's 30 m cells carry a couple of metres of roofs and
    // trees), scaled down, and softly held between these bounds (metres above the flat ground level, which is a metre
    // under the flood).
    public const float SmoothMetres = 45f;
    public const float VerticalScale = 0.8f;
    public const float LowestBelow = 1.5f;
    public const float HighestAbove = 4f;

    // The mission's route is held flat: fully within CorridorHold of its streets and places, easing back to the real
    // ground over CorridorEase more.
    public const float CorridorHold = 16f;
    public const float CorridorEase = 40f;
    /// <summary>A candidate evacuation site outside the route stands at least this high: dry at the flood's highest (+0.6 m).</summary>
    public const float EvacuationFloor = 1.75f;
    public const float EvacuationEase = 20f;
    /// <summary>A landmark's lot is graded level this far past its outline, then eased back into the slope over PadEase.</summary>
    public const float PadGrow = 3f;
    public const float PadEase = 15f;

    const float FieldStep = 2f;
    const float ChunkSize = 64f;
    const float GroundCell = 4f;
    const float MaxEdge = 4f;          // flat layers are cut into pieces no longer than this before they are draped

    /// <summary>Makes the field and drapes the map over it. Returns a line for the build log.</summary>
    public static string Build(Scene scene, BarangayMockSettings settings)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BarangayMock");
        if (root == null) return "Terrain: no map to drape.";
        var layout = settings.Layout;

        var field = MakeField(scene, settings, layout, root, out string why);
        if (field == null) return "Terrain: " + why + " The map stays flat.";

        var groundGroup = root.transform.Find("Ground");
        int chunks = BuildGround(root.transform, groundGroup, field, settings);

        // Work out what everything stands on while it is all still flat, then move it.
        Physics.SyncTransforms();
        var d = new Drape(field, root.transform);
        foreach (var name in new[] { "Areas", "Water", "Roads", "Buildings", "Landmarks", "Signs", "Street Furniture",
                                     "Compound Walls", "Infill Buildings", "Foliage", "Props" })
        {
            var group = root.transform.Find(name);
            if (group != null) d.Collect(group, IsBuildingGroup(name));
        }
        foreach (var name in new[] { MabuhayLevelBuilder.AnchorsName, MabuhayLevelBuilder.HazardsName, MabuhayLevelBuilder.PostsName })
        {
            var group = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (group != null) d.Collect(group.transform, false);
        }
        var edge = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MabuhayLevelBuilder.EdgeName);
        d.edge = edge != null ? edge.transform : null;
        d.FindSupports();
        float crestLift = edge != null ? LiftEdge(edge.transform, field, settings) : 0f;
        d.edgeLift = crestLift;

        int lifted = d.LiftUnits();
        int draped = d.DrapeMeshes();
        int plinths = d.BuildPlinths(root.transform.Find("Buildings") ?? root.transform, settings);
        int plants = LiftPlants(root.transform, field);

        AssetDatabase.SaveAssets();
        float wet = WetShare(field, settings, layout);
        return "Terrain: " + field.min.ToString("0.0") + " to +" + field.max.ToString("0.0") + " m about the flat ground, " +
               Mathf.RoundToInt(wet * 100f) + " % of the map under the flood at its start; " + chunks + " ground chunks, " +
               draped + " layers draped, " + lifted + " things lifted, " + plinths + " plinths, " + plants + " plants lifted" +
               (crestLift > 0.01f ? ", levee crest raised " + crestLift.ToString("0.0") + " m." : ".");
    }

    // Houses stand on the highest ground under them, with a plinth. Landmarks stand on their own graded lot (see MakeField),
    // so they are lifted to it like anything else, with no plinth across their yards.
    static bool IsBuildingGroup(string name) => name == "Buildings" || name == "Infill Buildings";

    // ------------------------------------------------------------------ the field

    /// <summary>The real elevation on its 30 m grid, in metres above sea level.</summary>
    sealed class Elevation
    {
        public float xMin, yMin, step; public int nx, ny; public float[] h;

        public float At(float x, float y)
        {
            float fx = Mathf.Clamp((x - xMin) / step, 0f, nx - 1.001f), fy = Mathf.Clamp((y - yMin) / step, 0f, ny - 1.001f);
            int i = (int)fx, j = (int)fy; float tx = fx - i, ty = fy - j;
            return Mathf.Lerp(Mathf.Lerp(h[j * nx + i], h[j * nx + i + 1], tx), Mathf.Lerp(h[(j + 1) * nx + i], h[(j + 1) * nx + i + 1], tx), ty);
        }
    }

    static Elevation ReadElevation(out string why)
    {
        why = null;
        string path = Path.Combine(Directory.GetCurrentDirectory(), ElevationPath);
        if (!File.Exists(path)) { why = ElevationPath + " is missing (run Docs/Maps/fetch_mabuhay_elevation.py)."; return null; }
        if (!(MiniJson.Parse(File.ReadAllText(path)) is Dictionary<string, object> doc)) { why = ElevationPath + " cannot be read."; return null; }
        float F(string k) => Convert.ToSingle(doc[k]);
        var e = new Elevation { xMin = F("xMin"), yMin = F("yMin"), step = F("step"), nx = Convert.ToInt32(doc["nx"]), ny = Convert.ToInt32(doc["ny"]) };
        var list = (List<object>)doc["heights"];
        e.h = list.Select(Convert.ToSingle).ToArray();
        if (e.h.Length != e.nx * e.ny) { why = ElevationPath + " has the wrong number of heights."; return null; }
        // Smooth on its own grid first: the model's cells carry roofs and trees, not the ground.
        e.h = Blur(e.h, e.nx, e.ny, SmoothMetres / e.step);
        return e;
    }

    /// <summary>A separable Gaussian blur of a grid, sigma in cells, edges held.</summary>
    static float[] Blur(float[] src, int nx, int ny, float sigma)
    {
        int r = Mathf.CeilToInt(sigma * 2.5f);
        var k = new float[2 * r + 1];
        float sum = 0f;
        for (int i = -r; i <= r; i++) sum += k[i + r] = Mathf.Exp(-(i * i) / (2f * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            float v = 0f;
            for (int a = -r; a <= r; a++) v += k[a + r] * src[j * nx + Mathf.Clamp(i + a, 0, nx - 1)];
            tmp[j * nx + i] = v;
        }
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            float v = 0f;
            for (int a = -r; a <= r; a++) v += k[a + r] * tmp[Mathf.Clamp(j + a, 0, ny - 1) * nx + i];
            dst[j * nx + i] = v;
        }
        return dst;
    }

    /// <summary>Holds a height softly between -LowestBelow and +HighestAbove: gentle near 0, flattening towards the bounds.</summary>
    public static float SoftClamp(float v) =>
        v >= 0f ? HighestAbove * (float)Math.Tanh(v / HighestAbove) : -LowestBelow * (float)Math.Tanh(-v / LowestBelow);

    static MapHeightField MakeField(Scene scene, BarangayMockSettings settings, BarangayLayout layout, GameObject root, out string why)
    {
        var e = ReadElevation(out why);
        if (e == null) return null;
        float scale = settings.scale;

        // Cover the drawn ground and the levee, with a little to spare.
        var area = MabuhayLevelBuilder.EdgeAreaWorld(settings, layout);
        var ground = root.transform.Find("Ground");
        if (ground != null)
            foreach (var r in ground.GetComponentsInChildren<Renderer>())
                area = Rect.MinMaxRect(Mathf.Min(area.xMin, r.bounds.min.x), Mathf.Min(area.yMin, r.bounds.min.z),
                                       Mathf.Max(area.xMax, r.bounds.max.x), Mathf.Max(area.yMax, r.bounds.max.z));
        area = Rect.MinMaxRect(area.xMin - 12f, area.yMin - 12f, area.xMax + 12f, area.yMax + 12f);

        var field = LoadOrMakeFieldAsset(settings);
        field.step = FieldStep;
        field.origin = new Vector2(area.xMin, area.yMin);
        field.nx = Mathf.CeilToInt(area.width / FieldStep) + 1;
        field.ny = Mathf.CeilToInt(area.height / FieldStep) + 1;
        var h = new float[field.nx * field.ny];

        // The real ground relative to the hall (the layout's origin), scaled for play and softly bounded.
        float hall = e.At(0f, 0f);
        for (int j = 0; j < field.ny; j++)
        for (int i = 0; i < field.nx; i++)
        {
            float wx = field.origin.x + i * FieldStep, wz = field.origin.y + j * FieldStep;
            h[j * field.nx + i] = SoftClamp((e.At(wx / scale, wz / scale) - hall) * VerticalScale);
        }

        // Candidate evacuation sites stand clear of the flood, unless they are on the route (the hall and its court are,
        // and the boat lands its passengers there).
        var corridor = Corridor(scene, layout, scale);
        int raised = 0;
        foreach (var site in layout.landmarks.Where(l => l.evacuationRole != EvacuationRole.None && l.positionKnown))
        {
            var outline = site.footprint != null && site.footprint.Count >= 3 ? site.footprint.Select(p => p * scale).ToList() : null;
            Vector2 centre = site.position * scale;
            if (!area.Contains(centre) || Distance(corridor, centre) < CorridorHold + CorridorEase) continue;
            raised++;
            for (int j = 0; j < field.ny; j++)
            for (int i = 0; i < field.nx; i++)
            {
                var p = new Vector2(field.origin.x + i * FieldStep, field.origin.y + j * FieldStep);
                float dist = outline != null ? (MapGeometry.Contains(outline, p) ? 0f : DistanceToRing(outline, p)) : Mathf.Max(0f, Vector2.Distance(p, centre) - 12f);
                if (dist > EvacuationEase) continue;
                float w = 1f - Mathf.SmoothStep(0f, 1f, dist / EvacuationEase);
                int k = j * field.nx + i;
                h[k] = Mathf.Max(h[k], Mathf.Lerp(h[k], EvacuationFloor, w));
            }
        }

        // Every landmark's lot is graded level, as a real site is: its average height inside the outline, easing back
        // into the slope round it. The building then sits flush with no plinth, and its yard is one depth of water.
        int pads = 0;
        foreach (var site in layout.landmarks)
        {
            if (site.footprint == null || site.footprint.Count < 3) continue;
            var ring = site.footprint.Select(p => p * scale).ToList();
            var bb = MapGeometry.BoundsOf(ring);
            if (!area.Overlaps(bb)) continue;
            float sum = 0f; int n = 0;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((bb.xMin - PadEase - field.origin.x) / FieldStep)), i1 = Mathf.Min(field.nx - 1, Mathf.CeilToInt((bb.xMax + PadEase - field.origin.x) / FieldStep));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((bb.yMin - PadEase - field.origin.y) / FieldStep)), j1 = Mathf.Min(field.ny - 1, Mathf.CeilToInt((bb.yMax + PadEase - field.origin.y) / FieldStep));
            var dist = new Dictionary<int, float>();
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                var p = new Vector2(field.origin.x + i * FieldStep, field.origin.y + j * FieldStep);
                float d = MapGeometry.Contains(ring, p) ? 0f : DistanceToRing(ring, p);
                if (d > PadEase + PadGrow) continue;
                dist[j * field.nx + i] = d;
                if (d <= PadGrow) { sum += h[j * field.nx + i]; n++; }
            }
            if (n == 0) continue;
            float level = sum / n;
            foreach (var pair in dist)
            {
                float w = pair.Value <= PadGrow ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (pair.Value - PadGrow) / PadEase);
                h[pair.Key] = Mathf.Lerp(h[pair.Key], level, w);
            }
            pads++;
        }

        // The rescue route keeps the depth it was laid out for.
        for (int j = 0; j < field.ny; j++)
        for (int i = 0; i < field.nx; i++)
        {
            var p = new Vector2(field.origin.x + i * FieldStep, field.origin.y + j * FieldStep);
            float dist = Distance(corridor, p);
            if (dist >= CorridorHold + CorridorEase) continue;
            float hold = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dist - CorridorHold) / CorridorEase));
            int k = j * field.nx + i;
            h[k] = Mathf.Lerp(h[k], 0f, hold);
        }

        field.heights = h;
        field.min = h.Min();
        field.max = h.Max();
        field.source = "SRTM 30 m via OpenTopoData (" + ElevationPath + "), smoothed " + SmoothMetres + " m, x" + VerticalScale +
                       ", held flat within " + CorridorHold + " m of the mission route; " + raised + " evacuation sites raised to +" + EvacuationFloor + " m; " + pads + " landmark lots graded level";
        EditorUtility.SetDirty(field);
        return field;
    }

    static MapHeightField LoadOrMakeFieldAsset(BarangayMockSettings settings)
    {
        PlaceholderBaker.EnsureFolder(settings.generatedFolder);
        string path = settings.generatedFolder + "/" + FieldName;
        var field = AssetDatabase.LoadAssetAtPath<MapHeightField>(path);
        if (field == null)
        {
            field = ScriptableObject.CreateInstance<MapHeightField>();
            AssetDatabase.CreateAsset(field, path);
        }
        return field;
    }

    /// <summary>The mission's route, in the world: its two streets as polylines and its places as points.</summary>
    sealed class Route { public readonly List<List<Vector2>> lines = new List<List<Vector2>>(); public readonly List<Vector2> points = new List<Vector2>(); }

    static Route Corridor(Scene scene, BarangayLayout layout, float scale)
    {
        var route = new Route();
        foreach (var road in layout.roads)
        {
            string id = road.osmId ?? "";
            if (id.StartsWith(MabuhayLevelBuilder.BagumbayanId) || id.StartsWith(MabuhayLevelBuilder.MapaladId))
                route.lines.Add(road.points.Select(p => p * scale).ToList());
        }
        foreach (var name in new[] { MabuhayLevelBuilder.AnchorsName, MabuhayLevelBuilder.HazardsName, MabuhayLevelBuilder.PostsName })
        {
            var group = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (group == null) continue;
            foreach (Transform t in group.transform) route.points.Add(new Vector2(t.position.x, t.position.z));
        }
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.GetComponentInChildren<SimpleBoat>() != null || go.GetComponent<EvacuationCenter>() != null || go.name.StartsWith("EvaCen"))
                route.points.Add(new Vector2(go.transform.position.x, go.transform.position.z));
        }
        return route;
    }

    static float Distance(Route r, Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var line in r.lines)
            for (int i = 1; i < line.Count; i++) best = Mathf.Min(best, SegmentDistance(p, line[i - 1], line[i]));
        foreach (var q in r.points) best = Mathf.Min(best, Vector2.Distance(p, q));
        return best;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static float DistanceToRing(List<Vector2> ring, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < ring.Count; i++) best = Mathf.Min(best, SegmentDistance(p, ring[i], ring[(i + 1) % ring.Count]));
        return best;
    }

    /// <summary>How much of the drawn map is under the flood at its starting level.</summary>
    static float WetShare(MapHeightField f, BarangayMockSettings settings, BarangayLayout layout)
    {
        var area = MabuhayLevelBuilder.EdgeAreaWorld(settings, layout);
        int wet = 0, all = 0;
        for (float x = area.xMin; x < area.xMax; x += 8f)
        for (float z = area.yMin; z < area.yMax; z += 8f)
        {
            all++;
            if (settings.groundHeight + f.Sample(new Vector2(x, z)) < 0f) wet++;
        }
        return all == 0 ? 0f : wet / (float)all;
    }

    // ------------------------------------------------------------------ the ground

    /// <summary>Replaces the flat ground with 64 m chunks of a 4 m grid that follows the field, each with its collider.</summary>
    static int BuildGround(Transform root, Transform group, MapHeightField field, BarangayMockSettings settings)
    {
        Material material = null; PhysicsMaterial surface = null; float y = 0f;
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic;
        Rect extent;
        if (group != null && group.GetComponentInChildren<Renderer>() != null)
        {
            var r = group.GetComponentInChildren<Renderer>();
            material = r.sharedMaterial;
            flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
            var c = group.GetComponentInChildren<MeshCollider>();
            if (c != null) surface = c.sharedMaterial;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) y = mf.sharedMesh.bounds.center.y;
            var b = r.bounds;
            extent = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            for (int i = group.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(group.GetChild(i).gameObject);
        }
        else
        {
            if (group == null) { group = new GameObject("Ground").transform; group.SetParent(root, false); }
            extent = new Rect(field.origin, new Vector2((field.nx - 1) * field.step, (field.ny - 1) * field.step));
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        string path = settings.generatedFolder + "/Terrain_Meshes.asset";
        var bundle = AssetDatabase.LoadAssetAtPath<MockMeshBundle>(path);
        if (bundle == null)
        {
            bundle = ScriptableObject.CreateInstance<MockMeshBundle>();
            bundle.note = "Generated by TerrainBuilder (Bayanihan > Build Mabuhay Gameplay Scene): the ground's chunks. Rebuilt each time.";
            AssetDatabase.CreateAsset(bundle, path);
        }
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path)) UnityEngine.Object.DestroyImmediate(o, true);

        // The root is at the ground level and scaled by the map's scale; the field is in the world.
        var toLocal = root.worldToLocalMatrix;
        float groundWorld = root.position.y;
        int count = 0;
        for (float cx = extent.xMin; cx < extent.xMax - 0.01f; cx += ChunkSize)
        for (float cz = extent.yMin; cz < extent.yMax - 0.01f; cz += ChunkSize)
        {
            float x1 = Mathf.Min(cx + ChunkSize, extent.xMax), z1 = Mathf.Min(cz + ChunkSize, extent.yMax);
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - cx) / GroundCell)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - cz) / GroundCell));
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float wx = Mathf.Lerp(cx, x1, i / (float)nx), wz = Mathf.Lerp(cz, z1, j / (float)nz);
                float wy = groundWorld + y + field.Sample(new Vector2(wx, wz));
                verts[j * (nx + 1) + i] = toLocal.MultiplyPoint3x4(new Vector3(wx, wy, wz));
                uvs[j * (nx + 1) + i] = new Vector2(wx, wz) * 0.05f;
            }
            var tris = new int[nx * nz * 6];
            int t = 0;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "Ground " + Mathf.RoundToInt(cx) + "," + Mathf.RoundToInt(cz) };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.AddObjectToAsset(mesh, bundle);

            // Every chunk is called Ground: tests and tools tell bare ground from what stands on it by that name.
            var go = new GameObject("Ground");
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (settings.colliders)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                col.sharedMaterial = surface;
            }
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            count++;
        }
        EditorUtility.SetDirty(bundle);
        return count;
    }

    // ------------------------------------------------------------------ the levee

    /// <summary>
    /// The bank's foot follows the ground along the map's edge; its crest, and the dry land beyond, are raised (never
    /// lowered) to stand clear above the highest ground at the edge. Returns how far the crest went up.
    /// </summary>
    static float LiftEdge(Transform edge, MapHeightField field, BarangayMockSettings settings)
    {
        float crest = MabuhayLevelBuilder.LeveeCrestY;
        float highest = float.MinValue;
        var bank = edge.Find("Levee bank");
        var bankFilter = bank != null ? bank.GetComponent<MeshFilter>() : null;
        if (bankFilter != null && bankFilter.sharedMesh != null)
        {
            var mesh = bankFilter.sharedMesh;
            Tessellate(mesh, MaxEdge, v => v.y < 0f);
            var m = bank.localToWorldMatrix; var inv = bank.worldToLocalMatrix;
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = m.MultiplyPoint3x4(verts[i]);
                if (w.y < 0f) { highest = Mathf.Max(highest, settings.groundHeight + field.Sample(w)); }
            }
        }
        float target = Mathf.Max(crest, highest + 0.8f);
        float lift = target - crest;
        foreach (var filter in edge.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            var m = filter.transform.localToWorldMatrix; var inv = filter.transform.worldToLocalMatrix;
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = m.MultiplyPoint3x4(verts[i]);
                if (w.y < 0f) w.y += field.Sample(w);
                else w.y += lift;
                verts[i] = inv.MultiplyPoint3x4(w);
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            Refresh(filter);
        }
        return lift;
    }

    // ------------------------------------------------------------------ the plants

    static int LiftPlants(Transform root, MapHeightField field)
    {
        int n = 0;
        foreach (var f in root.GetComponentsInChildren<InstancedFoliage>(true))
        {
            for (int i = 0; i < f.plants.Count; i++)
            {
                var p = f.plants[i];
                p.position.y += field.Sample(p.position);
                f.plants[i] = p;
                n++;
            }
            EditorUtility.SetDirty(f);
        }
        foreach (var g in root.GetComponentsInChildren<GrassField>(true))
        {
            g.heights = g.instances.Select(v => field.Sample(new Vector2(v.x, v.y))).ToList();
            n += g.instances.Count;
            EditorUtility.SetDirty(g);
        }
        return n;
    }

    // ------------------------------------------------------------------ meshes

    static void Refresh(MeshFilter filter)
    {
        var col = filter.GetComponent<MeshCollider>();
        if (col != null && col.sharedMesh == filter.sharedMesh) { col.sharedMesh = null; col.sharedMesh = filter.sharedMesh; }
    }

    /// <summary>
    /// Splits every triangle edge longer than <paramref name="maxEdge"/> (on the ground plane) until none is, carrying
    /// normals, uvs and colours along, so a flat layer can bend over the ground. Only edges whose both ends pass
    /// <paramref name="split"/> are split, when given.
    /// </summary>
    public static void Tessellate(Mesh mesh, float maxEdge, Func<Vector3, bool> split = null)
    {
        var v = new List<Vector3>(mesh.vertices);
        var n = new List<Vector3>(mesh.normals);
        var uv = new List<Vector2>(mesh.uv);
        var col = new List<Color>(mesh.colors);
        bool hasN = n.Count == v.Count, hasUV = uv.Count == v.Count, hasC = col.Count == v.Count;
        var cache = new Dictionary<long, int>();
        float max2 = maxEdge * maxEdge;

        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int m)) return m;
            v.Add((v[a] + v[b]) * 0.5f);
            if (hasN) n.Add((n[a] + n[b]).normalized);
            if (hasUV) uv.Add((uv[a] + uv[b]) * 0.5f);
            if (hasC) col.Add((col[a] + col[b]) * 0.5f);
            return cache[key] = v.Count - 1;
        }
        bool Long(int a, int b)
        {
            var d = v[a] - v[b];
            if (d.x * d.x + d.z * d.z <= max2) return false;
            return split == null || (split(v[a]) && split(v[b]));
        }

        var outSubs = new List<int[]>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var work = new Stack<(int, int, int, int)>();
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3) work.Push((tris[i], tris[i + 1], tris[i + 2], 0));
            var done = new List<int>(tris.Length);
            while (work.Count > 0)
            {
                var (a, b, c, depth) = work.Pop();
                if (depth > 14) { done.Add(a); done.Add(b); done.Add(c); continue; }
                // Split the longest edge that is too long; the triangle keeps its winding.
                float ab = (v[a] - v[b]).sqrMagnitude, bc = (v[b] - v[c]).sqrMagnitude, ca = (v[c] - v[a]).sqrMagnitude;
                if (ab >= bc && ab >= ca && Long(a, b)) { int m = Mid(a, b); work.Push((a, m, c, depth + 1)); work.Push((m, b, c, depth + 1)); }
                else if (bc >= ab && bc >= ca && Long(b, c)) { int m = Mid(b, c); work.Push((a, b, m, depth + 1)); work.Push((a, m, c, depth + 1)); }
                else if (ca >= ab && ca >= bc && Long(c, a)) { int m = Mid(c, a); work.Push((a, b, m, depth + 1)); work.Push((m, b, c, depth + 1)); }
                else if (Long(a, b)) { int m = Mid(a, b); work.Push((a, m, c, depth + 1)); work.Push((m, b, c, depth + 1)); }
                else if (Long(b, c)) { int m = Mid(b, c); work.Push((a, b, m, depth + 1)); work.Push((a, m, c, depth + 1)); }
                else if (Long(c, a)) { int m = Mid(c, a); work.Push((a, b, m, depth + 1)); work.Push((m, b, c, depth + 1)); }
                else { done.Add(a); done.Add(b); done.Add(c); }
            }
            outSubs.Add(done.ToArray());
        }

        if (v.Count == mesh.vertexCount) return;
        int subs = mesh.subMeshCount;
        mesh.Clear();
        mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        if (hasN) mesh.SetNormals(n);
        if (hasUV) mesh.SetUVs(0, uv);
        if (hasC) mesh.SetColors(col);
        mesh.subMeshCount = subs;
        for (int s = 0; s < subs; s++) mesh.SetTriangles(outSubs[s], s);
        mesh.RecalculateBounds();
    }

    // ------------------------------------------------------------------ draping

    /// <summary>Everything to move, sorted into flat layers (bent vertex by vertex) and standing things (lifted whole).</summary>
    sealed class Drape
    {
        readonly MapHeightField field;
        readonly Transform map;
        public Transform edge;
        public float edgeLift;

        readonly List<MeshFilter> layers = new List<MeshFilter>();
        readonly List<Transform> units = new List<Transform>();
        readonly HashSet<Transform> buildings = new HashSet<Transform>();
        readonly Dictionary<Collider, Transform> owner = new Dictionary<Collider, Transform>();
        readonly Dictionary<Transform, Transform> support = new Dictionary<Transform, Transform>();
        readonly HashSet<Transform> onEdge = new HashSet<Transform>();
        readonly Dictionary<Transform, float> lift = new Dictionary<Transform, float>();
        readonly List<(Vector3[] corners, float low, float high)> plinths = new List<(Vector3[], float, float)>();
        Dictionary<Mesh, int> users;

        public Drape(MapHeightField field, Transform map) { this.field = field; this.map = map; }

        public void Collect(Transform group, bool areBuildings)
        {
            if (users == null)
            {
                users = new Dictionary<Mesh, int>();
                foreach (var f in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (f.sharedMesh != null) users[f.sharedMesh] = users.TryGetValue(f.sharedMesh, out int k) ? k + 1 : 1;
            }
            foreach (Transform t in group) Sort(t, areBuildings);
        }

        void Sort(Transform t, bool building)
        {
            if (Floats(t)) return;
            bool prefab = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject);
            var filter = t.GetComponent<MeshFilter>();
            if (!prefab && filter != null && IsLayer(filter))
            {
                layers.Add(filter);
                foreach (Transform child in t) Sort(child, building);
                return;
            }
            bool hasParts = t.GetComponents<Component>().Length > 1;
            if (prefab || hasParts)
            {
                units.Add(t);
                if (building) buildings.Add(t);
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) owner[c] = t;
                return;
            }
            foreach (Transform child in t) Sort(child, building);
        }

        /// <summary>A generated, single-use mesh that spreads over the ground: bent rather than lifted.</summary>
        bool IsLayer(MeshFilter f)
        {
            var mesh = f.sharedMesh;
            if (mesh == null) return false;
            string path = AssetDatabase.GetAssetPath(mesh);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".glb" || ext == ".gltf" || ext == ".fbx" || ext == ".obj" || ext == ".blend") return false;
            if (users.TryGetValue(mesh, out int k) && k > 1) return false;
            var b = f.GetComponent<Renderer>() != null ? f.GetComponent<Renderer>().bounds : new Bounds();
            return Mathf.Max(b.size.x, b.size.z) > 12f;
        }

        /// <summary>Floating things (debris, the jam) ride the water and are left where they are.</summary>
        static bool Floats(Transform t)
        {
            if (t.gameObject.layer == 8) return true;
            var body = t.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) return true;
            return t.GetComponent<DebrisBarrier>() != null || t.GetComponent<FloatingDebris>() != null;
        }

        /// <summary>What each standing thing rests on, found while everything is still flat.</summary>
        public void FindSupports()
        {
            foreach (var u in units)
            {
                if (!Bounds(u, out var b)) continue;
                var from = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
                var hits = Physics.RaycastAll(from, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; Transform under = null; bool edgeHit = false;
                foreach (var h in hits)
                {
                    if (owner.TryGetValue(h.collider, out var o) && o == u) continue;
                    if (h.distance >= best) continue;
                    best = h.distance;
                    under = owner.TryGetValue(h.collider, out o) ? o : null;
                    edgeHit = edge != null && h.collider.transform.IsChildOf(edge);
                }
                if (under != null) support[u] = under;
                else if (edgeHit) onEdge.Add(u);
            }
        }

        public int LiftUnits()
        {
            int n = 0;
            foreach (var u in units)
            {
                float dy = Lift(u, 0);
                if (Mathf.Abs(dy) < 1e-4f) continue;
                n++;
            }
            foreach (var pair in lift)
                if (Mathf.Abs(pair.Value) >= 1e-4f) { pair.Key.position += Vector3.up * pair.Value; EditorUtility.SetDirty(pair.Key); }
            return n;
        }

        float Lift(Transform u, int depth)
        {
            if (lift.TryGetValue(u, out float known)) return known;
            float dy;
            if (support.TryGetValue(u, out var under) && depth < 8) dy = Lift(under, depth + 1);
            else if (onEdge.Contains(u)) dy = edgeLift;
            else if (buildings.Contains(u) && Footprint(u, out var corners))
            {
                field.Range(corners, out float low, out float high);
                dy = high;
                if (high - low > 0.03f) plinths.Add((corners, low, high));
            }
            else dy = field.Sample(u.position);
            lift[u] = dy;
            return dy;
        }

        /// <summary>The four world corners of a thing's drawn footprint, along its own axes, plus its middle.</summary>
        static bool Footprint(Transform u, out Vector3[] corners)
        {
            corners = null;
            var inv = u.worldToLocalMatrix;
            bool any = false; var local = new Bounds();
            foreach (var r in u.GetComponentsInChildren<MeshRenderer>())
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                var mb = f.sharedMesh.bounds; var m = inv * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
                }
            }
            if (!any) return false;
            var w = u.localToWorldMatrix;
            float y = local.min.y;
            corners = new[]
            {
                w.MultiplyPoint3x4(new Vector3(local.min.x, y, local.min.z)), w.MultiplyPoint3x4(new Vector3(local.max.x, y, local.min.z)),
                w.MultiplyPoint3x4(new Vector3(local.max.x, y, local.max.z)), w.MultiplyPoint3x4(new Vector3(local.min.x, y, local.max.z)),
                w.MultiplyPoint3x4(new Vector3(local.center.x, y, local.center.z)),
            };
            return true;
        }

        static bool Bounds(Transform u, out Bounds b)
        {
            b = new Bounds(u.position, Vector3.zero);
            bool any = false;
            foreach (var r in u.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            foreach (var c in u.GetComponentsInChildren<Collider>())
            {
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return true;
        }

        public int DrapeMeshes()
        {
            foreach (var f in layers)
            {
                var mesh = f.sharedMesh;
                Tessellate(mesh, MaxEdge);
                var m = f.transform.localToWorldMatrix; var inv = f.transform.worldToLocalMatrix;
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = m.MultiplyPoint3x4(verts[i]);
                    w.y += field.Sample(w);
                    verts[i] = inv.MultiplyPoint3x4(w);
                }
                mesh.vertices = verts;
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                Refresh(f);
            }
            return layers.Count;
        }

        /// <summary>
        /// A concrete plinth under every house on a slope, from a little below the lowest ground under it up to its floor,
        /// all in one mesh (and one collider), so the downhill side never shows a gap.
        /// </summary>
        public int BuildPlinths(Transform parent, BarangayMockSettings settings)
        {
            var old = parent.Find("Plinths");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            if (plinths.Count == 0) return 0;

            var inv = parent.worldToLocalMatrix;
            var b = new MapGeometry.MeshBuffers();
            foreach (var (corners, low, high) in plinths)
            {
                var ring = new List<Vector2>();
                for (int i = 0; i < 4; i++) { var l = inv.MultiplyPoint3x4(corners[i]); ring.Add(new Vector2(l.x, l.z)); }
                if (MapGeometry.SignedArea(ring) < 0f) ring.Reverse();
                // The corners are at the building's base before it was lifted (the flat ground); the plinth runs from
                // just under the lowest ground to the lifted base.
                float baseLocal = inv.MultiplyPoint3x4(corners[0]).y;
                float s = Mathf.Max(1e-4f, parent.lossyScale.y);
                MapGeometry.Extrude(ring, baseLocal + (low - 0.15f) / s, (high - low + 0.15f) / s, b);
            }
            var mesh = b.ToMesh("Plinths");
            string path = settings.generatedFolder + "/Plinths.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }

            var go = new GameObject("Plinths");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Placeholders/Mock_Plinth.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Mock_Plinth" };
                mat.SetColor("_BaseColor", new Color(0.62f, 0.61f, 0.58f));
                mat.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(mat, "Assets/Materials/Placeholders/Mock_Plinth.mat");
            }
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (settings.colliders) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return plinths.Count;
        }
    }
}
