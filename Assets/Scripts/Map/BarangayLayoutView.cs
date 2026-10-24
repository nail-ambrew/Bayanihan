using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a BarangayLayout in the Scene view, to lay a level out against: the boundary, roads by class, waterways,
/// named areas, building footprints, and a marker on every landmark with the evacuation sites ringed. Lines and
/// markers only: nothing is spawned, and the scene keeps nothing but this component's settings.
///
/// Add it with Bayanihan > Add Barangay Layout Preview. The object is tagged EditorOnly, so it never reaches a build.
/// The layout's origin (the barangay hall) sits on this object; move or rotate it to line the map up with a level.
/// Names are written by BarangayLayoutViewLabels in the editor.
/// </summary>
public class BarangayLayoutView : MonoBehaviour
{
    [Tooltip("Leave empty for the layout that ships (Resources/Layouts).")]
    public BarangayLayout layout;
    [Tooltip("Game metres per real metre. 0 = the layout's own scale.")]
    [Min(0f)] public float scaleOverride = 0f;
    [Tooltip("Lift above this object, so lines are not lost in the ground or the water.")]
    public float heightOffset = 0.2f;

    [Header("Layers")]
    public bool showBoundary = true;
    [Tooltip("The built-up centre, where a level would start.")]
    public bool showFocusArea = true;
    public bool showAreas = true;
    [Tooltip("Outlines of every house and shed. At a small scale these only clutter.")]
    public bool showBuildings = true;
    public bool showWaterways = true;
    public bool showRoads = true;
    [Tooltip("Roads below this class are left out. At a small scale, footpaths and service lanes only clutter.")]
    public RoadClass minimumRoadClass = RoadClass.Residential;
    [Tooltip("Draw both kerbs of every road at its game width, to see where scaled-down streets run into each other.")]
    public bool showRoadWidths;
    public RoadWidths roadWidths = new RoadWidths();
    public bool showLandmarks = true;
    [Tooltip("Only the places a flood response leans on: shelter, command, rescue, health, water.")]
    public bool essentialOnly;
    [Tooltip("Building outlines of the landmarks that have one.")]
    public bool showFootprints = true;
    [Tooltip("Size of a landmark marker, in game metres.")]
    [Min(0.1f)] public float markerSize = 4f;

    [Header("Labels")]
    public bool showLabels = true;
    [Tooltip("In a perspective view, labels further than this from the camera are not drawn (game metres).")]
    public float labelDistance = 400f;

    /// <summary>How wide each class of road is in the game, in game metres. These are never scaled with the map: a
    /// street has to fit a boat however small the map is drawn. Today's main street is about 11 m kerb to kerb.</summary>
    [Serializable]
    public class RoadWidths
    {
        public float trunk = 14f;
        public float primary = 12f;
        public float secondary = 11f;
        public float tertiary = 9f;
        public float unclassified = 8f;
        public float residential = 7f;
        public float service = 5f;
        public float track = 4f;
        public float footway = 2f;

        public float For(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Trunk:        return trunk;
                case RoadClass.Primary:      return primary;
                case RoadClass.Secondary:    return secondary;
                case RoadClass.Tertiary:     return tertiary;
                case RoadClass.Unclassified: return unclassified;
                case RoadClass.Residential:  return residential;
                case RoadClass.Service:      return service;
                case RoadClass.Track:        return track;
                default:                     return footway;
            }
        }
    }

    /// <summary>A name to write on the map: what, where (real metres), and in what colour.</summary>
    public class MapLabel
    {
        public string text;
        public Vector2 position;
        public Color color;
        /// <summary>An evacuation site or the origin: drawn bold, and never hidden by distance.</summary>
        public bool important;
    }

    /// <summary>The names to write, rebuilt with the lines. Read by BarangayLayoutViewLabels.</summary>
    public readonly List<MapLabel> Labels = new List<MapLabel>();

    // ------------------------------------------------------------------ colours (dark Scene-view background)

    public static Color RoadColour(RoadClass c)
    {
        switch (c)
        {
            case RoadClass.Trunk:        return new Color(0.95f, 0.3f, 0.25f);     // national highway
            case RoadClass.Primary:
            case RoadClass.Secondary:    return new Color(1f, 0.6f, 0.15f);
            case RoadClass.Tertiary:     return new Color(1f, 0.88f, 0.3f);
            case RoadClass.Unclassified:
            case RoadClass.Residential:  return new Color(0.88f, 0.88f, 0.88f);
            case RoadClass.Service:      return new Color(0.6f, 0.6f, 0.6f);
            case RoadClass.Track:        return new Color(0.6f, 0.48f, 0.32f);
            default:                     return new Color(0.45f, 0.45f, 0.45f);
        }
    }

    public static Color WaterColour(WaterwayKind k) =>
        k >= WaterwayKind.Canal ? new Color(0.2f, 0.55f, 1f) : new Color(0.45f, 0.75f, 1f);

    public static Color AreaColour(AreaKind k)
    {
        switch (k)
        {
            case AreaKind.Residential:   return new Color(0.55f, 0.85f, 0.55f, 0.8f);
            case AreaKind.Commercial:    return new Color(0.95f, 0.55f, 0.75f, 0.8f);
            case AreaKind.Industrial:    return new Color(0.7f, 0.55f, 0.95f, 0.8f);
            case AreaKind.Institutional: return new Color(0.5f, 0.85f, 0.9f, 0.8f);
            case AreaKind.Water:         return new Color(0.2f, 0.55f, 1f, 0.9f);
            default:                     return new Color(0.6f, 0.75f, 0.45f, 0.8f);
        }
    }

    /// <summary>One colour per kind of place, bright enough for a dark Scene view and a white page alike.</summary>
    public static Color LandmarkColour(LandmarkCategory c)
    {
        switch (c)
        {
            case LandmarkCategory.BarangayHall:
            case LandmarkCategory.CityHall:
            case LandmarkCategory.Government:       return new Color(0.95f, 0.75f, 0.1f);   // command
            case LandmarkCategory.EvacuationCentre:
            case LandmarkCategory.Court:
            case LandmarkCategory.SportsHall:
            case LandmarkCategory.CommunityCentre:  return new Color(1f, 0.5f, 0.1f);       // shelter
            case LandmarkCategory.School:           return new Color(0.15f, 0.75f, 0.85f);
            case LandmarkCategory.Hospital:
            case LandmarkCategory.HealthCentre:
            case LandmarkCategory.Pharmacy:         return new Color(0.95f, 0.25f, 0.4f);   // health
            case LandmarkCategory.FireStation:      return new Color(0.9f, 0.15f, 0.1f);
            case LandmarkCategory.Police:           return new Color(0.3f, 0.45f, 1f);
            case LandmarkCategory.BarangayOutpost:  return new Color(0.55f, 0.35f, 0.95f);  // tanod post
            case LandmarkCategory.Grocery:          return new Color(0.45f, 0.8f, 0.3f);    // supplies
            case LandmarkCategory.Bank:             return new Color(0.2f, 0.7f, 0.55f);
            case LandmarkCategory.Pawnshop:         return new Color(0.8f, 0.6f, 0.25f);
            case LandmarkCategory.WaterSupply:
            case LandmarkCategory.Dam:              return new Color(0.1f, 0.6f, 0.95f);
            case LandmarkCategory.AssemblyPoint:
            case LandmarkCategory.EmergencyAccess:  return new Color(0.4f, 0.85f, 0.25f);
            default:                                return new Color(0.65f, 0.65f, 0.65f);  // market, fuel, church
        }
    }

    public static readonly Color BuildingColour = new Color(0.72f, 0.72f, 0.72f, 0.8f);
    public static readonly Color BoundaryColour = new Color(1f, 1f, 1f, 0.95f);
    public static readonly Color FocusColour = new Color(1f, 0.92f, 0.2f, 0.7f);
    public static readonly Color BridgeColour = new Color(0.95f, 0.35f, 0.95f);              // where a flood can cut a road
    public static readonly Color CandidateColour = new Color(1f, 0.55f, 0.1f);               // evacuation site, not confirmed
    public static readonly Color DesignatedColour = new Color(0.25f, 0.9f, 0.35f);           // confirmed by the barangay or CDRRMO

    // ------------------------------------------------------------------ drawing

    BarangayLayout shipped;
    BarangayLayout drawn;
    int drawnRevision = -1;
    float drawnScale = -1f;
    readonly Dictionary<Color, List<Vector3>> building = new Dictionary<Color, List<Vector3>>();
    readonly List<Color> batchColours = new List<Color>();
    readonly List<Vector3[]> batchLines = new List<Vector3[]>();

    /// <summary>The layout being drawn: the one set in the Inspector, or the one that ships.</summary>
    public BarangayLayout Layout
    {
        get
        {
            if (layout != null) return layout;
            if (shipped == null) shipped = BarangayLayout.LoadShipped();
            return shipped;
        }
    }

    public float Scale => scaleOverride > 0f ? scaleOverride : (Layout != null ? Layout.scale : 1f);

    /// <summary>Real metres to this object's local space.</summary>
    public Vector3 ToLocal(Vector2 metres) => BarangayLayout.ToWorld(metres, heightOffset, Scale);

    void OnValidate() { drawnRevision = -1; }       // a setting changed: draw it again

    void OnDrawGizmos()
    {
        var map = Layout;
        if (map == null || !isActiveAndEnabled) return;
        if (map != drawn || map.revision != drawnRevision || !Mathf.Approximately(Scale, drawnScale)) Rebuild(map);

        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < batchLines.Count; i++)
        {
            Gizmos.color = batchColours[i];
            Gizmos.DrawLineList(batchLines[i]);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>
    /// Turns the map into one line list per colour, so a repaint is a dozen draw calls rather than thousands. Only
    /// runs again when the layout is rebuilt, its scale changes or a setting here changes.
    /// </summary>
    void Rebuild(BarangayLayout map)
    {
        drawn = map;
        drawnRevision = map.revision;
        drawnScale = Scale;
        foreach (var list in building.Values) list.Clear();
        Labels.Clear();

        if (showBoundary)
            foreach (var ring in map.boundary) Polyline(ring.points, true, BoundaryColour);

        if (showFocusArea && map.focusArea.width > 0f)
        {
            Rect r = map.focusArea;
            var corners = new List<Vector2> { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                                              new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            Polyline(corners, true, FocusColour);
        }

        if (showAreas)
            foreach (var area in map.areas) Polyline(area.outline, true, AreaColour(area.kind));

        if (showBuildings)
            foreach (var b in map.buildings) Polyline(b.outline, true, BuildingColour);

        if (showWaterways)
            foreach (var w in map.waterways)
            {
                Polyline(w.points, false, WaterColour(w.kind));
                if (w.kind >= WaterwayKind.Canal) Polyline(w.points, false, WaterColour(w.kind), 0.6f);   // rivers read wider
            }

        if (showRoads)
        {
            var longest = new Dictionary<string, MapRoad>();
            foreach (var road in map.roads)
            {
                if (road.roadClass < minimumRoadClass) continue;
                Polyline(road.points, false, road.bridge ? BridgeColour : RoadColour(road.roadClass));
                if (showRoadWidths) Kerbs(road.points, roadWidths.For(road.roadClass) * 0.5f);

                // One label per named road, on its longest piece.
                if (road.roadClass >= RoadClass.Tertiary && !string.IsNullOrEmpty(road.name) &&
                    (!longest.TryGetValue(road.name, out var best) || Length(road.points) > Length(best.points)))
                    longest[road.name] = road;
            }
            foreach (var road in longest.Values)
                Labels.Add(new MapLabel { text = road.name, position = Midpoint(road.points), color = RoadColour(road.roadClass) });
        }

        if (showLandmarks)
            foreach (var l in map.landmarks)
            {
                if (!l.positionKnown || (essentialOnly && !l.Essential)) continue;     // no position: nothing to draw
                Color c = LandmarkColour(l.category);
                if (showFootprints && l.footprint.Count > 2) Polyline(l.footprint, true, c);
                Marker(l.position, c);
                if (l.IsEvacuationSite) EvacuationRing(l.position, l.verified && l.evacuationRole == EvacuationRole.Designated);
                if (l.Essential || l.IsEvacuationSite || l.Business)
                    Labels.Add(new MapLabel
                    {
                        text = l.Label + (l.IsEvacuationSite ? (l.verified ? "  [evacuation]" : "  [evacuation?]") : ""),
                        position = l.position,
                        color = l.IsEvacuationSite ? CandidateColour : c,
                        important = l.IsEvacuationSite || l.position.sqrMagnitude < 1f
                    });
            }

        batchColours.Clear();
        batchLines.Clear();
        foreach (var pair in building)
        {
            if (pair.Value.Count == 0) continue;
            batchColours.Add(pair.Key);
            batchLines.Add(pair.Value.ToArray());
        }
    }

    List<Vector3> Batch(Color c)
    {
        if (!building.TryGetValue(c, out var list)) building[c] = list = new List<Vector3>();
        return list;
    }

    void Polyline(List<Vector2> pts, bool closed, Color c, float sideOffset = 0f)
    {
        if (pts == null || pts.Count < 2) return;
        var list = Batch(c);
        int n = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = ToLocal(pts[i]), b = ToLocal(pts[(i + 1) % pts.Count]);
            if (sideOffset != 0f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized * sideOffset;
                a += side;
                b += side;
            }
            list.Add(a);
            list.Add(b);
        }
    }

    void Kerbs(List<Vector2> pts, float halfWidth)
    {
        var list = Batch(new Color(0.75f, 0.75f, 0.75f, 0.45f));
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 a = ToLocal(pts[i]), b = ToLocal(pts[i + 1]);
            Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized * halfWidth;
            list.Add(a + side); list.Add(b + side);
            list.Add(a - side); list.Add(b - side);
        }
    }

    void Marker(Vector2 position, Color c)
    {
        var list = Batch(c);
        Vector3 p = ToLocal(position);
        float s = markerSize * 0.5f;
        Vector3 n = p + Vector3.forward * s, e = p + Vector3.right * s, so = p - Vector3.forward * s, w = p - Vector3.right * s;
        list.Add(n); list.Add(e); list.Add(e); list.Add(so); list.Add(so); list.Add(w); list.Add(w); list.Add(n);
        list.Add(p); list.Add(p + Vector3.up * markerSize * 1.5f);                  // a short pole, for perspective views
    }

    void EvacuationRing(Vector2 position, bool confirmed)
    {
        var list = Batch(confirmed ? DesignatedColour : CandidateColour);
        Vector3 p = ToLocal(position);
        float r = markerSize * 1.6f;
        const int segments = 24;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            list.Add(p + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r);
            list.Add(p + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r);
        }
        list.Add(p); list.Add(p + Vector3.up * markerSize * 4f);                    // a tall pole: shelter is here
    }

    static float Length(List<Vector2> pts)
    {
        float d = 0f;
        for (int i = 0; i + 1 < pts.Count; i++) d += Vector2.Distance(pts[i], pts[i + 1]);
        return d;
    }

    /// <summary>The point half-way along a line.</summary>
    public static Vector2 Midpoint(List<Vector2> pts)
    {
        float half = Length(pts) * 0.5f;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            float d = Vector2.Distance(pts[i], pts[i + 1]);
            if (d >= half) return Vector2.Lerp(pts[i], pts[i + 1], d > 0f ? half / d : 0f);
            half -= d;
        }
        return pts.Count > 0 ? pts[pts.Count - 1] : Vector2.zero;
    }
}
