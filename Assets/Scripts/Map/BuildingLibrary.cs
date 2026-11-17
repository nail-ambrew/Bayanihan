using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One infill building model: what it is, how tall, and the lot it needs.</summary>
[Serializable]
public class InfillModel
{
    public string name;
    public GameObject prefab;
    public bool commercial;
    [Range(1, 3)] public int storeys = 1;
    [Tooltip("Lot it stands on, metres: x across the front, z deep (fence and front yard included).")]
    public Vector2 footprint = new Vector2(7f, 9f);
    public int triangles;
}

/// <summary>
/// The low-poly houses, row houses, apartments, shops and mixed-use units the Mabuhay map's empty land is filled with
/// (Source Assets/Buildings, made by make_buildings.py; prefabs by Bayanihan > Buildings > Import Building Kit). Placed by
/// InfillBuilder at the 70/20/10 storey mix and about 70/30 residential/commercial.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Building Library", fileName = "BuildingLibrary")]
public class BuildingLibrary : ScriptableObject
{
    public List<InfillModel> models = new List<InfillModel>();

    [Header("Mix (by building count)")]
    [Range(0f, 1f)] public float oneStorey = 0.7f;
    [Range(0f, 1f)] public float twoStorey = 0.2f;
    [Range(0f, 1f)] public float threeStorey = 0.1f;
    [Range(0f, 1f)] public float commercialShare = 0.3f;

    [Header("Placement (metres on the layout)")]
    [Tooltip("Front setback from the road's edge to the lot (the lot's own front yard is inside its footprint).")]
    public Vector2 setback = new Vector2(1.5f, 3f);
    [Tooltip("Gap between neighbouring lots.")]
    public Vector2 sideGap = new Vector2(1f, 2f);
    [Tooltip("An alley this wide is left every few lots.")]
    public float alley = 2.5f;
    public int lotsPerAlley = 6;
    [Tooltip("Metres kept round the protected zones (WalterMart, Macaria Business Center).")]
    public float protectedBuffer = 5f;
    [Tooltip("Random size variation of a placed building, each way.")]
    [Range(0f, 0.2f)] public float sizeJitter = 0.1f;
    public int seed = 20261117;

    public float Target(int storeys) => storeys == 1 ? oneStorey : storeys == 2 ? twoStorey : threeStorey;
}
