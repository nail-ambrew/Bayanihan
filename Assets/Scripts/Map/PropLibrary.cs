using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The street clutter Barangay Mabuhay is filled with, from the packs in Assets/3D Models: jeepneys, corrugated roof
/// sheets, spring buckets, hollow blocks and barriers, and the people of the Filipino assembly pack. Filled by
/// Bayanihan > Props > Import Packs; placed by PropBuilder. Change a number here and rebuild the map: nothing is placed by hand.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Prop Library", fileName = "PropLibrary")]
public class PropLibrary : ScriptableObject
{
    [Header("Models (filled by the importer)")]
    public List<GameObject> jeepneys = new List<GameObject>();
    public List<GameObject> roofSheets = new List<GameObject>();
    public List<GameObject> buckets = new List<GameObject>();
    [Tooltip("Hollow-block stacks, a jersey barrier and rubble piles, drawn in code from the concrete pack's colours.")]
    public List<GameObject> blockStacks = new List<GameObject>();
    public GameObject barrier;
    public List<GameObject> rubble = new List<GameObject>();
    [Tooltip("The residents and rescuers of the assembly pack: standing figures, no collider.")]
    public List<GameObject> people = new List<GameObject>();
    public List<GameObject> rescuers = new List<GameObject>();

    [Header("How many")]
    public int jeepneyCount = 16;
    [Tooltip("Metres between two parked jeepneys at least.")]
    public float jeepneyGap = 45f;
    [Tooltip("Share of shacks that have loose roof sheets leaning on a wall.")]
    [Range(0f, 1f)] public float roofSheetShare = 0.18f;
    [Tooltip("Share of houses with buckets set out on the roof (water, belongings) above the flood.")]
    [Range(0f, 1f)] public float roofBucketShare = 0.08f;
    [Tooltip("Share of houses with a stack of hollow blocks or a rubble pile beside them.")]
    [Range(0f, 1f)] public float concreteShare = 0.07f;
    public int barrierRuns = 10;
    [Tooltip("Evacuees and rescuers waiting on the hall's porch and onlookers along the levee.")]
    public int porchPeople = 10;
    public int leveeGroups = 8;

    public int seed = 7319;
}
