using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Residents as the mission describes them: built from a spec, worn down by waiting, tagged by how they are, and
/// only rescuable when the rules allow it. They are spawned straight from the spawner, so these do not need the
/// Gameplay scene.
/// </summary>
public class ResidentTests
{
    readonly List<GameObject> made = new List<GameObject>();

    [SetUp]
    public void Clean()
    {
        Time.timeScale = 1f;
        LoadoutState.Clear();
    }

    [TearDown]
    public void CleanUp()
    {
        foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        made.Clear();
        LoadoutState.Clear();
    }

    Survivor Spawn(string id, Situation situation, CharacterCategory category = CharacterCategory.Adult,
                   Need needs = Need.None, string household = "Cruz", float condition = 1f)
    {
        var spec = new ResidentSpec
        {
            id = id,
            displayName = id + " " + household,
            household = household,
            category = category,
            situation = situation,
            needs = needs,
            startCondition = condition
        };
        var survivor = ResidentSpawner.Spawn(spec, new Vector3(made.Count * 6f, 60f, 0f), Quaternion.identity);
        made.Add(survivor.gameObject);
        return survivor;
    }

    // ------------------------------------------------------------------ built from a spec

    [UnityTest]
    public IEnumerator Residents_are_built_to_suit_where_they_are()
    {
        var water = Spawn("ben", Situation.OpenWater, needs: Need.Lifebuoy);
        var cling = Spawn("rico", Situation.Clinging, CharacterCategory.Teen, Need.Lifebuoy);
        var roof = Spawn("cesar", Situation.Rooftop, CharacterCategory.Elder, Need.Rope);
        var trapped = Spawn("tess", Situation.Trapped, needs: Need.Rope);
        yield return null;

        Assert.AreEqual("ben Cruz", water.DisplayName);
        Assert.AreEqual("Cruz", water.household);
        Assert.AreEqual(Need.Lifebuoy, water.needs);

        var w = water.GetComponent<SurvivorVisual>();
        Assert.IsTrue(w.followWaves, "somebody in open water rides the waves");
        Assert.AreEqual(DriftMode.Always, w.drift, "and is carried by the current");

        var c = cling.GetComponent<SurvivorVisual>();
        Assert.IsTrue(c.clingToPost);
        Assert.AreEqual(DriftMode.Never, c.drift, "somebody holding a pole stays put");

        var r = roof.GetComponent<SurvivorVisual>();
        Assert.IsFalse(r.followWaves, "a roof does not bob");
        Assert.AreEqual(RestingPoseMode.Standing, r.poseOverride);

        Assert.AreEqual(DriftMode.Never, trapped.GetComponent<SurvivorVisual>().drift, "debris holds them where they are");

        Assert.AreEqual(CharacterCategory.Elder, roof.GetComponent<SurvivorVisual>().Character.category, "the category asked for");
        Assert.AreEqual(CharacterCategory.Teen, c.Character.category);
    }

    [UnityTest]
    public IEnumerator A_particular_character_can_be_asked_for_by_id()
    {
        var spec = new ResidentSpec { id = "x", displayName = "Specific", situation = Situation.Rooftop, characterId = "C26" };
        var s = ResidentSpawner.Spawn(spec, new Vector3(0f, 60f, 0f), Quaternion.identity);
        made.Add(s.gameObject);
        yield return null;

        Assert.AreEqual("C26", s.GetComponent<SurvivorVisual>().Character.id);
    }

    // ------------------------------------------------------------------ condition and tag

    [UnityTest]
    public IEnumerator Condition_drains_while_somebody_waits_at_the_rate_the_rules_give()
    {
        var kid = Spawn("joy", Situation.OpenWater, CharacterCategory.Kid, Need.Lifebuoy);
        yield return null;
        var condition = kid.Condition;
        condition.SetCondition(0.8f);

        yield return new WaitForSeconds(1f);

        float lost = 0.8f - condition.Condition01;
        float expected = condition.DrainPerMinute / 60f;                  // per second, for the one second we waited
        Assert.Greater(lost, 0f, "waiting in the water costs condition");
        Assert.AreEqual(expected, lost, expected * 0.35f, "and at about the rate the rules give");
        Assert.Greater(condition.DrainPerMinute, MissionDefinition.Current.rules.drainOpenWater,
            "a child in open water loses more than the base rate");
    }

    [UnityTest]
    public IEnumerator The_tag_follows_condition_and_critical_is_red()
    {
        var adult = Spawn("ben", Situation.Clinging);
        yield return null;
        Assert.AreEqual(Triage.Green, adult.Triage);

        adult.Condition.SetCondition(0.55f);
        Assert.AreEqual(Triage.Yellow, adult.Triage);

        adult.Condition.SetCondition(0.3f);
        Assert.AreEqual(Triage.Red, adult.Triage);

        var roofAdult = Spawn("marie", Situation.Rooftop);
        yield return null;
        roofAdult.Condition.SetCondition(0f);
        Assert.IsTrue(roofAdult.IsCritical);
        Assert.AreEqual(Triage.Red, roofAdult.Triage, "critical is red whoever it is");
    }

    [UnityTest]
    public IEnumerator A_child_is_a_priority_from_the_start_and_somebody_hurt_starts_lower()
    {
        var kid = Spawn("joy", Situation.OpenWater, CharacterCategory.Kid, Need.Lifebuoy);
        var hurtKid = Spawn("migo", Situation.Clinging, CharacterCategory.Kid, Need.Lifebuoy | Need.FirstAid, condition: 0.8f);
        var roofAdult = Spawn("marie", Situation.Rooftop, CharacterCategory.Adult, Need.Rope);
        yield return null;

        Assert.AreEqual(Triage.Yellow, kid.Triage);
        Assert.AreEqual(Triage.Red, hurtKid.Triage, "a hurt child who has already lost some is red");
        Assert.AreEqual(Triage.Green, roofAdult.Triage);
    }

    // ------------------------------------------------------------------ the rules about being rescued

    [UnityTest]
    public IEnumerator Somebody_left_too_long_cannot_board_without_the_first_aid_kit()
    {
        var ben = Spawn("ben", Situation.OpenWater, needs: Need.Lifebuoy);
        yield return null;
        ben.Condition.SetCondition(0f);

        Assert.IsTrue(ben.IsCritical);
        Assert.IsTrue(ben.RequiresStabilise, "critical means they need treating, even if they were not hurt");

        LoadoutState.Set(new[] { RescueItemId.Lifebuoy });
        Assert.IsFalse(ben.CanBeRescued(out string reason));
        StringAssert.Contains("Botika", reason);
        Assert.IsFalse(ben.HasRequiredItems);

        LoadoutState.Set(new[] { RescueItemId.Lifebuoy, RescueItemId.FirstAid });
        Assert.IsTrue(ben.CanBeRescued(out reason), reason);
        Assert.IsTrue(ben.HasRequiredItems);
    }

    [UnityTest]
    public IEnumerator A_roof_and_the_far_side_of_the_debris_need_a_line_before_the_boat_can_take_anyone()
    {
        LoadoutState.Set(new[] { RescueItemId.Rope });
        var roof = Spawn("cesar", Situation.Rooftop, CharacterCategory.Elder, Need.Rope);
        var trapped = Spawn("tess", Situation.Trapped, needs: Need.Rope);
        var water = Spawn("ben", Situation.OpenWater, needs: Need.None);
        yield return null;

        Assert.IsFalse(roof.CanBeRescued(out string reason));
        StringAssert.Contains("line", reason);
        Assert.IsFalse(trapped.CanBeRescued(out reason));
        StringAssert.Contains("line", reason);
        Assert.IsTrue(water.CanBeRescued(out reason), "somebody in the water is within reach, no line needed: " + reason);

        roof.LineDelivered = true;
        trapped.LineDelivered = true;
        Assert.IsTrue(roof.CanBeRescued(out reason), reason);
        Assert.IsTrue(trapped.CanBeRescued(out reason), reason);
    }

    [UnityTest]
    public IEnumerator A_live_wire_stops_a_rescue_whatever_is_packed()
    {
        var ben = Spawn("ben", Situation.OpenWater, needs: Need.None);
        yield return null;
        ben.InLiveWire = true;

        Assert.IsFalse(ben.CanBeRescued(out string reason));
        StringAssert.Contains("live wire", reason);

        ben.InLiveWire = false;
        Assert.IsTrue(ben.CanBeRescued(out reason), reason);
    }

    [UnityTest]
    public IEnumerator Nobody_is_missed_by_the_first_aid_use_count_when_they_were_critical_not_hurt()
    {
        var ben = Spawn("ben", Situation.OpenWater, needs: Need.Lifebuoy);
        yield return null;
        ben.Condition.SetCondition(0f);
        LoadoutState.Set(new[] { RescueItemId.Lifebuoy, RescueItemId.FirstAid });

        ben.Rescue(null);

        Assert.AreEqual(1, LoadoutState.Uses(RescueItemId.FirstAid), "the Botika earned its slot treating somebody who was critical");
        Assert.AreEqual(1, LoadoutState.Uses(RescueItemId.Lifebuoy));
    }

    // ------------------------------------------------------------------ the roster

    [UnityTest]
    public IEnumerator The_roster_knows_the_households_and_when_one_is_reunited()
    {
        var a = Spawn("nena", Situation.Rooftop, CharacterCategory.Elder, household: "Dela Cruz");
        var b = Spawn("ben", Situation.OpenWater, household: "Dela Cruz");
        var c = Spawn("lorna", Situation.OpenWater, CharacterCategory.Elder, household: "Villanueva");
        yield return null;

        var roster = new Roster(new[] { a, b, c });
        Assert.AreEqual(3, roster.Count);
        CollectionAssert.AreEqual(new[] { "Dela Cruz" }, roster.SharedHouseholds(), "only a household of more than one counts");
        Assert.AreEqual(2, roster.Household("Dela Cruz").Count);

        Assert.IsFalse(roster.HouseholdReunited("Dela Cruz"));
        roster.Find(a).status = ResidentStatus.Registered;
        Assert.IsFalse(roster.HouseholdReunited("Dela Cruz"), "one of two is not reunited");
        roster.Find(b).status = ResidentStatus.Registered;
        Assert.IsTrue(roster.HouseholdReunited("Dela Cruz"));
        Assert.AreEqual(2, roster.CountWith(ResidentStatus.Registered));
    }

    [UnityTest]
    public IEnumerator Taking_somebody_less_urgent_than_a_known_resident_who_could_have_been_taken_is_not_following_priority()
    {
        var urgent = Spawn("joy", Situation.OpenWater, CharacterCategory.Kid, Need.Lifebuoy, condition: 0.2f);
        var calm = Spawn("marie", Situation.Rooftop, CharacterCategory.Adult, Need.Rope);
        yield return null;
        var roster = new Roster(new[] { urgent, calm });
        var calmRecord = roster.Find(calm);

        Assert.AreEqual(Triage.Red, urgent.Triage);
        Assert.AreEqual(Triage.Green, calm.Triage);

        Assert.IsTrue(roster.WasMostUrgent(calmRecord, calm.Triage), "the crew had not found the child, so they cannot be blamed");

        urgent.Discovered = true;
        Assert.IsFalse(roster.WasMostUrgent(calmRecord, calm.Triage), "the crew knew about a red resident and took a green one");

        LoadoutState.Set(new[] { RescueItemId.Rope });      // no Salbabida: the child could not have been taken anyway
        Assert.IsTrue(roster.WasMostUrgent(calmRecord, calm.Triage), "they could not have taken the child without the Salbabida");
    }
}
