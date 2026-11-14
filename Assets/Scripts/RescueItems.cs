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
    public string name;          // the name on the card and in prompts (English, as in the Figma loadout)
    public string filipino;      // small subtitle: what the barangay calls it
    public string description;   // one line on the card: what it does in the game
    public ItemUse use;
    public string hotkey;
    public string button;        // the on-screen button that uses it, for active items
    public float cooldown;
    public bool recommended;
    public Color color;          // placeholder icon colour (used when no sprite is assigned)
    public string glyph;         // placeholder icon letter

    /// <summary>How it is used: "Passive", "Toggle · F" or "20s cooldown · Q" (or the button's name on a phone).</summary>
    public string BadgeText
    {
        get
        {
            string key = InputHelper.Key(hotkey, button);
            switch (use)
            {
                case ItemUse.Toggle:   return $"Toggle · {key}";
                case ItemUse.Cooldown: return $"{cooldown:0}s cooldown · {key}";
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
            id = RescueItemId.Lifebuoy, name = "Floatie", filipino = "Salbabida",
            description = "Keeps people safe in high water. Throw it to pull them out of open water.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.90f, 0.30f, 0.22f), glyph = "O"
        },
        new RescueItemDef
        {
            id = RescueItemId.FirstAid, name = "First-Aid Kit", filipino = "Botika",
            description = "Treats the injured so they can board. Hold to treat before lifting them in.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.86f, 0.25f, 0.30f), glyph = "+"
        },
        new RescueItemDef
        {
            id = RescueItemId.Flashlight, name = "Flashlight", filipino = "Flashlight",
            description = "Provides light, so you spot people further away, especially at night.",
            use = ItemUse.Toggle, hotkey = "F", button = "LIGHT", color = new Color(0.95f, 0.75f, 0.20f), glyph = "F"
        },
        new RescueItemDef
        {
            id = RescueItemId.Rope, name = "Rope", filipino = "Lubid",
            description = "Hauls residents off roofs and out from behind debris, and tethers your swimmer.",
            use = ItemUse.Passive, recommended = true, color = new Color(0.80f, 0.55f, 0.20f), glyph = "R"
        },
        new RescueItemDef
        {
            id = RescueItemId.Radio, name = "Radio", filipino = "Radyo",
            description = "Keeps you connected: finds the nearest missing resident and calls to cut live wires.",
            use = ItemUse.Cooldown, hotkey = "Q", button = "RADIO", cooldown = 20f, color = new Color(0.30f, 0.35f, 0.45f), glyph = "Q"
        },
        new RescueItemDef
        {
            id = RescueItemId.Raincoat, name = "Raincoat", filipino = "Kapote",
            description = "Cuts the rain visibility penalty for everyone on the boat.",
            use = ItemUse.Passive, color = new Color(0.98f, 0.65f, 0.10f), glyph = "K"
        },
    };

    /// <summary>The order the Figma loadout lays the cards out in: alphabetical, two rows of three.</summary>
    public static readonly RescueItemId[] DisplayOrder =
    {
        RescueItemId.FirstAid, RescueItemId.Flashlight, RescueItemId.Lifebuoy,
        RescueItemId.Radio, RescueItemId.Raincoat, RescueItemId.Rope
    };

    /// <summary>The "Recommended pack" button.</summary>
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
