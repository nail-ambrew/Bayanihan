using UnityEngine;

/// <summary>
/// Puts one resident from the mission's roster into the world: the Survivor, their condition, and the character that
/// represents them, set up for the situation they are in. Everything is built in code, like the player and the
/// weather, so there is nothing to wire up in the scene except a place for them to stand.
/// </summary>
public static class ResidentSpawner
{
    /// <summary>How far from the pole somebody clinging to it hangs on (metres).</summary>
    public const float ClingDistance = 0.55f;

    public static Survivor Spawn(ResidentSpec spec, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        // Built inactive so that every field is set before any component's Awake looks at it.
        var go = new GameObject("Resident " + spec.displayName);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);

        var trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 4f;                    // the pick-up range around them

        var condition = go.AddComponent<SurvivorCondition>();       // brings the Survivor with it
        condition.startCondition = spec.startCondition;

        var survivor = go.GetComponent<Survivor>();
        survivor.residentName = spec.displayName;
        survivor.household = spec.household;
        survivor.situation = spec.situation;
        survivor.needs = spec.needs;

        var visual = go.AddComponent<SurvivorVisual>();
        if (!string.IsNullOrEmpty(spec.characterId)) visual.characterOverride = spec.characterId;
        else visual.categories = new[] { spec.category };

        switch (spec.situation)
        {
            case Situation.OpenWater:
                visual.followWaves = true;
                visual.drift = DriftMode.Always;            // carried along by the flood
                break;

            case Situation.Clinging:
                visual.followWaves = true;
                visual.drift = DriftMode.Never;             // holding on to something fixed
                visual.randomFacing = false;                // facing the pole
                visual.clingToPost = true;
                visual.postAxis = position + rotation * Vector3.forward * ClingDistance;
                break;

            case Situation.Rooftop:
                visual.followWaves = false;                 // on a roof, not in the water
                visual.poseOverride = RestingPoseMode.Standing;
                visual.randomFacing = false;
                break;

            case Situation.Trapped:
                visual.followWaves = true;
                visual.drift = DriftMode.Never;             // wedged behind the debris
                break;
        }

        go.SetActive(true);
        return survivor;
    }
}
