using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes Assets/Resources/Layouts/MabuhayCarmona.asset from Docs/Maps/mabuhay-carmona.geojson: the boundary, roads,
/// waterways, areas and landmarks of Barangay Mabuhay, City of Carmona, in metres around the barangay hall.
///
/// To refresh the map from OpenStreetMap, run Docs/Maps/fetch_mabuhay_osm.py first (see Docs/Maps/README.md), then
/// Bayanihan > Rebuild Barangay Layout. Which places are evacuation sites is kept in Docs/Maps/mabuhay-overrides.json,
/// not here. Running this again replaces everything in the asset except its scale.
/// </summary>
public static class BarangayLayoutBuilder
{
    public const string SourceFile = "Docs/Maps/mabuhay-carmona.geojson";
    static string Folder => "Assets/Resources/" + BarangayLayout.ResourceFolder;
    static string AssetPath => Folder + "/MabuhayCarmona.asset";
    static string ProjectFolder => Directory.GetParent(Application.dataPath).FullName;

    // The focus area is drawn around these, plus a margin: the hall, its court, the school, the hospital, the market.
    static readonly string[] FocusIds = { "w620109194", "w671986825", "w984143589", "r10749165", "r10977833" };
    const float FocusMargin = 150f;

    [MenuItem("Bayanihan/Rebuild Barangay Layout")]
    public static void Rebuild()
    {
        if (!File.Exists(Path.Combine(ProjectFolder, SourceFile)))
        {
            EditorUtility.DisplayDialog("Rebuild Barangay Layout",
                "Couldn't find " + SourceFile + ".\n\nRun Docs/Maps/fetch_mabuhay_osm.py to make it " +
                "(see Docs/Maps/README.md), then run this again.", "OK");
            return;
        }
        var layout = Build();
        if (layout == null) return;
        Selection.activeObject = layout;
        EditorGUIUtility.PingObject(layout);
    }

    [MenuItem("Bayanihan/Rebuild Barangay Layout", true)]
    static bool RebuildValidate() => !Application.isPlaying;

    /// <summary>Reads the GeoJSON and writes the asset. Returns null, with the reason in the Console, if it cannot.</summary>
    public static BarangayLayout Build()
    {
        string path = Path.Combine(ProjectFolder, SourceFile);
        if (!File.Exists(path))
        {
            Debug.LogError("BarangayLayoutBuilder: there is no " + SourceFile + ". Run Docs/Maps/fetch_mabuhay_osm.py to make it.");
            return null;
        }

        Dictionary<string, object> root;
        try { root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>; }
        catch (FormatException e)
        {
            Debug.LogError("BarangayLayoutBuilder: " + SourceFile + " is not valid JSON (" + e.Message + "). " +
                           "Run Docs/Maps/fetch_mabuhay_osm.py --offline to write it again.");
            return null;
        }
        if (root == null || !(Get(root, "features") is List<object>) || !(Get(root, "metadata") is Dictionary<string, object>))
        {
            Debug.LogError("BarangayLayoutBuilder: " + SourceFile + " is not the map fetch_mabuhay_osm.py writes " +
                           "(it needs \"metadata\" and \"features\").");
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", BarangayLayout.ResourceFolder);

        var layout = AssetDatabase.LoadAssetAtPath<BarangayLayout>(AssetPath);
        if (layout == null)
        {
            layout = ScriptableObject.CreateInstance<BarangayLayout>();
            AssetDatabase.CreateAsset(layout, AssetPath);
        }

        float keepScale = layout.scale;
        var warnings = new List<string>();
        Fill(layout, root, warnings);
        layout.scale = keepScale;

        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();

        foreach (var w in warnings) Debug.LogWarning("BarangayLayoutBuilder: " + w, layout);
        Debug.Log("BarangayLayoutBuilder: " + Summary(layout), layout);
        return layout;
    }

    /// <summary>"Mabuhay, City of Carmona: boundary of 323 points (4255 x 2420 m; 638 x 363 m in the game), ..."</summary>
    public static string Summary(BarangayLayout map)
    {
        Rect b = map.Bounds;
        var sites = map.EvacuationSites.ToList();
        return map.barangay + ", " + map.city + " (OpenStreetMap " + map.snapshotDate + "): boundary of " +
               map.boundary.Sum(r => r.points.Count) + " points (" + Mathf.RoundToInt(b.width) + " x " +
               Mathf.RoundToInt(b.height) + " m; " + Mathf.RoundToInt(b.width * map.scale) + " x " +
               Mathf.RoundToInt(b.height * map.scale) + " m in the game at scale " + map.scale + "), " +
               map.roads.Count + " roads, " + map.waterways.Count + " waterways, " + map.areas.Count + " areas, " +
               map.landmarks.Count + " landmarks (" + map.landmarks.Count(l => l.insideBarangay) + " inside the barangay), " +
               sites.Count + " evacuation sites (" + sites.Count(s => !s.verified) + " unverified, " +
               sites.Count(s => !s.positionKnown) + " with no position).";
    }

    // ------------------------------------------------------------------ reading the GeoJSON

    static void Fill(BarangayLayout map, Dictionary<string, object> root, List<string> warnings)
    {
        var meta = (Dictionary<string, object>)root["metadata"];
        var origin = Get(meta, "origin") as Dictionary<string, object>;
        map.barangay = Str(meta, "barangay");
        map.city = Str(meta, "city");
        map.province = Str(meta, "province");
        map.psgc = Str(meta, "psgc");
        map.population = (int)Num(meta, "population");
        map.populationYear = (int)Num(meta, "populationYear");
        map.populationSource = Str(meta, "populationSource");
        map.originName = Str(origin, "name");
        map.originLatitude = Num(origin, "lat");
        map.originLongitude = Num(origin, "lon");
        map.attribution = Str(meta, "attribution");
        map.licence = Str(meta, "licence");
        map.snapshotDate = Str(meta, "snapshot");
        map.sourceFile = SourceFile;

        map.boundary = new List<MapRing>();
        map.roads = new List<MapRoad>();
        map.waterways = new List<MapWaterway>();
        map.areas = new List<MapArea>();
        map.landmarks = new List<MapLandmark>();

        foreach (var item in (List<object>)root["features"])
        {
            if (!(item is Dictionary<string, object> feature)) continue;
            var props = Get(feature, "properties") as Dictionary<string, object>;
            var geometry = Get(feature, "geometry") as Dictionary<string, object>;
            string layer = Str(props, "layer"), id = Str(props, "id"), name = Str(props, "name");

            switch (layer)
            {
                case "boundary":
                    foreach (var ring in Rings(map, geometry)) map.boundary.Add(new MapRing { points = ring });
                    break;

                case "road":
                    map.roads.Add(new MapRoad
                    {
                        osmId = id,
                        name = name,
                        roadClass = Parse(Str(props, "class"), RoadClass.Residential, id, warnings),
                        oneWay = Bool(props, "oneway"),
                        bridge = Bool(props, "bridge"),
                        points = Line(map, geometry)
                    });
                    break;

                case "waterway":
                    map.waterways.Add(new MapWaterway
                    {
                        osmId = id,
                        name = name,
                        kind = Parse(Str(props, "kind"), WaterwayKind.Stream, id, warnings),
                        points = Line(map, geometry)
                    });
                    break;

                case "area":
                    map.areas.Add(new MapArea
                    {
                        osmId = id,
                        name = name,
                        kind = Parse(Str(props, "kind"), AreaKind.Other, id, warnings),
                        outline = Rings(map, geometry).FirstOrDefault() ?? new List<Vector2>()
                    });
                    break;

                case "landmark":
                    var landmark = new MapLandmark
                    {
                        id = id,
                        name = name,
                        category = Parse(Str(props, "category"), LandmarkCategory.Other, id, warnings),
                        evacuationRole = Parse(Str(props, "evacuation"), EvacuationRole.None, id, warnings),
                        verified = Bool(props, "verified"),
                        source = Str(props, "source"),
                        note = Str(props, "note"),
                        osmTag = Str(props, "tag")
                    };
                    var point = Get(geometry, "coordinates") as List<object>;
                    landmark.positionKnown = point != null && point.Count >= 2;
                    if (landmark.positionKnown) landmark.position = Point(map, point);
                    if (Get(props, "footprint") is List<object> footprint) landmark.footprint = Ring(map, footprint);
                    map.landmarks.Add(landmark);

                    if (landmark.IsEvacuationSite && string.IsNullOrWhiteSpace(landmark.source))
                        warnings.Add(landmark.Label + " (" + id + ") is marked as an evacuation site but says nowhere why. " +
                                     "Add a source in Docs/Maps/mabuhay-overrides.json.");
                    if (landmark.evacuationRole == EvacuationRole.Designated && !landmark.verified)
                        warnings.Add(landmark.Label + " (" + id + ") is designated but not verified. Only designate a site " +
                                     "once the barangay or Carmona CDRRMO has confirmed it.");
                    break;

                default:
                    warnings.Add("feature " + id + " has an unknown layer '" + layer + "' and was left out.");
                    break;
            }
        }

        foreach (var l in map.landmarks) l.insideBarangay = l.positionKnown && map.Contains(l.position);
        map.focusArea = Focus(map, warnings);
        map.revision++;
    }

    static Rect Focus(BarangayLayout map, List<string> warnings)
    {
        bool any = false;
        Vector2 min = Vector2.zero, max = Vector2.zero;
        foreach (var id in FocusIds)
        {
            var l = map.Find(id);
            if (l == null || !l.positionKnown) { warnings.Add("the focus area leaves out " + id + ", which is not on the map."); continue; }
            if (!any) { min = max = l.position; any = true; continue; }
            min = Vector2.Min(min, l.position);
            max = Vector2.Max(max, l.position);
        }
        if (!any) return new Rect(-350f, -250f, 700f, 500f);
        return Rect.MinMaxRect(min.x - FocusMargin, min.y - FocusMargin, max.x + FocusMargin, max.y + FocusMargin);
    }

    // GeoJSON positions are [longitude, latitude]. Stored to the centimetre: more would only be noise.
    static Vector2 Point(BarangayLayout map, List<object> position)
    {
        Vector2 m = map.ToLocalMetres(Convert.ToDouble(position[1]), Convert.ToDouble(position[0]));
        return new Vector2(Mathf.Round(m.x * 100f) / 100f, Mathf.Round(m.y * 100f) / 100f);
    }

    static List<Vector2> Line(BarangayLayout map, Dictionary<string, object> geometry)
    {
        var points = new List<Vector2>();
        if (Get(geometry, "coordinates") is List<object> coords)
            foreach (var c in coords)
                if (c is List<object> position) points.Add(Point(map, position));
        return points;
    }

    /// <summary>A GeoJSON ring, without the closing point (it repeats the first).</summary>
    static List<Vector2> Ring(BarangayLayout map, List<object> coords)
    {
        var points = new List<Vector2>();
        foreach (var c in coords)
            if (c is List<object> position) points.Add(Point(map, position));
        if (points.Count > 1 && points[0] == points[points.Count - 1]) points.RemoveAt(points.Count - 1);
        return points;
    }

    /// <summary>Every ring of a Polygon or MultiPolygon, holes included (the boundary test is even-odd).</summary>
    static List<List<Vector2>> Rings(BarangayLayout map, Dictionary<string, object> geometry)
    {
        var rings = new List<List<Vector2>>();
        if (!(Get(geometry, "coordinates") is List<object> coords)) return rings;
        string type = Str(geometry, "type");
        if (type == "Polygon")
        {
            foreach (var ring in coords) if (ring is List<object> r) rings.Add(Ring(map, r));
        }
        else if (type == "MultiPolygon")
        {
            foreach (var polygon in coords)
                if (polygon is List<object> p)
                    foreach (var ring in p) if (ring is List<object> r) rings.Add(Ring(map, r));
        }
        return rings;
    }

    // ------------------------------------------------------------------ small readers

    static object Get(Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) ? v : null;

    static string Str(Dictionary<string, object> d, string key) => Get(d, key) as string ?? "";

    static double Num(Dictionary<string, object> d, string key) => Get(d, key) is double v ? v : 0.0;

    static bool Bool(Dictionary<string, object> d, string key) => Get(d, key) is bool v && v;

    /// <summary>"barangay_hall" to LandmarkCategory.BarangayHall, and so on. Anything unknown becomes the fallback, with a warning.</summary>
    static T Parse<T>(string snake, T fallback, string id, List<string> warnings) where T : struct
    {
        if (Enum.TryParse(snake.Replace("_", ""), true, out T value) && Enum.IsDefined(typeof(T), value)) return value;
        warnings.Add(id + ": '" + snake + "' is not a known " + typeof(T).Name + "; it was read as " + fallback + ".");
        return fallback;
    }

    // ------------------------------------------------------------------ preview

    [MenuItem("Bayanihan/Add Barangay Layout Preview")]
    static void AddPreview()
    {
        var go = new GameObject("BarangayLayout_Preview") { tag = "EditorOnly" };
        go.AddComponent<BarangayLayoutView>();
        Undo.RegisterCreatedObjectUndo(go, "Add Barangay Layout Preview");
        Selection.activeGameObject = go;
        if (BarangayLayout.LoadShipped() == null)
            Debug.LogWarning("BarangayLayoutBuilder: there is no layout in Resources/Layouts yet, so the preview is empty. " +
                             "Run Bayanihan > Rebuild Barangay Layout.", go);
    }

    [MenuItem("Bayanihan/Add Barangay Layout Preview", true)]
    static bool AddPreviewValidate() => !Application.isPlaying;
}
