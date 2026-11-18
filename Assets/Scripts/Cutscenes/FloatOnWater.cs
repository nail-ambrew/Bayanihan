using UnityEngine;

/// <summary>
/// Keeps a cutscene object on the flood's surface (WaterSurface.GetHeight) with a gentle roll, without physics: a boat
/// on a camera move, a person treading water. The object's x and z are left to whatever moves it.
/// </summary>
[DefaultExecutionOrder(100)]       // after movers like ShotMove have placed it this frame
public class FloatOnWater : MonoBehaviour
{
    [Tooltip("Height above the surface (metres); negative sits lower in the water.")]
    public float offset = 0f;
    [Tooltip("Degrees of roll and pitch the waves give it.")]
    public float rock = 3f;
    float phase;
    Quaternion baseRotation;
    bool captured;

    void Start() => phase = Random.value * 10f;

    void LateUpdate()
    {
        var water = WaterSurface.Instance;
        if (water == null) return;
        var p = transform.position;
        p.y = water.GetHeight(p) + offset;
        transform.position = p;
        if (!captured || transform.hasChanged) { baseRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); captured = true; }
        float t = Time.time + phase;
        transform.rotation = baseRotation * Quaternion.Euler(Mathf.Sin(t * 1.1f) * rock, 0f, Mathf.Sin(t * 0.8f + 1f) * rock);
        transform.hasChanged = false;
    }
}
