using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// The player's own body: that the water rescuer's skin is on him, that his head is hidden from his own first-person
/// view in the water and shown again at the helm, and that nothing of his body fills the middle of the view when he
/// looks ahead or a little down. The hands and shoulders may reach the bottom edge when looking steeply down.
/// </summary>
public class RescuerViewTests
{
    PlayerRescuer player;
    CharacterAnimator body;
    SimpleBoat boat;

    static readonly Vector3 StreetSpot = new Vector3(-30f, 0f, -6.5f);

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();
        ClearTheStage();
        StillWater();
        boat = Boat();
        player = Player();
        body = player.GetComponentInChildren<CharacterAnimator>();
        Teleport(boat, new Vector3(StreetSpot.x, boat.transform.position.y, StreetSpot.z), Vector3.left);
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    void LookAt(float pitch)
    {
        var field = typeof(PlayerRescuer).GetField("camPitch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, "PlayerRescuer.camPitch no longer exists");
        field.SetValue(player, pitch);
    }

    [UnityTest]
    public IEnumerator The_player_wears_the_water_rescuer_body()
    {
        var skin = CharacterSkin.Load();
        Assert.IsNotNull(skin, "the water rescuer skin is missing from Resources");
        var renderer = body.Character.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.AreSame(skin.mesh, renderer.sharedMesh, "the body on the player is not the water rescuer's");
        Assert.AreEqual(skin.boneNames.Length, renderer.bones.Length);
        Assert.AreSame(body.Bone("Head"), renderer.bones[System.Array.IndexOf(skin.boneNames, "Head")], "the mesh is bound to the wrong head");
        yield return null;
    }

    [UnityTest]
    public IEnumerator In_the_water_in_first_person_his_own_head_is_hidden()
    {
        player.povMode = PlayerRescuer.PovMode.FirstPerson;
        yield return WaitSeconds(0.3f);

        Assert.IsTrue(body.HeadHidden);
        Assert.AreEqual(Vector3.zero, body.Bone("Head").localScale, "the head is shrunk to nothing");
    }

    [UnityTest]
    public IEnumerator At_the_helm_the_head_is_shown_again()
    {
        player.povMode = PlayerRescuer.PovMode.FirstPerson;
        yield return WaitSeconds(0.2f);
        Board(player);
        yield return WaitSeconds(0.3f);

        Assert.IsFalse(body.HeadHidden);
        Assert.Greater(body.Bone("Head").localScale.x, 0.5f, "the head is back to full size");
    }

    [UnityTest]
    public IEnumerator Third_person_always_shows_the_head()
    {
        player.povMode = PlayerRescuer.PovMode.ThirdPerson;
        yield return WaitSeconds(0.3f);

        Assert.IsFalse(body.HeadHidden);
        Assert.Greater(body.Bone("Head").localScale.x, 0.5f);
    }

    [UnityTest]
    public IEnumerator Looking_ahead_and_a_little_down_nothing_of_his_body_fills_the_middle_of_the_view()
    {
        player.povMode = PlayerRescuer.PovMode.FirstPerson;
        var mesh = body.Character.GetComponentInChildren<SkinnedMeshRenderer>();

        foreach (float pitch in new[] { 0f, 20f, 40f, 50f })
        {
            LookAt(pitch);
            yield return WaitSeconds(0.2f);

            int inTheMiddle = CountBodyInTheMiddleOfView(mesh, SwimmersView());
            Assert.AreEqual(0, inTheMiddle, "looking at pitch " + pitch + " his own body is in the middle of the view");
        }
    }

    [UnityTest]
    public IEnumerator The_check_would_catch_the_old_eye_point_that_was_inside_the_head()
    {
        // The control for the test above: the placement it used to have (at the back of the skull, 6 cm forward)
        // must show the body in the middle of the view when looking down. Otherwise the check proves nothing.
        player.povMode = PlayerRescuer.PovMode.FirstPerson;
        player.firstPersonEye = new Vector2(0.2f, 0.06f);
        LookAt(40f);
        yield return WaitSeconds(0.2f);

        int inTheMiddle = CountBodyInTheMiddleOfView(body.Character.GetComponentInChildren<SkinnedMeshRenderer>(), SwimmersView());
        Assert.Greater(inTheMiddle, 0, "the old placement should have shown the body in the middle of the view");
    }

    /// <summary>A camera at the swimmer's eye, with the same lens as the game's first-person camera.</summary>
    Camera SwimmersView()
    {
        var field = typeof(PlayerRescuer).GetField("camRig", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var rig = (Transform)field.GetValue(player);
        Assert.IsNotNull(rig, "the swimmer's camera rig is missing");

        var eye = new GameObject("Swimmer's eye (test)").AddComponent<Camera>();
        eye.fieldOfView = player.fieldOfView;
        eye.nearClipPlane = 0.05f;
        eye.farClipPlane = 400f;
        eye.transform.SetPositionAndRotation(rig.position, rig.rotation);
        return eye;
    }

    /// <summary>
    /// Vertices of his own body that are in front of the eye, within a metre, and in the middle of the screen (the
    /// horizon and the water ahead). The bottom edge is excluded: the hands reach there when looking down.
    /// </summary>
    static int CountBodyInTheMiddleOfView(SkinnedMeshRenderer mesh, Camera eye)
    {
        var baked = new Mesh();
        mesh.BakeMesh(baked);
        Matrix4x4 toWorld = mesh.transform.localToWorldMatrix;
        int count = 0;
        foreach (var local in baked.vertices)
        {
            Vector3 vp = eye.WorldToViewportPoint(toWorld.MultiplyPoint3x4(local));
            if (vp.z < eye.nearClipPlane || vp.z > 1f) continue;
            if (vp.x < 0.2f || vp.x > 0.8f || vp.y < 0.2f || vp.y > 0.8f) continue;
            count++;
        }
        Object.DestroyImmediate(baked);
        Object.DestroyImmediate(eye.gameObject);
        return count;
    }
}
