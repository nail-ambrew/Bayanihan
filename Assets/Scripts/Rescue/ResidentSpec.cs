using System;
using UnityEngine;

/// <summary>
/// One resident on the mission's roster: who they are, what state they are in, and where they wait. The scene only
/// supplies a place (an anchor object with this name); everything else comes from here, so the briefing and the
/// level cannot disagree about who is out there.
/// </summary>
[Serializable]
public class ResidentSpec
{
    [Tooltip("Short key, unique in the mission: joy, cesar...")]
    public string id;
    [Tooltip("As the barangay would write it on the list.")]
    public string displayName;
    [Tooltip("Family name. Residents who share one are a household, and getting them all to the hall earns a bonus.")]
    public string household;
    public CharacterCategory category = CharacterCategory.Adult;
    [Tooltip("Optional: a particular person from the character library (C26...). Empty = a random one of the category.")]
    public string characterId;
    public Situation situation = Situation.OpenWater;
    [Tooltip("What has to be packed to bring them aboard. Unchanged from before: Salbabida, Lubid, Botika.")]
    public Need needs = Need.None;
    [Range(0f, 1f)] public float startCondition = 1f;
    [Tooltip("Name of the object in the Gameplay scene that marks where they wait (and which way they face).")]
    public string anchor;
    [Tooltip("A line for the briefing, if there is something worth telling the crew about this resident.")]
    [TextArea(1, 3)] public string note;

    public bool Injured => (needs & Need.FirstAid) != 0;

    /// <summary>The first word of the name: how the crew would call out to them.</summary>
    public string CallName
    {
        get
        {
            if (string.IsNullOrEmpty(displayName)) return id;
            int space = displayName.IndexOf(' ');
            return space > 0 ? displayName.Substring(0, space) : displayName;
        }
    }
}
