using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// The level that goes with the Mabuhay mission, as MissionLevelTests is for Manila's: that every resident stands where their
/// anchor says, in the state the mission gives them, that the hazards are where the briefing says, that the hall can be reached,
/// and that nobody is placed out of everybody's reach, inside a house, or under a roof. The reach tests ask the physics engine
/// where a boat can really go (BoatReach), so they hold for whatever models the map is built from.
/// </summary>
public class MabuhayLevelTests
{
    MissionDefinition mission;
    SimpleBoat boat;
    BoatReach reach;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        mission = MissionDefinition.Current;
        boat = Boat();
        yield return WaitSeconds(0.3f);
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    Survivor Resident(string id)
    {
        var spec = mission.residents.First(r => r.id == id);
        var survivor = Survivor.All.FirstOrDefault(s => s.residentName == spec.displayName);
        Assert.IsNotNull(survivor, spec.displayName + " is not in the scene");
        return survivor;
    }

    /// <summary>Where the boat can get to from its start, with the jam in place. A wide patch of the map, so a long way round is found if there is one.</summary>
    BoatReach WaterFromTheStart()
    {
        if (reach != null) return reach;
        reach = new BoatReach(Rect.MinMaxRect(-210f, -140f, 260f, 140f), 1f, BoatReach.DefaultRadius, 0f);
        Physics.SyncTransforms();
        int cells = reach.Flood(boat.transform.position);
        Assert.Greater(cells, 5000, "the boat is hemmed in at its start");
        return reach;
    }

    static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

    // ------------------------------------------------------------------ the roster is in the world

    [UnityTest]
    public IEnumerator Every_resident_on_the_roster_is_in_the_scene_and_the_scene_has_nobody_else()
    {
        Assert.AreEqual(10, mission.residents.Count);
        Assert.AreEqual(mission.residents.Count, Survivor.All.Count);
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.TotalSurvivors);
        Assert.AreEqual(mission.residents.Count, RescueManager.Instance.Roster.Count);
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
    public IEnumerator The_roster_is_the_same_ten_people_as_in_Manila_in_the_same_trouble()
    {
        var manila = MissionDefinition.LoadShipped();
        Assert.AreEqual(manila.residents.Count, mission.residents.Count);
        foreach (var m in manila.residents)
        {
            var here = mission.residents.FirstOrDefault(r => r.id == m.id);
            Assert.IsNotNull(here, m.displayName + " is not in the Mabuhay mission");
            Assert.AreEqual(m.displayName, here.displayName);
            Assert.AreEqual(m.household, here.household, m.displayName);
            Assert.AreEqual(m.situation, here.situation, m.displayName);
            Assert.AreEqual(m.needs, here.needs, m.displayName);
            Assert.AreEqual(m.characterId, here.characterId, m.displayName);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Residents_on_a_roof_stand_on_one_and_those_in_the_water_stand_in_it()
    {
        float waterY = WaterSurface.Instance.transform.position.y;
        foreach (var spec in mission.residents)
        {
            var s = Resident(spec.id);
            float y = s.transform.position.y;
            if (spec.situation == Situation.Rooftop)
            {
                Assert.Greater(y - waterY, 3f, spec.displayName + " is not up on a roof");
                // There is a solid roof right under their feet.
                bool onSomething = Physics.Raycast(s.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.0f, ~0, QueryTriggerInteraction.Ignore);
                Assert.IsTrue(onSomething && hit.collider.attachedRigidbody == null, spec.displayName + " is standing on nothing");
                Assert.Less(hit.distance, 0.8f, spec.displayName + " floats " + (hit.distance - 0.5f).ToString("0.0") + " m over the roof");
            }
            else Assert.Less(Mathf.Abs(y - waterY), 0.6f, spec.displayName + " is not at the water");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Nobody_is_inside_a_house_or_under_a_roof()
    {
        foreach (var spec in mission.residents)
        {
            var s = Resident(spec.id);
            Vector3 feet = s.transform.position;

            foreach (var hit in Physics.OverlapSphere(feet + Vector3.up * 0.9f, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                Assert.IsFalse(hit.attachedRigidbody == null && hit.GetComponent<WaterSurface>() == null,
                               spec.displayName + " is standing inside '" + hit.name + "'");

            bool covered = Physics.Raycast(feet + Vector3.up * 1.9f, Vector3.up, out RaycastHit above, 15f, ~0, QueryTriggerInteraction.Ignore);
            Assert.IsFalse(covered && above.collider.attachedRigidbody == null, spec.displayName + " is under '" + (covered ? above.collider.name : "") + "'");
        }
        yield return null;
    }

    // ------------------------------------------------------------------ everybody can be got to

    [UnityTest]
    public IEnumerator Everyone_in_the_water_or_on_a_post_is_within_reach_of_water_the_boat_can_get_to()
    {
        var water = WaterFromTheStart();
        foreach (var spec in mission.residents.Where(r => r.situation == Situation.OpenWater || r.situation == Situation.Clinging))
        {
            var s = Resident(spec.id);
            float gap = water.DistanceToReached(Flat(s.transform.position), 30f);
            Assert.LessOrEqual(gap, 3.5f, spec.displayName + " is " + (float.IsInfinity(gap) ? "out of reach" : gap.ToString("0.0") + " m") +
                               " from the nearest water the boat can get to: the hull would not be in their pick-up range");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Everyone_on_a_roof_or_behind_the_jam_can_be_reached_by_a_line_from_water_the_boat_can_get_to()
    {
        var water = WaterFromTheStart();
        foreach (var spec in mission.residents.Where(r => r.situation == Situation.Rooftop || r.situation == Situation.Trapped))
        {
            var s = Resident(spec.id);
            Assert.IsTrue(water.ThrowPointFor(s.SightPoint, 0.66f + 1.4f, 11f, out Vector2 from),
                          spec.displayName + " is out of a thrown line's range, or hidden, from every place the boat can reach");
            Assert.LessOrEqual(Vector3.Distance(new Vector3(from.x, 2.06f, from.y), s.SightPoint), 11.1f);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_boat_can_reach_the_hall_by_water()
    {
        var water = WaterFromTheStart();
        var zone = Hall().GetComponent<Collider>();
        Vector2 centre = Flat(zone.bounds.center);

        Assert.IsTrue(water.Reached(centre) || water.DistanceToReached(centre, 3f) < 3f, "the boat cannot get into the hall's drop-off zone");
        float byWater = water.DistanceFromStart(centre);
        Assert.Less(byWater, 200f, "the hall is " + byWater.ToString("0") + " m from the start by water: further than the mission's clock allows for three trips");
        Assert.Greater(byWater, 50f, "the hall is too close to the start to be a journey");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Nobody_waits_at_the_edge_of_the_hall_drop_off_zone()
    {
        var zone = Hall().GetComponent<Collider>();
        var centre = zone.bounds.center;

        // The boat's centre has to get 2 m into the zone: its inner part must be clear of every resident's pick-up radius (4 m), or
        // a boat that went to pick them up could be "arrived" and unload in the middle of it.
        foreach (var s in Survivor.All)
        {
            float d = Vector3.Distance(new Vector3(s.transform.position.x, centre.y, s.transform.position.z), zone.ClosestPoint(new Vector3(s.transform.position.x, centre.y, s.transform.position.z)));
            Assert.Greater(d, 3f, s.DisplayName + " waits at the edge of the hall's drop-off zone");
        }
        yield return null;
    }

    // ------------------------------------------------------------------ the hazards are where the briefing says

    [UnityTest]
    public IEnumerator The_live_wire_is_across_the_street_and_has_exactly_the_Bautistas_in_it()
    {
        Assert.AreEqual(1, LiveWireZone.All.Count);
        yield return WaitSeconds(0.3f);

        var inside = Survivor.All.Where(s => s.InLiveWire).Select(s => s.household).ToList();
        Assert.AreEqual(2, inside.Count, "two people are waiting in the live water");
        Assert.IsTrue(inside.All(h => h == "Bautista"), "and they are the Bautistas");

        // It spans the street between its two posts, and the cable comes from the tall one.
        var zone = LiveWireZone.All[0];
        var pole = GameObject.Find("post_pole");
        var other = GameObject.Find("post_celia");
        Assert.IsNotNull(pole, "there is no pole for the cable");
        Assert.IsNotNull(other, "there is no post on the far kerb");
        Assert.IsTrue(zone.Contains(pole.transform.position), "the pole is outside the live water");
        Assert.IsTrue(zone.Contains(other.transform.position), "the far post is outside the live water");
        Assert.IsNotNull(zone.cableFrom, "the cable comes from a pole");
        Assert.Greater(zone.cableFrom.position.y, 5f, "the cable comes from high up");
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
    public IEnumerator The_hall_can_be_reached_without_going_through_the_live_water()
    {
        // The wire holds the Bautistas; it must not also be the only way to the hall. With its water counted as closed there is still a way.
        var zone = LiveWireZone.All[0];
        Assert.IsTrue(zone.IsLive);
        var dry = new BoatReach(Rect.MinMaxRect(-210f, -140f, 260f, 140f), 1f, BoatReach.DefaultRadius, 0f)
        {
            Avoid = xz => zone.DistanceToEdge(new Vector3(xz.x, 0f, xz.y)) < 2f
        };
        Physics.SyncTransforms();
        dry.Flood(boat.transform.position);
        Vector2 centre = Flat(Hall().GetComponent<Collider>().bounds.center);
        Assert.IsTrue(dry.Reached(centre) || dry.DistanceToReached(centre, 3f) < 3f, "the only way to the hall is through the live water");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_Santos_are_behind_the_jam_where_the_boat_cannot_go_but_a_line_can_reach()
    {
        var jam = DebrisBarrier.All.Single();
        var rosa = Resident("rosa");
        var migo = Resident("migo");
        yield return WaitSeconds(0.2f);

        // The boat comes up to the jam's west face, along the lane; they are the other side of it.
        Vector3 along = jam.transform.forward;                         // the way the lane runs, east
        Vector3 face = jam.transform.position - along * (jam.size.z * 0.9f * 0.5f) + Vector3.up * 0.5f;
        foreach (var s in new[] { rosa, migo })
        {
            Vector3 fromCentre = s.transform.position - jam.transform.position;
            Assert.Greater(Vector3.Dot(fromCentre, along), jam.size.z * 0.45f, s.DisplayName + " is not behind the jam");
            float reachFromFace = Vector3.Distance(face, s.SightPoint);
            Assert.Greater(reachFromFace, 4.2f, s.DisplayName + " is in pick-up range of the boat at the jam: they need no line");
            Assert.Less(reachFromFace, 11f, s.DisplayName + " is out of a thrown line's range from the jam");
            Assert.IsTrue(LineOfSight.Clear(face + Vector3.up * 0.5f, s.SightPoint), "the jam hides " + s.DisplayName);
        }
    }

    [UnityTest]
    public IEnumerator The_jam_is_wall_to_wall_so_the_boat_has_no_short_way_round_it()
    {
        var jam = DebrisBarrier.All.Single();
        yield return null;

        // Both ends of the pile are buried in the walls of the lane.
        Vector3 end = jam.transform.right * (jam.size.x * 0.92f * 0.5f + 0.15f);
        foreach (var sign in new[] { 1f, -1f })
        {
            Vector3 at = jam.transform.position + end * sign + Vector3.up * 0.5f;
            Assert.IsTrue(Physics.CheckSphere(at, 0.2f, ~0, QueryTriggerInteraction.Ignore), "there is a gap at an end of the jam: the boat could slip round it");
        }

        // And so the boat's way to the Santos, if there is one, is long.
        var water = WaterFromTheStart();
        foreach (var id in new[] { "rosa", "migo" })
        {
            var s = Resident(id);
            float byWater = water.Reached(Flat(s.transform.position)) ? water.DistanceFromStart(Flat(s.transform.position)) : float.PositiveInfinity;
            Assert.Greater(byWater, 150f, s.DisplayName + " can be reached by water in " + byWater.ToString("0") + " m: the jam does not cut them off");
        }
    }

    [UnityTest]
    public IEnumerator Everyone_clinging_holds_a_post_that_is_really_there()
    {
        foreach (var spec in mission.residents.Where(r => r.situation == Situation.Clinging))
        {
            var s = Resident(spec.id);
            var axis = s.GetComponent<SurvivorVisual>().postAxis;
            bool solid = Physics.OverlapSphere(new Vector3(axis.x, 0.5f, axis.z), 0.3f, ~0, QueryTriggerInteraction.Ignore)
                                .Any(c => c.attachedRigidbody == null && c is CapsuleCollider);
            Assert.IsTrue(solid, spec.displayName + " is clinging to nothing: there is no post at " + axis);
        }
        yield return null;
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
    public IEnumerator Wreckage_is_afloat_in_the_street_from_the_start_and_none_of_it_starts_in_front_of_the_hall()
    {
        yield return WaitSeconds(0.5f);
        Assert.Greater(FloatingDebris.All.Count, 8, "the street should have wreckage floating in it");

        Vector3 hall = Hall().GetComponent<Collider>().bounds.center;
        foreach (var piece in FloatingDebris.All)
        {
            float d = Vector2.Distance(Flat(piece.transform.position), Flat(hall));
            Assert.Greater(d, 25f, "a piece of wreckage starts " + d.ToString("0") + " m from the hall's drop-off, where the boat has to go slowly");
        }
    }

    // ------------------------------------------------------------------ the hall

    [UnityTest]
    public IEnumerator The_landing_is_on_the_porch_in_front_of_the_hall_and_clear_of_its_walls()
    {
        var evac = Hall();
        Assert.IsNotNull(evac.landingArea, "evacuees have nowhere to land");
        var hall = FindHall();

        for (int i = 0; i < 30; i++)
        {
            Vector3 p = evac.RandomLandingPoint();
            Vector3 onHall = hall.ClosestPoint(new Vector3(p.x, hall.bounds.center.y, p.z));
            float apart = Vector2.Distance(Flat(p), Flat(onHall));
            Assert.Less(apart, 3f, "the landing is " + apart.ToString("0.0") + " m from the hall's front: not on its porch");
            Assert.Greater(apart, 0.4f, "the landing is inside the hall's wall");
            Assert.IsFalse(Physics.CheckSphere(p + Vector3.up * 0.9f, 0.3f, ~0, QueryTriggerInteraction.Ignore), "somebody lands inside something at " + p);
        }

        // It is on the side of the hall that faces the street, which is where the drop-off zone is.
        Vector3 zone = evac.GetComponent<Collider>().bounds.center;
        Vector3 landing = evac.landingArea.position;
        Assert.Less(Vector2.Distance(Flat(landing), Flat(zone)), 9f, "the landing is too far from the drop-off for people to walk to");
        yield return null;
    }

    /// <summary>The hall's solid body: the collider of the hall model (or of the block that stands for it).</summary>
    static Collider FindHall()
    {
        var map = GameObject.Find("BarangayMock");
        var named = map.GetComponentsInChildren<BoxCollider>().FirstOrDefault(c => c.name.Contains("Hall_Barangay67"));
        if (named != null) return named;
        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        var nearest = Physics.OverlapSphere(evac.landingArea.position, 6f, ~0, QueryTriggerInteraction.Ignore)
                             .Where(c => c.attachedRigidbody == null && c.transform.IsChildOf(map.transform))
                             .OrderBy(c => Vector3.Distance(c.ClosestPoint(evac.landingArea.position), evac.landingArea.position)).FirstOrDefault();
        Assert.IsNotNull(nearest, "there is no hall to land at");
        return nearest;
    }

    [UnityTest]
    public IEnumerator The_world_has_an_edge_the_boat_cannot_sail_off()
    {
        var edge = GameObject.Find(EdgeName);
        Assert.IsNotNull(edge, "the scene has no walls round the map");
        var walls = edge.GetComponentsInChildren<BoxCollider>();
        Assert.AreEqual(4, walls.Length);

        Vector3 inside = boat.transform.position;
        foreach (var wall in walls)
            Assert.IsFalse(wall.bounds.Contains(inside), wall.name + " wall is on top of the boat's start");
        yield return null;
    }

    const string EdgeName = "World Edge";

    // ------------------------------------------------------------------ playing it

    [UnityTest]
    public IEnumerator At_the_start_somebody_is_in_sight_and_somebody_is_not()
    {
        yield return WaitSeconds(1f);
        int seen = Survivor.All.Count(s => s.Discovered);
        Assert.Greater(seen, 0, "the crew should see somebody from where they start");
        Assert.Less(seen, mission.residents.Count / 2 + 1, "most of the roster should still have to be found");
    }

    [UnityTest]
    public IEnumerator The_residents_nearest_the_start_are_the_ones_who_wear_out_fastest()
    {
        // The girl adrift ahead of the boat goes critical first, so the crew meet her before anybody who can wait.
        Vector3 start = boat.transform.position;
        var joy = Resident("joy");
        Assert.Less(Vector3.Distance(start, joy.transform.position), 25f, "Joy should be close to the start");

        float joysMinutes = joy.Condition.Condition01 / joy.Condition.DrainPerMinute;
        foreach (var s in Survivor.All)
            if (s != joy && s.situation != Situation.OpenWater)
                Assert.Greater(s.Condition.Condition01 / Mathf.Max(s.Condition.DrainPerMinute, 1e-4f), joysMinutes * 0.6f,
                    s.DisplayName + " would be critical before the child adrift in open water");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_first_trip_has_enough_close_to_the_start_to_fill_the_boat()
    {
        // The boat holds four, and four of the ten are within a short drive of where it starts.
        Vector3 start = boat.transform.position;
        int near = Survivor.All.Count(s => Vector3.Distance(start, s.transform.position) < 60f);
        Assert.GreaterOrEqual(near, boat.capacity, "fewer than " + boat.capacity + " residents are within 60 m of the start");
        yield return null;
    }
}
