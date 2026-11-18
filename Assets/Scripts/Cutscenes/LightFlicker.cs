using UnityEngine;

/// <summary>A light that sputters like arcing wire (or a failing bulb). With Reduce Motion it glows steadily instead.</summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    public float baseIntensity = 2f;
    public float spike = 6f;
    [Range(0f, 1f)] public float chance = 0.15f;
    Light lamp;

    void Awake() => lamp = GetComponent<Light>();

    void Update()
    {
        if (GameSettings.ReduceMotion) { lamp.intensity = baseIntensity; return; }
        lamp.intensity = baseIntensity * Random.Range(0.4f, 1f) + (Random.value < chance ? spike : 0f);
    }
}
