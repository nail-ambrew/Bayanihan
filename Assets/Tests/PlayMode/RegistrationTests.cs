using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// What happens at the Barangay Hall: the boat unloads, everybody steps ashore and joins the line, and the desk writes
/// them down one at a time. Only then are they safe and scored. Played in the real Gameplay scene, using the
/// residents it comes with, so these hold however many residents the mission has.
/// </summary>
public class RegistrationTests
{
    RescueManager mission;
    SimpleBoat boat;
    PlayerRescuer player;
    EvacuationCenter evac;

    Vector3 startPosition, startHeading;

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();
        StillWater();
        mission = RescueManager.Instance;
        boat = Boat();
        player = Player();
        evac = Object.FindFirstObjectByType<EvacuationCenter>();
        Assert.IsNotNull(evac);
        startPosition = boat.transform.position;
        startHeading = boat.HeadingForward;
        Board(player);
        yield return WaitSeconds(0.4f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    /// <summary>The first few residents the scene has, who have not been rescued.</summary>
    List<Survivor> First(int count)
    {
        var all = Waiting();
        Assert.GreaterOrEqual(all.Length, count, "the scene has too few residents for this test");
        return new List<Survivor>(all).GetRange(0, count);
    }

    IEnumerator TakeAll(List<Survivor> people)
    {
        float side = 1f;
        foreach (var s in people)
        {
            yield return TakeAboard(boat, s, side);
            side = -side;
        }
        yield return null;
    }

    /// <summary>Back where the boat started, clear of the hall.</summary>
    void BackToTheStreet() => Teleport(boat, startPosition, startHeading);

    // ------------------------------------------------------------------ coming ashore is not being safe

    [UnityTest]
    public IEnumerator Residents_come_ashore_first_and_are_safe_only_when_the_desk_has_written_them_down()
    {
        var people = First(2);
        evac.Desk.secondsPerResident = 1.5f;
        yield return TakeAll(people);

        ArriveAtTheHall(boat);
        yield return WaitUntil(() => boat.Passengers == 0, 10f, "the boat to unload");
        yield return WaitUntil(() => evac.Desk.Pending == 2 || mission.Evacuated > 0, 10f, "them to join the line");

        foreach (var s in people)
        {
            var record = mission.Roster.Find(s);
            Assert.IsTrue(record.status == ResidentStatus.Ashore || record.status == ResidentStatus.Registered, record.name);
            Assert.GreaterOrEqual(record.droppedAt, 0f, "the time they stepped off the boat is on record");
        }
        Assert.AreEqual(0, mission.Evacuated, "ashore, but not yet on the list");
        Assert.AreEqual(0, mission.Score, "and not yet scored");

        yield return WaitUntil(() => mission.Evacuated == 1, 6f, "the first of them to be registered");
        Assert.AreEqual(1, evac.Desk.RegisteredCount);
        Assert.AreEqual(1, mission.Roster.CountWith(ResidentStatus.Registered));
        Assert.AreEqual(1, mission.Roster.CountWith(ResidentStatus.Ashore), "the other is still in the line");

        var first = mission.Roster.Records.Find(r => r.status == ResidentStatus.Registered);
        Assert.Greater(first.points, 0);
        Assert.AreEqual(first.points, mission.Score, "their points are in the score as soon as they are registered");
        Assert.GreaterOrEqual(first.registeredAt, first.droppedAt);

        yield return WaitUntil(() => mission.Evacuated == 2, 6f, "the second to be registered");
        Assert.AreEqual(0, evac.Desk.Pending);
    }

    [UnityTest]
    public IEnumerator The_desk_takes_the_time_it_is_set_to_for_each_resident_one_at_a_time()
    {
        var people = First(3);
        evac.Desk.secondsPerResident = 1f;
        yield return TakeAll(people);

        ArriveAtTheHall(boat);
        yield return WaitUntil(() => mission.Roster.CountWith(ResidentStatus.Registered) == 3, 20f, "all three to be registered");

        var times = new List<float>();
        foreach (var s in people) times.Add(mission.Roster.Find(s).registeredAt);
        times.Sort();
        for (int i = 1; i < times.Count; i++)
            Assert.GreaterOrEqual(times[i] - times[i - 1], 0.9f, "the desk sees one person at a time, a second each");
    }

    [UnityTest]
    public IEnumerator Each_resident_knows_where_they_landed_and_the_desk_is_told_who_is_next()
    {
        var people = First(2);
        evac.Desk.secondsPerResident = 3f;
        yield return TakeAll(people);

        ArriveAtTheHall(boat);
        yield return WaitUntil(() => evac.Desk.Current != null, 10f, "somebody to reach the desk");
        yield return WaitUntil(() => mission.Roster.Records.FindAll(r => r.landedAs != null).Count == 2, 10f, "both to land");

        Assert.IsNotNull(evac.Desk.Current.landedAs, "the one at the desk is standing ashore");
        Assert.AreEqual(1, evac.Desk.Waiting.Count);
        Assert.Greater(evac.Desk.Progress01, 0f);
        Assert.Less(evac.Desk.Progress01, 1f);
        foreach (var s in people)
        {
            var landed = mission.Roster.Find(s).landedAs;
            Assert.IsNull(landed.parent, "ashore, not on the boat");
            Assert.Less(Vector3.Distance(Flat(landed.position), Flat(evac.transform.position)), 25f, "on the porch of the hall");
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ------------------------------------------------------------------ households

    [UnityTest]
    public IEnumerator A_household_that_reaches_the_hall_together_earns_the_reunion_bonus()
    {
        var people = First(2);
        foreach (var s in people) mission.Roster.Find(s).household = "Testers";     // a family of their own
        evac.Desk.secondsPerResident = 0.4f;
        yield return TakeAll(people);

        ArriveAtTheHall(boat);
        yield return WaitUntil(() => mission.Evacuated == 2, 20f, "both to be registered");

        var sheet = mission.Sheet;
        var family = sheet.Households.Find(h => h.name == "Testers");
        Assert.IsNotNull(family);
        Assert.IsTrue(family.Reunited);
        int bonus = MissionDefinition.Current.rules.HouseholdBonus(2);
        Assert.AreEqual(bonus, family.bonus);
        Assert.AreEqual(bonus, sheet.HouseholdBonus, "no other family has everybody here yet");
        Assert.AreEqual(sheet.ResidentPoints + bonus, mission.Score, "the bonus is in the score");
    }

    [UnityTest]
    public IEnumerator A_household_with_somebody_still_out_there_earns_nothing_yet()
    {
        var people = First(3);
        foreach (var s in people) mission.Roster.Find(s).household = "Testers";
        evac.Desk.secondsPerResident = 0.4f;

        // Only two of the three come to the hall.
        yield return TakeAll(people.GetRange(0, 2));
        ArriveAtTheHall(boat);
        yield return WaitUntil(() => mission.Evacuated == 2, 20f, "two to be registered");

        var sheet = mission.Sheet;
        var family = sheet.Households.Find(h => h.name == "Testers");
        Assert.IsNotNull(family);
        Assert.IsFalse(family.Reunited);
        Assert.AreEqual(2, family.registered);
        Assert.AreEqual(3, family.members);
        Assert.AreEqual(0, sheet.HouseholdBonus);
    }

    // ------------------------------------------------------------------ the end

    [UnityTest]
    public IEnumerator When_time_runs_out_those_already_ashore_are_still_registered_and_those_aboard_are_not()
    {
        var people = First(3);
        evac.Desk.secondsPerResident = 0.6f;

        // Two have been delivered; the third is aboard, out in the street, when the clock runs out.
        yield return TakeAll(people.GetRange(0, 2));
        ArriveAtTheHall(boat);
        yield return WaitUntil(() => boat.Passengers == 0, 10f, "the boat to unload");
        yield return WaitUntil(() => evac.Desk.Pending > 0 || mission.Evacuated > 0, 10f, "them to join the line");

        BackToTheStreet();
        yield return WaitSeconds(0.3f);
        yield return TakeAboard(boat, people[2], 1f);

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => mission.ReportVisible, 20f, "the report");

        Assert.IsTrue(mission.TimeIsUp);
        Assert.IsTrue(mission.GameOver);
        Assert.AreEqual(ResidentStatus.Registered, mission.Roster.Find(people[0]).status, "ashore in time, so on the list");
        Assert.AreEqual(ResidentStatus.Registered, mission.Roster.Find(people[1]).status);
        Assert.AreEqual(ResidentStatus.Aboard, mission.Roster.Find(people[2]).status, "still on the boat");
        Assert.AreEqual(2, mission.Evacuated);
        Assert.AreEqual("Mission over", mission.ReportTitle);
        StringAssert.Contains("Time ran out", mission.Report);
        StringAssert.Contains("still on the boat", mission.Report);
    }

    [UnityTest]
    public IEnumerator Nobody_is_let_off_a_boat_once_the_mission_is_over()
    {
        var people = First(1);
        yield return TakeAll(people);

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => mission.ReportVisible, 10f, "the report");
        Time.timeScale = 1f;

        ArriveAtTheHall(boat);
        yield return WaitSeconds(2f);

        Assert.AreEqual(1, boat.Passengers, "the mission is over: they stay aboard");
        Assert.AreEqual(ResidentStatus.Aboard, mission.Roster.Find(people[0]).status);
        Assert.AreEqual(0, mission.Evacuated);
    }

    [UnityTest]
    public IEnumerator The_final_score_adds_the_bonuses_for_priority_and_for_a_clean_run()
    {
        var people = First(2);
        evac.Desk.secondsPerResident = 0.3f;
        yield return TakeAll(people);
        ArriveAtTheHall(boat);
        yield return WaitUntil(() => mission.Evacuated == 2, 20f, "both to be registered");

        var live = mission.Sheet;
        Assert.AreEqual(0, live.PriorityBonus, "bonuses for how the mission was run are worked out at the end");
        Assert.AreEqual(0, live.CleanRunBonus);
        int before = mission.Score;

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => mission.ReportVisible, 20f, "the report");

        var rules = MissionDefinition.Current.rules;
        Assert.IsTrue(mission.Sheet.IsFinal);
        Assert.AreEqual(rules.cleanRunBonus, mission.Sheet.CleanRunBonus, "nothing hit, nobody shocked: a clean run");
        Assert.Greater(mission.Sheet.PriorityBonus, 0);
        Assert.AreEqual(before + mission.Sheet.PriorityBonus + mission.Sheet.CleanRunBonus, mission.Score);
        StringAssert.Contains("Clean run", mission.Report);
    }

    [UnityTest]
    public IEnumerator A_mark_in_the_safety_record_costs_the_clean_run_bonus_and_is_in_the_report()
    {
        var people = First(1);
        evac.Desk.secondsPerResident = 0.3f;
        yield return TakeAll(people);

        boat.Shock(0.1f, "test");                                   // the boat ran into a live wire
        ArriveAtTheHall(boat);
        yield return WaitUntil(() => mission.Evacuated == 1, 20f, "them to be registered");
        SetTimeLeft(0.05f);
        yield return WaitUntil(() => mission.ReportVisible, 20f, "the report");

        Assert.AreEqual(0, mission.Sheet.CleanRunBonus);
        Assert.AreEqual(1, mission.Safety().Shocks);
        StringAssert.Contains("shock", mission.Report);
        StringAssert.DoesNotContain("Clean run - nothing hit", mission.Report);
    }
}
