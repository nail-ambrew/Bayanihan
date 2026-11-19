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
/// Bayanihan > Cutscenes > Build Intro: makes Scene_IntroCutscene and its Timeline (Cut_Intro), the flood story in seven
/// beats over the real Mabuhay map. The shots are placed from the map itself: it is opened, the boat's start, the hall,
/// the residents' roofs and the streets are read (raycasts find roofs, walls and ground), then the intro scene is
/// written beside it. The MabuhayGameplay scene is only read, never changed.
///
/// To retime, change <see cref="Beats"/> (or a shot's times) and run it again. Everything it makes is listed in
/// Assets/Cutscenes/REPORT.md.
/// </summary>
public static class IntroCutsceneBuilder
{
    public const string Folder = CutsceneKit.Root + "/Intro";
    public const string ScenePath = Folder + "/Scene_IntroCutscene.unity";
    public const string TimelinePath = Folder + "/Timelines/Cut_Intro.playable";
    public const string SignalFolder = Folder + "/Signals";
    public const string LookPath = Folder + "/Cut_Intro_Look.asset";
    const string MapScene = "Assets/Scenes/Maps/MabuhayGameplay.unity";

    /// <summary>The seven beats (seconds): retime here.</summary>
    public static readonly (string name, double start, double end)[] Beats =
    {
        ("Cut_Intro_01_Calm", 0.0, 2.0),
        ("Cut_Intro_02_RainStarts", 2.0, 5.0),
        ("Cut_Intro_03_RiverOverflows", 5.0, 9.0),
        ("Cut_Intro_04_DrainsClog", 9.0, 12.0),
        ("Cut_Intro_05_StreetsRise", 12.0, 16.0),
        ("Cut_Intro_06_DebrisFloats", 16.0, 20.0),
        ("Cut_Intro_07_BayanihanResponse", 20.0, 24.0),
    };
    public const double Length = 24.0;

    // What the builder read from the map.
    struct Map
    {
        public Vector3 boat; public Quaternion boatRot;
        public Vector3 hall; public float hallRoof;
        public Vector3 tessaRoof, nenaRoof;
        public float storm;
        public Vector3 drain; public Vector3 childSpot; public Vector3 childFacing;
        public Vector3 puddle, doorstep, doorstepLook;
        public Vector3 dog;
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
        dogGo.transform.rotation = Quaternion.LookRotation(Flat(map.boat - map.dog));
        var siren = CutsceneKit.Siren(props, new Vector3(map.hall.x + 6f, map.hallRoof, map.hall.z - 8f));
        var child = new GameObject("ChildSpot").transform;
        child.SetParent(props, false);
        child.position = map.childSpot;
        child.rotation = Quaternion.LookRotation(map.childFacing);

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
            CutsceneKit.Particles("FX_Mist_Start", fx, map.boat + new Vector3(0, 1.5f, 0), soft, new Color(0.75f, 0.78f, 0.8f, 0.16f), 6,
                                  new Vector2(6f, 9f), new Vector2(0.2f, 0.6f), new Vector2(8f, 14f), new Vector3(60f, 2f, 50f), 0f, 60),
        };
        var fwd = map.boatRot * Vector3.forward;
        var splash = CutsceneKit.Particles("FX_BoatLaunchSplash", fx, map.boat + fwd * 2f + Vector3.up * 0.2f, soft,
                                           new Color(0.85f, 0.85f, 0.8f, 0.9f), 120, new Vector2(0.5f, 1.1f), new Vector2(3f, 6f),
                                           new Vector2(0.1f, 0.35f), new Vector3(1.6f, 0.1f, 0.6f), 1.2f, 160, loop: false);
        var sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f;
        splash.transform.rotation = Quaternion.LookRotation(Vector3.up);
        effects.Add(splash);
        intro.effects = effects.ToArray();
        intro.splash = splash;

        // debris pool: let go upstream of the debris shot, carried towards the camera
        var poolGo = new GameObject("DebrisPool");
        poolGo.transform.SetParent(root, false);
        var debrisAt = map.boat + new Vector3(10f, 0f, 3f);
        poolGo.transform.position = new Vector3(debrisAt.x, 0f, debrisAt.z);
        var pool = poolGo.AddComponent<IntroDebrisPool>();
        pool.extraKinds = rubbish;
        pool.area = new Vector2(10f, 8f);
        pool.flow = new Vector3(1f, 0f, 0.15f);
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
        var shots = MakeShots(shotsRoot, director, map, dogGo.transform, siren.transform, child, out var settle, out var timing);
        intro.shots = shots.ToArray();
        intro.settle = settle;
        intro.siren = siren;
        intro.childSpot = child;

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
            { "Music", CutsceneKit.Source(audioRoot, "Music_Cue", CutsceneAudio.Bus.Music) },
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
        m.boat = boat != null ? boat.transform.position : new Vector3(-160f, 0f, -37f);
        m.boatRot = boat != null ? boat.transform.rotation : Quaternion.identity;
        var hall = GameObject.Find("BarangayMock")?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Hall_Barangay67"));
        var hallR = hall != null ? hall.GetComponentInChildren<Renderer>() : null;
        m.hall = hallR != null ? hallR.bounds.center : new Vector3(0f, 9f, 2f);
        m.hallRoof = Ground(m.hall + new Vector3(6f, 0f, -8f), 100f).y;
        var weather = Object.FindFirstObjectByType<TyphoonWeather>();
        m.storm = weather != null ? weather.intensity : 0.7f;

        Vector3 Anchor(string key, Vector3 fallback)
        {
            var a = GameObject.Find("Anchor_" + key);
            if (a == null) { notes.Add("no Anchor_" + key); return fallback; }
            return a.transform.position;
        }
        m.tessaRoof = Anchor("tessa", new Vector3(-67f, 5f, -43f));
        m.nenaRoof = Anchor("nena", new Vector3(-171f, 4f, -66f));

        // The dog: on Nena's roof, a little along from her.
        m.dog = m.nenaRoof + new Vector3(1.2f, 0f, 0f);
        foreach (var off in new[] { new Vector3(1.2f, 0, 0), new Vector3(-1.2f, 0, 0), new Vector3(0, 0, 1.2f), new Vector3(0, 0, -1.2f), new Vector3(0.8f, 0, 0.8f) })
        {
            var g = Ground(m.nenaRoof + off, 20f);
            if (Mathf.Abs(g.y - m.nenaRoof.y) < 0.6f) { m.dog = g; break; }
        }

        // The puddle and the doorstep: the street in front of the hall's south side. The drain is on the same street.
        float hallSouth = hallR != null ? hallR.bounds.min.z : m.hall.z - 17f;
        m.drain = Street(new Vector3(m.hall.x + 7f, 0f, hallSouth - 4f));
        m.puddle = Street(new Vector3(m.hall.x - 6f, 0f, hallSouth - 4f));
        m.doorstep = Street(new Vector3(m.hall.x - 9f, 0f, hallSouth - 5f));
        m.doorstepLook = new Vector3(m.hall.x - 3f, m.doorstep.y + 0.3f, hallSouth);

        // The child: at the front of the first house south of the hall, facing the street.
        var from = new Vector3(m.hall.x, m.puddle.y + 1.2f, hallSouth - 3f);
        if (Physics.Raycast(from, Vector3.back, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) && !IsWaterOrBoat(hit.collider))
        {
            m.childFacing = Flat(hit.normal);
            m.childSpot = Ground(hit.point + m.childFacing * 0.45f, 10f);
        }
        else
        {
            notes.Add("no house found south of the hall for the child");
            m.childFacing = Vector3.forward;
            m.childSpot = Ground(new Vector3(m.hall.x, 0f, hallSouth - 25f), 30f);
        }
        problems = string.Join(", ", notes);
        return m;
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

    static float GameplayFarClip()
    {
        var main = Camera.main;
        return main != null ? main.farClipPlane : 1000f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // ------------------------------------------------------------------ shots
    static List<CinemachineCamera> MakeShots(Transform parent, PlayableDirector director, Map m, Transform dog, Transform siren,
                                             Transform child, out ShotMove settle, out List<(double s, double e)> timing)
    {
        var shots = new List<CinemachineCamera>();
        var times = new List<(double s, double e)>();
        timing = times;
        var f = m.boatRot * Vector3.forward;
        var right = m.boatRot * Vector3.right;
        var up = Vector3.up;
        var focus = new GameObject("Focus").transform;
        focus.SetParent(parent, false);

        void Add(string name, double s, double e, Vector3 a, Vector3 b, Transform look, float fov = 50f, bool hand = true)
        {
            shots.Add(CutsceneKit.Shot(parent, name, director, s, e, a, b, look, fov, hand));
            times.Add((s, e));
        }

        // 1 Calm: high over the roofs towards the hall, overcast and still.
        Add("Shot_01_Calm", 0.0, 2.0, m.hall + new Vector3(-34f, 14f, -64f), m.hall + new Vector3(-27f, 13f, -56f),
            CutsceneKit.Focus(focus, "F_Hall", m.hall + new Vector3(0f, 2f, 0f)), 45f, false);
        // 2 Rain starts: a tin roof as the rain comes on, then the street, low over a puddle.
        Add("Shot_02a_RainOnRoof", 2.0, 3.6, m.tessaRoof + new Vector3(-4f, 1.4f, -3f), m.tessaRoof + new Vector3(-3.2f, 1.2f, -2.3f),
            CutsceneKit.Focus(focus, "F_TinRoof", m.tessaRoof + new Vector3(1.5f, -0.2f, 1.5f)), 40f);
        Add("Shot_02b_Puddle", 3.6, 5.0, m.puddle + new Vector3(-3f, 0.9f, -2f), m.puddle + new Vector3(-2.4f, 0.8f, -1.6f),
            CutsceneKit.Focus(focus, "F_Puddle", m.puddle + new Vector3(2f, 0f, 1.5f)), 35f);
        // 3 River overflows: high and moving, out towards the river south of the barangay.
        Add("Shot_03_RiverAerial", 5.0, 9.0, OnMap(new Vector3(-30f, 60f, -170f)), OnMap(new Vector3(15f, 52f, -235f)),
            CutsceneKit.Focus(focus, "F_River", OnMap(new Vector3(70f, -1f, -410f))), 55f, false);
        // 4 Drains clog: down at the grate.
        Add("Shot_04_DrainClose", 9.0, 12.0, m.drain + new Vector3(-2.0f, 1.5f, -1.8f), m.drain + new Vector3(-1.4f, 1.1f, -1.2f),
            CutsceneKit.Focus(focus, "F_Drain", m.drain + new Vector3(0f, 0.05f, 0f)), 40f);
        // 5 Streets rise: a locked-off time-lapse at a doorstep, the child at the house front, the dog on the roof.
        Add("Shot_05a_DoorstepTimelapse", 12.0, 13.8, m.doorstep + new Vector3(0f, 0.9f, -1f), m.doorstep + new Vector3(0.2f, 0.95f, -0.8f),
            CutsceneKit.Focus(focus, "F_Doorstep", m.doorstepLook), 45f, false);
        var childEye = m.childSpot + up * 0.9f;
        Add("Shot_05b_Child", 13.8, 15.0, childEye + m.childFacing * 3.6f + up * 0.3f, childEye + m.childFacing * 2.7f + up * 0.2f,
            CutsceneKit.Focus(focus, "F_Child", childEye), 40f);
        var dogSide = Flat(m.boat - m.dog);
        Add("Shot_05c_DogOnRoof", 15.0, 16.0, m.dog + dogSide * 4.5f + Vector3.up * 1.2f, m.dog + dogSide * 3.6f + Vector3.up * 0.9f,
            CutsceneKit.Focus(focus, "F_Dog", m.dog + up * 0.4f), 40f);
        // 6 Debris floats: low on the water, the wreckage coming at the camera.
        var debrisCam = m.boat + new Vector3(24f, 0.9f, 7f);
        Add("Shot_06_DebrisTracking", 16.0, 20.0, debrisCam, debrisCam + new Vector3(-3f, 0f, -1f),
            CutsceneKit.Focus(focus, "F_Debris", m.boat + new Vector3(12f, 0.2f, 3.5f)), 50f);
        // 7 Bayanihan response: the siren on the hall, the boat goes, a swing round it, and down behind it into play.
        Add("Shot_07a_HallSiren", 20.0, 21.0, siren.position + new Vector3(-5f, 1.6f, -6.5f), siren.position + new Vector3(-4f, 1.2f, -5.2f),
            siren, 45f);
        var boatEye = m.boat + up * 0.8f;
        var fBoat = CutsceneKit.Focus(focus, "F_Boat", boatEye);
        Add("Shot_07b_BoatLaunch", 21.0, 22.0, m.boat + f * 7f + right * 2f + up * 1.2f, m.boat + f * 5f + right * 1.5f + up * 1.0f, fBoat, 45f);
        Add("Shot_07c_SwingAround", 22.0, 22.8, m.boat + right * 6f + up * 2f, m.boat - f * 4f + right * 4f + up * 3f, fBoat, 50f);
        var settleFrom = m.boat - f * 6f + right * 2f + up * 3.5f;
        Add("Shot_07d_SettleIntoPlay", 22.8, Length, settleFrom, settleFrom, null, 60f, false);
        // How far each shot draws: close-ups need little of the map, the aerial all of it, and the last shot the
        // gameplay camera's own distance (it lands on that camera, so nothing may pop in at the hand-over).
        foreach (var cam in shots)
        {
            var lens = cam.Lens;
            lens.FarClipPlane = cam.name.Contains("RiverAerial") ? 450f
                : cam.name.Contains("SettleIntoPlay") ? GameplayFarClip()
                : cam.name.Contains("Calm") || cam.name.Contains("Debris") || cam.name.Contains("BoatLaunch") || cam.name.Contains("Swing") ? 180f
                : 90f;
            cam.Lens = lens;
        }
        settle = shots[shots.Count - 1].GetComponent<ShotMove>();
        settle.fromEuler = Quaternion.LookRotation(boatEye - settleFrom).eulerAngles;
        settle.toEuler = settle.fromEuler;
        settle.toPosition = m.boat - f * 9f + up * 4f;       // replaced at runtime by the gameplay camera's own pose
        return shots;
    }

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

        // Flood level: puddles, the river comes up, the drains back up, the streets fill, play level.
        var flood = timeline.CreateTrack<FloodLevelTrack>(null, "Flood Level");
        void Flood(double s, double e, float a, float b, string name)
        {
            var c = flood.CreateClip<FloodLevelClip>(); c.start = s; c.duration = e - s; c.displayName = name;
            var f = (FloodLevelClip)c.asset; f.from = a; f.to = b;
        }
        Flood(0, 5, FloodLevel.Dry, -0.95f, "Puddles");
        Flood(5, 9, -0.95f, -0.93f, "River overflows (far off)");
        Flood(9, 12, -0.93f, -0.86f, "Drains back up");
        Flood(12, 16, -0.86f, -0.12f, "Streets rise");
        Flood(16, 20, -0.12f, 0f, "Up to the mission");
        Flood(20, Length, 0f, 0f, "Mission level");

        // Storm: overcast, the rain arrives, the worst of it, then the mission's own storm for the hand-over.
        var storm = timeline.CreateTrack<StormTrack>(null, "Storm");
        void Storm(double s, double e, float a, float b, string name)
        {
            var c = storm.CreateClip<StormClip>(); c.start = s; c.duration = e - s; c.displayName = name;
            var st = (StormClip)c.asset; st.from = a; st.to = b;
        }
        Storm(0, 2, 0.1f, 0.14f, "Overcast");
        Storm(2, 5, 0.14f, 0.8f, "Rain arrives");
        Storm(5, 20, 0.8f, 0.88f, "Typhoon");
        Storm(20, Length, 0.88f, m.storm, "Mission storm");

        // Subtitles for the radio.
        var subs = timeline.CreateTrack<SubtitleTrack>(null, "Subtitles");
        void Sub(double s, double e, string line)
        {
            var c = subs.CreateClip<SubtitleClip>(); c.start = s; c.duration = e - s; c.displayName = "Subtitle";
            ((SubtitleClip)c.asset).line = line;
        }
        Sub(0.25, 3.3, "RADIO: Weather bulletin. Signal number three is raised over Cavite.");
        Sub(3.3, 6.6, "RADIO: Residents near the river, prepare to evacuate now.");

        // Sound.
        var a = CutsceneAssets.Current;
        CutsceneKit.Audio(timeline, director, "Radio", src["Radio"], a.radioBulletin, 0.2, a.radioBulletin != null ? a.radioBulletin.length : 6.5, 0.05, 0.2, false, 1f);
        CutsceneKit.Audio(timeline, director, "Rain Light", src["RainLight"], a.rainLight, 1.6, 4.4, 1.2, 1.5, true, 0.8f);
        CutsceneKit.Audio(timeline, director, "Tin Drips", src["TinDrips"], a.tinDrips, 2.0, 4.0, 0.3, 1.2, true, 0.7f);
        CutsceneKit.Audio(timeline, director, "Rain Heavy", src["RainHeavy"], a.rainHeavy, 4.5, Length - 4.5, 1.5, 2.0, true, 0.75f);
        CutsceneKit.Audio(timeline, director, "Water Rush", src["WaterRush"], a.waterRush, 5.0, Length - 5.0, 2.0, 2.0, true, 0.6f);
        CutsceneKit.Audio(timeline, director, "Wind", src["Wind"], a.wind, 1.0, Length - 1.0, 2.0, 2.0, true, 0.45f);
        CutsceneKit.Audio(timeline, director, "Siren", src["Siren"], a.siren, 20.0, 4.0, 0.3, 1.5, true, 0.6f);
        CutsceneKit.Audio(timeline, director, "Music Cue", src["Music"], a.musicCue, 12.0, Length - 12.0, 3.0, 1.5, false, 0.9f, a.musicCueStart);

        // Beats and cues.
        for (int i = 0; i < Beats.Length; i++) CutsceneKit.Emit(timeline, SignalFolder, "Beat_" + (i + 1), Beats[i].start + 0.01);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Letterbox_In", 0.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Lightning", 3.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Thunder", 3.4);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Lightning", 7.2);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Thunder", 7.9);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Whoosh", 9.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Lightning", 12.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Thunder", 12.5);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Debris", 15.2);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Siren_On", 20.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Splash", 21.0);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Whoosh", 21.5);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_TitleHit", 21.6);
        CutsceneKit.Emit(timeline, SignalFolder, "Cue_Siren_Off", 23.8);
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
