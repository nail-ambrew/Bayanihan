using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// The after-action report: that it says what happened to each resident and why, reports the household, priority and
/// safety results truthfully, and never fails on an odd mission (nobody rescued, nobody found, an empty pack).
/// </summary>
public class AfterActionReportTests
{
    RescueRules rules;

    [SetUp]
    public void Defaults() { rules = new RescueRules(); }

    // ------------------------------------------------------------------ building a mission by hand

    ResidentRecord Safe(string name, string household = "", Triage tag = Triage.Green, float found = 10f,
                               float taken = 60f, float registered = 90f, bool critical = false, bool followed = true,
                               bool line = false, bool swim = false)
    {
        return new ResidentRecord
        {
            name = name,
            household = household,
            status = ResidentStatus.Registered,
            foundAt = found,
            pickedUpAt = taken,
            registeredAt = registered,
            tagWhenPickedUp = tag,
            criticalWhenPickedUp = critical,
            followedPriority = followed,
            broughtByLine = line,
            broughtBySwimmer = swim,
            points = rules.Points(tag, critical)
        };
    }

    static ResidentRecord Out(string name, Need needs = Need.None, string household = "", float found = -1f)
    {
        return new ResidentRecord { name = name, household = household, needs = needs, foundAt = found };
    }

    ReportInput Input(params ResidentRecord[] records) => Input(new SafetyRecord(), records);

    ReportInput Input(SafetyRecord safety, params ResidentRecord[] records)
    {
        var list = new List<ResidentRecord>(records);
        return new ReportInput
        {
            records = list,
            sheet = ScoreSheet.Build(list, rules, safety, true),
            safety = safety,
            timeUp = false,
            elapsed = 200f,
            timeLimit = 360f
        };
    }

    static ReportSection Section(List<ReportSection> sections, string title)
    {
        var found = sections.Find(s => s.title == title);
        Assert.IsNotNull(found, "no " + title + " section; there is " + string.Join(", ", sections.Select(s => s.title)));
        return found;
    }

    static string Joined(ReportSection section) => string.Join("\n", section.lines.Select(l => l.text));

    // ------------------------------------------------------------------ headline and result

    [Test]
    public void The_title_celebrates_only_when_everybody_is_safe()
    {
        Assert.AreEqual("ALL RESIDENTS SAFE! Bayanihan!", AfterActionReport.Title(Input(Safe("A"), Safe("B"))));
        Assert.AreEqual("Mission over", AfterActionReport.Title(Input(Safe("A"), Out("B"))));
        Assert.AreEqual("Mission over", AfterActionReport.Title(Input()), "nobody on the roster is not a victory");
    }

    [Test]
    public void The_result_counts_who_is_safe_and_where_everybody_else_is()
    {
        var input = Input(Safe("A"), Safe("B"),
            new ResidentRecord { name = "C", status = ResidentStatus.Aboard, pickedUpAt = 100f },
            new ResidentRecord { name = "D", status = ResidentStatus.Ashore, pickedUpAt = 100f },
            Out("E"));
        input.timeUp = true;
        var result = Section(AfterActionReport.Build(input), "RESULT");
        string text = Joined(result);

        StringAssert.Contains("Evacuated 2 of 5 residents.", text);
        StringAssert.Contains("Time ran out at 6:00.", text);
        StringAssert.Contains("1 still on the boat", text);
        StringAssert.Contains("1 at the hall, not yet registered", text);
        StringAssert.Contains("1 never made it aboard", text);
    }

    [Test]
    public void A_mission_finished_in_time_says_how_quickly()
    {
        var text = Joined(Section(AfterActionReport.Build(Input(Safe("A"))), "RESULT"));
        StringAssert.Contains("Everybody was safe in 3:20 of 6:00.", text);
        StringAssert.DoesNotContain("Time ran out", text);
    }

    [Test]
    public void Times_are_written_as_minutes_and_seconds()
    {
        Assert.AreEqual("0:00", AfterActionReport.Clock(0f));
        Assert.AreEqual("0:59", AfterActionReport.Clock(59f));
        Assert.AreEqual("1:01", AfterActionReport.Clock(61f));
        Assert.AreEqual("2:05", AfterActionReport.Clock(125.4f));
        Assert.AreEqual("0:00", AfterActionReport.Clock(-5f), "a time before the start is not negative");
    }

    // ------------------------------------------------------------------ each resident

    [Test]
    public void Each_resident_gets_a_line_with_how_they_came_and_what_they_scored()
    {
        var input = Input(Safe("Ben Reyes", tag: Triage.Red, registered: 95f),
                          Safe("Tess Cruz", line: true), Safe("Joy Lim", swim: true));
        var lines = Section(AfterActionReport.Build(input), "RESIDENTS").lines;

        Assert.AreEqual(3, lines.Count);
        StringAssert.Contains("Ben Reyes - by boat, safe 1:35 - +150", lines[0].text);
        StringAssert.Contains("on a line", lines[1].text);
        StringAssert.Contains("swum in", lines[2].text);
        Assert.IsTrue(lines[0].hasTag);
        Assert.AreEqual(Triage.Red, lines[0].tag, "the tag they had when taken");
        Assert.AreEqual(ReportTone.Good, lines[0].tone);
    }

    [Test]
    public void A_critical_rescue_is_marked_as_half_marks()
    {
        var input = Input(Safe("Ben", tag: Triage.Red, critical: true));
        var line = Section(AfterActionReport.Build(input), "RESIDENTS").lines[0];
        StringAssert.Contains("critical, half marks", line.text);
        StringAssert.Contains("+50", line.text);
        Assert.AreEqual(ReportTone.Warning, line.tone);
    }

    [Test]
    public void Somebody_left_out_there_is_given_the_reason()
    {
        var packed = new HashSet<RescueItemId> { RescueItemId.Rope };
        var input = Input(
            Out("Needs a ring", Need.Lifebuoy, found: 40f),
            Out("Needs a kit", Need.FirstAid, found: 40f),
            Out("Needs both", Need.Lifebuoy | Need.FirstAid, found: 40f),
            Out("Unlucky", Need.None, found: 40f),
            Out("Lost", Need.None, found: -1f));
        input.isPacked = packed.Contains;

        var lines = Section(AfterActionReport.Build(input), "RESIDENTS").lines.Select(l => l.text).ToList();
        StringAssert.Contains("needed a Salbabida - not packed", lines[0]);
        StringAssert.Contains("needed a Botika - not packed", lines[1]);
        StringAssert.Contains("needed a Salbabida and a Botika - not packed", lines[2]);
        StringAssert.Contains("found, but not reached in time", lines[3]);
        StringAssert.Contains("never found", lines[4]);
    }

    [Test]
    public void A_wire_left_live_and_a_resident_gone_critical_are_named_as_the_reason()
    {
        var wire = Out("Wired", Need.None, found: 40f);
        wire.leftInLiveWire = true;
        var critical = Out("Cold", Need.None, found: 40f);
        critical.endCritical = true;
        var coming = Out("Coming", Need.None, found: 40f);
        coming.status = ResidentStatus.Held;

        var lines = Section(AfterActionReport.Build(Input(wire, critical, coming)), "RESIDENTS").lines.Select(l => l.text).ToList();
        StringAssert.Contains("left in a live wire - the power was never cut", lines[0]);
        StringAssert.Contains("critical by the time anybody came", lines[1]);
        StringAssert.Contains("still coming in on the line", lines[2]);
    }

    [Test]
    public void The_roster_and_the_pack_explain_a_left_behind_resident_who_is_critical_without_a_kit()
    {
        var critical = Out("Cold", Need.None, found: 40f);
        critical.endCritical = true;
        var input = Input(critical);
        input.isPacked = id => id != RescueItemId.FirstAid;
        StringAssert.Contains("needed a Botika - not packed", Section(AfterActionReport.Build(input), "RESIDENTS").lines[0].text);
    }

    // ------------------------------------------------------------------ households

    [Test]
    public void A_reunited_household_is_celebrated_and_an_incomplete_one_names_who_is_missing()
    {
        var input = Input(
            Safe("Ana Reyes", "Reyes"), Safe("Ben Reyes", "Reyes"),
            Safe("Cora Cruz", "Cruz"), Out("Dan Cruz", household: "Cruz"), Out("Eli Cruz", household: "Cruz"));
        var lines = Section(AfterActionReport.Build(input), "HOUSEHOLDS").lines;

        Assert.AreEqual(2, lines.Count);
        StringAssert.Contains("Reyes: all 2 reached the hall together  +" + rules.HouseholdBonus(2), lines[0].text);
        Assert.AreEqual(ReportTone.Good, lines[0].tone);
        StringAssert.Contains("Cruz: 1 of 3", lines[1].text);
        StringAssert.Contains("Dan, Eli", lines[1].text);
        Assert.AreEqual(ReportTone.Warning, lines[1].tone);
    }

    [Test]
    public void With_no_households_there_is_no_households_section()
    {
        var sections = AfterActionReport.Build(Input(Safe("A"), Safe("B")));
        Assert.IsNull(sections.Find(s => s.title == "HOUSEHOLDS"));
    }

    // ------------------------------------------------------------------ the debrief

    [Test]
    public void The_score_section_adds_up_to_the_total()
    {
        var input = Input(Safe("A", "Reyes", Triage.Red), Safe("B", "Reyes", Triage.Yellow));
        var lines = Section(AfterActionReport.Build(input), "SCORE").lines.Select(l => l.text).ToList();

        Assert.IsTrue(lines.Any(l => l.StartsWith("Residents  275")), string.Join(" | ", lines));
        Assert.IsTrue(lines.Any(l => l.StartsWith("Households  +" + rules.HouseholdBonus(2))));
        Assert.IsTrue(lines.Any(l => l.StartsWith("Clean run  +" + rules.cleanRunBonus)));
        Assert.IsTrue(lines.Any(l => l.StartsWith("Total  " + input.sheet.Total)));
    }

    [Test]
    public void Priority_says_how_often_the_most_urgent_was_taken_and_names_the_exceptions()
    {
        var input = Input(Safe("Ana Reyes", followed: true), Safe("Ben Cruz", followed: false), Safe("Cora Lim", followed: false));
        var text = Joined(Section(AfterActionReport.Build(input), "PRIORITY"));
        StringAssert.Contains("1 of 3 pick-ups were the most urgent", text);
        StringAssert.Contains("Ben, Cora", text);

        var perfect = Joined(Section(AfterActionReport.Build(Input(Safe("A"), Safe("B"))), "PRIORITY"));
        StringAssert.Contains("2 of 2", perfect);
        StringAssert.DoesNotContain("while somebody more urgent waited", perfect);
    }

    [Test]
    public void A_clean_safety_record_says_so()
    {
        var text = Joined(Section(AfterActionReport.Build(Input(Safe("A"))), "SAFETY"));
        StringAssert.Contains("Clean run", text);
    }

    [Test]
    public void Every_kind_of_incident_is_listed_by_how_many_times_it_happened()
    {
        var safety = new SafetyRecord
        {
            HullStrikes = 2, UnsafePasses = 1, RushedPickups = 3, Shocks = 1, UntetheredSwims = 2, UnneededSwims = 1, LinesLost = 4
        };
        var section = Section(AfterActionReport.Build(Input(safety, Safe("A"))), "SAFETY");
        string text = Joined(section);

        StringAssert.Contains("2 hull strike(s)", text);
        StringAssert.Contains("1 fast pass(es)", text);
        StringAssert.Contains("3 pick-up(s) tried before the boat had slowed", text);
        StringAssert.Contains("1 shock(s) from a live wire", text);
        StringAssert.Contains("2 swim(s) far from the boat with no tether", text);
        StringAssert.Contains("1 swim(s) to somebody a thrown line would have reached", text);
        StringAssert.Contains("4 line(s) lost", text);
        StringAssert.DoesNotContain("Clean run", text);
    }

    [Test]
    public void Only_the_incidents_that_happened_are_listed()
    {
        var text = Joined(Section(AfterActionReport.Build(Input(new SafetyRecord { Shocks = 1 }, Safe("A"))), "SAFETY"));
        StringAssert.Contains("shock", text);
        StringAssert.DoesNotContain("hull strike", text);
        StringAssert.DoesNotContain("line(s) lost", text);
    }

    [Test]
    public void Response_times_are_the_wait_from_found_to_taken_aboard()
    {
        var input = Input(Safe("Ana Reyes", found: 10f, taken: 70f), Safe("Ben Cruz", found: 20f, taken: 200f));
        var text = Joined(Section(AfterActionReport.Build(input), "RESPONSE"));
        StringAssert.Contains("Average, from found to taken aboard: 2:00", text);     // (60 + 180) / 2
        StringAssert.Contains("Longest wait: Ben, 3:00", text);
    }

    [Test]
    public void With_nobody_taken_aboard_there_is_no_response_section()
    {
        Assert.IsNull(AfterActionReport.Build(Input(Out("A"), Out("B"))).Find(s => s.title == "RESPONSE"));
    }

    // ------------------------------------------------------------------ the pack

    [Test]
    public void An_unused_item_is_called_a_wasted_slot()
    {
        var input = Input(Safe("A"));
        input.pack = new List<PackUse>
        {
            new PackUse { id = RescueItemId.Lifebuoy, uses = 2 },
            new PackUse { id = RescueItemId.Radio, uses = 0 },
            new PackUse { id = RescueItemId.Raincoat, uses = 0 }
        };
        var lines = Section(AfterActionReport.Build(input), "YOUR PACK").lines.Select(l => l.text).ToList();

        StringAssert.Contains("Salbabida: 2 pulled from open water", lines[0]);
        StringAssert.DoesNotContain("wasted", lines[0]);
        StringAssert.Contains("Radyo", lines[1]);
        StringAssert.Contains("a wasted slot", lines[1]);
        StringAssert.DoesNotContain("wasted", lines[2], "the raincoat does its work without being used");
    }

    [Test]
    public void A_test_run_and_an_empty_pack_are_both_said_plainly()
    {
        var testRun = Input(Safe("A"));
        testRun.hasLoadout = false;
        StringAssert.Contains("test run", Joined(Section(AfterActionReport.Build(testRun), "YOUR PACK")));

        var empty = Input(Safe("A"));
        StringAssert.Contains("launched empty", Joined(Section(AfterActionReport.Build(empty), "YOUR PACK")));
    }

    // ------------------------------------------------------------------ the whole thing

    [Test]
    public void The_report_reads_as_plain_text_with_every_section_in_it()
    {
        var input = Input(Safe("Ana Reyes", "Reyes"), Safe("Ben Reyes", "Reyes"), Out("Cora", Need.Lifebuoy));
        input.pack = new List<PackUse> { new PackUse { id = RescueItemId.Rope, uses = 1 } };
        var sections = AfterActionReport.Build(input);
        string text = AfterActionReport.ToText(AfterActionReport.Title(input), sections);

        StringAssert.StartsWith("Mission over", text);
        foreach (var title in new[] { "RESULT", "RESIDENTS", "HOUSEHOLDS", "SCORE", "PRIORITY", "SAFETY", "RESPONSE", "YOUR PACK" })
            StringAssert.Contains(title, text);
        Assert.IsTrue(sections.All(s => s.column == 0 || s.column == 1));
    }

    [Test]
    public void An_empty_mission_still_produces_a_report()
    {
        var input = Input();
        Assert.DoesNotThrow(() => AfterActionReport.ToText(AfterActionReport.Title(input), AfterActionReport.Build(input)));
    }
}
