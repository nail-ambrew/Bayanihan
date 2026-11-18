using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Drives the bound TyphoonWeather's intensity frame by frame.</summary>
[TrackColor(0.35f, 0.45f, 0.6f)]
[TrackClipType(typeof(StormClip))]
[TrackBindingType(typeof(TyphoonWeather))]
public class StormTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
        ScriptPlayable<StormMixer>.Create(graph, inputCount);
}

public class StormMixer : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var weather = playerData as TyphoonWeather;
        if (weather == null || !Application.isPlaying) return;
        if (RampBehaviour.Mix(playable, out float v)) weather.SnapIntensity(v);
    }
}
