using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a BarangayLayout as a printable map, Docs/Maps/mabuhay-layout.svg, that opens in any browser: the boundary,
/// roads, waterways, areas and landmarks with their names, the evacuation sites ringed, a legend, a scale bar in real
/// and game metres, and the OpenStreetMap credit. It is the layout as the asset holds it, so it doubles as a check on
/// the asset, and it is the thing to take to the barangay or Carmona CDRRMO to confirm the evacuation sites.
/// </summary>
public static class BarangayLayoutMapExport
{
    public const string OutputFile = "Docs/Maps/mabuhay-layout.svg";

    const float MapWidth = 2400f;      // pixels across the map; the legend sits to the right of it
    const float PanelWidth = 560f;
    const float Margin = 40f;
    const float Pad = 120f;            // real metres of map around everything drawn

    [MenuItem("Bayanihan/Export Barangay Layout Map")]
    static void ExportMenu()
    {
        string path = Export(BarangayLayout.LoadShipped());
        if (path != null) EditorUtility.RevealInFinder(path);
    }

    [MenuItem("Bayanihan/Export Barangay Layout Map", true)]
    static bool ExportValidate() => !Application.isPlaying;

    /// <summary>Writes the map and returns its full path, or null (with the reason in the Console).</summary>
    public static string Export(BarangayLayout map)
    {
        if (map == null || map.boundary.Count == 0)
        {
            Debug.LogError("BarangayLayoutMapExport: there is no layout to draw. Run Bayanihan > Rebuild Barangay Layout first.");
            return null;
        }

        // ------------------------------------------------------------ fit everything on the page
        Rect extent = map.Bounds;
        void Include(Vector2 p) => extent = Rect.MinMaxRect(Mathf.Min(extent.xMin, p.x), Mathf.Min(extent.yMin, p.y),
                                                            Mathf.Max(extent.xMax, p.x), Mathf.Max(extent.yMax, p.y));
        foreach (var l in map.landmarks) if (l.positionKnown) Include(l.position);
        foreach (var r in map.roads) if (r.roadClass >= RoadClass.Tertiary) foreach (var p in r.points) Include(p);
        extent = Rect.MinMaxRect(extent.xMin - Pad, extent.yMin - Pad, extent.xMax + Pad, extent.yMax + Pad);

        float k = MapWidth / extent.width;                      // pixels per real metre
        float mapHeight = extent.height * k;
        float width = Margin + MapWidth + Margin + PanelWidth + Margin;
        float height = Mathf.Max(mapHeight + 2f * Margin, 1500f);
        string X(Vector2 p) => F(Margin + (p.x - extent.xMin) * k);
        string Y(Vector2 p) => F(Margin + (extent.yMax - p.y) * k);
        string Path(List<Vector2> pts, bool closed)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pts.Count; i++) sb.Append(i == 0 ? "M" : " L").Append(X(pts[i])).Append(',').Append(Y(pts[i]));
            if (closed) sb.Append(" Z");
            return sb.ToString();
        }

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(width)).Append("\" height=\"").Append(F(height))
           .Append("\" viewBox=\"0 0 ").Append(F(width)).Append(' ').Append(F(height)).Append("\" font-family=\"Helvetica, Arial, sans-serif\">\n");
        svg.Append("<title>").Append(Esc("Barangay " + map.barangay + ", " + map.city + ", " + map.province + " - layout")).Append("</title>\n");
        svg.Append("<style>.halo{paint-order:stroke;stroke:#fff;stroke-width:3.5px;stroke-linejoin:round}</style>\n");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
        svg.Append("<rect x=\"").Append(F(Margin)).Append("\" y=\"").Append(F(Margin)).Append("\" width=\"").Append(F(MapWidth))
           .Append("\" height=\"").Append(F(mapHeight)).Append("\" fill=\"#e7e3d9\"/>\n");
        svg.Append("<svg x=\"0\" y=\"0\" width=\"").Append(F(Margin + MapWidth)).Append("\" height=\"").Append(F(Margin + mapHeight))
           .Append("\" overflow=\"hidden\">\n");

        // ------------------------------------------------------------ the map, bottom layer first
        foreach (var ring in map.boundary)
            svg.Append("<path d=\"").Append(Path(ring.points, true)).Append("\" fill=\"#fbf8f0\" fill-rule=\"evenodd\"/>\n");

        foreach (var a in map.areas)
        {
            string c = AreaFill(a.kind);
            svg.Append("<path d=\"").Append(Path(a.outline, true)).Append("\" fill=\"").Append(c)
               .Append("\" fill-opacity=\"").Append(a.kind == AreaKind.Water ? "0.8" : "0.35").Append("\" stroke=\"").Append(c)
               .Append("\" stroke-width=\"1\"/>\n");
        }

        Rect focus = map.focusArea;
        if (focus.width > 0f)
            svg.Append("<rect x=\"").Append(X(new Vector2(focus.xMin, 0f))).Append("\" y=\"").Append(Y(new Vector2(0f, focus.yMax)))
               .Append("\" width=\"").Append(F(focus.width * k)).Append("\" height=\"").Append(F(focus.height * k))
               .Append("\" fill=\"none\" stroke=\"#c99a06\" stroke-width=\"2\" stroke-dasharray=\"10 6\"/>\n");

        foreach (var w in map.waterways.OrderBy(w => w.kind))
            svg.Append("<path d=\"").Append(Path(w.points, false)).Append("\" fill=\"none\" stroke=\"")
               .Append(w.kind >= WaterwayKind.Canal ? "#2f7fd0" : "#73b2ec").Append("\" stroke-width=\"")
               .Append(w.kind >= WaterwayKind.Canal ? "4" : "1.8").Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>\n");

        foreach (var r in map.roads.Where(r => r.bridge))
            svg.Append("<path d=\"").Append(Path(r.points, false)).Append("\" fill=\"none\" stroke=\"#b23cc4\" stroke-width=\"")
               .Append(F(RoadWidth(r.roadClass) + 5f)).Append("\" stroke-linecap=\"butt\"/>\n");
        foreach (var r in map.roads.OrderBy(r => r.roadClass))
        {
            svg.Append("<path d=\"").Append(Path(r.points, false)).Append("\" fill=\"none\" stroke=\"").Append(RoadStroke(r.roadClass))
               .Append("\" stroke-width=\"").Append(F(RoadWidth(r.roadClass))).Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
            if (r.roadClass <= RoadClass.Track) svg.Append(" stroke-dasharray=\"4 3\"");
            svg.Append("/>\n");
        }

        foreach (var l in map.landmarks.Where(l => l.positionKnown && l.footprint.Count > 2))
        {
            string c = Hex(BarangayLayoutView.LandmarkColour(l.category));
            svg.Append("<path d=\"").Append(Path(l.footprint, true)).Append("\" fill=\"").Append(c)
               .Append("\" fill-opacity=\"0.45\" stroke=\"").Append(c).Append("\" stroke-width=\"1\"/>\n");
        }

        foreach (var ring in map.boundary)
            svg.Append("<path d=\"").Append(Path(ring.points, true))
               .Append("\" fill=\"none\" stroke=\"#2b2b2b\" stroke-width=\"3\" stroke-dasharray=\"16 6 3 6\"/>\n");

        // Markers: evacuation rings under the dots.
        foreach (var l in map.landmarks.Where(l => l.positionKnown))
        {
            if (l.IsEvacuationSite)
            {
                bool confirmed = l.verified && l.evacuationRole == EvacuationRole.Designated;
                svg.Append("<circle cx=\"").Append(X(l.position)).Append("\" cy=\"").Append(Y(l.position))
                   .Append("\" r=\"14\" fill=\"none\" stroke=\"").Append(confirmed ? "#1f9e3a" : "#f07d00")
                   .Append("\" stroke-width=\"3.5\"").Append(confirmed ? "" : " stroke-dasharray=\"6 3\"").Append("/>\n");
            }
            svg.Append("<circle cx=\"").Append(X(l.position)).Append("\" cy=\"").Append(Y(l.position)).Append("\" r=\"")
               .Append(l.Essential ? "6" : "4").Append("\" fill=\"").Append(Hex(BarangayLayoutView.LandmarkColour(l.category)))
               .Append("\" stroke=\"#ffffff\" stroke-width=\"1.5\"/>\n");
        }

        // Origin cross-hair on the hall.
        svg.Append("<path d=\"M").Append(F(Margin + (0f - extent.xMin) * k - 22f)).Append(',').Append(Y(Vector2.zero))
           .Append(" h44 M").Append(X(Vector2.zero)).Append(',').Append(F(Margin + (extent.yMax - 0f) * k - 22f))
           .Append(" v44\" stroke=\"#111\" stroke-width=\"1.5\"/>\n");

        // ------------------------------------------------------------ names
        var longest = new Dictionary<string, MapRoad>();
        foreach (var r in map.roads)
            if (r.roadClass >= RoadClass.Tertiary && !string.IsNullOrEmpty(r.name) &&
                (!longest.TryGetValue(r.name, out var best) || Length(r.points) > Length(best.points)))
                longest[r.name] = r;
        foreach (var r in longest.Values)
        {
            Vector2 mid = BarangayLayoutView.Midpoint(r.points);
            svg.Append("<text class=\"halo\" x=\"").Append(X(mid)).Append("\" y=\"").Append(Y(mid))
               .Append("\" font-size=\"14\" font-style=\"italic\" fill=\"#5a4a2a\" text-anchor=\"middle\">").Append(Esc(r.name)).Append("</text>\n");
        }

        foreach (var l in map.landmarks.Where(l => l.positionKnown && (l.Essential || l.IsEvacuationSite)))
        {
            bool evac = l.IsEvacuationSite;
            string text = l.Label + (l.position.sqrMagnitude < 1f ? "  (0, 0)" : "");
            svg.Append("<text class=\"halo\" x=\"").Append(F(Margin + (l.position.x - extent.xMin) * k + (evac ? 17f : 9f)))
               .Append("\" y=\"").Append(F(Margin + (extent.yMax - l.position.y) * k + 4.5f)).Append("\" font-size=\"")
               .Append(evac ? "14" : "12").Append("\"").Append(evac ? " font-weight=\"bold\" fill=\"#b35500\"" : " fill=\"#222\"")
               .Append('>').Append(Esc(text)).Append("</text>\n");
        }
        svg.Append("</svg>\n");

        // ------------------------------------------------------------ title, north, scale
        float tx = Margin + 16f, ty = Margin + 16f;
        svg.Append("<rect x=\"").Append(F(tx)).Append("\" y=\"").Append(F(ty)).Append("\" width=\"900\" height=\"118\" rx=\"6\" fill=\"#ffffff\" fill-opacity=\"0.92\" stroke=\"#999\"/>\n");
        Text(svg, tx + 18f, ty + 40f, 30, "bold", "#111", "Barangay " + map.barangay + " · " + map.city + ", " + map.province);
        Text(svg, tx + 18f, ty + 70f, 16, "normal", "#333",
             "Layout for the Bayanihan flood-rescue level · PSGC " + map.psgc + " · population " +
             map.population.ToString("N0", CultureInfo.InvariantCulture) + " (" + map.populationYear + " census)");
        Text(svg, tx + 18f, ty + 96f, 14, "normal", "#555",
             "Origin (0, 0): " + map.originName + " · " + map.originLatitude.ToString("0.00000", CultureInfo.InvariantCulture) + "° N, " +
             map.originLongitude.ToString("0.00000", CultureInfo.InvariantCulture) + "° E · OpenStreetMap snapshot " + Day(map.snapshotDate));

        float nx = Margin + MapWidth - 60f, ny = Margin + 40f;
        svg.Append("<path d=\"M").Append(F(nx)).Append(',').Append(F(ny)).Append(" l-14,40 l14,-10 l14,10 Z\" fill=\"#222\"/>\n");
        Text(svg, nx - 6f, ny + 64f, 18, "bold", "#222", "N");

        float bar = 1000f * k, sx = Margin + 24f, sy = Margin + mapHeight - 54f;
        svg.Append("<rect x=\"").Append(F(sx - 12f)).Append("\" y=\"").Append(F(sy - 34f)).Append("\" width=\"").Append(F(bar + 230f))
           .Append("\" height=\"78\" rx=\"6\" fill=\"#ffffff\" fill-opacity=\"0.92\" stroke=\"#999\"/>\n");
        svg.Append("<rect x=\"").Append(F(sx)).Append("\" y=\"").Append(F(sy)).Append("\" width=\"").Append(F(bar / 2f)).Append("\" height=\"8\" fill=\"#222\"/>\n");
        svg.Append("<rect x=\"").Append(F(sx + bar / 2f)).Append("\" y=\"").Append(F(sy)).Append("\" width=\"").Append(F(bar / 2f)).Append("\" height=\"8\" fill=\"#fff\" stroke=\"#222\"/>\n");
        Text(svg, sx - 4f, sy - 8f, 13, "normal", "#222", "0");
        Text(svg, sx + bar / 2f - 14f, sy - 8f, 13, "normal", "#222", "500");
        Text(svg, sx + bar - 10f, sy - 8f, 13, "normal", "#222", "1,000 m real");
        Text(svg, sx, sy + 30f, 13, "normal", "#555", "= " + Mathf.RoundToInt(1000f * map.scale) + " m in the game at scale " +
             map.scale.ToString("0.###", CultureInfo.InvariantCulture) + " (road widths are not scaled)");

        // ------------------------------------------------------------ legend
        float lx = Margin + MapWidth + Margin, ly = Margin;
        float y = ly + 10f;
        Text(svg, lx, y + 20f, 22, "bold", "#111", "Legend");
        y += 46f;

        y = Heading(svg, lx, y, "Roads");
        foreach (var c in new[] { RoadClass.Trunk, RoadClass.Secondary, RoadClass.Tertiary, RoadClass.Unclassified,
                                  RoadClass.Residential, RoadClass.Service, RoadClass.Footway })
        {
            svg.Append("<line x1=\"").Append(F(lx)).Append("\" y1=\"").Append(F(y)).Append("\" x2=\"").Append(F(lx + 44f))
               .Append("\" y2=\"").Append(F(y)).Append("\" stroke=\"").Append(RoadStroke(c)).Append("\" stroke-width=\"").Append(F(RoadWidth(c)))
               .Append('"').Append(c <= RoadClass.Track ? " stroke-dasharray=\"4 3\"" : "").Append("/>\n");
            Text(svg, lx + 56f, y + 5f, 14, "normal", "#222", RoadName(c));
            y += 24f;
        }
        svg.Append("<line x1=\"").Append(F(lx)).Append("\" y1=\"").Append(F(y)).Append("\" x2=\"").Append(F(lx + 44f)).Append("\" y2=\"")
           .Append(F(y)).Append("\" stroke=\"#b23cc4\" stroke-width=\"9\"/><line x1=\"").Append(F(lx)).Append("\" y1=\"").Append(F(y))
           .Append("\" x2=\"").Append(F(lx + 44f)).Append("\" y2=\"").Append(F(y)).Append("\" stroke=\"#8f8a80\" stroke-width=\"2\"/>\n");
        Text(svg, lx + 56f, y + 5f, 14, "normal", "#222", "Bridge: where a flood can cut the road");
        y += 34f;

        y = Heading(svg, lx, y, "Water and areas");
        LegendLine(svg, lx, y, "#2f7fd0", 4f, "", "River (Carmona River on the east)"); y += 24f;
        LegendLine(svg, lx, y, "#73b2ec", 1.8f, "", "Stream"); y += 24f;
        LegendLine(svg, lx, y, "#2b2b2b", 3f, "16 6 3 6", "Barangay boundary"); y += 24f;
        LegendLine(svg, lx, y, "#c99a06", 2f, "10 6", "Focus area: the built-up centre"); y += 24f;
        foreach (var kind in new[] { AreaKind.Residential, AreaKind.Commercial, AreaKind.Industrial, AreaKind.Institutional, AreaKind.Water })
        {
            svg.Append("<rect x=\"").Append(F(lx + 8f)).Append("\" y=\"").Append(F(y - 8f)).Append("\" width=\"28\" height=\"16\" fill=\"")
               .Append(AreaFill(kind)).Append("\" fill-opacity=\"").Append(kind == AreaKind.Water ? "0.8" : "0.45").Append("\"/>\n");
            Text(svg, lx + 56f, y + 5f, 14, "normal", "#222", AreaName(kind));
            y += 24f;
        }
        y += 10f;

        y = Heading(svg, lx, y, "Places");
        svg.Append("<circle cx=\"").Append(F(lx + 22f)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"14\" fill=\"none\" stroke=\"#f07d00\" stroke-width=\"3.5\" stroke-dasharray=\"6 3\"/>\n");
        Text(svg, lx + 56f, y + 5f, 14, "bold", "#b35500", "Evacuation site, not yet confirmed");
        y += 34f;
        svg.Append("<circle cx=\"").Append(F(lx + 22f)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"14\" fill=\"none\" stroke=\"#1f9e3a\" stroke-width=\"3.5\"/>\n");
        Text(svg, lx + 56f, y + 5f, 14, "normal", "#222", "Evacuation site, confirmed by the barangay or CDRRMO");
        y += 32f;
        var places = new (LandmarkCategory category, string label)[]
        {
            (LandmarkCategory.BarangayHall, "Barangay hall, city hall, government"),
            (LandmarkCategory.Court, "Court, gymnasium, community centre"),
            (LandmarkCategory.School, "School"),
            (LandmarkCategory.Hospital, "Hospital, health centre, pharmacy"),
            (LandmarkCategory.FireStation, "Fire station"),
            (LandmarkCategory.Police, "Police"),
            (LandmarkCategory.WaterSupply, "Water supply, dam"),
            (LandmarkCategory.EmergencyAccess, "Emergency access"),
            (LandmarkCategory.Market, "Market, fuel, church")
        };
        foreach (var (category, label) in places)
        {
            svg.Append("<circle cx=\"").Append(F(lx + 22f)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"6\" fill=\"")
               .Append(Hex(BarangayLayoutView.LandmarkColour(category))).Append("\" stroke=\"#fff\" stroke-width=\"1.5\"/>\n");
            Text(svg, lx + 56f, y + 5f, 14, "normal", "#222", label);
            y += 24f;
        }
        y += 14f;

        y = Heading(svg, lx, y, "Evacuation sites (" + map.EvacuationSites.Count() + ")");
        foreach (var s in map.EvacuationSites.OrderBy(s => !s.insideBarangay).ThenBy(s => s.Label))
        {
            string where = !s.positionKnown ? "position unknown" : s.insideBarangay ? "in Mabuhay" : "nearby";
            string status = s.verified && s.evacuationRole == EvacuationRole.Designated ? "confirmed" : "unconfirmed";
            Text(svg, lx, y, 14, "bold", "#222", "• " + s.Label);
            Text(svg, lx + 14f, y + 18f, 12, "normal", "#666", where + " · " + status);
            y += 40f;
        }
        y += 6f;

        foreach (var line in Wrap("Evacuation sites are candidates taken from public sources (see Docs/Maps/README.md), " +
                                  "not a list from the barangay or Carmona CDRRMO. Confirm them before relying on this map.", 66))
        {
            Text(svg, lx, y, 13, "normal", "#7a3d00", line);
            y += 18f;
        }
        y += 10f;
        foreach (var line in Wrap("Map data " + map.attribution + ", " + map.licence + ".", 66))
        {
            Text(svg, lx, y, 13, "normal", "#444", line);
            y += 18f;
        }

        svg.Append("</svg>\n");

        string path = System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFile);
        File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
        Debug.Log("BarangayLayoutMapExport: wrote " + OutputFile + " (" + F(width) + " x " + F(height) + " px).");
        return path;
    }

    // ------------------------------------------------------------------ styling (white page)

    static float RoadWidth(RoadClass c)
    {
        switch (c)
        {
            case RoadClass.Trunk:        return 7f;
            case RoadClass.Primary:
            case RoadClass.Secondary:    return 5f;
            case RoadClass.Tertiary:     return 4f;
            case RoadClass.Unclassified: return 2.5f;
            case RoadClass.Residential:  return 2f;
            case RoadClass.Service:      return 1.2f;
            default:                     return 1f;
        }
    }

    static string RoadStroke(RoadClass c)
    {
        switch (c)
        {
            case RoadClass.Trunk:        return "#d63a2f";
            case RoadClass.Primary:
            case RoadClass.Secondary:    return "#ee9325";
            case RoadClass.Tertiary:     return "#e3bd2c";
            case RoadClass.Unclassified: return "#6f6a61";
            case RoadClass.Residential:  return "#8f8a80";
            case RoadClass.Service:      return "#b9b3a6";
            case RoadClass.Track:        return "#a08a68";
            default:                     return "#a49d8f";
        }
    }

    static string RoadName(RoadClass c)
    {
        switch (c)
        {
            case RoadClass.Trunk:        return "National highway (Governor's Drive)";
            case RoadClass.Secondary:    return "Secondary road (Sugar Road, J. M. Loyola St)";
            case RoadClass.Tertiary:     return "Tertiary road (San Lazaro Drive, ...)";
            case RoadClass.Unclassified: return "Minor through road";
            case RoadClass.Residential:  return "Residential street";
            case RoadClass.Service:      return "Service lane";
            default:                     return "Footpath, steps";
        }
    }

    static string AreaFill(AreaKind k)
    {
        switch (k)
        {
            case AreaKind.Residential:   return "#7cc47c";
            case AreaKind.Commercial:    return "#e58db4";
            case AreaKind.Industrial:    return "#a78be0";
            case AreaKind.Institutional: return "#6cc3d1";
            case AreaKind.Water:         return "#8ec3f0";
            default:                     return "#a9c77a";
        }
    }

    static string AreaName(AreaKind k)
    {
        switch (k)
        {
            case AreaKind.Residential:   return "Subdivision";
            case AreaKind.Commercial:    return "Commercial";
            case AreaKind.Industrial:    return "Industrial park";
            case AreaKind.Institutional: return "Church, seminary";
            case AreaKind.Water:         return "Pond, reservoir";
            default:                     return "Green";
        }
    }

    // ------------------------------------------------------------------ helpers

    static float Heading(StringBuilder svg, float x, float y, string text)
    {
        Text(svg, x, y + 6f, 16, "bold", "#111", text);
        return y + 30f;
    }

    static void LegendLine(StringBuilder svg, float x, float y, string colour, float width, string dash, string label)
    {
        svg.Append("<line x1=\"").Append(F(x)).Append("\" y1=\"").Append(F(y)).Append("\" x2=\"").Append(F(x + 44f)).Append("\" y2=\"")
           .Append(F(y)).Append("\" stroke=\"").Append(colour).Append("\" stroke-width=\"").Append(F(width)).Append('"')
           .Append(string.IsNullOrEmpty(dash) ? "" : " stroke-dasharray=\"" + dash + "\"").Append("/>\n");
        Text(svg, x + 56f, y + 5f, 14, "normal", "#222", label);
    }

    static void Text(StringBuilder svg, float x, float y, int size, string weight, string fill, string text)
    {
        svg.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y)).Append("\" font-size=\"").Append(size)
           .Append("\" font-weight=\"").Append(weight).Append("\" fill=\"").Append(fill).Append("\">").Append(Esc(text)).Append("</text>\n");
    }

    static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    static float Length(List<Vector2> pts)
    {
        float d = 0f;
        for (int i = 0; i + 1 < pts.Count; i++) d += Vector2.Distance(pts[i], pts[i + 1]);
        return d;
    }

    static string Day(string timestamp) => timestamp != null && timestamp.Length >= 10 ? timestamp.Substring(0, 10) : timestamp;

    static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    static string Esc(string s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
