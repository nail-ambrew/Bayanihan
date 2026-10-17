using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum ReportTone { Normal, Good, Warning, Bad, Dim }

/// <summary>One line of the report: what it says, how to colour it, and (for a resident) their triage tag.</summary>
public class ReportLine
{
    public string text;
    public ReportTone tone;
    public bool hasTag;
    public Triage tag;
}

/// <summary>A headed block of lines. Column 0 is the left of the screen, column 1 the right.</summary>
public class ReportSection
{
    public readonly string title;
    public readonly int column;
    public readonly List<ReportLine> lines = new List<ReportLine>();

    public ReportSection(string title, int column)
    {
        this.title = title;
        this.column = column;
    }

    public ReportSection Add(string text, ReportTone tone = ReportTone.Normal)
    {
        lines.Add(new ReportLine { text = text, tone = tone });
        return this;
    }

    public ReportSection AddResident(string text, ReportTone tone, Triage tag)
    {
        lines.Add(new ReportLine { text = text, tone = tone, hasTag = true, tag = tag });
        return this;
    }
}

/// <summary>How many times an item in the pack was used.</summary>
public class PackUse
{
    public RescueItemId id;
    public int uses;
}

/// <summary>Everything the report is made from. Gathered by the RescueManager; built by hand in the tests.</summary>
public class ReportInput
{
    public IReadOnlyList<ResidentRecord> records;
    public ScoreSheet sheet;
    public SafetyRecord safety;
    public bool timeUp;
    /// <summary>Seconds into the mission when it ended.</summary>
    public float elapsed;
    public float timeLimit;
    /// <summary>False for a test run straight into the scene, where every item is available.</summary>
    public bool hasLoadout = true;
    public IReadOnlyList<PackUse> pack = new List<PackUse>();
    public Func<RescueItemId, bool> isPacked = id => true;
}

/// <summary>
/// The after-action report: what happened, resident by resident, and what the crew could have done better. It is
/// built from plain data (the roster, the score sheet, the safety record), so it can be tested without playing.
///
/// Left of the screen is the outcome: the result, each resident, each household. Right is the debrief: the score,
/// whether the most urgent were taken first, the safety record, how long people waited, and the pack.
/// </summary>
public static class AfterActionReport
{
    public static string Title(ReportInput input)
    {
        int registered = 0;
        foreach (var r in input.records) if (r.status == ResidentStatus.Registered) registered++;
        return input.records.Count > 0 && registered >= input.records.Count ? "ALL RESIDENTS SAFE! Bayanihan!" : "Mission over";
    }

    public static List<ReportSection> Build(ReportInput input)
    {
        var sections = new List<ReportSection>
        {
            Result(input),
            Residents(input)
        };

        var households = Households(input);
        if (households != null) sections.Add(households);

        sections.Add(ScoreSection(input));
        sections.Add(Priority(input));
        sections.Add(Safety(input));
        var waits = Waits(input);
        if (waits != null) sections.Add(waits);
        sections.Add(Pack(input));
        return sections;
    }

    /// <summary>The report as plain text: what is on the screen, in reading order.</summary>
    public static string ToText(string title, IReadOnlyList<ReportSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (var section in sections)
        {
            sb.AppendLine().AppendLine(section.title);
            foreach (var line in section.lines) sb.AppendLine("  " + line.text);
        }
        return sb.ToString();
    }

    /// <summary>Mission seconds as m:ss.</summary>
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    // ------------------------------------------------------------------ the outcome

    static ReportSection Result(ReportInput i)
    {
        var s = new ReportSection("RESULT", 0);
        int total = i.records.Count, registered = 0, aboard = 0, ashore = 0, out_ = 0;
        foreach (var r in i.records)
        {
            switch (r.status)
            {
                case ResidentStatus.Registered: registered++; break;
                case ResidentStatus.Aboard: aboard++; break;
                case ResidentStatus.Ashore: ashore++; break;
                default: out_++; break;
            }
        }

        s.Add("Evacuated " + registered + " of " + total + " residents.", registered >= total ? ReportTone.Good : ReportTone.Warning);
        s.Add(i.timeUp
            ? "Time ran out at " + Clock(i.timeLimit) + "."
            : "Everybody was safe in " + Clock(i.elapsed) + " of " + Clock(i.timeLimit) + ".", i.timeUp ? ReportTone.Warning : ReportTone.Good);
        if (aboard > 0) s.Add(aboard + " still on the boat when the mission ended.", ReportTone.Warning);
        if (ashore > 0) s.Add(ashore + " at the hall, not yet registered.", ReportTone.Warning);
        if (out_ > 0) s.Add(out_ + " never made it aboard.", ReportTone.Bad);
        return s;
    }

    static ReportSection Residents(ReportInput i)
    {
        var s = new ReportSection("RESIDENTS", 0);
        foreach (var r in i.records)
        {
            string name = string.IsNullOrEmpty(r.name) ? "?" : r.name;
            switch (r.status)
            {
                case ResidentStatus.Registered:
                {
                    string how = r.broughtBySwimmer ? "swum in" : r.broughtByLine ? "on a line" : "by boat";
                    string critical = r.criticalWhenPickedUp ? " - critical, half marks" : "";
                    s.AddResident(name + " - " + how + ", safe " + Clock(r.registeredAt) + " - +" + r.points + critical,
                                  r.criticalWhenPickedUp ? ReportTone.Warning : ReportTone.Good, r.tagWhenPickedUp);
                    break;
                }
                case ResidentStatus.Aboard:
                    s.AddResident(name + " - still on the boat", ReportTone.Warning, r.tagWhenPickedUp);
                    break;
                case ResidentStatus.Ashore:
                    s.AddResident(name + " - at the hall, not registered", ReportTone.Warning, r.tagWhenPickedUp);
                    break;
                default:
                    s.AddResident(name + " - " + WhyNot(r, i.isPacked), ReportTone.Bad, r.endTag);
                    break;
            }
        }
        return s;
    }

    /// <summary>Why somebody was not got out: what the pack lacked, a wire left live, or just not enough time.</summary>
    public static string WhyNot(ResidentRecord r, Func<RescueItemId, bool> isPacked)
    {
        var missing = new List<string>();
        if ((r.needs & Need.Lifebuoy) != 0 && !isPacked(RescueItemId.Lifebuoy)) missing.Add("a Salbabida");
        if ((r.needs & Need.Rope) != 0 && !isPacked(RescueItemId.Rope)) missing.Add("a Lubid");
        if (((r.needs & Need.FirstAid) != 0 || r.endCritical) && !isPacked(RescueItemId.FirstAid)) missing.Add("a Botika");
        if (missing.Count > 0) return "needed " + string.Join(" and ", missing) + " - not packed";

        if (r.leftInLiveWire) return "left in a live wire - the power was never cut";
        if (r.status == ResidentStatus.Held) return "was still coming in on the line";
        if (r.endCritical) return "critical by the time anybody came";
        return r.foundAt >= 0f ? "found, but not reached in time" : "never found";
    }

    static ReportSection Households(ReportInput i)
    {
        if (i.sheet == null || i.sheet.Households.Count == 0) return null;

        var s = new ReportSection("HOUSEHOLDS", 0);
        foreach (var h in i.sheet.Households)
        {
            if (h.Reunited)
            {
                s.Add(h.name + ": all " + h.members + " reached the hall together  +" + h.bonus, ReportTone.Good);
                continue;
            }

            var missing = new List<string>();
            foreach (var r in i.records)
                if (r.household == h.name && r.status != ResidentStatus.Registered) missing.Add(FirstName(r.name));
            s.Add(h.name + ": " + h.registered + " of " + h.members + " - not safe: " + string.Join(", ", missing), ReportTone.Warning);
        }
        return s;
    }

    static string FirstName(string full)
    {
        if (string.IsNullOrEmpty(full)) return "?";
        int space = full.IndexOf(' ');
        return space > 0 ? full.Substring(0, space) : full;
    }

    // ------------------------------------------------------------------ the debrief

    static ReportSection ScoreSection(ReportInput i)
    {
        var s = new ReportSection("SCORE", 1);
        var sheet = i.sheet ?? new ScoreSheet();
        s.Add("Residents  " + sheet.ResidentPoints + "   (" + sheet.Registered + " registered)");
        if (sheet.Households.Count > 0) s.Add("Households  +" + sheet.HouseholdBonus, sheet.HouseholdBonus > 0 ? ReportTone.Good : ReportTone.Dim);
        s.Add("Priority  +" + sheet.PriorityBonus, sheet.PriorityBonus > 0 ? ReportTone.Good : ReportTone.Dim);
        s.Add("Clean run  +" + sheet.CleanRunBonus, sheet.CleanRunBonus > 0 ? ReportTone.Good : ReportTone.Dim);
        s.Add("Total  " + sheet.Total, ReportTone.Good);
        return s;
    }

    static ReportSection Priority(ReportInput i)
    {
        var s = new ReportSection("PRIORITY", 1);
        var sheet = i.sheet ?? new ScoreSheet();
        if (sheet.PickedUp == 0) return s.Add("Nobody was taken aboard.", ReportTone.Dim);

        s.Add(sheet.FollowedPriority + " of " + sheet.PickedUp + " pick-ups were the most urgent the crew knew of.",
              sheet.FollowedPriority == sheet.PickedUp ? ReportTone.Good : ReportTone.Warning);

        var outOfOrder = new List<string>();
        foreach (var r in i.records) if (r.PickedUp && !r.followedPriority) outOfOrder.Add(FirstName(r.name));
        if (outOfOrder.Count > 0) s.Add("Taken while somebody more urgent waited: " + string.Join(", ", outOfOrder), ReportTone.Warning);
        return s;
    }

    static ReportSection Safety(ReportInput i)
    {
        var s = new ReportSection("SAFETY", 1);
        var safety = i.safety ?? new SafetyRecord();
        if (safety.IsClean)
            return s.Add("Clean run - nothing hit, nobody passed at speed, no shocks, no unsafe swims.", ReportTone.Good);

        if (safety.HullStrikes > 0)     s.Add(safety.HullStrikes + " hull strike(s): too fast near walls and wreckage", ReportTone.Warning);
        if (safety.UnsafePasses > 0)    s.Add(safety.UnsafePasses + " fast pass(es) close to someone in the water", ReportTone.Warning);
        if (safety.RushedPickups > 0)   s.Add(safety.RushedPickups + " pick-up(s) tried before the boat had slowed", ReportTone.Warning);
        if (safety.Shocks > 0)          s.Add(safety.Shocks + " shock(s) from a live wire", ReportTone.Warning);
        if (safety.UntetheredSwims > 0) s.Add(safety.UntetheredSwims + " swim(s) far from the boat with no tether (pack the Lubid)", ReportTone.Warning);
        if (safety.UnneededSwims > 0)   s.Add(safety.UnneededSwims + " swim(s) to somebody a thrown line would have reached", ReportTone.Warning);
        if (safety.LinesLost > 0)       s.Add(safety.LinesLost + " line(s) lost: the boat pulled away", ReportTone.Warning);
        return s;
    }

    static ReportSection Waits(ReportInput i)
    {
        float total = 0f, longest = -1f;
        int n = 0;
        string who = null;
        foreach (var r in i.records)
        {
            if (!r.PickedUp) continue;
            float wait = Mathf.Max(0f, r.pickedUpAt - Mathf.Max(0f, r.foundAt));
            total += wait;
            n++;
            if (wait > longest) { longest = wait; who = FirstName(r.name); }
        }
        if (n == 0) return null;

        var s = new ReportSection("RESPONSE", 1);
        s.Add("Average, from found to taken aboard: " + Clock(total / n));
        s.Add("Longest wait: " + who + ", " + Clock(longest));
        return s;
    }

    static ReportSection Pack(ReportInput i)
    {
        var s = new ReportSection("YOUR PACK", 1);
        if (!i.hasLoadout) return s.Add("(test run - every item was available)", ReportTone.Dim);
        if (i.pack.Count == 0) return s.Add("Nothing. You launched empty.", ReportTone.Warning);

        foreach (var use in i.pack)
        {
            var def = RescueItems.Get(use.id);
            string line;
            switch (use.id)
            {
                case RescueItemId.Lifebuoy:   line = use.uses + " pulled from open water"; break;
                case RescueItemId.FirstAid:   line = use.uses + " stabilised"; break;
                case RescueItemId.Rope:       line = use.uses + " hauled out"; break;
                case RescueItemId.Flashlight: line = "switched on " + use.uses + " time(s)"; break;
                case RescueItemId.Radio:      line = use.uses + " call(s): pings and power cuts"; break;
                default:                      line = "rain penalty reduced all mission"; break;
            }
            bool wasted = use.uses == 0 && use.id != RescueItemId.Raincoat;
            s.Add(def.name + ": " + line + (wasted ? "  (unused - a wasted slot)" : ""), wasted ? ReportTone.Warning : ReportTone.Normal);
        }
        return s;
    }
}
