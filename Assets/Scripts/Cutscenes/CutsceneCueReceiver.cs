using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Acts on the Timeline's Signal Emitters by the name of their Signal asset, so beats need no scene events wired up:
/// Cue_Lightning, Cue_Thunder, Cue_TitleHit, Cue_Letterbox_In / _Out, Cue_Splash, Cue_Whoosh, Cue_Siren_On / _Off,
/// Cue_Music_Duck / _Unduck. Anything else is passed to <see cref="Cue"/> listeners (a cutscene's own script).
/// Put it on the PlayableDirector's object; signal emitters on the Timeline's marker track reach it.
/// </summary>
public class CutsceneCueReceiver : MonoBehaviour, INotificationReceiver
{
    [Tooltip("Struck by Cue_Lightning. Found in the scenes if empty.")]
    public TyphoonWeather weather;
    [Tooltip("Played by Cue_Siren_On / _Off (looping).")]
    public AudioSource siren;

    public event Action<string> Cue;

    /// <summary>Every cue received, in order (for tests).</summary>
    public readonly List<string> Received = new List<string>();

    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is SignalEmitter emitter && emitter.asset != null) Fire(emitter.asset.name);
    }

    public void Fire(string cue)
    {
        Received.Add(cue);
        var a = CutsceneAssets.Current;
        switch (cue)
        {
            case "Cue_Lightning":
                if (weather == null) weather = FindFirstObjectByType<TyphoonWeather>();
                if (weather != null) weather.StrikeNow(-1f);           // thunder is its own cue, timed to the cut
                break;
            case "Cue_Thunder":
                if (a.thunder != null && a.thunder.Length > 0)
                    CutsceneAudio.PlayOneShot(a.thunder[UnityEngine.Random.Range(0, a.thunder.Length)], CutsceneAudio.Bus.Sfx);
                break;
            case "Cue_TitleHit":
                if (CutsceneOverlay.Current != null) CutsceneOverlay.Current.ShowTitle();
                break;
            case "Cue_Letterbox_In":
                if (CutsceneOverlay.Current != null) CutsceneOverlay.Current.SetLetterbox(true);
                break;
            case "Cue_Letterbox_Out":
                if (CutsceneOverlay.Current != null) CutsceneOverlay.Current.SetLetterbox(false);
                break;
            case "Cue_Splash": CutsceneAudio.PlayOneShot(a.splash, CutsceneAudio.Bus.Sfx); break;
            case "Cue_Whoosh": CutsceneAudio.PlayOneShot(a.whoosh, CutsceneAudio.Bus.Sfx, 0.7f); break;
            case "Cue_Siren_On": if (siren != null && !siren.isPlaying) siren.Play(); break;
            case "Cue_Siren_Off": if (siren != null) siren.Stop(); break;
            case "Cue_Music_Duck": MusicPlayer.Duck(0.15f, 0.8f); break;
            case "Cue_Music_Unduck": MusicPlayer.Unduck(1.5f); break;
        }
        Cue?.Invoke(cue);
    }
}
