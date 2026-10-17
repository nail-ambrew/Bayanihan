using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared set-up for the tests that play the real Gameplay scene: loading it, waiting without hanging forever, and
/// doing what the player would do - pick somebody up, climb aboard, step off - without needing a keyboard.
/// </summary>
public static class GameplayTestHelpers
{
    public const string SceneName = "Gameplay";

    public static IEnumerator LoadGameplay()
    {
        Application.runInBackground = true;     // keep simulating if the Editor is not the front window
        Time.timeScale = 1f;
        yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        yield return null;                      // a frame for every Start()
    }

    /// <summary>Game time. Stops advancing once a mission ends and the report pauses the game.</summary>
    public static IEnumerator WaitSeconds(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }

    /// <summary>Waits for something to become true, in real time, and fails the test rather than hanging if it never does.</summary>
    public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
    {
        float end = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out after " + timeoutSeconds + " s waiting for " + what);
            yield return null;
        }
    }

    // ------------------------------------------------------------------ doing what the player does
    /// <summary>Calls a method that is private in the game because only input is meant to reach it.</summary>
    public static object Invoke(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(m, target.GetType().Name + "." + method + " no longer exists - update the test that calls it.");
        return m.Invoke(target, args);
    }

    /// <summary>What pressing E does once somebody is in reach and the boat has slowed: they come aboard.</summary>
    public static void PickUp(SimpleBoat boat, Survivor survivor) => Invoke(boat, "PickUp", survivor);

    /// <summary>The player climbs aboard and takes the helm (E, from the water beside the boat).</summary>
    public static void Board(PlayerRescuer player) => Invoke(player, "Mount");

    /// <summary>The player steps off the boat into the water.</summary>
    public static void StepOff(PlayerRescuer player) => Invoke(player, "Dismount");

    /// <summary>
    /// Takes somebody aboard the way the game ends up doing it: a line or a swimmer has brought them alongside (so a
    /// roof or a debris jam is no obstacle), and the crew haul them in.
    /// </summary>
    public static IEnumerator TakeAboard(SimpleBoat boat, Survivor survivor, float side = 1f)
    {
        if (survivor.RequiresLine) survivor.LineDelivered = true;
        survivor.InLiveWire = false;                // alongside the boat, they are clear of any wire they were waiting in
        BringAlongside(survivor, boat, side);
        PickUp(boat, survivor);
        yield return WaitUntil(() => !survivor.gameObject.activeSelf, 8f, survivor.name + " to be hauled aboard");
    }

    /// <summary>Puts the boat in the middle of the Barangay Hall's drop-off zone, slowly enough to let people off.</summary>
    public static void ArriveAtTheHall(SimpleBoat boat)
    {
        var evac = UnityEngine.Object.FindFirstObjectByType<EvacuationCenter>();
        Assert.IsNotNull(evac, "the scene has no evacuation centre");
        Vector3 hall = evac.GetComponent<Collider>().bounds.center;
        hall.y = boat.transform.position.y;
        Teleport(boat, hall, Vector3.left);
    }

    /// <summary>Winds the mission clock to this many seconds left.</summary>
    public static void SetTimeLeft(float seconds)
    {
        var field = typeof(RescueManager).GetField("timeLeft", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "RescueManager.timeLeft no longer exists - update the test helper that winds the clock.");
        field.SetValue(RescueManager.Instance, seconds);
    }

    // ------------------------------------------------------------------ finding and placing things

    /// <summary>
    /// Takes out what the mission put in the scene - its residents, the live wire, the jam and the wreckage - so a test
    /// starts with open water and sets up exactly what it needs. The scene's own mission is covered by the tests that
    /// play it.
    /// </summary>
    public static void ClearTheStage()
    {
        foreach (var s in Survivor.All.ToArray()) Remove(s.gameObject);
        foreach (var z in LiveWireZone.All.ToArray()) Remove(z.gameObject);
        foreach (var j in DebrisBarrier.All.ToArray()) Remove(j.gameObject);
        foreach (var f in UnityEngine.Object.FindObjectsByType<DebrisField>(FindObjectsSortMode.None)) f.Clear();
        foreach (var d in FloatingDebris.All.ToArray()) Remove(d.gameObject);
        Physics.SyncTransforms();
    }

    static void Remove(GameObject go)
    {
        go.SetActive(false);                        // gone from the physics world and the registries now, not at the end of the frame
        UnityEngine.Object.Destroy(go);
    }

    /// <summary>A resident the scene has in this situation and with exactly these needs.</summary>
    public static Survivor ResidentWhere(Situation situation, Need needs)
    {
        foreach (var s in UnityEngine.Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None))
            if (!s.IsRescued && s.situation == situation && s.needs == needs) return s;
        Assert.Fail("The Gameplay scene has no resident waiting who is " + situation + " with needs = " + needs);
        return null;
    }
    public static SimpleBoat Boat() => UnityEngine.Object.FindFirstObjectByType<SimpleBoat>();
    public static PlayerRescuer Player() => UnityEngine.Object.FindFirstObjectByType<PlayerRescuer>();

    /// <summary>A resident who has not been rescued yet and needs exactly this (Need.None = nothing special).</summary>
    public static Survivor ResidentNeeding(Need needs)
    {
        foreach (var s in UnityEngine.Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None))
            if (!s.IsRescued && s.needs == needs) return s;
        Assert.Fail("The Gameplay scene has no resident waiting with needs = " + needs);
        return null;
    }

    /// <summary>Every resident still waiting, in a fixed order so a run is repeatable.</summary>
    public static Survivor[] Waiting()
    {
        var all = UnityEngine.Object.FindObjectsByType<Survivor>(FindObjectsSortMode.None);
        var waiting = Array.FindAll(all, s => !s.IsRescued);
        Array.Sort(waiting, (a, b) => string.CompareOrdinal(a.name, b.name));
        return waiting;
    }

    /// <summary>A resident of the given kind, put into the world right now.</summary>
    public static Survivor MakeResident(string name, Situation situation, Need needs, Vector3 at,
                                        CharacterCategory category = CharacterCategory.Adult, string household = "Test",
                                        Quaternion? facing = null)
    {
        var spec = new ResidentSpec
        {
            id = name.ToLowerInvariant(),
            displayName = name + " " + household,
            household = household,
            category = category,
            situation = situation,
            needs = needs
        };
        return ResidentSpawner.Spawn(spec, at, facing ?? Quaternion.identity);
    }

    /// <summary>Stills the flood so a test that stages a rescue is not carried off while it waits.</summary>
    public static void StillWater()
    {
        if (FloodCurrent.Instance != null) FloodCurrent.Instance.speed = 0f;
    }

    /// <summary>Puts somebody in the water beside the boat, a few metres off (side +1 or -1 = one side or the other).</summary>
    public static void BringAlongside(Survivor survivor, SimpleBoat boat, float side)
    {
        Vector3 across = Vector3.Cross(Vector3.up, boat.HeadingForward).normalized;
        survivor.transform.position = boat.transform.position + across * (3f * Mathf.Sign(side));
        Physics.SyncTransforms();
    }

    /// <summary>Puts the boat somewhere, pointing somewhere, at rest.</summary>
    public static void Teleport(SimpleBoat boat, Vector3 position, Vector3 heading)
    {
        // Which end of this boat is the bow is SimpleBoat's business (Bow Direction), so aim from there.
        Quaternion rotation = Quaternion.LookRotation(heading) * Quaternion.Inverse(Quaternion.LookRotation(boat.LocalBow));
        var body = boat.GetComponent<Rigidbody>();
        body.position = position;
        body.rotation = rotation;
        boat.transform.SetPositionAndRotation(position, rotation);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    /// <summary>-1 or +1 for the side of the boat's centreline a point (in the boat's local space) is on.</summary>
    public static int SideOf(SimpleBoat boat, Vector3 local)
    {
        Vector3 across = Vector3.Cross(Vector3.up, boat.LocalBow);
        var box = boat.GetComponent<BoxCollider>();
        float centre = box != null ? Vector3.Dot(box.center, across) : 0f;
        return Vector3.Dot(local, across) - centre >= 0f ? 1 : -1;
    }
}
