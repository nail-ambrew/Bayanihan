using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// Mabuhay's landmarks where they really are and square to their roads: The Feast Carmona on Hebron Street by the school,
/// the Iglesia ni Cristo facing Purificacion Street across its forecourt, and no landmark standing in a street.
/// </summary>
public class MabuhayLandmarkTests
{
    Transform landmarks;
    BarangayLayout layout;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.3f);
        landmarks = GameObject.Find("BarangayMock").transform.Find("Landmarks");
        layout = BarangayLayout.LoadShipped();
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    Transform Model(string name) => landmarks.Cast<Transform>().FirstOrDefault(t => t.name.Contains("(" + name + ")"));

    /// <summary>The direction of the named road where it passes nearest the point, and how far its centre line is.</summary>
    float Road(string name, Vector2 p, out Vector2 along)
    {
        along = Vector2.right;
        float best = float.MaxValue;
        foreach (var r in layout.roads.Where(r => r.name == name))
            for (int i = 0; i + 1 < r.points.Count; i++)
            {
                Vector2 a = r.points[i], ab = r.points[i + 1] - a;
                if (ab.sqrMagnitude < 1e-6f) continue;
                var c = a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, c);
                if (d < best) { best = d; along = ab.normalized; }
            }
        return best;
    }

    static Vector2 Front(Transform t) => new Vector2(t.forward.x, t.forward.z).normalized;

    [UnityTest]
    public IEnumerator The_Feast_Carmona_is_on_Hebron_Street_by_the_school_and_square_to_it()
    {
        var feast = Model("LM_FeastCarmonaChurch");
        Assert.IsNotNull(feast, "The Feast Carmona is on the map");
        var at = new Vector2(feast.position.x, feast.position.z);
        // The owner's spot (layout -16.1, -132.1, from Google Maps): the church stands there, set back from the street.
        Assert.Less(Vector2.Distance(at, new Vector2(-16.1f, -132.1f)), 12f, "near the spot given for it");
        float toHebron = Road("Hebron Street", at, out var along);
        Assert.Less(toHebron, 25f, "on Hebron Street");
        Assert.Less(Mathf.Abs(Vector2.Dot(Front(feast), along)), Mathf.Sin(5f * Mathf.Deg2Rad), "its front is parallel to Hebron Street");
        var school = layout.landmarks.First(l => l.name == "Mabuhay Elementary School");
        Assert.Less(Vector2.Distance(at, school.position), 120f, "near Mabuhay Elementary School");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_Iglesia_ni_Cristo_faces_Purificacion_Street_across_its_forecourt()
    {
        var inc = Model("LM_IglesiaNiCristo");
        Assert.IsNotNull(inc, "the Iglesia ni Cristo is on the map");
        var at = new Vector2(inc.position.x, inc.position.z);
        Road("Purificacion Street", at, out var along);
        Assert.Less(Mathf.Abs(Vector2.Dot(Front(inc), along)), Mathf.Sin(5f * Mathf.Deg2Rad), "its front is parallel to Purificacion Street");
        // The front looks at the street: a step ahead of the church is nearer Purificacion than its middle is.
        float now = Road("Purificacion Street", at, out _), ahead = Road("Purificacion Street", at + Front(inc) * 5f, out _);
        Assert.Less(ahead, now, "its entrance faces Purificacion Street");
        // Nothing but the forecourt between its front and the street: no row house there.
        var units = MapGroups.Houses(GameObject.Find("BarangayMock").transform).Where(t => t.name.StartsWith("House row "));
        foreach (var u in units)
        {
            var d = new Vector2(u.position.x, u.position.z) - at;
            float forward = Vector2.Dot(d, Front(inc)), sideways = Mathf.Abs(Vector2.Dot(d, new Vector2(Front(inc).y, -Front(inc).x)));
            Assert.IsFalse(forward > 0f && forward < now && sideways < 15f, u.name + " stands in the church's forecourt");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator No_landmark_stands_in_a_street()
    {
        foreach (Transform t in landmarks)
        {
            if (!t.name.Contains("(LM_")) continue;
            var hits = Physics.RaycastAll(new Vector3(t.position.x, 60f, t.position.z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            Assert.IsFalse(hits.Any(h => h.collider.name.StartsWith("road_")), t.name + " stands on a street");
        }
        yield return null;
    }
}
