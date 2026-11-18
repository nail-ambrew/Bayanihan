using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>The shared <see cref="FloodLevel"/> (metres from the flood's start height), eased from `from` to `to` over the clip.</summary>
[Serializable]
public class FloodLevelClip : PlayableAsset, ITimelineClipAsset
{
    public float from = FloodLevel.Dry;
    public float to = 0f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<RampBehaviour>.Create(graph);
        var b = p.GetBehaviour(); b.from = from; b.to = to; b.curve = curve;
        return p;
    }
}
