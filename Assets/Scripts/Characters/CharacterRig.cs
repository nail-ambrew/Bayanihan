using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one person out of the 30-character model.
///
/// The model keeps every character self-contained (C07_Teen_NavyHoodie > C07_Hips + C07_mesh) and its clips only
/// touch bones inside that character, so extracting somebody is just "lift this node out and zero its position" -
/// no cancelling of grid offsets, no destroying 29 siblings per spawn.
///
/// The catch is that an imported clip's channel paths are relative to whichever object Unity treats as the
/// animation root, and that depends on the importer. So the first time a character is spawned we rebuild their
/// original transform path, probe which object in it actually drives the bones, and cache the result as a
/// template. Later spawns are a plain Instantiate of that template.
/// </summary>
public static class CharacterRig
{
    class Template
    {
        public GameObject prefab;        // inactive, parked under the holder
        public string animRootPath;      // path from the rig root to the object the clips animate ("" = the root)
    }

    static readonly Dictionary<string, Template> templates = new Dictionary<string, Template>();
    static Transform holder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        templates.Clear();
        holder = null;
    }

    static Transform Holder
    {
        get
        {
            if (holder == null)
            {
                var go = new GameObject("~CharacterTemplates");
                go.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(go);          // templates survive a scene reload (Retry)
                holder = go.transform;
            }
            return holder;
        }
    }

    /// <summary>
    /// Puts one character under the given parent at local origin, with a CharacterAnimator ready to play.
    /// Returns null if the character can't be built (missing library, model or node).
    /// </summary>
    public static CharacterAnimator Spawn(CharacterEntry entry, Transform parent, CharacterPose pose = CharacterPose.Idle)
    {
        if (entry == null || !entry.IsComplete) return null;

        var template = GetTemplate(entry);
        if (template == null) return null;

        var go = Object.Instantiate(template.prefab, parent, false);
        go.name = "Person_" + entry.id;
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var animRoot = string.IsNullOrEmpty(template.animRootPath)
            ? go.transform
            : go.transform.Find(template.animRootPath);

        var anim = go.AddComponent<CharacterAnimator>();
        anim.Bind(entry, animRoot != null ? animRoot : go.transform);
        anim.Play(pose);
        return anim;
    }

    // ------------------------------------------------------------------ template construction
    static Template GetTemplate(CharacterEntry entry)
    {
        if (templates.TryGetValue(entry.id, out var cached) && cached.prefab != null) return cached;

        var library = CharacterLibrary.Instance;
        if (library == null || library.sourceModel == null)
        {
            Debug.LogWarning("CharacterRig: the character library has no source model. " +
                             "Run Bayanihan > Rebuild Character Library.");
            return null;
        }

        var instance = Object.Instantiate(library.sourceModel);
        instance.SetActive(true);

        // Whichever Animation Method the model imports with, glTFast puts a live Animator or Animation on it.
        // An enabled one rewrites the skeleton to its own state every frame and silently undoes everything
        // SampleAnimation does - which looks exactly like "the animations aren't applied". Disable rather than
        // destroy: Destroy is deferred to end of frame, and the first clone is taken in this same frame.
        foreach (var a in instance.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var a in instance.GetComponentsInChildren<Animation>(true)) a.enabled = false;

        var character = FindDeep(instance.transform, entry.nodeName);
        if (character == null)
        {
            Debug.LogWarning("CharacterRig: node '" + entry.nodeName + "' is not in " + library.sourceModel.name + ".");
            Object.Destroy(instance);
            return null;
        }

        // Rebuild the character's original transform path on a fresh root, then lift the character into it.
        // Keeping the path intact is what lets an imported clip's channel paths still resolve.
        var chain = new List<Transform>();
        for (Transform t = character.parent; t != null; t = t.parent) chain.Add(t);
        chain.Reverse();

        var root = new GameObject(entry.id + "_Rig");
        root.transform.SetParent(Holder, false);
        Transform tip = root.transform;
        foreach (var link in chain)
        {
            var node = new GameObject(link.name);
            node.transform.SetParent(tip, false);
            node.transform.localPosition = link.localPosition;
            node.transform.localRotation = link.localRotation;
            node.transform.localScale = link.localScale;
            tip = node.transform;
        }

        character.SetParent(tip, false);
        character.localPosition = Vector3.zero;        // drop the studio grid offset
        character.localRotation = Quaternion.identity;

        if (library.stripNodeNames != null)
        {
            foreach (var strip in library.stripNodeNames)
            {
                var junk = FindDeep(root.transform, strip);
                if (junk != null) Object.Destroy(junk.gameObject);
            }
        }

        Object.Destroy(instance);                      // the other 29 go with it

        foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var a in root.GetComponentsInChildren<Animation>(true)) a.enabled = false;

        var template = new Template
        {
            prefab = root,
            animRootPath = ResolveAnimRootPath(root.transform, character, entry)
        };
        VerifySampling(root.transform, character, entry, template.animRootPath);
        root.SetActive(false);
        templates[entry.id] = template;
        return template;
    }

    /// <summary>
    /// Finds which object the clips are authored against. Returns the path from the rig root ("" = the root).
    ///
    /// The reliable answer comes from the library, which recorded the clip's own curve binding for the hip bone
    /// at build time - counting the segments in that path tells us exactly how many levels above the hips the
    /// animation root sits. Sampling and watching for movement is only a fallback: a subtle idle can move a bone
    /// by less than any sensible threshold and silently report "this isn't the root".
    /// </summary>
    static string ResolveAnimRootPath(Transform root, Transform character, CharacterEntry entry)
    {
        var hips = FindDeep(character, entry.Bone("Hips"));
        if (hips == null)
        {
            Debug.LogWarning("CharacterRig: no '" + entry.Bone("Hips") + "' under '" + entry.nodeName +
                             "' - animation may not play.");
            return "";
        }

        if (!string.IsNullOrEmpty(entry.hipsBindingPath))
        {
            int depth = entry.hipsBindingPath.Split('/').Length;
            Transform t = hips;
            for (int i = 0; i < depth && t != null; i++) t = t.parent;

            if (t != null && t.Find(entry.hipsBindingPath) == hips)
                return PathFrom(root, t);

            Debug.LogWarning("CharacterRig: the recorded binding path '" + entry.hipsBindingPath + "' for " +
                             entry.id + " doesn't line up with the rig - falling back to probing. " +
                             "Re-run Bayanihan > Rebuild Character Library.");
        }

        // Fallback: the rig root first, then each step down to the character itself.
        var candidates = new List<Transform> { root };
        var down = new List<Transform>();
        for (Transform t = character; t != null && t != root; t = t.parent) down.Add(t);
        down.Reverse();
        candidates.AddRange(down);

        foreach (var candidate in candidates)
            if (MovesSkeleton(candidate, character, entry.idle))
                return PathFrom(root, candidate);

        Debug.LogWarning("CharacterRig: nothing above '" + entry.nodeName + "' responds to '" + entry.idle.name +
                         "'. Check that the clips belong to this model.");
        return "";
    }

    /// <summary>
    /// Sanity-checks, once per character, that sampling actually moves the skeleton. Without this a silent
    /// failure looks identical to a character who is simply standing still, which is very hard to spot.
    /// </summary>
    static void VerifySampling(Transform root, Transform character, CharacterEntry entry, string animRootPath)
    {
        var animRoot = string.IsNullOrEmpty(animRootPath) ? root : root.Find(animRootPath);
        if (animRoot == null) return;

        var clip = entry.floating != null ? entry.floating : entry.idle;
        if (clip == null || clip.length <= 0f) return;

        if (!MovesSkeleton(animRoot, character, clip))
        {
            Debug.LogWarning("CharacterRig: " + entry.id + " does not respond to '" + clip.name +
                             "' sampled on '" + (animRoot == root ? "<rig root>" : animRootPath) +
                             "'. The character will stand in bind pose. Check that no Animator is re-enabled on " +
                             "the rig, and that Bayanihan > Rebuild Character Library has been run.");
        }
    }

    // Deliberately not 0 and 0.5: a looping clip is often in the same pose at both.
    static readonly float[] ProbeTimes = { 0.23f, 0.41f, 0.67f };

    /// <summary>
    /// True if sampling the clip on this object moves any bone of the character.
    ///
    /// Watching a single bone at two instants is not proof either way: the Float clip never turns the hips at
    /// all, and a bobbing bone passes through the same height twice a cycle. So every bone is compared, at
    /// several points through the clip.
    /// </summary>
    static bool MovesSkeleton(Transform sampleOn, Transform character, AnimationClip clip)
    {
        if (clip == null || clip.length <= 0f) return false;

        var bones = character.GetComponentsInChildren<Transform>(true);
        var rotations = new Quaternion[bones.Length];
        var positions = new Vector3[bones.Length];

        clip.SampleAnimation(sampleOn.gameObject, 0f);
        for (int i = 0; i < bones.Length; i++)
        {
            rotations[i] = bones[i].localRotation;
            positions[i] = bones[i].localPosition;
        }

        foreach (float t in ProbeTimes)
        {
            clip.SampleAnimation(sampleOn.gameObject, clip.length * t);
            for (int i = 0; i < bones.Length; i++)
            {
                if (Quaternion.Angle(rotations[i], bones[i].localRotation) > 0.05f ||
                    (positions[i] - bones[i].localPosition).sqrMagnitude > 1e-8f)
                    return true;
            }
        }
        return false;
    }

    static string PathFrom(Transform root, Transform target)
    {
        if (target == root) return "";
        var parts = new List<string>();
        for (Transform t = target; t != null && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }
        return null;
    }
}
