using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the mission on the Barangay Mabuhay map: where the boat starts, where the hall lets people ashore, where each of the
/// ten residents waits, and the hazards. MabuhayGameplayBuilder calls it after it has built the map.
///
/// Everything here is worked out from the layout's streets and from the map's own colliders, never from how a placeholder
/// model looks: a roof is found by casting down onto whatever is standing there, the hall's front is the way the planner
/// turned it, a corridor is measured with a ray. So swapping the final models in and building again puts the posts, the
/// roofs and the hazards back on whatever is there.
///
/// The map around the hall, in real metres east (x) and north (y) of it:
///  - Bagumbayan Street runs west-south-west to east-north-east along the north edge of the barangay. The hall stands on its
///    south side, facing it. North of the street the flood spreads over open ground; south of it, 15 to 20 m back, is a wall of
///    houses.
///  - A. Mapalad Street leaves it by a short spur at the west end and runs east between two rows of houses: a lane 6 to 9 m
///    wide that dead-ends 160 m in. The debris jam is in it.
/// The boat starts on Bagumbayan Street near its west end and the hall is 90 m along it.
/// </summary>
public static class MabuhayLevelBuilder
{
    public const string AnchorsName = "Mission Anchors";
    public const string HazardsName = "Hazards";
    public const string PostsName = "Posts";
    public const string EdgeName = "World Edge";

    // The streets, by their OpenStreetMap ids, with the name to fall back on if the layout is rebuilt with other ids.
    const string BagumbayanId = "w668009058";
    const string MapaladId = "w145889435";

    // ------------------------------------------------------------------ where things go (metres along Bagumbayan Street)
    // s counts from the street's south end, up the spur, round the corner and along the street towards the hall.
    const float StartS = 46f;                // the boat
    const float JoyS = 62f, JoySide = 5f;    // adrift, north of the street
    const float BenS = 66f;                  // a post on the south kerb
    const float WireS = 86f;                 // the cable comes down across the street
    const float AdorS = 106f;                // a post on the north kerb
    const float LitoS = 116f, LitoSide = 6f; // adrift and hurt, nearer the hall
    const float KerbOffset = 3.4f;           // posts stand on the kerb: half the 6 m road, and a little
    const float JamX = -47f;                 // where in A. Mapalad Street (real metres east) the jam is

    public sealed class Result
    {
        public readonly List<string> notes = new List<string>();
        public readonly List<string> problems = new List<string>();
        public override string ToString() =>
            string.Join("\n", notes) + (problems.Count > 0 ? "\nPROBLEMS:\n" + string.Join("\n", problems) : "");
    }

    /// <summary>What the level needs of the scene it is put into.</summary>
    sealed class Scenery
    {
        public Scene scene;
        public BarangayMockSettings settings;
        public BarangayLayout layout;
        public MockPlan plan;
        public SimpleBoat boat;
        public PlayerRescuer player;
        public EvacuationCenter evac;
        public Transform anchors, hazards, posts;
        public float groundY;
        public PhysicsMaterial slippery;
        public Material postMaterial, poleMaterial;
        public Result result;
    }

    // ------------------------------------------------------------------ streets

    /// <summary>A street as a line: where it is at a distance along it, and which way it runs.</summary>
    public sealed class Street
    {
        public readonly string name;
        readonly List<Vector2> points;
        readonly List<float> along = new List<float>();
        public float Length { get; }

        public Street(MapRoad road)
        {
            name = road.name;
            points = new List<Vector2>(road.points);
            along.Add(0f);
            for (int i = 1; i < points.Count; i++) along.Add(along[i - 1] + Vector2.Distance(points[i - 1], points[i]));
            Length = along[along.Count - 1];
        }

        int SegmentAt(float s)
        {
            for (int i = 1; i < points.Count; i++) if (s <= along[i]) return i - 1;
            return points.Count - 2;
        }

        public Vector2 Tangent(float s)
        {
            int i = SegmentAt(s);
            return (points[i + 1] - points[i]).normalized;
        }

        /// <summary>The point <paramref name="s"/> metres along the street, <paramref name="side"/> metres to its left (north, going east).</summary>
        public Vector2 At(float s, float side = 0f)
        {
            int i = SegmentAt(s);
            float t = (s - along[i]) / Mathf.Max(0.0001f, along[i + 1] - along[i]);
            Vector2 p = Vector2.LerpUnclamped(points[i], points[i + 1], t);
            return p + MapGeometry.Left((points[i + 1] - points[i]).normalized) * side;
        }

        /// <summary>How far along the street the nearest point to <paramref name="p"/> is.</summary>
        public float Project(Vector2 p, out float distance)
        {
            distance = MapGeometry.DistanceToPolyline(points, p, out Vector2 closest, out int segment);
            return along[segment] + Vector2.Distance(points[segment], closest);
        }
    }

    static Street FindStreet(BarangayLayout layout, string osmId, string nameFragment)
    {
        var road = layout.roads.FirstOrDefault(r => r.osmId == osmId) ??
                   layout.roads.Where(r => !string.IsNullOrEmpty(r.name) && r.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                              .OrderByDescending(r => new Street(r).Length).FirstOrDefault();
        return road != null ? new Street(road) : null;
    }

    static Vector3 W(Vector2 metres, float y = 0f) => new Vector3(metres.x, y, metres.y);

    // ------------------------------------------------------------------ the hall

    /// <summary>The hall as the planner placed it: the middle of its footprint, the way it faces, and how big its front and depth are.</summary>
    sealed class HallPose
    {
        public Vector2 centre, front;
        public float frontage, depth;
        public Vector2 FrontCentre => centre + front * (depth * 0.5f);
    }

    static HallPose FindHall(BarangayLayout layout, MockPlan plan, Street street)
    {
        var hall = layout.landmarks.Where(l => l.category == LandmarkCategory.BarangayHall && l.positionKnown)
                                   .OrderBy(l => l.position.sqrMagnitude).FirstOrDefault();
        if (hall == null) return null;

        var placement = plan.placements.FirstOrDefault(p => p.sourceId == hall.id && p.kind == PlacementKind.Model);
        if (placement != null)
        {
            float yaw = placement.yaw * Mathf.Deg2Rad;
            return new HallPose
            {
                centre = placement.position,
                front = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)),
                frontage = placement.modelSize.x * placement.scale.x,
                depth = placement.modelSize.z * placement.scale.z
            };
        }

        // The hall is a plain block of its footprint: it faces the nearest street, by the side of its rectangle that does.
        var block = plan.placements.FirstOrDefault(p => p.sourceId == hall.id && p.outline != null && p.outline.Count >= 3);
        var ring = block != null ? block.outline : hall.footprint;
        if (ring == null || ring.Count < 3) return null;
        var rect = MapGeometry.MinimumRectangle(ring);
        float s = street.Project(rect.centre, out _);
        Vector2 toStreet = (street.At(s) - rect.centre).normalized;
        Vector2 best = rect.axisU;
        foreach (var side in new[] { rect.axisU, -rect.axisU, rect.axisV, -rect.axisV })
            if (Vector2.Dot(side, toStreet) > Vector2.Dot(best, toStreet)) best = side;
        bool alongU = Mathf.Abs(Vector2.Dot(best, rect.axisU)) > 0.5f;
        return new HallPose { centre = rect.centre, front = best, frontage = alongU ? rect.sizeV : rect.sizeU, depth = alongU ? rect.sizeU : rect.sizeV };
    }

    // ------------------------------------------------------------------ the whole level

    public static Result Build(Scene scene, BarangayMockSettings settings, MissionDefinition mission)
    {
        var s = new Scenery
        {
            scene = scene,
            settings = settings,
            layout = settings.Layout,
            groundY = settings.groundHeight,
            result = new Result(),
            slippery = BarangayMockBuilder.SlipperySurface()
        };
        s.plan = BarangayMockPlanner.Plan(s.layout, settings.Region(s.layout), settings.library);
        s.boat = UnityEngine.Object.FindFirstObjectByType<SimpleBoat>();
        s.player = UnityEngine.Object.FindFirstObjectByType<PlayerRescuer>();
        s.evac = UnityEngine.Object.FindFirstObjectByType<EvacuationCenter>();
        if (s.boat == null || s.player == null || s.evac == null)
        {
            s.result.problems.Add("the scene has no boat, swimmer or evacuation centre: nothing to place the level around.");
            return s.result;
        }

        var bagumbayan = FindStreet(s.layout, BagumbayanId, "Bagumbayan");
        var mapalad = FindStreet(s.layout, MapaladId, "Mapalad");
        if (bagumbayan == null || mapalad == null)
        {
            s.result.problems.Add("the layout has no Bagumbayan Street or A. Mapalad Street.");
            return s.result;
        }

        ClearOwnGroups(scene);
        s.anchors = NewGroup(AnchorsName);
        s.hazards = NewGroup(HazardsName);
        s.posts = NewGroup(PostsName);
        s.postMaterial = LevelMaterial("Level_Post", new Color(0.62f, 0.62f, 0.60f));
        s.poleMaterial = LevelMaterial("Level_Pole", new Color(0.36f, 0.29f, 0.23f));
        Physics.SyncTransforms();            // the map's colliders have only just been made

        var hall = FindHall(s.layout, s.plan, bagumbayan);
        if (hall == null) { s.result.problems.Add("the layout has no Barangay Hall."); return s.result; }

        PlaceHall(s, hall, bagumbayan);
        PlaceBoat(s, bagumbayan);
        PlaceResidents(s, bagumbayan, mapalad, hall);
        PlaceWreckage(s, bagumbayan, mapalad);
        PlaceEdge(s);
        SetFlow(s, bagumbayan);
        AssignMission(mission);

        var signs = GameObject.Find("Signs");
        if (signs != null) signs.SetActive(false);          // flat names for an overview camera: out of the way at street level

        Physics.SyncTransforms();
        Validate(s, mission);
        return s.result;
    }

    static Transform NewGroup(string name)
    {
        var go = new GameObject(name);
        return go.transform;
    }

    static void ClearOwnGroups(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects().ToList())
            if (root.name == AnchorsName || root.name == HazardsName || root.name == PostsName || root.name == EdgeName)
                UnityEngine.Object.DestroyImmediate(root);
    }

    // ------------------------------------------------------------------ the hall and the way ashore

    /// <summary>
    /// The evacuation centre goes on the street in front of the hall, a little towards it, laid along the street; the landing area
    /// is the strip of porch along the hall's front wall, turned to face the way the hall does.
    /// </summary>
    static void PlaceHall(Scenery s, HallPose hall, Street street)
    {
        float along = street.Project(hall.FrontCentre, out float gap);
        Vector2 tangent = street.Tangent(along);
        Vector2 zoneCentre = street.At(along, -1.5f);                   // south of the middle of the street, towards the hall

        var evac = s.evac.transform;
        evac.position = W(zoneCentre, 0.25f);
        evac.rotation = Quaternion.Euler(0f, MapGeometry.Heading(tangent), 0f);
        evac.localScale = new Vector3(10f, 3f, 12f);                    // across the street, high, along it
        s.evac.arrivalMargin = 2f;

        var landing = s.evac.landingArea;
        if (landing == null)
        {
            landing = new GameObject("EvaCen Landing").transform;
            s.evac.landingArea = landing;
        }
        landing.position = W(hall.FrontCentre + hall.front * 1.1f, -0.25f);
        landing.rotation = Quaternion.Euler(0f, MapGeometry.Heading(hall.front), 0f);
        s.evac.landingAreaSize = new Vector2(Mathf.Clamp(hall.frontage * 0.4f, 3f, 6.4f), 0.8f);
        EditorUtility.SetDirty(s.evac);

        s.result.notes.Add("Hall: faces (" + hall.front.x.ToString("0.00") + ", " + hall.front.y.ToString("0.00") + ") (yaw " + MapGeometry.Heading(hall.front).ToString("0") +
                           "°), front wall " + gap.ToString("0.0") + " m from " + street.name + ". Drop-off zone on the street at " + along.ToString("0") + " m along it.");
    }

    // ------------------------------------------------------------------ the boat and the swimmer

    /// <summary>The boat starts on Bagumbayan Street heading for the hall, the swimmer 1.4 m off its side.</summary>
    static void PlaceBoat(Scenery s, Street street)
    {
        Vector2 at = street.At(StartS);
        Vector2 tangent = street.Tangent(StartS);
        float yaw = MapGeometry.Heading(tangent);

        // Which end of this boat is the bow is SimpleBoat's business (Bow Direction), so aim from there.
        Quaternion rotation = Quaternion.LookRotation(W(tangent)) * Quaternion.Inverse(Quaternion.LookRotation(s.boat.LocalBow));
        s.boat.transform.SetPositionAndRotation(W(at, 0.06f), rotation);

        // As in Gameplay: the swimmer is 1.4 m off the hull on the side of the tiller, with "Press E to board" showing.
        s.player.transform.SetPositionAndRotation(s.boat.transform.TransformPoint(new Vector3(2.69f, 1.35f, 0f)), Quaternion.Euler(0f, yaw, 0f));

        // The camera rig starts where it would settle: behind and above the boat, looking along it.
        Vector3 behind = W(at, 0f) - W(tangent) * 8.3f + Vector3.up * 4.8f;
        var cam = Camera.main;
        if (cam != null) cam.transform.SetPositionAndRotation(behind, Quaternion.Euler(30f, yaw, 0f));
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb != null && mb.GetType().Name == "CinemachineCamera") mb.transform.SetPositionAndRotation(behind, Quaternion.Euler(30f, yaw, 0f));

        EditorUtility.SetDirty(s.boat.transform);
    }

    // ------------------------------------------------------------------ the residents, the posts and the hazards that go with them

    static void PlaceResidents(Scenery s, Street bag, Street mapalad, HallPose hall)
    {
        // ---- Joy, adrift in the street ahead of the start
        AddAnchor(s, "joy", W(bag.At(JoyS, JoySide)), 55f);

        // ---- Ben, clinging to a post on the south kerb
        Vector2 benPost = bag.At(BenS, -KerbOffset);
        AddPost(s, "post_ben", benPost, 5.2f, s.postMaterial);
        AddClinger(s, "ben", benPost, towardsPost: -MapGeometry.Left(bag.Tangent(BenS)));

        // ---- the Bautistas, either side of the pole that holds the live cable
        Vector2 polePost = bag.At(WireS, KerbOffset);
        Vector2 celiaPost = bag.At(WireS, -KerbOffset);
        var pole = AddPost(s, "post_pole", polePost, 9.5f, s.poleMaterial);
        AddPost(s, "post_celia", celiaPost, 5.2f, s.postMaterial);
        AddClinger(s, "jun", polePost, towardsPost: MapGeometry.Left(bag.Tangent(WireS)));
        AddClinger(s, "celia", celiaPost, towardsPost: -MapGeometry.Left(bag.Tangent(WireS)));
        AddLiveWire(s, bag, pole);

        // ---- Ador, clinging to a post on the north kerb nearer the hall
        Vector2 adorPost = bag.At(AdorS, KerbOffset);
        AddPost(s, "post_ador", adorPost, 5.2f, s.postMaterial);
        AddClinger(s, "ador", adorPost, towardsPost: MapGeometry.Left(bag.Tangent(AdorS)));

        // ---- Lito, hurt, in the open water
        AddAnchor(s, "lito", W(bag.At(LitoS, LitoSide)), 90f);

        // ---- the two on roofs: Nena by the corner, near the start; Tessa nearer the hall
        AddRoofer(s, "nena", new Vector2(-88f, -34f), bag.At(StartS - 2f, -6f), 4.5f, 10f);
        AddRoofer(s, "tessa", hall.centre + new Vector2(-26f, -14f), bag.At(104f, -2f), 4.5f, 10f);

        // ---- the Santos, behind the jam in A. Mapalad Street
        AddJam(s, mapalad);
    }

    // ---- anchors

    static Transform AddAnchor(Scenery s, string id, Vector3 position, float yaw)
    {
        var go = new GameObject("Anchor_" + id);
        go.transform.SetParent(s.anchors, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return go.transform;
    }

    /// <summary>
    /// Somebody clinging to a post: they stand 0.55 m from its axis (ResidentSpawner.ClingDistance) on the side nearer the street,
    /// facing the post.
    /// </summary>
    static void AddClinger(Scenery s, string id, Vector2 post, Vector2 towardsPost)
    {
        Vector2 dir = towardsPost.normalized;
        Vector2 where = post - dir * ResidentSpawner.ClingDistance;
        AddAnchor(s, id, W(where), MapGeometry.Heading(dir));
    }

    // ---- posts

    static GameObject AddPost(Scenery s, string name, Vector2 at, float height, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(s.posts, false);
        go.transform.position = W(at, s.groundY + height * 0.5f);
        go.transform.localScale = new Vector3(0.34f, height * 0.5f, 0.34f);          // the primitive is 2 m high and 1 m across
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        go.GetComponent<Collider>().sharedMaterial = s.slippery;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static Material LevelMaterial(string name, Color colour)
    {
        const string folder = "Assets/Materials/Placeholders";
        PlaceholderBaker.EnsureFolder(folder);
        string path = folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name };
        m.SetColor("_BaseColor", colour);
        m.SetColor("_Color", colour);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ---- the live wire

    static void AddLiveWire(Scenery s, Street bag, GameObject pole)
    {
        var go = new GameObject("Live wire (Bagumbayan Street)");
        go.transform.SetParent(s.hazards, false);
        go.transform.position = W(bag.At(WireS), 0f);
        var top = new GameObject("Cable from the pole top").transform;
        top.SetParent(go.transform, false);
        top.position = pole.transform.position + Vector3.up * (pole.transform.localScale.y);

        var zone = go.AddComponent<LiveWireZone>();
        zone.radius = 6.5f;
        zone.cableFrom = top;
    }

    // ---- roofs

    /// <summary>
    /// Puts somebody on a roof: the best house near <paramref name="target"/> whose roof is between the heights asked for, judged by
    /// what is drawn. They stand on the roof itself (found by casting down onto the house's own mesh), just inside its edge on the side
    /// that faces <paramref name="towards"/> (where the boat will be), looking that way. A spot with no roof under it, or one too
    /// steep to stand on, is skipped for the next one in, then the next house.
    /// </summary>
    static void AddRoofer(Scenery s, string id, Vector2 target, Vector2 towards, float minHeight, float maxHeight)
    {
        var map = GameObject.Find("BarangayMock");
        var buildings = map != null ? map.transform.Find("Buildings") : null;
        var candidates = new List<(Transform house, float score)>();
        if (buildings != null)
            foreach (Transform house in buildings)
            {
                if (!house.name.StartsWith("House ") || !DrawnBounds(house, out Bounds drawn)) continue;
                var centre = new Vector2(drawn.center.x, drawn.center.z);
                if (Vector2.Distance(centre, target) > 24f + new Vector2(drawn.extents.x, drawn.extents.z).magnitude) continue;
                float height = drawn.max.y - s.groundY;
                if (height < minHeight || height > maxHeight) continue;
                candidates.Add((house, Vector2.Distance(centre, target) + 1.2f * Mathf.Abs(height - 6f)));
            }

        foreach (var (house, _) in candidates.OrderBy(c => c.score))
        {
            if (!RoofSpot(house, towards, s.groundY + minHeight, s.groundY + maxHeight, out Vector3 spot, out Vector2 facing)) continue;
            AddAnchor(s, id, spot, MapGeometry.Heading(facing));
            s.result.notes.Add(id + ": on the roof of " + house.name + " (" + (spot.y - s.groundY).ToString("0.0") + " m up).");
            return;
        }

        s.result.problems.Add("no roof to stand on between " + minHeight + " and " + maxHeight + " m near " + target + " for " + id + ".");
        AddAnchor(s, id, W(target, 4f), 0f);
    }

    /// <summary>The box round everything drawn of a placed model, in world space.</summary>
    static bool DrawnBounds(Transform model, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>
    /// A place to stand on a house's roof, on the side facing <paramref name="towards"/>: starts 0.8 m in from the edge of what is
    /// drawn and works inwards until the house's own mesh is under foot, level enough to stand on, at a height in range.
    /// </summary>
    static bool RoofSpot(Transform house, Vector2 towards, float minY, float maxY, out Vector3 spot, out Vector2 facing)
    {
        spot = default;
        facing = Vector2.up;
        var solids = house.GetComponentsInChildren<MeshCollider>();
        if (solids.Length == 0) return false;

        // The drawn footprint in the house's own frame, so the edge is found along its walls, not along the world's axes.
        Matrix4x4 toHouse = house.worldToLocalMatrix;
        bool any = false;
        Bounds local = default;
        foreach (var mf in house.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            Matrix4x4 m = toHouse * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        if (!any) return false;

        Vector3 centre = house.TransformPoint(local.center);
        Vector2 here = new Vector2(centre.x, centre.z);
        facing = (towards - here).normalized;
        Vector2 right = new Vector2(house.right.x, house.right.z).normalized, forward = new Vector2(house.forward.x, house.forward.z).normalized;
        float halfWidth = local.extents.x * Mathf.Abs(house.lossyScale.x), halfDepth = local.extents.z * Mathf.Abs(house.lossyScale.z);
        float u = Mathf.Abs(Vector2.Dot(facing, right)), v = Mathf.Abs(Vector2.Dot(facing, forward));
        float reachToEdge = Mathf.Min(u < 1e-4f ? float.MaxValue : halfWidth / u, v < 1e-4f ? float.MaxValue : halfDepth / v);
        float top = house.TransformPoint(new Vector3(0f, local.max.y, 0f)).y + 2f;

        for (float inset = 0.8f; inset <= reachToEdge; inset += 0.4f)
        {
            Vector2 at = here + facing * (reachToEdge - inset);
            var ray = new Ray(new Vector3(at.x, top, at.y), Vector3.down);
            bool hitAny = false;
            RaycastHit highest = default;
            foreach (var solid in solids)
                if (solid.Raycast(ray, out RaycastHit hit, top - minY + 1f) && (!hitAny || hit.point.y > highest.point.y)) { highest = hit; hitAny = true; }
            if (!hitAny || highest.normal.y < 0.9f || highest.point.y < minY || highest.point.y > maxY) continue;    // nothing there, or too steep
            spot = highest.point;
            return true;
        }
        return false;
    }

    // ---- the jam

    /// <summary>
    /// A jam of wreckage across A. Mapalad Street, wall to wall (the width is measured, not assumed), and the Santos behind it: far
    /// enough that the boat at the jam is out of reach of them, near enough for a thrown line.
    /// </summary>
    static void AddJam(Scenery s, Street mapalad)
    {
        float along = mapalad.Project(new Vector2(JamX, -40f), out _);
        Vector2 on = mapalad.At(along);
        Vector2 tangent = mapalad.Tangent(along);
        Vector2 left = MapGeometry.Left(tangent);

        // Measure the lane from its middle to the wall on each side, with a ray a little above the water.
        Vector3 from = W(on, 0.6f);
        float toLeft = Wall(from, W(left), 14f), toRight = Wall(from, W(-left), 14f);
        Vector2 middle = on + left * ((toLeft - toRight) * 0.5f);
        float width = toLeft + toRight;

        var go = new GameObject("Debris jam (A. Mapalad Street)");
        go.transform.SetParent(s.hazards, false);
        go.transform.position = W(middle, 0f);
        go.transform.rotation = Quaternion.LookRotation(W(tangent), Vector3.up);       // its x runs across the lane
        var barrier = go.AddComponent<DebrisBarrier>();
        // The solid block is 92% of the width given, so ask for enough to bury its ends in the walls.
        barrier.size = new Vector3((width + 0.8f) / 0.92f, 1.6f, 2.6f);
        barrier.pieces = 36;
        barrier.seed = 5;

        // The Santos are on the far side from the boat, which comes in from the west: east, along the lane.
        Vector2 eastward = tangent.x >= 0f ? tangent : -tangent;
        Vector2 eastFace = middle + eastward * (barrier.size.z * 0.9f * 0.5f);
        Vector2 sideways = MapGeometry.Left(eastward);
        AddAnchor(s, "rosa", W(eastFace + eastward * 4.8f + sideways * 0.7f), MapGeometry.Heading(-eastward));
        AddAnchor(s, "migo", W(eastFace + eastward * 6.0f - sideways * 0.7f), MapGeometry.Heading(-eastward));
        s.result.notes.Add("Jam: " + width.ToString("0.0") + " m of lane, centred " + middle.x.ToString("0") + ", " + middle.y.ToString("0") + ".");
    }

    static float Wall(Vector3 from, Vector3 direction, float maxDistance)
    {
        foreach (var hit in Physics.RaycastAll(from, direction, maxDistance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (hit.collider.attachedRigidbody == null && hit.collider.GetComponent<WaterSurface>() == null) return hit.distance;
        return maxDistance;
    }

    // ------------------------------------------------------------------ wreckage

    static void PlaceWreckage(Scenery s, Street bag, Street mapalad)
    {
        // Wreckage drifts with the flood, away from the hall: nothing starts in front of it, where it would pile up and pen the boat in.
        AddWreckage(s, "Wreckage (east of the start)", bag.At(56f, 1f), bag.Tangent(56f), new Vector2(14f, 8f), 5, 3);
        AddWreckage(s, "Wreckage (before the live wire)", bag.At(76f, 0f), bag.Tangent(76f), new Vector2(14f, 8f), 6, 8);
        float lane = mapalad.Project(new Vector2(-78f, -44f), out _);
        AddWreckage(s, "Wreckage (A. Mapalad Street)", mapalad.At(lane), mapalad.Tangent(lane), new Vector2(10f, 3f), 3, 13);

        // Plant scraps the flood has torn from the yards and lots: branches, uprooted bananas, mats of grass and creeper.
        // Soft (only a branch dents a hull), so they can float where the boat works, but still not in front of the hall.
        AddWreckage(s, "Plant scraps (east of the start)", bag.At(54f, -2f), bag.Tangent(54f), new Vector2(18f, 9f), 7, 21, FloatingDebris.PlantScraps);
        AddWreckage(s, "Plant scraps (before the live wire)", bag.At(72f, 2f), bag.Tangent(72f), new Vector2(18f, 9f), 7, 22, FloatingDebris.PlantScraps);
        AddWreckage(s, "Plant scraps (A. Mapalad Street)", mapalad.At(lane), mapalad.Tangent(lane), new Vector2(12f, 3f), 4, 23, FloatingDebris.PlantScraps);
    }

    static void AddWreckage(Scenery s, string name, Vector2 at, Vector2 along, Vector2 area, int count, int seed, DebrisKind[] kinds = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(s.hazards, false);
        go.transform.SetPositionAndRotation(W(at), Quaternion.LookRotation(W(along), Vector3.up));
        var field = go.AddComponent<DebrisField>();
        field.area = new Vector2(area.y, area.x);           // across (x) and along (z) the street
        field.count = count;
        field.seed = seed;
        if (kinds != null) field.kinds = kinds;
    }

    // ------------------------------------------------------------------ the edge of the world

    /// <summary>World height of the levee's crest: well above the water at its highest (it rises at most 0.6 m in a mission).</summary>
    public const float LeveeCrestY = 2.5f;
    /// <summary>How far (metres) the levee's face runs out from the flooded ground to the crest: a steep earth bank.</summary>
    public const float LeveeRun = 4f;
    /// <summary>How far out from the map's middle the dry land goes: past the water's skirt, so no open water shows beyond the levee.</summary>
    public const float DryLandReach = 1200f;

    /// <summary>The flooded ground the map lies on; the levee rises from its edge.</summary>
    public static Rect EdgeArea(BarangayMockSettings settings, BarangayLayout layout)
    {
        var region = settings.Region(layout);
        float margin = settings.margin;
        return Rect.MinMaxRect(region.xMin - margin, region.yMin - margin, region.xMax + margin, region.yMax + margin);
    }

    /// <summary>
    /// The edge of the world is something you can see: an earth levee all round the flooded ground, from the bottom of the water up to
    /// a grassy crest well above the highest the flood gets, and dry land beyond it out to the horizon. The boat runs aground on the
    /// bank it can see; there is no invisible wall, and no open water past it to want to reach. Its colliders are the drawn bank.
    /// </summary>
    static void PlaceEdge(Scenery s)
    {
        var inner = EdgeArea(s.settings, s.layout);
        var crest = Rect.MinMaxRect(inner.xMin - LeveeRun, inner.yMin - LeveeRun, inner.xMax + LeveeRun, inner.yMax + LeveeRun);
        var outer = Rect.MinMaxRect(Mathf.Min(crest.xMin - 1f, -DryLandReach), Mathf.Min(crest.yMin - 1f, -DryLandReach),
                                    Mathf.Max(crest.xMax + 1f, DryLandReach), Mathf.Max(crest.yMax + 1f, DryLandReach));
        float foot = s.groundY - 0.05f;     // a little under the ground, so no seam shows where they meet

        var root = new GameObject(EdgeName).transform;
        var bank = new MapGeometry.MeshBuffers();
        Ring(bank, inner, foot, crest, LeveeCrestY, 0.25f);
        var land = new MapGeometry.MeshBuffers();
        Ring(land, crest, LeveeCrestY, outer, LeveeCrestY, 0.02f);

        EdgePart(s, root, "Levee bank", bank, LevelMaterial("Levee_Earth", new Color(0.45f, 0.38f, 0.27f)));
        EdgePart(s, root, "Dry land", land, LevelMaterial("Levee_Grass", new Color(0.42f, 0.55f, 0.28f)));
    }

    /// <summary>
    /// The band between two nested rectangles as four trapezoids (one per side), the inner edge at one height and the outer at
    /// another, facing up and inwards.
    /// </summary>
    static void Ring(MapGeometry.MeshBuffers b, Rect inner, float innerY, Rect outer, float outerY, float uvScale)
    {
        Vector3[] I = { new Vector3(inner.xMin, innerY, inner.yMin), new Vector3(inner.xMax, innerY, inner.yMin),
                        new Vector3(inner.xMax, innerY, inner.yMax), new Vector3(inner.xMin, innerY, inner.yMax) };
        Vector3[] O = { new Vector3(outer.xMin, outerY, outer.yMin), new Vector3(outer.xMax, outerY, outer.yMin),
                        new Vector3(outer.xMax, outerY, outer.yMax), new Vector3(outer.xMin, outerY, outer.yMax) };
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Vector3 a = I[k], c = I[n], d = O[n], e = O[k];
            Vector3 normal = Vector3.Cross(c - a, e - a).normalized;
            if (normal.y < 0f) normal = -normal;
            int ia = b.Add(a, normal, new Vector2(a.x, a.z) * uvScale), ic = b.Add(c, normal, new Vector2(c.x, c.z) * uvScale);
            int id = b.Add(d, normal, new Vector2(d.x, d.z) * uvScale), ie = b.Add(e, normal, new Vector2(e.x, e.z) * uvScale);
            // Wound so the face looks up (the side the camera and the boat are on): Unity's front face is the one whose
            // Cross(b - a, c - a) points at the viewer.
            if (Vector3.Dot(Vector3.Cross(c - a, d - a), normal) > 0f) { b.Triangle(ia, ic, id); b.Triangle(ia, id, ie); }
            else { b.Triangle(ia, id, ic); b.Triangle(ia, ie, id); }
        }
    }

    static void EdgePart(Scenery s, Transform root, string name, MapGeometry.MeshBuffers b, Material material)
    {
        string folder = s.settings.generatedFolder;
        PlaceholderBaker.EnsureFolder(folder);
        string path = folder + "/" + name.Replace(' ', '_') + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var built = b.ToMesh(name);
        if (mesh == null) { mesh = built; AssetDatabase.CreateAsset(mesh, path); }
        else { EditorUtility.CopySerialized(built, mesh); UnityEngine.Object.DestroyImmediate(built); }

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var solid = go.AddComponent<MeshCollider>();
        solid.sharedMesh = mesh;
        solid.sharedMaterial = s.slippery;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // ------------------------------------------------------------------ the water and the mission

    /// <summary>The flood runs away from the hall, along the street, as the Manila one does: the boat takes everyone upstream.</summary>
    static void SetFlow(Scenery s, Street street)
    {
        var current = UnityEngine.Object.FindFirstObjectByType<FloodCurrent>();
        if (current == null) return;
        current.direction = Mathf.Repeat(MapGeometry.Heading(-street.Tangent(WireS)), 360f);
        EditorUtility.SetDirty(current);
    }

    static void AssignMission(MissionDefinition mission)
    {
        var builder = UnityEngine.Object.FindFirstObjectByType<MissionBuilder>();
        if (builder == null) return;
        builder.mission = mission;
        EditorUtility.SetDirty(builder);
    }

    // ------------------------------------------------------------------ checking it

    /// <summary>
    /// Floods the boat's reach from the start and checks that everything the mission asks for is in it: the boat can get to every
    /// resident it has to pick up, a line can be thrown from the water to each of the others, and the boat can reach the hall.
    /// </summary>
    static void Validate(Scenery s, MissionDefinition mission)
    {
        // The jam is made when the scene plays, so stand in for it while the water is measured: solid to the boat, and (being a
        // kinematic body, like the real one) not in the way of a sight line.
        var stand = new GameObject("Jam stand-in");
        var barrier = s.hazards.GetComponentInChildren<DebrisBarrier>();
        var standBox = stand.AddComponent<BoxCollider>();
        stand.AddComponent<Rigidbody>().isKinematic = true;
        stand.transform.SetPositionAndRotation(barrier.transform.position, barrier.transform.rotation);
        standBox.size = new Vector3(barrier.size.x * 0.92f, 4f, barrier.size.z * 0.9f);
        Physics.SyncTransforms();

        var wide = s.settings.Region(s.layout);
        var reach = new BoatReach(Rect.MinMaxRect(wide.xMin - 20f, wide.yMin - 20f, wide.xMax + 20f, wide.yMax + 20f), 1f, BoatReach.DefaultRadius, 0f);
        int cells = reach.Flood(s.boat.transform.position);
        s.result.notes.Add("The boat's start reaches " + cells + " cells of water.");
        if (cells < 1000) s.result.problems.Add("the boat is hemmed in at its start (" + cells + " cells).");

        Vector2 zone = new Vector2(s.evac.transform.position.x, s.evac.transform.position.z);
        float toHall = reach.DistanceToReached(zone, 3f);
        if (float.IsInfinity(toHall)) s.result.problems.Add("the boat cannot reach the hall's drop-off zone.");
        else s.result.notes.Add("The hall is " + reach.DistanceFromStart(zone).ToString("0") + " m from the start by water.");

        foreach (var spec in mission.residents)
        {
            var anchor = s.anchors.Find(spec.anchor);
            if (anchor == null) { s.result.problems.Add("no anchor for " + spec.displayName + "."); continue; }
            Vector2 xz = new Vector2(anchor.position.x, anchor.position.z);
            float d = reach.DistanceToReached(xz, 30f);
            bool needsLine = spec.situation == Situation.Rooftop || spec.situation == Situation.Trapped;
            if (!needsLine)
            {
                if (d > 3.5f) s.result.problems.Add(spec.displayName + " is " + (float.IsInfinity(d) ? "far" : d.ToString("0.0") + " m") + " from the nearest water the boat can reach (limit 3.5 m).");
            }
            else
            {
                bool thrown = ThrowableFrom(reach, anchor.position, out float how);
                if (!thrown) s.result.problems.Add(spec.displayName + " cannot be reached by a line from any water the boat can reach.");
                else s.result.notes.Add(spec.displayName + ": a line can reach them from " + how.ToString("0.0") + " m.");
            }

            // Somebody trapped behind wreckage really is cut off: the boat's way to them, if there is one, is the long way round.
            if (spec.situation == Situation.Trapped)
            {
                float byWater = reach.DistanceFromStart(xz);
                if (byWater < 150f) s.result.problems.Add(spec.displayName + " is only " + byWater.ToString("0") + " m from the start by water: the jam does not cut them off.");
            }

            // Nobody is inside a house, and nobody is under a roof.
            Vector3 body = anchor.position + Vector3.up * 0.9f;
            foreach (var hit in Physics.OverlapSphere(body, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.attachedRigidbody == null && hit.GetComponent<WaterSurface>() == null)
                    s.result.problems.Add(spec.displayName + " is inside " + hit.name + ".");
            if (Physics.Raycast(anchor.position + Vector3.up * 1.9f, Vector3.up, out RaycastHit above, 15f, ~0, QueryTriggerInteraction.Ignore) && above.collider.attachedRigidbody == null)
                s.result.problems.Add(spec.displayName + " is under " + above.collider.name + ".");
        }

        UnityEngine.Object.DestroyImmediate(stand);
    }

    /// <summary>Is there water the boat can reach from which somebody at this spot is in a thrown line's range, in plain view?</summary>
    public static bool ThrowableFrom(BoatReach reach, Vector3 resident, out float distance)
    {
        distance = float.PositiveInfinity;
        Vector3 head = resident + Vector3.up * 1.6f;
        if (!reach.ThrowPointFor(head, 2.05f, 11.5f, out Vector2 from)) return false;
        distance = Vector3.Distance(new Vector3(from.x, 2.05f, from.y), head);
        return true;
    }
}
