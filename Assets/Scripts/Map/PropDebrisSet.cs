using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The household scraps the flood carries: buckets and torn roof sheets, from the prop packs. Lives in Resources so
/// <see cref="DebrisProps"/> finds it at run time. A missing slot falls back to a drawn shape.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Prop Debris Set", fileName = "PropDebrisSet")]
public class PropDebrisSet : ScriptableObject
{
    public const string ResourcePath = "Props/PropDebrisSet";

    public List<GameObject> buckets = new List<GameObject>();
    public List<GameObject> roofSheets = new List<GameObject>();

    static PropDebrisSet cached;
    static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; looked = false; }

    public static PropDebrisSet Current
    {
        get
        {
            if (!looked) { looked = true; cached = Resources.Load<PropDebrisSet>(ResourcePath); }
            return cached;
        }
    }

    public GameObject For(DebrisKind kind)
    {
        var list = kind == DebrisKind.Bucket ? buckets : kind == DebrisKind.RoofSheet ? roofSheets : null;
        if (list == null || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }
}
