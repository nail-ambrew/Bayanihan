using System.Collections.Generic;
using UnityEngine;

/// <summary>The six things a player can pack for the boat (4 slots).</summary>
public enum RescueItemId { Lifebuoy, FirstAid, Flashlight, Rope, Radio, Raincoat }

public enum ItemUse { Passive, Toggle, Cooldown }

/// <summary>
/// What a survivor needs before they can be pulled aboard. Set these per Survivor in the Inspector.
/// Combine them freely (e.g. Lifebuoy + FirstAid = injured and floating in open water).
/// </summary>
[System.Flags]
public enum Need
{
    None = 0,
    Lifebuoy = 1,   // in open water  -> needs Salbabida
    Rope = 2,       // behind debris  -> needs Lubid
    FirstAid = 4    // injured        -> needs Botika (2 s hold)
}

public class RescueItemDef
{
    public RescueItemId id;
    public string name;          // Filipino name shown big on the card
    public string english;       // small subtitle
    public string description;
    public ItemUse use;
    public string hotkey;
    public float cooldown;
    public bool recommended;
    public Color color;          // placeholder icon colour (used when no sprite is assigned)
    public string glyph;         // placeholder icon letter

    public string BadgeText
    {
        get
        {
            switch (use)
            {
                case ItemUse.Toggle:   return $"Toggle · {hotkey}";
                case ItemUse.Cooldown: return $"{cooldown:0}s cooldown · {hotkey}";
                default:               return "Passive";
            }
        }
    }
}

/// <summary>Item catalogue. Kept in code on purpose (6 items); can be swapped for ScriptableObjects later.</summary>
public static class RescueItems
{
    public const int Slots = 4;

    public static readonly RescueItemDef[] All =
    {
        new RescueItemDef
        {
            id = RescueItemId.Lifebuoy, name = "Salbabida", english = "Lifebuoy",
            description = "Lets you pull someone out of open water. Without it you can only pass them by.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.90f, 0.30f, 0.22f), glyph = "O"
        },
        new RescueItemDef
        {
            id = RescueItemId.FirstAid, name = "Botika", english = "First-aid kit",
            description = "Stabilise an injured resident (a 2-second hold) so they can board at all.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.86f, 0.25f, 0.30f), glyph = "+"
        },
        new RescueItemDef
        {
            id = RescueItemId.Flashlight, name = "Flashlight", english = "Flashlight",
            description = "Widens and lengthens the visibility cone. At dusk and at night, searching without it is nearly blind.",
            use = ItemUse.Toggle, hotkey = "F", color = new Color(0.95f, 0.75f, 0.20f), glyph = "F"
        },
        new RescueItemDef
        {
            id = RescueItemId.Rope, name = "Lubid", english = "Rope",
            description = "Haul residents out from behind debris and off collapsing structures.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.80f, 0.55f, 0.20f), glyph = "R"
        },
        new RescueItemDef
        {
            id = RescueItemId.Radio, name = "Radyo", english = "Two-way radio",
            description = "Points to the nearest resident you have not found yet, then cools down for 20 seconds. Also calls the power company to cut a downed live wire (C).",
            use = ItemUse.Cooldown, hotkey = "Q", cooldown = 20f, color = new Color(0.30f, 0.35f, 0.45f), glyph = "Q"
        },
        new RescueItemDef
        {
            id = RescueItemId.Raincoat, name = "Kapote", english = "Raincoat",
            description = "Cuts the rain visibility penalty for everyone in the boat.",
            use = ItemUse.Passive, color = new Color(0.98f, 0.65f, 0.10f), glyph = "K"
        },
    };

    /// <summary>The "Suggested pack" button.</summary>
    public static readonly RescueItemId[] Suggested =
        { RescueItemId.Lifebuoy, RescueItemId.FirstAid, RescueItemId.Rope, RescueItemId.Radio };

    public static RescueItemDef Get(RescueItemId id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return null;
    }

    /// <summary>Which survivor need (if any) an item satisfies.</summary>
    public static Need NeedFor(RescueItemId id)
    {
        switch (id)
        {
            case RescueItemId.Lifebuoy: return Need.Lifebuoy;
            case RescueItemId.Rope:     return Need.Rope;
            case RescueItemId.FirstAid: return Need.FirstAid;
            default:                    return Need.None;
        }
    }
}

/// <summary>
/// The pack chosen on the loadout screen. Static, so it survives the scene change from Loadout to Gameplay.
/// </summary>
public static class LoadoutState
{
    /// <summary>Editor convenience: pressing Play directly in the Gameplay scene gives you every item.</summary>
    public static bool DevAllItemsWhenEmpty = true;

    static readonly List<RescueItemId> selected = new List<RescueItemId>();
    static readonly Dictionary<RescueItemId, int> uses = new Dictionary<RescueItemId, int>();

    public static bool HasLoadout { get; private set; }
    public static IReadOnlyList<RescueItemId> Selected => selected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        selected.Clear();
        uses.Clear();
        HasLoadout = false;
    }

    public static void Set(IEnumerable<RescueItemId> items)
    {
        selected.Clear();
        selected.AddRange(items);
        uses.Clear();
        HasLoadout = true;
    }

    /// <summary>Back to "nothing chosen yet": the Editor hands out every item again. For tests, and for starting over.</summary>
    public static void Clear()
    {
        selected.Clear();
        uses.Clear();
        HasLoadout = false;
    }

    public static bool Has(RescueItemId id)
    {
#if UNITY_EDITOR
        if (!HasLoadout && DevAllItemsWhenEmpty) return true;
#endif
        return selected.Contains(id);
    }

    /// <summary>Called when a mission starts, so "R - try again" does not add to the previous attempt's counts.</summary>
    public static void ResetUses()
    {
        uses.Clear();
    }

    public static void RegisterUse(RescueItemId id)
    {
        uses.TryGetValue(id, out int n);
        uses[id] = n + 1;
    }

    public static int Uses(RescueItemId id)
    {
        uses.TryGetValue(id, out int n);
        return n;
    }
}
