using System.Collections.Generic;
using UnityEngine;

/// <summary>Which part of the barangay the placeholder map covers.</summary>
public enum MockRegion
{
    /// <summary>The built-up centre around the hall, school, hospital and market (the layout's focus area).</summary>
    FocusArea,
    /// <summary>The focus area and its margin, where the data is complete outside the barangay too.</summary>
    DetailArea,
    /// <summary>All of Mabuhay: about 4.3 x 2.4 km and over 2,300 buildings. Heavy.</summary>
    WholeBarangay,
    Custom
}

/// <summary>
/// What Bayanihan > Build Barangay Mock builds: from which layout, with which stand-ins, over which part of the barangay,
/// at what size, with which layers. The map is always built at real size and scaled as a whole, so a scale model has the
/// same shapes as the real thing.
///
/// Lives in Assets/Settings/Maps. Make it with Bayanihan > Make Placeholder Prefabs, or Create > Bayanihan.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/Barangay Mock Settings", fileName = "BarangayMockSettings")]
public class BarangayMockSettings : ScriptableObject
{
    [Header("What to build from")]
    [Tooltip("Leave empty for the layout that ships (Resources/Layouts).")]
    public BarangayLayout layout;
    [Tooltip("The models that stand in for houses and landmarks.")]
    public PlaceholderLibrary library;

    [Header("Where")]
    public MockRegion region = MockRegion.FocusArea;
    [Tooltip("The region for Custom, in real metres east (x) and north (y) of the barangay hall.")]
    public Rect customRegion = new Rect(-150f, -150f, 300f, 300f);
    [Tooltip("Ground and roads continue this far past the region, in metres.")]
    [Min(0f)] public float margin = 30f;
    [Tooltip("Game metres per real metre. 1 is real size; the whole map is scaled by this.")]
    [Min(0.05f)] public float scale = 1f;
    [Tooltip("Height of the ground, in metres. 0 = the ground is at y = 0. A map to play on uses -1, so water at y = 0 " +
             "floods its streets about a metre deep, as it does in the Gameplay scene.")]
    public float groundHeight;

    [Header("Layers")]
    public bool ground = true;
    public bool areas = true;
    public bool water = true;
    public bool roads = true;
    public bool buildings = true;
    public bool landmarks = true;
    public bool signs = true;
    [Tooltip("Give the ground, blocks and houses colliders, so a boat or a person can be tested on it. They are given a " +
             "slippery surface (no friction), so a boat that runs into a wall glances off it instead of snagging.")]
    public bool colliders;

    [Header("Where it goes")]
    [Tooltip("The scene the map is built in. It is never Gameplay.")]
    public string scenePath = "Assets/Scenes/Maps/MabuhayMock.unity";
    [Tooltip("Where the generated meshes are saved, so the scene stays small.")]
    public string generatedFolder = "Assets/Generated/BarangayMock";

    /// <summary>The layout to build from: the one set here, otherwise the one that ships.</summary>
    public BarangayLayout Layout => layout != null ? layout : BarangayLayout.LoadShipped();

    /// <summary>The part of the barangay to cover, in real metres.</summary>
    public Rect Region(BarangayLayout map)
    {
        switch (region)
        {
            case MockRegion.DetailArea:    return map.detailArea;
            case MockRegion.WholeBarangay: return map.Bounds;
            case MockRegion.Custom:        return customRegion;
            default:                       return map.focusArea;
        }
    }
}
