using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wreckage the intro's "debris floats" beat sends down the street: made once when the cutscene starts (never
/// during it), kept hidden, then let go a few at a time upstream of the camera on Cue_Debris. At most the quality
/// preset's cap (40 on High) are alive. Sixteen kinds: the ten the game already floats (<see cref="FloatingDebris"/>)
/// and the household rubbish in <see cref="extraKinds"/> (bottle, trash bag, cardboard, plastic chair, bin, coconut
/// frond). They float and bump on the real flood; everything is removed when the intro ends.
/// </summary>
public class IntroDebrisPool : MonoBehaviour
{
    [Tooltip("Household rubbish models (placeholders made by the intro builder). Each gets a body and floats.")]
    public GameObject[] extraKinds;
    [Tooltip("Where the pieces are let go: across (x) and along (z) this object's area, in metres.")]
    public Vector2 area = new Vector2(10f, 6f);
    [Tooltip("The way the flood carries them (world direction), and how hard (m/s^2 per kg-ish).")]
    public Vector3 flow = Vector3.forward;
    public float push = 1.2f;
    [Tooltip("Seconds over which the whole pool is let go.")]
    public float releaseOver = 2.5f;
    public int seed = 4;

    readonly List<Rigidbody> pieces = new List<Rigidbody>();
    int released;
    float releaseStarted = -1f;

    public int Cap { get; private set; }
    public int Active
    {
        get { int n = 0; foreach (var p in pieces) if (p != null && p.gameObject.activeSelf) n++; return n; }
    }
    /// <summary>Different kinds made, for tests.</summary>
    public int KindCount { get; private set; }

    /// <summary>Makes the pool (hidden). Called once when the intro starts.</summary>
    public void Prepare(int cap)
    {
        Clear();
        Cap = Mathf.Max(0, cap);
        var random = new System.Random(seed);
        var builtIn = (DebrisKind[])System.Enum.GetValues(typeof(DebrisKind));
        int kinds = builtIn.Length + (extraKinds != null ? extraKinds.Length : 0);
        var made = new HashSet<string>();
        for (int i = 0; i < Cap; i++)
        {
            int k = i < kinds ? i : random.Next(kinds);         // every kind at least once, then a mix
            Rigidbody body;
            if (k < builtIn.Length)
                body = FloatingDebris.Spawn(builtIn[k], transform.position, 0f, transform).Body;
            else
                body = MakeExtra(extraKinds[k - builtIn.Length]);
            if (body == null) continue;
            made.Add(body.name);
            body.gameObject.SetActive(false);
            pieces.Add(body);
        }
        KindCount = made.Count;
        released = 0;
        releaseStarted = -1f;
    }

    Rigidbody MakeExtra(GameObject prefab)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, transform);
        go.name = prefab.name;
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(bounds.center);
        box.size = Vector3.Max(bounds.size, Vector3.one * 0.1f);
        var body = go.AddComponent<Rigidbody>();
        body.mass = Mathf.Clamp(bounds.size.x * bounds.size.y * bounds.size.z * 40f, 0.5f, 8f);
        body.linearDamping = 0.3f;
        var floating = go.AddComponent<Buoyancy>();
        floating.floatStrength = 2f;
        floating.submergeDepth = Mathf.Max(0.06f, box.size.y * 0.6f);
        floating.uprightStrength = 1.2f;
        floating.overrideCenterOfMass = false;
        FloatingDebris.MoveToDebrisLayer(go);
        return body;
    }

    /// <summary>Starts letting the pieces go (Cue_Debris).</summary>
    public void Release() => releaseStarted = Time.time;

    void FixedUpdate()
    {
        if (releaseStarted < 0f) return;
        int due = releaseOver <= 0f ? pieces.Count : Mathf.CeilToInt(pieces.Count * Mathf.Clamp01((Time.time - releaseStarted) / releaseOver));
        var random = new System.Random(seed + released);
        while (released < due && released < pieces.Count)
        {
            var body = pieces[released++];
            if (body == null) continue;
            float x = ((float)random.NextDouble() - 0.5f) * area.x, z = ((float)random.NextDouble() - 0.5f) * area.y;
            var at = transform.TransformPoint(new Vector3(x, 0f, z));
            var water = WaterSurface.Instance;
            at.y = (water != null ? water.GetHeight(at) : transform.position.y) + 0.1f;
            body.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
            body.gameObject.SetActive(true);
            body.linearVelocity = flow.normalized * 1.5f;
        }
        var f = flow.normalized * push;
        foreach (var body in pieces)
            if (body != null && body.gameObject.activeSelf) body.AddForce(f, ForceMode.Acceleration);
    }

    public void Clear()
    {
        foreach (var p in pieces)
            if (p != null) { p.gameObject.SetActive(false); Destroy(p.gameObject); }
        pieces.Clear();
        released = 0;
        releaseStarted = -1f;
    }

    void OnDestroy() => Clear();
}
