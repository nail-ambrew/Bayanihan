using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Everything about one mission that is not a place: the briefing, the clock, the weather, how rescues are scored
/// and who is out there. The loadout screen reads it for the briefing and the Gameplay scene reads it to put the
/// residents in the water, so the two always agree.
///
/// Lives in Assets/Resources/Missions. Rebuild the shipped one with Bayanihan > Rebuild Mission Asset.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Mission Definition", fileName = "Mission")]
public class MissionDefinition : ScriptableObject
{
    public const string ResourceFolder = "Missions";

    [Header("Briefing")]
    public string title = "Pagbaha sa Barangay · The Water Rises";
    [TextArea(2, 6)] public string briefing =
        "The creek jumped its banks before dawn. Families are on their roofs and clinging to the posts, waving at anything that floats.";
    [Tooltip("Start of the one-line summary under the briefing: when and what.")]
    public string setting = "Daytime · Flood";
    [Tooltip("Extra things the crew has heard that are not about a particular resident: hazards, mostly.")]
    [TextArea(1, 3)] public string[] reports = new string[0];

    [Header("Mission")]
    [Tooltip("Seconds on the clock.")]
    public float timeLimit = 180f;
    [Range(0f, 1f)] public float stormIntensity = 0.7f;
    [Tooltip("How far the flood climbs each minute (metres). 0 = the water holds steady.")]
    public float waterRisePerMinute = 0f;
    [Tooltip("The flood stops climbing after this many metres.")]
    public float maxWaterRise = 0.6f;

    public RescueRules rules = new RescueRules();

    [Header("Residents")]
    public List<ResidentSpec> residents = new List<ResidentSpec>();

    // ------------------------------------------------------------------ the active mission
    static MissionDefinition current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; }

    /// <summary>
    /// The mission being played. The Gameplay scene's MissionBuilder sets it; otherwise it is the one in Resources,
    /// and failing that a plain default - so the rules always exist, even in a scene or a test that has no mission.
    /// </summary>
    public static MissionDefinition Current
    {
        get
        {
            if (current == null) current = LoadShipped();
            if (current == null) current = CreateInstance<MissionDefinition>();
            return current;
        }
        set { current = value; }
    }

    /// <summary>The mission asset that ships with the game, or null if there is none.</summary>
    public static MissionDefinition LoadShipped()
    {
        var all = Resources.LoadAll<MissionDefinition>(ResourceFolder);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    // ------------------------------------------------------------------ what the briefing can say
    public int Count(Situation situation)
    {
        int n = 0;
        foreach (var r in residents) if (r.situation == situation) n++;
        return n;
    }

    public int CountNeeding(Need need)
    {
        int n = 0;
        foreach (var r in residents) if ((r.needs & need) != 0) n++;
        return n;
    }

    /// <summary>Household name to how many of them are out there.</summary>
    public Dictionary<string, int> Households()
    {
        var map = new Dictionary<string, int>();
        foreach (var r in residents)
        {
            if (string.IsNullOrEmpty(r.household)) continue;
            map[r.household] = map.TryGetValue(r.household, out int n) ? n + 1 : 1;
        }
        return map;
    }

    /// <summary>"Daytime · Flood · 10 residents on the roster · boat holds 4 at a time".</summary>
    public string Meta(int boatCapacity)
    {
        string who = residents.Count == 1 ? "1 resident" : residents.Count + " residents";
        return setting + " · " + who + " on the roster · boat holds " + boatCapacity + " at a time";
    }

    /// <summary>
    /// What dispatch has told the crew, worked out from the roster so it can never drift from the level: how many
    /// are where, and which items they will want. Packing is a choice, and this is what it is a choice about.
    /// </summary>
    public string Intel()
    {
        var sb = new StringBuilder();

        var where = new List<string>();
        AddCount(where, Count(Situation.OpenWater), "in open water");
        AddCount(where, Count(Situation.Clinging), "clinging to posts");
        AddCount(where, Count(Situation.Rooftop), "on rooftops");
        AddCount(where, Count(Situation.Trapped), "trapped behind debris");
        if (where.Count > 0) sb.AppendLine("• " + string.Join(", ", where));

        var items = new List<string>();
        AddCount(items, CountNeeding(Need.Lifebuoy), "need a Salbabida");
        AddCount(items, CountNeeding(Need.Rope), "need a Lubid");
        AddCount(items, CountNeeding(Need.FirstAid), "hurt, need a Botika");
        if (items.Count > 0) sb.AppendLine("• " + string.Join(" · ", items));

        if (reports != null)
            foreach (var r in reports)
                if (!string.IsNullOrWhiteSpace(r)) sb.AppendLine("• " + r.Trim());
        return sb.ToString().TrimEnd();
    }

    static void AddCount(List<string> into, int n, string what)
    {
        if (n > 0) into.Add(n + " " + what);
    }
}
