using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The flat geometry behind a placeholder map: outlines, rectangles that fit them, nearest streets, ear-clipped fills,
/// extruded walls, road ribbons and clipping. Everything is in real metres on the map: x is east, y is north, and a
/// ring is counter-clockwise when viewed from above. A mesh point is (x, height, y), so north is +Z like the rest of
/// the game. None of it touches a scene, so it can be tested.
/// </summary>
public static class MapGeometry
{
    const float Eps = 1e-6f;

    // ------------------------------------------------------------------ rings

    /// <summary>Area of an outline: positive when it runs counter-clockwise, negative clockwise.</summary>
    public static float SignedArea(IList<Vector2> ring)
    {
        if (ring == null || ring.Count < 3) return 0f;
        // Taken from the first point and added in double, so a small building far from the origin keeps its precision.
        Vector2 o = ring[0];
        double a = 0.0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            double x0 = ring[j].x - o.x, y0 = ring[j].y - o.y, x1 = ring[i].x - o.x, y1 = ring[i].y - o.y;
            a += x0 * y1 - x1 * y0;
        }
        return (float)(a * 0.5);
    }

    /// <summary>The middle of an outline by area. A flat outline gives the average of its points.</summary>
    public static Vector2 Centroid(IList<Vector2> ring)
    {
        if (ring == null || ring.Count == 0) return Vector2.zero;
        Vector2 o = ring[0];
        double a = 0.0, cx = 0.0, cy = 0.0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            double x0 = ring[j].x - o.x, y0 = ring[j].y - o.y, x1 = ring[i].x - o.x, y1 = ring[i].y - o.y;
            double c = x0 * y1 - x1 * y0;
            a += c;
            cx += (x0 + x1) * c;
            cy += (y0 + y1) * c;
        }
        if (Math.Abs(a) < 1e-9)
        {
            double sx = 0.0, sy = 0.0;
            foreach (var p in ring) { sx += p.x; sy += p.y; }
            return new Vector2((float)(sx / ring.Count), (float)(sy / ring.Count));
        }
        return new Vector2((float)(o.x + cx / (3.0 * a)), (float)(o.y + cy / (3.0 * a)));
    }

    /// <summary>True if the point is inside the outline (even-odd).</summary>
    public static bool Contains(IList<Vector2> ring, Vector2 p)
    {
        if (ring == null) return false;
        bool hit = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Vector2 a = ring[i], b = ring[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                hit = !hit;
        }
        return hit;
    }

    /// <summary>The smallest upright rectangle round some points. Nothing at all gives an empty rectangle.</summary>
    public static Rect BoundsOf(IList<Vector2> points)
    {
        if (points == null || points.Count == 0) return new Rect();
        Vector2 min = points[0], max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>A copy of the outline that runs counter-clockwise.</summary>
    public static List<Vector2> CounterClockwise(IList<Vector2> ring)
    {
        var copy = new List<Vector2>(ring);
        if (SignedArea(copy) < 0f) copy.Reverse();
        return copy;
    }

    public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>The direction turned a quarter-turn counter-clockwise (to the left of the way you are facing).</summary>
    public static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);

    /// <summary>Degrees about +Y that turn a model's +Z to face this map direction (x east, y north).</summary>
    public static float Heading(Vector2 direction) => Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

    // ------------------------------------------------------------------ rectangles

    /// <summary>A rectangle at any angle. U runs along the long side, V along the short side, a quarter-turn to its left.</summary>
    public class OrientedRect
    {
        public Vector2 centre;
        public Vector2 axisU = Vector2.right;
        public Vector2 axisV = Vector2.up;
        public float sizeU;
        public float sizeV;

        public float Area => sizeU * sizeV;

        /// <summary>The four corners, counter-clockwise.</summary>
        public List<Vector2> Corners()
        {
            Vector2 u = axisU * (sizeU * 0.5f), v = axisV * (sizeV * 0.5f);
            return new List<Vector2> { centre - u - v, centre + u - v, centre + u + v, centre - u + v };
        }

        /// <summary>True if the point is inside, with room to spare (in metres) on every side.</summary>
        public bool Contains(Vector2 p, float margin = 0f)
        {
            Vector2 d = p - centre;
            return Mathf.Abs(Vector2.Dot(d, axisU)) <= sizeU * 0.5f + margin &&
                   Mathf.Abs(Vector2.Dot(d, axisV)) <= sizeV * 0.5f + margin;
        }
    }

    /// <summary>The corners of the convex hull, counter-clockwise, without repeats (Andrew's monotone chain).</summary>
    public static List<Vector2> ConvexHull(IList<Vector2> points)
    {
        var pts = new List<Vector2>(points);
        pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        for (int i = pts.Count - 1; i > 0; i--)
            if ((pts[i] - pts[i - 1]).sqrMagnitude < 1e-10f) pts.RemoveAt(i);
        if (pts.Count < 3) return pts;

        var hull = new List<Vector2>(pts.Count * 2);
        for (int i = 0; i < pts.Count; i++)                         // lower hull
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], pts[i] - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }
        int lower = hull.Count + 1;
        for (int i = pts.Count - 2; i >= 0; i--)                    // upper hull
        {
            while (hull.Count >= lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], pts[i] - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>
    /// The smallest rectangle, at any angle, that holds the points: the best of one per hull edge. U is the long side.
    /// A tie goes to the first edge, so the same outline always gives the same rectangle.
    /// </summary>
    public static OrientedRect MinimumRectangle(IList<Vector2> points)
    {
        var rect = new OrientedRect();
        if (points == null || points.Count == 0) return rect;
        var hull = ConvexHull(points);
        if (hull.Count < 3)
        {
            Vector2 a = hull[0], b = hull[hull.Count - 1];
            Vector2 d = b - a;
            rect.centre = (a + b) * 0.5f;
            rect.axisU = d.sqrMagnitude > Eps ? d.normalized : Vector2.right;
            rect.axisV = Left(rect.axisU);
            rect.sizeU = d.magnitude;
            return rect;
        }

        Vector2 o = hull[0];
        float best = float.MaxValue;
        Vector2 bestU = Vector2.right;
        float bMinU = 0, bMaxU = 0, bMinV = 0, bMaxV = 0;
        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 edge = hull[(i + 1) % hull.Count] - hull[i];
            if (edge.sqrMagnitude < Eps) continue;
            Vector2 u = edge.normalized, v = Left(u);
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in hull)
            {
                Vector2 d = p - o;
                float du = Vector2.Dot(d, u), dv = Vector2.Dot(d, v);
                if (du < minU) minU = du;
                if (du > maxU) maxU = du;
                if (dv < minV) minV = dv;
                if (dv > maxV) maxV = dv;
            }
            float area = (maxU - minU) * (maxV - minV);
            if (area < best - 1e-4f)                                // strictly better: a tie keeps the first
            {
                best = area; bestU = u;
                bMinU = minU; bMaxU = maxU; bMinV = minV; bMaxV = maxV;
            }
        }

        Vector2 axisV = Left(bestU);
        rect.centre = o + bestU * ((bMinU + bMaxU) * 0.5f) + axisV * ((bMinV + bMaxV) * 0.5f);
        rect.axisU = bestU; rect.axisV = axisV;
        rect.sizeU = bMaxU - bMinU; rect.sizeV = bMaxV - bMinV;
        if (rect.sizeV > rect.sizeU)                                // make U the long side
        {
            rect.axisU = axisV; rect.axisV = -bestU;
            float t = rect.sizeU; rect.sizeU = rect.sizeV; rect.sizeV = t;
        }
        return rect;
    }

    // ------------------------------------------------------------------ lines

    /// <summary>How far a point is from a line, with the nearest point on it and the segment that holds it.</summary>
    public static float DistanceToPolyline(IList<Vector2> line, Vector2 p, out Vector2 closest, out int segment)
    {
        closest = line != null && line.Count > 0 ? line[0] : p;
        segment = 0;
        if (line == null || line.Count < 2) return Vector2.Distance(p, closest);

        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector2 a = line[i], ab = line[i + 1] - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < Eps ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            Vector2 c = a + ab * t;
            float d = (p - c).sqrMagnitude;
            if (d < best) { best = d; closest = c; segment = i; }
        }
        return Mathf.Sqrt(best);
    }

    public static float DistanceToPolyline(IList<Vector2> line, Vector2 p, out Vector2 closest) =>
        DistanceToPolyline(line, p, out closest, out _);

    // ------------------------------------------------------------------ triangles

    /// <summary>
    /// Fills an outline with triangles by ear clipping. Appends index triples into the ring, each counter-clockwise on
    /// the map. Returns false if the outline touches or crosses itself and an ear had to be forced to finish.
    /// </summary>
    public static bool Triangulate(IList<Vector2> ring, List<int> triangles)
    {
        int n = ring.Count;
        if (n < 3) return true;

        var idx = new List<int>(n);
        bool ccw = SignedArea(ring) >= 0f;
        for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);
        DropFlatCorners(ring, idx);

        bool clean = true;
        int start = 0;
        while (idx.Count > 3)
        {
            int m = idx.Count;
            bool clipped = false;
            for (int k = 0; k < m && !clipped; k++)
            {
                int i = (start + k) % m;
                int ip = idx[(i + m - 1) % m], ic = idx[i], inx = idx[(i + 1) % m];
                Vector2 a = ring[ip], b = ring[ic], c = ring[inx];
                if (Cross(b - a, c - b) <= Eps) continue;           // reflex or flat: not an ear
                bool blocked = false;
                for (int j = 0; j < m && !blocked; j++)
                {
                    int q = idx[j];
                    if (q == ip || q == ic || q == inx) continue;
                    blocked = StrictlyInside(a, b, c, ring[q]);
                }
                if (blocked) continue;

                triangles.Add(ip); triangles.Add(ic); triangles.Add(inx);
                idx.RemoveAt(i);
                start = (i + idx.Count - 1) % idx.Count;
                clipped = true;
            }
            if (clipped) continue;

            // No ear: the outline is not simple. Cut off the most convex corner so the loop still ends.
            clean = false;
            int best = 0;
            float bestCross = float.MinValue;
            for (int i = 0; i < m; i++)
            {
                Vector2 a = ring[idx[(i + m - 1) % m]], b = ring[idx[i]], c = ring[idx[(i + 1) % m]];
                float cr = Cross(b - a, c - b);
                if (cr > bestCross) { bestCross = cr; best = i; }
            }
            if (bestCross > Eps)
            {
                triangles.Add(idx[(best + m - 1) % m]); triangles.Add(idx[best]); triangles.Add(idx[(best + 1) % m]);
            }
            idx.RemoveAt(best);
            start = (best + idx.Count - 1) % idx.Count;
        }

        if (idx.Count == 3 && Cross(ring[idx[1]] - ring[idx[0]], ring[idx[2]] - ring[idx[1]]) > Eps)
        {
            triangles.Add(idx[0]); triangles.Add(idx[1]); triangles.Add(idx[2]);
        }
        return clean;
    }

    static void DropFlatCorners(IList<Vector2> ring, List<int> idx)
    {
        bool changed = true;
        while (changed && idx.Count > 3)
        {
            changed = false;
            for (int i = idx.Count - 1; i >= 0 && idx.Count > 3; i--)
            {
                Vector2 a = ring[idx[(i + idx.Count - 1) % idx.Count]], b = ring[idx[i]], c = ring[idx[(i + 1) % idx.Count]];
                Vector2 d1 = b - a, d2 = c - b;
                float l1 = d1.magnitude, l2 = d2.magnitude;
                if (l1 < 1e-4f || l2 < 1e-4f || Mathf.Abs(Cross(d1, d2)) <= 1e-4f * l1 * l2)
                {
                    idx.RemoveAt(i);
                    changed = true;
                }
            }
        }
    }

    static bool StrictlyInside(Vector2 a, Vector2 b, Vector2 c, Vector2 p) =>
        Cross(b - a, p - a) > Eps && Cross(c - b, p - b) > Eps && Cross(a - c, p - c) > Eps;

    // ------------------------------------------------------------------ meshes

    /// <summary>Vertices, normals, UVs and triangles being collected for one mesh. Switches to 32-bit indices when needed.</summary>
    public class MeshBuffers
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();

        public int VertexCount => vertices.Count;
        public bool IsEmpty => triangles.Count == 0;

        public int Add(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            vertices.Add(position);
            normals.Add(normal);
            uv.Add(texCoord);
            return vertices.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>A flat fill of an outline at one height, facing up. Returns false if the outline had to be forced.</summary>
    public static bool Fill(IList<Vector2> ring, float y, MeshBuffers into, float uvScale = 0.1f)
    {
        var tris = new List<int>();
        bool clean = Triangulate(ring, tris);
        int first = into.VertexCount;
        foreach (var p in ring) into.Add(new Vector3(p.x, y, p.y), Vector3.up, p * uvScale);
        // Triangulate gives map counter-clockwise triples; Unity faces point up for the opposite order.
        for (int i = 0; i < tris.Count; i += 3) into.Triangle(first + tris[i], first + tris[i + 2], first + tris[i + 1]);
        return clean;
    }

    /// <summary>Walls round an outline from baseY up to baseY + height, with a flat roof. Faces point outwards and up.</summary>
    public static bool Extrude(IList<Vector2> ring, float baseY, float height, MeshBuffers into)
    {
        if (ring == null || ring.Count < 3) return true;
        var r = CounterClockwise(ring);
        float top = baseY + height;

        for (int e = 0; e < r.Count; e++)
        {
            Vector2 p0 = r[e], p1 = r[(e + 1) % r.Count], d = p1 - p0;
            float len = d.magnitude;
            if (len < 1e-4f) continue;
            Vector3 normal = new Vector3(d.y, 0f, -d.x) / len;      // to the right of the way round: outwards
            int v0 = into.Add(new Vector3(p0.x, baseY, p0.y), normal, new Vector2(0f, 0f));
            int v1 = into.Add(new Vector3(p1.x, baseY, p1.y), normal, new Vector2(len, 0f));
            int v2 = into.Add(new Vector3(p1.x, top, p1.y), normal, new Vector2(len, height));
            int v3 = into.Add(new Vector3(p0.x, top, p0.y), normal, new Vector2(0f, height));
            into.Triangle(v0, v3, v2);
            into.Triangle(v0, v2, v1);
        }
        return Fill(r, top, into);
    }

    /// <summary>
    /// A flat strip along a line, facing up: a road or a stream. Corners are mitred, so the edges stay a constant
    /// distance from the line; a sharp turn is held to twice the half-width.
    /// </summary>
    public static void Ribbon(IList<Vector2> line, float halfWidth, float y, MeshBuffers into)
    {
        var pts = new List<Vector2>(line.Count);
        foreach (var p in line)
            if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude > 1e-6f) pts.Add(p);
        if (pts.Count < 2 || halfWidth <= 0f) return;

        int n = pts.Count;
        int first = into.VertexCount;
        float run = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 nPrev = Left((i > 0 ? pts[i] - pts[i - 1] : pts[1] - pts[0]).normalized);
            Vector2 nNext = i < n - 1 ? Left((pts[i + 1] - pts[i]).normalized) : nPrev;
            Vector2 m = nPrev + nNext;
            float len;
            if (m.sqrMagnitude < 1e-4f) { m = nPrev; len = halfWidth; }
            else
            {
                m.Normalize();
                len = Mathf.Min(halfWidth / Mathf.Max(Vector2.Dot(m, nPrev), 0.5f), halfWidth * 2f);
            }
            if (i > 0) run += Vector2.Distance(pts[i], pts[i - 1]);
            float v = run / (halfWidth * 2f);
            Vector2 left = pts[i] + m * len, right = pts[i] - m * len;
            into.Add(new Vector3(left.x, y, left.y), Vector3.up, new Vector2(0f, v));
            into.Add(new Vector3(right.x, y, right.y), Vector3.up, new Vector2(1f, v));
        }
        for (int i = 0; i + 1 < n; i++)
        {
            int l0 = first + 2 * i, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
            into.Triangle(r0, l1, r1);                              // up-facing order of the map-counter-clockwise (r0, r1, l1)
            into.Triangle(r0, l0, l1);
        }
    }

    // ------------------------------------------------------------------ clipping

    /// <summary>The parts of a line that lie inside a rectangle (Liang-Barsky). A line that leaves and returns is in pieces.</summary>
    public static List<List<Vector2>> ClipPolyline(IList<Vector2> line, Rect r)
    {
        var result = new List<List<Vector2>>();
        List<Vector2> run = null;
        for (int i = 0; line != null && i + 1 < line.Count; i++)
        {
            if (!ClipSegment(line[i], line[i + 1], r, out Vector2 a, out Vector2 b)) { run = null; continue; }
            if (run == null || (run[run.Count - 1] - a).sqrMagnitude > 1e-6f)
            {
                run = new List<Vector2> { a };
                result.Add(run);
            }
            run.Add(b);
        }
        return result;
    }

    static bool ClipSegment(Vector2 a, Vector2 b, Rect r, out Vector2 ca, out Vector2 cb)
    {
        float dx = b.x - a.x, dy = b.y - a.y, t0 = 0f, t1 = 1f;
        ca = a; cb = b;
        float[] p = { -dx, dx, -dy, dy };
        float[] q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Abs(p[i]) < 1e-9f)
            {
                if (q[i] < 0f) return false;                        // parallel to this edge and outside it
                continue;
            }
            float t = q[i] / p[i];
            if (p[i] < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
        }
        ca = new Vector2(a.x + t0 * dx, a.y + t0 * dy);
        cb = new Vector2(a.x + t1 * dx, a.y + t1 * dy);
        return (cb - ca).sqrMagnitude > 1e-10f;
    }

    /// <summary>An outline cut to a rectangle (Sutherland-Hodgman). Nothing inside gives an empty list.</summary>
    public static List<Vector2> ClipPolygon(IList<Vector2> ring, Rect r)
    {
        var current = new List<Vector2>(ring);
        for (int edge = 0; edge < 4 && current.Count > 0; edge++)
        {
            var next = new List<Vector2>(current.Count + 4);
            for (int i = 0, j = current.Count - 1; i < current.Count; j = i++)
            {
                Vector2 a = current[j], b = current[i];
                bool aIn = Inside(a, r, edge), bIn = Inside(b, r, edge);
                if (aIn != bIn) next.Add(Cut(a, b, r, edge));
                if (bIn) next.Add(b);
            }
            current = next;
        }
        return current;
    }

    static bool Inside(Vector2 p, Rect r, int edge)
    {
        switch (edge)
        {
            case 0:  return p.x >= r.xMin;
            case 1:  return p.x <= r.xMax;
            case 2:  return p.y >= r.yMin;
            default: return p.y <= r.yMax;
        }
    }

    static Vector2 Cut(Vector2 a, Vector2 b, Rect r, int edge)
    {
        float t;
        switch (edge)
        {
            case 0:  t = (r.xMin - a.x) / (b.x - a.x); break;
            case 1:  t = (r.xMax - a.x) / (b.x - a.x); break;
            case 2:  t = (r.yMin - a.y) / (b.y - a.y); break;
            default: t = (r.yMax - a.y) / (b.y - a.y); break;
        }
        return a + (b - a) * t;
    }

    // ------------------------------------------------------------------ the same answer every time

    /// <summary>
    /// A hash of a string that is the same on every machine and every run (FNV-1a over its UTF-8 bytes), unlike
    /// string.GetHashCode. A rebuilt map picks the same house for the same building.
    /// </summary>
    public static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            if (s != null)
                foreach (byte b in Encoding.UTF8.GetBytes(s))
                {
                    h ^= b;
                    h *= 16777619u;
                }
            return h;
        }
    }

    /// <summary>A repeatable number from 0 up to (not including) 1 for a string.</summary>
    public static float Unit(string s) => (StableHash(s) >> 8) / 16777216f;
}
