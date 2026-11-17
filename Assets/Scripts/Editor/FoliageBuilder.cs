using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plants the open scene's barangay map with the foliage pack: runs <see cref="FoliagePlanner"/> and puts each plant
/// under BarangayMock/Foliage, one group per 64 m tile so the camera culls what it cannot see. Plants are static, so
/// Unity batches them, and they share the pack's fifteen materials.
///
/// Called by <see cref="MabuhayGameplayBuilder"/> after the mission is laid out, because the plants keep clear of the
/// boat's start, the hall's landing, the residents and the hazards. Running it again replaces what it planted before.
/// </summary>
public static class FoliageBuilder
{
    const string MapRootName = "BarangayMock";
    public const string GroupName = "Foliage";
    const float Tile = 64f;

    public static FoliageLibrary LoadLibrary() => AssetDatabase.LoadAssetAtPath<FoliageLibrary>(FoliageImporter.LibraryPath);

    /// <summary>Plants the map and returns one line saying how much went where.</summary>
    public static string Build(Scene scene, BarangayMockSettings settings, IList<KeepClear> keepClear)
    {
        var flora = LoadLibrary();
        if (flora == null || flora.species.Count == 0) return "Foliage: no library yet (Bayanihan > Foliage > Import Pack).";
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (root == null) return "Foliage: no map in the scene.";

        var old = root.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var layout = settings.Layout;
        var lib = settings.library;
        if (lib != null) lib.MeasureAll();
        var region = settings.Region(layout);
        var mock = BarangayMockPlanner.Plan(layout, region, lib);
        // The planting is worked out on the layout (real metres); the places to keep clear were measured in the world.
        float scale = Mathf.Max(0.05f, settings.scale);
        var clearOnLayout = keepClear.Select(k => new KeepClear(k.centre / scale, k.radius / scale)).ToList();
        // The whole ground out to the levee is planted; buildings are only drawn inside the region.
        var ground = MabuhayLevelBuilder.EdgeArea(settings, layout);
        // The infill buildings, as the footprints they stand on (layout metres: they are under the map's root).
        var infillBoxes = new List<(Vector2, Vector2, float)>();
        var infillGroup = root.transform.Find(InfillBuilder.GroupName);
        if (infillGroup != null)
            foreach (Transform b in infillGroup)
            {
                var mf = b.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var c = b.TransformPoint(mb.center);
                var local = root.transform.InverseTransformPoint(c);
                infillBoxes.Add((new Vector2(local.x, local.z), new Vector2(mb.extents.x, mb.extents.z) * b.localScale.x, b.localEulerAngles.y));
            }
        var plan = FoliagePlanner.Plan(layout, ground, mock, lib, flora, clearOnLayout, region, infillBoxes);

        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);

        // Everything without a trunk (grass, low plants, bushes, vines, bananas) is drawn instanced: one line each in the
        // scene, not a prefab instance. Trees keep their trunk colliders, so they stay objects.
        var undergrowth = new GameObject(UndergrowthName).AddComponent<InstancedFoliage>();
        undergrowth.transform.SetParent(group, false);
        var speciesIndex = new Dictionary<int, int>();
        float groundY = root.transform.position.y;

        var tiles = new Dictionary<Vector2Int, Transform>();
        foreach (var p in plan.placements)
        {
            var spec = flora.species[p.species];
            if (spec.prefab.GetComponentInChildren<Collider>(true) == null)
            {
                if (!speciesIndex.TryGetValue(p.species, out int si))
                {
                    si = undergrowth.species.Count;
                    speciesIndex[p.species] = si;
                    bool tall = IsTree(spec.group);
                    undergrowth.species.Add(new InstancedFoliage.Species
                    {
                        name = spec.name, prefab = spec.prefab, shadows = tall,
                        drawDistance = tall ? 300f : spec.group == FoliageGroup.Bush || spec.group == FoliageGroup.Vine ? 240f : 180f
                    });
                }
                undergrowth.plants.Add(new InstancedFoliage.Plant
                {
                    species = si,
                    position = new Vector3(p.position.x * scale, groundY, p.position.y * scale),
                    euler = new Vector3(p.leanX, p.yaw, p.leanZ),
                    scale = p.scale * (IsTree(spec.group) ? scale : 1f)
                });
                continue;
            }

            var key = new Vector2Int(Mathf.FloorToInt(p.position.x / Tile), Mathf.FloorToInt(p.position.y / Tile));
            if (!tiles.TryGetValue(key, out var tile))
            {
                tile = new GameObject("Tile " + key.x + "," + key.y).transform;
                tile.SetParent(group, false);
                GameObjectUtility.SetStaticEditorFlags(tile.gameObject, StaticEditorFlags.BatchingStatic);
                tiles[key] = tile;
            }

            var sp = flora.species[p.species];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(sp.prefab, tile);
            go.transform.localPosition = new Vector3(p.position.x, 0f, p.position.y);
            go.transform.localRotation = Quaternion.Euler(p.leanX, p.yaw, p.leanZ);
            // Under the map's root the trees grow with the map; grass, bushes and vines stay their real size beside the crew.
            go.transform.localScale = Vector3.one * (p.scale * (IsTree(sp.group) ? 1f : 1f / scale));
        }

        int blades = GrowGrass(group, plan.openGround, flora, scale);
        int edge = PlantLevee(group, root.transform, settings, layout, flora);

        // Trunks glance a hull off like every other solid thing on the map, instead of catching it.
        var slippery = BarangayMockBuilder.SlipperySurface();
        foreach (var trunk in group.GetComponentsInChildren<Collider>(true)) trunk.sharedMaterial = slippery;

        EditorUtility.SetDirty(undergrowth);
        return Summary(plan, flora) + " Drawn instanced: " + undergrowth.Count + "." + " Grassland: " + plan.Grassland + " stands over open ground, " + blades + " tufts of low grass." +
               " Along the levee: " + edge + " more.";
    }

    public const string GrassName = "Grassland";
    public const string UndergrowthName = "Undergrowth";

    /// <summary>
    /// Low grass over every open spot of ground: a patch of tufts round each, sticking up out of the flood. A handful of patch
    /// meshes are made once (assets); the patches themselves are a list on a <see cref="GrassField"/>, drawn with GPU
    /// instancing near the camera only. No collider, no shadow. Under Foliage/Grassland.
    /// </summary>
    static int GrowGrass(Transform foliage, List<Vector2> openGround, FoliageLibrary flora, float scale)
    {
        if (openGround.Count == 0 || flora.grassTuftsPerSpot <= 0) return 0;
        var holder = new GameObject(GrassName);
        holder.transform.SetParent(foliage, false);
        var field = holder.AddComponent<GrassField>();
        field.groundY = foliage.parent != null ? foliage.parent.position.y : -1f;
        float patchSize = flora.grasslandGap * scale;             // one patch covers one open spot's square
        field.patches = PatchMeshes(flora, patchSize);
        field.materials = GrassMaterials();
        var random = new System.Random(flora.seed + 7);
        int kinds = field.patches.Length * field.materials.Length;
        foreach (var spot in openGround)
        {
            Vector2 at = spot * scale;
            field.instances.Add(new Vector4(at.x, at.y, (float)random.NextDouble() * 360f, random.Next(kinds)));
        }
        EditorUtility.SetDirty(field);
        return field.instances.Count * flora.grassTuftsPerSpot;
    }

    const string GrassFolder = "Assets/Generated/Grass";

    /// <summary>Four patches of tufts, each <paramref name="size"/> metres across, blades a few centimetres wide.</summary>
    static Mesh[] PatchMeshes(FoliageLibrary flora, float size)
    {
        PlaceholderBaker.EnsureFolder(GrassFolder);
        var result = new Mesh[4];
        for (int variant = 0; variant < result.Length; variant++)
        {
            var random = new System.Random(911 + variant * 31);
            float R() => (float)random.NextDouble();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float half = size * 0.5f;
            for (int k = 0; k < flora.grassTuftsPerSpot; k++)
            {
                var at = new Vector3((R() * 2f - 1f) * half, 0f, (R() * 2f - 1f) * half);
                float height = Mathf.Lerp(flora.grassTuftHeight.x, flora.grassTuftHeight.y, R());
                int blades = 4 + random.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    float yaw = R() * Mathf.PI * 2f;
                    Vector3 across = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (0.07f + R() * 0.06f);
                    Vector3 basePos = at + new Vector3((R() - 0.5f) * 0.5f, 0f, (R() - 0.5f) * 0.5f);
                    Vector3 tip = basePos + Vector3.up * height * (0.75f + 0.25f * R()) + new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.6f;
                    int first = v.Count;
                    v.Add(basePos - across); v.Add(basePos + across); v.Add(tip);
                    Vector3 normal = Vector3.Cross(tip - basePos, across).normalized;
                    if (normal.y < 0f) normal = -normal;
                    normal = (normal + Vector3.up).normalized;               // lit like the ground, not edge-on
                    n.Add(normal); n.Add(normal); n.Add(normal);
                    t.Add(first); t.Add(first + 1); t.Add(first + 2);
                }
            }
            string path = GrassFolder + "/GrassPatch_" + variant + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.name = "GrassPatch_" + variant;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            result[variant] = mesh;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    /// <summary>Three greens, two-sided, instanced.</summary>
    static Material[] GrassMaterials()
    {
        PlaceholderBaker.EnsureFolder(FoliageImporter.MaterialFolder);
        var shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
        Color[] greens = { new Color(0.34f, 0.50f, 0.21f), new Color(0.44f, 0.58f, 0.25f), new Color(0.52f, 0.56f, 0.30f) };
        var result = new Material[greens.Length];
        for (int i = 0; i < greens.Length; i++)
        {
            string path = FoliageImporter.MaterialFolder + "/M_GrassBlades_" + i + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", greens[i]);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            m.doubleSidedGI = true;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            result[i] = m;
        }
        return result;
    }

    /// <summary>
    /// The dry land past the levee is planted too, so the edge of the map reads as land from the water: a line of trees, bamboo and
    /// bananas along the crest, and a looser scatter behind it. Same seed, same planting.
    /// </summary>
    static int PlantLevee(Transform group, Transform mapRoot, BarangayMockSettings settings, BarangayLayout layout, FoliageLibrary flora)
    {
        var pool = new List<int>();
        for (int i = 0; i < flora.species.Count; i++)
        {
            var sp = flora.species[i];
            if (sp.prefab == null || sp.size.y < flora.minStandingHeight) continue;
            if (IsTree(sp.group)) pool.Add(i);
        }
        if (pool.Count == 0) return 0;

        // Planted under the map's root, so in layout metres: the levee's run is world metres.
        var inner = MabuhayLevelBuilder.EdgeArea(settings, layout);
        float run = MabuhayLevelBuilder.LeveeRun / Mathf.Max(0.05f, settings.scale);
        var crest = Rect.MinMaxRect(inner.xMin - run, inner.yMin - run, inner.xMax + run, inner.yMax + run);
        float localY = (MabuhayLevelBuilder.LeveeCrestY - mapRoot.position.y) / Mathf.Max(0.01f, mapRoot.lossyScale.y);
        var random = new System.Random(flora.seed + 1);
        var holder = new GameObject("Levee").transform;
        holder.SetParent(group, false);
        GameObjectUtility.SetStaticEditorFlags(holder.gameObject, StaticEditorFlags.BatchingStatic);

        int planted = 0;
        void Plant(Vector2 at)
        {
            var sp = flora.species[pool[random.Next(pool.Count)]];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(sp.prefab, holder);
            go.transform.localPosition = new Vector3(at.x, localY, at.y);
            go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(sp.scale.x, sp.scale.y, (float)random.NextDouble());
            planted++;
        }

        // Walk the crest's four sides: a tree every 6-14 m, set back 2-7 m from the top of the bank, and a few further back.
        Vector2[] corners = { new Vector2(crest.xMin, crest.yMin), new Vector2(crest.xMax, crest.yMin), new Vector2(crest.xMax, crest.yMax), new Vector2(crest.xMin, crest.yMax) };
        for (int k = 0; k < 4; k++)
        {
            Vector2 a = corners[k], b = corners[(k + 1) % 4];
            float length = Vector2.Distance(a, b);
            Vector2 along = (b - a) / length, outward = new Vector2(along.y, -along.x);     // the rectangle runs counter-clockwise: out is to the right
            for (float d = (float)random.NextDouble() * 6f; d < length; d += 6f + (float)random.NextDouble() * 8f)
            {
                Plant(a + along * d + outward * (2f + (float)random.NextDouble() * 5f));
                if (random.NextDouble() < 0.45) Plant(a + along * (d + (float)random.NextDouble() * 4f) + outward * (10f + (float)random.NextDouble() * 30f));
            }
        }
        return planted;
    }

    static bool IsTree(FoliageGroup g) =>
        g == FoliageGroup.CanopyTree || g == FoliageGroup.SmallTree || g == FoliageGroup.Bamboo || g == FoliageGroup.Banana;

    public static string Summary(FoliagePlan plan, FoliageLibrary flora)
    {
        var groups = new[] { FoliageGroup.CanopyTree, FoliageGroup.SmallTree, FoliageGroup.Bamboo, FoliageGroup.Banana,
                             FoliageGroup.Bush, FoliageGroup.Vine, FoliageGroup.TallGrass };
        var habitats = new[] { FoliageHabitat.Green, FoliageHabitat.Farm, FoliageHabitat.Cemetery, FoliageHabitat.Bank,
                               FoliageHabitat.Roadside, FoliageHabitat.Yard, FoliageHabitat.OpenLot, FoliageHabitat.Wall };
        int species = plan.placements.Select(p => p.species).Distinct().Count();
        return "Foliage: " + plan.placements.Count + " plants of " + species + " species (" +
               string.Join(", ", groups.Select(g => plan.Count(g) + " " + g)) + "); by place: " +
               string.Join(", ", habitats.Where(h => plan.Count(h) > 0).Select(h => h + " " + plan.Count(h))) + ".";
    }

    /// <summary>
    /// The places no plant may stand, read from the laid-out mission: the boat's start, the hall and its landing, every
    /// resident and post, and the hazards (the debris jam, the live wire, the wreckage).
    /// </summary>
    public static List<KeepClear> KeepClearOf(Scene scene)
    {
        var list = new List<KeepClear>();
        void Add(Vector3 world, float radius) => list.Add(new KeepClear(new Vector2(world.x, world.z), radius));

        foreach (var go in scene.GetRootGameObjects())
        {
            switch (go.name)
            {
                case "Boat": Add(go.transform.position, 16f); break;
                case "EvaCen":
                case "EvaCen Landing": Add(go.transform.position, 16f); break;
                case "Mission Anchors": foreach (Transform t in go.transform) Add(t.position, 8f); break;
                case "Residents": foreach (Transform t in go.transform) Add(t.position, 8f); break;
                case "Posts": foreach (Transform t in go.transform) Add(t.position, 3f); break;
                case "Hazards": foreach (Transform t in go.transform) Add(t.position, 14f); break;
            }
        }
        // What the street and wall builders have put up: the plants keep off the poles and out of the walls.
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MapRootName);
        if (map != null)
        {
            var walls = map.transform.Find(CompoundWallBuilder.GroupName);
            if (walls != null)
                foreach (var f in walls.GetComponentsInChildren<MeshFilter>())
                    if (f.name == "Wall" && f.sharedMesh != null)
                    {
                        // A wall mesh is many pieces; its vertices every few metres along it are near enough.
                        var verts = f.sharedMesh.vertices;
                        for (int v = 0; v < verts.Length; v += 96) Add(f.transform.TransformPoint(verts[v]), 2.5f);
                    }
            var street = map.transform.Find(RoadBuilder.FurnitureName);
            if (street != null)
                foreach (Transform holder in street)
                    foreach (Transform t in holder) Add(t.position, holder.name == "Bridges" ? 10f : 1.2f);
        }
        foreach (var boat in Object.FindObjectsByType<SimpleBoat>(FindObjectsSortMode.None)) Add(boat.transform.position, 16f);
        foreach (var evac in Object.FindObjectsByType<EvacuationCenter>(FindObjectsSortMode.None)) Add(evac.transform.position, 18f);
        return list;
    }
}
