using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A jam of wreckage piled across a channel: boards, crates, drums and logs wedged together. The boat cannot go
/// through it, and a hard hit is a hull strike. The swimmer can: they slip through the gaps, which is how somebody
/// trapped behind a jam gets reached from the water. A line can be thrown over it, and it does not hide anyone
/// behind it from the crew.
///
/// Put this on an empty object across the street, at the water level. "Size" is the pile's footprint: across the
/// channel (x), how high it stands above the water (y) and how deep it is along the channel (z). The pile itself is
/// built when the mission starts, from the seed, so the scene stores only these settings.
/// </summary>
[DisallowMultipleComponent]
public class DebrisBarrier : MonoBehaviour
{
    public static readonly List<DebrisBarrier> All = new List<DebrisBarrier>();

    [Tooltip("Across the channel (x), height above the water (y), depth along it (z), in metres.")]
    public Vector3 size = new Vector3(8f, 1.5f, 3f);
    [Tooltip("How many pieces make up the pile (looks only; the barrier itself is one solid block).")]
    [Min(1)] public int pieces = 30;
    [Tooltip("Same seed, same pile.")]
    public int seed = 3;
    [Tooltip("How far below the water the block reaches, so the boat cannot slide underneath (metres).")]
    public float depthBelowWater = 1.4f;

    /// <summary>The solid block the boat runs into.</summary>
    public BoxCollider Jam { get; private set; }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    void Awake()
    {
        // A kinematic body, so the pile is solid to the boat (and a hard hit counts as a strike) yet does not hide
        // anybody: sight lines pass things that belong to a rigidbody. A loose pile of boards does not hide a person.
        var body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;

        Jam = gameObject.AddComponent<BoxCollider>();
        Jam.size = new Vector3(size.x * 0.92f, size.y + depthBelowWater, size.z * 0.9f);
        Jam.center = new Vector3(0f, (size.y - depthBelowWater) * 0.5f, 0f);

        BuildPile();
    }

    void Start()
    {
        // The swimmer slips through the gaps; only the boat is stopped.
        var player = FindFirstObjectByType<PlayerRescuer>();
        if (player == null) return;
        foreach (var c in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(Jam, c, true);
    }

    /// <summary>Is this point over the jam? Horizontal position only.</summary>
    public bool Covers(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
    }

    void BuildPile()
    {
        var random = new System.Random(seed);
        var pile = new GameObject("Pile").transform;
        pile.SetParent(transform, false);

        // Long boards and logs make most of it; crates and drums fill in.
        var kinds = new[] { DebrisKind.Plank, DebrisKind.Plank, DebrisKind.Plank, DebrisKind.Log, DebrisKind.Crate, DebrisKind.Drum };

        // One piece to each strip of the channel's width, so the pile really does reach from side to side and a gap
        // that looks open is not blocked by something nobody can see. The strips are dealt out in a shuffled order.
        var order = new int[pieces];
        for (int i = 0; i < pieces; i++) order[i] = i;
        for (int i = pieces - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int i = 0; i < pieces; i++)
        {
            var kind = kinds[random.Next(kinds.Length)];
            float strip = (order[i] + (float)random.NextDouble()) / pieces;
            var local = new Vector3(
                (strip - 0.5f) * size.x,
                Mathf.Lerp(-0.1f, size.y * 0.8f, (float)random.NextDouble()),
                ((float)random.NextDouble() - 0.5f) * size.z * 0.8f);

            // Tilted every which way, as things are when they have been thrown together.
            var tilt = Quaternion.Euler(
                ((float)random.NextDouble() - 0.5f) * 50f,
                (float)random.NextDouble() * 360f,
                ((float)random.NextDouble() - 0.5f) * 50f);

            DebrisProps.AddVisual(pile, kind, local, tilt);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.5f, 0.2f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
    }
}

/// <summary>What each kind of wreckage looks like, weighs and measures. Built from primitives, so there is nothing to import.</summary>
public static class DebrisProps
{
    /// <summary>The box a piece fits in (its collider, and what its float points are taken from).</summary>
    public static Vector3 Size(DebrisKind kind)
    {
        switch (kind)
        {
            case DebrisKind.Plank: return new Vector3(0.22f, 0.06f, 2.4f);
            case DebrisKind.Crate: return new Vector3(0.7f, 0.55f, 0.7f);
            case DebrisKind.Drum:  return new Vector3(0.58f, 0.85f, 0.58f);
            case DebrisKind.Branch:     return new Vector3(0.6f, 0.35f, 2.2f);
            case DebrisKind.BananaLeaf: return new Vector3(1.4f, 0.3f, 1.6f);
            case DebrisKind.GrassMat:   return new Vector3(1.8f, 0.2f, 1.8f);
            case DebrisKind.WeedRaft:   return new Vector3(1.8f, 0.2f, 2.4f);
            default:               return new Vector3(0.4f, 0.4f, 3f);        // log
        }
    }

    public static float Mass(DebrisKind kind)
    {
        switch (kind)
        {
            case DebrisKind.Plank: return 14f;
            case DebrisKind.Crate: return 30f;
            case DebrisKind.Drum:  return 25f;
            case DebrisKind.Branch:     return 18f;
            case DebrisKind.BananaLeaf: return 5f;
            case DebrisKind.GrassMat:   return 4f;
            case DebrisKind.WeedRaft:   return 6f;
            default:               return 90f;
        }
    }

    static Color ColourOf(DebrisKind kind)
    {
        switch (kind)
        {
            case DebrisKind.Plank: return new Color(0.58f, 0.45f, 0.30f);
            case DebrisKind.Crate: return new Color(0.66f, 0.48f, 0.27f);
            case DebrisKind.Drum:  return new Color(0.20f, 0.36f, 0.62f);
            case DebrisKind.Branch:     return new Color(0.37f, 0.31f, 0.26f);
            case DebrisKind.BananaLeaf: return new Color(0.29f, 0.50f, 0.19f);
            case DebrisKind.GrassMat:   return new Color(0.43f, 0.62f, 0.22f);
            case DebrisKind.WeedRaft:   return new Color(0.25f, 0.48f, 0.18f);
            default:               return new Color(0.38f, 0.29f, 0.20f);
        }
    }

    /// <summary>Adds a piece's looks (no collider) as a child. Boards and crates are cubes; drums and logs are cylinders.</summary>
    public static GameObject AddVisual(Transform parent, DebrisKind kind, Vector3 localPosition, Quaternion localRotation)
    {
        if (FloatingDebris.PlantScraps is var scraps && System.Array.IndexOf(scraps, kind) >= 0)
        {
            var plant = PlantVisual(parent, kind, localPosition, localRotation);
            if (plant != null) return plant;
        }
        bool round = kind == DebrisKind.Drum || kind == DebrisKind.Log;
        var piece = GameObject.CreatePrimitive(round ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        piece.name = kind.ToString();

        var collider = piece.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Object.Destroy(collider);
        }

        var t = piece.transform;
        t.SetParent(parent, false);
        t.localPosition = localPosition;

        Vector3 size = Size(kind);
        if (kind == DebrisKind.Log)
        {
            // A cylinder stands along its y axis; a log lies along z.
            t.localRotation = localRotation * Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(size.x, size.z * 0.5f, size.y);
        }
        else if (kind == DebrisKind.Drum)
        {
            t.localRotation = localRotation;
            t.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
        }
        else
        {
            t.localRotation = localRotation;
            t.localScale = size;
        }

        var renderer = piece.GetComponent<Renderer>();
        renderer.sharedMaterial = ProceduralArt.SharedLit(ColourOf(kind));
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return piece;
    }

    /// <summary>
    /// A plant scrap made from the barangay's own foliage models: a sapling or a banana lying on its side, a flattened mat
    /// of grass, a raft of creeper. Null if the set or the model is missing (the drawn shape is used then).
    /// </summary>
    static GameObject PlantVisual(Transform parent, DebrisKind kind, Vector3 localPosition, Quaternion localRotation)
    {
        var set = FoliageDebrisSet.Current;
        var prefab = set != null ? set.For(kind) : null;
        if (prefab == null) return null;

        var holder = new GameObject(kind.ToString());
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localPosition;
        holder.transform.localRotation = localRotation;

        var model = Object.Instantiate(prefab, holder.transform, false);
        model.name = prefab.name;
        foreach (var c in model.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Object.Destroy(c.gameObject == model ? (Object)c : c.gameObject);
        }
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent != null ? parent.gameObject.layer : 0;

        var m = model.transform;
        Vector3 size = Size(kind);
        switch (kind)
        {
            case DebrisKind.Branch:         // a 2.5 m sapling, torn off and lying along the water
            case DebrisKind.BananaLeaf:     // a young banana, uprooted and flat
                float length = kind == DebrisKind.Branch ? 2.47f : 1.68f;
                float s = size.z / length;
                m.localRotation = Quaternion.Euler(90f, 0f, 0f);                 // the trunk (up) now runs along +z
                m.localScale = new Vector3(s, s, s);
                m.localPosition = new Vector3(0f, 0f, -size.z * 0.5f);           // centred on the piece
                break;
            case DebrisKind.GrassMat:       // a cogon patch pressed flat
                m.localScale = new Vector3(0.95f, 0.3f, 0.95f);
                m.localPosition = new Vector3(0f, -0.08f, 0f);
                break;
            default:                        // a creeper patch, already low and wide
                m.localScale = new Vector3(1f, 0.9f, 1f);
                m.localPosition = new Vector3(0f, -0.05f, 0f);
                break;
        }
        return holder;
    }
}
