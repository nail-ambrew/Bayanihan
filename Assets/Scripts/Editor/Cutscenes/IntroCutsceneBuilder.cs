using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// Bayanihan > Cutscenes > Build Intro: makes Scene_IntroCutscene and its Timeline (Cut_Intro), the story of the flood in
/// five beats over the real Mabuhay map. The Dela Cruz family (Ben, his daughter Joy and his mother Lola Nena, the same
/// three the mission asks the player to rescue) are packing to leave as the storm comes in; the water takes the street,
/// the house next door, the family photo and Joy, and ends with the crew at the Barangay Hall setting out to find them.
/// The shots are placed from the map itself: it is opened, the boat's start (the RescueBase_Spawn marker), the hall, the
/// residents' anchors and the streets are read (raycasts find roofs, walls and ground), then the intro scene is written
/// beside it. The MabuhayGameplay scene is only read, never changed.
///
/// To retime, change <see cref="Beats"/> and the times in MakeShots / FillTimeline and run it again. Everything it makes
/// is listed in Assets/Cutscenes/REPORT.md.
/// </summary>
public static class IntroCutsceneBuilder
{
    public const string Folder = CutsceneKit.Root + "/Intro";
    public const string ScenePath = Folder + "/Scene_IntroCutscene.unity";
    public const string TimelinePath = Folder + "/Timelines/Cut_Intro.playable";
    public const string SignalFolder = Folder + "/Signals";
    public const string LookPath = Folder + "/Cut_Intro_Look.asset";
    const string MapScene = "Assets/Scenes/Maps/MabuhayGameplay.unity";

    /// <summary>The five beats (seconds): retime here.</summary>
    public static readonly (string name, double start, double end)[] Beats =
    {
        ("Cut_Intro_01_BeforeTheStorm", 0.0, 12.0),
        ("Cut_Intro_02_TheStormHits", 12.0, 22.0),
        ("Cut_Intro_03_Tragedy", 22.0, 40.0),
        ("Cut_Intro_04_Hope", 40.0, 49.0),
        ("Cut_Intro_05_Handover", 49.0, 55.0),
    };
    public const double Length = 55.0;

    // What the builder read from the map.
    struct Map
    {
        public Vector3 boat; public Quaternion boatRot; public Vector3 fwd;       // the start pose, and the way the bow points
        public Vector3 hall; public float hallRoof; public Vector3 landing;
        public Vector3 tessaRoof, nenaRoof, benPost, joyWater;
        public float storm;
        public Vector3 home, toStreet;                                              // the street in front of the Dela Cruz house, and the way from the house to it
        public Vector3 puddle, drain;
        public Vector3 dog, shanty, tricycle;
        public Vector3 roofOut; public float roofEdge;                              // the way off the roof towards open water, and how far from the anchor
        public Vector3 photoLaunch;
    }

    /// <summary>
    /// A point placed by hand on the map when it was drawn at twice real size, moved to where it is on the map now
    /// (MabuhayGameplayBuilder.MapScale). Heights are world heights and stay.
    /// </summary>
    static Vector3 OnMap(Vector3 atTwice) =>
        new Vector3(atTwice.x * MabuhayGameplayBuilder.MapScale / 2f, atTwice.y, atTwice.z * MabuhayGameplayBuilder.MapScale / 2f);

    [MenuItem("Bayanihan/Cutscenes/Build Intro", false, 61)]
    public static void BuildFromMenu() => Debug.Log("IntroCutsceneBuilder: " + Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) return "stop Play mode first";
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
        foreach (var f in new[] { Folder + "/Timelines", Folder + "/Prefabs", SignalFolder, Folder + "/Audio" }) CutsceneKit.EnsureFolder(f);

        // ---------------- read the map
        var mapScene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
        var map = ReadMap(out string problems);

        // ---------------- the intro scene, beside it
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root = new GameObject("IntroCutscene").transform;

        var dog = CutsceneKit.Dog(Folder + "/Prefabs");
        var rubbish = CutsceneKit.Rubbish(Folder + "/Prefabs");
        var photoPrefab = CutsceneKit.FamilyPhoto(Folder + "/Prefabs");
        var shantyPrefab = CutsceneKit.Shanty(Folder + "/Prefabs");
        var tricyclePrefab = CutsceneKit.Tricycle(Folder + "/Prefabs");
        var gearPrefab = CutsceneKit.RescueGear(Folder + "/Prefabs");

        // director
        var directorGo = new GameObject("IntroDirector");
        directorGo.transform.SetParent(root, false);
        var director = directorGo.AddComponent<PlayableDirector>();
        director.playOnAwake = false;
        var manager = directorGo.AddComponent<CutsceneManager>();
        manager.cutsceneName = "Cut_Intro";
        manager.playOnStart = false;               // IntroCutscene binds the map first, then plays it
        manager.skippable = true;
        manager.letterbox = true;
        var cues = directorGo.AddComponent<CutsceneCueReceiver>();
        var intro = directorGo.AddComponent<IntroCutscene>();
        intro.manager = manager;
        intro.cues = cues;

        var timeline = MakeTimeline();
        director.playableAsset = timeline;

        // ---------------- props
        var props = new GameObject("Props").transform;
        props.SetParent(root, false);
        T Place<T>(GameObject prefab, Transform parent, Vector3 at, Quaternion rot) where T : Component
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(at, rot);
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }
        var drainPrefab = CutsceneKit.StormDrain(Folder + "/Prefabs");
        var d = (GameObject)PrefabUtility.InstantiatePrefab(drainPrefab, scene);
        d.transform.SetParent(props, true);
        d.transform.position = map.drain + Vector3.up * 0.005f;
        // rubbish caught on the grate
        var rnd = new System.Random(9);
        for (int i = 0; i < 7; i++)
        {
            var p = rubbish[i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 5];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, scene);
            go.transform.SetParent(props, true);
            go.transform.position = map.drain + new Vector3((float)rnd.NextDouble() * 1.2f - 0.6f, 0.05f, (float)rnd.NextDouble() * 0.8f - 0.4f);
            go.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            go.transform.localScale = Vector3.one * (p == rubbish[5] ? 0.6f : 1f);
        }
        var dogGo = (GameObject)PrefabUtility.InstantiatePrefab(dog, scene);
        dogGo.transform.SetParent(props, true);
        dogGo.transform.position = map.dog;
        dogGo.transform.rotation = Quaternion.LookRotation(Flat(map.home - map.dog));
        var siren = CutsceneKit.Siren(props, new Vector3(map.hall.x + 6f, map.hallRoof, map.hall.z - 8f));

        // the neighbour's shanty, which gives way; the tricycle, which tips
        var up = Vector3.up;
        var shanty = Place<IntroPropMotion>(shantyPrefab, props, map.shanty, Quaternion.LookRotation(Flat(map.nenaRoof - map.shanty)));
        shanty.mode = IntroPropMotion.Mode.TiltAndSink;
        shanty.tiltAxis = Vector3.Cross(up, Flat(map.nenaRoof - map.shanty));        // leans away from the family's roof
        shanty.tiltDegrees = 38f; shanty.sinkMetres = 1.8f; shanty.seconds = 3.4f;
        var alongStreet = Vector3.Cross(up, map.toStreet);
        var tricycle = Place<IntroPropMotion>(tricyclePrefab, props, map.tricycle, Quaternion.LookRotation(alongStreet));
        tricycle.mode = IntroPropMotion.Mode.TiltAndSink;
        tricycle.tiltAxis = alongStreet; tricycle.tiltDegrees = 75f; tricycle.sinkMetres = 0.15f; tricycle.seconds = 1.4f;

        // ---------------- the cast: where each actor stands in each part of the story (see IntroCutscene.castRoot)
        var cast = new GameObject("Cast").transform;
        cast.SetParent(root, false);
        var focus = new GameObject("Focus").transform;
        focus.SetParent(root, false);
        Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(cast, false); g.gameObject.SetActive(false); return g; }
        Transform Look(string name, Vector3 at) => CutsceneKit.Focus(focus, name, at);

        CharacterSpot Spot(Transform group, string name, string id, Vector3 at, Vector3 facing, PoseOverlay overlays = PoseOverlay.None,
                           Transform lookAt = null, CharacterPose pose = CharacterPose.Idle, bool floatOnWater = false, float floatOffset = -1.1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Flat(facing)));
            var s = go.AddComponent<CharacterSpot>();
            s.id = id; s.pose = pose; s.overlays = overlays; s.lookAt = lookAt;
            s.floatOnWater = floatOnWater; s.floatOffset = floatOffset;
            return s;
        }

        // Home: the family on the street in front of their house, the street's side of them to the camera.
        var side = Vector3.Cross(up, map.toStreet);
        var homeAll = Group("HomeAll");
        var nenaHomeAt = Ground(map.home + up * 2f, 4f);
        var nenaLook = Look("Look_NenaHome", nenaHomeAt + map.toStreet * 3f + up * 0.9f);
        Spot(homeAll, "Nena_Home", "C26", nenaHomeAt, map.toStreet, PoseOverlay.Clutch, nenaLook);
        var benHomeAt = Ground(map.home + side * 1.15f + up * 2f, 4f);
        Spot(homeAll, "Ben_Home", "C16", benHomeAt, nenaHomeAt - benHomeAt, PoseOverlay.None, Look("Look_BenHome", nenaHomeAt + up * 1.55f));
        var joyHomeAt = Ground(map.home - side * 0.95f + up * 2f, 4f);
        Spot(homeAll, "Joy_Home", "C01", joyHomeAt, nenaHomeAt - joyHomeAt, PoseOverlay.Hug, Look("Look_JoyHome", nenaHomeAt + up * 1.1f));
        Place<Transform>(photoPrefab, homeAll, nenaHomeAt + map.toStreet * 0.3f + up * 0.98f, Quaternion.LookRotation(-map.toStreet) * Quaternion.Euler(-20f, 0f, 0f));

        // The roof: Nena where the mission finds her, Joy and Ben in each other's arms beside her.
        var roofNena = Group("RoofNena");
        Spot(roofNena, "Nena_Roof", "C26", map.nenaRoof, map.roofOut, PoseOverlay.Clutch, Look("Look_NenaRoof", map.nenaRoof + map.roofOut * 3f + up * 0.7f));
        var photo = Place<IntroPropMotion>(photoPrefab, roofNena, map.nenaRoof + map.roofOut * 0.3f + up * 0.98f, Quaternion.LookRotation(-map.roofOut) * Quaternion.Euler(-20f, 0f, 0f));
        photo.mode = IntroPropMotion.Mode.Fall;
        photo.launch = map.photoLaunch;
        photo.drift = map.roofOut * 0.5f + Vector3.Cross(up, map.roofOut) * 0.3f;
        var roofSide = Vector3.Cross(up, map.roofOut);
        var joyRoofAt = OnRoof(map.nenaRoof, map.nenaRoof + roofSide * 1.05f);
        var benRoofAt = OnRoof(map.nenaRoof, map.nenaRoof + roofSide * 1.6f);
        var roofJoy = Group("RoofJoy");
        Spot(roofJoy, "Joy_Roof", "C01", joyRoofAt, benRoofAt - joyRoofAt, PoseOverlay.Hug, Look("Look_JoyRoof", benRoofAt + up * 0.95f));
        var roofBen = Group("RoofBen");
        Spot(roofBen, "Ben_Roof", "C16", benRoofAt, joyRoofAt - benRoofAt, PoseOverlay.Hug, Look("Look_BenRoof", joyRoofAt + up * 0.85f));
        var phoneTorch = new GameObject("PhoneTorch").AddComponent<Light>();     // Lola Nena's phone, on her face
        phoneTorch.transform.SetParent(roofNena, false);
        phoneTorch.transform.position = map.nenaRoof + map.roofOut * 0.45f + up * 1.05f;
        phoneTorch.transform.rotation = Quaternion.LookRotation((map.nenaRoof + up * 1.2f) - phoneTorch.transform.position);
        phoneTorch.type = LightType.Spot; phoneTorch.color = new Color(0.8f, 0.9f, 1f); phoneTorch.intensity = 0.7f;
        phoneTorch.range = 3f; phoneTorch.spotAngle = 38f; phoneTorch.shadows = LightShadows.None;

        // The water and the post, where the mission finds Joy and Ben.
        var waterJoy = Group("WaterJoy");
        Spot(waterJoy, "Joy_Water", "C01", map.joyWater, map.benPost - map.joyWater, PoseOverlay.WaveForHelp, null, CharacterPose.Float, true, -0.35f);
        var postBen = Group("PostBen");
        Spot(postBen, "Ben_Post", "C16", map.benPost, map.joyWater - map.benPost, PoseOverlay.ShoutCall, null, CharacterPose.Idle, true, -1.1f);

        // The Barangay Hall: the Captain at the radio and three of the crew with the gear.
        var hallGroup = Group("Hall");
        var toBoat = Flat(map.boat - map.landing);
        var landSide = Vector3.Cross(up, toBoat);
        Vector3 Land(Vector3 at) => Ground(new Vector3(at.x, map.landing.y + 0.6f, at.z), 0f);      // the porch step if there is one under this spot, else the street
        Spot(hallGroup, "Captain_C28", "C28", Land(map.landing), toBoat, PoseOverlay.None, Look("Look_Captain", map.landing + toBoat * 4f + up * 1.5f));
        Spot(hallGroup, "Crew_C21", "C21", Land(map.landing + landSide * 1.7f + toBoat * 0.8f), toBoat, PoseOverlay.None, Look("Look_Crew21", map.landing + toBoat * 6f + up * 1.2f));
        Spot(hallGroup, "Crew_C22", "C22", Land(map.landing - landSide * 1.6f + toBoat * 1.2f), toBoat, PoseOverlay.None, Look("Look_Crew22", map.landing + toBoat * 6f + up * 1.2f));
        Spot(hallGroup, "Crew_C23", "C23", Land(map.landing + landSide * 3.2f + toBoat * 0.2f), toBoat, PoseOverlay.None, Look("Look_Crew23", map.landing + toBoat * 6f + up * 1.2f));
        Place<Transform>(gearPrefab, hallGroup, Land(map.landing + landSide * 2.2f + toBoat * 2.4f), Quaternion.LookRotation(landSide));

        // The light Lola Nena sees: the rescue boat's torch, far off.
        var beacon = Group("Beacon");
        var beaconMat = CutsceneKit.Lit("Mat_Beacon", new Color(1f, 0.95f, 0.8f), 0.1f, new Color(1f, 0.9f, 0.6f) * 6f);
        var beaconAt = map.boat + up * 2.4f;
        CutsceneKit.Part(beacon, PrimitiveType.Sphere, beaconAt, Vector3.one * 2.2f, beaconMat);
        var beaconLight = new GameObject("BeaconLight").AddComponent<Light>();
        beaconLight.transform.SetParent(beacon, false);
        beaconLight.transform.position = beaconAt;
        beaconLight.type = LightType.Point; beaconLight.color = new Color(1f, 0.92f, 0.7f); beaconLight.intensity = 60f;
        beaconLight.range = 30f; beaconLight.shadows = LightShadows.None;

        intro.castRoot = cast;
        intro.photo = photo;
        intro.shanty = shanty;
        intro.tricycle = tricycle;

        // ---------------- effects
        var fx = new GameObject("Effects").transform;
        fx.SetParent(root, false);
        var soft = CutsceneKit.ParticleMaterial("Mat_ParticleSoft");
        var effects = new List<ParticleSystem>
        {
            CutsceneKit.Particles("FX_DrainBubbles", fx, map.drain, soft, new Color(0.55f, 0.47f, 0.34f, 0.8f), 40,
                                  new Vector2(0.4f, 0.9f), new Vector2(0.4f, 1.2f), new Vector2(0.08f, 0.22f), new Vector3(0.8f, 0.05f, 0.5f), 0.3f, 120),
            CutsceneKit.Particles("FX_RiverFoam", fx, OnMap(new Vector3(70f, -0.6f, -400f)), soft, new Color(0.7f, 0.62f, 0.48f, 0.9f), 160,
                                  new Vector2(2f, 4f), new Vector2(0.5f, 2f), new Vector2(1.5f, 4f), OnMap(new Vector3(140f, 0.2f, 12f)), 0f, 500),
            CutsceneKit.Particles("FX_Mist_Hall", fx, map.hall + new Vector3(0, 1.5f - map.hall.y, -20f), soft, new Color(0.75f, 0.78f, 0.8f, 0.18f), 6,
                                  new Vector2(6f, 9f), new Vector2(0.2f, 0.6f), new Vector2(8f, 14f), new Vector3(60f, 2f, 40f), 0f, 60),
            CutsceneKit.Particles("FX_Mist_Home", fx, map.home + new Vector3(0, 1.5f, 0f), soft, new Color(0.75f, 0.78f, 0.8f, 0.16f), 6,
                                  new Vector2(6f, 9f), new Vector2(0.2f, 0.6f), new Vector2(8f, 14f), new Vector3(60f, 2f, 50f), 0f, 60),
        };
        var splash = CutsceneKit.Particles("FX_BoatLaunchSplash", fx, map.boat + map.fwd * 2f + up * 0.2f, soft,
                                           new Color(0.85f, 0.85f, 0.8f, 0.9f), 120, new Vector2(0.5f, 1.1f), new Vector2(3f, 6f),
                                           new Vector2(0.1f, 0.35f), new Vector3(1.6f, 0.1f, 0.6f), 1.2f, 160, loop: false);
        var sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f;
        splash.transform.rotation = Quaternion.LookRotation(Vector3.up);
        effects.Add(splash);
        intro.effects = effects.ToArray();
        intro.splash = splash;

        // debris pool: let go behind the boat, carried past it down the street
        var poolGo = new GameObject("DebrisPool");
        poolGo.transform.SetParent(root, false);
        var debrisAt = map.boat - map.fwd * 8f;
        poolGo.transform.position = new Vector3(debrisAt.x, 0f, debrisAt.z);
        var pool = poolGo.AddComponent<IntroDebrisPool>();
        pool.extraKinds = rubbish;
        pool.area = new Vector2(10f, 8f);
        pool.flow = map.fwd + Vector3.right * 0.15f;
        intro.debris = pool;

        // ---------------- look (post-processing on top of the map's own)
        var look = new GameObject("CutsceneLook").AddComponent<Volume>();
        look.transform.SetParent(root, false);
        look.isGlobal = true;
        look.priority = 10;
        look.sharedProfile = LookProfile();
        intro.look = look;

        // ---------------- shots
        var shotsRoot = new GameObject("Shots").transform;
        shotsRoot.SetParent(root, false);
        var shots = MakeShots(shotsRoot, director, map, siren.transform, photo.transform, shanty.transform, tricycle.transform,
                              nenaHomeAt, benRoofAt, out var settle, out var timing);
        intro.shots = shots.ToArray();
        intro.settle = settle;
        intro.siren = siren;

        // ---------------- audio sources
        var audioRoot = new GameObject("Audio").transform;
        audioRoot.SetParent(root, false);
        var sources = new Dictionary<string, AudioSource>
        {
            { "RainLight", CutsceneKit.Source(audioRoot, "Amb_RainLight", CutsceneAudio.Bus.Ambience) },
            { "RainHeavy", CutsceneKit.Source(audioRoot, "Amb_RainHeavy", CutsceneAudio.Bus.Ambience) },
            { "TinDrips", CutsceneKit.Source(audioRoot, "Amb_TinDrips", CutsceneAudio.Bus.Ambience) },
            { "WaterRush", CutsceneKit.Source(audioRoot, "Amb_WaterRush", CutsceneAudio.Bus.Ambience) },
            { "Wind", CutsceneKit.Source(audioRoot, "Amb_Wind", CutsceneAudio.Bus.Ambience) },
            { "Radio", CutsceneKit.Source(audioRoot, "VO_Radio", CutsceneAudio.Bus.Vo) },
            { "Ben", CutsceneKit.Source(audioRoot, "VO_Ben", CutsceneAudio.Bus.Vo) },
            { "Nena", CutsceneKit.Source(audioRoot, "VO_Nena", CutsceneAudio.Bus.Vo) },
            { "Joy", CutsceneKit.Source(audioRoot, "VO_Joy", CutsceneAudio.Bus.Vo) },
            { "Crowd", CutsceneKit.Source(audioRoot, "VO_Neighbours", CutsceneAudio.Bus.Vo) },
            { "Music", CutsceneKit.Source(audioRoot, "Music_Cue", CutsceneAudio.Bus.Music) },
            { "Bed", CutsceneKit.Source(audioRoot, "Music_Bed", CutsceneAudio.Bus.Music) },
            { "Siren", CutsceneKit.Source(audioRoot, "Sfx_Siren", CutsceneAudio.Bus.Sfx) },
        };

        FillTimeline(timeline, director, shots, timing, sources, map);

        // ---------------- save
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        EditorSceneManager.CloseScene(scene, true);
        SceneManager.SetActiveScene(mapScene);

        if (cameraNotes.Count > 0) problems = (problems.Length > 0 ? problems + ", " : "") + string.Join(", ", cameraNotes);
        return "intro built: " + shots.Count + " shots, " + timeline.GetOutputTracks().Count() + " tracks, " +
               (timeline.markerTrack != null ? timeline.markerTrack.GetMarkerCount() : 0) + " signals, " + Length + " s" +
               (problems.Length > 0 ? "; " + problems : "");
    }

    // ------------------------------------------------------------------ the map
    static Map ReadMap(out string problems)
    {
        var notes = new List<string>();
        var m = new Map();
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        m.boat = boat != null ? boat.transform.position : new Vector3(-12f, 0f, 8f);
        m.boatRot = boat != null ? boat.transform.rotation : Quaternion.identity;
        // The way the bow points is the start marker's heading (SimpleBoat's own forward is whichever axis its model uses).
        var marker = GameObject.Find(MabuhayLevelBuilder.SpawnName);
        if (marker == null) notes.Add("no " + MabuhayLevelBuilder.SpawnName + " marker: rebuild the map first");
        m.fwd = marker != null ? Flat(marker.transform.forward) : Flat(m.boatRot * Vector3.forward);
        var hall = GameObject.Find("BarangayMock")?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Hall_Barangay67"));
        var hallR = hall != null ? hall.GetComponentInChildren<Renderer>() : null;
        m.hall = hallR != null ? hallR.bounds.center : new Vector3(0f, 9f, 2f);
        m.hallRoof = Ground(m.hall + new Vector3(6f, 0f, -8f), 100f).y;
        var landing = GameObject.Find("EvaCen Landing");
        m.landing = landing != null ? landing.transform.position : m.hall + new Vector3(-2.4f, -m.hall.y - 0.25f, 6f);
        var weather = Object.FindFirstObjectByType<TyphoonWeather>();
        m.storm = weather != null ? weather.intensity : 0.7f;

        Vector3 Anchor(string key, Vector3 fallback)
        {
            var a = GameObject.Find("Anchor_" + key);
            if (a == null) { notes.Add("no Anchor_" + key); return fallback; }
            return a.transform.position;
        }
        m.tessaRoof = Anchor("tessa", new Vector3(-22f, 4.3f, -9f));
        m.nenaRoof = Anchor("nena", new Vector3(-90f, 4.1f, -31f));
        m.benPost = Anchor("ben", new Vector3(-60f, 0f, -14f));
        m.joyWater = Anchor("joy", new Vector3(-67f, 0f, -8f));

        // The Dela Cruz house is the one with Nena's roof. Its front is the street nearest it, and "to the street" is the
        // way from the roof to there.
        m.home = Street(m.nenaRoof);
        m.toStreet = Flat(m.home - m.nenaRoof);

        // Off the roof: the shortest way to open water (ground well below the roof), so the photo can be thrown clear of it.
        m.roofOut = m.toStreet; m.roofEdge = 99f;
        for (int a = 0; a < 360; a += 30)
        {
            var dir = Quaternion.Euler(0, a, 0) * m.toStreet;
            for (float r = 1f; r <= 9f; r += 0.5f)
            {
                var g = Ground(m.nenaRoof + dir * r + Vector3.up * 1f, 8f);
                if (g.y < m.nenaRoof.y - 2.5f) { if (r < m.roofEdge - 0.01f || (Mathf.Abs(r - m.roofEdge) < 0.01f && a == 0)) { m.roofEdge = r; m.roofOut = dir; } break; }
            }
        }
        if (m.roofEdge > 90f) { notes.Add("no roof edge found near Nena"); m.roofEdge = 3f; }
        m.photoLaunch = PhotoLaunch(m);

        // The dog: on Nena's roof, a little along from her.
        m.dog = m.nenaRoof + new Vector3(1.2f, 0f, 0f);
        foreach (var off in new[] { new Vector3(1.2f, 0, 0), new Vector3(-1.2f, 0, 0), new Vector3(0, 0, 1.2f), new Vector3(0, 0, -1.2f), new Vector3(0.8f, 0, 0.8f) })
        {
            var g = Ground(m.nenaRoof + off, 20f);
            if (Mathf.Abs(g.y - m.nenaRoof.y) < 0.6f) { m.dog = g; break; }
        }

        // The puddle and the drain: the street in front of the hall's south side.
        float hallSouth = hallR != null ? hallR.bounds.min.z : m.hall.z - 17f;
        m.drain = Street(new Vector3(m.hall.x + 7f, 0f, hallSouth - 4f));
        m.puddle = Street(new Vector3(m.hall.x - 6f, 0f, hallSouth - 4f));

        // A free patch of ground for the shanty and for the tricycle.
        m.shanty = FreeSpot(m.nenaRoof, new Vector3(1.6f, 1.2f, 1.6f), 6f, 20f, out bool shantyFound, m.nenaRoof + Vector3.up * 2.2f);   // one the roof can see
        if (!shantyFound) notes.Add("no free ground for the shanty (placed anyway)");
        m.tricycle = FreeSpot(m.home, new Vector3(1.3f, 0.7f, 0.9f), 4f, 14f, out bool tricycleFound);
        if (!tricycleFound) notes.Add("no free ground for the tricycle (placed anyway)");

        problems = string.Join(", ", notes);
        return m;
    }

    /// <summary>
    /// The speed the photo leaves Nena's hands with: up and out over the roof's edge, so that it clears the roof and
    /// lands on the water beyond. Found by trying steeper throws until the arc is above the roof at the edge.
    /// </summary>
    static Vector3 PhotoLaunch(Map m)
    {
        float y0 = m.nenaRoof.y + 1.2f, roof = m.nenaRoof.y, water = -0.45f;
        for (float vy = 0.5f; vy <= 6f; vy += 0.5f)
        {
            // y(t) = y0 + vy t - 4.905 t^2 = water
            float t = (vy + Mathf.Sqrt(vy * vy + 4f * 4.905f * (y0 - water))) / (2f * 4.905f);
            float vh = (m.roofEdge + 1.2f) / t;                            // lands 1.2 m past the edge
            float te = m.roofEdge / vh;
            if (y0 + vy * te - 4.905f * te * te >= roof + 0.2f) return m.roofOut * vh + Vector3.up * vy;
        }
        return m.roofOut * 3f + Vector3.up * 6f;
    }

    static bool IsWaterOrBoat(Collider c) =>
        c.GetComponentInParent<WaterSurface>() != null || c.GetComponentInParent<SimpleBoat>() != null ||
        c.GetComponentInParent<Survivor>() != null || c.GetComponentInParent<PlayerRescuer>() != null;

    /// <summary>The first solid surface under (x, z), skipping the flood and the mission's own bodies.</summary>
    static Vector3 Ground(Vector3 at, float above) => Ground(at, above, out _);

    static Vector3 Ground(Vector3 at, float above, out Collider on)
    {
        var start = new Vector3(at.x, at.y + above, at.z);
        var hits = Physics.RaycastAll(start, Vector3.down, above + 50f, ~0, QueryTriggerInteraction.Ignore)
                          .Where(h => !IsWaterOrBoat(h.collider)).OrderBy(h => h.distance).ToArray();
        on = hits.Length > 0 ? hits[0].collider : null;
        return hits.Length > 0 ? hits[0].point : new Vector3(at.x, -1f, at.z);
    }

    /// <summary>A place on the same roof as <paramref name="origin"/>: <paramref name="want"/> if the roof is there, else the anchor.</summary>
    static Vector3 OnRoof(Vector3 origin, Vector3 want)
    {
        var g = Ground(want, 6f);
        return Mathf.Abs(g.y - origin.y) < 0.7f ? g : origin;
    }

    /// <summary>Open street near (x, z): ground at street level (y about -1) with nothing over it, searched outwards.</summary>
    static Vector3 Street(Vector3 near)
    {
        for (float r = 0f; r <= 12f; r += 1.5f)
        for (int a = 0; a < 360; a += r == 0f ? 360 : 30)
        {
            var at = near + Quaternion.Euler(0, a, 0) * Vector3.forward * r;
            var g = Ground(at, 60f, out var on);
            if (g.y < -0.6f && g.y > -1.3f && on != null && on.GetComponentsInParent<Transform>(true).Any(t => t.name == "Roads")) return g;
        }
        return new Vector3(near.x, -1f, near.z);
    }

    /// <summary>Low ground (y about -1) near a point with nothing solid standing on it: room for a prop of this half-size.</summary>
    static Vector3 FreeSpot(Vector3 near, Vector3 half, float minRadius, float maxRadius, out bool found, Vector3? seenFrom = null)
    {
        for (float r = minRadius; r <= maxRadius; r += 1.5f)
        for (int a = 0; a < 360; a += 30)
        {
            var at = near + Quaternion.Euler(0, a, 0) * Vector3.forward * r;
            var g = Ground(at + Vector3.up * 3f, 60f, out var on);
            if (on == null || g.y > -0.6f || g.y < -1.3f) continue;
            var blockers = Physics.OverlapBox(g + Vector3.up * (half.y + 0.1f), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                                  .Where(c => !IsWaterOrBoat(c) && !c.GetComponentsInParent<Transform>(true).Any(t => t.name == "Roads" || t.name == "Terrain"));
            if (blockers.Any()) continue;
            if (seenFrom.HasValue && Blocked(seenFrom.Value, g + Vector3.up * (half.y + 0.3f))) continue;
            found = true;
            return g;
        }
        found = false;
        return Ground(near + Vector3.forward * minRadius + Vector3.up * 3f, 60f);
    }

    // ------------------------------------------------------------------ keeping cameras out of walls

    /// <summary>Problems the camera pass could not fix (a shot with no way to see its subject), reported by the build.</summary>
    static readonly List<string> cameraNotes = new List<string>();

    static bool InSolid(Vector3 p)
    {
        foreach (var c in Physics.OverlapSphere(p, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            if (!IsWaterOrBoat(c)) return true;
        return false;
    }

    /// <summary>Inside a wall, or with a wall between the camera and what it looks at.</summary>
    static bool Blocked(Vector3 camera, Vector3 target)
    {
        if (InSolid(camera)) return true;
        var d = target - camera;
        float dist = d.magnitude;
        if (dist < 1f) return false;
        foreach (var h in Physics.RaycastAll(camera, d / dist, dist - 0.6f, ~0, QueryTriggerInteraction.Ignore))
            if (!IsWaterOrBoat(h.collider)) return true;
        return false;
    }

    /// <summary>The camera spot, raised (up to 9 m) until it is out of the walls and sees its subject: a street-level guess in a
    /// dense barangay is often inside a house.</summary>
    static Vector3 Clear(string shot, Vector3 camera, Vector3 target)
    {
        if (!Blocked(camera, target)) return camera;
        for (float lift = 0.75f; lift <= 9f; lift += 0.75f)
        {
            var q = camera + Vector3.up * lift;
            if (!Blocked(q, target)) return q;
        }
        cameraNotes.Add(shot + " cannot see its subject");
        return camera;
    }

    /// <summary>A camera spot <paramref name="dist"/> from the target on the first of these sides that is not walled off.</summary>
    static Vector3 FreeSide(Vector3 target, float dist, float height, params Vector3[] sides)
    {
        foreach (var d in sides)
        {
            var p = target + d * dist + Vector3.up * height;
            if (!Blocked(p, target)) return p;
        }
        return target + sides[0] * dist + Vector3.up * (height + 3f);
    }

    static float GameplayFarClip()
    {
        var main = Camera.main;
        return main != null ? main.farClipPlane : 1000f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // ------------------------------------------------------------------ shots
    static List<CinemachineCamera> MakeShots(Transform parent, PlayableDirector director, Map m, Transform siren, Transform photo,
                                             Transform shanty, Transform tricycle, Vector3 nenaHome, Vector3 benRoof,
                                             out ShotMove settle, out List<(double s, double e)> timing)
    {
        var shots = new List<CinemachineCamera>();
        var times = new List<(double s, double e)>();
        timing = times;
        cameraNotes.Clear();
        var f = m.fwd;
        var right = Vector3.Cross(Vector3.up, f);
        var up = Vector3.up;
        var focus = new GameObject("Focus").transform;
        focus.SetParent(parent, false);

        void Add(string name, double s, double e, Vector3 a, Vector3 b, Transform look, float fov = 50f, bool hand = true)
        {
            // The aerials look over the roofs from far off on purpose; every other shot must see its subject.
            if (look != null && !name.Contains("Aerial") && !name.Contains("Dusk"))
            {
                a = Clear(name, a, look.position);
                b = Clear(name, b, look.position);
            }
            shots.Add(CutsceneKit.Shot(parent, name, director, s, e, a, b, look, fov, hand));
            times.Add((s, e));
        }

        var street = m.toStreet;
        var streetSide = Vector3.Cross(up, street);
        var familyMid = nenaHome + street * 0.2f + up * 1.25f;

        // ---- 1 Before the storm
        // High over the roofs towards the hall at dusk, then in on the family, then on Lola Nena and her photo.
        Add("Shot_01a_Dusk", 0.0, 3.5, m.hall + new Vector3(-34f, 14f, -64f), m.hall + new Vector3(-27f, 13f, -56f),
            CutsceneKit.Focus(focus, "F_Hall", m.hall + new Vector3(0f, 2f, 0f)), 45f, false);
        Add("Shot_01b_Family", 3.5, 7.5, nenaHome + street * 4.2f + streetSide * 0.5f + up * 1.55f, nenaHome + street * 3.0f + streetSide * 0.3f + up * 1.5f,
            CutsceneKit.Focus(focus, "F_Family", familyMid), 38f, false);
        Add("Shot_01c_NenaAndThePhoto", 7.5, 12.0, nenaHome + street * 1.9f + streetSide * 0.35f + up * 1.5f, nenaHome + street * 1.35f + streetSide * 0.2f + up * 1.45f,
            CutsceneKit.Focus(focus, "F_NenaChest", nenaHome + street * 0.2f + up * 1.3f), 32f, false);

        // ---- 2 The storm hits: rain on tin, the river, the tricycle going over, the dog
        Add("Shot_02a_RainOnRoof", 12.0, 14.5, m.tessaRoof + new Vector3(-4f, 1.4f, -3f), m.tessaRoof + new Vector3(-3.2f, 1.2f, -2.3f),
            CutsceneKit.Focus(focus, "F_TinRoof", m.tessaRoof + new Vector3(1.5f, -0.2f, 1.5f)), 40f);
        Add("Shot_02b_RiverAerial", 14.5, 17.5, OnMap(new Vector3(-30f, 60f, -170f)), OnMap(new Vector3(15f, 52f, -235f)),
            CutsceneKit.Focus(focus, "F_River", OnMap(new Vector3(70f, -1f, -410f))), 55f);
        var tricycleEye = m.tricycle + up * 0.6f;
        var triFrom = FreeSide(tricycleEye, 6.5f, 1.7f, streetSide, -streetSide, street, -street);
        Add("Shot_02c_TricycleTips", 17.5, 19.5, triFrom, Vector3.Lerp(triFrom, tricycleEye, 0.15f), tricycle, 48f);
        var dogSide = Flat(m.home - m.dog);
        Add("Shot_02d_DogOnRoof", 19.5, 22.0, m.dog + dogSide * 4.5f + up * 1.2f, m.dog + dogSide * 3.6f + up * 0.9f,
            CutsceneKit.Focus(focus, "F_Dog", m.dog + up * 0.4f), 40f);

        // ---- 3 Tragedy
        Add("Shot_03a_WaterAtTheDoor", 22.0, 24.5, nenaHome + street * 3.6f + up * 1.2f, nenaHome + street * 2.9f + up * 1.15f,
            CutsceneKit.Focus(focus, "F_Doorstep", nenaHome - street * 0.2f + up * 0.95f), 45f);
        var onWater = m.nenaRoof + m.roofOut * 8f + up * 1.6f;     // out over the street, a little above the roof, looking back at the family
        Add("Shot_03b_OnTheRoof", 24.5, 28.5, onWater, onWater - m.roofOut * 1.4f,
            CutsceneKit.Focus(focus, "F_RoofFamily", m.nenaRoof + roofSideOf(m) * 0.9f + up * 0.9f), 42f);
        Add("Shot_03c_ThePhotoSlips", 28.5, 30.5, m.nenaRoof + roofSideOf(m) * 2.8f + up * 1.9f, m.nenaRoof + roofSideOf(m) * 2.4f + up * 1.7f, photo, 40f);
        Add("Shot_03d_TheShantyGoes", 30.5, 32.5, m.nenaRoof + roofSideOf(m) * 1.4f + up * 2.2f, m.nenaRoof + roofSideOf(m) * 1.9f + up * 2.2f,
            CutsceneKit.Focus(focus, "F_Shanty", m.shanty + up * 1.0f), 50f);
        Add("Shot_03e_NenaSeesIt", 32.5, 34.0, m.nenaRoof + m.roofOut * 2.0f + roofSideOf(m) * 0.4f + up * 1.3f, m.nenaRoof + m.roofOut * 1.6f + roofSideOf(m) * 0.3f + up * 1.3f,
            CutsceneKit.Focus(focus, "F_NenaFace", m.nenaRoof + up * 1.28f), 32f);
        var towardJoy = Flat(m.joyWater - m.benPost);
        var midWater = Vector3.Lerp(m.benPost, m.joyWater, 0.5f);
        var acrossWater = Vector3.Cross(up, towardJoy);
        Add("Shot_03f_JoyInTheWater", 34.0, 36.5, midWater + acrossWater * 7f + up * 2.4f, midWater + acrossWater * 6f + up * 2.1f,
            CutsceneKit.Focus(focus, "F_Joy", midWater + up * 0.7f), 50f);
        Add("Shot_03g_NenaAlone", 36.5, 40.0, m.nenaRoof + m.roofOut * 2.8f + up * 1.3f, m.nenaRoof + m.roofOut * 1.7f + up * 1.28f,
            CutsceneKit.Focus(focus, "F_NenaFaceLit", m.nenaRoof + up * 1.28f), 30f, false);

        // ---- 4 Hope: the Captain at the radio, the crew with the gear, the light in the dark
        var toBoat = Flat(m.boat - m.landing);
        var landSide = Vector3.Cross(up, toBoat);
        Add("Shot_04a_TheCaptain", 40.0, 43.5, m.landing + toBoat * 3.6f + landSide * 0.7f + up * 1.55f, m.landing + toBoat * 2.7f + landSide * 0.5f + up * 1.5f,
            CutsceneKit.Focus(focus, "F_Captain", m.landing + up * 1.5f), 36f);
        Add("Shot_04b_TheCrew", 43.5, 46.0, m.landing + toBoat * 7.0f + landSide * 2.0f + up * 1.7f, m.landing + toBoat * 5.6f + landSide * 1.2f + up * 1.6f,
            CutsceneKit.Focus(focus, "F_Crew", m.landing + landSide * 1.0f + toBoat * 0.9f + up * 1.1f), 42f);
        var beaconAt = m.boat + up * 2.4f;
        Add("Shot_04c_ALightFarOff", 46.0, 49.0, m.nenaRoof - m.roofOut * 0.2f + up * 1.9f, m.nenaRoof - m.roofOut * 0.2f + up * 1.9f,
            CutsceneKit.Focus(focus, "F_Beacon", beaconAt), 28f);

        // ---- 5 Handover: the siren on the hall and the title, the boat goes, a swing round it, and down behind it into play
        Add("Shot_05a_HallSiren", 49.0, 50.8, siren.position + new Vector3(-5f, 1.6f, -6.5f), siren.position + new Vector3(-4f, 1.2f, -5.2f), siren, 45f);
        var boatEye = m.boat + up * 0.8f;
        var fBoat = CutsceneKit.Focus(focus, "F_Boat", boatEye);
        Add("Shot_05b_BoatLaunch", 50.8, 52.8, m.boat + f * 7f + right * 2f + up * 1.2f, m.boat + f * 5f + right * 1.5f + up * 1.0f, fBoat, 45f);
        Add("Shot_05c_SwingAround", 52.8, 53.8, m.boat + right * 6f + up * 2f, m.boat - f * 4f + right * 4f + up * 3f, fBoat, 50f);
        var settleFrom = m.boat - f * 6f + right * 2f + up * 3.5f;
        Add("Shot_05d_SettleIntoPlay", 53.8, Length, settleFrom, settleFrom, null, 60f, false);
        // How far each shot draws: close-ups need little of the map, the aerial all of it, and the last shot the
        // gameplay camera's own distance (it lands on that camera, so nothing may pop in at the hand-over).
        foreach (var cam in shots)
        {
            var lens = cam.Lens;
            lens.FarClipPlane = cam.name.Contains("RiverAerial") ? 450f
                : cam.name.Contains("SettleIntoPlay") ? GameplayFarClip()
                : cam.name.Contains("ALightFarOff") ? 220f
                : cam.name.Contains("Dusk") || cam.name.Contains("JoyInTheWater") || cam.name.Contains("BoatLaunch") || cam.name.Contains("Swing") ? 180f
                : 100f;
            cam.Lens = lens;
        }
        settle = shots[shots.Count - 1].GetComponent<ShotMove>();
        settle.fromEuler = Quaternion.LookRotation(boatEye - settleFrom).eulerAngles;
        settle.toEuler = settle.fromEuler;
        settle.toPosition = m.boat - f * 9f + up * 4f;       // replaced at runtime by the gameplay camera's own pose
        return shots;
    }

    static Vector3 roofSideOf(Map m) => Vector3.Cross(Vector3.up, m.roofOut);

    // ------------------------------------------------------------------ the Timeline
    static TimelineAsset MakeTimeline()
    {
        AssetDatabase.DeleteAsset(TimelinePath);
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, TimelinePath);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = Length;
        timeline.editorSettings.frameRate = 60;
        return timeline;
    }

    /// <summary>One spoken line: who says it, the clip's name in CutsceneAssets.introVoices, when, what is shown, and which source plays it.</summary>
    struct Line
    {
        public string speaker, clip, voice, text, english; public double at;
        public Line(double at, string speaker, string clip, string voice, string text, string english)
        { this.at = at; this.speaker = speaker; this.clip = clip; this.voice = voice; this.text = text; this.english = english; }
    }

    static readonly Line[] Lines =
    {
        new Line(5.9,  "BEN",        "Ben",        "Ben",   "Nay, tara na po. Kunin na natin ang mga gamit.",          "Ma, let's go. Let's take our things."),
        new Line(9.3,  "LOLA NENA",  "Nena1",      "Nena",  "Hindi ako aalis nang wala ang litrato natin.",            "I'm not leaving without our family photo."),
        new Line(15.0, "KAPITBAHAY", "Neighbour1", "Crowd", "Tumataas na ang tubig! Umakyat na kayo!",                  "The water is rising! Get up high!"),
        new Line(18.0, "KAPITBAHAY", "Neighbour2", "Crowd", "Sa bubong! Sa bubong!",                                    "To the roof! To the roof!"),
        new Line(25.5, "JOY",        "Joy",        "Joy",   "Tay, may darating ba?",                                    "Dad, is anyone coming?"),
        new Line(34.2, "BEN",        "BenShout",   "Ben",   "Joy! Joy!",                                                 ""),
        new Line(37.0, "LOLA NENA",  "Nena2",      "Nena",  "Diyos ko... may darating pa ba?",                          "Dear God... is anyone still coming?"),
        new Line(40.3, "KAPITAN",    "Captain",    "Ben",   "Lahat ng rescue team, handa na. Puntahan natin sila.",     "All rescue teams, get ready. Let's go to them."),
        new Line(46.3, "LOLA NENA",  "Nena3",      "Nena",  "May ilaw... may ilaw!",                                    "A light... there's a light!"),
        new Line(49.2, "RESCUER",    "Rescuer",    "Ben",   "Kapit lang po. Parating na kami.",                         "Hold on. We're on our way."),
    };

    static void FillTimeline(TimelineAsset timeline, PlayableDirector director, List<CinemachineCamera> shots,
                             List<(double s, double e)> timing, Dictionary<string, AudioSource> src, Map m)
    {
        // Cameras: one clip per shot, cut to cut.
        var cmTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        for (int i = 0; i < shots.Count; i++)
        {
            var clip = cmTrack.CreateClip<CinemachineShot>();
            clip.start = timing[i].s;
            clip.duration = timing[i].e - timing[i].s;
            clip.displayName = shots[i].name;
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, shots[i]);
        }

        // Flood level (the water's height: the ground is -1, the mission level 0): puddles, the creek overflows, the street
        // fills, the water is at the door and then over the knee, and at the end it is where the mission begins.
        var flood = timeline.CreateTrack<FloodLevelTrack>(null, "Flood Level");
        void Flood(double s, double e, float a, float b, string name)
        {
            var c = flood.CreateClip<FloodLevelClip>(); c.start = s; c.duration = e - s; c.displayName = name;
            var f = (FloodLevelClip)c.asset; f.from = a; f.to = b;
        }
        Flood(0, 12, FloodLevel.Dry, -0.95f, "Puddles");
        Flood(12, 22, -0.95f, -0.88f, "The creek overflows");
        Flood(22, 30, -0.88f, -0.45f, "The street fills");
        Flood(30, 40, -0.45f, -0.04f, "Water over the doorsteps");
        Flood(40, 49, -0.04f, 0f, "Up to the mission");
        Flood(49, Length, 0f, 0f, "Mission level");

        // Storm: dusk with light rain, the storm hits, the worst of it, the rain easing as help comes, then the mission's own.
        var storm = timeline.CreateTrack<StormTrack>(null, "Storm");
        void Storm(double s, double e, float a, float b, string name)
        {
            var c = storm.CreateClip<StormClip>(); c.start = s; c.duration = e - s; c.displayName = name;
            var st = (StormClip)c.asset; st.from = a; st.to = b;
        }
        Storm(0, 6, 0.12f, 0.3f, "Dusk, light rain");
        Storm(6, 12, 0.3f, 0.45f, "The rain sets in");
        Storm(12, 24, 0.45f, 0.88f, "The storm hits");
        Storm(24, 40, 0.88f, 0.92f, "The worst of it");
        Storm(40, 49, 0.92f, 0.7f, "The rain eases");
        Storm(49, Length, 0.7f, m.storm, "Mission storm");

        // Subtitles: the radio bulletin, then every spoken line with its translation under it.
        var a = CutsceneAssets.Current;
        var subs = timeline.CreateTrack<SubtitleTrack>(null, "Subtitles");
        void Sub(double s, double e, string speaker, string line, string english)
        {
            var c = subs.CreateClip<SubtitleClip>(); c.start = s; c.duration = e - s; c.displayName = speaker;
            var sc = (SubtitleClip)c.asset; sc.speaker = speaker; sc.line = line; sc.translation = english;
        }
        Sub(0.1, 5.8, "RADYO", "Hudyat bilang apat na po sa Cavite. Lumikas na po ang mga nakatira sa mabababang lugar.",
            "Signal No. 4 is up over Cavite. Residents of low-lying areas, please evacuate now.");
        foreach (var l in Lines)
        {
            var clip = a.IntroVoice(l.clip);
            double len = clip != null ? clip.length : 2.5;
            Sub(l.at, l.at + len + 0.3, l.speaker, l.text, l.english);
        }

        // Sound.
        CutsceneKit.Audio(timeline, director, "Radio", src["Radio"], a.radioBulletin, 0.0, a.radioBulletin != null ? a.radioBulletin.length : 5.8, 0.05, 0.2, false, 1f);
        foreach (var l in Lines)
        {
            var clip = a.IntroVoice(l.clip);
            if (clip != null) CutsceneKit.Audio(timeline, director, "VO " + l.voice, src[l.voice], clip, l.at, clip.length, 0.02, 0.06, false, 1f);
        }
        CutsceneKit.Audio(timeline, director, "Rain Light", src["RainLight"], a.rainLight, 0.5, 15.0, 1.5, 2.0, true, 0.7f);
        CutsceneKit.Audio(timeline, director, "Tin Drips", src["TinDrips"], a.tinDrips, 10.0, 14.0, 0.5, 1.5, true, 0.6f);
        CutsceneKit.Audio(timeline, director, "Rain Heavy", src["RainHeavy"], a.rainHeavy, 12.0, Length - 12.0, 2.0, 2.0, true, 0.75f);
        CutsceneKit.Audio(timeline, director, "Water Rush", src["WaterRush"], a.waterRush, 13.0, Length - 13.0, 2.5, 2.0, true, 0.6f);
        CutsceneKit.Audio(timeline, director, "Wind", src["Wind"], a.wind, 4.0, Length - 4.0, 3.0, 2.0, true, 0.45f);
        CutsceneKit.Audio(timeline, director, "Siren", src["Siren"], a.siren, 40.0, 9.0, 0.3, 1.5, true, 0.5f);
        // The music: a quiet bed from the family's last quiet minute through the tragedy, then the rising cue for the hope.
        CutsceneKit.Audio(timeline, director, "Music Bed", src["Bed"], a.musicBed, 6.0, 36.0, 4.0, 3.0, true, 0.5f);
        CutsceneKit.Audio(timeline, director, "Music Cue", src["Music"], a.musicCue, 41.0, Length - 41.0, 3.0, 1.5, false, 0.9f, a.musicCueStart);

        // Beats and cues.
        for (int i = 0; i < Beats.Length; i++) CutsceneKit.Emit(timeline, SignalFolder, "Beat_" + (i + 1), Beats[i].start + 0.01);
        void Cue(string name, double at) => CutsceneKit.Emit(timeline, SignalFolder, name, at);
        Cue("Cue_Letterbox_In", 0.0);
        // who is where
        Cue("Cast_Show_HomeAll", 0.02);
        Cue("Cast_Hide_HomeAll", 24.5); Cue("Cast_Show_RoofNena", 24.5); Cue("Cast_Show_RoofBen", 24.5); Cue("Cast_Show_RoofJoy", 24.5);
        Cue("Cast_Hide_RoofJoy", 31.5); Cue("Cast_Show_WaterJoy", 31.5); Cue("Cue_JoySplash", 31.5);
        Cue("Cast_Hide_RoofBen", 32.5);
        Cue("Cast_Show_PostBen", 34.0);
        Cue("Cast_Show_Hall", 40.0);
        Cue("Cast_Show_Beacon", 46.0); Cue("Cast_Hide_Beacon", 49.0);
        // the weather and the sounds of the storm
        Cue("Cue_PowerOut", 13.0);
        Cue("Cue_Lightning", 14.0); Cue("Cue_Thunder", 14.5);
        Cue("Cue_Whoosh", 14.5);
        Cue("Cue_TricycleTip", 17.8);
        Cue("Cue_Lightning", 18.6); Cue("Cue_Thunder", 19.2);
        Cue("Cue_DogBark", 20.0); Cue("Cue_DogBark", 21.3);
        Cue("Cue_Creak", 22.4); Cue("Cue_Creak", 26.5);
        Cue("Cue_Lightning", 23.6); Cue("Cue_Thunder", 24.2);
        // the tragedy
        Cue("Cue_PhotoDrop", 29.0);
        Cue("Cue_ShantyFall", 30.6);
        Cue("Cue_Lightning", 30.8); Cue("Cue_Thunder", 31.4);
        // hope
        Cue("Cue_Siren_On", 40.0);
        Cue("Cue_Debris", 41.0);
        Cue("Cue_Siren_Off", 49.0);
        Cue("Cue_TitleHit", 50.2);
        Cue("Cue_Splash", 51.0);
        Cue("Cue_Whoosh", 51.2);
        EditorUtility.SetDirty(timeline);
    }

    static VolumeProfile LookProfile()
    {
        AssetDatabase.DeleteAsset(LookPath);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, LookPath);
        T Add<T>() where T : VolumeComponent { var c = p.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); return c; }
        var dof = Add<DepthOfField>();
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(10f);
        dof.focalLength.Override(55f);
        dof.aperture.Override(4f);
        var grain = Add<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(0.35f);
        var ca = Add<ChromaticAberration>();
        ca.intensity.Override(0f);
        var vig = Add<Vignette>();
        vig.intensity.Override(0.32f);
        vig.smoothness.Override(0.45f);
        var grade = Add<ColorAdjustments>();
        grade.contrast.Override(6f);
        grade.saturation.Override(-6f);
        grade.postExposure.Override(0.35f);
        EditorUtility.SetDirty(p);
        return p;
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
