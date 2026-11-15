using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays one of a character's four clips by sampling it directly, so it works no matter how the model was
/// imported (Animator, legacy Animation, controller or none). Added by CharacterRig.Spawn.
///
/// On top of the sampled clip it does three things the clips cannot:
/// - cross-fades from the old pose into the new clip, so changing state never snaps the body;
/// - applies pose overlays (seated legs, a raised arm waving for help...) after every sample - see CharacterPoses;
/// - reports where the waterline belongs for the current pose, so somebody treading water is wet to the
///   shoulders and somebody swimming lies along the surface, whatever their height.
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    [Tooltip("Each person gets a random speed in this range and a random start time so nobody moves in sync.")]
    public Vector2 speedRange = new Vector2(0.9f, 1.1f);
    [Tooltip("Stop sampling when further than this from the Main Camera (0 = always animate).")]
    public float animateWithinDistance = 80f;
    [Tooltip("Seconds a change of clip takes to blend in.")]
    public float crossFade = 0.25f;

    public CharacterEntry Entry { get; private set; }
    public CharacterPose Pose { get; private set; }
    /// <summary>The character node itself (C07_Teen_NavyHoodie), for bone lookups and facing.</summary>
    public Transform Character { get; private set; }
    /// <summary>False freezes the pose where it is.</summary>
    public bool Playing { get; set; } = true;

    /// <summary>Poses layered over the clip after every sample. Combine freely.</summary>
    public PoseOverlay Overlays { get; set; }
    /// <summary>World point the head turns towards while the HeadLook overlay is on.</summary>
    public Vector3 LookTarget { get; set; }
    /// <summary>Where the right hand goes while the HoldTiller overlay is on (the tiller's grip). Null = a relaxed arm.</summary>
    public Transform HandTarget { get; set; }
    /// <summary>World points the two hands go to while the HoldOn overlay is on: a pole, a ring, a rope.</summary>
    public Vector3 LeftHandPoint { get; set; }
    public Vector3 RightHandPoint { get; set; }
    /// <summary>Seated overlay: how far the thighs swing up, and the knees bend back, in degrees.</summary>
    public float SeatedThighBend { get; set; } = 85f;
    public float SeatedShinBend { get; set; } = 80f;
    /// <summary>Multiplies playback speed (an exhausted or injured person moves slowly).</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>
    /// Hides the head (the Head bone is shrunk to nothing) so a first-person camera set at the face does not look out
    /// through the person's own helmet. Only the head moves: the body, arms and the rest of the pose are untouched.
    /// </summary>
    public bool HeadHidden { get; private set; }

    /// <summary>
    /// Height above this object's origin where the water surface belongs for the current pose, eased so that it
    /// follows a cross-fade. Whoever floats this character lowers it by this much.
    /// </summary>
    public float Waterline { get; private set; }

    Transform animRoot;
    AnimationClip clip;
    float time, speed = 1f;
    Camera cam;

    Transform[] bones;
    Vector3 headScale = Vector3.one;
    Vector3[] fadeFromPosition;
    Quaternion[] fadeFromRotation;
    float fadeLeft, fadeLength;

    readonly Dictionary<string, Transform> boneCache = new Dictionary<string, Transform>();
    readonly float[] waterlineForPose = new float[4];
    readonly bool[] waterlineKnown = new bool[4];
    float waterlineTarget;

    public void Bind(CharacterEntry entry, Transform animationRoot)
    {
        Entry = entry;
        animRoot = animationRoot;
        Character = CharacterRig.FindDeep(transform, entry.nodeName);
        speed = Random.Range(speedRange.x, speedRange.y);

        // Every joint under the character, but not the skinned mesh node: these are what a clip moves.
        var list = new List<Transform>();
        if (Character != null)
        {
            foreach (var t in Character.GetComponentsInChildren<Transform>(true))
                if (t != Character && t.GetComponent<SkinnedMeshRenderer>() == null) list.Add(t);
        }
        bones = list.ToArray();
        var head = Bone("Head");
        if (head != null) headScale = head.localScale;
        fadeFromPosition = new Vector3[bones.Length];
        fadeFromRotation = new Quaternion[bones.Length];
    }

    /// <summary>Switch state. Keeps a random phase so a crowd in the same pose doesn't move as one.</summary>
    public void Play(CharacterPose pose)
    {
        Pose = pose;
        var next = Entry != null ? Entry.Clip(pose) : null;
        if (next == clip) { Playing = true; return; }

        bool hadPose = clip != null;
        if (hadPose) BeginFade();

        clip = next;
        time = clip != null ? Random.value * clip.length : 0f;
        Playing = true;

        MeasureWaterline(pose);
        waterlineTarget = waterlineForPose[(int)pose];
        if (!hadPose) Waterline = waterlineTarget;      // first pose: nothing to ease from

        Refresh();
    }

    /// <summary>Pose once and stop, leaving the skeleton where it lands.</summary>
    public void Freeze(CharacterPose pose, float atTime = 0f)
    {
        Pose = pose;
        clip = Entry != null ? Entry.Clip(pose) : null;
        time = atTime;
        fadeLeft = 0f;
        Refresh();
        Playing = false;
    }

    /// <summary>Re-pose right now (sample, blend, overlays) without waiting for the next Update.</summary>
    public void Refresh()
    {
        Sample();
        ApplyFade(0f);
        CharacterPoses.Apply(this);
        ApplyHeadVisibility();
    }

    /// <summary>Hides or shows the head now. See HeadHidden.</summary>
    public void SetHeadHidden(bool hidden)
    {
        HeadHidden = hidden;
        ApplyHeadVisibility();
    }

    void ApplyHeadVisibility()
    {
        var head = Bone("Head");
        if (head == null) return;
        head.localScale = HeadHidden ? Vector3.zero : headScale;
    }

    public Transform Bone(string suffix)
    {
        if (Entry == null) return null;
        if (boneCache.TryGetValue(suffix, out var cached) && cached != null) return cached;

        var found = CharacterRig.FindDeep(Character != null ? Character : transform, Entry.Bone(suffix));
        if (found != null) boneCache[suffix] = found;
        return found;
    }

    /// <summary>+1 or -1: which way this person faces along their local Z. Set model-wide on the library -
    /// the old trick of reading it off a foot bone is unreliable (a heel offset looks like a backwards foot).</summary>
    public float ForwardSign()
    {
        var library = CharacterLibrary.Instance;
        return library != null && library.forwardSign < 0f ? -1f : 1f;
    }

    void Update()
    {
        Waterline = Mathf.MoveTowards(Waterline, waterlineTarget, 1.5f * Time.deltaTime);

        if (!Playing || clip == null || clip.length <= 0f) return;

        if (animateWithinDistance > 0f)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null &&
                (cam.transform.position - transform.position).sqrMagnitude > animateWithinDistance * animateWithinDistance)
                return;
        }

        time += Time.deltaTime * speed * SpeedScale;
        Sample();
        ApplyFade(Time.deltaTime);
        CharacterPoses.Apply(this);     // only ever straight after a sample, so overlays never pile up
        ApplyHeadVisibility();
    }

    void Sample()
    {
        if (clip == null || animRoot == null || clip.length <= 0f) return;
        clip.SampleAnimation(animRoot.gameObject, time % clip.length);
    }

    // ------------------------------------------------------------------ cross-fade
    void BeginFade()
    {
        if (crossFade <= 0f || bones == null) return;
        for (int i = 0; i < bones.Length; i++)
        {
            fadeFromPosition[i] = bones[i].localPosition;
            fadeFromRotation[i] = bones[i].localRotation;
        }
        fadeLength = fadeLeft = crossFade;
    }

    /// <summary>Pulls the freshly sampled pose back towards the pose the body was in when the clip changed.</summary>
    void ApplyFade(float dt)
    {
        if (fadeLeft <= 0f) return;
        fadeLeft -= dt;
        if (fadeLeft <= 0f) return;

        float old = Mathf.SmoothStep(0f, 1f, fadeLeft / fadeLength);     // weight of the pose being left
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i].localPosition = Vector3.Lerp(bones[i].localPosition, fadeFromPosition[i], old);
            bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, fadeFromRotation[i], old);
        }
    }

    // ------------------------------------------------------------------ waterline
    /// <summary>
    /// Works out, once per pose, how high up the body the water should come: the chest for somebody upright in
    /// the water, the hips for somebody lying along it. Measured off the skeleton, so a child and a tall adult
    /// both end up wet to the same part of the body.
    /// </summary>
    void MeasureWaterline(CharacterPose pose)
    {
        int i = (int)pose;
        if (waterlineKnown[i]) return;
        waterlineKnown[i] = true;
        waterlineForPose[i] = 0f;

        if (pose != CharacterPose.Float && pose != CharacterPose.Swim) return;
        var poseClip = Entry != null ? Entry.Clip(pose) : null;
        var joint = Bone(pose == CharacterPose.Swim ? "Hips" : "Chest");
        if (poseClip == null || joint == null || animRoot == null) return;

        poseClip.SampleAnimation(animRoot.gameObject, 0f);
        waterlineForPose[i] = transform.InverseTransformPoint(joint.position).y + (pose == CharacterPose.Swim ? 0.05f : 0.08f);
    }
}
