using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// The level that goes with the shipped mission: that every resident on the roster stands where their anchor says, in
/// the state the mission gives them, that the hazards are where the briefing says they are, and that nobody is
/// placed out of everybody's reach. Played in the real Gameplay scene, untouched.
/// </summary>
public class MissionLevelTests
{
    MissionDefinition mission;
    SimpleBoat boat;

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();
        mission = MissionDefinition.Current;
        boat = Boat();
        yield return WaitSeconds(0.3f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    Survivor Resident(string id)
    {
        var spec = mission.residents.First(r => r.id == id);
        var survivor = Survivor.All.FirstOrDefault(s => s.residentName == spec.displayName);
        Assert.IsNotNull(survivor, spec.displayName + " is not in the scene");
        return survivor;
    }

    // ------------------------------------------------------------------ the roster is in the world

    [UnityTest]
    public IEnumerator Every_resident_on_the_roster_is_in_the_scene_and_the_scene_has_nobody_else()
    {
        Assert.AreSame(MissionDefinition.LoadShipped(), mission, "the scene is playing the mission that ships");
        Assert.AreEqual(mission.residents.Count, Survivor.All.Count);
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.TotalSurvivors);
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.Roster.Count);
        Assert.AreEqual(mission.timeLimit, RescueManager.Instance.timeLimit, "the clock is the mission's");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Each_anchor_exists_and_its_resident_starts_there_in_the_state_the_mission_gives_them()
    {
        foreach (var spec in mission.residents)
        {
            var anchor = GameObject.Find(spec.anchor);
            Assert.IsNotNull(anchor, "the scene has no '" + spec.anchor + "' for " + spec.displayName);

            var survivor = Resident(spec.id);
            Assert.AreEqual(spec.situation, survivor.situation, spec.displayName);
            Assert.AreEqual(spec.needs, survivor.needs, spec.displayName);
            Assert.AreEqual(spec.household, survivor.household, spec.displayName);

            Vector3 here = survivor.transform.position, there = anchor.transform.position;
            float apart = Vector3.Distance(new Vector3(here.x, 0f, here.z), new Vector3(there.x, 0f, there.z));
            Assert.Less(apart, 1.5f, spec.displayName + " is " + apart.ToString("0.0") + " m from their anchor at the start");

            var visual = survivor.GetComponent<SurvivorVisual>();
            Assert.AreEqual(spec.characterId, visual.Character.id, spec.displayName + " is the wrong person");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Residents_on_a_roof_stand_on_one_and_those_in_the_water_stand_in_it()
    {
        foreach (var spec in mission.residents)
        {
            var s = Resident(spec.id);
            float y = s.transform.position.y;
            if (spec.situation == Situation.Rooftop) Assert.Greater(y, 3f, spec.displayName + " is not up on a roof");
            else Assert.Less(Mathf.Abs(y), 0.6f, spec.displayName + " is not at the water");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Nobody_on_a_roof_is_further_than_a_thrown_line_from_the_street()
    {
        foreach (var spec in mission.residents.Where(r => r.situation == Situation.Rooftop))
        {
            var s = Resident(spec.id);
            // From the middle of the main street (z = -6.5) straight across to them.
            Vector3 street = new Vector3(s.transform.position.x, 0.5f, -6.5f);
            Assert.Less(Vector3.Distance(street, s.SightPoint), 12f, spec.displayName + " is out of a thrown line's range from the street");
            Assert.IsTrue(LineOfSight.Clear(street + Vector3.up * 1.4f, s.SightPoint), spec.displayName + " cannot be seen from the street");
        }
        yield return null;
    }

    // ------------------------------------------------------------------ the hazards are where the briefing says

    [UnityTest]
    public IEnumerator The_live_wire_is_across_the_junction_and_has_exactly_the_Bautistas_in_it()
    {
        Assert.AreEqual(1, LiveWireZone.All.Count);
        yield return WaitSeconds(0.3f);

        var inside = Survivor.All.Where(s => s.InLiveWire).Select(s => s.household).ToList();
        Assert.AreEqual(2, inside.Count, "two people are waiting in the live water");
        Assert.IsTrue(inside.All(h => h == "Bautista"), "and they are the Bautistas");

        // It spans the street, so the boat cannot go west without going through it.
        var zone = LiveWireZone.All[0];
        Assert.Greater(zone.radius * 2f, 11f, "the zone is as wide as the street");
        Assert.IsTrue(zone.Contains(new Vector3(zone.transform.position.x, 0f, -2f)));
        Assert.IsTrue(zone.Contains(new Vector3(zone.transform.position.x, 0f, -11f)));
        Assert.IsNotNull(zone.cableFrom, "the cable comes from a pole");
    }

    [UnityTest]
    public IEnumerator The_live_wire_can_be_called_in_from_the_start_with_the_Radyo()
    {
        var zone = LiveWireZone.All[0];
        Assert.LessOrEqual(zone.DistanceToEdge(boat.transform.position), zone.requestRange,
            "the boat starts out of the Radyo's reach of the wire, so the cut could not be asked for until the crew had travelled");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_Santos_are_behind_the_jam_where_the_boat_cannot_go_but_a_line_can_reach()
    {
        var jam = DebrisBarrier.All.Single();
        var rosa = Resident("rosa");
        var migo = Resident("migo");
        yield return WaitSeconds(0.2f);

        // The nearest the boat can get is the jam's face on the main street side; they are the other side of it.
        Vector3 face = jam.transform.position + new Vector3(0f, 0.5f, -jam.size.z * 0.5f);
        foreach (var s in new[] { rosa, migo })
        {
            Assert.Greater(s.transform.position.z, jam.transform.position.z + jam.size.z * 0.5f, s.DisplayName + " is not behind the jam");
            float reach = Vector3.Distance(face, s.SightPoint);
            Assert.Greater(reach, 4.2f, s.DisplayName + " is in pick-up range of the boat from the jam: they need no line");
            Assert.Less(reach, 11f, s.DisplayName + " is out of a thrown line's range from the jam");
            Assert.IsTrue(LineOfSight.Clear(face + Vector3.up * 1.0f, s.SightPoint), "the jam hides " + s.DisplayName);
        }
    }

    [UnityTest]
    public IEnumerator The_water_rises_as_the_mission_says()
    {
        Assert.IsNotNull(FloodRise.Instance, "the mission says the water rises, and nothing raises it");
        Assert.AreEqual(mission.waterRisePerMinute, FloodRise.Instance.risePerMinute, 1e-5f);
        Assert.AreEqual(mission.maxWaterRise, FloodRise.Instance.maxRise, 1e-5f);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Wreckage_is_afloat_in_the_street_from_the_start()
    {
        yield return WaitSeconds(0.5f);
        Assert.Greater(FloatingDebris.All.Count, 8, "the street should have wreckage floating in it");
    }

    // ------------------------------------------------------------------ playing it

    [UnityTest]
    public IEnumerator At_the_start_somebody_is_in_sight_and_somebody_is_not()
    {
        yield return WaitSeconds(1f);
        int seen = Survivor.All.Count(s => s.Discovered);
        Assert.Greater(seen, 0, "the crew should see somebody from where they start");
        Assert.Less(seen, mission.residents.Count / 2, "most of the roster should still have to be found");
    }

    [UnityTest]
    public IEnumerator The_residents_nearest_the_start_are_the_ones_who_wear_out_fastest()
    {
        // The girl adrift in the street goes critical first, so the crew meet her before anybody who can wait.
        Vector3 start = boat.transform.position;
        var joy = Resident("joy");
        var nearest = Survivor.All.OrderBy(s => Vector3.Distance(start, s.transform.position)).First();
        Assert.IsTrue(nearest == joy || Vector3.Distance(start, joy.transform.position) < 25f, "Joy should be close to the start");

        float joysMinutes = joy.Condition.Condition01 / joy.Condition.DrainPerMinute;
        foreach (var s in Survivor.All)
            if (s != joy && s.situation != Situation.OpenWater)
                Assert.Greater(s.Condition.Condition01 / Mathf.Max(s.Condition.DrainPerMinute, 1e-4f), joysMinutes * 0.6f,
                    s.DisplayName + " would be critical before the child adrift in open water");
        yield return null;
    }
}
