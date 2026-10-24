using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/Maps/MabuhayGameplay.unity: the flood-rescue mission played over the placeholder map of Barangay
/// Mabuhay, Carmona. The map is the same one Bayanihan > Build Barangay Mock makes (BarangayMockBuilder), built from its
/// own settings with colliders on and the ground a metre below the water, as the Manila map is in the Gameplay scene.
/// Everything else the mission needs is copied from the Gameplay scene the first time, and left alone after that:
/// the camera rig, light, volume, water, current, storm, boat, swimmer and the rescue manager. Gameplay itself is only
/// ever read, never changed.
///
/// What is put where comes from MabuhayLevel, which works from the layout and the map's colliders, not from how the
/// placeholder models look. So when the final models are swapped in, run this again and the hall landing, the
/// roofs the residents stand on, the posts and the hazards are put back on whatever is there.
///
/// The whole thing is rebuilt by Bayanihan > Build Mabuhay Gameplay Scene. Anything of your own under BarangayMock,
/// Mission Anchors, Hazards, Posts or World Edge is deleted by it.
/// </summary>
public static class MabuhayGameplayBuilder
{
    public const string ScenePath = "Assets/Scenes/Maps/MabuhayGameplay.unity";
    public const string SettingsPath = PlaceholderBaker.SettingsFolder + "/BarangayMockSettings_Gameplay.asset";
    public const string GeneratedFolder = "Assets/Generated/MabuhayGameplay";
    const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

    [MenuItem("Bayanihan/Build Mabuhay Gameplay Scene")]
    static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string result = BuildAndSave(true);
        Debug.Log("MabuhayGameplayBuilder: " + result);
    }

    [MenuItem("Bayanihan/Build Mabuhay Gameplay Scene", true)]
    static bool BuildValidate() => !Application.isPlaying;

    // ------------------------------------------------------------------ the settings the play map is built from

    /// <summary>The map's own settings, made from the default ones the first time: colliders on, a metre under the water.</summary>
    public static BarangayMockSettings EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(SettingsPath);
        if (settings == null)
        {
            var defaults = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(PlaceholderBaker.SettingsPath);
            if (defaults == null) throw new InvalidOperationException("There are no mock settings yet. Run Bayanihan > Make Placeholder Prefabs first.");
            PlaceholderBaker.EnsureFolder(PlaceholderBaker.SettingsFolder);
            settings = ScriptableObject.CreateInstance<BarangayMockSettings>();
            EditorUtility.CopySerialized(defaults, settings);
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        // These are the point of this asset: they are put back every time, so nobody edits them by accident.
        settings.region = MockRegion.FocusArea;
        settings.scale = 1f;
        settings.groundHeight = -1f;
        settings.colliders = true;
        settings.scenePath = ScenePath;
        settings.generatedFolder = GeneratedFolder;
        if (settings.library == null)
        {
            var defaults = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(PlaceholderBaker.SettingsPath);
            settings.library = defaults != null ? defaults.library : AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>(PlaceholderBaker.LibraryPath);
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    // ------------------------------------------------------------------ building

    /// <summary>Builds the scene and saves it, with no dialogs when <paramref name="interactive"/> is false.</summary>
    public static string BuildAndSave(bool interactive = false)
    {
        BarangayMockSettings settings;
        try { settings = EnsureSettings(); }
        catch (InvalidOperationException e) { return "Cannot build: " + e.Message; }

        string problem = OpenScene(interactive);
        if (problem != null) return "Cannot build: " + problem;
        settings = EnsureSettings();            // switching scenes unloads unused assets, so load them, and what they point at, again

        var scene = SceneManager.GetActiveScene();
        if (UnityEngine.Object.FindFirstObjectByType<SimpleBoat>() == null) ImportCrew(scene);

        var root = BarangayMockBuilder.Build(settings);
        if (root == null) return "Cannot build: see the Console.";

        var mission = MabuhayMissionBuilder.Build();
        var level = MabuhayLevelBuilder.Build(scene, settings, mission);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        foreach (var trouble in level.problems) Debug.LogWarning("MabuhayGameplayBuilder: " + trouble);
        return root.GetComponent<BarangayMockInfo>().summary + "\n" + level;
    }

    /// <summary>The scene has to be in Build Settings to be loaded by name, and for R on the report to reload it.</summary>
    public static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        var existing = scenes.FindIndex(sc => sc.path == ScenePath);
        if (existing >= 0)
        {
            if (scenes[existing].enabled) return;
            scenes[existing] = new EditorBuildSettingsScene(ScenePath, true);
        }
        else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Opens the play scene, making it if it is not there. Returns null if it is now the active scene, or why not.</summary>
    static string OpenScene(bool interactive)
    {
        if (SceneManager.GetActiveScene().path == ScenePath) return null;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.isDirty && !interactive) return "scene '" + open.name + "' has unsaved changes. Save or discard them first.";
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        else
        {
            PlaceholderBaker.EnsureFolder(System.IO.Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            var created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(created, ScenePath);
        }
        return null;
    }

    // ------------------------------------------------------------------ the crew: copied from Gameplay, once

    /// <summary>The things in the Gameplay scene that are not the map or the mission's places: found by what they are, not by name.</summary>
    static bool IsPartOfTheCrew(GameObject root)
    {
        // The editor assembly does not reference Cinemachine or the render pipeline, so the camera rig and the volume are found by name.
        bool packageBehaviour = root.GetComponents<MonoBehaviour>().Any(m => m != null && (m.GetType().Name == "CinemachineCamera" || m.GetType().Name == "Volume"));
        return root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null || packageBehaviour ||
               root.GetComponent<WaterSurface>() != null ||
               root.GetComponent<RescueManager>() != null || root.GetComponent<EvacuationCenter>() != null ||
               root.GetComponent<SimpleBoat>() != null || root.GetComponent<PlayerRescuer>() != null ||
               root.GetComponent<FloodCurrent>() != null || root.GetComponent<TyphoonWeather>() != null;
    }

    /// <summary>
    /// Copies the camera rig, light, volume, water, current, storm, boat, swimmer, rescue manager and evacuation centre out
    /// of the Gameplay scene, with the scene's sky and fog. Gameplay is opened beside this scene, read, and closed again
    /// without being saved. The copies keep every setting, and the references between them are pointed at each other.
    /// </summary>
    static void ImportCrew(Scene target)
    {
        var gameplay = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
        try
        {
            var wanted = new List<GameObject>();
            foreach (var root in gameplay.GetRootGameObjects())
                if (IsPartOfTheCrew(root)) wanted.Add(root);
            // The landing area the evacuation centre points at goes with it.
            foreach (var centre in wanted.Select(w => w.GetComponent<EvacuationCenter>()).Where(c => c != null).ToList())
                if (centre.landingArea != null && centre.landingArea.parent == null && !wanted.Contains(centre.landingArea.gameObject))
                    wanted.Add(centre.landingArea.gameObject);

            SceneManager.SetActiveScene(gameplay);
            var sky = RenderSettingsCopy.Take();                      // the active scene's settings are the ones in force

            SceneManager.SetActiveScene(target);
            var copies = new Dictionary<GameObject, GameObject>();
            foreach (var original in wanted)
            {
                var copy = UnityEngine.Object.Instantiate(original);
                copy.name = original.name;
                if (copy.GetComponent<PlayerRescuer>() != null) copy.name = "Player";
                if (copy.GetComponent<TyphoonWeather>() != null) copy.name = "Typhoon";
                if (copy.scene != target) SceneManager.MoveGameObjectToScene(copy, target);
                copies[original] = copy;
            }

            RepointReferences(copies);
            sky.Apply();
            var sun = copies.Values.Select(c => c.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);
            if (sun != null) RenderSettings.sun = sun;
        }
        finally
        {
            EditorSceneManager.CloseScene(gameplay, true);
            SceneManager.SetActiveScene(target);
        }
    }

    /// <summary>A reference from one copy to another copied object (the camera's target, the landing area) is made to point at the copy.</summary>
    static void RepointReferences(Dictionary<GameObject, GameObject> copies)
    {
        foreach (var copy in copies.Values)
        {
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                var so = new SerializedObject(component);
                var it = so.GetIterator();
                bool changed = false;
                for (bool enter = true; it.Next(enter); enter = it.propertyType == SerializedPropertyType.Generic)
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var target = it.objectReferenceValue;
                    if (EditorUtility.IsPersistent(target)) continue;

                    Transform t = target is Component c ? c.transform : target is GameObject g ? g.transform : null;
                    if (t == null || t.gameObject.scene == copy.scene) continue;     // already in this scene

                    var original = copies.Keys.FirstOrDefault(o => t.IsChildOf(o.transform));
                    if (original == null)
                    {
                        Debug.LogWarning("MabuhayGameplayBuilder: " + component.GetType().Name + " on " + copy.name + " pointed at '" + t.name +
                                         "' in Gameplay, which is not copied. The reference is cleared.", copy);
                        it.objectReferenceValue = null;
                        changed = true;
                        continue;
                    }

                    var copied = copies[original].transform;
                    var found = t == original.transform ? copied : copied.Find(RelativePath(original.transform, t));
                    UnityEngine.Object replacement = found;
                    if (found != null && target is Component sourceComponent)
                    {
                        var same = found.GetComponents(sourceComponent.GetType());
                        var sameIndex = Array.IndexOf(t.GetComponents(sourceComponent.GetType()), sourceComponent);
                        replacement = sameIndex >= 0 && sameIndex < same.Length ? (UnityEngine.Object)same[sameIndex] : found;
                    }
                    else if (found != null && target is GameObject) replacement = found.gameObject;
                    it.objectReferenceValue = replacement;
                    changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    static string RelativePath(Transform root, Transform child)
    {
        var parts = new List<string>();
        for (var t = child; t != root && t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>The scene's sky, ambient light and fog: what makes a scene look the way it does apart from its objects.</summary>
    sealed class RenderSettingsCopy
    {
        Material skybox;
        UnityEngine.Rendering.AmbientMode ambientMode;
        Color ambientLight, ambientSky, ambientEquator, ambientGround;
        float ambientIntensity, reflectionIntensity;
        bool fog;
        Color fogColor;
        FogMode fogMode;
        float fogDensity, fogStart, fogEnd;
        UnityEngine.Rendering.DefaultReflectionMode reflectionMode;
        Cubemap customReflection;
        Color subtractiveShadow;
        float haloStrength, flareStrength, flareFadeSpeed;

        public static RenderSettingsCopy Take()
        {
            return new RenderSettingsCopy
            {
                skybox = RenderSettings.skybox,
                ambientMode = RenderSettings.ambientMode,
                ambientLight = RenderSettings.ambientLight,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                fog = RenderSettings.fog,
                fogColor = RenderSettings.fogColor,
                fogMode = RenderSettings.fogMode,
                fogDensity = RenderSettings.fogDensity,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                reflectionMode = RenderSettings.defaultReflectionMode,
                customReflection = RenderSettings.customReflectionTexture as Cubemap,
                subtractiveShadow = RenderSettings.subtractiveShadowColor,
                haloStrength = RenderSettings.haloStrength,
                flareStrength = RenderSettings.flareStrength,
                flareFadeSpeed = RenderSettings.flareFadeSpeed
            };
        }

        public void Apply()
        {
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.defaultReflectionMode = reflectionMode;
            if (customReflection != null) RenderSettings.customReflectionTexture = customReflection;
            RenderSettings.subtractiveShadowColor = subtractiveShadow;
            RenderSettings.haloStrength = haloStrength;
            RenderSettings.flareStrength = flareStrength;
            RenderSettings.flareFadeSpeed = flareFadeSpeed;
        }
    }
}
