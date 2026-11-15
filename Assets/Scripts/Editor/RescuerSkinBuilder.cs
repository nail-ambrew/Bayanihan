using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the player's water-rescue body (Assets/3D Models/water_rescue_responder.glb) as a skin on the library
/// character C24, and saves it to Assets/Resources/Characters/WaterRescueResponder.asset.
///
/// The model is one static set of parts, with its arms out in an A-pose. C24's bones stay exactly as they are, so
/// every clip, pose and overlay keeps working. What changes is the geometry, once, here:
///   1. the model is scaled to C24's height and stood on the floor;
///   2. each arm (sleeve and glove) is swung into C24's T-pose, about the shoulder, so it sits on C24's arm bones;
///   3. every vertex is weighted to the bones whose segments are nearest it, from the set of bones that part may move.
/// The mesh is bound to C24's rest pose, so it looks exactly like the model at rest and bends with the skeleton.
/// Run again after changing the model or the rules; the asset is rewritten in place.
/// </summary>
public static class RescuerSkinBuilder
{
    const string ModelPath = "Assets/3D Models/water_rescue_responder.glb";
    const string SourcePath = "Assets/3D Models/filipino_characters_30.glb";
    const string CharacterPath = "FilipinoCharacters_30/Row_Rescuers/C24_Rescuer_Lean_DarkHelmet";
    const string CharacterId = "C24";
    const string AssetPath = "Assets/Resources/Characters/WaterRescueResponder.asset";

    /// <summary>Bone names without the character prefix. Each BoneWeight indexes this list, in this order.</summary>
    static readonly string[] Bones =
    {
        "Hips", "Spine", "Chest", "Neck", "Head",
        "UpperArmL", "ForearmL", "HandL", "UpperArmR", "ForearmR", "HandR",
        "ThighL", "ShinL", "FootL", "ThighR", "ShinR", "FootR"
    };

    /// <summary>The bone segment each bone spans, for distance-based weights (a leaf spans its parent's segment).</summary>
    static readonly Dictionary<string, string[]> Segments = new Dictionary<string, string[]>
    {
        { "Hips", new[] { "Hips", "Spine" } }, { "Spine", new[] { "Spine", "Chest" } },
        { "Chest", new[] { "Chest", "Neck" } }, { "Neck", new[] { "Neck", "Head" } }, { "Head", new[] { "Neck", "Head" } },
        { "UpperArmL", new[] { "UpperArmL", "ForearmL" } }, { "ForearmL", new[] { "ForearmL", "HandL" } }, { "HandL", new[] { "ForearmL", "HandL" } },
        { "UpperArmR", new[] { "UpperArmR", "ForearmR" } }, { "ForearmR", new[] { "ForearmR", "HandR" } }, { "HandR", new[] { "ForearmR", "HandR" } },
        { "ThighL", new[] { "ThighL", "ShinL" } }, { "ShinL", new[] { "ShinL", "FootL" } }, { "FootL", new[] { "ShinL", "FootL" } },
        { "ThighR", new[] { "ThighR", "ShinR" } }, { "ShinR", new[] { "ShinR", "FootR" } }, { "FootR", new[] { "ShinR", "FootR" } },
    };

    static readonly string[] Arms = { "gloves", "jacket", "straps_buckles" };

    [MenuItem("Bayanihan/Rebuild Water Rescuer Skin")]
    public static void Rebuild()
    {
        var skin = Build();
        Selection.activeObject = skin;
        Debug.Log("Water rescuer skin rebuilt: " + AssetPath + " (" + skin.mesh.vertexCount + " vertices, " +
                  skin.mesh.subMeshCount + " materials)", skin);
    }

    public static CharacterSkin Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (model == null || source == null) throw new System.Exception("Missing " + ModelPath + " or " + SourcePath);

        var sourceCharacter = source.transform.Find(CharacterPath);
        if (sourceCharacter == null) throw new System.Exception("No " + CharacterPath + " in " + SourcePath);

        // The character as it is in the library: its own rig and renderer, at rest, in edit mode.
        var rig = Object.Instantiate(sourceCharacter.gameObject);
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            return BuildOn(model, rig.transform);
        }
        finally
        {
            Object.DestroyImmediate(rig);
        }
    }

    static CharacterSkin BuildOn(GameObject model, Transform rig)
    {
        var renderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (renderer == null) throw new System.Exception("C24 has no skinned mesh renderer.");

        // ---- the skeleton, in the character's own space (feet on the floor, root at the origin)
        var bones = new Dictionary<string, Transform>();
        foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            foreach (var name in Bones)
                if (t.name == CharacterId + "_" + name) bones[name] = t;
        foreach (var name in Bones)
            if (!bones.ContainsKey(name)) throw new System.Exception("C24 has no bone " + CharacterId + "_" + name);

        var bonePos = new Dictionary<string, Vector3>();
        var boneIndex = new Dictionary<string, int>();
        for (int i = 0; i < Bones.Length; i++)
        {
            bonePos[Bones[i]] = rig.InverseTransformPoint(bones[Bones[i]].position);
            boneIndex[Bones[i]] = i;
        }

        // ---- the character's height, from its own baked mesh
        var baked = new Mesh();
        renderer.BakeMesh(baked);
        float rigMin = float.MaxValue, rigMax = float.MinValue;
        foreach (var v in baked.vertices)
        {
            float y = rig.InverseTransformPoint(renderer.transform.TransformPoint(v)).y;
            rigMin = Mathf.Min(rigMin, y);
            rigMax = Mathf.Max(rigMax, y);
        }
        Object.DestroyImmediate(baked);
        float rigHeight = rigMax - rigMin;

        // ---- the model's parts, in order, with their materials
        var parts = new List<(string name, Mesh mesh, Material material)>();
        float modelMin = float.MaxValue, modelMax = float.MinValue;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var part = filter.GetComponent<MeshRenderer>();
            parts.Add((filter.name, mesh, part != null ? part.sharedMaterial : null));
            foreach (var v in mesh.vertices)
            {
                modelMin = Mathf.Min(modelMin, v.y);
                modelMax = Mathf.Max(modelMax, v.y);
            }
        }
        float scale = rigHeight / (modelMax - modelMin);

        // Model space -> character space: stand it on the floor and scale it to the character's height.
        var vertices = new Dictionary<string, Vector3[]>();
        foreach (var part in parts)
        {
            var src = part.mesh.vertices;
            var dst = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = new Vector3(src[i].x * scale, (src[i].y - modelMin) * scale, src[i].z * scale);
            vertices[part.name] = dst;
        }

        // ---- swing the arms from the model's A-pose into the character's T-pose, about each shoulder
        var morph = new Dictionary<string, ArmMorph>();
        foreach (var side in new[] { "L", "R" })
            morph[side] = ArmMorph.Measure(side, vertices, bonePos["UpperArm" + side], bonePos["Hand" + side]);
        foreach (var arm in Arms)
        {
            if (!vertices.ContainsKey(arm)) continue;
            var vs = vertices[arm];
            for (int i = 0; i < vs.Length; i++)
            {
                string side = vs[i].x < 0f ? "L" : "R";
                vs[i] = morph[side].Apply(vs[i]);
            }
        }

        // ---- weights and the combined mesh
        var materials = new List<Material>();
        var triangles = new List<List<int>>();
        var outVertices = new List<Vector3>();
        var outWeights = new List<BoneWeight>();

        foreach (var part in parts)
        {
            if (!materials.Contains(part.material))
            {
                materials.Add(part.material);
                triangles.Add(new List<int>());
            }
            int sub = materials.IndexOf(part.material);

            var vs = vertices[part.name];
            int offset = outVertices.Count;
            for (int i = 0; i < vs.Length; i++)
            {
                outVertices.Add(vs[i]);
                outWeights.Add(Weigh(part.name, vs[i], bonePos, boneIndex));
            }

            var tris = part.mesh.triangles;
            for (int i = 0; i < tris.Length; i++) triangles[sub].Add(tris[i] + offset);
        }

        // Skin space is the renderer's own transform. Its bind poses are taken from the same rig, at rest.
        var bind = new Matrix4x4[Bones.Length];
        for (int i = 0; i < Bones.Length; i++)
            bind[i] = bones[Bones[i]].worldToLocalMatrix * renderer.transform.localToWorldMatrix;

        var mesh2 = new Mesh { name = "WaterRescueResponder", indexFormat = IndexFormat.UInt32 };
        mesh2.SetVertices(outVertices);
        mesh2.boneWeights = outWeights.ToArray();
        mesh2.bindposes = bind;
        mesh2.subMeshCount = materials.Count;
        for (int i = 0; i < materials.Count; i++) mesh2.SetTriangles(triangles[i], i);
        mesh2.RecalculateNormals();
        mesh2.RecalculateBounds();

        return SaveAsset(mesh2, materials.ToArray());
    }

    static BoneWeight Weigh(string part, Vector3 p, Dictionary<string, Vector3> bonePos, Dictionary<string, int> index)
    {
        string[] candidates = CandidatesFor(part, p);

        var best = new List<(float weight, int bone)>();
        foreach (var name in candidates)
        {
            if (candidates.Length == 1)
            {
                best.Add((1f, index[name]));
                continue;
            }
            var seg = Segments[name];
            float d = DistanceToSegment(p, bonePos[seg[0]], bonePos[seg[1]]);
            best.Add((1f / (d * d + 0.0004f), index[name]));
        }
        best.Sort((a, b) => b.weight.CompareTo(a.weight));

        var w = new BoneWeight();
        float total = 0f;
        for (int i = 0; i < Mathf.Min(2, best.Count); i++) total += best[i].weight;
        for (int i = 0; i < Mathf.Min(2, best.Count); i++)
        {
            float share = best[i].weight / total;
            if (i == 0) { w.boneIndex0 = best[i].bone; w.weight0 = share; }
            else { w.boneIndex1 = best[i].bone; w.weight1 = share; }
        }
        return w;
    }

    /// <summary>Which bones a part may move. Head parts follow the head; hands, boots and knee pads follow their joint.</summary>
    static string[] CandidatesFor(string part, Vector3 p)
    {
        string s = p.x < 0f ? "L" : "R";
        switch (part)
        {
            case "helmet":
            case "helmet_vents":
            case "chin_strap":
            case "hair":
            case "head":
            case "face_features":
            case "mouth":
                return new[] { "Head" };
            case "gloves": return new[] { "Hand" + s };
            case "boots":
            case "soles": return new[] { "Foot" + s };
            case "knee_pads": return new[] { "Thigh" + s, "Shin" + s };
            case "pants": return new[] { "Hips", "ThighL", "ShinL", "ThighR", "ShinR" };
            case "whistle": return new[] { "Spine", "Chest" };
            case "life_vest": return new[] { "Hips", "Spine", "Chest" };
            default: return new[] { "Hips", "Spine", "Chest", "Neck", "UpperArmL", "ForearmL", "UpperArmR", "ForearmR" };
        }
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
        return Vector3.Distance(p, a + ab * t);
    }

    static CharacterSkin SaveAsset(Mesh mesh, Material[] materials)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Characters")) AssetDatabase.CreateFolder("Assets/Resources", "Characters");

        var skin = AssetDatabase.LoadAssetAtPath<CharacterSkin>(AssetPath);
        if (skin == null)
        {
            skin = ScriptableObject.CreateInstance<CharacterSkin>();
            AssetDatabase.CreateAsset(skin, AssetPath);
        }

        if (skin.mesh != null) AssetDatabase.RemoveObjectFromAsset(skin.mesh);
        AssetDatabase.AddObjectToAsset(mesh, skin);

        skin.baseCharacterId = CharacterId;
        skin.boneNames = (string[])Bones.Clone();
        skin.mesh = mesh;
        skin.materials = materials;
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        return skin;
    }

    /// <summary>
    /// One arm's swing: rotates the sleeve and glove about the shoulder from where the model has the arm to where
    /// C24's arm bone runs, blended in along the arm so the torso does not move.
    /// </summary>
    struct ArmMorph
    {
        Vector3 pivot, direction, target;
        Quaternion turn;
        bool inert;

        public static ArmMorph Measure(string side, Dictionary<string, Vector3[]> parts, Vector3 upperArm, Vector3 hand)
        {
            var gloves = parts["gloves"];
            var jacket = parts["jacket"];

            // The hand: the centre of the glove on this side.
            Vector3 handCentre = Vector3.zero;
            int n = 0;
            foreach (var v in gloves)
                if ((v.x < 0f) == (side == "L")) { handCentre += v; n++; }
            if (n == 0) return new ArmMorph { inert = true };
            handCentre /= n;

            // The shoulder: the top of the sleeve, just outside the torso.
            var shoulderCandidates = new List<Vector3>();
            foreach (var v in jacket)
                if ((v.x < 0f) == (side == "L") && Mathf.Abs(v.x) > 0.14f && Mathf.Abs(v.x) < 0.32f) shoulderCandidates.Add(v);
            shoulderCandidates.Sort((a, b) => b.y.CompareTo(a.y));
            int top = Mathf.Max(1, shoulderCandidates.Count / 20);
            Vector3 shoulder = Vector3.zero;
            for (int i = 0; i < top; i++) shoulder += shoulderCandidates[i];
            shoulder /= top;

            var m = new ArmMorph
            {
                pivot = shoulder,
                direction = (handCentre - shoulder).normalized,
                target = upperArm,
            };
            Vector3 toHand = (hand - upperArm).normalized;
            m.turn = Quaternion.FromToRotation(m.direction, toHand);
            return m;
        }

        public Vector3 Apply(Vector3 v)
        {
            if (inert) return v;
            float along = Vector3.Dot(v - pivot, direction);
            float outward = Mathf.Abs(v.x);
            float wAlong = Smooth(0f, 0.14f, along);
            float wOut = Smooth(0.14f, 0.22f, outward);
            float w = wAlong * wOut;
            if (w <= 0f) return v;

            Vector3 turned = target + turn * (v - pivot);
            return Vector3.Lerp(v, turned, w);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
