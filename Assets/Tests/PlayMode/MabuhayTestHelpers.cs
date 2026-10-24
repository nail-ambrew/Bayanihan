using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared set-up for the tests that play the Barangay Mabuhay scene: loading it the way the loadout screen does, and what
/// the level promises (where the hall is, which streets the hazards are on). The set-up that is not specific to Mabuhay is
/// GameplayTestHelpers', which works on whichever mission scene is loaded.
/// </summary>
public static class MabuhayTestHelpers
{
    public const string SceneName = MapChoice.MabuhayScene;

    /// <summary>The part of the world the boat is driven in: the west end of Bagumbayan Street, the hall and the lane behind the corner houses.</summary>
    public static readonly Rect Arena = Rect.MinMaxRect(-175f, -120f, 60f, 90f);

    /// <summary>Loads the scene with the Mabuhay map picked, as pressing LAUNCH on the loadout screen does.</summary>
    public static IEnumerator LoadMabuhay()
    {
        Application.runInBackground = true;     // keep simulating if the Editor is not the front window
        Time.timeScale = 1f;
        LoadoutState.Clear();
        MapChoice.Select(MapChoice.MabuhayId);
        yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        yield return null;                      // a frame for every Start()
    }

    /// <summary>Puts every static the map choice leaves behind back to how the game starts, so the next test (Manila, say) sees none of it.</summary>
    public static void Restore()
    {
        MapChoice.Reset();
        LoadoutState.Clear();
        Time.timeScale = 1f;
    }

    /// <summary>The resident the Mabuhay mission calls this (joy, ben...).</summary>
    public static Survivor Resident(string id) => MissionBot.Resident(id);

    public static EvacuationCenter Hall()
    {
        var evac = Object.FindFirstObjectByType<EvacuationCenter>();
        Assert.IsNotNull(evac, "the scene has no evacuation centre");
        return evac;
    }
}
