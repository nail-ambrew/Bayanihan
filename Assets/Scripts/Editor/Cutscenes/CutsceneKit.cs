using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

/// <summary>
/// What the cutscene builders share: placeholder models built from simple shapes (saved as prefabs, listed in
/// Assets/Cutscenes/REPORT.md for replacement), particle effects, materials, shot cameras and Timeline helpers.
/// </summary>
public static class CutsceneKit
{
    public const string Root = "Assets/Cutscenes";
    public const string SharedMaterials = Root + "/Shared/Materials";
    public const string NoisePreset = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ materials
    public static Material Lit(string name, Color color, float smoothness = 0.2f, Color? emission = null)
    {
        EnsureFolder(SharedMaterials);
        string path = SharedMaterials + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A soft round dot for particles (generated once, saved as a texture).</summary>
    public static Texture2D SoftDot()
    {
        string path = SharedMaterials + "/Tex_SoftDot.png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null) return tex;
        EnsureFolder(SharedMaterials);
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            float a = Mathf.Clamp01(1f - d);
            t.SetPixel(x, y, new Color(1, 1, 1, a * a));
        }
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static Material ParticleMaterial(string name, bool additive = false)
    {
        EnsureFolder(SharedMaterials);
        string path = SharedMaterials + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_BaseMap", SoftDot());
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ placeholder models
    /// <summary>A part of a placeholder model: a primitive, no collider, under <paramref name="parent"/>.</summary>
    public static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    public static GameObject SavePrefab(GameObject go, string folder)
    {
        EnsureFolder(folder);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + "/" + go.name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>A sitting dog, about 0.6 m tall: body, head, snout, ears, legs, tail.</summary>
    public static GameObject Dog(string folder)
    {
        var fur = Lit("Mat_DogFur", new Color(0.62f, 0.45f, 0.28f));
        var dark = Lit("Mat_DogDark", new Color(0.18f, 0.12f, 0.08f));
        var root = new GameObject("Prop_Dog").transform;
        Part(root, PrimitiveType.Capsule, new Vector3(0, 0.32f, -0.05f), new Vector3(0.28f, 0.26f, 0.28f), fur, new Vector3(-35, 0, 0));
        Part(root, PrimitiveType.Sphere, new Vector3(0, 0.55f, 0.12f), new Vector3(0.22f, 0.2f, 0.22f), fur);
        Part(root, PrimitiveType.Cube, new Vector3(0, 0.52f, 0.25f), new Vector3(0.1f, 0.08f, 0.12f), fur);
        Part(root, PrimitiveType.Sphere, new Vector3(0, 0.54f, 0.31f), new Vector3(0.05f, 0.04f, 0.04f), dark);
        Part(root, PrimitiveType.Cube, new Vector3(-0.08f, 0.66f, 0.1f), new Vector3(0.05f, 0.1f, 0.03f), dark, new Vector3(0, 0, 20));
        Part(root, PrimitiveType.Cube, new Vector3(0.08f, 0.66f, 0.1f), new Vector3(0.05f, 0.1f, 0.03f), dark, new Vector3(0, 0, -20));
        Part(root, PrimitiveType.Cylinder, new Vector3(-0.07f, 0.12f, 0.1f), new Vector3(0.05f, 0.12f, 0.05f), fur);
        Part(root, PrimitiveType.Cylinder, new Vector3(0.07f, 0.12f, 0.1f), new Vector3(0.05f, 0.12f, 0.05f), fur);
        Part(root, PrimitiveType.Capsule, new Vector3(-0.1f, 0.1f, -0.12f), new Vector3(0.1f, 0.1f, 0.18f), fur, new Vector3(90, 0, 0));
        Part(root, PrimitiveType.Capsule, new Vector3(0.1f, 0.1f, -0.12f), new Vector3(0.1f, 0.1f, 0.18f), fur, new Vector3(90, 0, 0));
        Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.2f, -0.28f), new Vector3(0.03f, 0.12f, 0.03f), fur, new Vector3(-50, 0, 0));
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>The household rubbish the intro's flood carries (six kinds), each a placeholder prefab.</summary>
    public static GameObject[] Rubbish(string folder)
    {
        var bottle = Lit("Mat_PlasticClear", new Color(0.55f, 0.75f, 0.7f, 1f), 0.8f);
        var cap = Lit("Mat_PlasticBlue", new Color(0.15f, 0.35f, 0.8f), 0.5f);
        var bag = Lit("Mat_TrashBag", new Color(0.07f, 0.07f, 0.08f), 0.6f);
        var card = Lit("Mat_Cardboard", new Color(0.62f, 0.47f, 0.3f));
        var chair = Lit("Mat_PlasticChair", new Color(0.85f, 0.2f, 0.15f), 0.4f);
        var bin = Lit("Mat_BinGreen", new Color(0.2f, 0.45f, 0.25f), 0.3f);
        var frond = Lit("Mat_Frond", new Color(0.42f, 0.5f, 0.2f));

        var list = new System.Collections.Generic.List<GameObject>();
        var t = new GameObject("Rubbish_Bottle").transform;
        Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.08f, 0.12f, 0.08f), bottle, new Vector3(90, 0, 0));
        Part(t, PrimitiveType.Cylinder, new Vector3(0, 0, 0.14f), new Vector3(0.035f, 0.02f, 0.035f), cap, new Vector3(90, 0, 0));
        list.Add(SavePrefab(t.gameObject, folder));

        t = new GameObject("Rubbish_TrashBag").transform;
        Part(t, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.5f, 0.4f, 0.45f), bag);
        Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.22f, 0), new Vector3(0.08f, 0.06f, 0.08f), bag);
        list.Add(SavePrefab(t.gameObject, folder));

        t = new GameObject("Rubbish_Cardboard").transform;
        Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.6f, 0.04f, 0.45f), card, new Vector3(0, 0, 4));
        list.Add(SavePrefab(t.gameObject, folder));

        t = new GameObject("Rubbish_PlasticChair").transform;
        Part(t, PrimitiveType.Cube, new Vector3(0, 0.22f, 0), new Vector3(0.45f, 0.04f, 0.42f), chair);
        Part(t, PrimitiveType.Cube, new Vector3(0, 0.46f, -0.2f), new Vector3(0.45f, 0.45f, 0.04f), chair, new Vector3(-8, 0, 0));
        foreach (var x in new[] { -0.19f, 0.19f })
        foreach (var z in new[] { -0.18f, 0.18f })
            Part(t, PrimitiveType.Cube, new Vector3(x, 0.1f, z), new Vector3(0.04f, 0.22f, 0.04f), chair);
        list.Add(SavePrefab(t.gameObject, folder));

        t = new GameObject("Rubbish_Bin").transform;
        Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.45f, 0.3f, 0.45f), bin, new Vector3(0, 0, 80));
        list.Add(SavePrefab(t.gameObject, folder));

        t = new GameObject("Rubbish_CoconutFrond").transform;
        Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.04f, 1.1f, 0.04f), frond, new Vector3(90, 0, 0));
        for (int i = -4; i <= 4; i++)
        {
            if (i == 0) continue;
            Part(t, PrimitiveType.Cube, new Vector3(Mathf.Sign(i) * 0.28f, 0, i * 0.2f), new Vector3(0.55f, 0.01f, 0.07f), frond,
                 new Vector3(0, Mathf.Sign(i) * 25f, 0));
        }
        list.Add(SavePrefab(t.gameObject, folder));
        return list.ToArray();
    }

    /// <summary>A kerbside storm drain: a concrete surround and an iron grate of bars, 0.9 x 0.5 m.</summary>
    public static GameObject StormDrain(string folder)
    {
        var concrete = Lit("Mat_DrainConcrete", new Color(0.55f, 0.55f, 0.52f));
        var iron = Lit("Mat_DrainIron", new Color(0.12f, 0.12f, 0.13f), 0.45f);
        var root = new GameObject("Prop_StormDrain").transform;
        Part(root, PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(1.1f, 0.04f, 0.7f), concrete);
        Part(root, PrimitiveType.Cube, new Vector3(0, 0.03f, 0), new Vector3(0.9f, 0.03f, 0.5f), iron);
        for (int i = -4; i <= 4; i++)
            Part(root, PrimitiveType.Cube, new Vector3(i * 0.1f, 0.05f, 0), new Vector3(0.035f, 0.02f, 0.5f), concrete);
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>The family photo Lola Nena will not leave without: a wooden frame round a faded print, 22 x 16 cm. A placeholder.</summary>
    public static GameObject FamilyPhoto(string folder)
    {
        var wood = Lit("Mat_FrameWood", new Color(0.36f, 0.22f, 0.12f), 0.3f);
        var print = Lit("Mat_PhotoPrint", new Color(0.78f, 0.7f, 0.58f), 0.1f);
        var dark = Lit("Mat_PhotoFigures", new Color(0.25f, 0.2f, 0.17f), 0.1f);
        var root = new GameObject("Prop_FamilyPhoto").transform;
        Part(root, PrimitiveType.Cube, Vector3.zero, new Vector3(0.22f, 0.16f, 0.02f), wood);
        Part(root, PrimitiveType.Cube, new Vector3(0, 0, -0.011f), new Vector3(0.19f, 0.13f, 0.004f), print);
        foreach (float x in new[] { -0.05f, 0f, 0.05f })      // four small figures in a row: a family
            Part(root, PrimitiveType.Capsule, new Vector3(x, -0.01f, -0.014f), new Vector3(0.025f, 0.03f, 0.004f), dark);
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>A neighbour's shanty about 3 x 3 m and 2.4 m tall: plywood walls, a pitched tin roof, a door. A placeholder.</summary>
    public static GameObject Shanty(string folder)
    {
        var ply = Lit("Mat_Plywood", new Color(0.55f, 0.42f, 0.28f));
        var tin = Lit("Mat_TinRoof", new Color(0.5f, 0.52f, 0.54f), 0.55f);
        var door = Lit("Mat_ShantyDoor", new Color(0.28f, 0.4f, 0.55f));
        var root = new GameObject("Prop_Shanty").transform;
        Part(root, PrimitiveType.Cube, new Vector3(0, 1.1f, 0), new Vector3(3f, 2.2f, 3f), ply);
        Part(root, PrimitiveType.Cube, new Vector3(-0.85f, 2.45f, 0), new Vector3(1.9f, 0.06f, 3.4f), tin, new Vector3(0, 0, 22));
        Part(root, PrimitiveType.Cube, new Vector3(0.85f, 2.45f, 0), new Vector3(1.9f, 0.06f, 3.4f), tin, new Vector3(0, 0, -22));
        Part(root, PrimitiveType.Cube, new Vector3(0.4f, 0.95f, -1.51f), new Vector3(0.8f, 1.9f, 0.04f), door);
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>A Filipino tricycle: a motorbike with a sidecar cab, 2.4 m long. A placeholder.</summary>
    public static GameObject Tricycle(string folder)
    {
        var body = Lit("Mat_TricycleBody", new Color(0.8f, 0.18f, 0.12f), 0.5f);
        var black = Lit("Mat_TricycleBlack", new Color(0.06f, 0.06f, 0.07f), 0.3f);
        var steel = Lit("Mat_TricycleSteel", new Color(0.55f, 0.55f, 0.58f), 0.6f);
        var root = new GameObject("Prop_Tricycle").transform;
        Part(root, PrimitiveType.Cube, new Vector3(0.55f, 0.55f, -0.1f), new Vector3(0.95f, 0.7f, 1.3f), body);          // sidecar cab
        Part(root, PrimitiveType.Cube, new Vector3(0.55f, 1.0f, -0.1f), new Vector3(1.05f, 0.05f, 1.45f), body);         // its roof
        Part(root, PrimitiveType.Cube, new Vector3(-0.35f, 0.62f, -0.2f), new Vector3(0.28f, 0.28f, 0.9f), steel);       // the bike's frame
        Part(root, PrimitiveType.Cube, new Vector3(-0.35f, 0.9f, 0.3f), new Vector3(0.5f, 0.05f, 0.05f), black);         // handlebar
        foreach (var (x, z) in new[] { (-0.35f, 0.7f), (-0.35f, -0.9f), (0.55f, -0.55f) })
            Part(root, PrimitiveType.Cylinder, new Vector3(x, 0.3f, z), new Vector3(0.6f, 0.05f, 0.6f), black, new Vector3(0, 0, 90));
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>What the crew loads: three orange life vests on a crate, a first-aid box and a coil of rope. A placeholder.</summary>
    public static GameObject RescueGear(string folder)
    {
        var orange = Lit("Mat_VestOrange", new Color(1f, 0.42f, 0.05f), 0.3f);
        var crate = Lit("Mat_GearCrate", new Color(0.3f, 0.33f, 0.28f));
        var white = Lit("Mat_FirstAidWhite", new Color(0.92f, 0.92f, 0.9f));
        var red = Lit("Mat_FirstAidRed", new Color(0.8f, 0.1f, 0.1f));
        var rope = Lit("Mat_RopeTan", new Color(0.7f, 0.6f, 0.4f));
        var root = new GameObject("Prop_RescueGear").transform;
        Part(root, PrimitiveType.Cube, new Vector3(0, 0.18f, 0), new Vector3(0.9f, 0.36f, 0.6f), crate);
        for (int i = 0; i < 3; i++)
            Part(root, PrimitiveType.Cube, new Vector3(-0.28f + i * 0.28f, 0.42f, 0), new Vector3(0.24f, 0.1f, 0.4f), orange, new Vector3(0, i * 8f, 0));
        Part(root, PrimitiveType.Cube, new Vector3(0.75f, 0.12f, 0.1f), new Vector3(0.35f, 0.24f, 0.25f), white);
        Part(root, PrimitiveType.Cube, new Vector3(0.75f, 0.245f, 0.1f), new Vector3(0.14f, 0.01f, 0.04f), red);
        Part(root, PrimitiveType.Cube, new Vector3(0.75f, 0.245f, 0.1f), new Vector3(0.04f, 0.01f, 0.14f), red);
        Part(root, PrimitiveType.Cylinder, new Vector3(-0.85f, 0.08f, 0.1f), new Vector3(0.4f, 0.08f, 0.4f), rope);
        return SavePrefab(root.gameObject, folder);
    }

    /// <summary>A roof-mounted emergency lamp with a turning beam.</summary>
    public static SirenLight Siren(Transform parent, Vector3 at)
    {
        var red = Lit("Mat_SirenRed", new Color(0.9f, 0.1f, 0.05f), 0.7f, new Color(1f, 0.1f, 0.05f) * 2f);
        var grey = Lit("Mat_SirenBase", new Color(0.3f, 0.3f, 0.32f), 0.4f);
        var root = new GameObject("Prop_HallSiren").transform;
        root.SetParent(parent, false);
        root.position = at;
        root.localScale = Vector3.one * 2.5f;
        Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.5f, 0.1f, 0.5f), grey);
        var lamp = Part(root, PrimitiveType.Sphere, new Vector3(0, 0.35f, 0), new Vector3(0.38f, 0.32f, 0.38f), red);
        var turn = new GameObject("Beam").transform;
        turn.SetParent(root, false);
        turn.localPosition = new Vector3(0, 0.35f, 0);
        var lightGo = new GameObject("Spot");
        lightGo.transform.SetParent(turn, false);
        lightGo.transform.localEulerAngles = new Vector3(15, 0, 0);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Spot;
        l.color = new Color(1f, 0.15f, 0.08f);
        l.intensity = 40f;
        l.range = 35f;
        l.spotAngle = 40f;
        l.shadows = LightShadows.None;
        l.enabled = false;
        var s = turn.gameObject.AddComponent<SirenLight>();
        s.beam = l;
        s.lamp = lamp.GetComponent<Renderer>();
        return s;
    }

    // ------------------------------------------------------------------ particles
    public static ParticleSystem Particles(string name, Transform parent, Vector3 at, Material mat, Color color, float rate,
                                           Vector2 life, Vector2 speed, Vector2 size, Vector3 boxSize, float gravity = 0f,
                                           int max = 600, bool loop = true, bool world = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = loop;
        main.playOnAwake = loop;
        main.duration = loop ? 5f : 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.maxParticles = max;
        main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        var em = ps.emission;
        em.rateOverTime = loop ? rate : 0f;
        if (!loop) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)rate) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxSize;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    // ------------------------------------------------------------------ cameras and Timeline
    public static CinemachineCamera Shot(Transform parent, string name, PlayableDirector director, double start, double end,
                                         Vector3 from, Vector3 to, Transform look, float fov = 50f, bool handheld = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var cam = go.AddComponent<CinemachineCamera>();
        cam.Priority = -10;                     // live only while the Timeline's Cinemachine track says so
        var lens = cam.Lens;
        lens.FieldOfView = fov;
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = 450f;
        cam.Lens = lens;
        var move = go.AddComponent<ShotMove>();
        move.director = director;
        move.start = start;
        move.end = end;
        move.fromPosition = from;
        move.toPosition = to;
        move.lookAt = look;
        if (look == null)
        {
            var dir = (to - from).sqrMagnitude > 1e-6f ? to - from : Vector3.forward;
            move.fromEuler = Quaternion.LookRotation(dir).eulerAngles;
            move.toEuler = move.fromEuler;
        }
        go.transform.position = from;
        if (look != null) go.transform.LookAt(look);
        if (handheld)
        {
            var noise = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
            noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePreset);
            noise.AmplitudeGain = 0.5f;
            noise.FrequencyGain = 0.7f;
        }
        return cam;
    }

    public static Transform Focus(Transform parent, string name, Vector3 at)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = at;
        return t;
    }

    public static SignalAsset Signal(string folder, string name)
    {
        EnsureFolder(folder);
        string path = folder + "/" + name + ".signal";
        var s = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
        if (s != null) return s;
        s = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(s, path);
        return s;
    }

    public static void Emit(TimelineAsset timeline, string signalFolder, string name, double at)
    {
        if (timeline.markerTrack == null) timeline.CreateMarkerTrack();
        var e = timeline.markerTrack.CreateMarker<SignalEmitter>(at);
        e.asset = Signal(signalFolder, name);
        e.emitOnce = true;
        e.retroactive = false;
        e.name = name;
    }

    public static TimelineClip Audio(TimelineAsset timeline, PlayableDirector director, string trackName, AudioSource source,
                                     AudioClip clip, double start, double duration, double fadeIn, double fadeOut, bool loop,
                                     float volume = 1f, double clipIn = 0)
    {
        if (clip == null) return null;
        var track = timeline.GetOutputTracks().OfType<AudioTrack>().FirstOrDefault(t => t.name == trackName)
                    ?? timeline.CreateTrack<AudioTrack>(null, trackName);
        director.SetGenericBinding(track, source);
        var c = track.CreateClip(clip);
        c.start = start;
        c.duration = duration;
        c.clipIn = clipIn;
        c.easeInDuration = fadeIn;
        c.easeOutDuration = fadeOut;
        c.displayName = clip.name;
        var asset = (AudioPlayableAsset)c.asset;
        asset.loop = loop;
        var so = new SerializedObject(asset);
        var vol = so.FindProperty("m_ClipProperties.volume");
        if (vol != null) { vol.floatValue = volume; so.ApplyModifiedPropertiesWithoutUndo(); }
        return c;
    }

    public static AudioSource Source(Transform parent, string name, CutsceneAudio.Bus bus)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.outputAudioMixerGroup = CutsceneAudio.Group(bus);
        return s;
    }
}
