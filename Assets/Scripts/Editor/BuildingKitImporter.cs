using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the low-poly building kit (Assets/3D Models/Buildings, made by Source Assets/Buildings/make_buildings.py) into
/// prefabs and the <see cref="BuildingLibrary"/>: the kit's <c>_COL</c> boxes taken out and each building made solid where
/// it is drawn (the map's rule: no invisible boxes), static, culled when small on screen. Reads Buildings_Manifest.json for
/// each model's category, storeys, footprint and triangle count. Safe to run again.
/// </summary>
public static class BuildingKitImporter
{
    public const string SourceFolder = "Assets/3D Models/Buildings";
    const string PrefabFolder = "Assets/Prefabs/Buildings";
    public const string LibraryPath = "Assets/Settings/Maps/BuildingLibrary.asset";

    [Serializable] class Footprint { public float x, z; }
    [Serializable] class Entry { public string name, category; public int storeys, triangles; public Footprint footprint; }
    [Serializable] class Manifest { public List<Entry> models; }

    public static BuildingLibrary LoadLibrary() => AssetDatabase.LoadAssetAtPath<BuildingLibrary>(LibraryPath);

    [MenuItem("Bayanihan/Buildings/Import Building Kit", false, 44)]
    public static void ImportFromMenu() => Debug.Log("BuildingKitImporter: " + ImportAll());

    public static string ImportAll()
    {
        string manifestPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), SourceFolder, "Buildings_Manifest.json");
        if (!File.Exists(manifestPath)) return "no Buildings_Manifest.json in " + SourceFolder;
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));

        PlaceholderBaker.EnsureFolder(PrefabFolder);
        PlaceholderBaker.EnsureFolder("Assets/Settings/Maps");
        var lib = LoadLibrary();
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<BuildingLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        lib.models.Clear();
        var slippery = BarangayMockBuilder.SlipperySurface();

        foreach (var e in manifest.models)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFolder + "/" + e.name + ".glb");
            if (source == null) { Debug.LogWarning("BuildingKitImporter: " + e.name + ".glb is not imported."); continue; }
            var go = UnityEngine.Object.Instantiate(source);
            go.name = e.name;
            try
            {
                // The kit's _COL box (the building's body) becomes a box collider on the drawn mesh; its renderer goes.
                Bounds body = default; bool hasBody = false;
                foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_COL")).ToList())
                {
                    var cr = t.GetComponent<Renderer>();
                    if (cr != null) { body = cr.bounds; hasBody = true; }
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                if (!go.TryGetComponent(out LODGroup lod)) lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, renderers) });
                lod.RecalculateBounds();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                var drawn = go.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.GetComponent<MeshRenderer>() != null);
                if (hasBody && drawn != null)
                {
                    var box = drawn.gameObject.AddComponent<BoxCollider>();
                    var t = drawn.transform;
                    box.center = t.InverseTransformPoint(body.center);
                    var ls = t.lossyScale;
                    box.size = new Vector3(body.size.x / Mathf.Abs(ls.x), body.size.y / Mathf.Abs(ls.y), body.size.z / Mathf.Abs(ls.z));
                    box.sharedMaterial = slippery;
                }
                else BarangayMockBuilder.SolidWhereDrawn(go, slippery);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + e.name + ".prefab");
                lib.models.Add(new InfillModel
                {
                    name = e.name, prefab = prefab, commercial = e.category == "commercial", storeys = e.storeys,
                    footprint = new Vector2(e.footprint.x, e.footprint.z), triangles = e.triangles
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return lib.models.Count + " building prefabs (" + lib.models.Count(m => m.storeys == 1) + " one-storey, " +
               lib.models.Count(m => m.storeys == 2) + " two, " + lib.models.Count(m => m.storeys == 3) + " three; " +
               lib.models.Count(m => m.commercial) + " commercial).";
    }
}
