using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Subtitles for a cutscene's spoken lines.</summary>
[TrackColor(0.9f, 0.9f, 0.9f)]
[TrackClipType(typeof(SubtitleClip))]
public class SubtitleTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
        ScriptPlayable<SubtitleMixer>.Create(graph, inputCount);
}

public class SubtitleBehaviour : PlayableBehaviour { public string line; }

public class SubtitleMixer : PlayableBehaviour
{
    string shown;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        string line = null;
        for (int i = 0; i < playable.GetInputCount(); i++)
            if (playable.GetInputWeight(i) > 0.5f)
                line = ((ScriptPlayable<SubtitleBehaviour>)playable.GetInput(i)).GetBehaviour().line;
        if (line == shown) return;
        shown = line;
        if (CutsceneOverlay.Current != null) CutsceneOverlay.Current.ShowSubtitle(line);
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (shown != null && CutsceneOverlay.Current != null) CutsceneOverlay.Current.ShowSubtitle(null);
    }
}
