using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The four numbers on the Figma "Mission Complete" card: Time Taken, Survivors Saved, Areas Cleared, Supplies Used.
/// Worked out from the roster and the pack, so they always agree with the full report.
/// </summary>
public struct MissionStats
{
    public float timeTaken;
    public float timeLimit;
    public int saved, total;
    public int areasCleared, areas;
    public int itemsUsed, itemsPacked, uses;

    /// <summary>Residents waiting closer than this to each other count as one area (a junction, a roof row, a side street).</summary>
    public const float AreaRadius = 18f;

    public string TimeText => Clock(timeTaken) + " / " + Clock(timeLimit);
    public string SavedText => saved + " / " + total;
    public string AreasText => areasCleared + " / " + areas;
    public string SuppliesText => itemsPacked == 0 ? "None packed" : itemsUsed + " / " + itemsPacked + " items · " + uses + (uses == 1 ? " use" : " uses");

    public static string Clock(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        return Mathf.FloorToInt(seconds / 60f) + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00");
    }

    public static MissionStats From(RescueManager m)
    {
        var stats = new MissionStats { timeTaken = Mathf.Min(m.Elapsed, m.timeLimit), timeLimit = m.timeLimit };
        if (m.Roster != null)
        {
            var positions = new List<Vector3>();
            var safe = new List<bool>();
            foreach (var r in m.Roster.Records)
            {
                positions.Add(r.startPosition);
                safe.Add(r.IsSafe);
                stats.total++;
                if (r.IsSafe) stats.saved++;
            }
            Areas(positions, safe, AreaRadius, out stats.areasCleared, out stats.areas);
        }
        foreach (var id in LoadoutState.Selected)
        {
            int n = LoadoutState.Uses(id);
            stats.itemsPacked++;
            stats.uses += n;
            if (n > 0) stats.itemsUsed++;
        }
        return stats;
    }

    /// <summary>
    /// Groups the residents into areas (anyone within <paramref name="radius"/> of somebody already in a group joins it)
    /// and counts the areas where everybody was got to safety.
    /// </summary>
    public static void Areas(IList<Vector3> positions, IList<bool> safe, float radius, out int cleared, out int total)
    {
        int n = positions.Count;
        var group = new int[n];
        for (int i = 0; i < n; i++) group[i] = -1;
        int groups = 0;
        float r2 = radius * radius;

        for (int i = 0; i < n; i++)
        {
            if (group[i] >= 0) continue;
            // flood-fill everyone reachable through neighbours
            var stack = new Stack<int>();
            stack.Push(i);
            group[i] = groups;
            while (stack.Count > 0)
            {
                int a = stack.Pop();
                for (int b = 0; b < n; b++)
                {
                    if (group[b] >= 0) continue;
                    Vector3 d = positions[a] - positions[b];
                    d.y = 0f;
                    if (d.sqrMagnitude <= r2) { group[b] = groups; stack.Push(b); }
                }
            }
            groups++;
        }

        var allSafe = new bool[groups];
        for (int g = 0; g < groups; g++) allSafe[g] = true;
        for (int i = 0; i < n; i++) if (!safe[i]) allSafe[group[i]] = false;

        total = groups;
        cleared = 0;
        for (int g = 0; g < groups; g++) if (allSafe[g]) cleared++;
    }
}
