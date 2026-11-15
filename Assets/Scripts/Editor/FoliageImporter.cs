using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the "Carmona Roadside Flora" pack (Assets/3D Models/Foliage, 45 GLBs) into what the map is planted with:
/// one shared set of matte materials from the pack's palette, a prefab per model (trees get a thin trunk collider), the
/// <see cref="FoliageLibrary"/> saying where each species grows, and the <see cref="FoliageDebrisSet"/> for the flood.
/// Safe to run again: it rewrites the same assets.
/// </summary>
public static class FoliageImporter
{
    public const string SourceFolder = "Assets/3D Models/Foliage";
    public const string MaterialFolder = "Assets/Materials/Foliage";
    public const string PrefabFolder = "Assets/Prefabs/Foliage";
    public const string LibraryPath = "Assets/Settings/Maps/FoliageLibrary.asset";
    public const string DebrisSetPath = "Assets/Resources/Foliage/FoliageDebrisSet.asset";

    /// <summary>The trunk colliders go on this layer: the boat and the wreckage bump into trunks, sight lines and thrown lines pass them.</summary>
    public const int TrunkLayer = 2;     // Ignore Raycast

    /// <summary>The pack's palette (README, sRGB).</summary>
    static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
    {
        { "M_Bark", "5E4E42" }, { "M_BarkLight", "8C8073" }, { "M_BarkDark", "45382F" },
        { "M_LeafDeep", "24442A" }, { "M_LeafDark", "2F5B2C" }, { "M_LeafMid", "4A7F31" }, { "M_LeafLight", "74A83C" },
        { "M_LeafYellow", "A2B544" }, { "M_DryLeaf", "B89A55" },
        { "M_VineDark", "2B5527" }, { "M_VineMid", "3F7A2E" }, { "M_VineLight", "64A23A" },
        { "M_GrassDark", "4B7A2C" }, { "M_GrassMid", "6E9E38" }, { "M_GrassLight", "9CC04E" },
    };

    const FoliageHabitat Road = FoliageHabitat.Roadside, Yard = FoliageHabitat.Yard, Open = FoliageHabitat.OpenLot,
        Green = FoliageHabitat.Green, Farm = FoliageHabitat.Farm, Cem = FoliageHabitat.Cemetery, Bank = FoliageHabitat.Bank,
        Wall = FoliageHabitat.Wall;

    /// <summary>Where each species grows and how common it is there.</summary>
    static readonly Dictionary<string, (FoliageGroup group, FoliageHabitat habitats, float weight, float lean)> Species =
        new Dictionary<string, (FoliageGroup, FoliageHabitat, float, float)>
    {
        { "T01", (FoliageGroup.CanopyTree, Yard | Green | Cem | Road, 1.0f, 3f) },
        { "T02", (FoliageGroup.CanopyTree, Yard | Road | Green | Farm, 1.4f, 3f) },
        { "T03", (FoliageGroup.CanopyTree, Green | Cem | Road | Open, 0.9f, 2f) },
        { "T04", (FoliageGroup.SmallTree, Road | Green | Open | Yard, 0.9f, 4f) },
        { "T05", (FoliageGroup.SmallTree, Open | Road | Bank, 0.9f, 6f) },
        { "T06", (FoliageGroup.SmallTree, Open | Road, 0.6f, 5f) },
        { "T07", (FoliageGroup.SmallTree, Open | Bank | Green, 0.8f, 4f) },
        { "T08", (FoliageGroup.SmallTree, Road | Yard, 1.2f, 3f) },
        { "T09", (FoliageGroup.CanopyTree, Road | Green | Cem, 0.8f, 2f) },
        { "T10", (FoliageGroup.CanopyTree, Green | Bank | Open, 0.7f, 3f) },
        { "T11", (FoliageGroup.Vine, Green | Bank | Open, 0.6f, 2f) },
        { "T12", (FoliageGroup.SmallTree, Yard | Open | Road | Farm, 0.8f, 7f) },
        { "T13", (FoliageGroup.Bamboo, Bank | Green | Farm | Open, 1.0f, 3f) },
        { "T14", (FoliageGroup.CanopyTree, Green | Cem, 0.5f, 1f) },
        { "T15", (FoliageGroup.SmallTree, Open | Road, 0.5f, 5f) },
        { "G01", (FoliageGroup.TallGrass, Open | Road, 1.0f, 6f) },
        { "G02", (FoliageGroup.TallGrass, Open | Road | Bank | Farm, 2.2f, 6f) },
        { "G03", (FoliageGroup.TallGrass, Open | Bank | Green, 2.0f, 6f) },
        { "G04", (FoliageGroup.TallGrass, Open | Road | Bank, 1.8f, 6f) },
        { "G05", (FoliageGroup.TallGrass, Open | Bank | Green, 1.5f, 6f) },
        { "G06", (FoliageGroup.LowPlant, Open | Road, 1f, 6f) },
        { "G07", (FoliageGroup.LowPlant, Open | Road, 1f, 4f) },
        { "G08", (FoliageGroup.LowPlant, Open | Road, 1f, 6f) },
        { "G09", (FoliageGroup.TallGrass, Open | Road | Farm, 1.8f, 6f) },
        { "G10", (FoliageGroup.LowPlant, Open | Road, 1f, 6f) },
        { "G11", (FoliageGroup.LowPlant, Road, 1f, 2f) },
        { "G12", (FoliageGroup.LowPlant, Open, 1f, 6f) },
        { "G13", (FoliageGroup.LowPlant, Open | Bank, 1f, 6f) },
        { "G14", (FoliageGroup.LowPlant, Open, 1f, 4f) },
        { "G15", (FoliageGroup.LowPlant, Open | Road, 1f, 2f) },
        { "F01", (FoliageGroup.Banana, Yard | Farm | Bank, 1.0f, 5f) },
        { "F02", (FoliageGroup.Banana, Yard | Farm | Bank | Open, 1.2f, 5f) },
        { "F03", (FoliageGroup.Banana, Yard | Farm | Bank | Open, 0.8f, 6f) },
        { "F04", (FoliageGroup.Banana, Yard | Farm | Bank, 1.0f, 4f) },
        { "F05", (FoliageGroup.Banana, Yard | Farm | Open, 0.8f, 5f) },
        { "F06", (FoliageGroup.LowPlant, Yard | Green, 1f, 3f) },
        { "F07", (FoliageGroup.Bush, Yard | Green | Cem | Road, 1.0f, 3f) },
        { "F08", (FoliageGroup.Bush, Yard | Green | Cem | Road, 1.0f, 3f) },
        { "F09", (FoliageGroup.Vine, Green | Open | Bank, 0.7f, 2f) },
        { "F10", (FoliageGroup.Vine, Wall, 1.0f, 0f) },
        { "F11", (FoliageGroup.LowPlant, Open | Green, 1f, 2f) },
        { "F12", (FoliageGroup.LowPlant, Bank | Yard, 1f, 4f) },
        { "F13", (FoliageGroup.LowPlant, Green | Bank, 1f, 4f) },
        { "F14", (FoliageGroup.Bush, Open | Road | Bank, 1.2f, 4f) },
        { "F15", (FoliageGroup.Bush, Open | Road, 1.1f, 4f) },
    };

    [MenuItem("Bayanihan/Foliage/Import Pack", false, 40)]
    public static string ImportAll()
    {
        var materials = MakeMaterials();
        PlaceholderBaker.EnsureFolder(PrefabFolder);
        PlaceholderBaker.EnsureFolder("Assets/Settings/Maps");
        PlaceholderBaker.EnsureFolder("Assets/Resources/Foliage");

        var library = AssetDatabase.LoadAssetAtPath<FoliageLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<FoliageLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.species.Clear();

        var prefabs = new Dictionary<string, GameObject>();
        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { SourceFolder });
        var paths = new List<string>();
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith(".glb")) paths.Add(p);
        }
        paths.Sort(System.StringComparer.Ordinal);

        foreach (var path in paths)
        {
            string file = Path.GetFileNameWithoutExtension(path);          // T01_MangoTreeLarge
            string code = file.Substring(0, 3);
            if (!Species.TryGetValue(code, out var info)) continue;
            var prefab = MakePrefab(path, file, info.group, materials);
            if (prefab == null) continue;
            prefabs[code] = prefab;
            library.species.Add(new FoliageSpecies
            {
                code = code, name = Readable(file.Substring(4)), prefab = prefab, group = info.group, habitats = info.habitats,
                weight = info.weight, maxLean = info.lean, size = PlaceholderLibrary.Measure(prefab),
                scale = info.group == FoliageGroup.Vine && code == "F10" ? new Vector2(0.9f, 1.05f) : new Vector2(0.85f, 1.2f)
            });
        }
        PropPackImporter.AddLowPolyTrees(library);          // the low-poly palm, shrub, stump, oak and chestnut, if imported
        EditorUtility.SetDirty(library);

        var debris = AssetDatabase.LoadAssetAtPath<FoliageDebrisSet>(DebrisSetPath);
        if (debris == null)
        {
            debris = ScriptableObject.CreateInstance<FoliageDebrisSet>();
            AssetDatabase.CreateAsset(debris, DebrisSetPath);
        }
        debris.branch = prefabs.TryGetValue("T12", out var b) ? b : null;          // a torn-off sapling
        debris.bananaLeaf = prefabs.TryGetValue("F01", out var bl) ? bl : null;    // an uprooted young banana
        debris.grassMat = prefabs.TryGetValue("G07", out var gm) ? gm : null;      // a mat of cogon
        debris.weedRaft = prefabs.TryGetValue("F11", out var wr) ? wr : null;      // a raft of creeper
        EditorUtility.SetDirty(debris);
        AssetDatabase.SaveAssets();

        int standing = library.species.FindAll(s => s.size.y >= library.minStandingHeight).Count;
        return library.species.Count + " species (" + standing + " tall enough to stand in the flood), " + materials.Count + " materials.";
    }

    /// <summary>The palette as the pack's README lists it ("M_GrassDry   #C7B068"), so a material the pack adds is picked up too.</summary>
    static Dictionary<string, string> ReadPalette()
    {
        var palette = new Dictionary<string, string>(Palette);
        string readme = Path.Combine(Path.GetDirectoryName(Application.dataPath), SourceFolder, "README.txt");
        if (!File.Exists(readme)) return palette;
        foreach (var line in File.ReadAllLines(readme))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^(M_\w+)\s+#([0-9A-Fa-f]{6})");
            if (m.Success) palette[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return palette;
    }

    static Dictionary<string, Material> MakeMaterials()
    {
        PlaceholderBaker.EnsureFolder(MaterialFolder);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var result = new Dictionary<string, Material>();
        foreach (var kv in ReadPalette())
        {
            string path = MaterialFolder + "/" + kv.Key + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            ColorUtility.TryParseHtmlString("#" + kv.Value, out var c);
            m.shader = shader;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            result[kv.Key] = m;
        }
        return result;
    }

    static GameObject MakePrefab(string sourcePath, string file, FoliageGroup group, Dictionary<string, Material> materials)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null) return null;
        var go = Object.Instantiate(source);
        go.name = file;
        try
        {
            // Only what stands tall casts a shadow worth its cost on the water; grass, bushes and vines do not.
            bool castsShadow = group == FoliageGroup.CanopyTree || group == FoliageGroup.SmallTree ||
                               group == FoliageGroup.Bamboo || group == FoliageGroup.Banana;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string key = mats[i] != null ? mats[i].name.Split(' ')[0] : "";
                    if (materials.TryGetValue(key, out var shared)) mats[i] = shared;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = castsShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
            }

            // Culled once it is small on screen: grass and bushes go first, the big shade trees stay out to the horizon.
            if (!go.TryGetComponent(out LODGroup lod)) lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(CullBelow(group), renderers) });
            lod.RecalculateBounds();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            if (group == FoliageGroup.CanopyTree || group == FoliageGroup.SmallTree || group == FoliageGroup.Bamboo)
            {
                Vector3 size = PlaceholderLibrary.Measure(go);
                var trunk = new GameObject("Trunk") { layer = TrunkLayer };
                trunk.transform.SetParent(go.transform, false);
                var capsule = trunk.AddComponent<CapsuleCollider>();
                float r = group == FoliageGroup.Bamboo ? Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.18f, 0.5f, 1.2f)
                                                       : Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.05f, 0.18f, 0.45f);
                capsule.radius = r;
                capsule.height = Mathf.Min(4f, size.y * 0.6f);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                GameObjectUtility.SetStaticEditorFlags(trunk, StaticEditorFlags.BatchingStatic);
            }

            string path = PrefabFolder + "/" + file + ".prefab";
            return PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>The share of the screen's height below which a plant is not drawn at all.</summary>
    static float CullBelow(FoliageGroup group)
    {
        switch (group)
        {
            case FoliageGroup.CanopyTree:
            case FoliageGroup.Bamboo: return 0.015f;
            case FoliageGroup.SmallTree:
            case FoliageGroup.Banana: return 0.03f;
            case FoliageGroup.Vine: return 0.035f;
            default: return 0.05f;               // grass, bushes, low plants
        }
    }

    /// <summary>"MangoTreeLarge" to "Mango Tree Large".</summary>
    static string Readable(string camel)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < camel.Length; i++)
        {
            char ch = camel[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(camel[i - 1])) sb.Append(' ');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
