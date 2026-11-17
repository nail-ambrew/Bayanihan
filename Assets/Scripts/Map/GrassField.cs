using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The low grass over all the open ground of a map: patches of blades sticking up out of the flood. Stored as a list of
/// patches (where, which way, which of a few patch meshes and colours) and drawn with GPU instancing - only the patches
/// within <see cref="drawDistance"/> of the camera and in its view - so tens of thousands of patches cost a few draw calls,
/// and the scene stores a few hundred kilobytes rather than a mesh per blade. No colliders: the boat goes through grass.
/// Filled by FoliageBuilder.
/// </summary>
[ExecuteAlways]
public class GrassField : MonoBehaviour
{
    [Tooltip("The patch meshes, world-sized, each a few metres across.")]
    public Mesh[] patches;
    [Tooltip("The colours a patch may be drawn in (one material each, instancing on).")]
    public Material[] materials;
    [Tooltip("Each patch: x, z (world metres), yaw (degrees), and mesh * 16 + material in w.")]
    public List<Vector4> instances = new List<Vector4>();
    [Tooltip("World height of the patches' base (the map's ground).")]
    public float groundY = -1f;
    [Tooltip("Patches further than this from the camera are not drawn.")]
    public float drawDistance = 170f;

    const float Tile = 32f;

    sealed class Bucket { public Bounds bounds; public readonly List<Matrix4x4>[] byKind; public Bucket(int kinds) { byKind = new List<Matrix4x4>[kinds]; } }
    readonly Dictionary<Vector2Int, Bucket> tiles = new Dictionary<Vector2Int, Bucket>();
    readonly Plane[] planes = new Plane[6];
    int built = -1;

    public int Count => instances.Count;

    void OnEnable() => built = -1;

    void Build()
    {
        tiles.Clear();
        int kinds = Mathf.Max(1, (patches?.Length ?? 0) * Mathf.Max(1, materials?.Length ?? 0));
        foreach (var v in instances)
        {
            var key = new Vector2Int(Mathf.FloorToInt(v.x / Tile), Mathf.FloorToInt(v.y / Tile));
            if (!tiles.TryGetValue(key, out var b))
            {
                tiles[key] = b = new Bucket(kinds);
                b.bounds = new Bounds(new Vector3((key.x + 0.5f) * Tile, groundY + 1.5f, (key.y + 0.5f) * Tile), new Vector3(Tile + 8f, 4f, Tile + 8f));
            }
            int kind = Mathf.Clamp((int)v.w, 0, kinds - 1);
            (b.byKind[kind] ??= new List<Matrix4x4>()).Add(Matrix4x4.TRS(new Vector3(v.x, groundY, v.y), Quaternion.Euler(0f, v.z, 0f), Vector3.one));
        }
        built = instances.Count;
    }

    void Update()
    {
        if (patches == null || patches.Length == 0 || materials == null || materials.Length == 0) return;
        if (built != instances.Count) Build();
        var cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 eye = cam.transform.position;
        float reach = drawDistance + Tile;

        int mats = materials.Length;
        foreach (var t in tiles.Values)
        {
            Vector3 c = t.bounds.center;
            if ((new Vector2(c.x - eye.x, c.z - eye.z)).sqrMagnitude > reach * reach) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, t.bounds)) continue;
            for (int k = 0; k < t.byKind.Length; k++)
            {
                var list = t.byKind[k];
                if (list == null || list.Count == 0) continue;
                var mesh = patches[(k / mats) % patches.Length];
                var rp = new RenderParams(materials[k % mats]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = t.bounds };
                Graphics.RenderMeshInstanced(rp, mesh, 0, list);
            }
        }
    }

    /// <summary>Is there a patch whose middle is within <paramref name="radius"/> metres (world) of this point?</summary>
    public bool AnyWithin(Vector2 world, float radius)
    {
        float r2 = radius * radius;
        foreach (var v in instances) if ((new Vector2(v.x, v.y) - world).sqrMagnitude <= r2) return true;
        return false;
    }
}
