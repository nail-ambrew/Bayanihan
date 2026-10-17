using System.Collections.Generic;
using UnityEngine;

/// <summary>How the crew handled the mission, in numbers: what the report's safety section and the clean-run bonus are
/// read from. Nothing here is fatal to anybody; they are marks against the crew.</summary>
public class SafetyRecord
{
    public int HullStrikes;         // hit something solid, or wreckage, hard
    public int UnsafePasses;        // went close past somebody in the water at speed
    public int RushedPickups;       // tried to take somebody aboard before the boat had slowed
    public int Shocks;              // the boat or the swimmer ran into a live wire
    public int UntetheredSwims;     // swam a long way from the boat without the Lubid as a tether
    public int UnneededSwims;       // swam to somebody a thrown line would have reached
    public int LinesLost;           // a thrown line went taut and the person let go

    public int Incidents => HullStrikes + UnsafePasses + RushedPickups + Shocks + UntetheredSwims + UnneededSwims + LinesLost;
    public bool IsClean => Incidents == 0;
}

/// <summary>One household's result: how many of them reached the hall, and the bonus if every one did.</summary>
public class HouseholdResult
{
    public string name;
    public int members;
    public int registered;
    public int bonus;
    public bool Reunited => members > 0 && registered == members;
}

/// <summary>
/// The score, broken down the way the report shows it. It is worked out from the roster alone, so it can be
/// rebuilt after every registration for the live score and once more, with the end-of-mission bonuses, for the
/// report - and tested without playing anything.
///
///   Residents  - 100 each when they are registered at the hall, times how urgent they were tagged when taken
///                (Red 1.5, Yellow 1.25, Green 1), or half if they had gone critical by then.
///   Households - a bonus per member for every household of two or more that got everybody to the hall.
///   Priority   - up to 100, by how often the crew took the most urgent person they could see.
///   Clean run  - 100 if there was nothing in the safety record at all.
/// </summary>
public class ScoreSheet
{
    public int ResidentPoints;
    public int Registered;
    public readonly List<HouseholdResult> Households = new List<HouseholdResult>();
    public int HouseholdBonus;

    /// <summary>How many were taken aboard, and how many of those were the right one to take next.</summary>
    public int PickedUp;
    public int FollowedPriority;
    public int PriorityBonus;
    public int CleanRunBonus;

    /// <summary>The end-of-mission bonuses are in this one.</summary>
    public bool IsFinal;

    public int Total => ResidentPoints + HouseholdBonus + PriorityBonus + CleanRunBonus;

    /// <param name="final">Add the bonuses that are only worked out once the mission is over.</param>
    public static ScoreSheet Build(IReadOnlyList<ResidentRecord> records, RescueRules rules, SafetyRecord safety, bool final)
    {
        var sheet = new ScoreSheet { IsFinal = final };

        foreach (var r in records)
        {
            if (r.PickedUp)
            {
                sheet.PickedUp++;
                if (r.followedPriority) sheet.FollowedPriority++;
            }
            if (r.status != ResidentStatus.Registered) continue;

            sheet.Registered++;
            sheet.ResidentPoints += rules.Points(r.tagWhenPickedUp, r.criticalWhenPickedUp);
        }

        // Households of one are not households as far as the bonus goes.
        var seen = new HashSet<string>();
        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.household) || !seen.Add(r.household)) continue;

            var result = new HouseholdResult { name = r.household };
            foreach (var m in records)
            {
                if (m.household != r.household) continue;
                result.members++;
                if (m.status == ResidentStatus.Registered) result.registered++;
            }
            if (result.members < 2) continue;

            if (result.Reunited) result.bonus = rules.HouseholdBonus(result.members);
            sheet.HouseholdBonus += result.bonus;
            sheet.Households.Add(result);
        }

        if (final)
        {
            if (sheet.PickedUp > 0)
                sheet.PriorityBonus = Mathf.RoundToInt(rules.priorityBonusMax * (float)sheet.FollowedPriority / sheet.PickedUp);
            if (safety != null && safety.IsClean && sheet.Registered > 0)
                sheet.CleanRunBonus = rules.cleanRunBonus;
        }
        return sheet;
    }
}
