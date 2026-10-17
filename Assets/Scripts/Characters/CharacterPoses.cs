using UnityEngine;

/// <summary>Poses that can be layered over whatever clip a character is playing.</summary>
[System.Flags]
public enum PoseOverlay
{
    None = 0,
    Seated = 1,         // legs bent to sit; the upper body keeps its idle motion
    WaveForHelp = 2,    // right arm up and waving
    HoldTiller = 4,     // right hand on the outboard's tiller (CharacterAnimator.HandTarget)
    Limp = 8,           // head and shoulders slumped: hurt or exhausted
    HeadLook = 16,      // head turns towards CharacterAnimator.LookTarget
    HoldOn = 32         // both hands on a pole, a ring or a line (CharacterAnimator.LeftHandPoint / RightHandPoint)
}

/// <summary>
/// The model ships with four clips - idle, walk, float, swim - and a rescue needs more than that: people sit in
/// the boat, wave at it, slump when they are hurt, steer it. These are built by turning the model's own bones
/// after each clip sample, so no new animation assets are needed and they blend with whatever is playing.
///
/// The rig rests with every bone at identity: spine up +Y, arms out along X (right arm +X), legs down -Y.
/// So a rotation about X swings a limb forwards/backwards, and about Z raises or lowers an arm.
/// </summary>
public static class CharacterPoses
{
    public static void Apply(CharacterAnimator a)
    {
        var overlays = a.Overlays;
        if (overlays == PoseOverlay.None) return;

        float facing = a.ForwardSign();

        if ((overlays & PoseOverlay.Seated) != 0) Seated(a, facing);
        if ((overlays & PoseOverlay.Limp) != 0) Limp(a, facing);
        if ((overlays & PoseOverlay.WaveForHelp) != 0) Wave(a);
        if ((overlays & PoseOverlay.HoldTiller) != 0) Tiller(a, facing);
        if ((overlays & PoseOverlay.HoldOn) != 0) HoldOn(a, facing);
        if ((overlays & PoseOverlay.HeadLook) != 0) HeadLook(a, facing);
    }

    /// <summary>Both hands on whatever they are holding. Applied after Limp, so a hurt person still keeps hold.</summary>
    static void HoldOn(CharacterAnimator a, float facing)
    {
        var upperR = a.Bone("UpperArmR"); var foreR = a.Bone("ForearmR"); var handR = a.Bone("HandR");
        var upperL = a.Bone("UpperArmL"); var foreL = a.Bone("ForearmL"); var handL = a.Bone("HandL");

        Vector3 right = a.transform.right * facing;
        if (upperR != null && foreR != null && handR != null)
            Reach(upperR, foreR, handR, a.RightHandPoint, Vector3.down + right * 0.35f);
        if (upperL != null && foreL != null && handL != null)
            Reach(upperL, foreL, handL, a.LeftHandPoint, Vector3.down - right * 0.35f);
    }

    /// <summary>Thighs forward, then knees back down so the shins hang instead of sticking out.</summary>
    static void Seated(CharacterAnimator a, float facing)
    {
        var thigh = Quaternion.Euler(-a.SeatedThighBend * facing, 0f, 0f);
        var shin = Quaternion.Euler(a.SeatedShinBend * facing, 0f, 0f);
        Turn(a.Bone("ThighL"), thigh);
        Turn(a.Bone("ThighR"), thigh);
        Turn(a.Bone("ShinL"), shin);
        Turn(a.Bone("ShinR"), shin);
    }

    /// <summary>Head down, shoulders rounded.</summary>
    static void Limp(CharacterAnimator a, float facing)
    {
        Turn(a.Bone("Spine"), Quaternion.Euler(8f * facing, 0f, 0f));
        Turn(a.Bone("Neck"), Quaternion.Euler(18f * facing, 0f, 0f));
        Turn(a.Bone("Head"), Quaternion.Euler(14f * facing, 0f, 6f));
    }

    /// <summary>Right arm straight up, waving from the elbow. Replaces whatever the clip had that arm doing.</summary>
    static void Wave(CharacterAnimator a)
    {
        float t = Time.time * 7f + a.GetInstanceID() * 0.37f;      // everybody waves out of step
        Set(a.Bone("UpperArmR"), Quaternion.Euler(0f, 0f, 72f + Mathf.Sin(t) * 10f));
        Set(a.Bone("ForearmR"), Quaternion.Euler(0f, 0f, 22f + Mathf.Sin(t + 0.6f) * 28f));
    }

    /// <summary>
    /// Right hand on the tiller. The arm is solved to reach the grip wherever the helmsman happens to sit, so the
    /// hand follows the tiller as it swings with the steering. With no tiller to hold, the arm just hangs back by
    /// the hip.
    /// </summary>
    static void Tiller(CharacterAnimator a, float facing)
    {
        var upper = a.Bone("UpperArmR");
        var fore = a.Bone("ForearmR");
        var hand = a.Bone("HandR");

        if (a.HandTarget != null && upper != null && fore != null && hand != null)
        {
            // The elbow hangs down and a little out, the way it does with a hand resting on a tiller.
            Vector3 elbowHint = Vector3.down + a.transform.right * (0.35f * facing);
            Reach(upper, fore, hand, a.HandTarget.position, elbowHint);
            return;
        }

        Set(upper, Quaternion.Euler(0f, 55f * facing, -48f));
        Set(fore, Quaternion.Euler(0f, 18f * facing, 0f));
    }

    /// <summary>
    /// Two-bone reach: bends the arm so the hand lands on <paramref name="target"/>, or as near as the arm can get,
    /// with the elbow pushed towards <paramref name="elbowHint"/>.
    /// </summary>
    static void Reach(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 elbowHint)
    {
        Vector3 shoulder = upper.position, elbow = fore.position, wrist = hand.position;
        float a = (elbow - shoulder).magnitude;      // upper arm
        float b = (wrist - elbow).magnitude;         // forearm
        Vector3 toTarget = target - shoulder;
        if (a < 1e-4f || b < 1e-4f || toTarget.sqrMagnitude < 1e-6f) return;

        // Out of reach is a straight arm pointing at the target; closer than the bend allows is a folded one.
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.005f);
        Vector3 along = toTarget.normalized;

        // Law of cosines: how far along the shoulder-to-target line the elbow sits, and how far off it.
        float x = (a * a - b * b + d * d) / (2f * d);
        float y = Mathf.Sqrt(Mathf.Max(a * a - x * x, 0f));
        Vector3 bend = Vector3.ProjectOnPlane(elbowHint, along);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, along);
        bend.Normalize();

        Vector3 newElbow = shoulder + along * x + bend * y;
        Vector3 newWrist = shoulder + along * d;

        upper.rotation = Quaternion.FromToRotation(elbow - shoulder, newElbow - shoulder) * upper.rotation;
        fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, newWrist - fore.position) * fore.rotation;
    }

    /// <summary>Turns the head to face a point, as far as a neck comfortably goes.</summary>
    static void HeadLook(CharacterAnimator a, float facing)
    {
        Vector3 local = a.transform.InverseTransformPoint(a.LookTarget);
        float yaw = Mathf.Atan2(local.x * facing, local.z * facing) * Mathf.Rad2Deg;
        yaw = Mathf.Clamp(yaw, -70f, 70f);
        var half = Quaternion.Euler(0f, yaw * 0.5f, 0f);
        Turn(a.Bone("Neck"), half);
        Turn(a.Bone("Head"), half);
    }

    static void Turn(Transform bone, Quaternion by)
    {
        if (bone != null) bone.localRotation = bone.localRotation * by;
    }

    static void Set(Transform bone, Quaternion to)
    {
        if (bone != null) bone.localRotation = to;
    }
}
