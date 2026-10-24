using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The real layout of a barangay: its boundary, roads, waterways, named areas and the places that matter in a flood
/// (evacuation sites, the barangay hall, schools, health, police, fire, water). It is a map, not a level: nothing here
/// is a 3D asset. A level is built over it later, and BarangayLayoutView draws it in the Scene view to build against.
///
/// Everything is stored at real size, in metres east (x) and north (y) of the origin (the barangay hall), so distances
/// can be checked against the real place. <see cref="scale"/> shrinks it to game size: ToWorld puts east on +X and
/// north on +Z, like the rest of the game.
///
/// The one that ships is Barangay Mabuhay, City of Carmona, Cavite, built from OpenStreetMap. It lives in
/// Assets/Resources/Layouts; rebuild it from Docs/Maps/mabuhay-carmona.geojson with Bayanihan > Rebuild Barangay Layout.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Barangay Layout", fileName = "BarangayLayout")]
public class BarangayLayout : ScriptableObject
{
    public const string ResourceFolder = "Layouts";

    [Header("Place")]
    public string barangay = "";
    public string city = "";
    public string province = "";
    [Tooltip("Philippine Standard Geographic Code of the barangay.")]
    public string psgc = "";
    public int population;
    public int populationYear;
    public string populationSource = "";

    [Header("Origin (0, 0)")]
    public string originName = "";
    public double originLatitude;
    public double originLongitude;

    [Header("Scale")]
    [Tooltip("Game metres per real metre. The map is kept at real size and ToWorld shrinks it by this much: 0.15 puts " +
             "the whole barangay (about 4.3 km across) into about 640 m. Road widths are not scaled.")]
    [Range(0.05f, 1f)] public float scale = 0.15f;
    [Tooltip("The built-up centre around the hall, school, hospital and market, in real metres: where a level starts.")]
    public Rect focusArea;
    [Tooltip("Roads, streams and buildings are complete inside this, even where it spills past the barangay boundary: " +
             "the focus area plus a margin. Real metres.")]
    public Rect detailArea;

    [Header("Map (real metres east and north of the origin)")]
    [Tooltip("The barangay's boundary. Usually one ring.")]
    public List<MapRing> boundary = new List<MapRing>();
    public List<MapRoad> roads = new List<MapRoad>();
    public List<MapWaterway> waterways = new List<MapWaterway>();
    public List<MapArea> areas = new List<MapArea>();
    [Tooltip("Every building in the barangay, and every one in the detail area. The landmarks are not repeated here.")]
    public List<MapBuilding> buildings = new List<MapBuilding>();
    public List<MapLandmark> landmarks = new List<MapLandmark>();

    [Header("Source")]
    [Tooltip("Has to be shown wherever the map is shown, in the game included.")]
    public string attribution = "";
    public string licence = "";
    [Tooltip("When the OpenStreetMap data was taken.")]
    public string snapshotDate = "";
    [Tooltip("The file this was built from, relative to the project folder.")]
    public string sourceFile = "";
    [Tooltip("Goes up by one every time the layout is rebuilt, so previews know to redraw.")]
    public int revision;

    // ------------------------------------------------------------------ the layout that ships
    static BarangayLayout current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; }

    /// <summary>The layout in use: whatever was set, otherwise the one in Resources/Layouts (null if there is none).</summary>
    public static BarangayLayout Current
    {
        get
        {
            if (current == null) current = LoadShipped();
            return current;
        }
        set { current = value; }
    }

    /// <summary>The layout asset that ships with the game, or null if there is none.</summary>
    public static BarangayLayout LoadShipped()
    {
        var all = Resources.LoadAll<BarangayLayout>(ResourceFolder);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    // ------------------------------------------------------------------ real places to metres to the game

    /// <summary>
    /// How many metres one degree spans at this latitude, east and north (WGS84). Near Carmona it is about 107.9 km
    /// east and 110.6 km north. Docs/Maps/fetch_mabuhay_osm.py uses the same series, so the two always agree.
    /// </summary>
    public static void MetresPerDegree(double latitude, out double east, out double north)
    {
        double p = latitude * Math.PI / 180.0;
        north = 111132.954 - 559.822 * Math.Cos(2 * p) + 1.175 * Math.Cos(4 * p);
        east = 111412.84 * Math.Cos(p) - 93.5 * Math.Cos(3 * p) + 0.118 * Math.Cos(5 * p);
    }

    /// <summary>A real place, in metres east (x) and north (y) of the origin. Good to well under a metre across the barangay.</summary>
    public Vector2 ToLocalMetres(double latitude, double longitude)
    {
        MetresPerDegree(originLatitude, out double east, out double north);
        return new Vector2((float)((longitude - originLongitude) * east), (float)((latitude - originLatitude) * north));
    }

    /// <summary>The way back: metres from the origin to latitude and longitude.</summary>
    public void ToLatLon(Vector2 metres, out double latitude, out double longitude)
    {
        MetresPerDegree(originLatitude, out double east, out double north);
        latitude = originLatitude + metres.y / north;
        longitude = originLongitude + metres.x / east;
    }

    /// <summary>Real metres to game space at this layout's scale: east is +X, north is +Z.</summary>
    public Vector3 ToWorld(Vector2 metres, float y = 0f) => ToWorld(metres, y, scale);

    /// <summary>Real metres to game space at any scale (a level can blow up the focus area, say).</summary>
    public static Vector3 ToWorld(Vector2 metres, float y, float withScale) =>
        new Vector3(metres.x * withScale, y, metres.y * withScale);

    /// <summary>Game space back to real metres, at this layout's scale.</summary>
    public Vector2 ToMetres(Vector3 world) => new Vector2(world.x / scale, world.z / scale);

    // ------------------------------------------------------------------ questions about the map

    /// <summary>True if a point (real metres) is inside the barangay's boundary.</summary>
    public bool Contains(Vector2 metres) => Contains(boundary, metres);

    /// <summary>Even-odd test over every ring, so a hole in the boundary is outside.</summary>
    public static bool Contains(List<MapRing> rings, Vector2 p)
    {
        bool hit = false;
        if (rings == null) return false;
        foreach (var ring in rings)
        {
            var pts = ring.points;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                Vector2 a = pts[i], b = pts[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    hit = !hit;
            }
        }
        return hit;
    }

    /// <summary>The boundary's extent, in real metres.</summary>
    public Rect Bounds
    {
        get
        {
            bool any = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            foreach (var ring in boundary)
                foreach (var p in ring.points)
                {
                    if (!any) { min = max = p; any = true; continue; }
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }

    /// <summary>The buildings whose middle is inside a rectangle of real metres.</summary>
    public IEnumerable<MapBuilding> BuildingsIn(Rect region)
    {
        foreach (var b in buildings)
            if (b.outline.Count >= 3 && region.Contains(MapGeometry.Centroid(b.outline))) yield return b;
    }

    public MapLandmark Find(string id)
    {
        foreach (var l in landmarks) if (l.id == id) return l;
        return null;
    }

    public IEnumerable<MapLandmark> Landmarks(LandmarkCategory category)
    {
        foreach (var l in landmarks) if (l.category == category) yield return l;
    }

    /// <summary>Every place marked as somewhere evacuees can go, candidate or designated.</summary>
    public IEnumerable<MapLandmark> EvacuationSites
    {
        get { foreach (var l in landmarks) if (l.IsEvacuationSite) yield return l; }
    }

    /// <summary>Roads whose name contains this (any case): "Loyola" finds J. M. Loyola Street.</summary>
    public IEnumerable<MapRoad> RoadsNamed(string fragment)
    {
        foreach (var r in roads)
            if (!string.IsNullOrEmpty(r.name) && r.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                yield return r;
    }
}
