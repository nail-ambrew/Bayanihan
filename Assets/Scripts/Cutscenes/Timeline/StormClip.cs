using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>The storm's strength (TyphoonWeather intensity, 0..1), eased from `from` to `to` over the clip.</summary>
[Serializable]
public class StormClip : PlayableAsset, ITimelineClipAsset
{
    [Range(0, 1)] public float from = 0f;
    [Range(0, 1)] public float to = 1f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<RampBehaviour>.Create(graph);
        var b = p.GetBehaviour(); b.from = from; b.to = to; b.curve = curve;
        return p;
    }
}
