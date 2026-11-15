using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the prop packs in Assets/3D Models into what Barangay Mabuhay is filled with:
/// <list type="bullet">
/// <item>the corrugated roof, spring bucket and low-poly vegetation OBJs, which Unity imports as one merged, uncoloured lump each
/// (their .mtl files never came with them): read here piece by piece, one mesh per prop, coloured from the material names;</item>
/// <item>the jeepney and assembly GLBs: one prefab per jeepney and per person, the demo road left out;</item>
/// <item>the concrete-blocks pack, of which only the .mtl came: hollow-block stacks, a jersey barrier and rubble piles drawn
/// from boxes in its colours.</item>
/// </list>
/// Fills the <see cref="PropLibrary"/>, the <see cref="PropDebrisSet"/>, and adds the low-poly trees that suit Carmona (palm,
/// shrub, stump, oak, chestnut) to the <see cref="FoliageLibrary"/>. Safe to run again: it rewrites the same assets.
/// </summary>
public static class PropPackImporter
{
    const string Source = "Assets/3D Models";
    const string Root = "Assets/Generated/Props";
    const string MeshFolder = Root + "/Meshes";
    const string MaterialFolder = Root + "/Materials";
    const string PrefabFolder = "Assets/Prefabs/Props";
    public const string LibraryPath = "Assets/Settings/Maps/PropLibrary.asset";
    const string DebrisSetPath = "Assets/Resources/Props/PropDebrisSet.asset";
    /// <summary>The low-poly trees in the foliage library start with this, so a re-import can find and replace them.</summary>
    public const string TreeCodePrefix = "LP";

    /// <summary>The colours the missing .mtl files would have given, by material name (sRGB).</summary>
    static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
    {
        // corrugated roof
        { "rust_orange", "B0592C" }, { "rust_dark", "6A3B26" }, { "rust_amber", "A7743A" }, { "metal_grey", "8E9398" },
        // spring buckets
        { "plastic_blue", "2E6CC2" }, { "plastic_green", "3E9A4A" }, { "plastic_green_weathered", "6C8A5B" }, { "plastic_red", "C23C32" },
        { "plastic_yellow", "E9C12F" }, { "plastic_yellow_scuffed", "C6A84A" },
        { "label_clean", "EDE9DD" }, { "label_clean_0", "EDE9DD" }, { "label_clean_1", "EDE9DD" }, { "label_faded", "D2CEC0" },
        { "label_chipped", "DCD7C9" }, { "label_scuffed", "CEC8B8" },
        // low-poly vegetation
        { "bark_tan", "8A6B4E" }, { "bark_warm", "6D4D35" }, { "bark_grey", "7A746C" }, { "wood_core", "C9A777" },
        { "foliage_green", "4A7F31" }, { "foliage_green_deep", "2F5B2C" }, { "foliage_green_light", "74A83C" }, { "foliage_olive", "6E7A35" },
        { "frond_green", "4F8A2E" },
        { "autumn_orange", "C46A2A" }, { "autumn_plum", "6B2E40" }, { "autumn_red", "A8322C" }, { "autumn_rose", "B85A60" },
        { "blossom_pink", "E7A6B8" }, { "blossom_white", "F2EEE8" },
        // concrete (concrete-blocks.mtl, Kd as sRGB)
        { "concrete", "B3B1A9" }, { "concrete_worn", "A2A08F" }, { "mortar", "958F84" }, { "rebar_steel", "7C7268" },
    };

    /// <summary>The low-poly trees used, with how big they are made and where they grow. The rest of the pack (autumn, blossom, pine, willow, bare) is not Carmona.</summary>
    static readonly (string obj, string code, string name, float scale, FoliageGroup group, FoliageHabitat habitats, float weight)[] Trees =
    {
        ("tree01", "LP01", "Low-Poly Oak", 1.6f, FoliageGroup.CanopyTree, FoliageHabitat.Yard | FoliageHabitat.Green | FoliageHabitat.OpenLot, 0.5f),
        ("tree05", "LP05", "Low-Poly Palm", 1.6f, FoliageGroup.SmallTree, FoliageHabitat.Roadside | FoliageHabitat.Yard | FoliageHabitat.Bank, 0.8f),
        ("tree08", "LP08", "Low-Poly Chestnut", 1.5f, FoliageGroup.SmallTree, FoliageHabitat.Roadside | FoliageHabitat.Yard | FoliageHabitat.Green, 0.5f),
        ("tree09", "LP09", "Low-Poly Shrub", 1.0f, FoliageGroup.Bush, FoliageHabitat.Yard | FoliageHabitat.Roadside | FoliageHabitat.OpenLot, 0.6f),
        ("tree10", "LP10", "Low-Poly Stump", 1.4f, FoliageGroup.Bush, FoliageHabitat.OpenLot | FoliageHabitat.Bank | FoliageHabitat.Green, 0.4f),
    };

    [MenuItem("Bayanihan/Props/Import Packs", false, 41)]
    public static void ImportFromMenu() => Debug.Log("PropPackImporter: " + ImportAll());

    public static string ImportAll()
    {
        foreach (var f in new[] { MeshFolder, MaterialFolder, PrefabFolder, "Assets/Settings/Maps", "Assets/Resources/Props" })
            PlaceholderBaker.EnsureFolder(f);
        var materials = new Dictionary<string, Material>();
        var slippery = BarangayMockBuilder.SlipperySurface();

        var lib = LoadOrCreate<PropLibrary>(LibraryPath);
        var debris = LoadOrCreate<PropDebrisSet>(DebrisSetPath);

        // ---- the OBJ packs, piece by piece
        var roofs = ReadObj(Source + "/corrugated_roof_pack.obj", n => n.Substring(0, Mathf.Min(7, n.Length)));     // roof_01 ... roof_10
        // No collider: a sheet leans in the strip along a wall, where the boat cannot go anyway, and a thin tilted mesh is a poor solid.
        lib.roofSheets = roofs.Select(kv => SavePrefab("RoofSheet_" + kv.Key.Substring(5), BuildMesh(kv.Key, kv.Value, 1f), kv.Value, materials,
                                                       Solid.None, slippery, 0.04f, false)).ToList();

        var buckets = ReadObj(Source + "/spring-bucket-pack.obj", n => n.StartsWith("lid_") ? "lid" : string.Join("_", n.Split('_').Take(3)));
        buckets.Remove("lid");
        lib.buckets = buckets.Select(kv => SavePrefab("Bucket_" + kv.Key.Substring(7), BuildMesh(kv.Key, kv.Value, 1f), kv.Value, materials,
                                                      Solid.None, slippery, 0.03f, false)).ToList();

        var trees = ReadObj(Source + "/lowpoly-vegetation-pack.obj", n => n.Split('_')[0]);
        var treePrefabs = new List<(GameObject prefab, int index)>();
        for (int i = 0; i < Trees.Length; i++)
        {
            if (!trees.TryGetValue(Trees[i].obj, out var pieces)) continue;
            var t = Trees[i];
            bool tall = t.group != FoliageGroup.Bush;
            var prefab = SavePrefab(t.code + "_" + t.name.Replace("Low-Poly ", "LowPoly"), BuildMesh(t.obj, pieces, t.scale), pieces, materials,
                                    tall ? Solid.Trunk : Solid.None, slippery, tall ? 0.015f : 0.04f, tall);
            treePrefabs.Add((prefab, i));
        }

        // ---- the GLBs, one prefab per jeepney and per person
        lib.jeepneys = SplitGlb(Source + "/philippine-jeepneys.glb", n => n.StartsWith("jeepney_"), "Jeepney_", Solid.Mesh, slippery, true, 0.02f);
        var crowd = SplitGlb(Source + "/filipino-assembly-30.glb", n => true, "Person_", Solid.None, slippery, false, 0.04f);
        lib.people = crowd.Where(p => !p.name.Contains("rescuer")).ToList();
        lib.rescuers = crowd.Where(p => p.name.Contains("rescuer")).ToList();

        // ---- concrete, drawn
        lib.blockStacks = new List<GameObject> { Concrete("Concrete_BlockStack_Full", materials, slippery, BlockStack(3, 5, 8)),
                                                 Concrete("Concrete_BlockStack_Half", materials, slippery, BlockStack(3, 3, 7)),
                                                 Concrete("Concrete_BlockStack_Low", materials, slippery, BlockStack(2, 4, 6)) };
        lib.barrier = Concrete("Concrete_JerseyBarrier", materials, slippery, JerseyBarrier());
        lib.rubble = new List<GameObject> { Concrete("Concrete_Rubble_A", materials, slippery, Rubble(11)),
                                            Concrete("Concrete_Rubble_B", materials, slippery, Rubble(23)) };
        EditorUtility.SetDirty(lib);

        debris.buckets = new List<GameObject>(lib.buckets);
        debris.roofSheets = new List<GameObject>(lib.roofSheets);
        EditorUtility.SetDirty(debris);

        int trees2 = AddLowPolyTrees(AssetDatabase.LoadAssetAtPath<FoliageLibrary>(FoliageImporter.LibraryPath));
        AssetDatabase.SaveAssets();
        return lib.jeepneys.Count + " jeepneys, " + lib.roofSheets.Count + " roof sheets, " + lib.buckets.Count + " buckets, " +
               (lib.blockStacks.Count + lib.rubble.Count + 1) + " concrete pieces, " + crowd.Count + " people, " + trees2 + " low-poly trees, " +
               materials.Count + " materials.";
    }

    /// <summary>Puts the low-poly trees in the foliage library (replacing any from before). Called by the foliage import too, which clears the list.</summary>
    public static int AddLowPolyTrees(FoliageLibrary flora)
    {
        if (flora == null) return 0;
        flora.species.RemoveAll(s => s.code != null && s.code.StartsWith(TreeCodePrefix));
        int added = 0;
        foreach (var t in Trees)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + t.code + "_" + t.name.Replace("Low-Poly ", "LowPoly") + ".prefab");
            if (prefab == null) continue;
            flora.species.Add(new FoliageSpecies
            {
                code = t.code, name = t.name, prefab = prefab, group = t.group, habitats = t.habitats, weight = t.weight,
                maxLean = t.group == FoliageGroup.SmallTree ? 5f : 3f, size = PlaceholderLibrary.Measure(prefab), scale = new Vector2(0.85f, 1.15f)
            });
            added++;
        }
        EditorUtility.SetDirty(flora);
        return added;
    }

    // ------------------------------------------------------------------ reading an OBJ

    /// <summary>One OBJ object: triangles, each corner a position and a normal, in the material it is drawn in.</summary>
    sealed class Piece
    {
        public readonly Dictionary<string, List<(Vector3 p, Vector3 n)>> byMaterial = new Dictionary<string, List<(Vector3, Vector3)>>();
    }

    /// <summary>Reads an OBJ's objects and groups them into props by <paramref name="propOf"/> (object name to prop name), in file order.</summary>
    static Dictionary<string, Piece> ReadObj(string assetPath, System.Func<string, string> propOf)
    {
        var result = new Dictionary<string, Piece>();
        var order = new List<string>();
        var v = new List<Vector3>();
        var vn = new List<Vector3>();
        Piece current = null;
        string material = "default";
        var ci = CultureInfo.InvariantCulture;
        string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        foreach (var raw in File.ReadLines(full))
        {
            var line = raw.Trim();
            if (line.Length < 2) continue;
            var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "o":
                case "g":
                    string prop = propOf(parts.Length > 1 ? parts[1] : "default");
                    if (!result.TryGetValue(prop, out current)) { current = new Piece(); result[prop] = current; order.Add(prop); }
                    break;
                case "usemtl": material = parts.Length > 1 ? parts[1] : "default"; break;
                // OBJ is right-handed: x is mirrored for Unity, as its own importer does (and the triangles turned round below).
                case "v": v.Add(new Vector3(-float.Parse(parts[1], ci), float.Parse(parts[2], ci), float.Parse(parts[3], ci))); break;
                case "vn": vn.Add(new Vector3(-float.Parse(parts[1], ci), float.Parse(parts[2], ci), float.Parse(parts[3], ci))); break;
                case "f":
                    if (current == null) { current = new Piece(); result["default"] = current; order.Add("default"); }
                    if (!current.byMaterial.TryGetValue(material, out var tris)) current.byMaterial[material] = tris = new List<(Vector3, Vector3)>();
                    var corners = new List<(Vector3, Vector3)>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var idx = parts[i].Split('/');
                        int pi = int.Parse(idx[0], ci); pi = pi < 0 ? v.Count + pi : pi - 1;
                        Vector3 n = Vector3.zero;
                        if (idx.Length > 2 && idx[2].Length > 0) { int ni = int.Parse(idx[2], ci); ni = ni < 0 ? vn.Count + ni : ni - 1; n = vn[ni]; }
                        corners.Add((v[pi], n));
                    }
                    for (int i = 1; i + 1 < corners.Count; i++) { tris.Add(corners[0]); tris.Add(corners[i + 1]); tris.Add(corners[i]); }
                    break;
            }
        }
        // Keep the file's order.
        var ordered = new Dictionary<string, Piece>();
        foreach (var k in order) ordered[k] = result[k];
        return ordered;
    }

    /// <summary>One mesh for a prop: a submesh per material (in name order), its base centred on the origin, scaled.</summary>
    static Mesh BuildMesh(string name, Piece piece, float scale)
    {
        var mats = piece.byMaterial.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
        var all = mats.SelectMany(m => piece.byMaterial[m]).ToList();
        Vector3 min = all[0].p, max = all[0].p;
        foreach (var c in all) { min = Vector3.Min(min, c.p); max = Vector3.Max(max, c.p); }
        Vector3 pivot = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var submeshes = new List<int[]>();
        foreach (var m in mats)
        {
            var tris = piece.byMaterial[m];
            var indices = new int[tris.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = (tris[i].p - pivot) * scale, b = (tris[i + 1].p - pivot) * scale, c = (tris[i + 2].p - pivot) * scale;
                Vector3 face = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    indices[i + k] = positions.Count;
                    positions.Add(k == 0 ? a : k == 1 ? b : c);
                    Vector3 n = tris[i + k].n;
                    normals.Add(n.sqrMagnitude > 0.01f ? n.normalized : face);
                }
            }
            submeshes.Add(indices);
        }

        var mesh = new Mesh { name = name, indexFormat = positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return SaveMesh(mesh);
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        string path = MeshFolder + "/" + mesh.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    // ------------------------------------------------------------------ materials

    static Material MaterialFor(string name, Dictionary<string, Material> made)
    {
        if (made.TryGetValue(name, out var m)) return m;
        string path = MaterialFolder + "/Prop_" + name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        ColorUtility.TryParseHtmlString("#" + (Palette.TryGetValue(name, out var hex) ? hex : "9A9A9A"), out var colour);
        m.SetColor("_BaseColor", colour);
        bool metal = name.StartsWith("metal") || name.StartsWith("rust") || name.StartsWith("rebar");
        bool plastic = name.StartsWith("plastic") || name.StartsWith("label");
        m.SetFloat("_Metallic", metal ? (name.StartsWith("rust") ? 0.15f : 0.45f) : 0f);
        m.SetFloat("_Smoothness", plastic ? 0.45f : metal ? 0.3f : 0.08f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        made[name] = m;
        return m;
    }

    // ------------------------------------------------------------------ prefabs

    enum Solid { None, Mesh, Trunk }

    static GameObject SavePrefab(string name, Mesh mesh, Piece piece, Dictionary<string, Material> materials, Solid solid, PhysicsMaterial surface,
                                 float cullBelow, bool castsShadow)
    {
        var go = new GameObject(name);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = piece.byMaterial.Keys.OrderBy(k => k, System.StringComparer.Ordinal).Select(k => MaterialFor(k, materials)).ToArray();
            Finish(go, solid, surface, cullBelow, castsShadow);
            return PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// <summary>Shadows, culling, static flags and what is solid, as the foliage pack does.</summary>
    static void Finish(GameObject go, Solid solid, PhysicsMaterial surface, float cullBelow, bool castsShadow)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.shadowCastingMode = castsShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
        if (!go.TryGetComponent(out LODGroup lod)) lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(cullBelow, renderers) });
        lod.RecalculateBounds();
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

        if (solid == Solid.Mesh) BarangayMockBuilder.SolidWhereDrawn(go, surface);
        else if (solid == Solid.Trunk)
        {
            Vector3 size = PlaceholderLibrary.Measure(go);
            var trunk = new GameObject("Trunk") { layer = FoliageImporter.TrunkLayer };
            trunk.transform.SetParent(go.transform, false);
            var capsule = trunk.AddComponent<CapsuleCollider>();
            capsule.radius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.05f, 0.18f, 0.4f);
            capsule.height = Mathf.Min(4f, size.y * 0.6f);
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            capsule.sharedMaterial = surface;
            GameObjectUtility.SetStaticEditorFlags(trunk, StaticEditorFlags.BatchingStatic);
        }
    }

    /// <summary>
    /// One prefab per top-level part of a GLB that <paramref name="keep"/> says yes to, each moved so its base is centred on the
    /// origin. Long things (jeepneys) are turned so they run along +Z.
    /// </summary>
    static List<GameObject> SplitGlb(string path, System.Func<string, bool> keep, string prefix, Solid solid, PhysicsMaterial surface, bool castsShadow, float cullBelow)
    {
        var made = new List<GameObject>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) { Debug.LogWarning("PropPackImporter: " + path + " is not imported."); return made; }
        var top = source.transform;
        while (top.childCount == 1) top = top.GetChild(0);

        var names = new List<string>();
        for (int i = 0; i < top.childCount; i++) if (keep(top.GetChild(i).name)) names.Add(top.GetChild(i).name);

        foreach (var partName in names)
        {
            var go = new GameObject(prefix + partName);
            var whole = Object.Instantiate(source);
            try
            {
                // Lift the part out of a copy of the whole model, keeping where it is in the world (the import's own turn and scale).
                var t = whole.transform;
                while (t.childCount == 1) t = t.GetChild(0);
                var copy = t.Find(partName);
                copy.SetParent(go.transform, true);

                var b = Drawn(go);
                if (b.size.x > b.size.z * 1.5f && b.size.x > 2f)
                {
                    // Long side along x: turn it to run along z, the way a placed model faces.
                    copy.RotateAround(b.center, Vector3.up, 90f);
                    b = Drawn(go);
                }
                copy.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                Finish(go, solid, surface, cullBelow, castsShadow);
                made.Add(PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + go.name + ".prefab"));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(whole);
            }
        }
        return made;
    }

    static Bounds Drawn(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ------------------------------------------------------------------ concrete, drawn from boxes

    /// <summary>A box in a prop: its centre, size, turn and material.</summary>
    struct Box
    {
        public Vector3 centre, size, euler;
        public string material;
        public Box(Vector3 centre, Vector3 size, string material, Vector3 euler = default) { this.centre = centre; this.size = size; this.material = material; this.euler = euler; }
    }

    /// <summary>A pallet of hollow blocks (40 × 20 × 15 cm): <paramref name="rows"/> × <paramref name="cols"/> blocks a layer, <paramref name="layers"/> high, on a timber pallet.</summary>
    static List<Box> BlockStack(int rows, int cols, int layers)
    {
        var boxes = new List<Box>();
        const float bx = 0.4f, by = 0.2f, bz = 0.15f, gap = 0.012f;
        float w = cols * (bx + gap), d = rows * (bz * 2f + gap);
        boxes.Add(new Box(new Vector3(0f, 0.06f, 0f), new Vector3(w + 0.06f, 0.12f, d + 0.06f), "wood_core"));
        var random = new System.Random(rows * 97 + cols * 13 + layers);
        for (int l = 0; l < layers; l++)
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (l == layers - 1 && random.NextDouble() < 0.25) continue;            // a few taken from the top
                    float x = -w * 0.5f + (c + 0.5f) * (bx + gap) + (float)(random.NextDouble() - 0.5) * 0.02f;
                    float z = -d * 0.5f + (r + 0.5f) * (bz * 2f + gap);
                    string m = random.NextDouble() < 0.3 ? "concrete_worn" : "concrete";
                    boxes.Add(new Box(new Vector3(x, 0.12f + (l + 0.5f) * by, z), new Vector3(bx, by, bz * 2f), m, new Vector3(0f, (float)(random.NextDouble() - 0.5) * 3f, 0f)));
                }
        return boxes;
    }

    /// <summary>A 2 m jersey barrier: a wide foot, a sloped waist (two turned boxes) and a narrow top.</summary>
    static List<Box> JerseyBarrier() => new List<Box>
    {
        new Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.6f, 0.24f, 2f), "concrete"),
        new Box(new Vector3(0f, 0.42f, 0f), new Vector3(0.42f, 0.4f, 2f), "concrete"),
        new Box(new Vector3(0f, 0.72f, 0f), new Vector3(0.24f, 0.22f, 2f), "concrete_worn"),
        new Box(new Vector3(0f, 0.81f, 0.9f), new Vector3(0.25f, 0.04f, 0.2f), "rebar_steel"),
    };

    /// <summary>A pile of broken slab and blocks with rebar sticking out of it.</summary>
    static List<Box> Rubble(int seed)
    {
        var random = new System.Random(seed);
        float R() => (float)random.NextDouble();
        var boxes = new List<Box>();
        for (int i = 0; i < 14; i++)
        {
            float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * 1.3f;
            float h = 0.15f + R() * 0.3f;
            float y = Mathf.Lerp(1.6f, 0.1f, d / 1.3f) * (0.5f + R() * 0.5f);
            var size = new Vector3(0.4f + R() * 0.9f, h, 0.3f + R() * 0.8f);
            boxes.Add(new Box(new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d), size, R() < 0.5f ? "concrete" : R() < 0.5f ? "concrete_worn" : "mortar",
                              new Vector3((R() - 0.5f) * 40f, R() * 180f, (R() - 0.5f) * 40f)));
        }
        for (int i = 0; i < 5; i++)
        {
            float a = R() * Mathf.PI * 2f, d = R() * 0.9f;
            boxes.Add(new Box(new Vector3(Mathf.Cos(a) * d, 1.4f + R() * 0.4f, Mathf.Sin(a) * d), new Vector3(0.03f, 1.0f + R() * 0.6f, 0.03f), "rebar_steel",
                              new Vector3((R() - 0.5f) * 70f, R() * 180f, (R() - 0.5f) * 70f)));
        }
        return boxes;
    }

    static GameObject Concrete(string name, Dictionary<string, Material> materials, PhysicsMaterial surface, List<Box> boxes)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var mats = boxes.Select(b => b.material).Distinct().OrderBy(m => m, System.StringComparer.Ordinal).ToList();
        var combines = new List<CombineInstance>();
        foreach (var m in mats)
        {
            var parts = boxes.Where(b => b.material == m).Select(b => new CombineInstance
            {
                mesh = cube, transform = Matrix4x4.TRS(b.centre, Quaternion.Euler(b.euler), b.size)
            }).ToArray();
            var sub = new Mesh();
            sub.CombineMeshes(parts, true, true);
            combines.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
        }
        var mesh = new Mesh { name = name };
        mesh.CombineMeshes(combines.ToArray(), false, true);
        foreach (var c in combines) Object.DestroyImmediate(c.mesh);
        // The base sits on the ground.
        var bounds = mesh.bounds;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);

        var go = new GameObject(name);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.Select(m => MaterialFor(m, materials)).ToArray();
            Finish(go, Solid.Mesh, surface, 0.03f, true);
            return PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(go); }
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
