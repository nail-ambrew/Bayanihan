using UnityEngine;

/// <summary>
/// The flood climbing. It raises the WaterSurface a little every minute, and everything that floats or reads the water
/// height - the boat, the swimmer, residents, debris - goes up with it. The briefing is called "The Water Rises"; this
/// is what makes that true. It also makes anybody still low down wear out faster (see RescueRules).
///
/// MissionBuilder adds it from the mission's settings. Set the rate to 0 and the water holds steady.
/// </summary>
public class FloodRise : MonoBehaviour
{
    public static FloodRise Instance { get; private set; }

    [Tooltip("Metres the water climbs each minute.")]
    public float risePerMinute = 0.1f;
    [Tooltip("It stops climbing here (metres above where it started).")]
    public float maxRise = 0.6f;

    /// <summary>How far the water has climbed since the mission began, in metres.</summary>
    public float Rise { get; private set; }

    float baseY;
    bool started;

    void OnEnable() { Instance = this; }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        var water = WaterSurface.Instance;
        if (water == null) return;

        // Remember where the water started the first time we can see it, not when this object was made.
        if (!started)
        {
            baseY = water.transform.position.y;
            started = true;
        }

        if (risePerMinute > 0f && Rise < maxRise)
            Rise = Mathf.Min(maxRise, Rise + risePerMinute / 60f * Time.deltaTime);

        Vector3 p = water.transform.position;
        p.y = baseY + Rise;
        water.transform.position = p;
    }

    void OnDestroy()
    {
        // A scene that goes on without this (a retry reloads everything anyway) should not be left with high water.
        if (started && WaterSurface.Instance != null)
        {
            Vector3 p = WaterSurface.Instance.transform.position;
            p.y = baseY;
            WaterSurface.Instance.transform.position = p;
        }
    }
}
