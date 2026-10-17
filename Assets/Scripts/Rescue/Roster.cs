using System.Collections.Generic;
using UnityEngine;

/// <summary>One resident's journey through the mission: what they were, when they were found, how they were when
/// taken, and where they are now. It is what the roster panel, the score and the after-action report are built from.</summary>
public class ResidentRecord
{
    public Survivor survivor;
    public string name;
    public string household;
    public CharacterCategory category;
    public Situation situation;
    public Need needs;
    public ResidentStatus status = ResidentStatus.Waiting;

    public float foundAt = -1f;         // mission seconds; -1 = never
    public float pickedUpAt = -1f;
    public float droppedAt = -1f;       // stepped off the boat at the hall
    public float registeredAt = -1f;

    public Triage tagWhenPickedUp = Triage.Green;
    public bool criticalWhenPickedUp;
    public float conditionWhenPickedUp = 1f;
    /// <summary>When they were taken, nobody more urgent that the crew knew about (and could have taken) was left waiting.</summary>
    public bool followedPriority = true;
    public int points;
    /// <summary>The line or the swimmer got them to the boat, rather than the boat getting to them.</summary>
    public bool broughtByLine;
    public bool broughtBySwimmer;

    /// <summary>Where they are standing once they are ashore (for the label over them while they are registered).</summary>
    public Transform landedAs;

    // Filled in when the mission ends, for those who were not got out (what the report says about them).
    public Triage endTag = Triage.Green;
    public bool endCritical;
    public bool leftInLiveWire;

    public bool PickedUp => pickedUpAt >= 0f;
    public bool IsSafe => status == ResidentStatus.Registered;
}

/// <summary>Everybody the mission is about, and the households they belong to.</summary>
public class Roster
{
    public readonly List<ResidentRecord> Records = new List<ResidentRecord>();

    public Roster(IEnumerable<Survivor> survivors)
    {
        var sorted = new List<Survivor>(survivors);
        // A repeatable order, families together, so the panel and the report do not shuffle between runs.
        sorted.Sort((a, b) =>
        {
            int byHousehold = string.CompareOrdinal(a.household, b.household);
            return byHousehold != 0 ? byHousehold : string.CompareOrdinal(a.DisplayName, b.DisplayName);
        });
        foreach (var s in sorted)
        {
            var visual = s.GetComponent<SurvivorVisual>();
            Records.Add(new ResidentRecord
            {
                survivor = s,
                name = s.DisplayName,
                household = s.household,
                category = visual != null && visual.Character != null ? visual.Character.category : CharacterCategory.Adult,
                situation = s.situation,
                needs = s.needs
            });
        }
    }

    public int Count => Records.Count;

    public ResidentRecord Find(Survivor s)
    {
        foreach (var r in Records) if (r.survivor == s) return r;
        return null;
    }

    public int CountWith(ResidentStatus status)
    {
        int n = 0;
        foreach (var r in Records) if (r.status == status) n++;
        return n;
    }

    /// <summary>Taken aboard at some point (aboard, ashore or registered).</summary>
    public int PickedUpCount
    {
        get
        {
            int n = 0;
            foreach (var r in Records) if (r.PickedUp) n++;
            return n;
        }
    }

    public List<ResidentRecord> Household(string household)
    {
        var members = new List<ResidentRecord>();
        if (string.IsNullOrEmpty(household)) return members;
        foreach (var r in Records) if (r.household == household) members.Add(r);
        return members;
    }

    public bool HouseholdReunited(string household)
    {
        var members = Household(household);
        if (members.Count == 0) return false;
        foreach (var m in members) if (!m.IsSafe) return false;
        return true;
    }

    /// <summary>The household names that have more than one member, in order of first appearance.</summary>
    public List<string> SharedHouseholds()
    {
        var seen = new List<string>();
        foreach (var r in Records)
        {
            if (string.IsNullOrEmpty(r.household) || seen.Contains(r.household)) continue;
            if (Household(r.household).Count > 1) seen.Add(r.household);
        }
        return seen;
    }

    /// <summary>
    /// Was this the right resident to take next? Yes unless somebody who is tagged more urgently, whom the crew has
    /// found, and whom they could actually have taken, was left waiting.
    /// </summary>
    public bool WasMostUrgent(ResidentRecord taken, Triage tagWhenTaken)
    {
        foreach (var r in Records)
        {
            if (r == taken || r.PickedUp) continue;
            var s = r.survivor;
            if (s == null || s.IsRescued || !s.Discovered) continue;       // not known about
            if (!s.HasRequiredItems || s.InLiveWire) continue;             // could not have been taken anyway
            if (s.Triage > tagWhenTaken) return false;
        }
        return true;
    }
}
