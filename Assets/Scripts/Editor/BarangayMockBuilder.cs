using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the placeholder map: the ground, areas, streams, roads, houses, blocks, landmarks and signs of the barangay,
/// laid out from the real layout, in a scene of its own (Assets/Scenes/Maps/MabuhayMock.unity). What goes where is
/// worked out by BarangayMockPlanner; this turns it into objects.
///
/// Run Bayanihan > Make Placeholder Prefabs once, then Bayanihan > Build Barangay Mock whenever the layout, the library
/// or the settings change. The whole map is under one object, BarangayMock, which is deleted and made again each time:
/// do not put anything of your own inside it.
///
/// The roads, ground and blocks are meshes kept in one asset under Assets/Generated, so the scene itself holds only
/// objects and links. It is never built in the Gameplay scene.
/// </summary>
public static class BarangayMockBuilder
{
    const string RootName = "BarangayMock";

    // Everything lies flat at a slightly different height, so no two surfaces fight over the same pixels.
    const float GroundY = 0f;
    const float AreaY = 0.02f, AreaStep = 0.004f;
    const float WaterY = 0.06f;
    const float GroundsY = 0.07f;
    const float RoadY = 0.08f, RoadStep = 0.02f;

    [MenuItem("Bayanihan/Build Barangay Mock")]
    static void BuildMenu()
    {
        var settings = LoadSettings();
        if (settings == null)
        {
            EditorUtility.DisplayDialog("Build Barangay Mock",
                "There are no settings yet.\n\nRun Bayanihan > Make Placeholder Prefabs first: it makes them.", "OK");
            return;
        }
        string problem = OpenScene(settings, true);
        if (problem != null) { Debug.LogWarning("BarangayMockBuilder: " + problem); return; }
        settings = LoadSettings();      // switching scenes unloads unused assets, so load the settings, and what they point at, again
        var root = Build(settings);
        if (root == null) return;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem("Bayanihan/Build Barangay Mock", true)]
    static bool BuildValidate() => !Application.isPlaying;

    public static BarangayMockSettings LoadSettings() => AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(PlaceholderBaker.SettingsPath);

    /// <summary>
    /// Builds the map in the mock scene and saves it, with no dialogs, so it can be run from the command line. It will
    /// not touch any scene that has unsaved changes. Returns what it built, or a sentence starting "Cannot".
    /// </summary>
    public static string BuildAndSave()
    {
        var settings = LoadSettings();
        if (settings == null) return "Cannot build: there are no settings. Run Bayanihan > Make Placeholder Prefabs.";
        string problem = OpenScene(settings, false);
        if (problem != null) return "Cannot build: " + problem;
        settings = LoadSettings();      // switching scenes unloads unused assets, so load the settings, and what they point at, again
        var root = Build(settings);
        if (root == null) return "Cannot build: see the Console.";
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return root.GetComponent<BarangayMockInfo>().summary;
    }

    /// <summary>Opens (or makes) the scene the settings name. Returns null if the active scene is now the right one, or why not.</summary>
    static string OpenScene(BarangayMockSettings settings, bool interactive)
    {
        string path = settings.scenePath;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return "the settings have no scene path.";
        if (Path.GetFileName(path).Equals("Gameplay.unity", StringComparison.OrdinalIgnoreCase)) return "the mock is never built in the Gameplay scene.";
        if (SceneManager.GetActiveScene().path == path) return null;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.isDirty && !interactive) return "scene '" + open.name + "' has unsaved changes. Save or discard them first.";
        }
        if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled.";

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        else
        {
            PlaceholderBaker.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(created, path);
        }
        return null;
    }

    // ------------------------------------------------------------------ building

    /// <summary>What a build needs to hand round.</summary>
    sealed class Context
    {
        public BarangayMockSettings settings;
        public BarangayLayout layout;
        public PlaceholderLibrary lib;
        public MockPlan plan;
        public Rect region, wide;
        public MeshSet meshes;
        public Materials mats;
        /// <summary>The surface given to every collider when the settings ask for colliders; null otherwise.</summary>
        public PhysicsMaterial slippery;
        public Transform root, ground, areas, water, roads, buildings, landmarks, signs;
        public Font font;
    }

    /// <summary>Builds the map into the active scene, replacing any earlier one. Returns its root, or null if it cannot.</summary>
    public static GameObject Build(BarangayMockSettings settings)
    {
        var scene = SceneManager.GetActiveScene();
        if (Path.GetFileName(scene.path).Equals("Gameplay.unity", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("BarangayMockBuilder: the active scene is Gameplay, and the mock is never built there. Open the mock scene first.");
            return null;
        }
        var layout = settings.Layout;
        if (layout == null)
        {
            Debug.LogError("BarangayMockBuilder: there is no layout to build from. Run Bayanihan > Rebuild Barangay Layout.");
            return null;
        }
        var lib = settings.library;
        if (lib == null)
        {
            Debug.LogError("BarangayMockBuilder: the settings have no placeholder library. Run Bayanihan > Make Placeholder Prefabs.");
            return null;
        }

        lib.MeasureAll();
        var c = new Context { settings = settings, layout = layout, lib = lib, mats = new Materials() };
        if (settings.colliders) c.slippery = SlipperySurface();
        c.region = settings.Region(layout);
        c.wide = Rect.MinMaxRect(c.region.xMin - settings.margin, c.region.yMin - settings.margin, c.region.xMax + settings.margin, c.region.yMax + settings.margin);
        c.plan = BarangayMockPlanner.Plan(layout, c.region, lib);
        c.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName).ToList())
            UnityEngine.Object.DestroyImmediate(old);

        PlaceholderBaker.EnsureFolder(settings.generatedFolder);
        string bundlePath = settings.generatedFolder + "/BarangayMock_Meshes.asset";
        var bundle = AssetDatabase.LoadAssetAtPath<MockMeshBundle>(bundlePath);
        if (bundle == null)
        {
            bundle = ScriptableObject.CreateInstance<MockMeshBundle>();
            AssetDatabase.CreateAsset(bundle, bundlePath);
        }
        c.meshes = new MeshSet(bundle, bundlePath);

        var root = new GameObject(RootName);
        root.transform.position = new Vector3(0f, settings.groundHeight, 0f);
        root.transform.localScale = Vector3.one * settings.scale;
        c.root = root.transform;
        c.ground = Group(root, "Ground");
        c.areas = Group(root, "Areas");
        c.water = Group(root, "Water");
        c.roads = Group(root, "Roads");
        c.buildings = Group(root, "Buildings");
        c.landmarks = Group(root, "Landmarks");
        c.signs = Group(root, "Signs");

        if (settings.ground) BuildGround(c);
        if (settings.areas) BuildAreas(c);
        if (settings.water) BuildWater(c);
        if (settings.roads) BuildRoads(c);
        BuildPlacements(c);
        c.meshes.Finish();

        AddLightAndCamera(c);

        var info = root.AddComponent<BarangayMockInfo>();
        info.settings = settings;
        info.layoutRevision = layout.revision;
        info.houses = c.plan.Count(PlacementKind.House);
        info.blocks = c.plan.Count(PlacementKind.Block) + c.plan.Count(PlacementKind.Shed);
        info.landmarks = c.plan.Count(PlacementKind.Model) + c.plan.Count(PlacementKind.Grounds) + c.plan.Count(PlacementKind.Court) + c.plan.Count(PlacementKind.Sign);
        info.summary = Summary(c);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("BarangayMockBuilder: " + info.summary + "\n" + string.Join("\n", c.plan.notes.Take(60)), root);
        return root;
    }

    static string Summary(Context c)
    {
        var p = c.plan;
        int buildings = p.fates.Count;
        var sb = new StringBuilder();
        sb.Append(c.layout.barangay).Append(" mock, ").Append(Mathf.RoundToInt(c.region.width)).Append(" x ").Append(Mathf.RoundToInt(c.region.height))
          .Append(" m at scale ").Append(c.settings.scale.ToString("0.##")).Append(": ")
          .Append(buildings).Append(" buildings (")
          .Append(p.Count(PlacementKind.House)).Append(" house prefabs, ")
          .Append(p.Count(PlacementKind.Block)).Append(" blocks, ")
          .Append(p.Count(PlacementKind.Shed)).Append(" sheds, ")
          .Append(p.Count(PlacementKind.Canopy)).Append(" canopies, ")
          .Append(p.fates.Count(f => f.Value.StartsWith("replaced by") || f.Value.StartsWith("landmark")) ).Append(" taken over by landmarks), ")
          .Append(p.Count(PlacementKind.Model)).Append(" landmark models, ")
          .Append(p.Count(PlacementKind.Sign)).Append(" signs, ")
          .Append(p.Count(PlacementKind.Banner)).Append(" evacuation banners.");
        return sb.ToString();
    }

    static Transform Group(GameObject root, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        return g.transform;
    }

    // ------------------------------------------------------------------ the flat things

    static void BuildGround(Context c)
    {
        var b = new MapGeometry.MeshBuffers();
        MapGeometry.Fill(RectRing(c.wide), GroundY, b, 0.05f);
        MeshObject(c, c.ground, "Ground", b, c.mats.Get("Ground", new Color(0.55f, 0.60f, 0.47f)), true);
    }

    static void BuildAreas(Context c)
    {
        foreach (var group in c.layout.areas.GroupBy(a => a.kind).OrderBy(g => (int)g.Key))
        {
            bool isWater = group.Key == AreaKind.Water;
            float y = isWater ? WaterY : AreaY + AreaStep * (int)group.Key;
            var b = new MapGeometry.MeshBuffers();
            foreach (var area in group.OrderBy(a => a.osmId, StringComparer.Ordinal))
            {
                var clipped = MapGeometry.ClipPolygon(area.outline, c.wide);
                if (clipped.Count >= 3 && Mathf.Abs(MapGeometry.SignedArea(clipped)) > 1f) MapGeometry.Fill(clipped, y, b);
            }
            MeshObject(c, c.areas, "area_" + group.Key, b, isWater ? c.mats.Get("Water", WaterColour) : c.mats.Get("Area_" + group.Key, AreaColour(group.Key)), false);
        }
    }

    static void BuildWater(Context c)
    {
        var b = new MapGeometry.MeshBuffers();
        foreach (var w in c.layout.waterways.OrderBy(w => w.osmId, StringComparer.Ordinal))
            foreach (var piece in MapGeometry.ClipPolyline(w.points, c.wide))
                MapGeometry.Ribbon(piece, c.lib.WaterWidth(w.kind) * 0.5f, WaterY, b);
        MeshObject(c, c.water, "waterways", b, c.mats.Get("Water", WaterColour), false);
    }

    static void BuildRoads(Context c)
    {
        foreach (var group in c.layout.roads.GroupBy(r => r.roadClass).OrderBy(g => (int)g.Key))
        {
            float y = RoadY + RoadStep * (int)group.Key;
            float half = c.lib.roadWidths.For(group.Key) * 0.5f;
            var road = new MapGeometry.MeshBuffers();
            var bridge = new MapGeometry.MeshBuffers();
            foreach (var r in group.OrderBy(r => r.osmId, StringComparer.Ordinal))
                foreach (var piece in MapGeometry.ClipPolyline(r.points, c.wide))
                    MapGeometry.Ribbon(piece, half, y, r.bridge ? bridge : road);
            MeshObject(c, c.roads, "road_" + group.Key, road, c.mats.Get("Road_" + group.Key, RoadColour(group.Key)), true);
            MeshObject(c, c.roads, "bridge_" + group.Key, bridge, c.mats.Get("Bridge", new Color(0.62f, 0.62f, 0.64f)), true);
        }
    }

    static readonly Color WaterColour = new Color(0.25f, 0.45f, 0.75f);

    static Color RoadColour(RoadClass k)
    {
        switch (k)
        {
            case RoadClass.Trunk:        return new Color(0.17f, 0.17f, 0.19f);
            case RoadClass.Primary:
            case RoadClass.Secondary:    return new Color(0.20f, 0.20f, 0.22f);
            case RoadClass.Tertiary:     return new Color(0.24f, 0.24f, 0.26f);
            case RoadClass.Unclassified: return new Color(0.30f, 0.30f, 0.30f);
            case RoadClass.Residential:  return new Color(0.34f, 0.34f, 0.35f);
            case RoadClass.Service:      return new Color(0.45f, 0.45f, 0.44f);
            case RoadClass.Track:        return new Color(0.55f, 0.47f, 0.35f);
            default:                     return new Color(0.72f, 0.68f, 0.58f);
        }
    }

    static Color AreaColour(AreaKind k)
    {
        switch (k)
        {
            case AreaKind.Residential:   return new Color(0.62f, 0.70f, 0.52f);
            case AreaKind.Commercial:    return new Color(0.78f, 0.66f, 0.66f);
            case AreaKind.Industrial:    return new Color(0.66f, 0.63f, 0.72f);
            case AreaKind.Institutional: return new Color(0.60f, 0.72f, 0.74f);
            case AreaKind.Green:         return new Color(0.45f, 0.62f, 0.38f);
            case AreaKind.Farmland:      return new Color(0.70f, 0.72f, 0.45f);
            case AreaKind.Cemetery:      return new Color(0.55f, 0.62f, 0.55f);
            default:                     return new Color(0.60f, 0.60f, 0.55f);
        }
    }

    // ------------------------------------------------------------------ houses, blocks, landmarks, signs

    static void BuildPlacements(Context c)
    {
        // Plain blocks and slabs are collected by colour and made into one mesh each; prefabs are instanced one by one.
        var flats = new SortedDictionary<string, MapGeometry.MeshBuffers>(StringComparer.Ordinal);
        MapGeometry.MeshBuffers Flat(string key)
        {
            if (!flats.TryGetValue(key, out var b)) flats[key] = b = new MapGeometry.MeshBuffers();
            return b;
        }
        string Key(string group, string kind, Color colour) => group + "|" + kind + "|" + Hex(colour);

        foreach (var p in c.plan.placements)
        {
            bool landmark = p.category != LandmarkCategory.Other || p.kind == PlacementKind.Model;
            string group = landmark ? "landmarks" : "buildings";
            switch (p.kind)
            {
                case PlacementKind.House:
                case PlacementKind.Model:
                    if (!c.settings.buildings && p.kind == PlacementKind.House) break;
                    if (!c.settings.landmarks && p.kind == PlacementKind.Model) break;
                    Instance(c, p, Flat(Key(p.kind == PlacementKind.Model ? "landmarks" : "buildings", "fallback", new Color(0.70f, 0.70f, 0.72f))));
                    break;

                case PlacementKind.Block:
                case PlacementKind.Shed:
                    if (landmark ? !c.settings.landmarks : !c.settings.buildings) break;
                    MapGeometry.Extrude(p.outline, 0f, p.height, Flat(Key(group, "block", p.colour)));
                    break;

                case PlacementKind.Canopy:
                    if (!c.settings.buildings) break;
                    Canopy(Flat(Key("buildings", "canopy", p.colour)), p.outline, p.height - 0.25f);
                    break;

                case PlacementKind.Court:
                    if (!c.settings.landmarks) break;
                    MapGeometry.Extrude(p.outline, 0f, 0.12f, Flat(Key("landmarks", "court", p.colour)));
                    Canopy(Flat(Key("landmarks", "canopy", new Color(0.55f, 0.52f, 0.5f))), p.outline, p.height - 0.25f);
                    break;

                case PlacementKind.Grounds:
                    if (!c.settings.landmarks) break;
                    MapGeometry.Fill(p.outline, GroundsY, Flat(Key("landmarks", "grounds", p.colour)));
                    break;

                case PlacementKind.Banner:
                    if (!c.settings.landmarks) break;
                    var pole = Flat(Key("landmarks", "banner", p.colour));
                    MapGeometry.Extrude(Square(p.position, 0.5f), 0f, p.height, pole);
                    MapGeometry.Extrude(RectangleRing(p.position + new Vector2(2.75f, 0f), 5f, 0.3f), p.height - 3f, 3f, pole);
                    if (c.settings.signs) Sign(c, p.position, p.height + 4f, p.label, p.colour, 2.2f);
                    break;

                case PlacementKind.Sign:
                    if (c.settings.signs && (landmark ? c.settings.landmarks : c.settings.buildings)) Sign(c, p.position, p.height, p.label, p.colour, 1.6f);
                    break;
            }
        }

        foreach (var pair in flats)
        {
            var parts = pair.Key.Split('|');
            var parent = parts[0] == "landmarks" ? c.landmarks : c.buildings;
            Color colour = ColorUtility.TryParseHtmlString("#" + parts[2], out var parsed) ? parsed : Color.grey;
            string name = parts[1] + "_" + parts[0] + "_" + parts[2];
            bool collide = parts[1] == "block" || parts[1] == "court" || parts[1] == "fallback";
            MeshObject(c, parent, name, pair.Value, c.mats.Get(parts[1] == "grounds" ? "Grounds_" + parts[2] : "Block_" + parts[2], colour), collide);
        }
    }

    /// <summary>Places a house or a landmark prefab, or - if the library has no prefab for it - a plain block of its size.</summary>
    static void Instance(Context c, MockPlacement p, MapGeometry.MeshBuffers fallback)
    {
        bool landmark = p.kind == PlacementKind.Model;
        var parent = landmark ? c.landmarks : c.buildings;
        if (p.prefab != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p.prefab, parent);
            go.name = (landmark ? "" : "House ") + p.sourceId + " (" + p.modelName + ")";
            go.transform.localPosition = new Vector3(p.position.x, 0f, p.position.y);
            go.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
            go.transform.localScale = p.scale;
            if (c.settings.colliders)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, p.modelSize.y * 0.5f, 0f);
                box.size = p.modelSize;
                box.sharedMaterial = c.slippery;
            }
            return;
        }

        Vector3 size = Vector3.Scale(p.modelSize.sqrMagnitude > 0f ? p.modelSize : new Vector3(6f, 4f, 6f), p.scale);
        Quaternion turn = Quaternion.Euler(0f, p.yaw, 0f);
        var ring = new List<Vector2>();
        foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
        {
            Vector3 o = turn * new Vector3(corner.x * size.x * 0.5f, 0f, corner.y * size.z * 0.5f);
            ring.Add(p.position + new Vector2(o.x, o.z));
        }
        MapGeometry.Extrude(ring, 0f, size.y, fallback);
    }

    /// <summary>A flat roof on four posts, over an outline. <paramref name="roofY"/> is the underside of the roof.</summary>
    static void Canopy(MapGeometry.MeshBuffers b, IList<Vector2> outline, float roofY)
    {
        MapGeometry.Extrude(outline, roofY, 0.25f, b);
        foreach (var corner in MapGeometry.MinimumRectangle(outline).Corners())
            MapGeometry.Extrude(Square(corner, 0.35f), 0f, roofY, b);
    }

    static List<Vector2> Square(Vector2 centre, float side) => RectangleRing(centre, side, side);

    static List<Vector2> RectangleRing(Vector2 centre, float w, float h) => new List<Vector2>
    {
        centre + new Vector2(-w / 2, -h / 2), centre + new Vector2(w / 2, -h / 2), centre + new Vector2(w / 2, h / 2), centre + new Vector2(-w / 2, h / 2)
    };

    static List<Vector2> RectRing(Rect r) => new List<Vector2>
    {
        new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax)
    };

    /// <summary>A name lying flat above a building, readable from above with north at the top of the screen.</summary>
    static void Sign(Context c, Vector2 position, float height, string text, Color colour, float characterSize)
    {
        var go = new GameObject("Sign " + text);
        go.transform.SetParent(c.signs, false);
        go.transform.localPosition = new Vector3(position.x, height, position.y);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = c.font;
        tm.fontSize = 64;
        tm.characterSize = characterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = colour;
        go.GetComponent<MeshRenderer>().sharedMaterial = c.font.material;
    }

    // ------------------------------------------------------------------ light, camera, objects

    /// <summary>
    /// A sun and an overview camera, found by name or made, and put back every build. If the scene already has a light or
    /// a camera of its own, it is left alone.
    /// </summary>
    static void AddLightAndCamera(Context c)
    {
        var sun = GameObject.Find("Sun");
        if (sun != null || UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length == 0)
        {
            if (sun == null) sun = new GameObject("Sun");
            var light = sun.GetComponent<Light>() ?? sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        var cam = GameObject.Find("Overview Camera");
        if (cam != null || UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 0)
        {
            if (cam == null) cam = new GameObject("Overview Camera") { tag = "MainCamera" };
            var camera = cam.GetComponent<Camera>() ?? cam.AddComponent<Camera>();
            camera.nearClipPlane = 1f;                  // 1 m, so the flat layers a few centimetres apart do not flicker
            camera.farClipPlane = 4000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.78f, 0.95f);
            float s = c.settings.scale;
            Vector2 centre = c.region.center;
            cam.transform.position = new Vector3(centre.x * s, 450f * s + c.settings.groundHeight, centre.y * s - 380f * s);
            cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
        }
    }

    static GameObject MeshObject(Context c, Transform parent, string name, MapGeometry.MeshBuffers b, Material material, bool collider)
    {
        if (b.IsEmpty) return null;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = c.meshes.Add(parent.name + "_" + name, b);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (collider && c.settings.colliders)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.sharedMaterial = c.slippery;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();

    /// <summary>
    /// The surface of every collider on a map to play on: no friction. A hull that is steered into a wall keeps its speed
    /// along it and glances off, rather than being held against the wall by the contact.
    /// </summary>
    public static PhysicsMaterial SlipperySurface()
    {
        const string path = "Assets/Materials/Placeholders/Mock_Slippery.asset";
        var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (m != null) return m;
        PlaceholderBaker.EnsureFolder("Assets/Materials/Placeholders");
        m = new PhysicsMaterial("Mock_Slippery")
        {
            dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ meshes and materials kept as assets

    /// <summary>
    /// The generated meshes, kept as sub-assets of one bundle. A mesh with the same name as last time is rewritten in
    /// place (so nothing breaks), and any left over from a bigger earlier build are removed.
    /// </summary>
    sealed class MeshSet
    {
        readonly MockMeshBundle bundle;
        readonly Dictionary<string, Mesh> existing = new Dictionary<string, Mesh>();
        readonly HashSet<string> used = new HashSet<string>();

        public MeshSet(MockMeshBundle bundle, string path)
        {
            this.bundle = bundle;
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (o is Mesh m) existing[m.name] = m;
        }

        public Mesh Add(string name, MapGeometry.MeshBuffers buffers)
        {
            var fresh = buffers.ToMesh(name);
            used.Add(name);
            if (existing.TryGetValue(name, out var mesh))
            {
                EditorUtility.CopySerialized(fresh, mesh);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
            AssetDatabase.AddObjectToAsset(fresh, bundle);
            existing[name] = fresh;
            return fresh;
        }

        public void Finish()
        {
            foreach (var pair in existing.ToList())
                if (!used.Contains(pair.Key)) UnityEngine.Object.DestroyImmediate(pair.Value, true);
            EditorUtility.SetDirty(bundle);
        }
    }

    /// <summary>
    /// The plain materials of the map, made once and then left alone, so a colour changed by hand in the Inspector stays
    /// changed. Delete a material to have it made again.
    /// </summary>
    sealed class Materials
    {
        const string Folder = "Assets/Materials/Placeholders";
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public Material Get(string key, Color colour)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            PlaceholderBaker.EnsureFolder(Folder);
            string path = Folder + "/Mock_" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader) { name = "Mock_" + key };
                colour.a = 1f;
                m.SetColor("_BaseColor", colour);
                m.SetColor("_Color", colour);
                m.SetFloat("_Smoothness", 0.12f);
                m.SetFloat("_Metallic", 0f);
                AssetDatabase.CreateAsset(m, path);
            }
            cache[key] = m;
            return m;
        }
    }
}
