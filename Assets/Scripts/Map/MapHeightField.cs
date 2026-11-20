using UnityEngine;

/// <summary>
/// The lie of the land under a map: how far the ground at each point stands above (+) or below (-) the map's flat
/// ground level, in metres, on a regular grid in world x and z. The Mabuhay map's ground level is a metre under the flood,
/// so 0 here is a metre of water, +1 is the water's edge and anything above about +1.6 stays dry even at the flood's highest.
///
/// Made by TerrainBuilder from the real elevation (Docs/Maps/mabuhay-elevation.json), smoothed, scaled for play and then
/// held at 0 along the mission's streets so the rescue route keeps its depth. Every builder that stands something on
/// the ground reads it, so the roads, houses, walls and plants all follow the same slopes.
/// </summary>
public class MapHeightField : ScriptableObject
{
    [Tooltip("World x and z of the first grid point (south-west corner).")]
    public Vector2 origin;
    [Tooltip("Metres between grid points.")]
    public float step = 2f;
    public int nx, ny;
    [Tooltip("Height above the map's ground level at each grid point, row by row from the south: heights[j * nx + i].")]
    public float[] heights = new float[0];

    [Header("Where it came from")]
    public string source = "";
    [Tooltip("Lowest and highest point of the field, in metres above the ground level.")]
    public float min, max;

    public bool Ready => heights != null && nx > 1 && ny > 1 && heights.Length == nx * ny;

    /// <summary>Height above the ground level at a world point (x, z), bilinear; the nearest edge value outside the grid.</summary>
    public float Sample(Vector2 world)
    {
        if (!Ready) return 0f;
        float fx = (world.x - origin.x) / step, fy = (world.y - origin.y) / step;
        fx = Mathf.Clamp(fx, 0f, nx - 1.001f);
        fy = Mathf.Clamp(fy, 0f, ny - 1.001f);
        int i = (int)fx, j = (int)fy;
        float tx = fx - i, ty = fy - j;
        float a = heights[j * nx + i], b = heights[j * nx + i + 1];
        float c = heights[(j + 1) * nx + i], d = heights[(j + 1) * nx + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    /// <summary>Height above the ground level at a world position (its x and z).</summary>
    public float Sample(Vector3 world) => Sample(new Vector2(world.x, world.z));

    /// <summary>The lowest and highest ground under a rectangle given by its world corners.</summary>
    public void Range(Vector3[] corners, out float low, out float high)
    {
        low = float.MaxValue; high = float.MinValue;
        foreach (var p in corners)
        {
            float h = Sample(p);
            low = Mathf.Min(low, h); high = Mathf.Max(high, h);
        }
        if (corners.Length == 0) low = high = 0f;
    }
}
