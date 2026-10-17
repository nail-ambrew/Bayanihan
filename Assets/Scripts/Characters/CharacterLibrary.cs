using System.Collections.Generic;
using UnityEngine;

/// <summary>Which row of the character model a person came from. Doubles as the "who is this" tag.</summary>
public enum CharacterCategory { Kid, Teen, Adult, Rescuer, Elder }

/// <summary>The four animation states every character in the model ships with.</summary>
public enum CharacterPose { Idle, Walk, Float, Swim }

/// <summary>One of the 30 people in filipino_characters_30.glb, with their four clips.</summary>
[System.Serializable]
public class CharacterEntry
{
    [Tooltip("Short id, e.g. C07.")]
    public string id;
    [Tooltip("Exact node name in the model, e.g. C07_Teen_NavyHoodie.")]
    public string nodeName;
    public CharacterCategory category;

    [Tooltip("The path the clips use to address this character's hip bone, from the animation root. " +
             "Filled in by Bayanihan > Rebuild Character Library; it's what pins down which object to sample on.")]
    public string hipsBindingPath;

    public AnimationClip idle;
    public AnimationClip walk;
    public AnimationClip floating;
    public AnimationClip swim;

    public AnimationClip Clip(CharacterPose pose)
    {
        switch (pose)
        {
            case CharacterPose.Walk:  return walk;
            case CharacterPose.Float: return floating;
            case CharacterPose.Swim:  return swim;
            default:                  return idle;
        }
    }

    /// <summary>Bone names are the id plus a fixed suffix: C07_Hips, C07_ThighL, C07_FootL...</summary>
    public string Bone(string suffix) => id + "_" + suffix;

    public bool IsComplete => !string.IsNullOrEmpty(nodeName) && idle != null;
}

/// <summary>
/// Catalogue of the people in the character model and their clips, so runtime code never has to hunt through
/// 120 animation sub-assets by name. Rebuild it from the menu: Bayanihan > Rebuild Character Library.
///
/// Lives in Assets/Resources so nothing in a scene has to hold a reference to it - which also means swapping
/// the character model does not invalidate every serialized field in every scene.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Character Library", fileName = CharacterLibrary.ResourceName)]
public class CharacterLibrary : ScriptableObject
{
    public const string ResourceName = "CharacterLibrary";

    [Tooltip("The imported character model (filipino_characters_30.glb). Set by the rebuild menu item.")]
    public GameObject sourceModel;
    [Tooltip("Nodes with these names are scenery in the model and are stripped from every spawned character.")]
    public string[] stripNodeNames = { "StudioGround" };
    [Tooltip("Which way the characters face along their own Z axis: 1 = +Z, -1 = -Z. " +
             "Flip this once if everybody faces backwards - it applies to the whole model.")]
    public float forwardSign = 1f;

    public List<CharacterEntry> characters = new List<CharacterEntry>();

    static CharacterLibrary cached;
    static bool searched;

    public static CharacterLibrary Instance
    {
        get
        {
            if (cached == null && !searched)
            {
                searched = true;
                cached = Resources.Load<CharacterLibrary>(ResourceName);
                if (cached == null)
                    Debug.LogWarning($"CharacterLibrary: no Resources/{ResourceName}.asset found. " +
                                     "Run Bayanihan > Rebuild Character Library.");
            }
            return cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; searched = false; }

    /// <summary>Accepts a full node name ("C07_Teen_NavyHoodie") or a short id ("C07"). Null if unknown.</summary>
    public CharacterEntry Find(string idOrNodeName)
    {
        if (string.IsNullOrEmpty(idOrNodeName)) return null;
        foreach (var c in characters)
            if (c.nodeName == idOrNodeName || c.id == idOrNodeName) return c;
        return null;
    }

    public CharacterEntry RandomOf(params CharacterCategory[] categories)
    {
        var pool = new List<CharacterEntry>();
        foreach (var c in characters)
        {
            if (!c.IsComplete) continue;
            if (categories == null || categories.Length == 0) { pool.Add(c); continue; }
            foreach (var cat in categories)
                if (c.category == cat) { pool.Add(c); break; }
        }
        if (pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)];
    }

    /// <summary>The people a flood victim can be: everyone except the rescue crew.</summary>
    public CharacterEntry RandomCivilian() =>
        RandomOf(CharacterCategory.Kid, CharacterCategory.Teen, CharacterCategory.Adult, CharacterCategory.Elder);

    public CharacterEntry RandomRescuer() => RandomOf(CharacterCategory.Rescuer);
}
