using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Where the water meets a person's body in each pose. The waterline is measured off every character's own
/// skeleton, so a child and a tall adult should end up wet to the same part of the body - the chest when treading
/// water, the hips when swimming - rather than to the same height above their feet.
///
/// People are spawned straight from the character library and sunk by their Waterline the way SurvivorVisual and
/// PlayerRescuer do it, so these do not need the Gameplay scene.
/// </summary>
public class CharacterWaterlineTests
{
    const string KidId = "C01";         // the smallest child in the library
    const string AdultId = "C13";       // an adult

    // How far under the surface the chest, and the hips, are meant to be (CharacterAnimator.MeasureWaterline).
    const float ChestDepth = 0.08f;
    const float HipsDepth = 0.05f;
    const int Samples = 12;             // moments through a clip at which the body is checked

    GameObject water;                   // its height is the water level; everybody spawned is a child of it

    [SetUp]
    public void MakeWater()
    {
        Time.timeScale = 1f;
        water = new GameObject("Test water");
        water.transform.position = new Vector3(0f, 50f, 0f);        // nowhere near the street
    }

    [TearDown]
    public void RemoveWater()
    {
        Object.DestroyImmediate(water);
    }

    float Level => water.transform.position.y;

    CharacterAnimator Spawn(CharacterEntry entry, CharacterPose pose)
    {
        var person = CharacterRig.Spawn(entry, water.transform, pose);
        Assert.IsNotNull(person, "could not spawn " + entry.id);
        person.transform.localPosition = new Vector3(0f, -person.Waterline, 0f);
        return person;
    }

    CharacterAnimator Spawn(string id, CharacterPose pose)
    {
        var library = CharacterLibrary.Instance;
        Assert.IsNotNull(library, "no character library in Resources");
        var entry = library.Find(id);
        Assert.IsNotNull(entry, id + " is not in the character library");
        return Spawn(entry, pose);
    }

    /// <summary>Height above the water of the head, chest and hips at each of several moments through the pose's clip.</summary>
    (float head, float chest, float hips)[] Sweep(CharacterAnimator person, CharacterPose pose)
    {
        float length = person.Entry.Clip(pose).length;
        var heights = new (float head, float chest, float hips)[Samples];
        for (int i = 0; i < Samples; i++)
        {
            person.Freeze(pose, length * i / Samples);
            heights[i] = (person.Bone("Head").position.y - Level,
                          person.Bone("Chest").position.y - Level,
                          person.Bone("Hips").position.y - Level);
        }
        return heights;
    }

    // ------------------------------------------------------------------ standing

    [Test]
    public void Nobody_standing_or_walking_is_sunk(
        [Values(KidId, AdultId)] string id,
        [Values(CharacterPose.Idle, CharacterPose.Walk)] CharacterPose pose)
    {
        var person = Spawn(id, pose);
        Assert.AreEqual(0f, person.Waterline, 0.0001f, id + " is " + pose + " on dry ground, so nothing of them should be under water");
    }

    // ------------------------------------------------------------------ treading water

    [Test]
    public void Treading_water_wets_the_chest_and_keeps_the_head_clear([Values(KidId, AdultId)] string id)
    {
        var person = Spawn(id, CharacterPose.Float);
        var body = Sweep(person, CharacterPose.Float);

        Assert.AreEqual(-ChestDepth, body[0].chest, 0.01f, id + ": the water should come up to the chest");
        for (int i = 0; i < body.Length; i++)
        {
            Assert.Less(body[i].chest, 0f, id + ": the chest came out of the water " + i + "/" + Samples + " of the way through the float");
            Assert.Greater(body[i].head, 0.02f, id + ": the head went under " + i + "/" + Samples + " of the way through the float");
        }
    }

    // ------------------------------------------------------------------ swimming

    [Test]
    public void A_swimmer_lies_along_the_surface([Values(KidId, AdultId)] string id)
    {
        var person = Spawn(id, CharacterPose.Swim);
        var body = Sweep(person, CharacterPose.Swim);

        Assert.AreEqual(-HipsDepth, body[0].hips, 0.01f, id + ": a swimmer is wet to the hips");
        for (int i = 0; i < body.Length; i++)
        {
            foreach (float y in new[] { body[i].head, body[i].chest, body[i].hips })
                Assert.That(y, Is.InRange(-0.1f, 0.05f), id + ": part of the swimmer is well off the surface " + i + "/" + Samples + " of the way through the stroke");
        }
    }

    // ------------------------------------------------------------------ child against adult

    [Test]
    public void A_child_rides_lower_than_an_adult_but_is_wet_to_the_same_place()
    {
        foreach (var pose in new[] { CharacterPose.Float, CharacterPose.Swim })
        {
            var kid = Spawn(KidId, pose);
            var adult = Spawn(AdultId, pose);

            Assert.Less(kid.Waterline, adult.Waterline - 0.3f,
                pose + ": a child's body is shorter, so the water belongs lower on the object than for an adult");

            kid.Freeze(pose, 0f);
            adult.Freeze(pose, 0f);
            string part = pose == CharacterPose.Float ? "Chest" : "Hips";
            Assert.AreEqual(adult.Bone(part).position.y - Level, kid.Bone(part).position.y - Level, 0.005f,
                pose + ": the child and the adult should be wet to the same part of the body (" + part + ")");
        }
    }

    [Test]
    public void Everybody_in_the_library_floats_and_swims_at_the_right_depth()
    {
        var library = CharacterLibrary.Instance;
        Assert.IsNotNull(library, "no character library in Resources");

        int checkedPeople = 0;
        foreach (var entry in library.characters)
        {
            if (!entry.IsComplete) continue;
            checkedPeople++;

            var floating = Sweep(Spawn(entry, CharacterPose.Float), CharacterPose.Float);
            Assert.AreEqual(-ChestDepth, floating[0].chest, 0.01f, entry.id + " treading water");
            foreach (var f in floating)
            {
                Assert.Less(f.chest, 0f, entry.id + ": chest out of the water while floating");
                Assert.Greater(f.head, 0.02f, entry.id + ": head under the water while floating");
            }

            var swimming = Sweep(Spawn(entry, CharacterPose.Swim), CharacterPose.Swim);
            Assert.AreEqual(-HipsDepth, swimming[0].hips, 0.01f, entry.id + " swimming");
            foreach (var s in swimming)
                foreach (float y in new[] { s.head, s.chest, s.hips })
                    Assert.That(y, Is.InRange(-0.1f, 0.05f), entry.id + ": off the surface while swimming");
        }
        Assert.Greater(checkedPeople, 0, "the character library is empty");
        Assert.AreEqual(library.characters.Count, checkedPeople, "somebody in the library has no model or clips, so was not checked");
    }

    // ------------------------------------------------------------------ getting out

    [UnityTest]
    public IEnumerator Standing_up_eases_the_sink_back_to_nothing()
    {
        var person = Spawn(AdultId, CharacterPose.Float);
        Assert.Greater(person.Waterline, 1f, "an adult treading water is sunk by about a metre");

        person.Play(CharacterPose.Idle);
        Assert.Greater(person.Waterline, 1f, "the sink should ease out, not snap");

        yield return new WaitForSeconds(1.5f);      // it moves at 1.5 m/s: a metre takes under a second
        Assert.AreEqual(0f, person.Waterline, 0.001f);
    }
}
