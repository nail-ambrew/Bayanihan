using UnityEngine;

/// <summary>
/// Where a cutscene puts one of the game's characters (from the character library), in which pose: the cutscene
/// spawns them when it starts, so the scene stores only this marker. The hand and look targets drive the pose
/// overlays (HoldOn reaches both hands to points, HoldTiller puts a hand on the tiller, HeadLook turns the head).
/// </summary>
public class CharacterSpot : MonoBehaviour
{
    [Tooltip("Character id in the library, e.g. C24 (a rescuer) or C01 (a girl).")]
    public string id = "C24";
    public CharacterPose pose = CharacterPose.Idle;
    public PoseOverlay overlays = PoseOverlay.None;
    public Transform leftHand, rightHand, tillerHand, lookAt;
    [Tooltip("Ride the flood's surface (someone in the water), rather than stand where placed.")]
    public bool floatOnWater;
    [Tooltip("When floating: the character's root below the surface (metres, negative).")]
    public float floatOffset = -1.1f;

    public CharacterAnimator Spawned { get; private set; }

    public CharacterAnimator Spawn()
    {
        if (Spawned != null) return Spawned;
        var library = CharacterLibrary.Instance;
        var entry = library != null ? library.Find(id) : null;
        if (entry == null) { Debug.LogWarning("CharacterSpot: no character " + id + " in the library.", this); return null; }
        Spawned = CharacterRig.Spawn(entry, transform, pose);
        if (Spawned == null) return null;
        Spawned.Overlays = overlays;
        if (lookAt != null) { Spawned.Overlays |= PoseOverlay.HeadLook; Spawned.LookTarget = lookAt.position; }
        if (tillerHand != null) Spawned.HandTarget = tillerHand;
        if (leftHand != null) Spawned.LeftHandPoint = leftHand.position;
        if (rightHand != null) Spawned.RightHandPoint = rightHand.position;
        Spawned.animateWithinDistance = 400f;            // the cinematic camera can be far off; keep them alive
        if (floatOnWater && GetComponent<FloatOnWater>() == null) gameObject.AddComponent<FloatOnWater>().offset = floatOffset;
        return Spawned;
    }

    void LateUpdate()
    {
        if (Spawned == null) return;
        if (lookAt != null) Spawned.LookTarget = lookAt.position;
        if (leftHand != null) Spawned.LeftHandPoint = leftHand.position;
        if (rightHand != null) Spawned.RightHandPoint = rightHand.position;
    }
}
