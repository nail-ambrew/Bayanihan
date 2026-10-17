using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The numbers behind a rescue: how fast residents wear down, when that shows as a yellow or red tag, and what a
/// rescue is worth. These pin down the shape of the rules - open water is worse than a roof, a child is worse off
/// than an adult - and the promises the design makes, such as "a child left in open water goes critical within a few
/// minutes".
/// </summary>
public class RescueRulesTests
{
    RescueRules rules;

    [SetUp]
    public void Defaults() { rules = new RescueRules(); }

    float Drain(Situation s, CharacterCategory c = CharacterCategory.Adult, bool injured = false, bool live = false,
                bool held = false, float storm = 0f, float rise = 0f)
        => rules.DrainPerMinute(s, c, injured, live, held, storm, rise);

    // ------------------------------------------------------------------ condition

    [Test]
    public void Open_water_wears_people_down_fastest_and_a_roof_slowest()
    {
        Assert.Greater(Drain(Situation.OpenWater), Drain(Situation.Clinging));
        Assert.Greater(Drain(Situation.Clinging), Drain(Situation.Trapped));
        Assert.Greater(Drain(Situation.Trapped), Drain(Situation.Rooftop));
        Assert.Greater(Drain(Situation.Rooftop), 0f, "even on a roof, waiting costs something");
    }

    [Test]
    public void Children_and_the_old_and_the_hurt_wear_down_faster_than_a_healthy_adult()
    {
        float adult = Drain(Situation.OpenWater);
        Assert.Greater(Drain(Situation.OpenWater, CharacterCategory.Kid), adult);
        Assert.Greater(Drain(Situation.OpenWater, CharacterCategory.Elder), adult);
        Assert.Greater(Drain(Situation.OpenWater, injured: true), adult);
        Assert.AreEqual(adult, Drain(Situation.OpenWater, CharacterCategory.Teen), 1e-5f, "a teenager is as hardy as an adult");
    }

    [Test]
    public void A_live_wire_and_a_storm_make_it_worse_and_a_hold_on_a_ring_makes_it_better()
    {
        float calm = Drain(Situation.OpenWater);
        Assert.Greater(Drain(Situation.OpenWater, live: true), calm);
        Assert.Greater(Drain(Situation.OpenWater, storm: 1f), calm);
        Assert.Less(Drain(Situation.OpenWater, held: true), calm);
    }

    [Test]
    public void Rising_water_hurts_everybody_who_is_still_low_but_not_the_people_on_roofs()
    {
        Assert.Greater(Drain(Situation.OpenWater, rise: 0.5f), Drain(Situation.OpenWater));
        Assert.Greater(Drain(Situation.Clinging, rise: 0.5f), Drain(Situation.Clinging));
        Assert.Greater(Drain(Situation.Trapped, rise: 0.5f), Drain(Situation.Trapped));
        Assert.AreEqual(Drain(Situation.Rooftop), Drain(Situation.Rooftop, rise: 0.5f), 1e-6f);
    }

    [Test]
    public void A_child_left_in_open_water_goes_critical_within_a_few_minutes_but_an_adult_on_a_roof_does_not()
    {
        float kidMinutes = 1f / Drain(Situation.OpenWater, CharacterCategory.Kid);
        float roofMinutes = 1f / Drain(Situation.Rooftop);
        Assert.Less(kidMinutes, 3.5f, "leaving a child in the water has to matter within the mission");
        Assert.Greater(kidMinutes, 1.0f, "but not so soon that it is hopeless");
        Assert.Greater(roofMinutes, 7f, "somebody on a roof can wait, so the order of the rescues is a real choice");
    }

    // ------------------------------------------------------------------ the triage tag

    [Test]
    public void A_healthy_adult_starts_green_and_a_child_in_the_water_does_not()
    {
        float adult = rules.Urgency(1f, Situation.Rooftop, CharacterCategory.Adult, false);
        Assert.AreEqual(Triage.Green, rules.TriageOf(adult, false));

        float kid = rules.Urgency(1f, Situation.OpenWater, CharacterCategory.Kid, false);
        Assert.AreEqual(Triage.Yellow, rules.TriageOf(kid, false), "a child in open water is a priority from the start");
    }

    [Test]
    public void The_tag_goes_from_green_to_yellow_to_red_as_condition_falls()
    {
        var last = Triage.Green;
        for (float c = 1f; c >= 0f; c -= 0.05f)
        {
            var tag = rules.TriageOf(rules.Urgency(c, Situation.Clinging, CharacterCategory.Adult, false), false);
            Assert.GreaterOrEqual((int)tag, (int)last, "the tag got better as condition fell, at " + c);
            last = tag;
        }
        Assert.AreEqual(Triage.Red, last);
    }

    [Test]
    public void Critical_is_always_red()
    {
        float urgency = rules.Urgency(1f, Situation.Rooftop, CharacterCategory.Adult, false);
        Assert.AreEqual(Triage.Green, rules.TriageOf(urgency, false));
        Assert.AreEqual(Triage.Red, rules.TriageOf(urgency, true));
    }

    [Test]
    public void Urgency_ignores_a_condition_outside_zero_to_one()
    {
        Assert.AreEqual(rules.Urgency(0f, Situation.Rooftop, CharacterCategory.Adult, false),
                        rules.Urgency(-3f, Situation.Rooftop, CharacterCategory.Adult, false), 1e-6f);
        Assert.AreEqual(rules.Urgency(1f, Situation.Rooftop, CharacterCategory.Adult, false),
                        rules.Urgency(7f, Situation.Rooftop, CharacterCategory.Adult, false), 1e-6f);
    }

    // ------------------------------------------------------------------ the score

    [Test]
    public void Taking_the_urgent_cases_first_pays_and_a_late_rescue_is_worth_half()
    {
        int green = rules.Points(Triage.Green, false);
        int yellow = rules.Points(Triage.Yellow, false);
        int red = rules.Points(Triage.Red, false);
        int late = rules.Points(Triage.Red, true);

        Assert.AreEqual(rules.pointsPerResident, green);
        Assert.Greater(yellow, green);
        Assert.Greater(red, yellow);
        Assert.AreEqual(rules.pointsPerResident / 2, late, "a late rescue is half a base one, whatever the tag was");
        Assert.Less(late, green, "a late rescue is worth less than a green one that was not left waiting");
    }

    [Test]
    public void A_reunited_household_pays_by_the_member()
    {
        Assert.AreEqual(0, rules.HouseholdBonus(0));
        Assert.AreEqual(rules.householdBonusPerMember * 3, rules.HouseholdBonus(3));
    }
}
