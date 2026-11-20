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
        // The crew sets out from the hall (RescueBase_Spawn): the drop-off is a short run from the boat, but not the start itself.
        Assert.Greater(byWater, 8f, "the boat starts inside the hall's drop-off");
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

    /// <summary>
    /// The jam across A. Mapalad Street. On a map drawn bigger than real there is more wreckage, piled across the alleys that
    /// would have led round it ("Debris jam (alley N)"); this is the one in the lane itself.
    /// </summary>
    static DebrisBarrier TheJam()
    {
        var jam = DebrisBarrier.All.Where(b => b != null && b.name == "Debris jam (A. Mapalad Street)").ToList();
        Assert.AreEqual(1, jam.Count, "there should be exactly one jam across A. Mapalad Street");
        return jam[0];
    }

    [UnityTest]
    public IEnumerator The_Santos_are_behind_the_jam_where_the_boat_cannot_go_but_a_line_can_reach()
    {
        var jam = TheJam();
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
        var jam = TheJam();
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
            float apart = HorizontalDistance(hall, p + Vector3.up * 1f);         // to the hall's front wall, at shoulder height
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

    /// <summary>
    /// The hall's solid parts: the colliders of the hall model, or of whatever model is nearest the landing. Each is the hall's drawn
    /// shape (a mesh), so distances to it are measured with rays (<see cref="HorizontalDistance"/>), not ClosestPoint, which only
    /// works on boxes, spheres and convex shapes.
    /// </summary>
    static Collider[] FindHall()
    {
        var map = GameObject.Find("BarangayMock");
        var named = map.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.Contains("Hall_Barangay67"));
        if (named != null && named.GetComponentsInChildren<Collider>().Length > 0) return named.GetComponentsInChildren<Collider>();

        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        Vector3 landing = evac.landingArea.position + Vector3.up * 1f;
        Collider nearest = null;
        float best = float.PositiveInfinity;
        foreach (var c in Physics.OverlapSphere(landing, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody != null || !c.transform.IsChildOf(map.transform)) continue;
            float d = HorizontalDistance(new[] { c }, landing);
            if (d < best) { best = d; nearest = c; }
        }
        Assert.IsNotNull(nearest, "there is no hall to land at");
        var model = nearest.transform;                                  // the whole model that part belongs to
        while (model.parent != null && model.parent.parent != map.transform) model = model.parent;
        return model.GetComponentsInChildren<Collider>();
    }

    /// <summary>How far, across the ground, a point is from the nearest of these colliders at its own height (rays all round).</summary>
    static float HorizontalDistance(Collider[] solids, Vector3 p, float range = 10f)
    {
        float best = float.PositiveInfinity;
        for (int k = 0; k < 72; k++)
        {
            float a = k * Mathf.PI * 2f / 72f;
            var ray = new Ray(p, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            foreach (var c in solids)
                if (c.Raycast(ray, out RaycastHit hit, range) && hit.distance < best) best = hit.distance;
        }
        return best;
    }

    // ------------------------------------------------------------------ no hidden walls

    [UnityTest]
    public IEnumerator The_world_has_an_edge_the_boat_cannot_sail_off()
    {
        // The edge is a levee you can see, not an invisible wall: drawn, solid where it is drawn, round the start, above the flood.
        var edge = GameObject.Find(EdgeName);
        Assert.IsNotNull(edge, "the scene has no edge round the map");
        var bank = edge.transform.Find("Levee bank");
        Assert.IsNotNull(bank, "the edge is a levee");
        var drawn = bank.GetComponent<MeshRenderer>();
        var solid = bank.GetComponent<MeshCollider>();
        Assert.IsTrue(drawn != null && drawn.enabled, "the levee is drawn");
        Assert.IsTrue(solid != null && Same(drawn.bounds, solid.bounds, 0.05f), "and solid exactly where it is drawn");
        Assert.IsEmpty(edge.GetComponentsInChildren<BoxCollider>(), "no invisible walls");

        float floodTop = mission.maxWaterRise;
        Assert.Greater(drawn.bounds.max.y, floodTop + 1f, "the levee's crest stands well above the highest flood");

        // Straight out from the start in every direction, the boat runs into the bank, which is there to be seen.
        Vector3 start = boat.transform.position + Vector3.up * 0.3f;
        for (int k = 0; k < 16; k++)
        {
            float a = k * Mathf.PI * 2f / 16f;
            var ray = new Ray(start, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            // The boat meets the levee's bank, or before it ground rising out of the flood (the terrain's high side).
            bool meetsBank = solid.Raycast(ray, out _, 2000f);
            bool land = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore).Any(h => h.collider.name == "Ground");
            Assert.IsTrue(meetsBank || land, "heading " + (k * 22.5f) + "°, the boat would never meet the edge");
        }
        yield return null;
    }

    const string EdgeName = "World Edge";

    [UnityTest]
    public IEnumerator Every_solid_part_of_the_map_is_something_you_can_see()
    {
        // Something stops the boat only where something is drawn: each solid part's shape is the mesh drawn there (a house, a block,
        // a landmark, the levee), or a tree's thin trunk inside a drawn tree, or a drawn post. No invisible boxes round models.
        int checkedParts = 0;
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c.isTrigger || !c.enabled || c.attachedRigidbody != null) continue;
            string root = c.transform.root.name;
            if (root != "BarangayMock" && root != EdgeName && root != "Posts") continue;
            checkedParts++;

            string what = root + " > " + c.transform.parent?.name + " > " + c.name + " (" + c.GetType().Name + ")";
            if (c is MeshCollider)
            {
                // In play mode Unity draws static parts from one combined mesh, so compare where the shape is, not which mesh
                // asset it is: the solid part covers exactly what is drawn on the same object.
                var drawn = c.GetComponent<MeshRenderer>();
                Assert.IsTrue(drawn != null && drawn.enabled, what + " is solid but nothing is drawn there");
                // 25 cm of slack: a model's stored drawing box can lag its real vertices a little (more on a map drawn bigger than
                // real, MabuhayGameplayBuilder.MapScale). An invisible box round a model is off by metres.
                var scale = c.transform.lossyScale;
                // Half a metre: in play a static part's drawn box is the tight box of its batched vertices, while a turned
                // collider keeps the looser box of its turned bounds; for an 8 m shop at 60 degrees that is about 0.6 m.
                float slack = (0.5f + 0.08f * Mathf.Max(c.bounds.extents.x, c.bounds.extents.z) * 2f) * Mathf.Max(1f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
                if (drawn.isPartOfStaticBatch)
                {
                    // Batched, the drawn box is the tight box of its vertices while a turned collider keeps the looser box
                    // of its turned mesh (an L-shaped house at 35 degrees: most of a metre). So: the solid box holds what
                    // is drawn, and is not much bigger than it - an invisible box round a model is off by metres.
                    var room = c.bounds; room.Expand(0.1f);
                    Assert.IsTrue(room.Contains(drawn.bounds.min) && room.Contains(drawn.bounds.max), what + " does not cover what is drawn there (" + c.bounds + " vs " + drawn.bounds + ")");
                    for (int axis = 0; axis < 3; axis++)
                        Assert.LessOrEqual(c.bounds.size[axis], drawn.bounds.size[axis] * 1.5f + 1f, what + " is much bigger than what is drawn there (" + c.bounds + " vs " + drawn.bounds + ")");
                }
                else
                    Assert.IsTrue(Same(drawn.bounds, c.bounds, slack), what + " is not the shape drawn there (" + c.bounds + " vs " + drawn.bounds + ")");
            }
            else if (c.gameObject.layer == 2)
                Assert.IsNotNull(c.transform.parent?.GetComponentInChildren<MeshRenderer>(), what + " is a trunk with no tree");
            else
            {
                var drawn = c.GetComponent<MeshRenderer>();
                Assert.IsTrue(drawn != null && drawn.enabled, what + " is solid but nothing is drawn there");
                Assert.IsTrue(Grown(drawn.bounds, 0.05f).Contains(c.bounds.min) && Grown(drawn.bounds, 0.05f).Contains(c.bounds.max),
                              what + " is bigger than what is drawn");
            }
        }
        Assert.Greater(checkedParts, 1000, "the map's houses and blocks are solid");
        yield return null;
    }

    static Bounds Grown(Bounds b, float by) { b.Expand(by * 2f); return b; }

    /// <summary>
    /// The solid part and the drawn part are the same shape: the solid one sits inside what is drawn and fills nearly all of it.
    /// (For a turned model the box Unity keeps round the drawing is a little looser than the collider's own, so not equal.)
    /// </summary>
    static bool Same(Bounds drawn, Bounds solid, float tolerance)
    {
        var room = drawn;
        room.Expand(tolerance * 2f);
        if (!room.Contains(solid.min) || !room.Contains(solid.max)) return false;
        for (int axis = 0; axis < 3; axis++)
            if (solid.size[axis] < drawn.size[axis] * 0.9f - tolerance) return false;
        return true;
    }

    [UnityTest]
    public IEnumerator Open_ground_in_the_big_lots_is_water_the_boat_can_get_to()
    {
        // The school yard, WalterMart's open ground, the church's compound and the hospital's: once walled off by a box round
        // each model, now water the boat reaches from its start wherever nothing is drawn.
        var landmarks = GameObject.Find("BarangayMock").transform.Find("Landmarks");
        int fullyChecked = 0;
        foreach (var model in new[] { "LM_MabuhayElementary", "LM_WalterMart", "LM_IglesiaNiCristo", "LM_CarmonaHospital" })
        {
            var lot = landmarks.Cast<Transform>().FirstOrDefault(t => t.name.Contains("(" + model + ")"));
            Assert.IsNotNull(lot, model + " is on the map");
            var drawn = lot.GetComponentsInChildren<MeshRenderer>();
            Bounds b = drawn[0].bounds;
            foreach (var r in drawn) b.Encapsulate(r.bounds);

            Vector3 start = boat.transform.position;
            // The flood area is the lot, the start and room round them to go the long way: with the start beside the hall
            // the boat's way to the church goes round a block, outside a box drawn tight round the two.
            const float room = 150f;
            var area = Rect.MinMaxRect(Mathf.Min(b.min.x, start.x) - room, Mathf.Min(b.min.z, start.z) - room,
                                       Mathf.Max(b.max.x, start.x) + room, Mathf.Max(b.max.z, start.z) + room);
            var water = new BoatReach(area, 1f, BoatReach.DefaultRadius, 0f);
            Physics.SyncTransforms();
            water.Flood(start);

            int roomy = 0, reached = 0;
            for (float x = b.min.x + 1f; x < b.max.x; x += 2f)
            for (float z = b.min.z + 1f; z < b.max.z; z += 2f)
            {
                var above = new Vector3(x, b.max.y + 1f, z);
                // Something drawn is there, or the ground is too near the surface to float a boat (on the terrain's slopes).
                if (Physics.Raycast(above, Vector3.down, above.y + 0.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var xz = new Vector2(x, z);
                if (!water.IsClear(xz)) continue;                                                                 // too tight for a boat
                roomy++;
                if (water.Reached(xz)) reached++;
            }
            // Too little open water in the box to need the full check: a lot on high ground (the school is raised as an
            // evacuation site) or on a rise is dry or too shallow for the boat, and at real size a model can fill most of
            // its lot. Whatever yard it has must still be water the boat reaches.
            if (roomy <= 100)
            {
                if (roomy > 20) Assert.GreaterOrEqual(reached, roomy * 0.85f, model + ": the boat reaches " + reached + " of " + roomy + " open places in its small yard");
                continue;
            }
            Assert.Greater(roomy, 100, model + ": there is open ground in its lot");
            // 85 %: the box round a big model reaches past its graded lot onto the slope round it, where a few pockets lie
            // behind ground too shallow for the boat (TerrainBuilder). On flat ground it was 90 %.
            Assert.GreaterOrEqual(reached, roomy * 0.85f, model + ": the boat reaches " + reached + " of " + roomy + " open places in its lot");
            fullyChecked++;
        }
        Assert.Greater(fullyChecked, 0, "at least one big lot (WalterMart's) has open water to check");
        yield return null;
    }

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
        // The crew sets out from the hall, so the first residents they meet are the ones just west of it: the grandfather on
        // his post and the hurt man adrift. The girl adrift farther along the street still goes critical before anybody who
        // can wait, so the crew has a reason to press on to her.
        Vector3 start = boat.transform.position;
        foreach (var near in new[] { "ador", "lito" })
            Assert.Less(Vector3.Distance(start, Resident(near).transform.position), 25f, near + " should be close to the hall where the crew starts");
        var joy = Resident("joy");

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

    [UnityTest]
    public IEnumerator The_crew_starts_beside_the_hall_clear_of_its_drop_off_zone()
    {
        // The rescuers set out from the Barangay Hall, the evacuation area, not from a random spot on the street.
        var marker = GameObject.Find("RescueBase_Spawn");
        Assert.IsNotNull(marker, "the RescueBase_Spawn marker the intro and the start share");
        Assert.Less(Vector3.Distance(Flat3(marker.transform.position), Flat3(boat.transform.position)), 0.5f, "the boat is at the marker (the boat floats a little above or below it)");

        var zone = Hall().GetComponent<Collider>();
        Vector3 start = boat.transform.position;
        float outside = Vector3.Distance(zone.ClosestPoint(start), start);
        Assert.GreaterOrEqual(outside, 5f, "the boat must start outside the drop-off, or its first passenger would be unloaded at once");
        Assert.Less(Vector3.Distance(Flat3(zone.bounds.center), Flat3(start)), 30f, "and beside the hall");
        yield return null;
    }

    static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
