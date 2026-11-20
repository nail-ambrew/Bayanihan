using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// The last step of the Mabuhay build. Every builder before it works on the map as it lays it out; this sorts the
/// finished map by what things are, and bakes the NavMesh. Under BarangayMock:
///  - Terrain: the ground, the lot tints, the streams, and the plinths the houses stand on (Foundations).
///  - Roads: the street surfaces and paint, and the street furniture (bridges, poles, wires, lamps, signs, crossings).
///  - Residential: the rows from the residential pack (BAY_I_).
///  - Commercial: the rows from the commercial pack (BAY_F_), and the plain blocks of factories, shops and the like.
///  - Landmarks: the landmark models, their compound walls and their name signs.
///  - Foliage and Props, as they were.
/// Code that looks for a group finds it with <see cref="MapGroups"/> paths (see Docs/Systems/Map and Props.md).
///
/// The whole-map meshes (street surfaces, lot tints, streams, plinths) are then cut into 64 m tiles so only the part in
/// view is drawn (<see cref="MeshTiler"/>).
///
/// The NavMesh (a NavMeshSurface on BarangayMock, humanoid, from the colliders) covers the dry land: everything below
/// the flood's starting level is marked not walkable, so nothing walks under water. Nothing uses it yet - the boat, the
/// swimmer and the residents move by physics - but onlookers and any future walkers can.
/// </summary>
public static class MapFinisher
{
    public const string NavMeshAsset = "MabuhayNavMesh.asset";

    public static string Finish(Scene scene, BarangayMockSettings settings)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BarangayMock");
        if (root == null) return "Finish: no map.";
        string sorted = Organise(root.transform);
        string tiled = MeshTiler.Tile(root.transform, settings.generatedFolder);
        string nav = BakeNavMesh(root, settings);
        return sorted + " " + tiled + " " + nav;
    }

    // ------------------------------------------------------------------ sorting

    static string Organise(Transform root)
    {
        // Old groups whose names a category takes are renamed out of the way first.
        Transform Old(string name)
        {
            var t = root.Find(name);
            if (t != null) t.name = "_" + name;
            return t;
        }
        Transform Category(string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            return g;
        }
        void Move(Transform child, Transform to) { if (child != null) child.SetParent(to, true); }
        void MoveChildren(Transform from, Transform to)
        {
            if (from == null) return;
            foreach (var c in from.Cast<Transform>().ToList()) c.SetParent(to, true);
        }
        void Drop(Transform t) { if (t != null && t.childCount == 0 && t.GetComponents<Component>().Length == 1) Object.DestroyImmediate(t.gameObject); }

        var oldRoads = Old("Roads");
        var oldLandmarks = Old("Landmarks");
        var ground = root.Find("Ground");
        var areas = root.Find("Areas");
        var water = root.Find("Water");
        var buildings = root.Find("Buildings");
        var furniture = root.Find("Street Furniture");
        var walls = root.Find(CompoundWallBuilder.GroupName);
        var signs = root.Find("Signs");

        var terrain = Category(MapGroups.Terrain);
        var roads = Category(MapGroups.Roads);
        var residential = Category(MapGroups.Residential);
        var commercial = Category(MapGroups.Commercial);
        var landmarks = Category(MapGroups.Landmarks);

        Move(ground, terrain);
        Move(areas, terrain);
        Move(water, terrain);
        MoveChildren(oldRoads, roads);
        Move(furniture, roads);
        MoveChildren(oldLandmarks, landmarks);
        Move(walls, landmarks);
        Move(signs, landmarks);

        int res = 0, com = 0;
        if (buildings != null)
        {
            foreach (var c in buildings.Cast<Transform>().ToList())
            {
                if (c.name == "Plinths") { c.name = "Foundations"; c.SetParent(terrain, true); }
                else if (c.name.Contains("(BAY_I_")) { c.SetParent(residential, true); res++; }
                else { c.SetParent(commercial, true); com++; }
            }
        }
        Drop(oldRoads); Drop(oldLandmarks); Drop(buildings);

        // The rest (Foliage, Props) stay, after the five categories.
        foreach (var name in new[] { FoliageBuilder.GroupName, PropBuilder.GroupName })
        {
            var t = root.Find(name);
            if (t != null) t.SetAsLastSibling();
        }
        return "Sorted: " + res + " residential, " + com + " commercial (rows and blocks).";
    }

    // ------------------------------------------------------------------ the NavMesh

    static string BakeNavMesh(GameObject root, BarangayMockSettings settings)
    {
        foreach (var old in root.GetComponents<NavMeshSurface>()) Object.DestroyImmediate(old);
        foreach (var old in root.GetComponents<NavMeshModifierVolume>()) Object.DestroyImmediate(old);

        var surface = root.AddComponent<NavMeshSurface>();
        surface.agentTypeID = 0;                                    // Humanoid
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~((1 << 8) | (1 << 30));                // not the floating debris, not the packing stage
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.25f;
        surface.overrideTileSize = true;
        surface.tileSize = 128;

        // Under the flood's starting level is not walkable: one volume from far below up to the water's top (world y 0).
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var wet = root.AddComponent<NavMeshModifierVolume>();
        float bottom = -40f, top = 0.05f;
        var centreWorld = new Vector3(bounds.center.x, (bottom + top) * 0.5f, bounds.center.z);
        wet.center = root.transform.InverseTransformPoint(centreWorld);
        var s = root.transform.lossyScale;
        wet.size = new Vector3(bounds.size.x / s.x + 20f, (top - bottom) / s.y, bounds.size.z / s.z + 20f);
        wet.area = 1;                                              // Not Walkable

        surface.BuildNavMesh();
        var data = surface.navMeshData;
        if (data == null) return "NavMesh: the bake made nothing.";
        string path = settings.generatedFolder + "/" + NavMeshAsset;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        EditorUtility.SetDirty(surface);

        var tri = NavMesh.CalculateTriangulation();
        float area = 0f;
        for (int i = 0; i < tri.indices.Length; i += 3)
        {
            Vector3 a = tri.vertices[tri.indices[i]], b = tri.vertices[tri.indices[i + 1]], c = tri.vertices[tri.indices[i + 2]];
            area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        return "NavMesh: " + Mathf.RoundToInt(area) + " m² of dry walkable ground baked (" + path + ").";
    }
}
