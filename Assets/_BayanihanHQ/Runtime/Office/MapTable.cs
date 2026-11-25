using System.Collections.Generic;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// The map table in the middle of the office: a small top-down plan of Barangay Mabuhay drawn at startup from the
    /// game's own map data (BarangayLayout: boundary, roads, creeks, landmarks). It reads the 855 KB layout asset, not the
    /// 6,600-house gameplay scene, so it costs a few milliseconds once.
    /// </summary>
    public class MapTable : MonoBehaviour
    {
        [Tooltip("The table top quad the plan is drawn on.")]
        public Renderer top;
        public int resolution = 768;

        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);
        static readonly Color Land = new Color(0.72f, 0.80f, 0.55f);
        static readonly Color Road = new Color(0.98f, 0.95f, 0.88f);
        static readonly Color MainRoad = new Color(1f, 0.82f, 0.45f);
        static readonly Color Water = new Color(0.30f, 0.58f, 0.85f);
        static readonly Color Edge = new Color(0.42f, 0.25f, 0.12f);
        static readonly Color Pin = new Color(0.82f, 0.18f, 0.15f);
        static readonly Color Shelter = new Color(0.15f, 0.55f, 0.20f);

        void Start()
        {
            var layout = BarangayLayout.Current;
            if (top == null || layout == null || layout.boundary.Count == 0) return;
            var tex = Draw(layout, resolution);
            var mat = top.material;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
        }

        /// <summary>Draws the plan into a texture, north up, the barangay boundary filling most of it.</summary>
        public static Texture2D Draw(BarangayLayout layout, int size)
        {
            var px = new Color32[size * size];
            Color32 paper = Paper;
            for (int i = 0; i < px.Length; i++) px[i] = paper;

            // Fit the boundary's bounds into the square with a margin.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var ring in layout.boundary)
                foreach (var p in ring.points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            float span = Mathf.Max(max.x - min.x, max.y - min.y) * 1.08f;
            var centre = (min + max) * 0.5f;
            Vector2 ToPx(Vector2 m) => new Vector2((m.x - centre.x) / span + 0.5f, (m.y - centre.y) / span + 0.5f) * size;

            foreach (var ring in layout.boundary) FillPolygon(px, size, ring.points, ToPx, Land);
            foreach (var w in layout.waterways)
                Polyline(px, size, w.points, ToPx, Water, w.kind == WaterwayKind.River || w.kind == WaterwayKind.Canal ? 3.5f : 1.8f);
            foreach (var r in layout.roads)
            {
                bool main = r.roadClass.ToString().Contains("Primary") || r.roadClass.ToString().Contains("Secondary") || r.roadClass.ToString().Contains("Trunk");
                Polyline(px, size, r.points, ToPx, main ? MainRoad : Road, main ? 3.2f : 1.6f);
            }
            foreach (var ring in layout.boundary) Polyline(px, size, ring.points, ToPx, Edge, 2.5f, closed: true);
            foreach (var l in layout.landmarks)
            {
                if (!l.positionKnown || !l.insideBarangay) continue;
                Dot(px, size, ToPx(l.position), l.IsEvacuationSite ? Shelter : Pin, l.Essential ? 5f : 3f);
            }
            Dot(px, size, ToPx(Vector2.zero), new Color(0.95f, 0.55f, 0.15f), 7f);   // the barangay hall, the layout's origin

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Mabuhay Plan", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static void FillPolygon(Color32[] px, int size, List<Vector2> points, System.Func<Vector2, Vector2> toPx, Color colour)
        {
            if (points.Count < 3) return;
            var poly = new Vector2[points.Count];
            for (int i = 0; i < poly.Length; i++) poly[i] = toPx(points[i]);
            Color32 c = colour;
            var xs = new List<float>();
            for (int y = 0; y < size; y++)
            {
                // Scanline fill: where this row crosses the outline, fill between pairs of crossings.
                float fy = y + 0.5f;
                xs.Clear();
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[i];
                    var b = poly[j];
                    if ((a.y > fy) == (b.y > fy)) continue;
                    xs.Add(a.x + (fy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt(xs[k]), 0, size);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt(xs[k + 1]), 0, size - 1);
                    for (int x = x0; x <= x1; x++) px[y * size + x] = c;
                }
            }
        }

        static void Polyline(Color32[] px, int size, List<Vector2> points, System.Func<Vector2, Vector2> toPx, Color colour, float width, bool closed = false)
        {
            int n = points.Count;
            for (int i = 0; i + 1 < n + (closed ? 1 : 0); i++)
                Line(px, size, toPx(points[i]), toPx(points[(i + 1) % n]), colour, width);
        }

        static void Line(Color32[] px, int size, Vector2 a, Vector2 b, Color colour, float width)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.75f));
            for (int s = 0; s <= steps; s++) Dot(px, size, Vector2.Lerp(a, b, s / (float)steps), colour, width * 0.5f);
        }

        static void Dot(Color32[] px, int size, Vector2 centre, Color colour, float radius)
        {
            Color32 c = colour;
            int r = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(centre.x), cy = Mathf.RoundToInt(centre.y);
            float r2 = radius * radius;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= size) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= size) continue;
                    float dx = x - centre.x, dy = y - centre.y;
                    if (dx * dx + dy * dy <= r2) px[y * size + x] = c;
                }
            }
        }
    }
}
