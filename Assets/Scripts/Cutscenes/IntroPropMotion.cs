using UnityEngine;

/// <summary>
/// The few things in the story intro that move on a cue rather than with the flood: a family photo that slips out of
/// somebody's hands and floats away, a shanty that leans over and sinks, a tricycle that tips. It is deliberately
/// small and simple (no physics), so it plays the same every time, can be skipped at any moment, and costs nothing
/// when idle. <see cref="RestoreStart"/> puts the prop back where the builder left it.
/// </summary>
public class IntroPropMotion : MonoBehaviour
{
    public enum Mode { None, Fall, TiltAndSink }

    public Mode mode = Mode.None;

    [Header("Fall (a photo: thrown clear, falls to the water, then drifts)")]
    [Tooltip("The speed it leaves the hand with: put it past the roof's edge so it lands in open water.")]
    public Vector3 launch = new Vector3(1.5f, 1.2f, 0f);
    [Tooltip("The current once it is on the water.")]
    public Vector3 drift = new Vector3(0.7f, 0f, 0.25f);

    [Header("Tilt and sink (a shanty, a tricycle)")]
    public Vector3 tiltAxis = Vector3.forward;
    public float tiltDegrees = 38f;
    public float sinkMetres = 1.6f;
    public float seconds = 3.2f;

    Vector3 startPosition;
    Quaternion startRotation;
    float playedAt = -1f;
    Vector3 velocity;
    bool onWater;

    /// <summary>Playing now, from the cue to its end.</summary>
    public bool Playing { get; private set; }

    void Awake()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    /// <summary>Starts the move (the cue).</summary>
    public void Play()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        playedAt = Time.time;
        velocity = launch;
        onWater = false;
        Playing = mode != Mode.None;
    }

    /// <summary>Back where it was before the cue. (Not named Reset: Unity calls that itself when a component is added in the
    /// Editor, which once threw every prop to the origin.)</summary>
    public void RestoreStart()
    {
        Playing = false;
        playedAt = -1f;
        transform.SetPositionAndRotation(startPosition, startRotation);
    }

    void Update()
    {
        if (!Playing) return;
        float t = Time.time - playedAt;
        if (mode == Mode.TiltAndSink)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            // A last shudder as it goes, so it does not just slide.
            float shudder = Mathf.Sin(t * 22f) * (1f - k) * 1.5f * Mathf.Clamp01(t * 2f);
            transform.rotation = Quaternion.AngleAxis(tiltDegrees * k + shudder, tiltAxis) * startRotation;
            transform.position = startPosition + Vector3.down * (sinkMetres * k);
            if (t >= seconds) Playing = false;
        }
        else if (mode == Mode.Fall)
        {
            var water = WaterSurface.Instance;
            float surface = water != null ? water.GetHeight(transform.position) : FloodLevel.Offset;
            if (!onWater)
            {
                velocity += Vector3.down * 9.81f * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;
                transform.Rotate(220f * Time.deltaTime, 90f * Time.deltaTime, 160f * Time.deltaTime, Space.Self);     // fluttering
                if (transform.position.y <= surface + 0.02f && velocity.y < 0f)
                {
                    onWater = true;
                    var p = transform.position; p.y = surface + 0.02f; transform.position = p;
                    transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);       // face up, lying on the water
                }
            }
            else
            {
                // Lying on the water and carried off, riding the surface as the flood climbs.
                var p = transform.position + drift * Time.deltaTime;
                p.y = surface + 0.02f;
                transform.position = p;
                transform.Rotate(0f, 6f * Time.deltaTime, 0f, Space.World);
            }
        }
    }
}
