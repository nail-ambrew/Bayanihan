using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// Who is where on the boat: the crew, the person at the helm, and every resident brought aboard. Played in the real
/// Gameplay scene, doing what the player's key presses would do.
/// </summary>
public class BoatPassengerTests
{
    SimpleBoat boat;
    BoatPassengers passengers;
    PlayerRescuer player;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadGameplay();
        boat = Boat();
        passengers = boat.GetComponent<BoatPassengers>();
        player = Player();
        Assert.IsNotNull(boat, "no boat");
        Assert.IsNotNull(passengers, "the boat has no BoatPassengers");
        Assert.IsNotNull(player, "no player");
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        Time.timeScale = 1f;
        yield return null;
    }

    /// <summary>The one person aboard at the start: the crew member minding the helm.</summary>
    CharacterAnimator CrewMember()
    {
        var crew = boat.GetComponentInChildren<CharacterAnimator>();
        Assert.IsNotNull(crew, "there is nobody on the boat at the start");
        return crew;
    }

    /// <summary>Which passenger seat somebody is sitting in, or -1.</summary>
    int SeatOf(CharacterAnimator person)
    {
        for (int i = 0; i < boat.capacity; i++)
        {
            passengers.SeatPose(i, out Vector3 seat, out _);
            if (Vector3.Distance(seat, person.transform.position) < 0.05f) return i;
        }
        return -1;
    }

    IEnumerator Rescue(Survivor survivor, float side)
    {
        yield return TakeAboard(boat, survivor, side);
        yield return null;
    }

    // ------------------------------------------------------------------ a rescue

    [UnityTest]
    public IEnumerator A_rescued_survivor_ends_up_seated_in_the_boat_at_full_size()
    {
        var survivor = ResidentWhere(Situation.OpenWater, Need.Lifebuoy);
        var person = survivor.GetComponent<SurvivorVisual>().Animator;
        Assert.IsNotNull(person, "the survivor has no character");
        Vector3 sizeInTheWater = person.transform.lossyScale;
        int helmTube = SideOf(boat, CrewMember().transform.localPosition);

        yield return Rescue(survivor, +1);

        Assert.AreSame(boat.transform, person.transform.parent, "the same character should be on the boat now");
        Assert.AreEqual(sizeInTheWater.x, person.transform.lossyScale.x, 0.001f, "nobody should change size coming aboard");
        Assert.AreEqual(1f, person.transform.lossyScale.x, 0.001f, "people on the boat are full size");
        Assert.AreEqual(1f, person.transform.lossyScale.y, 0.001f);
        Assert.AreEqual(1f, person.transform.lossyScale.z, 0.001f);

        Assert.AreNotEqual(PoseOverlay.None, person.Overlays & PoseOverlay.Seated, "they should be sitting");
        int seat = SeatOf(person);
        Assert.GreaterOrEqual(seat, 0, "they are not on any of the passenger seats");

        // The hips rest on the seat, the thighs are level, and they face in across the boat.
        float hipHeight = person.transform.InverseTransformPoint(person.Bone("Hips").position).y;
        Assert.AreEqual(passengers.seatHipHeight, hipHeight, 0.05f, "the hips should rest on the seat");

        Vector3 thighToKnee = person.Bone("ShinL").position - person.Bone("ThighL").position;
        Assert.Less(Mathf.Abs(thighToKnee.y), 0.35f * thighToKnee.magnitude, "the thighs should be roughly level when sitting");

        passengers.SeatPose(seat, out Vector3 seatPosition, out _);
        Vector3 local = boat.transform.InverseTransformPoint(seatPosition);
        Vector3 inward = boat.transform.TransformDirection(Vector3.Cross(Vector3.up, boat.LocalBow) * -SideOf(boat, local));
        Assert.Greater(Vector3.Dot(person.transform.forward * person.ForwardSign(), inward), 0.9f, "they should face across the boat");

        Assert.IsNotNull(person.Bone("Chest").Find("LifeVest"), "everybody aboard wears a life vest");
        Assert.AreEqual(1, boat.Passengers);
        Assert.AreEqual(1, RescueManager.Instance.PickedUp);

        // With the crew minding the helm on one tube, the first person aboard sits on the other.
        Assert.AreNotEqual(helmTube, SideOf(boat, local), "the first passenger should take the tube the helm crew is not on");
    }

    [UnityTest]
    public IEnumerator Four_rescues_fill_four_different_seats_two_to_a_tube_without_anybody_changing_size()
    {
        Assert.AreEqual(4, boat.capacity, "this test is written for a four-seat boat");
        Board(player);                       // the way a real rescue goes: the player at the helm, the crew forward

        var tubes = new Dictionary<int, int>();
        var seatsUsed = new HashSet<int>();
        var waiting = Waiting();
        Assert.GreaterOrEqual(waiting.Length, 4, "the scene needs four residents");

        float side = 1f;
        for (int i = 0; i < 4; i++)
        {
            var person = waiting[i].GetComponent<SurvivorVisual>().Animator;
            yield return Rescue(waiting[i], side);
            side = -side;

            int seat = SeatOf(person);
            Assert.GreaterOrEqual(seat, 0, waiting[i].name + " is not on a passenger seat");
            Assert.IsTrue(seatsUsed.Add(seat), "seat " + seat + " was given to two people");
            Assert.AreEqual(1f, person.transform.lossyScale.x, 0.001f, waiting[i].name + " changed size coming aboard");

            passengers.SeatPose(seat, out Vector3 world, out _);
            int tube = SideOf(boat, boat.transform.InverseTransformPoint(world));
            tubes[tube] = tubes.TryGetValue(tube, out int n) ? n + 1 : 1;
        }

        Assert.IsTrue(boat.IsFull);
        Assert.AreEqual(2, tubes[-1], "two passengers on one tube");
        Assert.AreEqual(2, tubes[+1], "and two on the other");
    }

    [UnityTest]
    public IEnumerator Seats_that_are_held_for_people_on_their_way_are_not_given_to_anyone_else()
    {
        int first = passengers.ReserveSeat();
        int second = passengers.ReserveSeat();
        Assert.AreNotEqual(first, second, "two people on their way aboard must not be promised the same seat");

        passengers.SeatPose(first, out Vector3 a, out _);
        passengers.SeatPose(second, out Vector3 b, out _);
        Assert.AreNotEqual(SideOf(boat, boat.transform.InverseTransformPoint(a)),
                           SideOf(boat, boat.transform.InverseTransformPoint(b)),
                           "the first two people to come aboard should not both sit on the same tube");

        // An unload lets go of every seat that was being held.
        passengers.UnloadAll(null);
        Assert.AreEqual(first, passengers.ReserveSeat(), "after an unload the seats are free again");
        yield break;
    }

    [UnityTest]
    public IEnumerator The_next_passenger_takes_the_lighter_tube_whichever_side_the_crew_are_on()
    {
        var crew = CrewMember();
        foreach (float side in new[] { +0.95f, -0.95f })
        {
            passengers.UnloadAll(null);                     // start from an empty boat each time
            crew.transform.localPosition = new Vector3(side, 0.66f, 1.15f);

            int seat = passengers.ReserveSeat();
            passengers.SeatPose(seat, out Vector3 world, out _);
            int seatSide = SideOf(boat, boat.transform.InverseTransformPoint(world));
            Assert.AreNotEqual(SideOf(boat, crew.transform.localPosition), seatSide,
                "with the only other person aboard on the " + (side > 0f ? "+" : "-") + " tube, the first passenger belongs on the other one");
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator A_person_sits_where_they_were_told_to_even_if_somebody_who_started_later_gets_there_first()
    {
        int seatForFirst = passengers.ReserveSeat();
        int seatForSecond = passengers.ReserveSeat();

        // Two residents are on their way across the water; the second one is closer and arrives first.
        var library = CharacterLibrary.Instance;
        var late = CharacterRig.Spawn(library.RandomCivilian(), boat.transform, CharacterPose.Idle);
        passengers.SeatExisting(late, seatForSecond, boat.BoardingEpoch);

        passengers.SeatPose(seatForSecond, out Vector3 expected, out _);
        Assert.Less(Vector3.Distance(late.transform.position, expected), 0.05f,
            "they should sit in the seat they were hauled towards, not jump to whichever is free");

        var early = CharacterRig.Spawn(library.RandomCivilian(), boat.transform, CharacterPose.Idle);
        passengers.SeatExisting(early, seatForFirst, boat.BoardingEpoch);
        passengers.SeatPose(seatForFirst, out expected, out _);
        Assert.Less(Vector3.Distance(early.transform.position, expected), 0.05f);
        yield break;
    }

    // ------------------------------------------------------------------ the crew and the helm

    [UnityTest]
    public IEnumerator The_crew_go_forward_to_the_bow_when_the_player_boards_and_come_back_when_they_step_off()
    {
        var crew = CrewMember();
        Assert.IsTrue(passengers.HasHelmCrew, "somebody should be minding the helm at the start");
        Vector3 helm = crew.transform.localPosition;

        Board(player);
        yield return null;

        Assert.IsTrue(player.IsMounted);
        Assert.IsTrue(boat.IsPlayerAboard);
        Assert.IsFalse(passengers.HasHelmCrew, "with the player at the helm nobody else is steering");
        Assert.IsTrue(crew.gameObject.activeInHierarchy, "the crew member should still be on the boat, not gone");

        Vector3 lookout = crew.transform.localPosition;
        Assert.Greater(Vector3.Dot(lookout - helm, boat.LocalBow), 2f, "the crew member should have gone forward to the bow");
        Assert.AreEqual(PoseOverlay.None, crew.Overlays & PoseOverlay.HoldTiller, "the lookout is not holding the tiller any more");
        Assert.AreNotEqual(PoseOverlay.None, crew.Overlays & PoseOverlay.Seated, "the lookout sits");
        Assert.Less(Vector3.Distance(player.transform.localPosition, helm), 0.01f, "the player takes the helm seat");

        StepOff(player);
        yield return null;

        Assert.IsFalse(player.IsMounted, "the player could not step off - is there something solid beside the boat?");
        Assert.Less(Vector3.Distance(crew.transform.localPosition, helm), 0.01f, "the crew member should go back to the helm");
        Assert.IsTrue(passengers.HasHelmCrew);
        Assert.AreNotEqual(PoseOverlay.None, crew.Overlays & PoseOverlay.HoldTiller, "back at the helm they hold the tiller");
    }

    [UnityTest]
    public IEnumerator Whoever_is_at_the_helm_has_a_hand_on_the_tiller()
    {
        var grip = CharacterRig.FindDeep(boat.transform, "TillerGrip");
        Assert.IsNotNull(grip, "the boat has no tiller to hold (is the motor's tiller still called '" + passengers.tillerName + "'?)");

        var crew = CrewMember();
        yield return null;
        Assert.Less(Vector3.Distance(crew.Bone("HandR").position, grip.position), 0.05f,
            "the helm crew's hand should be on the tiller");

        Board(player);
        yield return null;
        yield return null;
        var driver = player.GetComponentInChildren<CharacterAnimator>();
        Assert.IsNotNull(driver, "the player has no character");
        Assert.Less(Vector3.Distance(driver.Bone("HandR").position, grip.position), 0.05f,
            "the player's hand should be on the tiller");

        // The tiller swings with the steering, and the hand goes with it. BoatEffects is what turns the motor with
        // the steering input, so it is switched off to turn the motor by hand.
        boat.GetComponent<BoatEffects>().enabled = false;
        var motor = CharacterRig.FindDeep(boat.transform, "outboard_motor");
        Quaternion atRest = motor.localRotation;
        Vector3 gripAtRest = grip.position;

        // Swung towards the helmsman, the grip comes closer and the hand stays on it.
        motor.localRotation = atRest * Quaternion.Euler(0f, -20f, 0f);
        yield return null;
        Assert.Greater(Vector3.Distance(grip.position, gripAtRest), 0.1f, "the tiller should have swung");
        Assert.Less(Vector3.Distance(driver.Bone("HandR").position, grip.position), 0.05f,
            "the hand should follow the tiller when it swings towards the helmsman");

        // Swung away, the arm is already nearly straight when the tiller is at rest, so the hand goes as far as the
        // arm will reach rather than letting go.
        motor.localRotation = atRest * Quaternion.Euler(0f, 14f, 0f);
        yield return null;
        Assert.Less(Vector3.Distance(driver.Bone("HandR").position, grip.position), 0.1f,
            "the hand should reach for the tiller when it swings away");
    }
}
