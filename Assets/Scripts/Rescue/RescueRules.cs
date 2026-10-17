using System;
using UnityEngine;

/// <summary>
/// The numbers behind a rescue: how quickly a resident wears down, when that shows as a yellow or red tag, and what
/// a rescue is worth. They live on the MissionDefinition, so each mission can set its own, and they are plain
/// methods on a plain object so they can be tested without a scene.
///
/// Nobody dies. At zero condition a resident is critical: they have to be treated before they can board, and they
/// are worth half. That is the whole penalty for leaving somebody too long.
/// </summary>
[Serializable]
public class RescueRules
{
    [Header("Condition: how much is lost per minute (1 = fine, 0 = critical)")]
    [Tooltip("In the water and moving with the flood.")]
    public float drainOpenWater = 0.28f;
    [Tooltip("Holding on to a pole.")]
    public float drainClinging = 0.20f;
    [Tooltip("Behind debris.")]
    public float drainTrapped = 0.16f;
    [Tooltip("On a roof, out of the water.")]
    public float drainRooftop = 0.08f;
    [Tooltip("Children tire and chill faster.")]
    public float kidFactor = 1.5f;
    public float teenFactor = 1.0f;
    public float adultFactor = 1.0f;
    [Tooltip("So do the old.")]
    public float elderFactor = 1.4f;
    [Tooltip("Somebody who is hurt.")]
    public float injuredFactor = 1.5f;
    [Tooltip("In water with a live wire in it.")]
    public float liveWireFactor = 2f;
    [Tooltip("Holding a ring or a rope that is on its way to the boat: some of the strain is off them.")]
    public float heldFactor = 0.5f;
    [Tooltip("Extra drain at full storm (0.5 = half again as fast).")]
    public float stormExtra = 0.5f;
    [Tooltip("Extra drain for each metre the flood has risen, for anybody still low down.")]
    public float risingWaterExtra = 0.8f;

    [Header("Triage tag: urgency = condition lost + vulnerability")]
    [Tooltip("At or above this urgency, or when critical, the tag is red.")]
    public float redAt = 0.6f;
    public float yellowAt = 0.3f;
    public float kidVulnerability = 0.25f;
    public float elderVulnerability = 0.2f;
    public float injuredVulnerability = 0.2f;
    [Tooltip("The water takes people sooner than a roof does.")]
    public float openWaterExposure = 0.1f;

    [Header("Score")]
    public int pointsPerResident = 100;
    [Tooltip("Multiplier by the tag they had when they were picked up: taking the urgent cases first pays.")]
    public float redMultiplier = 1.5f;
    public float yellowMultiplier = 1.25f;
    public float greenMultiplier = 1f;
    [Tooltip("Critical when picked up: a late rescue. Replaces the tag multiplier.")]
    public float lateMultiplier = 0.5f;
    [Tooltip("Paid per member when every member of a household is registered.")]
    public int householdBonusPerMember = 50;
    [Tooltip("No hull strikes, unsafe passes, rushed pick-ups, shocks or untethered swims.")]
    public int cleanRunBonus = 100;
    [Tooltip("Scaled by how often the most urgent resident that was known about was taken first.")]
    public int priorityBonusMax = 100;

    // ------------------------------------------------------------------ condition

    /// <summary>Condition lost per minute for this resident right now.</summary>
    public float DrainPerMinute(Situation situation, CharacterCategory category, bool injured, bool inLiveWire,
                                bool held, float storm01, float waterRise)
    {
        float rate;
        switch (situation)
        {
            case Situation.OpenWater: rate = drainOpenWater; break;
            case Situation.Clinging:  rate = drainClinging;  break;
            case Situation.Trapped:   rate = drainTrapped;   break;
            default:                  rate = drainRooftop;   break;
        }

        rate *= CategoryFactor(category);
        if (injured) rate *= injuredFactor;
        if (inLiveWire) rate *= liveWireFactor;
        if (held) rate *= heldFactor;
        rate *= 1f + stormExtra * Mathf.Clamp01(storm01);
        if (situation != Situation.Rooftop) rate *= 1f + risingWaterExtra * Mathf.Max(0f, waterRise);
        return rate;
    }

    public float CategoryFactor(CharacterCategory category)
    {
        switch (category)
        {
            case CharacterCategory.Kid:   return kidFactor;
            case CharacterCategory.Teen:  return teenFactor;
            case CharacterCategory.Elder: return elderFactor;
            default:                      return adultFactor;
        }
    }

    // ------------------------------------------------------------------ triage

    /// <summary>How urgently this resident needs taking: what they have lost, plus how vulnerable they are.</summary>
    public float Urgency(float condition, Situation situation, CharacterCategory category, bool injured)
    {
        float u = 1f - Mathf.Clamp01(condition);
        if (category == CharacterCategory.Kid) u += kidVulnerability;
        else if (category == CharacterCategory.Elder) u += elderVulnerability;
        if (injured) u += injuredVulnerability;
        if (situation == Situation.OpenWater) u += openWaterExposure;
        return u;
    }

    public Triage TriageOf(float urgency, bool critical)
    {
        if (critical || urgency >= redAt) return Triage.Red;
        return urgency >= yellowAt ? Triage.Yellow : Triage.Green;
    }

    // ------------------------------------------------------------------ score

    /// <summary>Points for a resident who was picked up with this tag, critical or not.</summary>
    public int Points(Triage tagWhenPickedUp, bool criticalWhenPickedUp)
    {
        float multiplier;
        if (criticalWhenPickedUp) multiplier = lateMultiplier;
        else if (tagWhenPickedUp == Triage.Red) multiplier = redMultiplier;
        else if (tagWhenPickedUp == Triage.Yellow) multiplier = yellowMultiplier;
        else multiplier = greenMultiplier;
        return Mathf.RoundToInt(pointsPerResident * multiplier);
    }

    public int HouseholdBonus(int members) => householdBonusPerMember * Mathf.Max(members, 0);
}
