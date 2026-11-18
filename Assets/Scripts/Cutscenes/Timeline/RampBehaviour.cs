using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>A value eased from one number to another over a clip; the mixers blend overlapping clips by weight.</summary>
public class RampBehaviour : PlayableBehaviour
{
    public float from, to;
    public AnimationCurve curve;

    public float ValueAt(Playable p)
    {
        double d = p.GetDuration();
        float k = d > 0 ? Mathf.Clamp01((float)(p.GetTime() / d)) : 1f;
        return Mathf.LerpUnclamped(from, to, curve != null ? curve.Evaluate(k) : k);
    }

    /// <summary>The weighted value of a mixer's inputs. False if no clip is active (the value is left alone).</summary>
    public static bool Mix(Playable mixer, out float value)
    {
        value = 0f;
        float total = 0f;
        for (int i = 0; i < mixer.GetInputCount(); i++)
        {
            float w = mixer.GetInputWeight(i);
            if (w <= 0f) continue;
            var input = (ScriptPlayable<RampBehaviour>)mixer.GetInput(i);
            value += input.GetBehaviour().ValueAt(input) * w;
            total += w;
        }
        if (total <= 0f) return false;
        value /= total;
        return true;
    }
}
