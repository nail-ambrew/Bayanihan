using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// Bayanihan > Cutscenes > Build Menu Cinematic: Scene_MenuCinematic, the main menu's looping storm. It copies a slice
/// of the real Mabuhay map round the boat's start (houses, walls, trees, props, roads, the live wire; every mesh and
/// material is the map's own asset, referenced, not duplicated), the flood, the storm, the sky and light, adds the crew
/// as character spots, and writes a ~20 s Timeline of five shots that loops with a crossfade from the last into the
/// first. MabuhayGameplay is only read.
/// </summary>
public static class MenuCinematicBuilder
{
    public const string Folder = CutsceneKit.Root + "/MainMenu";
    public const string ScenePath = Folder + "/Scene_MenuCinematic.unity";
    public const string TimelinePath = Folder + "/Timelines/Cut_Menu_Loop.playable";
    public const string SignalFolder = CutsceneKit.Root + "/Intro/Signals";
    const string MapScene = "Assets/Scenes/Maps/MabuhayGameplay.unity";

    public const double Length = 20.0;
    /// <summary>The still frame Reduce Motion holds on: the team against the break in the clouds.</summary>
    public const double StillFrame = 17.6;

    /// <summary>The five shots (seconds): retime here. The loop-in (19.2..20) is the first shot's opening frame.</summary>
    public static readonly (string name, double start, double end)[] Shots =
    {
        ("Cut_Menu_01_RainOverRooftops", 0.0, 4.0),
        ("Cut_Menu_02_BoatInFloodedStreet", 4.0, 8.0),
        ("Cut_Menu_03_FlashlightRescue", 8.0, 12.0),
        ("Cut_Menu_04_LiveWireStop", 12.0, 16.0),
        ("Cut_Menu_05_TeamCloudBreak", 16.0, 20.0),
    };

    static readonly Vector3 Centre = new Vector3(-140f, 0f, -40f);
    const float Radius = 85f;
    static readonly string[] CopyGroups = { "Buildings", "Infill Buildings", "Compound Walls", "Foliage", "Props", "Landmarks", "Street Furniture" };      // not the map's floating name signs
    static readonly string[] CopyWhole = { "Ground", "Roads", "Areas" };

    [MenuItem("Bayanihan/Cutscenes/Build Menu Cinematic", false, 62)]
    public static void BuildFromMenu() => Debug.Log("MenuCinematicBuilder: " + Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) return "stop Play mode first";
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
        CutsceneKit.EnsureFolder(Folder + "/Timelines");

        var map = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        var boatStart = boat != null ? boat.transform.position : new Vector3(-160f, 0f, -37f);
        var tessa = GameObject.Find("Anchor_tessa")?.transform.position ?? new Vector3(-67f, 5f, -43f);
        var wireZone = Object.FindObjectsByType<LiveWireZone>(FindObjectsSortMode.None)
                             .OrderBy(z => Vector3.Distance(z.transform.position, Centre)).FirstOrDefault();
        var wire = wireZone != null ? wireZone.transform.position : new Vector3(-123f, 0f, -22f);
        var lighting = (sky: RenderSettings.skybox, fog: RenderSettings.fog, fogColor: RenderSettings.fogColor, fogMode: RenderSettings.fogMode,
                        fogDensity: RenderSettings.fogDensity, ambientMode: RenderSettings.ambientMode, ambientIntensity: RenderSettings.ambientIntensity,
                        ambientLight: RenderSettings.ambientLight, sunName: RenderSettings.sun != null ? RenderSettings.sun.name : null);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("MenuCinematic").transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        var world = new GameObject("MapSlice").transform;
        world.SetParent(root, false);

        // ---------------- the slice of the map
        int copied = 0, tris = 0;
        var mock = GameObject.Find("BarangayMock").transform;
        foreach (Transform group in mock)
        {
            bool whole = CopyWhole.Contains(group.name);
            if (!whole && !CopyGroups.Contains(group.name)) continue;
            var holder = new GameObject(group.name).transform;
            holder.SetParent(world, false);
            if (whole) foreach (Transform piece in group) Copy(piece, holder, ref copied, ref tris);
            else CopyNear(group, holder, ref copied, ref tris);
        }
        // the live wire and its posts (the hazard's own art)
        if (wireZone != null)
        {
            var w = Object.Instantiate(wireZone.gameObject, world);
            w.name = "LiveWire";
            w.transform.SetPositionAndRotation(wireZone.transform.position, wireZone.transform.rotation);
            Strip(w);
        }
        foreach (var name in new[] { "Water", "Typhoon", "Directional Light", "Global Volume", "Main Camera" })
        {
            var src = GameObject.Find(name);
            if (src == null) continue;
            var copy = Object.Instantiate(src, root);
            copy.name = name;
            copy.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            if (name == "Water") foreach (var c in copy.GetComponents<Collider>()) Object.DestroyImmediate(c);
        }
        var weather = root.Find("Typhoon")?.GetComponent<TyphoonWeather>();
        var mainCam = root.Find("Main Camera");
        var brain = mainCam != null ? mainCam.GetComponent<CinemachineBrain>() : null;
        if (brain != null) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

        // boat
        var boatRig = new GameObject("BoatRig").transform;
        boatRig.SetParent(root, false);
        if (boat != null)
        {
            var b = Object.Instantiate(boat.gameObject, boatRig);
            b.name = "Boat";
            b.transform.localPosition = Vector3.zero;
            b.transform.localRotation = Quaternion.identity;
            Strip(b);
        }
        boatRig.gameObject.AddComponent<FloatOnWater>().offset = boat != null ? boatStart.y : 0.05f;

        SceneManager.SetActiveScene(scene);
        RenderSettings.skybox = lighting.sky;
        RenderSettings.fog = lighting.fog;
        RenderSettings.fogColor = lighting.fogColor;
        RenderSettings.fogMode = lighting.fogMode;
        RenderSettings.fogDensity = lighting.fogDensity;
        RenderSettings.ambientMode = lighting.ambientMode;
        RenderSettings.ambientIntensity = lighting.ambientIntensity;
        RenderSettings.ambientLight = lighting.ambientLight;
        var sun = root.Find("Directional Light")?.GetComponent<Light>();
        if (sun != null) RenderSettings.sun = sun;

        // ---------------- director
        var directorGo = new GameObject("MenuDirector");
        directorGo.transform.SetParent(root, false);
        var director = directorGo.AddComponent<PlayableDirector>();
        director.playOnAwake = false;
        var manager = directorGo.AddComponent<CutsceneManager>();
        manager.cutsceneName = "Cut_Menu_Loop";
        manager.loop = true;
        manager.letterbox = false;
        manager.skippable = false;
        manager.playOnStart = false;
        manager.stillFrame = StillFrame;
        manager.overlayOrder = -10;
        directorGo.AddComponent<CutsceneCueReceiver>().weather = weather;
        var cinematic = directorGo.AddComponent<MenuCinematic>();
        cinematic.manager = manager;
        cinematic.weather = weather;
        cinematic.skybox = lighting.sky;
        cinematic.fog = lighting.fog;
        cinematic.fogColor = lighting.fogColor;
        cinematic.fogMode = lighting.fogMode;
        cinematic.fogDensity = lighting.fogDensity;
        cinematic.ambientMode = lighting.ambientMode;
        cinematic.ambientIntensity = lighting.ambientIntensity;
        cinematic.ambientLight = lighting.ambientLight;
        cinematic.sun = sun;
        AssetDatabase.DeleteAsset(TimelinePath);
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, TimelinePath);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = Length;
        director.playableAsset = timeline;

        // ---------------- the boat's run and the crew
        var fwd = boat != null ? boat.transform.forward : Vector3.forward;
        var heading = -new Vector3(fwd.x, 0f, fwd.z).normalized;          // up the open street, away from the boat's start view
        var right = Vector3.Cross(Vector3.up, heading);
        var p1 = boatStart - heading * 2f;
        var p2 = boatStart + heading * 14f;
        var boatMove = boatRig.gameObject.AddComponent<ShotMove>();
        boatMove.director = director;
        boatMove.start = 3.6; boatMove.end = 8.2;
        boatMove.fromPosition = new Vector3(p1.x, 0f, p1.z); boatMove.toPosition = new Vector3(p2.x, 0f, p2.z);
        boatMove.fromEuler = boatMove.toEuler = Quaternion.LookRotation(heading).eulerAngles;
        boatMove.ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        boatRig.position = boatMove.fromPosition;
        boatRig.rotation = Quaternion.LookRotation(heading);

        var crew = new GameObject("Crew").transform;
        crew.SetParent(root, false);
        CharacterSpot Spot(string name, Transform parent, Vector3 local, Vector3 lookDir, string id, CharacterPose pose, PoseOverlay o, bool world = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (world) { go.transform.position = local; go.transform.rotation = Quaternion.LookRotation(lookDir); }
            else { go.transform.localPosition = local; go.transform.localRotation = Quaternion.LookRotation(lookDir); }
            var s = go.AddComponent<CharacterSpot>();
            s.id = id; s.pose = pose; s.overlays = o;
            return s;
        }
        // in the boat: a pilot at the tiller, a rescuer reaching over the side with a flashlight
        var pilot = Spot("Pilot_C21", boatRig, new Vector3(0f, 0.15f, -1.3f), Vector3.forward, "C21", CharacterPose.Idle, PoseOverlay.Seated | PoseOverlay.HoldTiller);
        var tiller = new GameObject("Tiller").transform; tiller.SetParent(boatRig, false); tiller.localPosition = new Vector3(0.25f, 0.55f, -1.75f);
        pilot.tillerHand = tiller;
        var reacher = Spot("Rescuer_C24", boatRig, new Vector3(-0.35f, 0.15f, 0.3f), Vector3.left, "C24", CharacterPose.Idle, PoseOverlay.HoldOn);
        var child = Spot("Child_C03", root, p2 - right * 1.9f + Vector3.up * 0f, right, "C03", CharacterPose.Float, PoseOverlay.WaveForHelp, world: true);
        child.floatOnWater = true;
        child.floatOffset = -0.35f;
        var hands = new GameObject("ReachTo").transform; hands.SetParent(child.transform, false); hands.localPosition = new Vector3(0f, 1.25f, 0.3f);
        reacher.leftHand = hands; reacher.rightHand = hands;
        reacher.lookAt = hands;
        // flashlight from the rescuer's hands onto the child
        var torch = new GameObject("Flashlight").AddComponent<Light>();
        torch.transform.SetParent(boatRig, false);
        torch.transform.localPosition = new Vector3(-0.6f, 1.4f, 0.4f);
        torch.transform.rotation = Quaternion.LookRotation((p2 - right * 1.9f + Vector3.up * 0.8f) - (p2 + Vector3.up * 1.4f));
        torch.type = LightType.Spot; torch.color = new Color(1f, 0.93f, 0.78f); torch.intensity = 12f; torch.range = 14f; torch.spotAngle = 28f;
        torch.shadows = LightShadows.None;

        // at the wire: a rescuer in the water, arm up, warning the boat off
        var warnAt = wire + new Vector3(-6f, 0f, -4.5f);
        var warn = Spot("Warning_C22", root, new Vector3(warnAt.x, -1f, warnAt.z), Flat(boatStart - warnAt), "C22", CharacterPose.Idle, PoseOverlay.WaveForHelp, world: true);
        var fx = new GameObject("Effects").transform; fx.SetParent(root, false);
        var add = CutsceneKit.ParticleMaterial("Mat_ParticleAdd", additive: true);
        var soft = CutsceneKit.ParticleMaterial("Mat_ParticleSoft");
        var toCam = Flat(wire - warnAt);
        var sparkAt = new Vector3(warnAt.x, 0f, warnAt.z) + toCam * 2.6f + Vector3.Cross(Vector3.up, toCam) * 0.8f;
        var poleAt = sparkAt + Vector3.Cross(Vector3.up, toCam) * 4.5f + toCam * 1.0f;
        var concrete = CutsceneKit.Lit("Mat_PoleConcrete", new Color(0.62f, 0.62f, 0.6f));
        var cableMat = CutsceneKit.Lit("Mat_Cable", new Color(0.05f, 0.05f, 0.05f), 0.5f);
        var cable = new GameObject("Prop_DownedWire").transform; cable.SetParent(root, false);
        CutsceneKit.Part(cable, PrimitiveType.Cylinder, new Vector3(poleAt.x, 3f, poleAt.z), new Vector3(0.32f, 4f, 0.32f), concrete);
        CutsceneKit.Part(cable, PrimitiveType.Cube, new Vector3(poleAt.x, 6.6f, poleAt.z), new Vector3(1.6f, 0.12f, 0.12f), concrete,
                         Quaternion.LookRotation(toCam).eulerAngles);
        var top = new Vector3(poleAt.x, 6.5f, poleAt.z);
        var sag = Vector3.Lerp(top, sparkAt, 0.55f) + Vector3.down * 0.8f;
        foreach (var (a0, a1) in new[] { (top, sag), (sag, sparkAt) })
            CutsceneKit.Part(cable, PrimitiveType.Cylinder, (a0 + a1) * 0.5f, new Vector3(0.04f, Vector3.Distance(a0, a1) * 0.5f, 0.04f), cableMat,
                             Quaternion.FromToRotation(Vector3.up, a1 - a0).eulerAngles);
        wire = sparkAt;
        var sparks = CutsceneKit.Particles("FX_WireSparks", fx, wire + Vector3.up * 0.15f, add, new Color(1f, 0.85f, 0.45f, 1f), 70,
                                           new Vector2(0.15f, 0.45f), new Vector2(2f, 6f), new Vector2(0.03f, 0.08f), new Vector3(0.3f, 0.1f, 0.3f), 1.4f, 200);
        var sparkShape = sparks.shape; sparkShape.shapeType = ParticleSystemShapeType.Hemisphere; sparkShape.radius = 0.2f;
        var sparkLight = new GameObject("SparkLight").AddComponent<Light>();
        sparkLight.transform.SetParent(fx, false); sparkLight.transform.position = wire + Vector3.up * 0.6f;
        sparkLight.type = LightType.Point; sparkLight.color = new Color(0.75f, 0.85f, 1f); sparkLight.range = 9f; sparkLight.shadows = LightShadows.None;
        sparkLight.gameObject.AddComponent<LightFlicker>().baseIntensity = 2.5f;
        var wake = CutsceneKit.Particles("FX_BoatWake", boatRig, Vector3.zero, soft, new Color(0.9f, 0.88f, 0.82f, 0.7f), 0,
                                         new Vector2(0.8f, 1.6f), new Vector2(0.5f, 1.5f), new Vector2(0.3f, 0.9f), new Vector3(1.4f, 0.1f, 0.3f), 0.2f, 300);
        wake.transform.localPosition = new Vector3(0f, 0.15f, -2f);
        var em = wake.emission; em.rateOverDistance = 10f;
        var spray = CutsceneKit.Particles("FX_BowSpray", boatRig, Vector3.zero, soft, new Color(0.92f, 0.92f, 0.9f, 0.8f), 0,
                                          new Vector2(0.3f, 0.7f), new Vector2(1.5f, 3.5f), new Vector2(0.08f, 0.25f), new Vector3(1.2f, 0.1f, 0.2f), 1f, 200);
        spray.transform.localPosition = new Vector3(0f, 0.3f, 1.8f);
        var sem = spray.emission; sem.rateOverDistance = 14f;
        CutsceneKit.Particles("FX_Mist", fx, Centre + new Vector3(0f, 1.5f, 0f), soft, new Color(0.75f, 0.78f, 0.8f, 0.14f), 6,
                              new Vector2(6f, 9f), new Vector2(0.2f, 0.6f), new Vector2(8f, 14f), new Vector3(110f, 2f, 90f), 0f, 70);

        // the team on a roof, against the sky
        var roof = new Vector3(p2.x, -1f, p2.z) + heading * 12f;
        var team = new GameObject("Team").transform; team.SetParent(root, false);
        var faceCam = -heading;
        string[] ids = { "C21", "C23", "C24", "C22" };
        for (int i = 0; i < ids.Length; i++)
        {
            var at = roof + Quaternion.LookRotation(faceCam) * new Vector3((i - 1.5f) * 0.95f, 0f, (i % 2) * 0.35f);
            at.y = -0.5f;                                       // on a submerged kerb: waist deep
            Spot("Team_" + ids[i], team, at, faceCam, ids[i], CharacterPose.Idle, i == 2 ? PoseOverlay.WaveForHelp : PoseOverlay.None, world: true);
        }

        // ---------------- shots
        var shotsRoot = new GameObject("Shots").transform; shotsRoot.SetParent(root, false);
        var focus = new GameObject("Focus").transform; focus.SetParent(shotsRoot, false);
        var cams = new List<CinemachineCamera>();
        void Add(string name, double s, double e, Vector3 a, Vector3 b, Transform look, float fov = 50f, bool hand = true) =>
            cams.Add(CutsceneKit.Shot(shotsRoot, name, director, s, e, a, b, look, fov, hand));

        var roofsFrom = Centre + new Vector3(-38f, 24f, -42f);
        var roofsLook = CutsceneKit.Focus(focus, "F_Roofs", Centre + new Vector3(12f, 0f, 10f));
        Add("Shot_01_RainOverRooftops", 0, 4, roofsFrom, Centre + new Vector3(-24f, 20f, -30f), roofsLook, 50f, false);
        var boatLook = CutsceneKit.Focus(focus, "F_BoatRun", Vector3.zero);
        boatLook.SetParent(boatRig, false); boatLook.localPosition = new Vector3(0f, 0.8f, 0.5f);
        Add("Shot_02_BoatInFloodedStreet", 4, 8, p1 + right * 5.5f + heading * 3f + Vector3.up * 1.2f, p2 + right * 5f - heading * 1f + Vector3.up * 1.0f, boatLook, 45f);
        var rescueLook = CutsceneKit.Focus(focus, "F_Rescue", p2 - right * 1.2f + Vector3.up * 0.9f);
        Add("Shot_03_FlashlightRescue", 8, 12, p2 - right * 3.4f + heading * 2.6f + Vector3.up * 1.3f, p2 - right * 3.0f + heading * 1.9f + Vector3.up * 1.15f, rescueLook, 38f);
        var wireLook = CutsceneKit.Focus(focus, "F_Warning", warnAt + Vector3.up * 0.9f);
        var wireCam = wire + toCam * 3.2f + Vector3.up * 0.9f;
        var side = Vector3.Cross(Vector3.up, toCam);
        Add("Shot_04_LiveWireStop", 12, 16, wireCam - side * 1.2f, wireCam - side * 0.4f, wireLook, 45f);
        var teamLook = CutsceneKit.Focus(focus, "F_Team", roof + Vector3.up * 2.6f);
        var teamCam = roof + faceCam * 6.5f + Vector3.up * 1.25f;
        Add("Shot_05_TeamCloudBreak", 16, 20, teamCam, teamCam + faceCam * -1.5f + Vector3.up * 0.6f, teamLook, 48f, false);
        Add("Shot_01_LoopIn", 19.2, 20, roofsFrom, roofsFrom, roofsLook, 50f, false);

        // Cameras track: the five shots, then the loop-in blending in under the last 0.8 s.
        var cmTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        director.SetGenericBinding(cmTrack, brain);
        for (int i = 0; i < cams.Count; i++)
        {
            var ms = cams[i].GetComponent<ShotMove>();
            var clip = cmTrack.CreateClip<CinemachineShot>();
            clip.start = ms.start;
            clip.duration = ms.end - ms.start;
            clip.displayName = i < Shots.Length ? Shots[i].name : "Cut_Menu_LoopIn";
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, cams[i]);
        }

        // Storm: the rain over the roofs, the worst of it, the break in the clouds, and back for the loop.
        var storm = timeline.CreateTrack<StormTrack>(null, "Storm");
        director.SetGenericBinding(storm, weather);
        void Storm(double s, double e, float a, float b)
        {
            var c = storm.CreateClip<StormClip>(); c.start = s; c.duration = e - s;
            var st = (StormClip)c.asset; st.from = a; st.to = b;
        }
        Storm(0, 16, 0.82f, 0.86f);
        Storm(16, 18.6, 0.86f, 0.15f);
        Storm(18.6, 19.2, 0.15f, 0.15f);
        Storm(19.2, 20, 0.15f, 0.82f);

        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Lightning", 1.6);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Thunder", 2.2);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Lightning", 13.2);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Thunder", 13.6);
        EditorUtility.SetDirty(timeline);

        // Ambience under the menu music: rain and wind, playing on their own (a Timeline clip would restart each loop).
        var audioRoot = new GameObject("Ambience").transform; audioRoot.SetParent(root, false);
        var a = CutsceneAssets.Current;
        foreach (var (name, clip, vol) in new[] { ("Amb_RainHeavy", a.rainHeavy, 0.35f), ("Amb_Wind", a.wind, 0.25f) })
        {
            var src = CutsceneKit.Source(audioRoot, name, CutsceneAudio.Bus.Ambience);
            src.clip = clip; src.loop = true; src.playOnAwake = true; src.volume = vol;
        }

        // ---------------- save
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        AssetDatabase.SaveAssets();
        EditorSceneManager.CloseScene(scene, true);
        SceneManager.SetActiveScene(map);
        return "menu cinematic built: " + copied + " map pieces (" + tris + " triangles), " + cams.Count + " shots, " + Length + " s loop";
    }

    /// <summary>Copies the pieces of <paramref name="node"/> that stand in the slice, walking down until a piece is small
    /// enough to be one thing (a house, a tree, a pole), so a group that spans the map is not copied whole.</summary>
    static void CopyNear(Transform node, Transform into, ref int copied, ref int tris)
    {
        foreach (Transform piece in node)
        {
            if (!Bounds(piece, out var b)) continue;
            float reach = new Vector2(b.center.x - Centre.x, b.center.z - Centre.z).magnitude;
            if (reach > Radius + b.extents.magnitude) continue;                 // nothing of it is near
            if (Mathf.Max(b.extents.x, b.extents.z) > 30f && piece.childCount > 0) { CopyNear(piece, into, ref copied, ref tris); continue; }
            if (reach > Radius) continue;
            Copy(piece, into, ref copied, ref tris);
        }
    }

    static void Copy(Transform piece, Transform into, ref int copied, ref int tris)
    {
        var copy = Object.Instantiate(piece.gameObject, into);
        copy.name = piece.name;
        copy.transform.SetPositionAndRotation(piece.position, piece.rotation);
        copy.transform.localScale = piece.lossyScale;
        Strip(copy);
        copied++;
        foreach (var mf in copy.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
    }

    static bool Bounds(Transform t, out Bounds b)
    {
        b = default;
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return true;
    }

    static bool Near(Transform piece)
    {
        var rs = piece.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return new Vector2(b.center.x - Centre.x, b.center.z - Centre.z).magnitude < Radius + b.extents.magnitude * 0.5f;
    }

    /// <summary>Only what is drawn: no colliders, bodies or game scripts (the menu is a backdrop).</summary>
    static void Strip(GameObject go)
    {
        // scripts first (several passes: one that another needs goes once its dependant has), then bodies and colliders
        for (int pass = 0; pass < 4; pass++)
            foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (m != null && !RequiredByAnother(m)) Object.DestroyImmediate(m);
        foreach (var j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
    }

    static bool RequiredByAnother(Component c)
    {
        foreach (var other in c.GetComponents<MonoBehaviour>())
        {
            if (other == null || other == c) continue;
            foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if ((req.m_Type0 != null && req.m_Type0.IsInstanceOfType(c)) || (req.m_Type1 != null && req.m_Type1.IsInstanceOfType(c)) ||
                    (req.m_Type2 != null && req.m_Type2.IsInstanceOfType(c))) return true;
        }
        return false;
    }

    static Vector3 RoofNear(Vector3 at)
    {
        var hits = Physics.RaycastAll(at + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore)
                          .Where(h => h.collider.GetComponentInParent<WaterSurface>() == null && h.collider.GetComponentInParent<Survivor>() == null)
                          .OrderBy(h => h.distance).ToArray();
        return hits.Length > 0 ? hits[0].point : at;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
}
