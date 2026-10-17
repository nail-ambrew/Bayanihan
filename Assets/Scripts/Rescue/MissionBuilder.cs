using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sets the Gameplay scene up from the mission: the clock, the storm, the flood's rise, and every resident on the
/// roster, standing where the scene's anchor objects say. Put it on the same object as the RescueManager.
///
/// An anchor is any object in the scene named after a resident's Anchor field (Anchor_joy...). Its position is where
/// they wait, and the way it faces is the way they look. Move the anchors to move the residents; the mission asset
/// says everything else about them.
/// </summary>
[DefaultExecutionOrder(-50)]     // the residents exist before anything else looks for them
public class MissionBuilder : MonoBehaviour
{
    [Tooltip("The mission to play. Empty = the one in Resources/Missions.")]
    public MissionDefinition mission;
    [Tooltip("Parent for the residents that are built. Empty = a new object.")]
    public Transform residentsRoot;

    public static MissionBuilder Instance { get; private set; }

    /// <summary>Everybody the mission put into the world, in roster order.</summary>
    public List<Survivor> Residents { get; } = new List<Survivor>();

    void OnEnable() { Instance = this; }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Awake()
    {
        var def = mission != null ? mission : MissionDefinition.Current;
        MissionDefinition.Current = def;

        ApplyRules(def);
        BuildResidents(def);
    }

    /// <summary>The clock, the storm and the flood.</summary>
    void ApplyRules(MissionDefinition def)
    {
        var manager = GetComponent<RescueManager>();
        if (manager != null) manager.timeLimit = def.timeLimit;

        var weather = FindFirstObjectByType<TyphoonWeather>();
        if (weather != null) weather.SetIntensity(def.stormIntensity);

        if (def.waterRisePerMinute > 0f)
        {
            var rise = FindFirstObjectByType<FloodRise>();
            if (rise == null) rise = gameObject.AddComponent<FloodRise>();
            rise.risePerMinute = def.waterRisePerMinute;
            rise.maxRise = def.maxWaterRise;
        }
    }

    void BuildResidents(MissionDefinition def)
    {
        if (def.residents == null || def.residents.Count == 0) return;

        if (residentsRoot == null)
        {
            var root = new GameObject("Residents");
            root.transform.SetParent(transform.parent, false);
            residentsRoot = root.transform;
        }

        foreach (var spec in def.residents)
        {
            var anchor = FindAnchor(spec);
            if (anchor == null)
            {
                Debug.LogError("MissionBuilder: there is no anchor object called '" + spec.anchor + "' in the scene for " +
                               spec.displayName + ". They will not be in the mission.", this);
                continue;
            }
            Residents.Add(ResidentSpawner.Spawn(spec, anchor.position, anchor.rotation, residentsRoot));
        }
    }

    static Transform FindAnchor(ResidentSpec spec)
    {
        if (string.IsNullOrEmpty(spec.anchor)) return null;
        var go = GameObject.Find(spec.anchor);
        return go != null ? go.transform : null;
    }
}
