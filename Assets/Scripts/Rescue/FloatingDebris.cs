using System.Collections.Generic;
using UnityEngine;

public enum DebrisKind { Plank, Crate, Drum, Log }

/// <summary>
/// A piece of wreckage afloat in the flood: a board, a crate, a drum, a log. It rides the waves on the same
/// <see cref="Buoyancy"/> as the boat and is carried along by the current, so it drifts down the street and
/// fetches up against walls.
///
/// The boat can push it aside - that is what a boat does - but not at speed: ram it harder than "Strike Speed" and
/// it counts as a hull strike in the report. The swimmer is not stopped by it (they duck round it).
///
/// Pieces are made by <see cref="Spawn"/> (or scattered by a <see cref="DebrisField"/>), so a scene stores a field
/// and its settings rather than dozens of meshes.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FloatingDebris : MonoBehaviour
{
    public static readonly List<FloatingDebris> All = new List<FloatingDebris>();

    [Tooltip("How quickly it picks up the speed of the water (per second). Higher = it follows the current more closely.")]
    public float driftResponse = 0.8f;
    [Tooltip("Hit by the boat faster than this (m/s) and it is a hull strike.")]
    public float strikeSpeed = 3.5f;

    public DebrisKind Kind { get; private set; }
    public Rigidbody Body { get; private set; }
    public Buoyancy Float { get; private set; }

    /// <summary>Has the boat struck this hard enough to be logged?</summary>
    public int Strikes { get; private set; }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Float = GetComponent<Buoyancy>();
    }

    void Start()
    {
        // The swimmer slips round wreckage rather than being pinned against it.
        var player = FindFirstObjectByType<PlayerRescuer>();
        if (player != null) IgnoreAll(player.GetComponentsInChildren<Collider>());
    }

    void IgnoreAll(Collider[] others)
    {
        foreach (var mine in GetComponentsInChildren<Collider>())
            foreach (var other in others)
                if (mine != other) Physics.IgnoreCollision(mine, other, true);
    }

    void FixedUpdate()
    {
        // The water drags it along: its speed eases towards the water's. Only while it is actually in the water.
        if (Float != null && Float.Submerged < 0.02f) return;

        Vector3 flow = FloodCurrent.Sample(Body.position);
        Vector3 v = Body.linearVelocity;
        var difference = new Vector3(flow.x - v.x, 0f, flow.z - v.z);
        Body.AddForce(difference * driftResponse, ForceMode.Acceleration);
    }

    void OnCollisionEnter(Collision collision)
    {
        var other = collision.rigidbody;
        if (other == null || !other.TryGetComponent(out SimpleBoat boat)) return;
        if (collision.collider != boat.Hull) return;                       // somebody sitting on the boat is not the hull
        if (collision.relativeVelocity.magnitude < strikeSpeed) return;

        if (boat.Strike("Debris strike! Slow down where the wreckage is floating.")) Strikes++;
    }

    // ------------------------------------------------------------------ making one

    /// <summary>A piece of wreckage afloat at this spot, turned to <paramref name="yaw"/> degrees.</summary>
    public static FloatingDebris Spawn(DebrisKind kind, Vector3 at, float yaw = 0f, Transform parent = null)
    {
        var water = WaterSurface.Instance;
        at.y = (water != null ? water.GetHeight(at) : 0f) + 0.05f;

        var root = new GameObject("Debris " + kind);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

        DebrisProps.AddVisual(root.transform, kind, Vector3.zero, Quaternion.identity);

        // Everything the Rigidbody and Buoyancy read in Awake has to be in place before they are added.
        var body = root.AddComponent<Rigidbody>();
        body.mass = DebrisProps.Mass(kind);
        body.linearDamping = 0.2f;

        var box = root.AddComponent<BoxCollider>();
        box.size = DebrisProps.Size(kind);

        var floating = root.AddComponent<Buoyancy>();
        floating.floatStrength = 2f;
        floating.submergeDepth = Mathf.Max(0.08f, box.size.y * 0.7f);     // rides about a third under
        floating.verticalDamping = 2.5f;
        floating.uprightStrength = kind == DebrisKind.Drum ? 3f : 1.5f;
        floating.waterAngularDamping = 1.5f;
        floating.overrideCenterOfMass = false;

        var debris = root.AddComponent<FloatingDebris>();
        debris.Kind = kind;
        return debris;
    }
}
