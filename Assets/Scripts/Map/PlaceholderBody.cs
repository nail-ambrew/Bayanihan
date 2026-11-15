using UnityEngine;

/// <summary>
/// Put on the root of a placeholder prefab by the baker: how big the building's body is, for fitting it to a footprint.
/// Just the walls, without eaves, steps or signs, so a house fills its plot rather than overhanging it.
/// </summary>
public class PlaceholderBody : MonoBehaviour
{
    [Tooltip("Width (x), height (y) and depth (z) of the body, in metres.")]
    public Vector3 size;
}
