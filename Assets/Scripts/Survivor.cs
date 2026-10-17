using System.Collections;
using UnityEngine;

/// <summary>
/// A resident waiting to be rescued.
/// Needs a Collider set to "Is Trigger" - this is the pick-up range around the survivor.
/// "Needs" decides which packed items are required to pull them aboard.
/// When rescued, the survivor swims to the side of the boat and is hauled aboard into a seat.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Survivor : MonoBehaviour
{
    [Header("Who")]
    [Tooltip("As the barangay's list has them. Empty = the object's name.")]
    public string residentName = "";
    [Tooltip("Family name. Residents who share one are a household, and getting all of them to the hall earns a bonus.")]
    public string household = "";

    [Header("Where they are")]
    [Tooltip("Open water and clinging can be reached by the boat or by a thrown line. A rooftop, or behind debris, " +
             "can only be reached by a line (or a swimmer).")]
    public Situation situation = Situation.OpenWater;

    [Header("Rescue requirements")]
    [Tooltip("Lifebuoy = floating in open water, Rope = stuck behind debris, FirstAid = injured. Tick several if it applies.")]
    public Need needs = Need.None;

    [Header("Coming aboard")]
    [Tooltip("How fast they cover the last stretch of water to the side of the boat (m/s).")]
    public float swimToBoatSpeed = 2.2f;
    [Tooltip("Seconds it takes the crew to haul them up over the tube and into a seat.")]
    public float haulDuration = 0.9f;
    [Tooltip("How far they are lifted to clear the tube (metres).")]
    public float haulHeight = 0.45f;

    public bool IsRescued { get; private set; }

    /// <summary>Set once the boat's crew have actually seen them: close enough, with nothing in the way.
    /// The radio points at whoever has not been found yet.</summary>
    public bool Discovered
    {
        get => discovered;
        set
        {
            if (value && !discovered) DiscoveredAt = Time.time;
            discovered = value;
        }
    }
    bool discovered;

    /// <summary>When they were found (game time), or -1.</summary>
    public float DiscoveredAt { get; private set; } = -1f;

    /// <summary>The crew can see them right now: within assessment range with nothing in the way. Their tag and
    /// condition are only shown live while this is true.</summary>
    public bool InView { get; set; }

    /// <summary>Hurt to begin with: needs the Botika.</summary>
    public bool Injured => (needs & Need.FirstAid) != 0;

    /// <summary>Standing in water with a live wire in it (set by LiveWireZone).</summary>
    public bool InLiveWire { get; set; }

    public SurvivorCondition Condition { get; private set; }

    /// <summary>At zero condition. Nobody dies, but a critical resident cannot board without the first-aid hold.</summary>
    public bool IsCritical => Condition != null && Condition.IsCritical;

    /// <summary>The first-aid hold is needed: they were hurt, or they have been left too long.</summary>
    public bool NeedsFirstAidKit => Injured || IsCritical;

    public Triage Triage => Condition != null ? Condition.Triage : Triage.Green;

    public string DisplayName => string.IsNullOrEmpty(residentName) ? name : residentName;

    /// <summary>A roof, or the far side of a debris jam: the boat cannot get a hand to them.</summary>
    public bool RequiresLine => situation == Situation.Rooftop || situation == Situation.Trapped;

    /// <summary>A line (or a swimmer) has brought them to the boat, so they can be taken aboard from here.</summary>
    public bool LineDelivered { get; set; }

    /// <summary>Somebody has hold of them and is bringing them in: a line from the boat, or a swimmer towing.</summary>
    public bool IsHeld { get; set; }

    public enum Arrival { Boat, Line, Swimmer }
    /// <summary>How they got to the boat: it went to them, a thrown line brought them, or a swimmer towed them.</summary>
    public Arrival ArrivedBy { get; set; } = Arrival.Boat;

    /// <summary>The line from the boat that has hold of them, while it does.</summary>
    public RescueLine Line { get; set; }

    /// <summary>Where to aim a sight line to see this person (their head, not their feet under the water).</summary>
    public Vector3 SightPoint
    {
        get
        {
            var visual = GetComponent<SurvivorVisual>();
            return visual != null ? visual.SightPoint : transform.position + Vector3.up;
        }
    }

    /// <summary>How fast the flood is carrying them (zero for anyone holding on to something or standing).</summary>
    public Vector3 DriftVelocity
    {
        get
        {
            var visual = GetComponent<SurvivorVisual>();
            return visual != null ? visual.DriftVelocity : Vector3.zero;
        }
    }

    /// <summary>Every resident in the world right now, rescued or not. Kept up to date as they appear and go.</summary>
    public static readonly System.Collections.Generic.List<Survivor> All = new System.Collections.Generic.List<Survivor>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        // The pick-up radius bobs with the waves and may drift with the current. A trigger that moves belongs on
        // a kinematic body: moved as a bare collider, the physics engine has to treat it as scenery that keeps
        // teleporting.
        var body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        // Everybody waiting has a condition, whether the scene gave them one or not.
        Condition = GetComponent<SurvivorCondition>();
        if (Condition == null) Condition = gameObject.AddComponent<SurvivorCondition>();
    }

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
        if (c is SphereCollider sc) sc.radius = 4f;
    }

    void OnTriggerEnter(Collider other)
    {
        var boat = HullOwner(other);
        if (boat != null && !IsRescued) boat.RegisterNearby(this);
    }

    void OnTriggerExit(Collider other)
    {
        var boat = HullOwner(other);
        if (boat != null) boat.UnregisterNearby(this);
    }

    /// <summary>Only the boat's own hull counts. Evacuees and props are children of the boat, so a plain
    /// GetComponentInParent would let a passenger leaving this radius cancel a rescue the hull is still in.</summary>
    static SimpleBoat HullOwner(Collider other)
    {
        var boat = other.GetComponentInParent<SimpleBoat>();
        return (boat != null && other == boat.Hull) ? boat : null;
    }

    // ------------------------------------------------------------------ requirements
    /// <summary>Do we have the gear to pull this person aboard? (The first-aid hold is handled by the boat.)</summary>
    public bool CanBeRescued(out string reason)
    {
        reason = null;

        if (InLiveWire)
        {
            reason = "There is a live wire in the water here - get the power cut first (C with the Radyo).";
            return false;
        }
        if (RequiresLine && !LineDelivered)
        {
            if (IsHeld) reason = "They are still coming in on the line - wait until they are alongside.";
            else if (situation == Situation.Rooftop) reason = "On a roof - out of reach from the water. Throw them a line (T).";
            else reason = "Behind debris - the boat can't reach them. Throw them a line (T).";
            return false;
        }

        if (Missing(Need.Lifebuoy, RescueItemId.Lifebuoy))
        {
            reason = "In open water - you can't pull them out without a Salbabida (lifebuoy).";
            return false;
        }
        if (Missing(Need.Rope, RescueItemId.Rope))
        {
            reason = "Stuck behind debris - you can't reach them without a Lubid (rope).";
            return false;
        }
        if (NeedsFirstAidKit && !LoadoutState.Has(RescueItemId.FirstAid))
        {
            reason = IsCritical && !Injured
                ? "Critical - they were left too long and can't board without a Botika (first-aid kit)."
                : "Injured - they can't board without a Botika (first-aid kit).";
            return false;
        }
        return true;
    }

    /// <summary>Hurt or critical, and we have a kit: the boat asks the player to hold E for 2 seconds.</summary>
    public bool RequiresStabilise => NeedsFirstAidKit;

    /// <summary>
    /// They have let go of what they were holding on to - a line or a swimmer has them now. A clinger who lets go of the
    /// pole is in the water like anybody else: if they are then let go of, the current takes them.
    /// </summary>
    public void Dislodge()
    {
        if (situation != Situation.Clinging) return;
        situation = Situation.OpenWater;
        var visual = GetComponent<SurvivorVisual>();
        if (visual != null) visual.BecomeAdrift();
    }

    /// <summary>Everything they need is packed. Says nothing about whether they can be reached.</summary>
    public bool HasRequiredItems =>
        !Missing(Need.Lifebuoy, RescueItemId.Lifebuoy) && !Missing(Need.Rope, RescueItemId.Rope) &&
        (!NeedsFirstAidKit || LoadoutState.Has(RescueItemId.FirstAid));

    bool Missing(Need flag, RescueItemId item) => needs.HasFlag(flag) && !LoadoutState.Has(item);

    // ------------------------------------------------------------------ rescue
    /// <summary>Called by the boat. Pass the boat to get the hop animation.</summary>
    public void Rescue(SimpleBoat boat = null)
    {
        if (IsRescued) return;
        // Record how they were on the way in (their tag, whether they were critical), before anything changes it.
        RescueManager.Instance?.OnSurvivorPickedUp(this);
        IsRescued = true;
        IsHeld = false;
        if (Line != null) Line.Retrieve();      // the ring and the rope come back aboard with them
        boardingEpoch = boat != null ? boat.BoardingEpoch : 0;

        // Count which packed items actually earned their slot (shown in the end-of-mission report)
        if (needs.HasFlag(Need.Lifebuoy)) LoadoutState.RegisterUse(RescueItemId.Lifebuoy);
        if (needs.HasFlag(Need.Rope))     LoadoutState.RegisterUse(RescueItemId.Rope);
        if (NeedsFirstAidKit)             LoadoutState.RegisterUse(RescueItemId.FirstAid);

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        var visual = GetComponent<SurvivorVisual>();
        var passengers = boat != null ? boat.GetComponent<BoatPassengers>() : null;

        if (boat != null && passengers != null && passengers.enabled && visual != null && visual.Animator != null)
        {
            StartCoroutine(BoardBoat(boat, passengers, visual));
            return;
        }

        // No character to show (or no boat): just count them aboard.
        NotifyBoat(boat != null ? boat.transform : null);
        gameObject.SetActive(false);
    }

    int boardingEpoch;

    /// <summary>Tells the boat to show this person on board (BoatPassengers), as the same character.</summary>
    void NotifyBoat(Transform boat)
    {
        if (boat == null) return;
        var passengers = boat.GetComponent<BoatPassengers>();
        if (passengers == null) return;
        var visual = GetComponent<SurvivorVisual>();
        passengers.AddPassenger(visual != null ? visual.Character : null, boardingEpoch, this);
    }

    /// <summary>
    /// Coming aboard the way it is actually done: the person makes the last few strokes to the side of the boat,
    /// and is then hauled up over the tube and into a seat. Nobody leaves the water, nobody changes size, and it
    /// is the same character from the first stroke to sitting down.
    /// </summary>
    IEnumerator BoardBoat(SimpleBoat boat, BoatPassengers passengers, SurvivorVisual visual)
    {
        var anim = visual.Animator;
        int seat = passengers.ReserveSeat();
        var water = WaterSurface.Instance;

        // ---- to the side of the hull ----
        visual.SetPose(CharacterPose.Swim);
        Vector3 from = transform.position;
        Quaternion fromRotation = transform.rotation;
        float distance = (HullSide(boat, from) - from).magnitude;
        float duration = Mathf.Clamp(distance / swimToBoatSpeed, 0.35f, 1.4f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            Vector3 side = HullSide(boat, transform.position);
            Vector3 p = Vector3.Lerp(from, side, Mathf.SmoothStep(0f, 1f, t));
            if (water != null) p.y = water.GetHeight(p);
            transform.position = p;

            Vector3 toBoat = boat.transform.position - p;
            toBoat.y = 0f;
            if (toBoat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(fromRotation, Quaternion.LookRotation(toBoat * anim.ForwardSign()), t);

            visual.ApplyWaterline();
            yield return null;
        }

        // ---- up over the tube and into the seat ----
        visual.SetPose(CharacterPose.Idle);
        Vector3 liftFrom = transform.position;
        Quaternion liftFromRotation = transform.rotation;
        float sinkFrom = -anim.transform.localPosition.y;

        for (float t = 0f; t < 1f; t += Time.deltaTime / haulDuration)
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            passengers.SeatPose(seat, out Vector3 seatPosition, out Quaternion seatRotation);

            Vector3 p = Vector3.Lerp(liftFrom, seatPosition, k);
            p.y += Mathf.Sin(k * Mathf.PI) * haulHeight;        // clear the tube on the way over
            transform.SetPositionAndRotation(p, Quaternion.Slerp(liftFromRotation, seatRotation, k));
            anim.transform.localPosition = new Vector3(0f, -Mathf.Lerp(sinkFrom, 0f, k), 0f);
            yield return null;
        }

        var character = visual.Release();
        passengers.SeatExisting(character, seat, boardingEpoch, this);
        gameObject.SetActive(false);
    }

    /// <summary>The point on the waterline just outside the hull that is nearest to where they are.</summary>
    public static Vector3 HullSide(SimpleBoat boat, Vector3 from)
    {
        Vector3 centre = boat.transform.position;
        Vector3 onHull = boat.Hull != null ? boat.Hull.ClosestPoint(from) : centre;
        Vector3 outward = onHull - centre;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f) outward = from - centre;
        outward.y = 0f;
        return onHull + outward.normalized * 0.35f;
    }
}