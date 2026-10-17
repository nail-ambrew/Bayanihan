using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Assets/Resources/CharacterLibrary.asset from the imported character model: one entry per character,
/// with its four clips wired up. Run it after (re)importing filipino_characters_30.glb.
///
/// Everything is derived from the model's own naming, so adding characters to the .glb means re-running this
/// and nothing else: character nodes are "C##_Category_Description" and clips are "C##_Idle/Walk/Float/Swim".
/// </summary>
public static class CharacterLibraryBuilder
{
    const string DefaultModelPath = "Assets/3D Models/filipino_characters_30.glb";
    const string LibraryFolder = "Assets/Resources";
    static string LibraryPath => LibraryFolder + "/" + CharacterLibrary.ResourceName + ".asset";

    [MenuItem("Bayanihan/Rebuild Character Library")]
    public static void Rebuild()
    {
        var modelPath = ResolveModelPath();
        if (modelPath == null)
        {
            EditorUtility.DisplayDialog("Rebuild Character Library",
                "Couldn't find the character model.\n\nSelect the .glb in the Project window and run this again, " +
                "or put it at:\n" + DefaultModelPath, "OK");
            return;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogError("CharacterLibraryBuilder: " + modelPath + " has no GameObject - has it finished importing?");
            return;
        }

        // Clips live as sub-assets of the .glb, named C01_Idle, C01_Walk, ...
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>())
        {
            if (clip == null || clip.name.StartsWith("__preview__")) continue;
            clips[clip.name] = clip;                                  // last one wins; names are unique in practice
        }
        // SampleAnimation - which is how every character in this project is animated - only works on legacy
        // clips. A Mecanim clip needs an Animator and silently ignores sampling, so every character stands in
        // bind pose with no error anywhere. Catch that here rather than at runtime.
        var mecanim = clips.Values.FirstOrDefault(c => !c.legacy);
        if (mecanim != null)
            Debug.LogError("CharacterLibraryBuilder: '" + mecanim.name + "' is not a legacy clip, so " +
                           "SampleAnimation will do nothing and every character will stand in bind pose. " +
                           "Select " + modelPath + " and set the importer's Animation Method to Legacy, " +
                           "then run this again.", model);

        if (clips.Count == 0)
            Debug.LogWarning("CharacterLibraryBuilder: no animation clips in " + modelPath +
                             ". Check the importer's Animation Method is Legacy, not None.", model);

        var library = LoadOrCreateLibrary();
        library.sourceModel = model;
        library.characters = new List<CharacterEntry>();

        int missingClips = 0;
        foreach (var node in CharacterNodes(model.transform).OrderBy(n => n))
        {
            string id = node.Substring(0, node.IndexOf('_'));      // "C07_Teen_NavyHoodie" -> "C07"
            var entry = new CharacterEntry
            {
                id = id,
                nodeName = node,
                category = CategoryOf(node),
                idle = Clip(clips, id, "Idle"),
                walk = Clip(clips, id, "Walk"),
                floating = Clip(clips, id, "Float"),
                swim = Clip(clips, id, "Swim"),
            };
            entry.hipsBindingPath = HipsBindingPath(entry.idle, id);

            if (!entry.IsComplete)
            {
                missingClips++;
                Debug.LogWarning("CharacterLibraryBuilder: " + node + " has no " + id + "_Idle clip - skipped.", model);
                continue;
            }
            library.characters.Add(entry);
        }

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Selection.activeObject = library;
        EditorGUIUtility.PingObject(library);

        var byCategory = library.characters.GroupBy(c => c.category)
                                .Select(g => g.Key + " " + g.Count())
                                .ToArray();
        Debug.Log("CharacterLibraryBuilder: " + library.characters.Count + " characters from " + modelPath +
                  " (" + string.Join(", ", byCategory) + "), " + clips.Count + " clips found" +
                  (missingClips > 0 ? ", " + missingClips + " skipped" : "") + ".", library);
    }

    [MenuItem("Bayanihan/Rebuild Character Library", true)]
    static bool RebuildValidate() => !Application.isPlaying;

    // ------------------------------------------------------------------ helpers
    static string ResolveModelPath()
    {
        // Prefer whatever model the user has selected, so this also works for a renamed or moved .glb.
        foreach (var obj in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && IsModel(path)) return path;
        }
        if (File.Exists(DefaultModelPath)) return DefaultModelPath;

        // Last resort: any model in the project that contains character-shaped nodes.
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsModel(path)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && CharacterNodes(go.transform).Any()) return path;
        }
        return null;
    }

    static bool IsModel(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".glb" || ext == ".gltf" || ext == ".fbx";
    }

    /// <summary>Character nodes are named C##_Something_Something and carry a skinned mesh underneath.</summary>
    static IEnumerable<string> CharacterNodes(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name;
            if (n.Length < 4 || n[0] != 'C' || !char.IsDigit(n[1]) || !char.IsDigit(n[2]) || n[3] != '_') continue;
            if (n.IndexOf('_') != 3) continue;
            if (n.EndsWith("_mesh")) continue;                     // the skinned mesh child, not the character
            if (t.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0) continue;
            yield return n;
        }
    }

    static CharacterCategory CategoryOf(string nodeName)
    {
        var n = nodeName.ToLowerInvariant();
        if (n.Contains("_kid")) return CharacterCategory.Kid;
        if (n.Contains("_teen")) return CharacterCategory.Teen;
        if (n.Contains("_rescuer")) return CharacterCategory.Rescuer;
        if (n.Contains("_elder")) return CharacterCategory.Elder;
        return CharacterCategory.Adult;
    }

    /// <summary>
    /// The path a clip uses to address this character's hip bone, e.g. "Row_Kids/C01_Kid_.../C01_Hips".
    /// Runtime counts the segments to work out how far above the hips the animation root sits, instead of
    /// guessing by sampling.
    /// </summary>
    static string HipsBindingPath(AnimationClip clip, string id)
    {
        if (clip == null) return null;
        string bone = id + "_Hips";

        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            if (b.path == bone || b.path.EndsWith("/" + bone)) return b.path;
        }
        Debug.LogWarning("CharacterLibraryBuilder: " + clip.name + " has no curve targeting " + bone +
                         " - that character will fall back to probing.", clip);
        return null;
    }

    [MenuItem("Bayanihan/Diagnose Character Model")]
    public static void Diagnose()
    {
        var modelPath = ResolveModelPath();
        if (modelPath == null) { Debug.LogError("Diagnose: no character model found."); return; }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var all = AssetDatabase.LoadAllAssetsAtPath(modelPath);
        var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Character model diagnosis ===");
        sb.AppendLine("path:  " + modelPath);
        sb.AppendLine("root:  " + (model != null ? model.name : "<null>"));
        sb.AppendLine("clips: " + clips.Count);

        if (model != null)
        {
            sb.AppendLine();
            sb.AppendLine("-- hierarchy (first 3 levels) --");
            Dump(model.transform, model.transform, sb, 0, 3);
        }

        foreach (var clip in clips.Take(2))
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            sb.AppendLine();
            sb.AppendLine("-- clip '" + clip.name + "' --");
            sb.AppendLine("  length " + clip.length.ToString("0.00") + "s, legacy " + clip.legacy +
                          ", curves " + bindings.Length);
            foreach (var b in bindings.Take(4))
                sb.AppendLine("    path='" + b.path + "'  property='" + b.propertyName + "'");
        }

        Debug.Log(sb.ToString(), model);
    }

    static void Dump(Transform root, Transform t, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        sb.AppendLine(new string(' ', depth * 2) + t.name +
                      (t.GetComponent<SkinnedMeshRenderer>() != null ? "  [SkinnedMesh]" : ""));
        if (depth == maxDepth) return;
        for (int i = 0; i < t.childCount && i < 6; i++) Dump(root, t.GetChild(i), sb, depth + 1, maxDepth);
        if (t.childCount > 6) sb.AppendLine(new string(' ', (depth + 1) * 2) + "... " + (t.childCount - 6) + " more");
    }

    static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string id, string state)
    {
        clips.TryGetValue(id + "_" + state, out var clip);
        return clip;
    }

    static CharacterLibrary LoadOrCreateLibrary()
    {
        var existing = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(LibraryFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var library = ScriptableObject.CreateInstance<CharacterLibrary>();
        AssetDatabase.CreateAsset(library, LibraryPath);
        return library;
    }
}
