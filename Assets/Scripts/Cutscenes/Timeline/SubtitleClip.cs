using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>A spoken line, shown by the cutscene overlay while the clip plays (if Subtitles are on).</summary>
[Serializable]
public class SubtitleClip : PlayableAsset, ITimelineClipAsset
{
    /// <summary>Who is speaking, shown before the line ("CAPTAIN"). Empty for narration.</summary>
    public string speaker;
    /// <summary>The spoken line (Tagalog in the story intro).</summary>
    [TextArea] public string line;
    /// <summary>The English subtitle under it. Empty when the line is already English.</summary>
    [TextArea] public string translation;
    public ClipCaps clipCaps => ClipCaps.None;

    /// <summary>What the overlay shows: "SPEAKER: line" and, under it in italics, the translation.</summary>
    public string Compose()
    {
        string text = string.IsNullOrEmpty(speaker) ? line : speaker + ": " + line;
        if (!string.IsNullOrEmpty(translation)) text += "\n<i>" + translation + "</i>";
        return text;
    }

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<SubtitleBehaviour>.Create(graph);
        p.GetBehaviour().line = Compose();
        return p;
    }
}
