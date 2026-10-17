using UnityEngine;

/// <summary>
/// Puts one character from the 30-person model on this Survivor and keeps them in the right pose:
/// floating if they are in open water, idling if they are stranded on something dry, swimming while they make
/// their way to the boat.
///
/// Setup: add next to the Survivor script. Leave Character Override empty for a random civilian. The model and
/// clips come from the character library (Bayanihan > Rebuild Character Library), so nothing has to be wired up
/// per survivor and swapping the model doesn't invalidate the scene.
/// </summary>
public enum RestingPoseMode { Auto, Floating, Standing }

public enum DriftMode { Auto, Always, Never }

[DisallowMultipleComponent]
public class SurvivorVisual : MonoBehaviour
{
    [Header("Character")]
    [Tooltip("Optional: a short id (C07) or full node name (C07_Teen_NavyHoodie). Empty = random civilian.")]
    public string characterOverride = "";
    [Tooltip("Restrict a random pick to these categories. Empty = any civilian (no rescuers).")]
    public CharacterCategory[] categories;

    [Header("Animation")]
    public Vector2 animationSpeedRange = new Vector2(0.85f, 1.15f);
    [Tooltip("Animation pauses when further than this from the Main Camera (0 = always animate).")]
    public float animateWithinDistance = 80f;

    [Header("Floating on the waves")]
    [Tooltip("Auto = float if this survivor rides the water (Follow Waves), stand if not.")]
    public RestingPoseMode poseOverride = RestingPoseMode.Auto;
    public bool followWaves = true;
    [Tooltip("Fine-tune only. The waterline is worked out from the character's own skeleton (shoulders when " +
             "treading water, the body when swimming), so this should normally stay at 0.")]
    public float waterOffset = 0f;
    [Range(0f, 1f)] public float tiltAmount = 0.6f;
    public bool randomFacing = true;

    [Header("Drifting with the current")]
    [Tooltip("Auto = only people adrift in open water (Need: Lifebuoy) are carried along by the FloodCurrent.")]
    public DriftMode drift = DriftMode.Auto;
    [Tooltip("How much of the water's speed a floating person picks up.")]
    [Range(0f, 1f)] public float driftFactor = 0.6f;
    [Tooltip("They stop this far from where they started - caught on something, or out of the main flow.")]
    public float maxDrift = 25f;

    [Header("Reacting to rescuers")]
    [Tooltip("They start waving once the boat or the swimmer is this close with a clear line of sight.")]
    public float noticeDistance = 35f;

    [Header("Clinging")]
    [Tooltip("Holding on to a pole with both hands, so there is no hand free to wave.")]
    public bool clingToPost;
    [Tooltip("World position of the pole they hold (only the horizontal part matters).")]
    public Vector3 postAxis;

    /// <summary>The character's animator, handed to the boat when this person comes aboard.</summary>
    public CharacterAnimator Animator => anim;

    /// <summary>How fast the flood is carrying this person right now (zero when they are not adrift).</summary>
    public Vector3 DriftVelocity { get; private set; }

    /// <summary>Which of the 30 people this survivor is. The boat reuses it so the same person comes aboard.</summary>
    public CharacterEntry Character { get; private set; }

    Survivor survivor;
    CharacterAnimator anim;
    float yaw;
    Vector3 driftAnchor;
    SimpleBoat boat;
    PlayerRescuer player;
    float nextLookAt;
    bool seesRescuer;

    bool Drifts
    {
        get
        {
            if (survivor != null && survivor.IsHeld) return false;     // on a line to the boat, or in tow
            if (drift == DriftMode.Never || !followWaves) return false;
            if (drift == DriftMode.Always) return true;
            return survivor != null && survivor.needs.HasFlag(Need.Lifebuoy);
        }
    }

    void Awake()
    {
        survivor = GetComponent<Survivor>();
        driftAnchor = transform.position;
        yaw = randomFacing ? Random.Range(0f, 360f) : transform.eulerAngles.y;

        var library = CharacterLibrary.Instance;
        if (library == null) { enabled = false; return; }

        Character = library.Find(characterOverride);
        if (Character == null)
        {
            if (!string.IsNullOrEmpty(characterOverride))
                Debug.LogWarning("SurvivorVisual: no character '" + characterOverride +
                                 "' in the library - picking at random.", this);
            Character = categories != null && categories.Length > 0
                ? library.RandomOf(categories)
                : library.RandomCivilian();
        }

        if (Character == null)
        {
            Debug.LogWarning("SurvivorVisual: the character library is empty. " +
                             "Run Bayanihan > Rebuild Character Library.", this);
            enabled = false;
            return;
        }

        anim = CharacterRig.Spawn(Character, transform, RestingPose());
        if (anim == null) { enabled = false; return; }

        anim.speedRange = animationSpeedRange;
        anim.animateWithinDistance = animateWithinDistance;
        anim.Play(RestingPose());

        // Somebody hurt does not wave and shout: they hang in the water, barely moving.
        if (survivor != null && survivor.needs.HasFlag(Need.FirstAid))
        {
            anim.Overlays |= PoseOverlay.Limp;
            anim.SpeedScale = 0.55f;
        }
        ApplyWaterline();
    }

    void Start()
    {
        boat = FindFirstObjectByType<SimpleBoat>();
        player = FindFirstObjectByType<PlayerRescuer>();
    }

    /// <summary>Called by Survivor as they are taken aboard: the boat looks after the character from here on.</summary>
    public CharacterAnimator Release()
    {
        var a = anim;
        if (a != null) a.Overlays &= ~(PoseOverlay.WaveForHelp | PoseOverlay.HeadLook | PoseOverlay.HoldOn);
        anim = null;
        enabled = false;
        return a;
    }

    /// <summary>Sinks the body so the water comes up to the right part of it for the pose it is in.</summary>
    public void ApplyWaterline()
    {
        if (anim == null) return;
        float sink = Floating ? anim.Waterline : 0f;
        anim.transform.localPosition = new Vector3(0f, -sink, 0f);
    }

    bool Floating => followWaves || RestingPose() == CharacterPose.Float;

    /// <summary>
    /// In the water they float; on a roof or high ground they stand and wait.
    /// "Follow Waves" is the per-survivor flag that already means "this person is in the water", so it decides -
    /// keying off Need.Lifebuoy alone left anyone flagged Rope/FirstAid standing upright in the sea.
    /// </summary>
    CharacterPose RestingPose()
    {
        if (poseOverride != RestingPoseMode.Auto)
            return poseOverride == RestingPoseMode.Floating ? CharacterPose.Float : CharacterPose.Idle;
        if (followWaves) return CharacterPose.Float;
        return survivor != null && survivor.needs.HasFlag(Need.Lifebuoy) ? CharacterPose.Float : CharacterPose.Idle;
    }

    /// <summary>Called by Survivor when they set off for the boat.</summary>
    public void SetPose(CharacterPose pose) => anim?.Play(pose);

    /// <summary>Roughly where their head is - just clear of the water if they are floating, head height if they are
    /// standing on something. It is what a lookout would actually spot, so sight lines are aimed here.</summary>
    public Vector3 SightPoint
    {
        get
        {
            var head = anim != null ? anim.Bone("Head") : null;
            if (head != null) return head.position + Vector3.up * 0.1f;

            Vector3 p = transform.position;
            if (followWaves && WaterSurface.Instance != null) p.y = WaterSurface.Instance.GetHeight(p) + 0.3f;
            else p.y += 1.2f;
            return p;
        }
    }

    void Update()
    {
        FollowWaves();
        ApplyWaterline();
        HoldOnToPost();
        ReactToRescuers();
    }

    /// <summary>
    /// Both hands on the pole, a little apart, at the height of somebody treading water who has got a grip. The
    /// points are re-worked every frame, so as the water rises they climb the pole.
    /// </summary>
    void HoldOnToPost()
    {
        if (anim == null || !clingToPost) return;
        if (survivor != null && (survivor.IsRescued || survivor.IsHeld)) return;   // the line has them now

        float waterY = WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(postAxis) : transform.position.y;
        Vector3 toPost = postAxis - transform.position;
        toPost.y = 0f;
        Vector3 across = toPost.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, toPost).normalized : transform.right;

        anim.LeftHandPoint = new Vector3(postAxis.x, waterY + 0.55f, postAxis.z) - across * 0.11f;
        anim.RightHandPoint = new Vector3(postAxis.x, waterY + 0.85f, postAxis.z) + across * 0.11f;
        anim.Overlays |= PoseOverlay.HoldOn;
    }

    /// <summary>
    /// People who can see help coming signal for it. It is the most useful cue a search crew gets, and it is
    /// what makes somebody in the water read as a person rather than as scenery.
    /// </summary>
    void ReactToRescuers()
    {
        if (anim == null || (survivor != null && survivor.IsRescued)) return;

        if (Time.time >= nextLookAt)
        {
            nextLookAt = Time.time + 0.3f + Random.value * 0.2f;
            seesRescuer = false;

            Vector3 eyes = SightPoint;
            Vector3 rescuer = RescuerPosition();
            if ((rescuer - eyes).sqrMagnitude <= noticeDistance * noticeDistance && LineOfSight.Clear(eyes, rescuer))
            {
                seesRescuer = true;
                anim.LookTarget = rescuer;
            }
        }

        // Hurt people hang in the water barely moving, and somebody clinging to a pole has no hand to spare.
        bool hurt = (anim.Overlays & PoseOverlay.Limp) != 0;
        var signalling = PoseOverlay.HeadLook | (hurt || clingToPost ? PoseOverlay.None : PoseOverlay.WaveForHelp);
        if (seesRescuer) anim.Overlays |= signalling;
        else anim.Overlays &= ~(PoseOverlay.WaveForHelp | PoseOverlay.HeadLook);
    }

    /// <summary>Where help is: the swimmer if the player is in the water, otherwise the boat.</summary>
    Vector3 RescuerPosition()
    {
        if (player != null && !player.IsMounted) return player.transform.position + Vector3.up * 0.3f;
        if (boat != null) return boat.transform.position + Vector3.up * 1.2f;
        return transform.position;
    }

    // ------------------------------------------------------------------ being brought in
    /// <summary>Both hands on a ring or a rope. Whatever has hold of them works out where the points are.</summary>
    public void HoldOn(Vector3 left, Vector3 right)
    {
        if (anim == null) return;
        anim.LeftHandPoint = left;
        anim.RightHandPoint = right;
        anim.Overlays |= PoseOverlay.HoldOn;
    }

    /// <summary>They have let go (or been let go of). A clinger goes back to the pole on their own.</summary>
    public void LetGo()
    {
        if (anim != null) anim.Overlays &= ~PoseOverlay.HoldOn;
    }

    /// <summary>Turns them, gradually, to face a point: towards whoever is bringing them in.</summary>
    public void FaceTowards(Vector3 world, float degreesPerSecond = 160f)
    {
        Vector3 to = world - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;

        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        if (anim != null && anim.ForwardSign() < 0f) want += 180f;
        yaw = Mathf.MoveTowardsAngle(yaw, want, degreesPerSecond * Time.deltaTime);
        if (!followWaves) transform.rotation = Quaternion.Euler(0f, yaw, 0f);   // nothing else turns somebody on a roof
    }

    /// <summary>No longer holding the pole: adrift, like anybody else in the water.</summary>
    public void BecomeAdrift()
    {
        clingToPost = false;
        drift = DriftMode.Always;
        driftAnchor = transform.position;       // the leash is measured from here now
    }

    /// <summary>Somebody lowered off a roof ends up in the water beside the boat, like everybody else the boat picks up.</summary>
    public void EnterWater()
    {
        followWaves = true;
        drift = DriftMode.Never;
        poseOverride = RestingPoseMode.Floating;
        if (anim != null) anim.Play(CharacterPose.Float);
        ApplyWaterline();
    }

    void FollowWaves()
    {
        DriftVelocity = Vector3.zero;
        if (!followWaves || WaterSurface.Instance == null) return;
        if (survivor != null && survivor.IsRescued) return;   // Survivor drives the hop to the boat

        var water = WaterSurface.Instance;
        Vector3 p = transform.position;

        // Somebody adrift goes where the water goes, until they fetch up against something.
        if (Drifts)
        {
            Vector3 flow = FloodCurrent.Sample(p) * driftFactor;
            Vector3 next = p + flow * Time.deltaTime;
            Vector3 fromAnchor = next - driftAnchor;
            fromAnchor.y = 0f;
            if (fromAnchor.sqrMagnitude <= maxDrift * maxDrift)
            {
                p = next;
                DriftVelocity = flow;
            }
        }

        p.y = water.GetHeight(p) + waterOffset;
        transform.position = p;

        // Lean with the wave slope
        const float d = 0.6f;
        float dx = water.GetHeight(p + Vector3.right * d) - water.GetHeight(p - Vector3.right * d);
        float dz = water.GetHeight(p + Vector3.forward * d) - water.GetHeight(p - Vector3.forward * d);
        Vector3 normal = new Vector3(-dx, 2f * d, -dz).normalized;
        normal = Vector3.Slerp(Vector3.up, normal, tiltAmount);

        Quaternion target = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
    }
}
