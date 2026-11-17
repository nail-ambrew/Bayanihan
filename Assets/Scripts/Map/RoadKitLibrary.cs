using UnityEngine;

/// <summary>
/// The RoadKit pieces the Mabuhay map is dressed with (Assets/3D Models/RoadKit, made into prefabs by
/// Bayanihan > Roads > Import RoadKit): its road materials, the bridges laid over the map's bridge ways, the utility poles,
/// wires, lamps, signs and traffic lights along the streets, and the fence panels round commercial lots.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/RoadKit Library", fileName = "RoadKitLibrary")]
public class RoadKitLibrary : ScriptableObject
{
    [Header("Road surface materials (the kit's own)")]
    public Material asphalt;
    public Material concrete;
    public Material sidewalk;
    public Material dirt;
    public Material paintWhite;
    public Material paintYellow;

    [Header("Bridges")]
    public GameObject bridge2Lane;
    public GameObject bridge4Lane;
    public GameObject bridgeWood1Lane;
    [Tooltip("For the footpaths that cross a road or a creek on a bridge.")]
    public GameObject footbridge;

    [Header("Along the streets")]
    public GameObject utilityPole;
    public GameObject utilityPoleConcrete;
    public GameObject wireSpan;
    public GameObject lamp;
    public GameObject stopSign;
    public GameObject streetNameSign;
    public GameObject trafficLight;
    public GameObject crosswalk7;

    [Header("Fences")]
    public GameObject metalFence;

    [Header("How much")]
    [Tooltip("World metres between utility poles along a street.")]
    public float poleGap = 32f;
    [Tooltip("World metres between street lamps on the bigger roads.")]
    public float lampGap = 44f;

    public bool Ready => asphalt != null && utilityPole != null;
}
