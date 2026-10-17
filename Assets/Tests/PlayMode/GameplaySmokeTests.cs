using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Loads the real Gameplay scene and lets it run. Any error or exception logged while it does fails the test - and
/// that is the point: these are the "it broke on load" and "the level cannot be finished" mistakes that are
/// otherwise found by whoever plays it next.
/// </summary>
public class GameplaySmokeTests
{
    const string SceneName = "Gameplay";

    [UnitySetUp]
    public IEnumerator LoadGameplay()
    {
        Application.runInBackground = true;     // keep simulating if the Editor is not the front window
        Time.timeScale = 1f;
        yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        yield return null;                      // a frame for every Start()
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        Time.timeScale = 1f;                    // a finished mission leaves the game paused
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scene_runs_for_a_few_seconds_without_errors()
    {
        yield return WaitSeconds(3f);

        Assert.IsNotNull(WaterSurface.Instance, "no WaterSurface in the scene");
        Assert.IsNotNull(RescueManager.Instance, "no RescueManager in the scene");
        Assert.Greater(RescueManager.Instance.TotalSurvivors, 0, "nobody to rescue");
        Assert.IsNotNull(Object.FindFirstObjectByType<SimpleBoat>(), "no boat");
        Assert.IsNotNull(Object.FindFirstObjectByType<PlayerRescuer>(), "no player");
        Assert.IsFalse(RescueManager.Instance.GameOver, "the mission ended by itself");
    }

    [UnityTest]
    public IEnumerator Boat_fits_in_the_drop_off_it_has_to_reach()
    {
        yield return new WaitForFixedUpdate();

        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        var boat = Object.FindFirstObjectByType<SimpleBoat>();
        Assert.IsNotNull(evac, "no EvacuationCenter in the scene");
        Assert.IsNotNull(boat, "no boat");

        // The boat unloads once its centre is well inside the zone, so its hull has to fit at the zone's centre.
        Bounds zone = evac.GetComponent<Collider>().bounds;
        Vector3 centre = zone.center;
        centre.y = WaterSurface.Instance.transform.position.y + 0.2f;

        var hullBox = boat.Hull as BoxCollider;
        Vector3 half = hullBox != null ? hullBox.size * 0.5f : new Vector3(1.3f, 0.15f, 2.4f);

        foreach (var hit in Physics.OverlapBox(centre, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            bool floats = hit.attachedRigidbody != null;      // the boat itself, the swimmer, debris
            Assert.IsTrue(floats, "'" + hit.name + "' is in the way: the boat cannot get into the drop-off zone");
        }
    }

    [UnityTest]
    public IEnumerator Radio_has_somebody_left_to_find_at_the_start()
    {
        yield return WaitSeconds(1f);

        int found = 0, total = 0;
        foreach (var s in Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None))
        {
            total++;
            if (s.Discovered) found++;
        }

        Assert.Greater(total, 0);
        Assert.Less(found, total,
            "every resident already counts as found before the boat has moved, so the Radyo has nobody to point at");
    }

    [UnityTest]
    public IEnumerator Water_is_centred_on_whoever_is_looking()
    {
        yield return WaitSeconds(0.5f);

        var water = WaterSurface.Instance;
        var cam = Camera.main;
        Assert.IsNotNull(cam, "no Main Camera");

        Vector3 offset = water.transform.position - cam.transform.position;
        offset.y = 0f;
        Assert.Less(offset.magnitude, water.size * 0.25f, "the animated water has been left behind by the camera");
    }

    static IEnumerator WaitSeconds(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }
}
