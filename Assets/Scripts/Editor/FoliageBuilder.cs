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
        var plan = FoliagePlanner.Plan(layout, region, mock, lib, flora, keepClear);

        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);
        var tiles = new Dictionary<Vector2Int, Transform>();
        foreach (var p in plan.placements)
        {
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
            go.transform.localScale = Vector3.one * p.scale;
        }

        int edge = PlantLevee(group, root.transform, settings, layout, flora);

        // Trunks glance a hull off like every other solid thing on the map, instead of catching it.
        var slippery = BarangayMockBuilder.SlipperySurface();
        foreach (var trunk in group.GetComponentsInChildren<Collider>(true)) trunk.sharedMaterial = slippery;

        return Summary(plan, flora) + " Along the levee: " + edge + " more.";
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
            if (sp.group == FoliageGroup.CanopyTree || sp.group == FoliageGroup.SmallTree || sp.group == FoliageGroup.Bamboo || sp.group == FoliageGroup.Banana)
                pool.Add(i);
        }
        if (pool.Count == 0) return 0;

        var inner = MabuhayLevelBuilder.EdgeArea(settings, layout);
        float run = MabuhayLevelBuilder.LeveeRun;
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
        foreach (var boat in Object.FindObjectsByType<SimpleBoat>(FindObjectsSortMode.None)) Add(boat.transform.position, 16f);
        foreach (var evac in Object.FindObjectsByType<EvacuationCenter>(FindObjectsSortMode.None)) Add(evac.transform.position, 18f);
        return list;
    }
}
