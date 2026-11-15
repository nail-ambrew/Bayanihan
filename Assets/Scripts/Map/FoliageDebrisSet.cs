using UnityEngine;

/// <summary>
/// The plant models the flood's floating scraps are made of: a torn-off sapling, an uprooted banana, a mat of grass, a
/// raft of creeper. Lives in Resources so <see cref="DebrisProps"/> finds it at run time. Filled by
/// Bayanihan > Foliage > Import Pack; a missing slot falls back to a drawn shape.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Foliage Debris Set", fileName = "FoliageDebrisSet")]
public class FoliageDebrisSet : ScriptableObject
{
    public const string ResourcePath = "Foliage/FoliageDebrisSet";

    public GameObject branch;
    public GameObject bananaLeaf;
    public GameObject grassMat;
    public GameObject weedRaft;

    static FoliageDebrisSet cached;
    static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; looked = false; }

    public static FoliageDebrisSet Current
    {
        get
        {
            if (!looked) { looked = true; cached = Resources.Load<FoliageDebrisSet>(ResourcePath); }
            return cached;
        }
    }

    public GameObject For(DebrisKind kind)
    {
        switch (kind)
        {
            case DebrisKind.Branch: return branch;
            case DebrisKind.BananaLeaf: return bananaLeaf;
            case DebrisKind.GrassMat: return grassMat;
            case DebrisKind.WeedRaft: return weedRaft;
            default: return null;
        }
    }
}
