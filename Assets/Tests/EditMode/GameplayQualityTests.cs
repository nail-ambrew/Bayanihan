using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The GRAPHICS presets for the missions (<see cref="GameplayQuality"/>): High leaves the picture as built, Low and
/// Medium only ever draw less, and thinning the grass and plants keeps the same ones each time.
/// </summary>
public class GameplayQualityTests
{
    [TearDown]
    public void TearDown() { InstancedFoliage.DensityScale = 1f; InstancedFoliage.QualityScale = 1f; }

    [Test]
    public void High_changes_nothing_the_map_was_built_with()
    {
        var h = GameplayQuality.High;
        Assert.AreEqual(0f, h.farClip, "the camera's own far clip");
        Assert.AreEqual(0f, h.fogFloor, "the weather's own fog");
        Assert.AreEqual(0f, h.lodBias, "the quality level's own LOD bias");
        Assert.IsTrue(h.shadows && h.msaa);
        Assert.AreEqual(1f, h.foliageDistance);
        Assert.AreEqual(1f, h.foliageDensity);
    }

    [Test]
    public void Each_step_down_draws_less()
    {
        var low = GameplayQuality.Low;
        var mid = GameplayQuality.Medium;
        Assert.Less(low.farClip, mid.farClip);
        Assert.Less(low.lodBias, mid.lodBias);
        Assert.Less(low.foliageDistance, mid.foliageDistance);
        Assert.Less(low.foliageDensity, mid.foliageDensity);
        Assert.Less(mid.foliageDensity, 1f);
        Assert.IsFalse(low.shadows, "no shadows on Low");
        Assert.AreEqual(GameplayQuality.Low.farClip, GameplayQuality.For(0).farClip);
        Assert.AreEqual(GameplayQuality.High.farClip, GameplayQuality.For(2).farClip);
    }

    [Test]
    public void The_fog_hides_the_far_clip_on_Low_and_Medium()
    {
        foreach (var q in new[] { GameplayQuality.Low, GameplayQuality.Medium })
        {
            // Exponential squared fog: what is left of the scene's colour at the far clip.
            float left = Mathf.Exp(-Mathf.Pow(q.fogFloor * q.farClip, 2f));
            Assert.Less(left, 0.06f, "at least 94 % fog where the view is cut off (" + q.farClip + " m)");
        }
    }

    [Test]
    public void Thinning_draws_a_share_and_can_drop_a_lone_plant()
    {
        InstancedFoliage.DensityScale = 1f;
        Assert.AreEqual(7, InstancedFoliage.DrawnOf(7));
        InstancedFoliage.DensityScale = 0.35f;
        Assert.AreEqual(35, InstancedFoliage.DrawnOf(100));
        Assert.AreEqual(0, InstancedFoliage.DrawnOf(1), "one plant of a kind in a tile is not worth a draw call on Low");
        Assert.LessOrEqual(InstancedFoliage.DrawnOf(3), 3);
    }

    [Test]
    public void A_tile_is_shuffled_the_same_way_every_time()
    {
        List<Matrix4x4> Make() => Enumerable.Range(0, 50).Select(i => Matrix4x4.Translate(new Vector3(i, 0f, 0f))).ToList();
        var a = Make(); var b = Make();
        InstancedFoliage.Shuffle(a, 1234);
        InstancedFoliage.Shuffle(b, 1234);
        CollectionAssert.AreEqual(a, b);
        CollectionAssert.AreEquivalent(Make(), a, "nothing lost or doubled");
        CollectionAssert.AreNotEqual(Make(), a, "and the order really changes");
    }
}
