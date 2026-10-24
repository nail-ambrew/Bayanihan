using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// A whole mission on the Mabuhay map, start to finish, the way MissionFlowTests plays Manila's: climb aboard, pick everybody up,
/// take them to the Barangay Hall in as many trips as it takes, see each of them registered, read the report, and try again. The
/// boat is put where it needs to be, so this is quick; driving every metre of it with a keyboard is MabuhayBotTests.
/// </summary>
public class MabuhayFlowTests
{
    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    [UnityTest]
    public IEnumerator Every_resident_is_rescued_registered_and_reported_then_the_mission_can_be_retried()
    {
        var mission = RescueManager.Instance;
        var boat = Boat();
        var player = Player();
        var evac = Hall();
        Assert.IsNotNull(evac.Desk, "the evacuation centre has no registration desk");

        int residents = mission.TotalSurvivors;
        Assert.AreEqual(10, residents);
        evac.Desk.secondsPerResident = 0.5f;    // quicker than the real thing; the procedure is the same

        // ---- rescue everybody, a boatload at a time, with the player at the helm ----
        Board(player);
        Vector3 start = boat.transform.position;
        Vector3 startHeading = boat.HeadingForward;
        var people = new List<CharacterAnimator>();
        var remaining = new List<Survivor>(Waiting());
        int trips = 0;
        while (remaining.Count > 0)
        {
            trips++;
            Teleport(boat, start, startHeading);
            yield return WaitSeconds(0.3f);

            float side = 1f;
            int load = Mathf.Min(boat.capacity, remaining.Count);
            for (int i = 0; i < load; i++)
            {
                var resident = remaining[0];
                remaining.RemoveAt(0);
                people.Add(resident.GetComponent<SurvivorVisual>().Animator);
                yield return TakeAboard(boat, resident, side);
                side = -side;
            }
            yield return null;

            Assert.AreEqual(load, boat.Passengers);
            Assert.IsFalse(mission.GameOver);
            Assert.AreEqual(load + 2, boat.GetComponentsInChildren<CharacterAnimator>().Length,
                "everyone rescued, the crew member and the player should all be on the boat");

            // ---- to the Barangay Hall, slowly enough to let them off ----
            int safeAfter = mission.PickedUp;
            ArriveAtTheHall(boat);
            yield return WaitUntil(() => boat.Passengers == 0, 10f, "the boat to unload at the hall");
            Assert.Less(mission.Evacuated, safeAfter, "stepping ashore is not being safe: they still have to be registered");

            yield return WaitUntil(() => mission.Evacuated == safeAfter, 30f, "the desk to register everybody who came ashore");
        }

        Assert.Greater(trips, 0);
        Assert.AreEqual(3, trips, "ten residents and a boat for four make three trips");
        Assert.AreEqual(residents, mission.PickedUp);
        Assert.AreEqual(residents, mission.Evacuated);
        Assert.IsTrue(mission.GameOver, "everybody is registered, so the mission is over");
        Assert.IsFalse(mission.TimeIsUp);
        foreach (var record in mission.Roster.Records)
            Assert.AreEqual(ResidentStatus.Registered, record.status, record.name + " should be on the list");

        // ---- the report, once the last of them has been written down ----
        yield return WaitUntil(() => Time.timeScale == 0f, 15f, "the report to cover the screen and pause the game");
        Assert.IsTrue(mission.ReportVisible);

        Assert.IsTrue(mission.Sheet.IsFinal, "the report's score includes the end-of-mission bonuses");
        Assert.AreEqual(mission.Sheet.Total, mission.Score);
        int points = 0;
        foreach (var record in mission.Roster.Records) points += record.points;
        Assert.AreEqual(points, mission.Sheet.ResidentPoints, "each resident's points are the ones they were registered with");
        Assert.Greater(points, 0);

        StringAssert.Contains("Evacuated " + residents + " of " + residents + " residents.", mission.Report);
        StringAssert.Contains("RESIDENTS", mission.Report);
        Assert.AreEqual("ALL RESIDENTS SAFE! Bayanihan!", mission.ReportTitle);

        foreach (var person in people)
        {
            Assert.IsNull(person.transform.parent, person.name + " should be ashore, not still on the boat");
            Assert.AreEqual(CharacterPose.Idle, person.Pose, person.name + " was still walking ashore when the report covered the screen and stopped time");
        }

        // ---- everybody who came ashore is on the porch, in front of the hall ----
        foreach (var person in people)
        {
            float apart = Vector3.Distance(new Vector3(person.transform.position.x, 0f, person.transform.position.z),
                                           new Vector3(evac.landingArea.position.x, 0f, evac.landingArea.position.z));
            Assert.Less(apart, 5f, person.name + " is " + apart.ToString("0.0") + " m from the landing: not at the hall");
        }

        // ---- try again: what R on the report screen does ----
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        yield return null;
        yield return null;

        mission = RescueManager.Instance;
        boat = Boat();
        player = Player();
        Assert.AreEqual(MabuhayTestHelpers.SceneName, SceneManager.GetActiveScene().name, "R reloads the Mabuhay map, not another");
        Assert.AreSame(MapChoice.Find(MapChoice.MabuhayId).LoadMission(), MissionDefinition.Current, "and its mission");
        Assert.IsFalse(mission.GameOver, "a new mission should not start over");
        Assert.AreEqual(1f, Time.timeScale, "the game should be running again");
        Assert.AreEqual(residents, mission.TotalSurvivors);
        Assert.AreEqual(0, mission.Evacuated);
        Assert.AreEqual(0, mission.PickedUp);
        Assert.AreEqual(0, mission.Score);
        Assert.AreEqual(0, boat.Passengers);
        Assert.IsFalse(player.IsMounted, "the player starts in the water");
        Assert.AreEqual(residents, Waiting().Length, "everybody is waiting to be rescued again");
        Assert.AreEqual(0, mission.Desk.Pending, "nobody is waiting at the desk");
        Assert.AreEqual(start.x, boat.transform.position.x, 2f, "the boat is back where it started");
        Assert.AreEqual(start.z, boat.transform.position.z, 2f);
    }

    [UnityTest]
    public IEnumerator When_time_runs_out_those_still_aboard_are_not_counted_and_R_still_restarts()
    {
        var mission = RescueManager.Instance;
        var boat = Boat();
        Board(Player());

        yield return TakeAboard(boat, Waiting()[0], 1f);
        Assert.AreEqual(1, boat.Passengers);

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => mission.ReportVisible, 15f, "the report");
        Assert.IsTrue(mission.TimeIsUp);
        Assert.AreEqual(0, mission.Evacuated, "nobody was taken to the hall");
        Assert.AreEqual("Mission over", mission.ReportTitle);
        Assert.AreEqual(ResidentStatus.Aboard, mission.Roster.Records.Find(r => r.PickedUp).status, "the one aboard is still aboard: the boat is not unloaded after the clock runs out");
    }
}
