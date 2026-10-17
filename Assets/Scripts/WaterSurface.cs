using UnityEngine;

/// <summary>
/// Low-poly animated water. Builds a flat-shaded (faceted) grid mesh and moves it with a few
/// summed sine waves. The SAME wave function is exposed via GetHeight() so boats/objects can float on it.
/// Put this on your "Plane" (it replaces the plane's mesh at runtime).
///
/// The animated grid only covers the area around the viewer. Past it a flat "skirt" carries the flood out to the
/// horizon, and the waves flatten towards the grid's edge so the two meet without a step.
/// </summary>
[DefaultExecutionOrder(-100)]   // the waves advance before anything samples them this frame
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterSurface : MonoBehaviour
{
    public static WaterSurface Instance { get; private set; }

    [System.Serializable]
    public class Wave
    {
        [Range(0f, 360f)] public float direction = 0f;   // degrees, 0 = +Z, 90 = +X
        public float amplitude = 0.3f;                   // metres
        public float wavelength = 12f;                   // metres
        public float speed = 2f;                         // metres / second
        public Wave() { }
        public Wave(float dir, float amp, float len, float spd)
        { direction = dir; amplitude = amp; wavelength = len; speed = spd; }
    }

    [Header("Mesh")]
    [Tooltip("Mesh re-centres on this (snapped to the grid) so the flood feels endless. Leave empty to follow the " +
             "Main Camera, which is right whether the player is driving or swimming.")]
    public Transform followTarget;
    [Range(8, 100)] public int resolution = 60;          // quads per side
    public float size = 150f;                             // metres per side
    [Tooltip("Flat water drawn around the animated grid so the flood never visibly ends (metres per side). " +
             "Anything not bigger than Size switches it off.")]
    public float skirtSize = 2000f;
    [Tooltip("How far in from the grid's edge the waves start flattening out to meet the skirt (fraction of the half-size).")]
    [Range(0.02f, 0.5f)] public float edgeFade = 0.15f;

    [Header("Waves")]
    public Wave[] waves =
    {
        new Wave(20f,  0.30f, 14f, 2.0f),
        new Wave(110f, 0.18f,  7f, 1.5f),
        new Wave(250f, 0.08f,  4f, 1.0f),
    };

    const float SkirtDrop = 0.02f;   // the skirt sits just under the grid's flattened rim, so it can never poke through

    Mesh mesh, skirtMesh;
    Vector3[] verts;
    float[,] heights;
    float cell;
    int builtResolution;
    float builtSize, builtSkirt;
    MeshFilter skirtFilter;
    Camera cam;

    // Per-wave terms, refreshed once a frame so sampling a height is one Sin per wave.
    // Phase is accumulated rather than derived from the clock: speed can then change (the storm building up)
    // without the whole pattern jumping.
    float[] waveKx, waveKz, waveAmp, wavePhase;

    void Awake()
    {
        Instance = this;

        // The mesh works in world-aligned coordinates, so keep the object un-rotated / un-scaled.
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // The default Plane has a MeshCollider that would not match the waves - disable it.
        var mc = GetComponent<MeshCollider>();
        if (mc != null) mc.enabled = false;

        // Nothing under the flood needs its shadow, and the grid is rebuilt every frame.
        GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        RefreshWaves(0f);
        BuildMesh();
    }

    // Statics do not survive a script reload in Play mode. OnEnable runs again afterwards; Awake does not.
    void OnEnable()
    {
        Instance = this;
        RefreshWaves(0f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (mesh != null) Destroy(mesh);
        if (skirtMesh != null) Destroy(skirtMesh);
    }

    void Update()
    {
        RefreshWaves(Time.deltaTime);
    }

    void LateUpdate()
    {
        UpdateMesh();
    }

    // ------------------------------------------------------------------ wave math
    /// <summary>World-space water height at the given world position.</summary>
    public float GetHeight(Vector3 worldPos)
    {
        return transform.position.y + WaveSum(worldPos.x, worldPos.z);
    }

    float WaveSum(float x, float z)
    {
        if (waveAmp == null) return 0f;
        float y = 0f;
        for (int i = 0; i < waveAmp.Length; i++)
            y += waveAmp[i] * Mathf.Sin(waveKx[i] * x + waveKz[i] * z - wavePhase[i]);
        return y;
    }

    void RefreshWaves(float dt)
    {
        int n = waves != null ? waves.Length : 0;
        if (waveAmp == null || waveAmp.Length != n)
        {
            var oldPhase = wavePhase;
            waveKx = new float[n];
            waveKz = new float[n];
            waveAmp = new float[n];
            wavePhase = new float[n];
            if (oldPhase != null) System.Array.Copy(oldPhase, wavePhase, Mathf.Min(oldPhase.Length, n));
        }

        for (int i = 0; i < n; i++)
        {
            var w = waves[i];
            if (w == null || w.wavelength <= 0.01f) { waveAmp[i] = 0f; continue; }

            float rad = w.direction * Mathf.Deg2Rad;
            float k = 2f * Mathf.PI / w.wavelength;
            waveKx[i] = k * Mathf.Sin(rad);
            waveKz[i] = k * Mathf.Cos(rad);
            waveAmp[i] = w.amplitude;
            wavePhase[i] = Mathf.Repeat(wavePhase[i] + k * w.speed * dt, 2f * Mathf.PI);
        }
    }

    // ------------------------------------------------------------------ mesh
    bool HasSkirt => skirtSize > size;

    Transform Follow()
    {
        if (followTarget != null) return followTarget;
        if (cam == null) cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    void BuildMesh()
    {
        builtResolution = resolution;
        builtSize = size;
        cell = size / resolution;
        int quads = resolution * resolution;

        if (mesh != null) Destroy(mesh);
        mesh = new Mesh { name = "LowPolyWater" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();

        // Unshared vertices (6 per quad) => every triangle gets its own normal => faceted low-poly look.
        verts = new Vector3[quads * 6];
        var tris = new int[quads * 6];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;

        heights = new float[resolution + 1, resolution + 1];

        mesh.vertices = verts;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;

        BuildSkirt();
    }

    /// <summary>
    /// A flat frame of water around the grid. It tucks one cell under the grid's rim, which is flattened to the
    /// same level, so there is neither a gap nor a step where they meet.
    /// </summary>
    void BuildSkirt()
    {
        builtSkirt = skirtSize;

        if (!HasSkirt)
        {
            if (skirtFilter != null) skirtFilter.gameObject.SetActive(false);
            return;
        }

        if (skirtFilter == null)
        {
            var go = new GameObject("WaterSkirt");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            skirtFilter = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        skirtFilter.gameObject.SetActive(true);

        float o = skirtSize * 0.5f;
        float i = Mathf.Max(size * 0.5f - cell, 0f);
        float y = -SkirtDrop;

        if (skirtMesh != null) Destroy(skirtMesh);
        skirtMesh = new Mesh { name = "WaterSkirt" };
        skirtMesh.vertices = new[]
        {
            new Vector3(-o, y, -o), new Vector3(-o, y, o), new Vector3(o, y, o), new Vector3(o, y, -o),   // outer
            new Vector3(-i, y, -i), new Vector3(-i, y, i), new Vector3(i, y, i), new Vector3(i, y, -i),   // inner
        };
        skirtMesh.triangles = new[]
        {
            0, 1, 5,  0, 5, 4,   // -X side
            1, 2, 6,  1, 6, 5,   // +Z side
            2, 3, 7,  2, 7, 6,   // +X side
            3, 0, 4,  3, 4, 7,   // -Z side
        };
        skirtMesh.normals = new[]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up
        };
        skirtMesh.RecalculateBounds();
        skirtFilter.sharedMesh = skirtMesh;
    }

    void UpdateMesh()
    {
        // heights is a 2D array, which Unity cannot keep across a script reload - rebuild rather than throw.
        if (mesh == null || verts == null || heights == null || builtResolution != resolution ||
            !Mathf.Approximately(builtSize, size))
            BuildMesh();
        else if (!Mathf.Approximately(builtSkirt, skirtSize))
            BuildSkirt();

        // Follow the viewer, snapped to whole cells so the wave pattern never "swims" across the mesh.
        Vector3 pos = transform.position;
        Transform target = Follow();
        if (target != null)
        {
            pos.x = Mathf.Round(target.position.x / cell) * cell;
            pos.z = Mathf.Round(target.position.z / cell) * cell;
            transform.position = pos;
        }

        float half = size * 0.5f;

        // The outermost ring of cells is held flat so it lines up with the skirt; the fade leads into it.
        bool fade = HasSkirt;
        float fadeEnd = half - cell;
        float fadeStart = Mathf.Min(half * (1f - edgeFade), fadeEnd - cell);
        float fadeSpan = Mathf.Max(fadeEnd - fadeStart, 0.0001f);

        // 1) sample heights at grid corners
        for (int i = 0; i <= resolution; i++)
        {
            float lx = -half + i * cell;
            for (int j = 0; j <= resolution; j++)
            {
                float lz = -half + j * cell;
                float h = WaveSum(pos.x + lx, pos.z + lz);
                if (fade)
                {
                    float d = Mathf.Max(Mathf.Abs(lx), Mathf.Abs(lz));
                    if (d > fadeStart) h *= 1f - Mathf.SmoothStep(0f, 1f, (d - fadeStart) / fadeSpan);
                }
                heights[i, j] = h;
            }
        }

        // 2) fill two triangles per quad
        int v = 0;
        for (int i = 0; i < resolution; i++)
        {
            float x0 = -half + i * cell, x1 = x0 + cell;
            for (int j = 0; j < resolution; j++)
            {
                float z0 = -half + j * cell, z1 = z0 + cell;

                var a = new Vector3(x0, heights[i, j], z0);
                var b = new Vector3(x0, heights[i, j + 1], z1);
                var c = new Vector3(x1, heights[i + 1, j + 1], z1);
                var d = new Vector3(x1, heights[i + 1, j], z0);

                verts[v++] = a; verts[v++] = b; verts[v++] = c;
                verts[v++] = a; verts[v++] = c; verts[v++] = d;
            }
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 10f, size));
    }
}
