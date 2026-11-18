using UnityEngine;

/// <summary>The barangay hall's emergency light: a red lamp whose beam turns while it is on. Cue_Siren_On / _Off.</summary>
public class SirenLight : MonoBehaviour
{
    public Light beam;
    public Renderer lamp;
    public float degreesPerSecond = 360f;
    public bool on;

    static readonly int Emission = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;

    public void SetOn(bool value)
    {
        on = value;
        if (beam != null) beam.enabled = value;
    }

    void Update()
    {
        if (beam != null) beam.enabled = on;
        if (on) transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.World);
        if (lamp != null)
        {
            block ??= new MaterialPropertyBlock();
            float pulse = on ? 0.6f + 0.4f * Mathf.Sin(Time.time * 12f) : 0f;
            block.SetColor(Emission, new Color(1f, 0.1f, 0.05f) * (pulse * 4f));
            lamp.SetPropertyBlock(block);
        }
    }
}
