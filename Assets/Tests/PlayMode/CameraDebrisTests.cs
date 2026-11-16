using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// The boat's chase camera and the wreckage in the flood: the camera steps in front of walls that come between it and the
/// boat, but floating debris is not a wall. Played in the real Gameplay scene.
/// </summary>
public class CameraDebrisTests
{
    SimpleBoat boat;
    PlayerRescuer player;

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();
        ClearTheStage();
        StillWater();
        boat = Boat();
        player = Player();
        Teleport(boat, new Vector3(-30f, 0f, -6.5f), Vector3.left);
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator Debris_bumped_or_under_the_boat_does_not_pull_the_camera_in()
    {
        // The chase camera steps in front of walls that come between it and the boat. Wreckage is not a wall: a crate the boat
        // nudges aside, or a sheet drifting under the stern, must leave the view where it is.
        Board(player);
        yield return WaitSeconds(3f);                               // the blend to the boat's camera, and the camera settling
        var cam = Camera.main.transform;
        float Away() => Vector3.Distance(cam.position, boat.transform.position);
        float settled = Away();

        // Between the boat and the camera, along the water, and right under the stern.
        Vector3 back = cam.position - boat.transform.position;
        back.y = 0f;
        back.Normalize();
        var pieces = new List<FloatingDebris>();
        foreach (float d in new[] { 1.5f, 3f, 4.2f, 5.4f, 6.6f })
            pieces.Add(FloatingDebris.Spawn(d < 2f ? DebrisKind.RoofSheet : DebrisKind.Crate, boat.transform.position + back * d));
        Assert.Greater(FloatingDebris.Layer, 0, "the project has a Debris layer");
        foreach (var piece in pieces)
            foreach (var t in piece.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(FloatingDebris.Layer, t.gameObject.layer, t.name + " is not on the debris layer");

        float closest = settled;
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            closest = Mathf.Min(closest, Away());
            yield return null;
        }
        Assert.Greater(closest, settled - 0.4f, "the camera pulled in to " + closest.ToString("0.0") + " m from " + settled.ToString("0.0") + " m when debris came between it and the boat");

        // The same pieces on the ordinary layer would have pulled the camera in: the test puts them where it matters.
        foreach (var piece in pieces)
            foreach (var t in piece.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        float pulled = settled;
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            pulled = Mathf.Min(pulled, Away());
            yield return null;
        }
        Assert.Less(pulled, settled - 0.4f, "debris on the default layer did not come between the camera and the boat, so this test proves nothing");

        foreach (var piece in pieces) Object.Destroy(piece.gameObject);
    }
}
