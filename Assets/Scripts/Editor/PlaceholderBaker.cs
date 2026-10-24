using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the project's existing 3D models into the placeholders a generated map is built from, and fills the
/// Placeholder Library with them.
///
/// The slum-map houses, the hall, the market and the school blocks are each hundreds of small parts under a tile that
/// has been laid out by hand. A map with a thousand houses cannot afford that, so each one is baked into a single mesh
/// (one submesh per material), re-centred so its pivot is the middle of its footprint at ground level, and turned so
/// its front faces +Z. Every placeholder prefab follows that one convention, which is also what a final model needs to
/// follow to replace one.
///
/// Run it again after changing the source models: the meshes and prefabs are rewritten in place, so nothing that points
/// at them breaks. Entries you added to the library by hand (a model with no source) are left alone.
/// </summary>
public static class PlaceholderBaker
{
    public const string SettingsFolder = "Assets/Settings/Maps";
    public const string LibraryPath = SettingsFolder + "/PlaceholderLibrary.asset";
    public const string SettingsPath = SettingsFolder + "/BarangayMockSettings.asset";
    const string PrefabFolder = "Assets/Prefabs/Placeholders";

    const string SlumMap = "Assets/3D Models/manila_slum_map.glb";
    const string HallModel = "Assets/3D Models/barangay67-hall.glb";
    const string MarketModel = "Assets/3D Models/palengke-market-complex.glb";
    const string SchoolModel = "Assets/3D Models/ph-school-buildings.glb";

    // The walls of a house: ground floor and upper floors, and the piers and lintel of a shop front. Everything else
    // (roof, eaves, balconies, tanks, signs) hangs off the body and must not count towards its size.
    const string HouseBody = @"_f[0-9]+_(wall|pier_[lr]|lintel)$";

    [MenuItem("Bayanihan/Make Placeholder Prefabs")]
    public static void MakeFromMenu()
    {
        string summary = Make();
        Debug.Log(summary);
        var lib = AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>(LibraryPath);
        if (lib != null)
        {
            Selection.activeObject = lib;
            EditorGUIUtility.PingObject(lib);
        }
    }

    [MenuItem("Bayanihan/Make Placeholder Prefabs", true)]
    static bool MakeValidate() => !Application.isPlaying;

    /// <summary>Bakes every placeholder and fills the library and the settings. Returns what it did, one line each.</summary>
    public static string Make()
    {
        var log = new List<string>();
        EnsureFolder(SettingsFolder);
        EnsureFolder(PrefabFolder + "/Houses");
        EnsureFolder(PrefabFolder + "/Landmarks");
        EnsureFolder(PrefabFolder + "/Meshes");

        var slum = AssetDatabase.LoadAssetAtPath<GameObject>(SlumMap);
        var hall = AssetDatabase.LoadAssetAtPath<GameObject>(HallModel);
        var market = AssetDatabase.LoadAssetAtPath<GameObject>(MarketModel);
        var school = AssetDatabase.LoadAssetAtPath<GameObject>(SchoolModel);
        if (slum == null) log.Add("PlaceholderBaker: " + SlumMap + " is missing, so there are no houses.");
        if (hall == null) log.Add("PlaceholderBaker: " + HallModel + " is missing, so the hall stays a block.");
        if (market == null) log.Add("PlaceholderBaker: " + MarketModel + " is missing, so the market stays a block.");
        if (school == null) log.Add("PlaceholderBaker: " + SchoolModel + " is missing, so the school stays blocks.");

        var library = AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PlaceholderLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        // ------------------------------------------------------------ houses
        var houses = new List<HouseVariant>();
        if (slum != null)
        {
            var folder = slum.transform.Find("manila_slum_map/houses");
            if (folder == null) log.Add("PlaceholderBaker: " + SlumMap + " has no manila_slum_map/houses group.");
            else
                foreach (var node in folder.Cast<Transform>().Where(t => Regex.IsMatch(t.name, "^house_[0-9]+_")).OrderBy(t => t.name, StringComparer.Ordinal))
                {
                    var variant = HouseSpec(slum, node.name);
                    if (Bake(variant, "Houses", variant.name, log)) houses.Add(variant);
                }
        }
        // Anything in the library that was not baked from a source is somebody's own model: keep it.
        houses.AddRange(library.houses.Where(h => h.sourceModel == null && h.prefab != null));
        library.houses = houses;

        // ------------------------------------------------------------ landmarks
        var hallModel = Spec(hall, "", "Hall_Barangay67", 0f, new[] { "ground_plane" });
        var marketModel = Spec(market, "", "Market_Palengke", 0f, null);
        var schoolParts = new List<PlaceholderModel>
        {
            Spec(school, "buildings/A_linear_block", "School_A_Linear", 0f, null),
            Spec(school, "buildings/B_corner_L_block", "School_B_CornerL", 0f, null),
            Spec(school, "buildings/C_compact_block", "School_C_Compact", 0f, null)
        };
        bool hallBaked = hall != null && Bake(hallModel, "Landmarks", hallModel.name, log);
        bool marketBaked = market != null && Bake(marketModel, "Landmarks", marketModel.name, log);
        schoolParts = school == null ? new List<PlaceholderModel>() : schoolParts.Where(p => Bake(p, "Landmarks", p.name, log)).ToList();

        var styles = new List<LandmarkStyle>();
        foreach (LandmarkCategory category in Enum.GetValues(typeof(LandmarkCategory)))
        {
            var existing = library.landmarks.FirstOrDefault(s => s.category == category);
            // A style somebody set up by hand (their own prefab, no source model) is theirs: leave it.
            if (existing != null && ((existing.model != null && existing.model.prefab != null && existing.model.sourceModel == null) ||
                                     existing.parts.Any(p => p.prefab != null && p.sourceModel == null)))
            {
                styles.Add(existing);
                continue;
            }
            var style = DefaultStyle(category);
            if (category == LandmarkCategory.BarangayHall && hallBaked) { style.placement = LandmarkPlacement.Prefab; style.model = hallModel; }
            else if (category == LandmarkCategory.Market && marketBaked) { style.placement = LandmarkPlacement.Prefab; style.model = marketModel; }
            else if (category == LandmarkCategory.School && schoolParts.Count > 0) { style.placement = LandmarkPlacement.Parts; style.parts = schoolParts; }
            else if (style.placement == LandmarkPlacement.Prefab || style.placement == LandmarkPlacement.Parts) style.placement = LandmarkPlacement.Block;
            styles.Add(style);
        }
        library.landmarks = styles;
        library.MeasureAll();

        EditorUtility.SetDirty(library);

        // ------------------------------------------------------------ settings
        var settings = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<BarangayMockSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            log.Add("Made " + SettingsPath + ".");
        }
        if (settings.library == null) settings.library = library;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        var sb = new StringBuilder();
        sb.Append("PlaceholderBaker: ").Append(library.houses.Count).Append(" houses, ")
          .Append(hallBaked ? "the hall, " : "").Append(marketBaked ? "the market, " : "")
          .Append(schoolParts.Count).Append(" school blocks; ").Append(library.landmarks.Count).Append(" landmark styles in ")
          .Append(LibraryPath).Append('.');
        foreach (var line in log) sb.Append('\n').Append(line);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ what to bake

    static HouseVariant HouseSpec(GameObject slum, string nodeName)
    {
        // house_04_twostory -> House_04_TwoStorey
        var m = Regex.Match(nodeName, "^house_([0-9]+)_(.+)$");
        string number = m.Groups[1].Value, kind = m.Groups[2].Value;
        HouseType type; string typeName; int storeys;
        switch (kind)
        {
            case "shop":       type = HouseType.Shop;        typeName = "Shop";        storeys = 1; break;
            case "twostory":   type = HouseType.TwoStorey;   typeName = "TwoStorey";   storeys = 2; break;
            case "threestory": type = HouseType.ThreeStorey; typeName = "ThreeStorey"; storeys = 3; break;
            case "cornerturn": type = HouseType.Corner;      typeName = "Corner";      storeys = 3; break;
            case "tower":      type = HouseType.Tower;       typeName = "Tower";       storeys = 4; break;
            default:           type = HouseType.TwoStorey;   typeName = char.ToUpperInvariant(kind[0]) + kind.Substring(1); storeys = 2; break;
        }
        return new HouseVariant
        {
            name = "House_" + number + "_" + typeName,
            type = type,
            storeys = storeys,
            sourceModel = slum,
            sourcePath = "manila_slum_map/houses/" + nodeName,
            sourceFrontYaw = 180f,              // the doors are on the z = 0 face, so these houses face -Z
            bodyPattern = HouseBody
        };
    }

    static PlaceholderModel Spec(GameObject source, string path, string name, float frontYaw, string[] strip)
    {
        return new PlaceholderModel { name = name, sourceModel = source, sourcePath = path, sourceFrontYaw = frontYaw, strip = strip ?? new string[0] };
    }

    /// <summary>How a category is drawn when nothing better is baked: a block of a sensible height, or a shop house.</summary>
    static LandmarkStyle DefaultStyle(LandmarkCategory category)
    {
        LandmarkPlacement placement = LandmarkPlacement.Block;
        float height;
        switch (category)
        {
            case LandmarkCategory.BarangayHall:     placement = LandmarkPlacement.Prefab;    height = 10f; break;
            case LandmarkCategory.Market:           placement = LandmarkPlacement.Prefab;    height = 10f; break;
            case LandmarkCategory.School:           placement = LandmarkPlacement.Parts;     height = 8f;  break;
            case LandmarkCategory.Court:            placement = LandmarkPlacement.Court;     height = 6f;  break;
            case LandmarkCategory.Bank:
            case LandmarkCategory.Grocery:
            case LandmarkCategory.Pharmacy:
            case LandmarkCategory.Pawnshop:
            case LandmarkCategory.Government:       placement = LandmarkPlacement.ShopHouse; height = 7f;  break;
            case LandmarkCategory.CityHall:         height = 12f; break;
            case LandmarkCategory.EvacuationCentre: height = 9f;  break;
            case LandmarkCategory.SportsHall:       height = 10f; break;
            case LandmarkCategory.CommunityCentre:  height = 6f;  break;
            case LandmarkCategory.Hospital:         height = 12f; break;
            case LandmarkCategory.HealthCentre:     height = 6f;  break;
            case LandmarkCategory.FireStation:      height = 8f;  break;
            case LandmarkCategory.Police:           height = 6f;  break;
            case LandmarkCategory.BarangayOutpost:  height = 5f;  break;
            case LandmarkCategory.WaterSupply:      height = 7f;  break;
            case LandmarkCategory.Dam:              height = 3f;  break;
            case LandmarkCategory.AssemblyPoint:    height = 3f;  break;
            case LandmarkCategory.EmergencyAccess:  height = 2.5f; break;
            case LandmarkCategory.Fuel:             height = 5f;  break;
            case LandmarkCategory.Church:           height = 10f; break;
            default:                                height = 7f;  break;
        }
        return new LandmarkStyle { category = category, placement = placement, blockHeight = height };
    }

    // ------------------------------------------------------------------ baking one model

    /// <summary>
    /// Bakes one model into a mesh and a prefab, and records the prefab and its size on the spec. Returns false, with the
    /// reason in the log, if there is nothing to bake.
    /// </summary>
    static bool Bake(PlaceholderModel spec, string group, string prefabName, List<string> log)
    {
        if (spec.sourceModel == null) { log.Add("PlaceholderBaker: " + prefabName + " has no source model."); return false; }
        Transform node = string.IsNullOrEmpty(spec.sourcePath) ? spec.sourceModel.transform : spec.sourceModel.transform.Find(spec.sourcePath);
        if (node == null) { log.Add("PlaceholderBaker: " + prefabName + ": there is no '" + spec.sourcePath + "' in " + spec.sourceModel.name + "."); return false; }

        var strip = new HashSet<string>(spec.strip ?? new string[0]);
        var parts = new List<MeshFilter>();
        foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
            bool skip = false;
            for (var t = mf.transform; t != null && t != node.parent && !skip; t = t.parent) skip = strip.Contains(t.name);
            if (!skip) parts.Add(mf);
        }
        if (parts.Count == 0) { log.Add("PlaceholderBaker: " + prefabName + " has no meshes to bake."); return false; }
        foreach (var mf in parts)
            if (!mf.sharedMesh.isReadable)
            {
                log.Add("PlaceholderBaker: " + prefabName + ": mesh '" + mf.sharedMesh.name + "' is not readable, so it cannot be combined. " +
                        "Turn on Read/Write for " + spec.sourceModel.name + " in its import settings.");
                return false;
            }

        // The body is the walls (or, with no pattern, everything): it sets the pivot and the size used to fit it to a plot.
        var body = parts;
        if (!string.IsNullOrEmpty(spec.bodyPattern))
        {
            var rx = new Regex(spec.bodyPattern);
            var walls = parts.Where(p => rx.IsMatch(p.name)).ToList();
            if (walls.Count > 0) body = walls;
            else log.Add("PlaceholderBaker: " + prefabName + ": no part matches '" + spec.bodyPattern + "', so its size is all of it.");
        }

        Matrix4x4 nodeInv = node.worldToLocalMatrix;
        Bounds inNode = BoundsOf(body, nodeInv);

        // The new frame: origin at the middle of the body on the ground, turned so the model's front faces +Z.
        Vector3 origin = new Vector3(inNode.center.x, inNode.min.y, inNode.center.z);
        Matrix4x4 toFrame = Matrix4x4.TRS(origin, Quaternion.Euler(0f, spec.sourceFrontYaw, 0f), Vector3.one).inverse * nodeInv;
        Bounds bodyBounds = BoundsOf(body, toFrame);

        // Group every submesh by its material, then combine each group, then combine the groups as submeshes.
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        foreach (var mf in parts)
        {
            var mats = mf.GetComponent<MeshRenderer>().sharedMaterials;
            if (mats.Length == 0) continue;
            Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
            if (m.determinant < 0f) log.Add("PlaceholderBaker: " + prefabName + ": '" + mf.name + "' is mirrored, so its faces may point inwards.");
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
            {
                var material = mats[Mathf.Min(s, mats.Length - 1)];
                if (material == null) continue;
                if (!byMaterial.TryGetValue(material, out var list)) byMaterial[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = m });
            }
        }
        var ordered = byMaterial.Keys.OrderBy(m => m.name, StringComparer.Ordinal).ThenBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ToList();

        var groups = new List<CombineInstance>();
        var temporary = new List<Mesh>();
        foreach (var material in ordered)
        {
            var merged = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            merged.CombineMeshes(byMaterial[material].ToArray(), true, true);
            temporary.Add(merged);
            groups.Add(new CombineInstance { mesh = merged, subMeshIndex = 0, transform = Matrix4x4.identity });
        }
        var final = new Mesh { name = prefabName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        final.CombineMeshes(groups.ToArray(), false, false);
        final.RecalculateBounds();
        foreach (var t in temporary) UnityEngine.Object.DestroyImmediate(t);

        // A house's door says which way it faces: it should now be on the +Z side of the pivot.
        var door = parts.FirstOrDefault(p => p.name.EndsWith("_door", StringComparison.Ordinal));
        if (door != null && spec.sourceFrontYaw != 0f)
        {
            Bounds d = BoundsOf(new List<MeshFilter> { door }, toFrame);
            if (d.center.z <= 0f) log.Add("PlaceholderBaker: " + prefabName + ": the door ended up at the back (z = " + d.center.z.ToString("0.0") + "). Check sourceFrontYaw.");
        }

        // Overwrite in place so the GUIDs, and everything that points at them, stay the same.
        string meshPath = PrefabFolder + "/Meshes/" + prefabName + ".asset";
        var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (meshAsset == null) AssetDatabase.CreateAsset(final, meshPath);
        else
        {
            EditorUtility.CopySerialized(final, meshAsset);
            UnityEngine.Object.DestroyImmediate(final);
            final = meshAsset;
            EditorUtility.SetDirty(final);
        }

        var go = new GameObject(prefabName);
        go.AddComponent<MeshFilter>().sharedMesh = final;
        go.AddComponent<MeshRenderer>().sharedMaterials = ordered.ToArray();
        go.AddComponent<PlaceholderBody>().size = bodyBounds.size;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);     // instances inherit it
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + group + "/" + prefabName + ".prefab");
        UnityEngine.Object.DestroyImmediate(go);

        spec.name = prefabName;
        spec.prefab = prefab;
        spec.size = bodyBounds.size;
        return true;
    }

    /// <summary>The box round some parts, in the space that <paramref name="toFrame"/> takes world space to.</summary>
    static Bounds BoundsOf(IEnumerable<MeshFilter> parts, Matrix4x4 toFrame)
    {
        bool any = false;
        Bounds bounds = new Bounds();
        foreach (var mf in parts)
        {
            Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
