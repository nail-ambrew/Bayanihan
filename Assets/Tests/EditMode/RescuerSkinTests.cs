using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The water rescuer's body, as built into its asset: that it has the right skeleton, that every vertex is weighted
/// to real bones with weights that add up, and that it is the size of the character it is put on.
/// </summary>
public class RescuerSkinTests
{
    CharacterSkin skin;

    [SetUp]
    public void Load()
    {
        skin = AssetDatabase.LoadAssetAtPath<CharacterSkin>("Assets/Resources/Characters/WaterRescueResponder.asset");
    }

    [Test]
    public void The_skin_exists_and_is_bound_to_the_water_rescuer_C24()
    {
        Assert.IsNotNull(skin, "run Bayanihan > Rebuild Water Rescuer Skin");
        Assert.AreEqual("C24", skin.baseCharacterId);
        Assert.IsNotNull(skin.mesh);
        Assert.AreEqual(skin.materials.Length, skin.mesh.subMeshCount, "one material per sub-mesh");
        Assert.IsTrue(System.Array.IndexOf(skin.boneNames, "Head") >= 0, "the head can be hidden");
        Assert.IsTrue(System.Array.IndexOf(skin.boneNames, "Hips") >= 0);
    }

    [Test]
    public void Every_bone_the_mesh_uses_has_a_bind_pose_and_there_is_one_for_each_bone()
    {
        Assert.AreEqual(skin.boneNames.Length, skin.mesh.bindposes.Length);
        foreach (var pose in skin.mesh.bindposes)
            Assert.IsTrue(IsFinite(pose), "a bind pose is not a finite matrix");
    }

    [Test]
    public void Every_vertex_is_weighted_to_existing_bones_and_the_weights_add_up()
    {
        var weights = skin.mesh.boneWeights;
        Assert.Greater(weights.Length, 1000, "a whole person, not a few parts");
        for (int i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            Assert.That(w.boneIndex0, Is.InRange(0, skin.boneNames.Length - 1), "vertex " + i + " uses a bone that does not exist");
            Assert.That(w.boneIndex1, Is.InRange(0, skin.boneNames.Length - 1), "vertex " + i + " uses a bone that does not exist");
            Assert.That(w.weight0 + w.weight1, Is.InRange(0.999f, 1.001f), "vertex " + i + " weights do not add up to 1");
            Assert.GreaterOrEqual(w.weight0, 0f);
            Assert.GreaterOrEqual(w.weight1, 0f);
        }
    }

    [Test]
    public void The_head_is_weighted_to_the_head_bone_alone()
    {
        int head = System.Array.IndexOf(skin.boneNames, "Head");
        int headVertices = 0;
        var vertices = skin.mesh.vertices;
        var weights = skin.mesh.boneWeights;
        // The helmet and face sit above the neck; those vertices must all be carried by the Head bone.
        float top = skin.mesh.bounds.max.y;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].y < top - 0.2f) continue;
            headVertices++;
            Assert.AreEqual(head, weights[i].boneIndex0, "a vertex in the helmet is not on the Head bone");
            Assert.AreEqual(1f, weights[i].weight0, 0.001f, "a vertex in the helmet also follows another bone");
        }
        Assert.Greater(headVertices, 100, "the helmet and head should be most of the top of the mesh");
    }

    [Test]
    public void It_is_the_height_of_the_character_it_is_put_on()
    {
        var bounds = skin.mesh.bounds;
        Assert.That(bounds.size.y, Is.InRange(1.65f, 1.85f), "the library's C24 stands about 1.74 m tall");
        Assert.That(bounds.min.y, Is.InRange(-0.05f, 0.05f), "the feet are on the floor");
    }

    static bool IsFinite(Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (float.IsNaN(m[r, c]) || float.IsInfinity(m[r, c])) return false;
        return true;
    }
}
