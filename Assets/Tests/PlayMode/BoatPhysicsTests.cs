using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>How the boat sits in and moves with the water, checked in the real Gameplay scene.</summary>
public class BoatPhysicsTests
{
    [UnitySetUp]
    public IEnumerator LoadGameplay()
    {
        Application.runInBackground = true;
        Time.timeScale = 1f;
        yield return SceneManager.LoadSceneAsync("Gameplay", LoadSceneMode.Single);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator Heavier_hull_floats_lower()
    {
        float light = 0f, heavy = 0f;
        yield return AverageFloatHeight(100f, h => light = h);
        yield return AverageFloatHeight(180f, h => heavy = h);

        Assert.Less(heavy, light - 0.03f,
            "a hull loaded to 1.8x its design mass should ride visibly lower (light " + light + ", heavy " + heavy + ")");
    }

    /// <summary>Drops a test float designed for 100 kg, lets it settle, and averages how high it rides.</summary>
    static IEnumerator AverageFloatHeight(float mass, System.Action<float> result)
    {
        var go = new GameObject("Test float " + mass);
        go.transform.position = new Vector3(0f, 0.3f, 60f);     // open street, away from the boat
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 0.3f, 4f);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        var buoyancy = go.AddComponent<Buoyancy>();
        buoyancy.SetDesignMass(100f);

        yield return WaitSeconds(3f);                           // settle

        float sum = 0f;
        int samples = 0;
        float end = Time.time + 4f;
        while (Time.time < end)
        {
            sum += go.transform.position.y - WaterSurface.Instance.GetHeight(go.transform.position);
            samples++;
            yield return new WaitForFixedUpdate();
        }

        Object.Destroy(go);
        result(sum / Mathf.Max(samples, 1));
    }

    [UnityTest]
    public IEnumerator Boat_drifts_with_the_current_when_the_player_is_at_the_helm_with_the_throttle_shut()
    {
        Assert.IsNotNull(FloodCurrent.Instance, "the Gameplay scene has no FloodCurrent");
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        Vector3 downstream = FloodCurrent.Instance.Heading;

        yield return WaitSeconds(1f);
        boat.IsPlayerAboard = true;         // no keys are pressed, so this is a boat left to itself
        Vector3 start = boat.transform.position;
        yield return WaitSeconds(5f);

        float carried = Vector3.Dot(boat.transform.position - start, downstream);
        Assert.Greater(carried, 0.5f, "an idle boat should be carried downstream");
    }

    [UnityTest]
    public IEnumerator Crew_hold_the_boat_in_place_while_the_player_is_away()
    {
        var boat = Object.FindFirstObjectByType<SimpleBoat>();

        yield return WaitSeconds(1f);
        Assert.IsFalse(boat.IsPlayerAboard, "the player starts in the water");
        Vector3 start = boat.transform.position;
        yield return WaitSeconds(5f);

        Vector3 moved = boat.transform.position - start;
        moved.y = 0f;
        Assert.Less(moved.magnitude, 0.75f, "with a crew member at the helm the boat should stay where it was left");
    }

    static IEnumerator WaitSeconds(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }
}
