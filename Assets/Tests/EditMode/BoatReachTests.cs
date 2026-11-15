using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Where a boat can go, worked out from the colliders: open water is open, a wall is a wall, a gap the boat does not fit through is
/// closed, a jam that is fixed in place blocks while something that floats does not, and a route goes round the end of a wall. The
/// colliders are made here, far from the map, and taken away again.
/// </summary>
public class BoatReachTests
{
    // Far from anything in any scene that happens to be open.
    static readonly Vector3 Origin = new Vector3(9000f, 0f, 9000f);
    readonly List<GameObject> made = new List<GameObject>();

    [TearDown]
    public void Take_the_colliders_away()
    {
        foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        made.Clear();
        Physics.SyncTransforms();
    }

    /// <summary>A wall, a house, a post: a box from a metre under the water up to <paramref name="height"/>, centred on a point of the test arena.</summary>
    GameObject Box(float x, float z, float width, float depth, float height = 4f, bool body = false, bool kinematic = false)
    {
        var go = new GameObject("test box");
        go.transform.position = Origin + new Vector3(x, height * 0.5f - 1f, z);
        go.AddComponent<BoxCollider>().size = new Vector3(width, height, depth);
        if (body)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = kinematic;
        }
        made.Add(go);
        return go;
    }

    static BoatReach Arena(float size = 80f) =>
        new BoatReach(new Rect(Origin.x - size / 2f, Origin.z - size / 2f, size, size), 1f, BoatReach.DefaultRadius, 0f);

    static Vector3 At(float x, float z) => Origin + new Vector3(x, 0f, z);
    static Vector2 Flat(float x, float z) => new Vector2(Origin.x + x, Origin.z + z);

    [Test]
    public void Open_water_is_reached_all_the_way_across_and_a_distance_is_about_the_straight_line()
    {
        var reach = Arena();
        Physics.SyncTransforms();
        int cells = reach.Flood(At(0f, 0f));

        Assert.Greater(cells, 80 * 80 * 0.95f, "open water should be open everywhere");
        Assert.IsTrue(reach.Reached(Flat(30f, -20f)));
        Assert.AreEqual(25f, reach.DistanceFromStart(Flat(25f, 0f)), 3f);
        Assert.AreEqual(Mathf.Sqrt(2f) * 20f, reach.DistanceFromStart(Flat(20f, 20f)), 5f);
    }

    [Test]
    public void A_wall_right_across_the_arena_cuts_the_far_side_off()
    {
        Box(0f, 10f, 100f, 3f);                           // wider than the arena
        var reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));

        Assert.IsTrue(reach.Reached(Flat(0f, 0f)));
        Assert.IsTrue(reach.Reached(Flat(-30f, 5f)));
        Assert.IsFalse(reach.Reached(Flat(0f, 20f)), "the far side of the wall is not reached");
        Assert.IsTrue(float.IsInfinity(reach.DistanceFromStart(Flat(0f, 20f))));
    }

    [Test]
    public void A_gap_wide_enough_for_the_boat_lets_it_through_and_a_narrower_one_does_not()
    {
        // A wall of two halves, with a gap between them: 4 m is room for a 2.6 m boat, 2.5 m is not.
        foreach (var gap in new[] { 4.5f, 2.5f })
        {
            foreach (var go in made) Object.DestroyImmediate(go);
            made.Clear();
            Box(-gap * 0.5f - 25f, 10f, 50f, 2f);
            Box(gap * 0.5f + 25f, 10f, 50f, 2f);
            var reach = Arena();
            Physics.SyncTransforms();
            reach.Flood(At(0f, 0f));

            bool through = reach.Reached(Flat(0f, 20f));
            if (gap > 3f) Assert.IsTrue(through, "a " + gap + " m gap should let the boat through");
            else Assert.IsFalse(through, "a " + gap + " m gap is too narrow for the boat");
        }
    }

    [Test]
    public void A_jam_fixed_in_place_blocks_the_boat_but_something_that_floats_does_not()
    {
        var jam = Box(0f, 10f, 100f, 3f, 2f, body: true, kinematic: true);
        var reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));
        Assert.IsFalse(reach.Reached(Flat(0f, 20f)), "a jam on a kinematic body is solid to the boat");

        Object.DestroyImmediate(jam);
        made.Remove(jam);
        Box(0f, 10f, 100f, 3f, 2f, body: true, kinematic: false);        // a plank, a drum: it moves when the boat pushes it
        reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));
        Assert.IsTrue(reach.Reached(Flat(0f, 20f)), "wreckage that floats is pushed aside, not a wall");
    }

    [Test]
    public void The_road_under_the_flood_is_not_a_wall()
    {
        // Ground a metre down, and a low kerb: the boat floats over them.
        var ground = new GameObject("ground");
        ground.transform.position = Origin + new Vector3(0f, -1.05f, 0f);
        ground.AddComponent<BoxCollider>().size = new Vector3(100f, 0.1f, 100f);
        made.Add(ground);

        var reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));
        Assert.IsTrue(reach.Reached(Flat(30f, 30f)));
    }

    [Test]
    public void A_route_goes_round_the_end_of_a_wall_and_is_longer_than_the_straight_line()
    {
        Box(0f, 5f, 30f, 2f);                             // x from -15 to 15, at z = 5
        var reach = Arena(60f);
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));

        Vector2 target = Flat(0f, 12f);
        Assert.IsTrue(reach.Reached(target));
        var route = reach.RouteTo(target);
        Assert.GreaterOrEqual(route.Count, 3, "a route round a wall has a corner in it");

        float length = 0f;
        for (int i = 1; i < route.Count; i++) length += Vector2.Distance(route[i - 1], route[i]);
        Assert.Greater(length, 25f, "round the end of the wall, not through it");
        Assert.Less(length, 60f);
        Assert.AreEqual(Flat(0f, 0f).x, route[0].x, 1.5f);

        // Nothing on the route's straight stretches is inside the wall.
        for (int i = 1; i < route.Count; i++)
            Assert.IsTrue(reach.StraightRunIsClear(route[i - 1], route[i]), "the route cuts a corner through the wall");
    }

    [Test]
    public void Places_the_avoid_hook_closes_are_not_reached()
    {
        var reach = Arena();
        reach.Avoid = xz => xz.x > Origin.x + 10f;                                // live water, say
        Physics.SyncTransforms();
        reach.Flood(At(0f, 0f));

        Assert.IsTrue(reach.Reached(Flat(5f, 0f)));
        Assert.IsFalse(reach.Reached(Flat(20f, 0f)));
    }

    [Test]
    public void A_flood_from_inside_a_walled_yard_stays_in_it()
    {
        Box(0f, 8f, 20f, 2f);
        Box(0f, -8f, 20f, 2f);
        Box(10f, 0f, 2f, 18f);
        Box(-10f, 0f, 2f, 18f);
        var reach = Arena();
        Physics.SyncTransforms();
        int cells = reach.Flood(At(0f, 0f));

        Assert.Less(cells, 200, "a yard of 18 m by 14 m, less the boat's margin, is a small place");
        Assert.IsFalse(reach.Reached(Flat(30f, 0f)));
    }

    [Test]
    public void A_start_with_no_clear_water_near_it_reaches_nothing()
    {
        Box(0f, 0f, 30f, 30f);
        var reach = Arena();
        Physics.SyncTransforms();
        Assert.AreEqual(0, reach.Flood(At(0f, 0f)), "the middle of a solid block is not water");
        Assert.AreEqual(0, reach.ReachedCount);
    }

    [Test]
    public void A_line_can_be_thrown_to_somebody_in_the_open_but_not_to_somebody_walled_in()
    {
        var reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(-20f, 0f));

        var open = Origin + new Vector3(0f, 1.6f, 0f);
        Assert.IsTrue(reach.ThrowPointFor(open, 2f, 11f, out Vector2 from), "somebody in open water can be thrown to");
        Assert.LessOrEqual(Vector3.Distance(new Vector3(from.x, 2f, from.y), open), 11.1f);

        // The same person inside a tall walled room: nobody outside can see them, and the boat cannot get in.
        Box(0f, 4f, 8f, 1f, 12f);
        Box(0f, -4f, 8f, 1f, 12f);
        Box(4f, 0f, 1f, 8f, 12f);
        Box(-4f, 0f, 1f, 8f, 12f);
        reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(-20f, 0f));
        Assert.IsFalse(reach.ThrowPointFor(open, 2f, 11f, out _), "nobody can throw to somebody behind four tall walls");
    }

    [Test]
    public void The_nearest_reached_cell_to_a_point_inside_a_wall_is_found_at_its_edge()
    {
        Box(0f, 0f, 10f, 10f);
        var reach = Arena();
        Physics.SyncTransforms();
        reach.Flood(At(-20f, 0f));

        float d = reach.Nearest(Flat(0f, 0f), 20f, out Vector2 where);
        Assert.Greater(d, 5f, "the middle of the block is at least its half-width from open water");
        Assert.Less(d, 10f);
        Assert.IsTrue(reach.Reached(where));
        Assert.IsTrue(float.IsInfinity(reach.DistanceToReached(Flat(0f, 0f), 2f)), "nothing is reached within 2 m of the middle of the block");
    }
}
