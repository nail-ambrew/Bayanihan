using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the RoadKit (Assets/3D Models/RoadKit, from Source Assets/RoadKit.zip) into what the Mabuhay map uses: a prefab per
/// piece it places, with the kit's invisible <c>_COL</c> boxes taken out and each drawn part made solid where it is drawn,
/// and the <see cref="RoadKitLibrary"/> that names them. The road surfaces themselves are swept along the real streets by
/// <see cref="RoadBuilder"/> in the kit's materials, so only the materials are taken from the road pieces.
/// Safe to run again: it rewrites the same assets.
/// </summary>
public static class RoadKitImporter
{
    public const string SourceFolder = "Assets/3D Models/RoadKit";
    const string PrefabFolder = "Assets/Prefabs/RoadKit";
    public const string LibraryPath = "Assets/Settings/Maps/RoadKitLibrary.asset";

    [MenuItem("Bayanihan/Roads/Import RoadKit", false, 43)]
    public static void ImportFromMenu() => Debug.Log("RoadKitImporter: " + ImportAll());

    public static RoadKitLibrary LoadLibrary() => AssetDatabase.LoadAssetAtPath<RoadKitLibrary>(LibraryPath);

    public static string ImportAll()
    {
        PlaceholderBaker.EnsureFolder(PrefabFolder);
        PlaceholderBaker.EnsureFolder("Assets/Settings/Maps");
        var lib = LoadLibrary();
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<RoadKitLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        var slippery = BarangayMockBuilder.SlipperySurface();

        // The kit's materials, as its importer made them: every piece shares them.
        var materials = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { SourceFolder }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && !materials.ContainsKey(m.name)) materials[m.name] = m;
            if (materials.Count >= 15) break;
        }
        Material Mat(string n) => materials.TryGetValue(n, out var m) ? m : null;
        lib.asphalt = Mat("Asphalt");
        lib.concrete = Mat("Concrete");
        lib.sidewalk = Mat("Sidewalk") ?? lib.concrete;
        lib.dirt = Mat("Dirt") ?? lib.asphalt;
        lib.paintWhite = Mat("PaintWhite");
        lib.paintYellow = Mat("PaintYellow") ?? lib.paintWhite;

        int made = 0;
        GameObject Piece(string file, bool solid, bool shadows, float cullBelow)
        {
            var p = MakePrefab(SourceFolder + "/" + file + ".glb", solid, shadows, cullBelow, slippery);
            if (p != null) made++;
            return p;
        }
        lib.bridge2Lane = Piece("Bridges/Bridge_Concrete_2Lane_16m", true, true, 0.01f);
        lib.bridge4Lane = Piece("Bridges/Bridge_Concrete_4Lane_16m", true, true, 0.01f);
        lib.bridgeWood1Lane = Piece("Bridges/Bridge_Wood_1Lane_16m", true, true, 0.01f);
        // Not solid: a footbridge is a walkway high over the street, and the boat passes under it.
        lib.footbridge = Piece("Bridges/Bridge_Footbridge_Pedestrian_20m", false, true, 0.01f);
        lib.utilityPole = Piece("Props/Pole_Utility_Wood", true, true, 0.012f);
        lib.utilityPoleConcrete = Piece("Props/Pole_Utility_Concrete", true, true, 0.012f);
        lib.wireSpan = Piece("Props/Wire_Span_8m", false, false, 0.02f);
        lib.lamp = Piece("Props/Lamp_Modern_SingleArm", true, true, 0.015f);
        lib.stopSign = Piece("Props/Sign_Stop", true, false, 0.03f);
        lib.streetNameSign = Piece("Props/Sign_StreetName", true, false, 0.03f);
        lib.trafficLight = Piece("Props/TrafficLight_Pole", true, true, 0.02f);
        lib.crosswalk7 = Piece("Markings/Marking_Crosswalk_Zebra_7m", false, false, 0.03f);
        lib.metalFence = Piece("Props/Fence_Metal_4m", true, false, 0.02f);

        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return made + " RoadKit prefabs, " + materials.Count + " kit materials (asphalt " + (lib.asphalt != null) + ").";
    }

    /// <summary>
    /// A prefab of one kit piece: the <c>_COL</c> boxes removed (the kit's convention: an invisible box per piece), each drawn
    /// part solid where it is drawn if <paramref name="solid"/>, static, culled once small on screen.
    /// </summary>
    static GameObject MakePrefab(string path, bool solid, bool shadows, float cullBelow, PhysicsMaterial surface)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) { Debug.LogWarning("RoadKitImporter: " + path + " is not imported."); return null; }
        var go = Object.Instantiate(source);
        go.name = source.name;
        try
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_COL")).ToList())
                Object.DestroyImmediate(t.gameObject);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            if (!go.TryGetComponent(out LODGroup lod)) lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullBelow, renderers) });
            lod.RecalculateBounds();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            if (solid) BarangayMockBuilder.SolidWhereDrawn(go, surface);
            return PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + go.name + ".prefab");
        }
        finally { Object.DestroyImmediate(go); }
    }
}
