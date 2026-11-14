using System.Collections.Generic;
using UnityEngine;

/// <summary>One place the flood-rescue mission can be played: the scene it is in, and the mission that goes with it.</summary>
public sealed class PlayableMap
{
    public readonly string id;
    /// <summary>The name on the loadout screen's button.</summary>
    public readonly string title;
    /// <summary>The line under it: where this is.</summary>
    public readonly string place;
    /// <summary>The scene to load. It has to be in Build Settings.</summary>
    public readonly string sceneName;
    /// <summary>Where the mission asset is, under a Resources folder (without ".asset"). Null = the mission that ships (Resources/Missions).</summary>
    readonly string missionResource;

    public PlayableMap(string id, string title, string place, string sceneName, string missionResource)
    {
        this.id = id;
        this.title = title;
        this.place = place;
        this.sceneName = sceneName;
        this.missionResource = missionResource;
    }

    /// <summary>The mission to play here, or null if its asset is missing.</summary>
    public MissionDefinition LoadMission() =>
        string.IsNullOrEmpty(missionResource) ? MissionDefinition.LoadShipped() : Resources.Load<MissionDefinition>(missionResource);
}

/// <summary>
/// Which map the player has picked on the loadout screen. Static, so it survives the scene change from Loadout to the mission and
/// back again (Esc on the report): the loadout then opens on the map that was just played.
///
/// The first map, and the one played if nothing is picked, is Barangay Mabuhay (Carmona, Cavite): the real barangay the game
/// is about. The Manila slum street in the Gameplay scene was the team's first sample map; it stays as a second choice for
/// practice. Mabuhay's mission asset lives in Resources/Maps, not in Resources/Missions, because MissionDefinition.LoadShipped
/// takes the first asset in that folder, and that is still Manila's (the Gameplay scene and its tests read it).
/// </summary>
public static class MapChoice
{
    public const string ManilaId = "manila";
    public const string MabuhayId = "mabuhay";
    /// <summary>The sample map's scene. The loadout screen's own "Gameplay Scene Name" says the same.</summary>
    public const string ManilaScene = "Gameplay";
    public const string MabuhayScene = "MabuhayGameplay";
    public const string MabuhayMissionResource = "Maps/MabuhayMission";

    static readonly List<PlayableMap> all = new List<PlayableMap>
    {
        new PlayableMap(MabuhayId, "Mabuhay · Carmona, Cavite", "The real barangay, from OpenStreetMap", MabuhayScene, MabuhayMissionResource),
        new PlayableMap(ManilaId, "Manila · Sample map", "The first sample map: a slum street in Manila", ManilaScene, null)
    };

    static PlayableMap selected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { selected = null; }

    /// <summary>Every map there is to play, the default first.</summary>
    public static IReadOnlyList<PlayableMap> All => all;

    /// <summary>The map picked, the default (Barangay Mabuhay) if none has been.</summary>
    public static PlayableMap Selected => selected ?? all[0];

    /// <summary>True while the default map (Barangay Mabuhay) is the one picked.</summary>
    public static bool IsDefault => Selected == all[0];

    public static PlayableMap Find(string id)
    {
        foreach (var m in all) if (m.id == id) return m;
        return null;
    }

    /// <summary>The map a scene belongs to, or null if it is not a map's scene.</summary>
    public static PlayableMap ForScene(string sceneName)
    {
        foreach (var m in all) if (m.sceneName == sceneName) return m;
        return null;
    }

    /// <summary>
    /// Picks a map, and makes its mission the one in force, so the briefing, the roster and the rules everywhere read the
    /// same mission. Returns false (and changes nothing) if there is no such map.
    /// </summary>
    public static bool Select(string id)
    {
        var map = Find(id);
        if (map == null) return false;
        selected = map;
        MissionDefinition.Current = map.LoadMission();      // null falls back to the one that ships
        return true;
    }

    /// <summary>
    /// Back to the default map, with no mission forced: a scene then plays its own mission (the loadout screen selects the
    /// picked map's mission again when it opens). For tests, and for starting over.
    /// </summary>
    public static void Reset()
    {
        selected = null;
        MissionDefinition.Current = null;
    }
}
