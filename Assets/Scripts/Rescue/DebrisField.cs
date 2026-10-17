using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A stretch of water with wreckage floating in it. Put this on an empty object where the flood has torn things
/// loose; when the mission starts it scatters <see cref="count"/> boards, crates, drums and logs over the area, and
/// they drift off down the street with the current (see <see cref="FloatingDebris"/>).
///
/// The scene stores only these few settings, not the pieces. The same seed always scatters the same pieces.
/// </summary>
public class DebrisField : MonoBehaviour
{
    [Tooltip("Across (x) and along (z) the area the pieces are scattered over, centred on this object (metres).")]
    public Vector2 area = new Vector2(20f, 8f);
    [Tooltip("How many pieces.")]
    [Min(0)] public int count = 8;
    [Tooltip("Same seed, same pieces in the same places.")]
    public int seed = 7;
    [Tooltip("What can turn up. Empty = a mix of everything.")]
    public DebrisKind[] kinds;
    [Tooltip("Pieces keep this far (metres) from anything solid when they are placed, so none start inside a wall.")]
    public float clearance = 0.9f;
    public bool scatterOnStart = true;

    public readonly List<FloatingDebris> Pieces = new List<FloatingDebris>();

    void Start()
    {
        if (scatterOnStart) Scatter();
    }

    /// <summary>Scatters the pieces. Anything scattered before is removed first.</summary>
    public void Scatter()
    {
        Clear();
        var random = new System.Random(seed);
        var mix = kinds != null && kinds.Length > 0 ? kinds : (DebrisKind[])System.Enum.GetValues(typeof(DebrisKind));

        for (int i = 0; i < count; i++)
        {
            // A few tries for a spot with room: the street has parked things in it.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * area.x;
                float z = ((float)random.NextDouble() - 0.5f) * area.y;
                Vector3 at = transform.TransformPoint(new Vector3(x, 0f, z));

                if (Physics.CheckSphere(at + Vector3.up * 0.5f, clearance, ~0, QueryTriggerInteraction.Ignore)) continue;

                var kind = mix[random.Next(mix.Length)];
                float yaw = (float)random.NextDouble() * 360f;
                Pieces.Add(FloatingDebris.Spawn(kind, at, yaw, transform));
                break;
            }
        }
    }

    public void Clear()
    {
        foreach (var piece in Pieces)
        {
            if (piece == null) continue;
            piece.gameObject.SetActive(false);        // gone from the physics world now, not at the end of the frame
            Destroy(piece.gameObject);
        }
        Pieces.Clear();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(area.x, 0.2f, area.y));
    }
}
