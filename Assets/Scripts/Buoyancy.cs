using UnityEngine;

/// <summary>
/// Makes a Rigidbody float on the WaterSurface. Samples the wave height at several float points
/// and pushes each one up in proportion to how deep it is - so the boat bobs, pitches and rolls with the waves.
/// If you don't assign Float Points, 6 points are generated along the bottom of the BoxCollider.
///
/// The lift is sized for the hull's design mass, not for whatever the Rigidbody weighs right now. Load the boat
/// and it sinks lower until the deeper float points make up the difference - exactly as a real hull does.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Buoyancy : MonoBehaviour
{
    [Header("Float points (optional)")]
    [Tooltip("Empty child objects placed at the hull bottom (bow, stern, left, right). Leave empty to auto-generate from the BoxCollider.")]
    public Transform[] floatPoints;

    [Header("Buoyancy")]
    [Tooltip("Up-push relative to gravity when fully submerged. 2 = boat settles with roughly half of the float points' depth under water.")]
    public float floatStrength = 2f;
    [Tooltip("Depth (m) below the surface at which a point gives full lift.")]
    public float submergeDepth = 0.6f;
    [Tooltip("Stops endless bouncing.")]
    public float verticalDamping = 2.5f;

    [Header("Load")]
    [Tooltip("Mass (kg) the hull floats at its normal waterline with. A heavier Rigidbody rides lower. " +
             "0 = whatever the Rigidbody weighs when the game starts, so nothing changes unless its mass does.")]
    public float designMass = 0f;
    [Tooltip("How far below Submerge Depth the lift keeps growing, as a multiple of it. This is the hull's reserve: " +
             "loaded past (Float Strength x this) times its design mass, it can no longer float.")]
    public float reserveBuoyancy = 2f;

    [Header("Stability")]
    public float waterAngularDamping = 2f;
    public float airAngularDamping = 0.1f;
    [Tooltip("Pulls the boat upright so it can't capsize. 0 = off.")]
    public float uprightStrength = 4f;
    [Tooltip("Low centre of mass = more stable.")]
    public bool overrideCenterOfMass = true;
    public Vector3 centerOfMass = new Vector3(0f, -0.3f, 0f);

    /// <summary>0 = fully out of water, 1 = all float points fully submerged.</summary>
    public float Submerged { get; private set; }

    Rigidbody rb;
    Vector3[] localPoints;
    float startMass;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        if (overrideCenterOfMass) rb.centerOfMass = centerOfMass;
        localPoints = BuildLocalPoints();
        startMass = rb.mass;
    }

    /// <summary>The mass the hull should float at its normal waterline with. Anything above it is load.</summary>
    public void SetDesignMass(float kilograms)
    {
        designMass = Mathf.Max(kilograms, 0.01f);
    }

    int Count => (floatPoints != null && floatPoints.Length > 0) ? floatPoints.Length : localPoints.Length;

    Vector3 GetPoint(int i)
    {
        if (floatPoints != null && floatPoints.Length > 0) return floatPoints[i].position;
        return transform.TransformPoint(localPoints[i]);
    }

    Vector3[] BuildLocalPoints()
    {
        var box = GetComponent<BoxCollider>();
        Vector3 c = box != null ? box.center : Vector3.zero;
        Vector3 e = box != null ? box.size * 0.5f : new Vector3(0.6f, 0.3f, 1.2f);
        if (box == null)
            Debug.LogWarning("Buoyancy: no BoxCollider found - using default float points. Add a BoxCollider or assign Float Points.", this);

        float y = c.y - e.y;
        float x = e.x * 0.9f, z = e.z * 0.9f;
        return new[]
        {
            new Vector3(c.x - x, y, c.z - z), new Vector3(c.x + x, y, c.z - z),   // stern L / R
            new Vector3(c.x - x, y, c.z + z), new Vector3(c.x + x, y, c.z + z),   // bow  L / R
            new Vector3(c.x,     y, c.z - z), new Vector3(c.x,     y, c.z + z),   // stern / bow centre
        };
    }

    void FixedUpdate()
    {
        int n = Count;
        float g = Physics.gravity.magnitude;
        float total = 0f;

        // The push is applied as an acceleration, so it has to be scaled down by hand when the body is heavier than
        // the hull was designed for. At design mass the ratio is 1 and the boat sits exactly where it always did.
        float liftMass = designMass > 0f ? designMass : startMass;
        float massRatio = liftMass / Mathf.Max(rb.mass, 0.0001f);

        for (int i = 0; i < n; i++)
        {
            Vector3 p = GetPoint(i);
            float waterY = WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(p) : 0f;
            float depth = waterY - p.y;
            if (depth <= 0f) continue;

            float sub = Mathf.Clamp01(depth / submergeDepth);                       // how wet this point is
            float liftSub = Mathf.Min(depth / submergeDepth, reserveBuoyancy);      // how hard it pushes back
            total += sub;

            float lift = g * floatStrength * liftSub * massRatio / n;
            float damp = -rb.GetPointVelocity(p).y * verticalDamping * sub / n;

            rb.AddForceAtPosition(Vector3.up * (lift + damp), p, ForceMode.Acceleration);
        }

        Submerged = n > 0 ? total / n : 0f;

        rb.angularDamping = Mathf.Lerp(airAngularDamping, waterAngularDamping, Submerged);

        if (uprightStrength > 0f && Submerged > 0f)
        {
            Vector3 up = transform.up;
            Vector3 correction = Vector3.Cross(up, Vector3.up);

            // Past ninety degrees that cross product shrinks again, and dead upside-down it is zero - a capsized
            // hull would simply stay capsized. Keep pushing at full strength, and pick an axis when there is none.
            if (Vector3.Dot(up, Vector3.up) < 0f)
                correction = correction.sqrMagnitude > 1e-6f ? correction.normalized : transform.forward;

            rb.AddTorque(correction * uprightStrength * Submerged, ForceMode.Acceleration);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (floatPoints != null && floatPoints.Length > 0)
        {
            foreach (var t in floatPoints)
                if (t != null) Gizmos.DrawSphere(t.position, 0.12f);
        }
        else
        {
            var pts = Application.isPlaying && localPoints != null ? localPoints : BuildLocalPointsEditor();
            foreach (var lp in pts) Gizmos.DrawSphere(transform.TransformPoint(lp), 0.12f);
        }
    }

    Vector3[] BuildLocalPointsEditor()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return new Vector3[0];
        return BuildLocalPoints();
    }
}
