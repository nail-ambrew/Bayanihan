using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BayanihanHQ.Editor
{
    /// <summary>
    /// The "Bayanihan HQ" menu. The office scene is built from code, like the game's own scenes, and is never added to
    /// Build Settings: the HQ is an Editor-only dev tool.
    /// </summary>
    public static class HqMenu
    {
        public const string ScenePath = "Assets/_BayanihanHQ/Scenes/BayanihanHQ.unity";

        /// <summary>Opens the office scene (building it first if it doesn't exist yet).</summary>
        [MenuItem("Bayanihan HQ/Open Office", priority = 1)]
        public static void OpenOffice()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) OfficeSceneBuilder.Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Rebuilds BayanihanHQ.unity from code.</summary>
        [MenuItem("Bayanihan HQ/Build Office Scene", priority = 2)]
        public static void BuildOffice()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            OfficeSceneBuilder.Build();
        }

        /// <summary>Opens the office and presses Play in Simulate mode, for demos.</summary>
        [MenuItem("Bayanihan HQ/Play Demo (Simulate)", priority = 20)]
        public static void PlayDemo()
        {
            OpenOffice();
            SessionState.SetBool(SimulateKey, true);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Shows the hook log in Finder.</summary>
        [MenuItem("Bayanihan HQ/Reveal Event Log", priority = 40)]
        public static void RevealLog()
        {
            var path = HqReplay.DefaultLogPath(Application.dataPath);
            EditorUtility.RevealInFinder(File.Exists(path) ? path : Path.GetDirectoryName(path));
        }

        internal const string SimulateKey = "BayanihanHQ.SimulateOnPlay";

        [InitializeOnLoadMethod]
        static void WatchPlayMode()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SimulateKey, false)) return;
                SessionState.SetBool(SimulateKey, false);
                var director = Object.FindAnyObjectByType<HqDirector>();
                if (director != null && !director.Simulating) director.SetSimulate(true);
            };
        }
    }
}
