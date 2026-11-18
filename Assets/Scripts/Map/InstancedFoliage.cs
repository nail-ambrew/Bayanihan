using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The undergrowth of a map - tall grass, low plants, bushes, vines, bananas: everything that grows but has nothing solid -
/// drawn with GPU instancing instead of a GameObject each. The scene keeps one short line per plant (which species, where,
/// turned and leaning how, how big) rather than a prefab instance, and only the plants near the camera and in its view are
/// drawn. Filled by FoliageBuilder; trees, with their trunks, stay ordinary objects.
/// </summary>
[ExecuteAlways]
public class InstancedFoliage : MonoBehaviour
{
    [Serializable]
    public struct Plant
    {
        public int species;
        public Vector3 position;      // world
        public Vector3 euler;         // lean x, yaw, lean z
        public float scale;
    }

    [Serializable]
    public class Species
    {
        public string name;
        public GameObject prefab;
        [Tooltip("World metres beyond which it is not drawn.")]
        public float drawDistance = 120f;
        public bool shadows;
    }

    public List<Species> species = new List<Species>();
    public List<Plant> plants = new List<Plant>();

    const float Tile = 32f;

    /// <summary>One drawn part of a species: a mesh's submesh, its material, and where it sits in the prefab.</summary>
    struct Part { public Mesh mesh; public int submesh; public Material material; public Matrix4x4 local; public bool shadows; }

    sealed class Bucket
    {
        public Bounds bounds;
        public float reach;
        public readonly Dictionary<int, List<Matrix4x4>> byPart = new Dictionary<int, List<Matrix4x4>>();
    }

    readonly List<Part> parts = new List<Part>();
    readonly Dictionary<Vector2Int, Bucket> tiles = new Dictionary<Vector2Int, Bucket>();
    readonly Plane[] planes = new Plane[6];
    int built = -1;

    public int Count => plants.Count;

    /// <summary>
    /// Multiplies every plant and grass draw distance (here and in <see cref="GrassField"/>). 1 in play; the cinematics
    /// lower it (they show less of the map at once) and bring it back to 1 before handing over.
    /// </summary>
    public static float DistanceScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetScale() => DistanceScale = 1f;

    void OnEnable() => built = -1;

    void Build()
    {
        parts.Clear();
        tiles.Clear();
        var partsOf = new List<int>[species.Count];
        for (int s = 0; s < species.Count; s++)
        {
            partsOf[s] = new List<int>();
            var prefab = species[s].prefab;
            if (prefab == null) continue;
            var inverse = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var mats = mr.sharedMaterials;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount && sub < mats.Length; sub++)
                {
                    if (mats[sub] == null) continue;
                    partsOf[s].Add(parts.Count);
                    parts.Add(new Part { mesh = mf.sharedMesh, submesh = sub, material = mats[sub], local = inverse * mf.transform.localToWorldMatrix, shadows = species[s].shadows });
                }
            }
        }

        foreach (var p in plants)
        {
            if (p.species < 0 || p.species >= species.Count) continue;
            var key = new Vector2Int(Mathf.FloorToInt(p.position.x / Tile), Mathf.FloorToInt(p.position.z / Tile));
            if (!tiles.TryGetValue(key, out var b))
            {
                tiles[key] = b = new Bucket();
                b.bounds = new Bounds(new Vector3((key.x + 0.5f) * Tile, p.position.y + 3f, (key.y + 0.5f) * Tile), new Vector3(Tile + 12f, 14f, Tile + 12f));
            }
            b.reach = Mathf.Max(b.reach, species[p.species].drawDistance);
            var m = Matrix4x4.TRS(p.position, Quaternion.Euler(p.euler), Vector3.one * p.scale);
            foreach (int part in partsOf[p.species])
            {
                if (!b.byPart.TryGetValue(part, out var list)) b.byPart[part] = list = new List<Matrix4x4>();
                list.Add(m * parts[part].local);
            }
        }
        built = plants.Count;
    }

    void Update()
    {
        if (plants.Count == 0) return;
        if (built != plants.Count) Build();
        var cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 eye = cam.transform.position;

        foreach (var t in tiles.Values)
        {
            Vector3 c = t.bounds.center;
            float reach = t.reach * DistanceScale + Tile;
            if (new Vector2(c.x - eye.x, c.z - eye.z).sqrMagnitude > reach * reach) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, t.bounds)) continue;
            foreach (var kv in t.byPart)
            {
                var part = parts[kv.Key];
                var rp = new RenderParams(part.material) { shadowCastingMode = part.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = t.bounds };
                Graphics.RenderMeshInstanced(rp, part.mesh, part.submesh, kv.Value);
            }
        }
    }

    /// <summary>The plants within <paramref name="radius"/> metres (world, flat) of a point.</summary>
    public int CountWithin(Vector2 world, float radius)
    {
        float r2 = radius * radius;
        int n = 0;
        foreach (var p in plants) if ((new Vector2(p.position.x, p.position.z) - world).sqrMagnitude <= r2) n++;
        return n;
    }
}
