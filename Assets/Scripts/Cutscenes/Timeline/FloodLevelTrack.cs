using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Drives <see cref="FloodLevel"/>; overlapping clips blend.</summary>
[TrackColor(0.36f, 0.27f, 0.16f)]
[TrackClipType(typeof(FloodLevelClip))]
public class FloodLevelTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
        ScriptPlayable<FloodLevelMixer>.Create(graph, inputCount);
}

public class FloodLevelMixer : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (RampBehaviour.Mix(playable, out float v)) FloodLevel.Set(v);
    }
}
