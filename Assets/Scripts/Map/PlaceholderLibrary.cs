using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The kinds of house the slum-map models come in.</summary>
public enum HouseType { Shop, TwoStorey, ThreeStorey, Corner, Tower }

/// <summary>
/// A model that stands in for something until the final one is made. Every placeholder prefab follows one convention:
/// the pivot is the middle of the footprint at ground level, and the front faces +Z. A final model that follows it can
/// be dropped into <see cref="prefab"/> and the map rebuilt.
/// </summary>
[Serializable]
public class PlaceholderModel
{
    public string name;
    public GameObject prefab;
    [Tooltip("Width (x), height (y) and depth (z) in metres, measured from the prefab each time the map is built. " +
             "Do not type it.")]
    public Vector3 size;

    [Header("Where it came from (Make Placeholder Prefabs reads these)")]
    [Tooltip("The imported model it is baked from. Empty for a model you made yourself.")]
    public GameObject sourceModel;
    [Tooltip("Path of the part to bake inside the model, e.g. manila_slum_map/houses/house_04_twostory. Empty = the whole model.")]
    public string sourcePath;
    [Tooltip("Degrees the model's front is turned from +Z about the vertical: 0 if it already faces +Z, 180 if it faces -Z.")]
    public float sourceFrontYaw;
    [Tooltip("Names of parts to leave out when baking, e.g. ground_plane.")]
    public string[] strip = new string[0];
    [Tooltip("A pattern for the names of the parts that make up the body (walls). Empty = every part counts.")]
    public string bodyPattern;
}

/// <summary>One house the generator can put on a footprint.</summary>
[Serializable]
public class HouseVariant : PlaceholderModel
{
    public HouseType type;
    [Range(1, 6)] public int storeys = 2;
}

/// <summary>How a kind of landmark is drawn.</summary>
public enum LandmarkPlacement
{
    /// <summary>One prefab (the barangay hall), fitted to the footprint when it fits, otherwise a block.</summary>
    Prefab,
    /// <summary>A choice of prefabs, one per building in the grounds (the school's blocks).</summary>
    Parts,
    /// <summary>A shop house with a sign: banks, groceries, pharmacies, pawnshops.</summary>
    ShopHouse,
    /// <summary>A plain coloured block of the real outline.</summary>
    Block,
    /// <summary>A flat slab with an open roof on posts.</summary>
    Court
}

/// <summary>How one category of landmark is drawn on the placeholder map.</summary>
[Serializable]
public class LandmarkStyle
{
    public LandmarkCategory category;
    public LandmarkPlacement placement = LandmarkPlacement.Block;
    [Tooltip("The model for Prefab placement.")]
    public PlaceholderModel model = new PlaceholderModel();
    [Tooltip("The models for Parts placement: the best fit for each building is used.")]
    public List<PlaceholderModel> parts = new List<PlaceholderModel>();
    [Tooltip("A model is only used if it has to be scaled by at least this much to fit.")]
    [Min(0.1f)] public float minScale = 0.45f;
    [Tooltip("A model is only used if it has to be scaled by at most this much to fit.")]
    [Min(0.1f)] public float maxScale = 2f;
    [Tooltip("Height of the block, in metres.")]
    [Min(1f)] public float blockHeight = 8f;
    [Tooltip("Colour of the block and the sign. Transparent = the colour this category has on the map.")]
    public Color colour = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Text on the sign. Empty = the landmark's name.")]
    public string sign;
}

/// <summary>
/// The stand-ins the placeholder map is built from, and the numbers it is built with. Change a model here, rebuild,
/// and every house or landmark of that kind changes: nothing is placed by hand.
///
/// Lives in Assets/Settings/Maps, deliberately not in Resources: anything in Resources goes into every build.
/// Fill it with Bayanihan > Make Placeholder Prefabs; build the map with Bayanihan > Build Barangay Mock.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Placeholder Library", fileName = "PlaceholderLibrary")]
public class PlaceholderLibrary : ScriptableObject
{
    [Header("Houses")]
    [Tooltip("Every house the generator can choose from. Each building gets the one that fits its footprint best.")]
    public List<HouseVariant> houses = new List<HouseVariant>();

    [Header("Landmarks")]
    [Tooltip("One entry per category. A category with no entry is drawn as a plain block.")]
    public List<LandmarkStyle> landmarks = new List<LandmarkStyle>();

    [Header("Roads and water, real widths in metres")]
    public BarangayLayoutView.RoadWidths roadWidths = new BarangayLayoutView.RoadWidths
    {
        trunk = 12f, primary = 10f, secondary = 9f, tertiary = 8f, unclassified = 7f,
        residential = 6f, service = 4f, track = 3.5f, footway = 1.5f
    };
    public float riverWidth = 12f;
    public float canalWidth = 8f;
    public float streamWidth = 4f;
    public float drainWidth = 2f;
    public float ditchWidth = 1.5f;

    [Header("Fitting a house to a footprint")]
    [Tooltip("A house is stretched by at least this much on each axis to fill its plot.")]
    [Min(0.1f)] public float minStretch = 0.8f;
    [Tooltip("A house is stretched by at most this much on each axis. Longer plots become a row of houses.")]
    [Min(1f)] public float maxStretch = 1.6f;
    [Tooltip("The width of one house in a row, in metres.")]
    [Min(1f)] public float rowUnit = 4.5f;
    [Tooltip("A frontage longer than this many row units is split into several houses.")]
    [Min(1f)] public float rowSplit = 1.8f;
    [Tooltip("A building bigger than this (square metres) is a plain block, not a house.")]
    [Min(10f)] public float blockArea = 300f;
    [Tooltip("A building smaller than this (square metres) is a shed.")]
    [Min(1f)] public float shedArea = 15f;
    [Tooltip("A shop node counts as inside a building this close to it, in metres (shops often sit on the pavement).")]
    [Min(0f)] public float hostReach = 8f;
    [Tooltip("A building this small (square metres) becomes a shop house when a shop is in it; bigger ones become a block with a sign.")]
    [Min(10f)] public float hostMaxArea = 150f;

    /// <summary>How a category is drawn: its entry, or a plain block if it has none.</summary>
    public LandmarkStyle StyleFor(LandmarkCategory category)
    {
        foreach (var s in landmarks) if (s.category == category) return s;
        return new LandmarkStyle { category = category };
    }

    public float WaterWidth(WaterwayKind kind)
    {
        switch (kind)
        {
            case WaterwayKind.River: return riverWidth;
            case WaterwayKind.Canal: return canalWidth;
            case WaterwayKind.Stream: return streamWidth;
            case WaterwayKind.Drain: return drainWidth;
            default: return ditchWidth;
        }
    }

    // ------------------------------------------------------------------ measuring the models

    /// <summary>
    /// How big a prefab is: width, height and depth in metres. A baked placeholder says so itself (its body, without
    /// eaves); any other prefab is measured from its meshes.
    /// </summary>
    public static Vector3 Measure(GameObject prefab)
    {
        if (prefab == null) return Vector3.zero;
        var body = prefab.GetComponent<PlaceholderBody>();
        if (body != null && body.size.sqrMagnitude > 0f) return body.size;

        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        bool any = false;
        Bounds bounds = new Bounds();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds.size;
    }

    /// <summary>Measures every prefab in the library again. Call it before planning, so a swapped-in model is sized right.</summary>
    public void MeasureAll()
    {
        foreach (var h in houses) if (h.prefab != null) h.size = Measure(h.prefab);
        foreach (var s in landmarks)
        {
            if (s.model != null && s.model.prefab != null) s.model.size = Measure(s.model.prefab);
            foreach (var p in s.parts) if (p.prefab != null) p.size = Measure(p.prefab);
        }
    }
}
