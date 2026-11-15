using UnityEngine;

/// <summary>
/// A different body put on one of the library's characters. The mesh is skinned to that character's own skeleton
/// (the bones are not changed), so every clip, pose, overlay and the waterline work on it exactly as they do on the
/// original person. Built once by Bayanihan > Rebuild Water Rescuer Skin; applied at runtime with ApplyTo.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Character Skin", fileName = "CharacterSkin")]
public class CharacterSkin : ScriptableObject
{
    /// <summary>Where the player's skin lives, under Assets/Resources.</summary>
    public const string ResourcePath = "Characters/WaterRescueResponder";

    [Tooltip("The library character whose skeleton the mesh is bound to.")]
    public string baseCharacterId = "C24";

    [Tooltip("Bone names without the character id prefix (Hips, UpperArmL, Head...). Each weight in the mesh indexes this list.")]
    public string[] boneNames = new string[0];

    [Tooltip("The skinned mesh. Stored inside this asset.")]
    public Mesh mesh;

    [Tooltip("One material per sub-mesh, in the same order.")]
    public Material[] materials = new Material[0];

    public static CharacterSkin Load() => Resources.Load<CharacterSkin>(ResourcePath);

    /// <summary>
    /// Puts this mesh on the character's skinned mesh renderer. Returns false (and says why) if the character is not
    /// the one this skin was made for, or if one of its bones is missing.
    /// </summary>
    public bool ApplyTo(CharacterAnimator anim)
    {
        if (anim == null || anim.Entry == null || anim.Character == null || mesh == null) return false;
        if (anim.Entry.id != baseCharacterId)
        {
            Debug.LogWarning("CharacterSkin: '" + name + "' is made for " + baseCharacterId + ", not " + anim.Entry.id + ".", this);
            return false;
        }

        var renderer = anim.Character.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (renderer == null) return false;

        var bones = new Transform[boneNames.Length];
        for (int i = 0; i < boneNames.Length; i++)
        {
            bones[i] = anim.Bone(boneNames[i]);
            if (bones[i] == null)
            {
                Debug.LogWarning("CharacterSkin: " + anim.Entry.id + " has no bone '" + boneNames[i] + "'.", this);
                return false;
            }
        }

        renderer.sharedMesh = mesh;
        renderer.sharedMaterials = materials;
        renderer.bones = bones;
        renderer.rootBone = anim.Bone("Hips");
        return true;
    }
}
