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

        return Summary(plan, flora);
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
