using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The intro cutscene's place in the game's flow. LAUNCH on the loadout screen calls <see cref="Arm"/> (with Play Intro
/// on and the Mabuhay map chosen); the mission scene then loads as usual behind the loading screen, and the moment it
/// is in, the mission is held (<see cref="CutsceneHold"/>), the screen goes black under the loading screen and
/// Scene_IntroCutscene loads on top of it. The cutscene plays over the real, fully loaded map and hands it back
/// (<see cref="IntroCutscene"/>), so skipping is instant.
///
/// If the intro cannot load or never starts, the mission starts anyway after <see cref="StartTimeout"/> seconds.
/// </summary>
public static class IntroFlow
{
    public const string SceneName = "Scene_IntroCutscene";
    /// <summary>Real seconds the intro has to start before the mission is let go without it.</summary>
    public const float StartTimeout = 8f;

    public static bool Armed { get; private set; }

    /// <summary>Whether LAUNCH into <paramref name="gameplayScene"/> should play the intro first.</summary>
    public static bool ShouldPlay(string gameplayScene) =>
        GameSettings.PlayIntro && gameplayScene == MapChoice.MabuhayScene && Application.CanStreamedLevelBeLoaded(SceneName);

    /// <summary>Play the intro over the next gameplay scene to load.</summary>
    public static void Arm()
    {
        Armed = true;
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    public static void Disarm()
    {
        Armed = false;
        SceneManager.sceneLoaded -= OnLoaded;
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Armed || mode != LoadSceneMode.Single) return;
        Disarm();
        if (scene.name != MapChoice.MabuhayScene) return;
        Begin();
    }

    /// <summary>Holds the mission that has just loaded and loads the intro over it.</summary>
    public static void Begin()
    {
        CutsceneHold.Begin();
        MusicPlayer.Duck(0f, 0.05f);
        ScreenFade.Out(0f);                   // black under the loading screen until the first shot
        var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        if (op == null) { Abort("the intro scene would not load"); return; }
        Runner().StartCoroutine(WatchStart());
    }

    static IEnumerator WatchStart()
    {
        float until = Time.unscaledTime + StartTimeout;
        while (Time.unscaledTime < until)
        {
            if (IntroCutscene.Current != null && IntroCutscene.Current.Started) yield break;
            if (!CutsceneHold.Active) yield break;          // ended some other way
            yield return null;
        }
        Abort("the intro did not start within " + StartTimeout + " s");
    }

    /// <summary>The mission starts without the intro (it is missing or broken).</summary>
    public static void Abort(string why)
    {
        Debug.LogWarning("IntroFlow: " + why + "; starting the mission.");
        CutsceneHold.Release();
        MusicPlayer.Unduck(1f);
        ScreenFade.In(0.4f);
    }

    static RunnerHost runner;

    static RunnerHost Runner()
    {
        if (runner != null) return runner;
        var go = new GameObject("IntroFlow");
        Object.DontDestroyOnLoad(go);
        runner = go.AddComponent<RunnerHost>();
        return runner;
    }

    class RunnerHost : MonoBehaviour { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Armed = false;
        runner = null;
        SceneManager.sceneLoaded -= OnLoaded;
    }
}
