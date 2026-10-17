using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// Reaching somebody with a thrown ring or rope: who the crew can throw to, what stops a throw, how the person is
/// brought in - across the water, or down off a roof - and what happens if the boat pulls away. Played in the real
/// Gameplay scene, with the player at the helm.
/// </summary>
public class ThrowRescueTests
{
    SimpleBoat boat;
    BoatRescue rescue;
    PlayerRescuer player;

    // A clear stretch of the main street, facing west, and the direction the bow points.
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
        rescue = boat.GetComponent<BoatRescue>();
        player = Player();
        Assert.IsNotNull(rescue, "the boat has no BoatRescue");
        PutBoatInTheStreet();
        Board(player);
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    void PutBoatInTheStreet() =>
        Teleport(boat, new Vector3(StreetSpot.x, boat.transform.position.y, StreetSpot.z), West);

    /// <summary>Somebody adrift this far ahead of the bow.</summary>
    Survivor AdriftAhead(string name, float metres, Need needs = Need.Lifebuoy)
    {
        Vector3 at = boat.transform.position + boat.HeadingForward * metres;
        at.y = 0f;
        return MakeResident(name, Situation.OpenWater, needs, at);
    }

    // ------------------------------------------------------------------ the whole rescue

    [UnityTest]
    public IEnumerator A_ring_thrown_to_somebody_in_open_water_brings_them_alongside_and_they_are_taken_aboard()
    {
        var ben = AdriftAhead("Ben", 10f);
        yield return WaitSeconds(0.5f);

        Assert.AreSame(ben, rescue.Target, "somebody 10 m ahead in open water is who a throw would go to");
        StringAssert.Contains("throw the Salbabida", rescue.Prompt);
        Assert.IsFalse(rescue.PromptIsWarning);

        Assert.IsTrue(rescue.Throw(ben), "the throw should go ahead");
        Assert.IsTrue(rescue.LifebuoyOut);
        Assert.IsFalse(rescue.DeckLifebuoy.gameObject.activeSelf, "the ring on the deck is the one that was thrown");
        Assert.AreEqual(1, rescue.Lines.Count);

        yield return WaitUntil(() => ben.IsHeld, 4f, "them to take hold of the ring");
        Assert.IsNotNull(ben.Line);

        yield return WaitUntil(() => ben.LineDelivered, 15f, "them to be brought alongside");
        Assert.AreEqual(RescueLine.Phase.Alongside, ben.Line.Stage);
        Assert.Less(Vector3.Distance(Survivor.HullSide(boat, ben.transform.position), ben.transform.position), 1.2f,
            "they should be at the side of the boat");
        Assert.IsTrue(boat.IsInReach(ben) || Vector3.Distance(ben.transform.position, boat.transform.position) < 4f);

        PickUp(boat, ben);
        yield return WaitUntil(() => !ben.gameObject.activeSelf, 8f, "them to be hauled aboard");
        yield return null;

        Assert.AreEqual(1, boat.Passengers);
        Assert.AreEqual(0, rescue.Lines.Count, "the line is finished with once they are aboard");
        Assert.IsFalse(rescue.LifebuoyOut);
        Assert.IsTrue(rescue.DeckLifebuoy.gameObject.activeSelf, "the ring is back on the deck");
        Assert.AreEqual(Survivor.Arrival.Line, ben.ArrivedBy);
    }

    [UnityTest]
    public IEnumerator Somebody_in_reach_who_needs_no_line_is_simply_taken_aboard_not_thrown_to()
    {
        var near = MakeResident("Near", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + boat.HeadingForward * 3.2f);
        yield return WaitSeconds(0.6f);

        Assert.IsTrue(boat.IsInReach(near), "3 m off is inside the pick-up radius");
        Assert.IsNull(rescue.Target, "no need for a line when they are within arm's reach");
    }

    // ------------------------------------------------------------------ what stops a throw

    [UnityTest]
    public IEnumerator A_throw_needs_the_item_the_resident_needs_to_be_packed()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        var ben = AdriftAhead("Ben", 9f);
        yield return WaitSeconds(0.5f);

        Assert.AreSame(ben, rescue.Target);
        StringAssert.Contains("Salbabida", rescue.Prompt);
        StringAssert.Contains("not packed", rescue.Prompt);
        Assert.IsTrue(rescue.PromptIsWarning);
        Assert.IsFalse(rescue.Throw(ben));
        Assert.AreEqual(0, rescue.Lines.Count);
    }

    [UnityTest]
    public IEnumerator A_throw_is_refused_while_the_boat_is_going_too_fast()
    {
        var ben = AdriftAhead("Ben", 9f);
        yield return WaitSeconds(0.5f);

        boat.GetComponent<Rigidbody>().linearVelocity = boat.HeadingForward * (rescue.maxThrowSpeed + 2f);
        Assert.IsFalse(rescue.Throw(ben), "nobody throws accurately from a boat at speed");
        StringAssert.Contains("too fast", boat.ActiveStatus);
        Assert.AreEqual(0, rescue.Lines.Count);
    }

    [UnityTest]
    public IEnumerator Only_one_ring_can_be_out_at_a_time()
    {
        var ben = AdriftAhead("Ben", 9f);
        var tess = AdriftAhead("Tess", 7f);
        tess.transform.position += Vector3.forward * 3f;
        yield return WaitSeconds(0.5f);

        Assert.IsTrue(rescue.Throw(ben));
        yield return WaitSeconds(rescue.throwCooldown + 0.2f);           // the crew have the line ready again
        Assert.IsFalse(rescue.Throw(tess), "there is only one Salbabida");
        StringAssert.Contains("already out", boat.ActiveStatus);
        Assert.AreEqual(1, rescue.Lines.Count);
    }

    [UnityTest]
    public IEnumerator A_live_wire_in_the_water_stops_a_throw()
    {
        var ben = AdriftAhead("Ben", 9f);
        yield return WaitSeconds(0.5f);
        ben.InLiveWire = true;

        Assert.IsFalse(rescue.Throw(ben));
        StringAssert.Contains("live wire", boat.ActiveStatus);
    }

    [UnityTest]
    public IEnumerator A_full_boat_does_not_throw_to_anyone_else()
    {
        var ben = AdriftAhead("Ben", 9f);
        yield return WaitSeconds(0.5f);

        int capacity = boat.capacity;
        boat.capacity = 0;
        Assert.IsFalse(rescue.Throw(ben));
        StringAssert.Contains("full", boat.ActiveStatus);
        boat.capacity = capacity;
    }

    // ------------------------------------------------------------------ losing the line

    [UnityTest]
    public IEnumerator Somebody_in_the_water_lets_go_if_the_boat_pulls_away()
    {
        var ben = AdriftAhead("Ben", 9f);
        yield return WaitSeconds(0.5f);
        Assert.IsTrue(rescue.Throw(ben));
        yield return WaitUntil(() => ben.IsHeld, 4f, "them to take hold");

        Teleport(boat, boat.transform.position + Vector3.right * 30f, West);     // 30 m away in an instant
        yield return WaitUntil(() => rescue.LinesLost == 1, 4f, "the line to go taut and be let go");

        Assert.IsFalse(ben.IsHeld);
        Assert.AreEqual(0, rescue.Lines.Count);
        Assert.IsFalse(rescue.LifebuoyOut, "the ring comes back to the boat");
        Assert.IsTrue(rescue.DeckLifebuoy.gameObject.activeSelf);
        Assert.AreEqual(Survivor.Arrival.Boat, ben.ArrivedBy);
    }

    // ------------------------------------------------------------------ a roof

    [UnityTest]
    public IEnumerator A_resident_on_a_roof_is_out_of_reach_until_a_line_brings_them_down_into_the_water_beside_the_boat()
    {
        // The low shop roof at the north side of the street near x = -27: its top is about 3.8 m above the water.
        Teleport(boat, new Vector3(-24f, boat.transform.position.y, -4.2f), West);
        var cesar = MakeResident("Cesar", Situation.Rooftop, Need.Rope, new Vector3(-27.1f, 3.8f, 0.1f),
                                 CharacterCategory.Elder, facing: Quaternion.Euler(0f, 180f, 0f));
        yield return WaitSeconds(0.8f);

        Assert.IsFalse(cesar.CanBeRescued(out string reason), "out of reach until there is a line");
        StringAssert.Contains("line", reason);
        Assert.AreSame(cesar, rescue.Target, "a rooftop resident within 12 m with a clear view is who a throw goes to");
        StringAssert.Contains("Lubid", rescue.Prompt);

        Assert.IsTrue(rescue.Throw(cesar));
        Assert.IsTrue(rescue.RopeOut);
        Assert.IsFalse(rescue.LifebuoyOut, "a rope goes to somebody who needs a rope, not the ring");

        yield return WaitUntil(() => cesar.IsHeld, 4f, "them to catch the rope");
        float startHeight = cesar.transform.position.y;
        yield return WaitUntil(() => cesar.LineDelivered, 25f, "them to walk to the edge and come down the line");

        var visual = cesar.GetComponent<SurvivorVisual>();
        Assert.IsTrue(visual.followWaves, "lowered off the roof, they are in the water beside the boat now");
        Assert.Less(cesar.transform.position.y, startHeight - 2.5f, "they came down");
        Assert.Less(cesar.transform.position.y, 1.2f, "to the water, not part of the way");
        Assert.IsTrue(cesar.CanBeRescued(out reason), reason);

        PickUp(boat, cesar);
        yield return WaitUntil(() => !cesar.gameObject.activeSelf, 8f, "them to be hauled aboard");
        Assert.AreEqual(1, boat.Passengers);
        Assert.AreEqual(0, rescue.Lines.Count);
        Assert.IsFalse(rescue.RopeOut);
    }

    [UnityTest]
    public IEnumerator Somebody_who_has_tied_off_on_a_roof_cannot_lose_the_line()
    {
        Teleport(boat, new Vector3(-24f, boat.transform.position.y, -4.2f), West);
        var cesar = MakeResident("Cesar", Situation.Rooftop, Need.Rope, new Vector3(-27.1f, 3.8f, 0.1f),
                                 CharacterCategory.Elder, facing: Quaternion.Euler(0f, 180f, 0f));
        yield return WaitSeconds(0.8f);
        Assert.IsTrue(rescue.Throw(cesar));
        yield return WaitUntil(() => cesar.IsHeld, 4f, "them to catch the rope");

        Teleport(boat, boat.transform.position + Vector3.right * 30f, West);
        yield return WaitSeconds(1f);

        Assert.AreEqual(0, rescue.LinesLost, "a rope tied off on a roof does not slip");
        Assert.IsTrue(cesar.IsHeld);
    }
}
