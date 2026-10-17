using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// What the flood puts in the way: a downed wire that shocks anyone in it and has to be cut by radio, wreckage
/// afloat that the boat must not ram, a jam of debris across the street that stops the boat but not a swimmer, and
/// water that keeps rising. Played in the real Gameplay scene.
/// </summary>
public class HazardTests
{
    SimpleBoat boat;
    PlayerRescuer player;
    BoatTools tools;
    BoatRescue rescue;
    Rigidbody swimmerBody;

    static readonly Vector3 West = Vector3.left;
    static readonly Vector3 StreetSpot = new Vector3(-30f, 0f, -6.5f);

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();
        ClearTheStage();
        StillWater();
        boat = Boat();
        player = Player();
        tools = boat.GetComponent<BoatTools>();
        rescue = boat.GetComponent<BoatRescue>();
        swimmerBody = player.GetComponent<Rigidbody>();
        Assert.IsNotNull(tools, "the boat has no BoatTools");
        Teleport(boat, At(StreetSpot), West);
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>A point on the boat's own level, over this spot (the boat floats a little above y = 0).</summary>
    Vector3 At(Vector3 spot) => new Vector3(spot.x, boat.transform.position.y, spot.z);

    LiveWireZone MakeZone(Vector3 at, float radius = 5f)
    {
        at.y = 0f;
        var go = new GameObject("Live wire");
        go.transform.position = at;
        var zone = go.AddComponent<LiveWireZone>();
        zone.radius = radius;
        return zone;
    }

    void PutSwimmerAt(Vector3 where)
    {
        where.y = 0f;
        swimmerBody.position = where;
        player.transform.position = where;
        swimmerBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ------------------------------------------------------------------ the live wire

    [UnityTest]
    public IEnumerator A_boat_that_drives_into_a_live_wire_loses_its_engine_for_a_few_seconds()
    {
        var zone = MakeZone(StreetSpot + West * 20f);
        zone.shockInterval = 30f;                   // one shock is all this test is about
        yield return WaitSeconds(0.3f);
        Assert.AreEqual(0, boat.ShocksTaken, "20 m away is nowhere near it");
        Assert.IsFalse(boat.IsStalled);

        Teleport(boat, At(zone.transform.position), West);
        yield return WaitSeconds(0.3f);

        Assert.AreEqual(1, boat.ShocksTaken);
        Assert.IsTrue(boat.IsStalled, "the engine cuts out");
        StringAssert.Contains("Live wire", boat.ActiveStatus);

        yield return WaitSeconds(zone.boatStallSeconds + 0.3f);
        Assert.IsFalse(boat.IsStalled, "the engine catches again");
        Assert.AreEqual(1, boat.ShocksTaken, "and, being let alone, there is no second shock");
    }

    [UnityTest]
    public IEnumerator A_boat_that_stays_in_the_live_water_is_shocked_again()
    {
        var zone = MakeZone(StreetSpot + West * 20f);
        zone.shockInterval = 1.2f;
        zone.boatStallSeconds = 0.4f;
        Teleport(boat, At(zone.transform.position), West);

        yield return WaitSeconds(3.2f);
        Assert.GreaterOrEqual(boat.ShocksTaken, 2, "still in it, so shocked again");
        Assert.LessOrEqual(boat.ShocksTaken, 4, "but not every frame");
    }

    [UnityTest]
    public IEnumerator A_swimmer_in_a_live_wire_is_stunned_and_thrown_clear()
    {
        var zone = MakeZone(StreetSpot + West * 20f);
        Assert.IsFalse(player.IsMounted, "the player starts in the water");
        PutSwimmerAt(zone.transform.position + Vector3.right * 2f);
        yield return WaitSeconds(0.2f);

        Assert.IsTrue(player.IsStunned);
        Assert.AreEqual(1, player.ShocksTaken);

        float before = Vector3.Distance(Flat(player.transform.position), zone.transform.position);
        yield return WaitSeconds(0.8f);
        float after = Vector3.Distance(Flat(player.transform.position), zone.transform.position);
        Assert.Greater(after, before + 1.5f, "thrown clear of the wire");

        yield return WaitSeconds(zone.swimmerStunSeconds);
        Assert.IsFalse(player.IsStunned, "able to swim again");
    }

    [UnityTest]
    public IEnumerator Somebody_in_the_live_water_cannot_be_rescued_and_wears_down_twice_as_fast()
    {
        Board(player);
        var zone = MakeZone(StreetSpot + West * 10f);
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, zone.transform.position);
        var tess = MakeResident("Tess", Situation.OpenWater, Need.Lifebuoy, StreetSpot + Vector3.right * 8f);
        yield return WaitSeconds(0.5f);

        Assert.IsTrue(ben.InLiveWire);
        Assert.IsFalse(tess.InLiveWire, "she is well outside the zone");

        Assert.IsFalse(ben.CanBeRescued(out string reason));
        StringAssert.Contains("live wire", reason);

        Assert.IsFalse(rescue.Throw(ben), "nobody throws a line into live water");
        StringAssert.Contains("live wire", boat.ActiveStatus);

        Assert.AreEqual(tess.Condition.DrainPerMinute * 2f, ben.Condition.DrainPerMinute, 1e-4f,
            "twice the drain of the same kind of person outside it");
    }

    [UnityTest]
    public IEnumerator Cutting_the_power_makes_the_water_safe_again()
    {
        var zone = MakeZone(StreetSpot + West * 12f);
        zone.cutDelay = 1.5f;
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, zone.transform.position);
        yield return WaitSeconds(0.4f);
        Assert.IsTrue(ben.InLiveWire);

        Assert.IsTrue(zone.RequestCut());
        Assert.IsFalse(zone.RequestCut(), "the call has been made already");
        Assert.IsTrue(zone.IsLive, "it takes a while");
        Assert.IsTrue(ben.InLiveWire);
        Assert.AreEqual(zone.cutDelay, zone.CutTimeLeft, 0.2f);

        yield return WaitSeconds(1.8f);
        Assert.IsFalse(zone.IsLive, "the power is off");
        Assert.IsFalse(zone.CutRequested);
        Assert.IsFalse(ben.InLiveWire, "and they can be reached");
        ben.CanBeRescued(out string reasonAfter);
        StringAssert.DoesNotContain("live wire", reasonAfter ?? "", "nothing is stopping a rescue on that account now");

        Teleport(boat, At(zone.transform.position), West);
        yield return WaitSeconds(0.5f);
        Assert.AreEqual(0, boat.ShocksTaken, "a dead wire does nothing");
    }

    [UnityTest]
    public IEnumerator The_Radyo_is_needed_to_have_the_power_cut()
    {
        Board(player);
        var zone = MakeZone(StreetSpot + West * 15f);
        zone.cutDelay = 20f;
        yield return WaitSeconds(0.3f);

        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        Assert.IsFalse(tools.TryRequestCut(), "no Radyo packed");
        StringAssert.Contains("Radyo", boat.ActiveStatus);
        Assert.IsFalse(zone.CutRequested);

        LoadoutState.Set(new[] { RescueItemId.Radio });
        Assert.IsTrue(tools.TryRequestCut());
        Assert.IsTrue(zone.CutRequested);
        Assert.AreEqual(1, tools.PowerCutsRequested);
        Assert.AreEqual(1, LoadoutState.Uses(RescueItemId.Radio), "using the Radyo is logged");

        Assert.IsFalse(tools.TryRequestCut(), "there is nothing left to ask for");
        StringAssert.Contains("No live wire", boat.ActiveStatus);
    }

    [UnityTest]
    public IEnumerator A_live_wire_too_far_for_the_Radyo_cannot_be_cut_from_here()
    {
        Board(player);
        var zone = MakeZone(StreetSpot + West * 120f);
        yield return WaitSeconds(0.3f);

        Assert.IsFalse(tools.TryRequestCut());
        StringAssert.Contains("No live wire in range", boat.ActiveStatus);
        Assert.IsFalse(zone.CutRequested);
    }

    [UnityTest]
    public IEnumerator The_crew_are_told_about_a_live_wire_and_what_to_do_about_it()
    {
        Board(player);
        var zone = MakeZone(StreetSpot + West * 18f);
        zone.cutDelay = 20f;
        yield return WaitSeconds(0.4f);

        StringAssert.Contains("press C", tools.PowerPrompt);
        Assert.AreSame(zone, tools.ZoneToCut);
        Assert.IsFalse(tools.PowerPromptIsWarning);

        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        yield return WaitSeconds(0.2f);
        StringAssert.Contains("no Radyo", tools.PowerPrompt);
        Assert.IsTrue(tools.PowerPromptIsWarning, "without the Radyo it is only a warning");
        Assert.IsNull(tools.ZoneToCut);

        LoadoutState.Set(new[] { RescueItemId.Radio });
        Assert.IsTrue(tools.TryRequestCut());
        yield return WaitSeconds(0.2f);
        StringAssert.Contains("will be cut in", tools.PowerPrompt);
    }

    [UnityTest]
    public IEnumerator Nothing_is_said_about_a_live_wire_that_is_far_away()
    {
        Board(player);
        MakeZone(StreetSpot + West * 150f);
        yield return WaitSeconds(0.4f);
        Assert.IsNull(tools.PowerPrompt);
    }

    // ------------------------------------------------------------------ the debris jam

    DebrisBarrier MakeJam(float x)
    {
        var go = new GameObject("Jam");
        go.transform.position = new Vector3(x, 0f, -6.5f);
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);          // wide across the street, deep along it
        var jam = go.AddComponent<DebrisBarrier>();
        jam.size = new Vector3(10f, 1.5f, 3f);
        return jam;
    }

    [UnityTest]
    public IEnumerator The_boat_cannot_pass_a_debris_jam_and_hitting_it_hard_is_a_hull_strike()
    {
        var jam = MakeJam(-36f);                    // its near face is at x = -34.5
        yield return WaitSeconds(0.3f);

        boat.GetComponent<Rigidbody>().linearVelocity = West * 5f;
        yield return WaitSeconds(2.5f);

        Assert.Greater(boat.transform.position.x, jam.transform.position.x + 1.5f, "the jam stops her");
        Assert.GreaterOrEqual(boat.HullStrikes, 1, "at 5 m/s that is a hull strike");
    }

    [UnityTest]
    public IEnumerator A_swimmer_is_not_stopped_by_a_debris_jam_but_the_boat_and_other_wreckage_are()
    {
        var jam = MakeJam(-60f);
        var piece = FloatingDebris.Spawn(DebrisKind.Crate, new Vector3(-50f, 0f, -6.5f));
        yield return WaitSeconds(0.3f);

        foreach (var c in player.GetComponentsInChildren<Collider>())
            Assert.IsTrue(Physics.GetIgnoreCollision(jam.Jam, c), "the swimmer slips through the gaps");
        Assert.IsFalse(Physics.GetIgnoreCollision(jam.Jam, boat.Hull), "the boat does not");
        Assert.IsFalse(Physics.GetIgnoreCollision(jam.Jam, piece.GetComponent<Collider>()), "nor does drifting wreckage");
    }

    [UnityTest]
    public IEnumerator A_debris_jam_does_not_hide_anyone_from_the_crew_but_a_wall_does()
    {
        MakeJam(-36f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(-30f, 1.2f, -12f);
        wall.transform.localScale = new Vector3(6f, 4f, 0.5f);
        yield return WaitSeconds(0.3f);

        Assert.IsTrue(LineOfSight.Clear(new Vector3(-30f, 1.2f, -6.5f), new Vector3(-42f, 1.2f, -6.5f)),
            "a pile of boards does not hide a person");
        Assert.IsFalse(LineOfSight.Clear(new Vector3(-30f, 1.2f, -8f), new Vector3(-30f, 1.2f, -16f)),
            "a solid wall does");
    }

    [UnityTest]
    public IEnumerator Somebody_trapped_behind_a_jam_can_be_reached_with_a_line_thrown_over_it()
    {
        MakeJam(-36f);
        Board(player);
        var cora = MakeResident("Cora", Situation.Trapped, Need.Rope, new Vector3(-40f, 0f, -6.5f));
        yield return WaitSeconds(0.8f);

        Assert.AreSame(cora, rescue.Target, "she is in range and in view, over the top of the jam");
        StringAssert.Contains("Lubid", rescue.Prompt);
        Assert.IsTrue(rescue.Throw(cora));
        yield return WaitUntil(() => cora.IsHeld, 4f, "her to catch the rope");
    }

    // ------------------------------------------------------------------ wreckage afloat

    [UnityTest]
    public IEnumerator Floating_debris_rides_the_surface_with_the_waves()
    {
        var piece = FloatingDebris.Spawn(DebrisKind.Crate, new Vector3(-60f, 0f, -6.5f));
        Assert.Contains(piece, FloatingDebris.All);
        yield return WaitSeconds(3f);

        float surface = WaterSurface.Instance.GetHeight(piece.transform.position);
        Assert.Less(Mathf.Abs(piece.transform.position.y - surface), 0.45f, "afloat, not sunk and not flying");
        Assert.Greater(piece.Float.Submerged, 0.05f, "and in the water");
    }

    [UnityTest]
    public IEnumerator Floating_debris_is_carried_along_by_the_current()
    {
        FloodCurrent.Instance.speed = 1f;
        FloodCurrent.Instance.surge = 0f;
        FloodCurrent.Instance.slackDistance = 0f;

        var plank = FloatingDebris.Spawn(DebrisKind.Plank, new Vector3(-60f, 0f, -6.5f));
        yield return WaitSeconds(0.5f);
        float start = plank.transform.position.x;

        yield return WaitSeconds(4f);
        Assert.Greater(plank.transform.position.x - start, 2f, "the flood runs east, and so does the wreckage");
    }

    [UnityTest]
    public IEnumerator Nudging_floating_debris_aside_is_not_a_hull_strike()
    {
        float ahead = boat.Hull.bounds.min.x - 0.6f;                    // just off the bow (she is facing west)
        var piece = FloatingDebris.Spawn(DebrisKind.Crate, new Vector3(ahead, 0f, boat.transform.position.z));
        yield return WaitSeconds(0.3f);
        float placed = piece.transform.position.x;

        boat.GetComponent<Rigidbody>().linearVelocity = West * 2f;
        yield return WaitSeconds(1.2f);

        Assert.Less(piece.transform.position.x, placed - 0.1f, "she did touch it");
        Assert.AreEqual(0, piece.Strikes);
        Assert.AreEqual(0, boat.HullStrikes, "pushing wreckage aside slowly is what a boat does");
    }

    [UnityTest]
    public IEnumerator Ramming_floating_debris_at_speed_is_a_hull_strike()
    {
        float ahead = boat.Hull.bounds.min.x - 1.5f;
        var piece = FloatingDebris.Spawn(DebrisKind.Crate, new Vector3(ahead, 0f, boat.transform.position.z));
        yield return WaitSeconds(0.3f);

        boat.GetComponent<Rigidbody>().linearVelocity = West * 6f;
        yield return WaitUntil(() => piece.Strikes >= 1, 3f, "the boat to strike the debris");

        Assert.AreEqual(1, boat.HullStrikes, "ramming it at speed counts against the crew");
        StringAssert.Contains("Debris strike", boat.ActiveStatus);
    }

    [UnityTest]
    public IEnumerator A_debris_field_scatters_the_same_pieces_for_the_same_seed()
    {
        var go = new GameObject("Debris field");
        go.transform.position = new Vector3(-70f, 0f, -6.5f);
        var field = go.AddComponent<DebrisField>();
        field.scatterOnStart = false;
        field.area = new Vector2(14f, 4f);
        field.count = 6;
        field.seed = 11;

        field.Scatter();
        var first = Describe(field);
        yield return null;

        field.Scatter();
        var second = Describe(field);

        Assert.AreEqual(6, first.Count, "enough room in an open street");
        CollectionAssert.AreEqual(first, second, "same seed, same wreckage");

        foreach (var piece in field.Pieces)
        {
            Vector3 local = go.transform.InverseTransformPoint(piece.transform.position);
            Assert.LessOrEqual(Mathf.Abs(local.x), 7.1f);
            Assert.LessOrEqual(Mathf.Abs(local.z), 2.1f);
        }

        field.Clear();
        Assert.AreEqual(0, field.Pieces.Count);
    }

    static List<string> Describe(DebrisField field)
    {
        var lines = new List<string>();
        foreach (var piece in field.Pieces)
            lines.Add(piece.Kind + " " + piece.transform.position.x.ToString("0.00") + " " + piece.transform.position.z.ToString("0.00"));
        return lines;
    }

    // ------------------------------------------------------------------ rising water

    [UnityTest]
    public IEnumerator Rising_water_lifts_the_boat_with_it_and_wears_residents_down_faster()
    {
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, StreetSpot + Vector3.right * 6f);
        yield return WaitSeconds(1f);
        float drainBefore = ben.Condition.DrainPerMinute;
        float boatBefore = boat.transform.position.y;
        float waterBefore = WaterSurface.Instance.transform.position.y;

        var rise = new GameObject("Flood rise").AddComponent<FloodRise>();
        rise.risePerMinute = 120f;                  // 2 m a second: the cap is reached at once
        rise.maxRise = 0.5f;
        yield return WaitSeconds(2.5f);

        Assert.AreEqual(0.5f, rise.Rise, 1e-3f);
        Assert.AreEqual(waterBefore + 0.5f, WaterSurface.Instance.transform.position.y, 1e-3f);
        Assert.Greater(boat.transform.position.y, boatBefore + 0.3f, "whatever floats goes up with the water");
        Assert.Greater(ben.Condition.DrainPerMinute, drainBefore, "and anybody in it tires faster");
    }
}
