using UnityEngine;

/// <summary>
/// The flow of the floodwater: which way it runs and how fast.
///
/// Anything that floats asks for the water's velocity where it is and moves relative to that. So the boat drifts
/// once the throttle is let go, the swimmer is carried downstream, and somebody adrift in open water is slowly
/// swept along. Holding a boat still beside a person in moving water is the real skill of a flood rescue, and
/// without a current there is nothing to hold it against.
///
/// Put this on an empty GameObject. With none in the scene the water is simply still.
/// </summary>
public class FloodCurrent : MonoBehaviour
{
    public static FloodCurrent Instance { get; private set; }

    [Header("Flow")]
    [Tooltip("Where the water is heading, in degrees: 0 = +Z, 90 = +X (the same convention as the waves).")]
    [Range(0f, 360f)] public float direction = 90f;
    [Tooltip("Speed of the water in metres per second, before the storm's multiplier.")]
    public float speed = 0.6f;

    [Header("Surge")]
    [Tooltip("The flow swells and eases by this fraction of its speed, so it never feels like a conveyor belt.")]
    [Range(0f, 1f)] public float surge = 0.25f;
    [Tooltip("Seconds from one swell to the next.")]
    public float surgePeriod = 9f;

    [Header("Slack water")]
    [Tooltip("Water piles up and slows against whatever it runs into. Within this many metres upstream of a wall " +
             "the flow fades to nothing, so drifting things come to rest instead of being pinned. 0 = uniform flow.")]
    public float slackDistance = 6f;
    [Tooltip("What counts as an obstacle to the flow.")]
    public LayerMask obstacles = ~0;

    /// <summary>Multiplier set by the weather: a stronger storm drives the flood harder.</summary>
    public float StormScale { get; set; } = 1f;

    readonly RaycastHit[] hits = new RaycastHit[8];

    void OnEnable() { Instance = this; }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Velocity of the water at this spot (horizontal, m/s). Zero when the scene has no current.</summary>
    public static Vector3 Sample(Vector3 worldPos)
    {
        return Instance != null ? Instance.VelocityAt(worldPos) : Vector3.zero;
    }

    /// <summary>Unit vector the water is heading along.</summary>
    public Vector3 Heading
    {
        get
        {
            float rad = direction * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }
    }

    public Vector3 VelocityAt(Vector3 worldPos)
    {
        float flow = speed * StormScale;
        if (flow <= 0f) return Vector3.zero;

        if (surge > 0f && surgePeriod > 0.01f)
            flow *= 1f + surge * Mathf.Sin(Time.time * 2f * Mathf.PI / surgePeriod);

        Vector3 heading = Heading;
        if (slackDistance > 0f) flow *= Slack(worldPos, heading);
        return heading * flow;
    }

    /// <summary>
    /// 1 in open water, falling to 0 right up against something solid downstream. Only things fixed in place
    /// count: the boat, the swimmer and drifting debris do not stop the flood.
    /// </summary>
    float Slack(Vector3 worldPos, Vector3 heading)
    {
        // Just above the mean waterline, so sunken kerbs and road furniture are ignored and walls are not.
        Vector3 origin = worldPos;
        origin.y = (WaterSurface.Instance != null ? WaterSurface.Instance.transform.position.y : worldPos.y) + 0.3f;

        int n = Physics.RaycastNonAlloc(origin, heading, hits, slackDistance, obstacles, QueryTriggerInteraction.Ignore);
        float nearest = slackDistance;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].rigidbody != null) continue;
            if (hits[i].distance < nearest) nearest = hits[i].distance;
        }
        return nearest / slackDistance;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        Vector3 from = transform.position;
        Vector3 to = from + Heading * Mathf.Max(speed, 0.2f) * 5f;
        Gizmos.DrawLine(from, to);
        Gizmos.DrawSphere(to, 0.25f);
    }
}
