using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// Going in after somebody: taking hold, towing them back, the crew taking them aboard, the tether that keeps the
/// swimmer within reach of the boat, and the safety marks for swimming when a line would have done. The player is
/// in the water to begin with, as in the game.
/// </summary>
public class SwimRescueTests
{
    SimpleBoat boat;
    PlayerRescuer player;
    SwimRescue swim;
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
        swim = player.Rescue;
        swimmerBody = player.GetComponent<Rigidbody>();
        Assert.IsNotNull(swim, "the player has no SwimRescue");
        Teleport(boat, new Vector3(StreetSpot.x, boat.transform.position.y, StreetSpot.z), West);
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    void PutSwimmerAt(Vector3 where)
    {
        where.y = 0f;
        swimmerBody.position = where;
        player.transform.position = where;
        swimmerBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ the rescue

    [UnityTest]
    public IEnumerator A_swimmer_takes_hold_tows_them_back_and_the_crew_take_them_aboard()
    {
        // 20 m up the street: further than a line can be thrown.
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 20f);
        PutSwimmerAt(ben.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.4f);

        Assert.AreSame(ben, swim.Target, "within reach, so the swimmer could take hold");
        StringAssert.Contains("take hold", swim.Prompt);

        Assert.IsTrue(swim.TryGrab(ben));
        Assert.IsTrue(ben.IsHeld);
        Assert.AreEqual(Survivor.Arrival.Swimmer, ben.ArrivedBy);
        Assert.AreEqual(swim.towSpeedFactor, player.SpeedScale, 1e-4f, "towing somebody slows the swimmer");
        Assert.AreEqual(0, swim.UnneededSwims, "20 m was out of the boat's throwing range");

        // Swim 6 m back towards the boat: the person in tow comes with them.
        PutSwimmerAt(ben.transform.position + Vector3.right * 7.5f);
        yield return WaitSeconds(1.5f);
        Assert.Less(Vector3.Distance(ben.transform.position, player.transform.position), 3.5f, "they trail the swimmer");
        Assert.IsFalse(ben.IsRescued, "still too far from the boat");

        // Get them to the side of the boat: the crew take them.
        PutSwimmerAt(boat.transform.position + Vector3.back * 3.4f);
        yield return WaitUntil(() => ben.IsRescued, 8f, "the crew to take them aboard");
        yield return WaitUntil(() => !ben.gameObject.activeSelf, 6f, "them to be hauled up");

        Assert.AreEqual(1, boat.Passengers);
        Assert.IsNull(swim.Towing);
        Assert.AreEqual(1f, player.SpeedScale, 1e-4f, "the swimmer is back to full speed");
        Assert.AreEqual(1, swim.Grabs);
    }

    [UnityTest]
    public IEnumerator Somebody_hurt_is_treated_by_the_crew_before_they_come_aboard()
    {
        var nena = MakeResident("Nena", Situation.OpenWater, Need.Lifebuoy | Need.FirstAid, boat.transform.position + West * 20f,
                                CharacterCategory.Elder);
        PutSwimmerAt(nena.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(swim.TryGrab(nena));

        PutSwimmerAt(boat.transform.position + Vector3.back * 3.4f);
        yield return WaitUntil(() => boat.HoldProgress01 > 0.05f, 4f, "the crew to start treating them");
        Assert.IsFalse(nena.IsRescued, "they are treated first");

        yield return WaitUntil(() => nena.IsRescued, 6f, "the treatment to finish and them to be taken");
        Assert.AreEqual(1, LoadoutState.Uses(RescueItemId.FirstAid), "the Botika earned its slot");
    }

    [UnityTest]
    public IEnumerator A_swimmer_cannot_take_hold_of_somebody_whose_item_is_not_packed()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 20f);
        PutSwimmerAt(ben.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.4f);

        StringAssert.Contains("Salbabida", swim.Prompt);
        Assert.IsTrue(swim.PromptIsWarning);
        Assert.IsFalse(swim.TryGrab(ben));
        Assert.IsFalse(ben.IsHeld);
        Assert.IsNull(swim.Towing);
    }

    [UnityTest]
    public IEnumerator Nobody_is_taken_hold_of_in_a_live_wire()
    {
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 20f);
        PutSwimmerAt(ben.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.4f);
        ben.InLiveWire = true;

        Assert.IsFalse(swim.TryGrab(ben));
        StringAssert.Contains("live wire", boat.ActiveStatus);
    }

    [UnityTest]
    public IEnumerator Letting_go_leaves_them_in_the_water_and_gives_the_swimmer_their_speed_back()
    {
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 20f);
        PutSwimmerAt(ben.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(swim.TryGrab(ben));

        swim.LetGoOfTow();

        Assert.IsFalse(ben.IsHeld);
        Assert.IsNull(swim.Towing);
        Assert.AreEqual(1f, player.SpeedScale, 1e-4f);
        Assert.AreEqual(Survivor.Arrival.Boat, ben.ArrivedBy);
    }

    [UnityTest]
    public IEnumerator Climbing_aboard_lets_go_of_somebody_in_tow()
    {
        var ben = MakeResident("Ben", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 20f);
        PutSwimmerAt(ben.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(swim.TryGrab(ben));

        Board(player);

        Assert.IsTrue(player.IsMounted);
        Assert.IsFalse(ben.IsHeld, "you cannot climb aboard with somebody on a line");
        Assert.AreEqual(1f, player.SpeedScale, 1e-4f);
    }

    // ------------------------------------------------------------------ last resort

    [UnityTest]
    public IEnumerator Swimming_to_somebody_a_thrown_line_could_have_reached_is_recorded()
    {
        // 8 m up the street, in view, with everything packed: a throw would have done.
        var near = MakeResident("Near", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 8f);
        var far = MakeResident("Far", Situation.OpenWater, Need.Lifebuoy, boat.transform.position + West * 22f);
        Teleport(boat, boat.transform.position, West);
        yield return WaitSeconds(0.4f);

        PutSwimmerAt(far.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(swim.TryGrab(far));
        Assert.AreEqual(0, swim.UnneededSwims, "out of throwing range: swimming was the way");
        swim.LetGoOfTow();

        PutSwimmerAt(near.transform.position + Vector3.right * 1.5f);
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(swim.TryGrab(near));
        Assert.AreEqual(1, swim.UnneededSwims, "a line from the boat could have reached them");
    }

    // ------------------------------------------------------------------ the tether

    [UnityTest]
    public IEnumerator At_the_end_of_the_tether_a_swimmer_cannot_swim_any_further_out()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        Assert.IsTrue(swim.IsTethered);
        Vector3 centre = boat.transform.position;
        Vector3 outward = Vector3.right;

        // Well inside the line, nothing holds them.
        Vector3 inside = swim.ApplyTether(centre + outward * (swim.tetherLength - 8f), outward * 2.8f);
        Assert.AreEqual(2.8f, Vector3.Dot(inside, outward), 1e-4f);

        // At the end of it, they can swim along it or back, but not out.
        Vector3 atEnd = swim.ApplyTether(centre + outward * swim.tetherLength, outward * 2.8f + Vector3.forward * 1.5f);
        Assert.LessOrEqual(Vector3.Dot(atEnd, outward), 0.001f, "no further out");
        Assert.AreEqual(1.5f, Vector3.Dot(atEnd, Vector3.forward), 1e-4f, "along the line is still free");

        Vector3 coming = swim.ApplyTether(centre + outward * swim.tetherLength, -outward * 2.8f);
        Assert.AreEqual(-2.8f, Vector3.Dot(coming, outward), 1e-4f, "swimming back is free");

        // Without the Lubid there is no line to be held by.
        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        Vector3 loose = swim.ApplyTether(centre + outward * (swim.tetherLength + 10f), outward * 2.8f);
        Assert.AreEqual(2.8f, Vector3.Dot(loose, outward), 1e-4f);
        yield break;
    }

    [UnityTest]
    public IEnumerator A_swimmer_carried_past_the_end_of_the_tether_is_drawn_back()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        PutSwimmerAt(boat.transform.position + Vector3.right * (swim.tetherLength + 3f));
        yield return WaitSeconds(2.5f);

        Vector3 d = player.transform.position - boat.transform.position;
        d.y = 0f;
        Assert.Less(d.magnitude, swim.tetherLength + 0.8f, "the line draws them back to its length");
        Assert.AreEqual(0, swim.UntetheredSwims, "tethered, so not an untethered swim");
    }

    [UnityTest]
    public IEnumerator Without_the_Lubid_a_long_swim_is_untethered_and_recorded()
    {
        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        Assert.IsFalse(swim.IsTethered);

        PutSwimmerAt(boat.transform.position + Vector3.right * (swim.untetheredDistance + 6f));
        yield return WaitSeconds(swim.untetheredSeconds + 1f);

        Assert.AreEqual(1, swim.UntetheredSwims);
        StringAssert.Contains("untethered", swim.Prompt);

        // Coming back and going out again is a second excursion; staying out is not counted twice.
        yield return WaitSeconds(1f);
        Assert.AreEqual(1, swim.UntetheredSwims);
        PutSwimmerAt(boat.transform.position + Vector3.right * 3f);
        yield return WaitSeconds(0.3f);
        PutSwimmerAt(boat.transform.position + Vector3.right * (swim.untetheredDistance + 6f));
        yield return WaitSeconds(swim.untetheredSeconds + 1f);
        Assert.AreEqual(2, swim.UntetheredSwims);
    }

    [UnityTest]
    public IEnumerator A_swimmer_cannot_reach_somebody_standing_on_a_roof()
    {
        var cesar = MakeResident("Cesar", Situation.Rooftop, Need.Rope, new Vector3(-27.1f, 3.8f, 0.1f), CharacterCategory.Elder);
        PutSwimmerAt(new Vector3(-27.1f, 0f, -1.5f));      // directly below
        yield return WaitSeconds(0.4f);

        Assert.IsNull(swim.Target, "a roof is out of the water");
    }
}
