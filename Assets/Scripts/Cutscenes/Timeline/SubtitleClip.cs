using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>A spoken line, shown by the cutscene overlay while the clip plays (if Subtitles are on).</summary>
[Serializable]
public class SubtitleClip : PlayableAsset, ITimelineClipAsset
{
    [TextArea] public string line;
    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<SubtitleBehaviour>.Create(graph);
        p.GetBehaviour().line = line;
        return p;
    }
}
