using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>The wake and the bow spray: where they start, in the real Gameplay scene.</summary>
public class BoatEffectsTests
{
    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadGameplay();
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_wake_and_the_bow_spray_start_at_the_water_not_under_it()
    {
        var boat = Boat();
        var effects = boat.GetComponent<BoatEffects>();
        Assert.IsNotNull(effects, "the boat has no BoatEffects");
        Assert.IsNotNull(WaterSurface.Instance);

        foreach (var name in new[] { "Wake", "BowSpray" })
        {
            var emitter = boat.transform.Find(name);
            Assert.IsNotNull(emitter, "the boat has no '" + name + "'");

            // The hull's origin sits below the surface and the boat pitches with the waves: an emitter fixed to it
            // used to end up under the water, where the spray could not be seen.
            float above = emitter.position.y - WaterSurface.Instance.GetHeight(emitter.position);
            Assert.That(above, Is.InRange(0.02f, 0.3f), name + " starts " + above.ToString("0.00") + " m above the water");
        }
        yield break;
    }
}
