using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-off zone. Needs a Collider set to "Is Trigger" covering the dock / shore area.
///
/// Everybody who steps ashore joins the line at the registration desk (see RegistrationDesk): they are written on the
/// list one at a time, and only then are they safe and scored.
///
/// The boat unloads only once it has actually ARRIVED - its centre has to be inside the zone, not merely a
/// corner of the hull clipping the edge. A zone whose edge reaches into a survivor's pick-up radius would
/// otherwise empty the boat in the middle of a rescue: the player drives close enough to pull someone
/// aboard, the hull touches the zone, and everyone already on board is thrown off at the centre.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EvacuationCenter : MonoBehaviour
{
    [Header("Arrival")]
    [Tooltip("Unload only when the boat's centre is inside the zone. Off = unload the moment the hull touches it " +
             "(the old behaviour - only safe if no survivor can be reached from inside this zone).")]
    public bool requireBoatCentreInside = true;
    [Tooltip("How far past the edge the boat's centre has to be before anyone gets off (metres). Make this bigger " +
             "than (hull half-length + survivor pick-up radius) - (gap between this zone and the nearest survivor), " +
             "so the boat can never be 'arrived' and in pick-up range at the same time. Clamped to half the zone.")]
    public float arrivalMargin = 2f;
    [Tooltip("Seconds before the same boat can unload here again. Stops a double-unload from one approach.")]
    public float rearmSeconds = 1f;
    [Tooltip("Nobody steps off a boat that is still under way: it has to be slower than this (m/s) to unload.")]
    public float maxUnloadSpeed = 2.5f;

    [Header("Landing")]
    [Tooltip("Where evacuees end up once they are off the boat: a porch, a dock, a flight of steps. They are spread " +
             "over a rectangle around this point, at its height. Empty = on top of this zone's box (the old behaviour).")]
    public Transform landingArea;
    [Tooltip("Size of that rectangle along the landing area's own X and Z (metres).")]
    public Vector2 landingAreaSize = new Vector2(6f, 2f);

    /// <summary>Where the residents who have come ashore are registered. Added automatically.</summary>
    public RegistrationDesk Desk { get; private set; }

    Collider zone;
    SimpleBoat boat;            // the boat currently overlapping this zone, if any
    float nextDropAllowed;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        zone = GetComponent<Collider>();
        Desk = GetComponent<RegistrationDesk>();
        if (Desk == null) Desk = gameObject.AddComponent<RegistrationDesk>();
    }

    void OnTriggerEnter(Collider other)
    {
        var b = HullOwner(other);
        if (b != null) boat = b;
    }

    void OnTriggerExit(Collider other)
    {
        var b = HullOwner(other);
        if (b != null && b == boat) boat = null;
    }

    /// <summary>
    /// Only the boat's own hull counts. Evacuees and props are parented to the boat, so a plain
    /// GetComponentInParent would let anything ever attached to the deck unload the whole boat.
    /// </summary>
    static SimpleBoat HullOwner(Collider other)
    {
        var b = other.GetComponentInParent<SimpleBoat>();
        return (b != null && other == b.Hull) ? b : null;
    }

    void Update()
    {
        if (boat == null || boat.Passengers <= 0) return;
        var mission = RescueManager.Instance;
        if (mission != null && mission.GameOver) return;        // the mission is over: nobody else is let off
        if (Time.time < nextDropAllowed) return;
        if (requireBoatCentreInside && !Arrived(boat.transform.position)) return;
        if (boat.GroundSpeed > maxUnloadSpeed)
        {
            boat.SetStatus("Slow right down to let them off.", 0.5f);
            return;
        }

        var residents = new List<Survivor>();
        int dropped = boat.DropOffAll(zone, residents, JoinTheLine);
        if (dropped <= 0) return;

        nextDropAllowed = Time.time + rearmSeconds;
        if (mission != null) mission.OnDroppedOff(dropped, residents);
    }

    /// <summary>A resident is standing ashore: they take their place in the line at the desk.</summary>
    void JoinTheLine(Survivor who, Transform body)
    {
        var mission = RescueManager.Instance;
        var record = mission != null && mission.Roster != null ? mission.Roster.Find(who) : null;
        if (record == null) return;

        record.landedAs = body;
        if (Desk != null) Desk.Enqueue(record);
    }

    /// <summary>True when the point is inside the zone with the arrival margin to spare.</summary>
    bool Arrived(Vector3 p)
    {
        // ClosestPoint returns p unchanged when p is inside the collider - works for any collider shape.
        if ((zone.ClosestPoint(p) - p).sqrMagnitude > 1e-4f) return false;

        if (arrivalMargin <= 0f) return true;

        // Shrink the bounds by the margin on every side (Expand splits the amount across both sides).
        Bounds b = zone.bounds;
        float inset = Mathf.Min(arrivalMargin, Mathf.Min(b.extents.x, b.extents.z) * 0.5f);
        b.Expand(new Vector3(-2f * inset, 0f, -2f * inset));   // height is left alone: the boat sits low in the zone
        return b.Contains(p);
    }

    /// <summary>A spot for one evacuee to stand: somewhere on the landing area, or on top of the zone if none is set.</summary>
    public Vector3 RandomLandingPoint(float yOffset = 0f)
    {
        if (landingArea != null)
        {
            var local = new Vector3(
                Random.Range(-0.5f, 0.5f) * landingAreaSize.x,
                yOffset,
                Random.Range(-0.5f, 0.5f) * landingAreaSize.y);
            return landingArea.TransformPoint(local);
        }

        Bounds b = (zone != null ? zone : GetComponent<Collider>()).bounds;
        float mx = Mathf.Min(0.6f, b.extents.x * 0.5f);
        float mz = Mathf.Min(0.6f, b.extents.z * 0.5f);
        return new Vector3(
            Random.Range(b.min.x + mx, b.max.x - mx),
            b.max.y + yOffset,
            Random.Range(b.min.z + mz, b.max.z - mz));
    }

    void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;
        Bounds b = c.bounds;
        float inset = Mathf.Min(arrivalMargin, Mathf.Min(b.extents.x, b.extents.z) * 0.5f);
        b.Expand(new Vector3(-2f * inset, 0f, -2f * inset));
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.9f);       // where the boat's centre must reach to unload
        Gizmos.DrawWireCube(b.center, b.size);

        if (landingArea == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);        // where evacuees stand afterwards
        Gizmos.matrix = landingArea.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(landingAreaSize.x, 0.05f, landingAreaSize.y));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
