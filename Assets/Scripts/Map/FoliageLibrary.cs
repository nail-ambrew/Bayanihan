using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a plant is, for placing it: a big shade tree, a banana, a stand of tall grass.</summary>
public enum FoliageGroup { CanopyTree, SmallTree, Bamboo, Banana, Bush, Vine, TallGrass, LowPlant }

/// <summary>Where a plant grows in the barangay. A species can grow in several.</summary>
[Flags]
public enum FoliageHabitat
{
    None = 0,
    /// <summary>A strip along a road, just off the edge.</summary>
    Roadside = 1,
    /// <summary>The gaps between and behind houses.</summary>
    Yard = 2,
    /// <summary>Land with no building near it: vacant lots, the edges of the barangay.</summary>
    OpenLot = 4,
    /// <summary>Parks, groves and other green areas on the map.</summary>
    Green = 8,
    /// <summary>Farmland.</summary>
    Farm = 16,
    Cemetery = 32,
    /// <summary>Along a creek or river.</summary>
    Bank = 64,
    /// <summary>Against the long blank wall of a block (vine curtains).</summary>
    Wall = 128
}

/// <summary>One model of the foliage pack and where it belongs.</summary>
[Serializable]
public class FoliageSpecies
{
    [Tooltip("The pack's code, e.g. T01, G05, F04.")]
    public string code;
    public string name;
    public GameObject prefab;
    public FoliageGroup group;
    public FoliageHabitat habitats;
    [Tooltip("How often it is picked among the species that grow in the same place.")]
    [Min(0f)] public float weight = 1f;
    [Tooltip("Width, height and depth in metres (measured from the prefab).")]
    public Vector3 size;
    [Tooltip("Random uniform scale range.")]
    public Vector2 scale = new Vector2(0.85f, 1.2f);
    [Tooltip("Most it leans off upright, in degrees.")]
    [Range(0f, 12f)] public float maxLean = 4f;

    /// <summary>How far another plant has to keep from this one's base (metres, at scale 1).</summary>
    public float Spacing => Mathf.Max(0.8f, Mathf.Min(size.x, size.z) * (group == FoliageGroup.CanopyTree ? 0.45f : 0.4f));
}

/// <summary>
/// The foliage the Barangay Mabuhay map is planted with, and how thickly: the "Carmona Roadside Flora" pack (trees,
/// tall grass, bananas, bushes and vines from Carmona's roadsides). Filled by Bayanihan > Foliage > Import Pack; the
/// map is planted by <see cref="FoliagePlanner"/>. Change a number here and rebuild the map: nothing is placed by hand.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Foliage Library", fileName = "FoliageLibrary")]
public class FoliageLibrary : ScriptableObject
{
    public List<FoliageSpecies> species = new List<FoliageSpecies>();

    [Header("Extra tree scale (on top of the map's scale)")]
    [Tooltip("Trees, bamboo and bananas are drawn this much bigger, on top of the map's own scale (the Mabuhay map is drawn " +
             "at 2x, trees included). Grass, bushes and vines are always real-sized. 1 = the pack's sizes at the map's scale.")]
    [Min(0.5f)] public float treeScale = 1f;

    /// <summary>The extra size a species is drawn at: <see cref="treeScale"/> for the trees, 1 for the undergrowth.</summary>
    public float GameScale(FoliageSpecies sp) =>
        sp.group == FoliageGroup.CanopyTree || sp.group == FoliageGroup.SmallTree ||
        sp.group == FoliageGroup.Bamboo || sp.group == FoliageGroup.Banana ? treeScale : 1f;

    [Header("How much")]
    [Tooltip("Everything at once: 1 = the tuned amount, 0.5 = half, 2 = twice.")]
    [Range(0f, 3f)] public float density = 1f;
    [Tooltip("Chance a 3 m sample in each place gets a plant, before the clumping. Green areas are the thickest.")]
    public float greenDensity = 0.55f, farmDensity = 0.45f, cemeteryDensity = 0.3f, bankDensity = 0.45f,
                 openLotDensity = 0.17f, yardDensity = 0.16f, roadsideDensity = 0.16f;
    [Tooltip("Metres between the shade trees planted along a road (a random gap in this range, sides alternating).")]
    public Vector2 roadsideTreeGap = new Vector2(12f, 25f);
    [Tooltip("Chance a long blank wall gets a vine curtain.")]
    [Range(0f, 1f)] public float wallVineChance = 0.3f;

    [Header("Grassland (no bare ground)")]
    [Tooltip("Metres (on the layout) between the stands that fill every open patch nothing else grows in. Smaller = thicker, and more to draw.")]
    [Min(1.5f)] public float grasslandGap = 3.5f;
    [Tooltip("Share of those stands that are a bush, and a tree; the rest is tall cogon and talahib.")]
    [Range(0f, 0.5f)] public float grasslandBushShare = 0.08f;
    [Range(0f, 0.2f)] public float grasslandTreeShare = 0.025f;
    [Tooltip("Low grass blades drawn over all open ground: tufts per open spot, and how tall (world metres; the ground is a metre under the flood).")]
    [Range(0, 40)] public int grassTuftsPerSpot = 18;
    public Vector2 grassTuftHeight = new Vector2(1.2f, 2.2f);

    [Header("Where not")]
    [Tooltip("Plants shorter than this are under the flood (the ground is a metre down), so they are only used as floating scraps.")]
    public float minStandingHeight = 1.3f;
    [Tooltip("Metres kept clear of the edge of every road: the streets are the boat's lanes.")]
    public float roadMargin = 1.5f;
    [Tooltip("Metres kept clear round every building.")]
    public float buildingMargin = 1f;
    [Tooltip("A cell this close to a building counts as a yard; further than this it is an open lot.")]
    public float yardReach = 10f;
    [Tooltip("A cell this close to a waterway counts as its bank.")]
    public float bankReach = 8f;
    [Tooltip("Width of the roadside strip, beyond the road margin.")]
    public float roadsideBand = 5f;

    [Header("Clumping")]
    [Tooltip("Size (metres) of the patches plants gather in, with gaps between.")]
    public float clumpSize = 26f;
    [Range(0f, 1f)] public float clumpContrast = 0.6f;

    public int seed = 4127;

    public float DensityFor(FoliageHabitat h)
    {
        switch (h)
        {
            case FoliageHabitat.Green: return greenDensity;
            case FoliageHabitat.Farm: return farmDensity;
            case FoliageHabitat.Cemetery: return cemeteryDensity;
            case FoliageHabitat.Bank: return bankDensity;
            case FoliageHabitat.OpenLot: return openLotDensity;
            case FoliageHabitat.Yard: return yardDensity;
            case FoliageHabitat.Roadside: return roadsideDensity;
            default: return 0f;
        }
    }

    /// <summary>The species tall enough to stand in the flood that grow in this place.</summary>
    public List<int> StandingIn(FoliageHabitat h)
    {
        var list = new List<int>();
        for (int i = 0; i < species.Count; i++)
        {
            var s = species[i];
            if (s.prefab == null || s.weight <= 0f || (s.habitats & h) == 0) continue;
            if (s.size.y < minStandingHeight) continue;
            list.Add(i);
        }
        return list;
    }

    public FoliageSpecies Find(string code) => species.Find(s => s.code == code);
}
