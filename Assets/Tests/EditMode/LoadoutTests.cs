using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>The pack the player chooses, and the rule that decides who it lets them rescue.</summary>
public class LoadoutTests
{
    [TearDown]
    public void EmptyThePack()
    {
        // LoadoutState is static. Entering Play mode resets it properly; this just keeps one test from leaking
        // into the next.
        LoadoutState.Set(new RescueItemId[0]);
    }

    [Test]
    public void Every_item_id_has_a_catalogue_entry()
    {
        foreach (RescueItemId id in System.Enum.GetValues(typeof(RescueItemId)))
        {
            var def = RescueItems.Get(id);
            Assert.IsNotNull(def, id + " is missing from RescueItems.All");
            Assert.AreEqual(id, def.id);
            Assert.IsFalse(string.IsNullOrEmpty(def.name), id + " has no display name");
        }

        Assert.AreEqual(RescueItems.All.Length, RescueItems.All.Select(d => d.id).Distinct().Count(),
            "an item id appears twice in RescueItems.All");
    }

    [Test]
    public void Suggested_pack_fills_the_slots_without_repeats()
    {
        Assert.AreEqual(RescueItems.Slots, RescueItems.Suggested.Length);
        Assert.AreEqual(RescueItems.Suggested.Length, RescueItems.Suggested.Distinct().Count());
    }

    [Test]
    public void Has_reflects_only_what_was_packed()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope, RescueItemId.Radio });

        Assert.IsTrue(LoadoutState.HasLoadout);
        Assert.IsTrue(LoadoutState.Has(RescueItemId.Rope));
        Assert.IsTrue(LoadoutState.Has(RescueItemId.Radio));
        Assert.IsFalse(LoadoutState.Has(RescueItemId.Lifebuoy));
        Assert.IsFalse(LoadoutState.Has(RescueItemId.FirstAid));
    }

    [Test]
    public void Use_counts_restart_for_a_retry_but_the_pack_stays()
    {
        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        LoadoutState.RegisterUse(RescueItemId.Lifebuoy);
        LoadoutState.RegisterUse(RescueItemId.Lifebuoy);
        Assert.AreEqual(2, LoadoutState.Uses(RescueItemId.Lifebuoy));

        LoadoutState.ResetUses();

        Assert.AreEqual(0, LoadoutState.Uses(RescueItemId.Lifebuoy));
        Assert.IsTrue(LoadoutState.Has(RescueItemId.Lifebuoy), "resetting the counts must not empty the pack");
    }

    [Test]
    public void Packing_again_clears_the_previous_counts()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        LoadoutState.RegisterUse(RescueItemId.Rope);

        LoadoutState.Set(new[] { RescueItemId.Rope });

        Assert.AreEqual(0, LoadoutState.Uses(RescueItemId.Rope));
    }

    [Test]
    public void Survivor_is_refused_without_the_item_they_need_and_accepted_with_it()
    {
        var go = new GameObject("Survivor under test", typeof(SphereCollider));
        try
        {
            var survivor = go.AddComponent<Survivor>();
            survivor.needs = Need.Lifebuoy | Need.FirstAid;

            LoadoutState.Set(new[] { RescueItemId.FirstAid });
            Assert.IsFalse(survivor.CanBeRescued(out string reason));
            StringAssert.Contains("Salbabida", reason);

            LoadoutState.Set(new[] { RescueItemId.FirstAid, RescueItemId.Lifebuoy });
            Assert.IsTrue(survivor.CanBeRescued(out reason));
            Assert.IsNull(reason);
            Assert.IsTrue(survivor.RequiresStabilise, "an injured resident still needs the first-aid hold");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Each_need_maps_to_the_item_that_answers_it()
    {
        Assert.AreEqual(Need.Lifebuoy, RescueItems.NeedFor(RescueItemId.Lifebuoy));
        Assert.AreEqual(Need.Rope, RescueItems.NeedFor(RescueItemId.Rope));
        Assert.AreEqual(Need.FirstAid, RescueItems.NeedFor(RescueItemId.FirstAid));
        Assert.AreEqual(Need.None, RescueItems.NeedFor(RescueItemId.Radio));
    }
}
