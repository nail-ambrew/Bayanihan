using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// Barangay Mabuhay's houses and shops in continuous rows (RowBuilder): mostly one storey with taller ones now and then,
/// none overlapping another, none standing in a street, and WalterMart and Macaria Business Center left as they are.
/// </summary>
public class MabuhayRowsTests
{
    const string Prefix = "House row ";
    List<Transform> units;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.3f);
        units = MapGroups.Houses(GameObject.Find("BarangayMock").transform).Where(t => t.name.StartsWith(Prefix)).ToList();
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    /// <summary>A unit's footprint (its collider mesh's box, turned with it) and its height.</summary>
    static bool Footprint(Transform u, out Vector2 centre, out Vector2 half, out Vector2 right, out Vector2 fwd, out float height)
    {
        centre = half = right = fwd = Vector2.zero; height = 0f;
        var mc = u.GetComponentInChildren<MeshCollider>();
        if (mc == null || mc.sharedMesh == null) return false;
        var b = mc.sharedMesh.bounds;
        var t = mc.transform;
        var c = t.TransformPoint(b.center);
        centre = new Vector2(c.x, c.z);
        var r3 = t.TransformVector(new Vector3(b.extents.x, 0f, 0f));
        var f3 = t.TransformVector(new Vector3(0f, 0f, b.extents.z));
        half = new Vector2(new Vector2(r3.x, r3.z).magnitude, new Vector2(f3.x, f3.z).magnitude);
        right = new Vector2(r3.x, r3.z).normalized;
        fwd = new Vector2(f3.x, f3.z).normalized;
        height = b.size.y * Mathf.Abs(t.lossyScale.y);
        return true;
    }

    [UnityTest]
    public IEnumerator Mostly_one_storey_with_taller_ones_now_and_then()
    {
        int one = 0, two = 0, three = 0, taller = 0;
        foreach (var u in units)
        {
            if (!Footprint(u, out _, out _, out _, out _, out float h)) continue;
            if (h <= 4f) one++; else if (h <= 6.5f) two++; else if (h <= 10f) three++; else taller++;
        }
        int total = one + two + three + taller;
        Assert.Greater(total, 3000, "the barangay is packed with houses and shops");
        Assert.AreEqual(0, taller, "nothing over three storeys");
        float p1 = one / (float)total, p2 = two / (float)total, p3 = three / (float)total;
        string mix = (p1 * 100f).ToString("0.0") + " / " + (p2 * 100f).ToString("0.0") + " / " + (p3 * 100f).ToString("0.0") + " %";
        Assert.That(p1, Is.InRange(0.70f, 0.80f), "one-storey share, " + mix);
        Assert.That(p2, Is.InRange(0.15f, 0.20f), "two-storey share, " + mix);
        Assert.That(p3, Is.InRange(0.01f, 0.10f), "three-storey share, " + mix);
        yield return null;
    }

    [UnityTest]
    public IEnumerator No_house_overlaps_another()
    {
        // Their footprints may touch (shared walls); one may not stand more than 30 cm inside another (the half-metre grid
        // the rows are planned on).
        var boxes = new List<(Vector2 c, Vector2 h, Vector2 r, Vector2 f, string name)>();
        foreach (var u in units)
            if (Footprint(u, out var c, out var h, out var r, out var f, out _)) boxes.Add((c, h - Vector2.one * 0.3f, r, f, u.name));
        var cells = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < boxes.Count; i++)
        {
            var k = new Vector2Int(Mathf.FloorToInt(boxes[i].c.x / 10f), Mathf.FloorToInt(boxes[i].c.y / 10f));
            if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
            l.Add(i);
        }
        var clashes = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
        {
            var k = new Vector2Int(Mathf.FloorToInt(boxes[i].c.x / 10f), Mathf.FloorToInt(boxes[i].c.y / 10f));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!cells.TryGetValue(k + new Vector2Int(dx, dy), out var l)) continue;
                foreach (int j in l)
                    if (j > i && Overlap(boxes[i], boxes[j]) && clashes.Count < 10) clashes.Add(boxes[i].name + " / " + boxes[j].name);
            }
        }
        Assert.IsEmpty(clashes, "houses standing inside each other: " + string.Join(", ", clashes));
        yield return null;
    }

    static bool Overlap((Vector2 c, Vector2 h, Vector2 r, Vector2 f, string n) a, (Vector2 c, Vector2 h, Vector2 r, Vector2 f, string n) b)
    {
        // Separating axes of two turned rectangles.
        foreach (var axis in new[] { a.r, a.f, b.r, b.f })
        {
            float ra = a.h.x * Mathf.Abs(Vector2.Dot(a.r, axis)) + a.h.y * Mathf.Abs(Vector2.Dot(a.f, axis));
            float rb = b.h.x * Mathf.Abs(Vector2.Dot(b.r, axis)) + b.h.y * Mathf.Abs(Vector2.Dot(b.f, axis));
            if (Mathf.Abs(Vector2.Dot(b.c - a.c, axis)) > ra + rb) return false;
        }
        return true;
    }

    [UnityTest]
    public IEnumerator No_house_stands_in_a_street()
    {
        // Under each corner of a house (30 cm in from its walls) there is no street surface: the rows start at the kerb.
        var inStreet = new List<string>();
        foreach (var u in units)
        {
            if (!Footprint(u, out var c, out var h, out var r, out var f, out _)) continue;
            foreach (var s in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
            {
                var p = c + r * s.x * (h.x - 0.3f) + f * s.y * (h.y - 0.3f);
                var hits = Physics.RaycastAll(new Vector3(p.x, 40f, p.y), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
                if (hits.Any(x => x.collider.name.StartsWith("road_")))
                {
                    if (inStreet.Count < 10) inStreet.Add(u.name);
                    break;
                }
            }
        }
        Assert.IsEmpty(inStreet, "houses standing on a street: " + string.Join(", ", inStreet));
        yield return null;
    }

    [UnityTest]
    public IEnumerator WalterMart_and_Macaria_are_left_as_they_are()
    {
        var layout = BarangayLayout.LoadShipped();
        foreach (var (name, id) in new[] { ("WalterMart", "r10749166"), ("Macaria Business Center", "r10749164") })
        {
            var rings = layout.areas.Where(a => a.osmId == id).Select(a => a.outline)
                                    .Concat(layout.landmarks.Where(l => l.id == id).Select(l => l.footprint))
                                    .Where(r => r != null && r.Count >= 3).ToList();
            Assert.IsNotEmpty(rings, name + " is on the layout");
            foreach (var u in units)
            {
                if (!Footprint(u, out var c, out _, out _, out _, out _)) continue;
                foreach (var ring in rings)
                    Assert.IsFalse(MapGeometry.Contains(ring, c), u.name + " stands inside " + name);
            }
        }
        yield return null;
    }
}
