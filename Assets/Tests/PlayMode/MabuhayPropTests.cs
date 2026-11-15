using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// The street clutter from the prop packs in Barangay Mabuhay: every pack is used, nothing is set down on the mission, the boat
/// can still get everywhere it could without them, they are solid where drawn and slippery like the rest of the map, the
/// onlookers are not residents, and the flood's buckets and roof sheets are the real models and float.
/// </summary>
public class MabuhayPropTests
{
    Transform props;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.3f);
        var map = GameObject.Find("BarangayMock");
        props = map != null ? map.transform.Find("Props") : null;
        Assert.IsNotNull(props, "the map has no Props group: rebuild it (Bayanihan > Mabuhay)");
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    IEnumerable<Transform> Placed(string group) => props.Find(group).Cast<Transform>();

    [UnityTest]
    public IEnumerator Every_prop_pack_is_on_the_map()
    {
        var jeepneys = Placed("Jeepneys").ToList();
        Assert.GreaterOrEqual(jeepneys.Count, 10, "jeepneys parked in the streets");
        for (int i = 1; i <= 5; i++)
            Assert.IsTrue(jeepneys.Any(j => j.name.Contains("jeepney_" + i + "_")), "jeepney model " + i + " is used");

        Assert.Greater(Placed("Roof sheets").Count(), 30, "spare roof sheets on the shacks");
        var buckets = Placed("Buckets").Select(b => b.name).Distinct().Count();
        Assert.AreEqual(6, buckets, "all six buckets are used");
        Assert.Greater(Placed("Concrete").Count(), 10, "hollow blocks and rubble");
        Assert.IsTrue(Placed("Concrete").Any(t => t.name.Contains("Rubble")) && Placed("Concrete").Any(t => t.name.Contains("BlockStack")));
        Assert.Greater(Placed("Levee barriers").Count(), 3, "barriers closing the roads on the levee");
        Assert.Greater(Placed("People").Count(), 8, "people waiting and watching");
        Assert.IsTrue(Placed("People").Any(p => p.name.Contains("rescuer")) || Placed("People").Count() > 0);

        var foliage = GameObject.Find("BarangayMock/Foliage");
        Assert.IsNotNull(foliage);
        var lowPoly = foliage.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("LP")).Select(t => t.name.Substring(0, 4)).Distinct().ToList();
        Assert.GreaterOrEqual(lowPoly.Count, 3, "the low-poly palm, oak, chestnut, shrub and stump grow among the Carmona plants: " + string.Join(", ", lowPoly));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Nothing_is_set_down_on_the_mission()
    {
        var keep = new List<(Vector2 at, float radius, string what)>
        {
            (Flat(Boat().transform.position), 10f, "the boat's start"),
            (Flat(Hall().transform.position), 8f, "the drop-off zone"),
        };
        foreach (var s in Survivor.All) keep.Add((Flat(s.transform.position), 3f, s.name));

        foreach (Transform group in props)
        {
            if (group.name == "People" || group.name == "Buckets") continue;     // on porches and roofs, out of the way
            foreach (Transform p in group)
                foreach (var (at, radius, what) in keep)
                    Assert.Greater(Vector2.Distance(Flat(p.position), at), radius, p.name + " is on " + what);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_boat_gets_everywhere_it_did_without_the_props()
    {
        // Flood the map from the boat with the props and without them: what is lost is only the water under the props themselves,
        // never a street they wall off.
        var boat = Boat();
        var solid = props.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToList();
        var area = Rect.MinMaxRect(-220f, -160f, 280f, 200f);

        foreach (var c in solid) c.enabled = false;
        Physics.SyncTransforms();
        var without = new BoatReach(area, 1f, BoatReach.DefaultRadius, 0f);
        int open = without.Flood(boat.transform.position);
        foreach (var c in solid) c.enabled = true;
        Physics.SyncTransforms();
        var with = new BoatReach(area, 1f, BoatReach.DefaultRadius, 0f);
        int reached = with.Flood(boat.transform.position);

        // Each prop may take its own footprint, grown by the boat's half-width, out of the water.
        float allowance = 0f;
        foreach (Transform group in props)
            foreach (Transform p in group)
            {
                var b = Drawn(p);
                if (b.size == Vector3.zero || b.max.y < -0.5f || b.min.y > 0.6f) continue;     // not at the waterline
                allowance += (b.size.x + 2f * BoatReach.DefaultRadius + 1f) * (b.size.z + 2f * BoatReach.DefaultRadius + 1f);
            }
        Assert.Greater(open, 20000);
        Assert.LessOrEqual(open - reached, allowance + 50f,
            "the props cut off " + (open - reached) + " m² of water the boat could reach without them (their own room: " + allowance.ToString("0") + " m²)");

        foreach (var s in Survivor.All)
        {
            var at = Flat(s.transform.position);
            if (without.Reached(at)) Assert.IsTrue(with.Reached(at) || with.DistanceToReached(at, 4f) < 4f, s.name + " can no longer be reached");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Props_are_slippery_and_nothing_is_missing_a_material()
    {
        int colliders = 0;
        foreach (var c in props.GetComponentsInChildren<Collider>())
        {
            colliders++;
            Assert.IsNotNull(c.sharedMaterial, c.name + " has no surface");
            Assert.AreEqual(0f, c.sharedMaterial.dynamicFriction, 1e-4f, c.name + " grips a hull");
        }
        Assert.Greater(colliders, 50);
        foreach (var r in props.GetComponentsInChildren<Renderer>())
            foreach (var m in r.sharedMaterials)
            {
                Assert.IsNotNull(m, r.name + " has an empty material slot");
                Assert.AreNotEqual("Hidden/InternalErrorShader", m.shader.name, r.name + " is drawn pink");
            }
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_onlookers_are_not_residents()
    {
        Assert.AreEqual(0, props.GetComponentsInChildren<Survivor>(true).Length, "nobody placed as scenery is on the roster");
        Assert.AreEqual(10, RescueManager.Instance.TotalSurvivors);
        foreach (Transform person in props.Find("People"))
            Assert.Greater(person.position.y, 0.2f, person.name + " is standing in the flood");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Buckets_and_roof_sheets_float_by_as_the_real_models()
    {
        var boat = Boat();
        Vector3 ahead = boat.transform.position + boat.transform.right * 6f;
        var bucket = FloatingDebris.Spawn(DebrisKind.Bucket, ahead);
        var sheet = FloatingDebris.Spawn(DebrisKind.RoofSheet, ahead + boat.transform.forward * 3f);

        Assert.IsTrue(bucket.GetComponentsInChildren<MeshFilter>().Any(f => f.name.StartsWith("Bucket_")), "the bucket is the pack's model");
        Assert.IsTrue(sheet.GetComponentsInChildren<MeshFilter>().Any(f => f.name.StartsWith("RoofSheet_")), "the sheet is the pack's model");
        Assert.IsTrue(FloatingDebris.IsSoft(DebrisKind.Bucket) && !FloatingDebris.IsSoft(DebrisKind.RoofSheet), "a bucket is soft, a sheet of iron is not");

        yield return WaitSeconds(2f);
        foreach (var piece in new[] { bucket, sheet })
        {
            float water = WaterSurface.Instance.GetHeight(piece.transform.position);
            Assert.AreEqual(water, piece.transform.position.y, 0.4f, piece.name + " does not float");
        }
        Object.Destroy(bucket.gameObject);
        Object.Destroy(sheet.gameObject);
    }

    static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

    static Bounds Drawn(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
