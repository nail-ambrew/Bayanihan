using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// How a mission is scored: points for each resident who is registered, by how urgent they were when they were taken;
/// a bonus for every household that gets all of its members to the hall; and, once the mission is over, bonuses for
/// taking the most urgent first and for running a clean mission.
/// </summary>
public class ScoreSheetTests
{
    RescueRules rules;

    [SetUp]
    public void Defaults() { rules = new RescueRules(); }

    static ResidentRecord Registered(string name, string household = "", Triage tag = Triage.Green,
                                     bool critical = false, bool followed = true)
    {
        return new ResidentRecord
        {
            name = name,
            household = household,
            status = ResidentStatus.Registered,
            pickedUpAt = 60f,
            registeredAt = 90f,
            tagWhenPickedUp = tag,
            criticalWhenPickedUp = critical,
            followedPriority = followed
        };
    }

    static ResidentRecord Waiting(string name, string household = "") =>
        new ResidentRecord { name = name, household = household };

    static ResidentRecord Aboard(string name, string household = "", bool followed = true) =>
        new ResidentRecord { name = name, household = household, status = ResidentStatus.Aboard, pickedUpAt = 70f, followedPriority = followed };

    ScoreSheet Live(params ResidentRecord[] records) => ScoreSheet.Build(records, rules, new SafetyRecord(), false);
    ScoreSheet Final(SafetyRecord safety, params ResidentRecord[] records) => ScoreSheet.Build(records, rules, safety, true);

    // ------------------------------------------------------------------ residents

    [Test]
    public void A_registered_resident_scores_by_how_urgent_they_were_when_taken()
    {
        Assert.AreEqual(100, Live(Registered("A", tag: Triage.Green)).ResidentPoints);
        Assert.AreEqual(125, Live(Registered("A", tag: Triage.Yellow)).ResidentPoints);
        Assert.AreEqual(150, Live(Registered("A", tag: Triage.Red)).ResidentPoints);
        Assert.AreEqual(50, Live(Registered("A", tag: Triage.Red, critical: true)).ResidentPoints, "left too long: half marks");
    }

    [Test]
    public void Only_a_resident_who_is_on_the_list_scores()
    {
        var sheet = Live(Registered("A"), Aboard("B"), Waiting("C"),
                         new ResidentRecord { name = "D", status = ResidentStatus.Ashore, pickedUpAt = 50f });
        Assert.AreEqual(100, sheet.ResidentPoints, "aboard, ashore and still waiting are worth nothing yet");
        Assert.AreEqual(1, sheet.Registered);
    }

    [Test]
    public void The_total_is_every_part_added_up()
    {
        var sheet = Final(new SafetyRecord(),
            Registered("A", "Reyes", Triage.Red), Registered("B", "Reyes", Triage.Yellow));
        Assert.AreEqual(sheet.ResidentPoints + sheet.HouseholdBonus + sheet.PriorityBonus + sheet.CleanRunBonus, sheet.Total);
        Assert.AreEqual(150 + 125 + rules.HouseholdBonus(2) + rules.priorityBonusMax + rules.cleanRunBonus, sheet.Total);
    }

    // ------------------------------------------------------------------ households

    [Test]
    public void A_household_with_everybody_registered_earns_a_bonus_for_each_member()
    {
        var sheet = Live(Registered("A", "Reyes"), Registered("B", "Reyes"), Registered("C", "Reyes"), Registered("D", "Cruz"));
        Assert.AreEqual(1, sheet.Households.Count, "Cruz is one person: not a household for the bonus");
        Assert.AreEqual("Reyes", sheet.Households[0].name);
        Assert.IsTrue(sheet.Households[0].Reunited);
        Assert.AreEqual(3, sheet.Households[0].members);
        Assert.AreEqual(rules.HouseholdBonus(3), sheet.HouseholdBonus);
    }

    [Test]
    public void A_household_with_somebody_missing_earns_nothing_but_is_still_listed()
    {
        var sheet = Live(Registered("A", "Reyes"), Registered("B", "Reyes"), Aboard("C", "Reyes"));
        Assert.AreEqual(0, sheet.HouseholdBonus);
        Assert.AreEqual(1, sheet.Households.Count);
        Assert.IsFalse(sheet.Households[0].Reunited);
        Assert.AreEqual(2, sheet.Households[0].registered);
        Assert.AreEqual(3, sheet.Households[0].members);
    }

    [Test]
    public void Residents_with_no_household_never_form_one()
    {
        var sheet = Live(Registered("A"), Registered("B"));
        Assert.AreEqual(0, sheet.Households.Count);
        Assert.AreEqual(0, sheet.HouseholdBonus);
    }

    [Test]
    public void Two_households_are_worked_out_separately()
    {
        var sheet = Live(Registered("A", "Reyes"), Registered("B", "Reyes"), Registered("C", "Cruz"), Waiting("D", "Cruz"));
        Assert.AreEqual(2, sheet.Households.Count);
        Assert.AreEqual(rules.HouseholdBonus(2), sheet.HouseholdBonus, "only the Reyes got everybody to the hall");
    }

    // ------------------------------------------------------------------ the end-of-mission bonuses

    [Test]
    public void The_bonuses_for_how_the_mission_was_run_only_appear_in_the_final_sheet()
    {
        var live = Live(Registered("A"));
        Assert.IsFalse(live.IsFinal);
        Assert.AreEqual(0, live.PriorityBonus);
        Assert.AreEqual(0, live.CleanRunBonus);

        var final = Final(new SafetyRecord(), Registered("A"));
        Assert.IsTrue(final.IsFinal);
        Assert.AreEqual(rules.priorityBonusMax, final.PriorityBonus);
        Assert.AreEqual(rules.cleanRunBonus, final.CleanRunBonus);
    }

    [Test]
    public void The_priority_bonus_follows_how_often_the_most_urgent_was_taken()
    {
        var sheet = Final(new SafetyRecord(),
            Registered("A", followed: true), Registered("B", followed: true), Registered("C", followed: false));
        Assert.AreEqual(3, sheet.PickedUp);
        Assert.AreEqual(2, sheet.FollowedPriority);
        Assert.AreEqual(UnityEngine.Mathf.RoundToInt(rules.priorityBonusMax * 2f / 3f), sheet.PriorityBonus);

        var none = Final(new SafetyRecord(), Registered("A", followed: false));
        Assert.AreEqual(0, none.PriorityBonus);
    }

    [Test]
    public void Those_taken_aboard_but_not_yet_registered_still_count_towards_the_priority_bonus()
    {
        var sheet = Final(new SafetyRecord(), Registered("A", followed: true), Aboard("B", followed: false));
        Assert.AreEqual(2, sheet.PickedUp, "the choice was made when they were taken, wherever they are now");
        Assert.AreEqual(1, sheet.FollowedPriority);
    }

    [Test]
    public void Nobody_taken_aboard_means_no_priority_bonus_at_all()
    {
        var sheet = Final(new SafetyRecord(), Waiting("A"), Waiting("B"));
        Assert.AreEqual(0, sheet.PriorityBonus);
        Assert.AreEqual(0, sheet.CleanRunBonus, "doing nothing is not a clean run");
    }

    [Test]
    public void Anything_in_the_safety_record_loses_the_clean_run_bonus()
    {
        foreach (var mark in new System.Action<SafetyRecord>[]
        {
            s => s.HullStrikes = 1, s => s.UnsafePasses = 1, s => s.RushedPickups = 1, s => s.Shocks = 1,
            s => s.UntetheredSwims = 1, s => s.UnneededSwims = 1, s => s.LinesLost = 1
        })
        {
            var safety = new SafetyRecord();
            mark(safety);
            Assert.IsFalse(safety.IsClean);
            Assert.AreEqual(0, Final(safety, Registered("A")).CleanRunBonus);
        }
        Assert.IsTrue(new SafetyRecord().IsClean);
        Assert.AreEqual(rules.cleanRunBonus, Final(new SafetyRecord(), Registered("A")).CleanRunBonus);
    }

    [Test]
    public void An_empty_roster_scores_nothing_without_complaint()
    {
        var sheet = Final(new SafetyRecord());
        Assert.AreEqual(0, sheet.Total);
        Assert.AreEqual(0, sheet.Households.Count);
    }

    [Test]
    public void The_safety_record_counts_every_kind_of_incident()
    {
        var safety = new SafetyRecord
        {
            HullStrikes = 1, UnsafePasses = 2, RushedPickups = 3, Shocks = 4, UntetheredSwims = 5, UnneededSwims = 6, LinesLost = 7
        };
        Assert.AreEqual(28, safety.Incidents);
    }
}
