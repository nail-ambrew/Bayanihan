using UnityEngine;

/// <summary>
/// How a resident is holding up. It drains while they wait and stops once they are aboard (see RescueRules for how
/// fast, and why). It is what makes the order of the rescues matter.
///
/// Nobody dies: at zero they are critical, which means they cannot be pulled aboard without the first-aid hold and
/// they are worth half.
///
/// Added next to the Survivor script. The Gameplay scene's residents get it from MissionBuilder.
/// </summary>
[RequireComponent(typeof(Survivor))]
public class SurvivorCondition : MonoBehaviour
{
    [Tooltip("1 = fine, 0 = critical.")]
    [Range(0f, 1f)] public float startCondition = 1f;

    /// <summary>1 = fine, 0 = critical.</summary>
    public float Condition01 { get; private set; } = 1f;
    public bool IsCritical => Condition01 <= 0f;
    /// <summary>Condition lost, plus how vulnerable they are. The tag is read off this.</summary>
    public float Urgency { get; private set; }
    public Triage Triage { get; private set; }
    /// <summary>How much condition they are losing per minute right now (for the HUD).</summary>
    public float DrainPerMinute { get; private set; }

    Survivor survivor;
    SurvivorVisual visual;
    TyphoonWeather weather;

    RescueRules Rules => MissionDefinition.Current.rules;

    void Awake()
    {
        survivor = GetComponent<Survivor>();
        Condition01 = Mathf.Clamp01(startCondition);
        Refresh();
    }

    void Start()
    {
        visual = GetComponent<SurvivorVisual>();
        weather = FindFirstObjectByType<TyphoonWeather>();
        Refresh();
    }

    void Update()
    {
        if (survivor.IsRescued || Condition01 <= 0f) return;

        float rise = FloodRise.Instance != null ? FloodRise.Instance.Rise : 0f;
        float storm = weather != null ? weather.intensity : 0f;
        DrainPerMinute = Rules.DrainPerMinute(survivor.situation, Category, survivor.Injured, survivor.InLiveWire,
                                              survivor.IsHeld, storm, rise);
        Condition01 = Mathf.Max(0f, Condition01 - DrainPerMinute / 60f * Time.deltaTime);
        Refresh();
    }

    CharacterCategory Category =>
        visual != null && visual.Character != null ? visual.Character.category : CharacterCategory.Adult;

    void Refresh()
    {
        var rules = Rules;
        Urgency = rules.Urgency(Condition01, survivor.situation, Category, survivor.Injured);
        Triage = rules.TriageOf(Urgency, IsCritical);
    }

    /// <summary>For tests and scripted scenes: set the condition directly.</summary>
    public void SetCondition(float value)
    {
        Condition01 = Mathf.Clamp01(value);
        Refresh();
    }
}
