using UnityEngine;

/// <summary>
/// Sits on the root of a built placeholder map and says what it was built from and what is in it. Nothing reads it at
/// run time: it is there so anyone who opens the scene knows how it got there.
/// </summary>
public class BarangayMockInfo : MonoBehaviour
{
    public BarangayMockSettings settings;
    [Tooltip("The revision of the layout it was built from. If the layout has moved on, rebuild the map.")]
    public int layoutRevision;
    [TextArea(2, 8)] public string summary;
    public int houses;
    public int blocks;
    public int landmarks;
}
