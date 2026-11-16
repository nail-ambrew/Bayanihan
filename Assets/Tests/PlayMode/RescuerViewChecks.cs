using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// The player's own body, checked on whichever map it is given: that the water rescuer's skin is on him, that his head
/// stays on in every view yet never comes into his own first-person view, and that nothing of his body fills the middle of
/// the view when he looks ahead or down as far as first person allows. The hands may reach the bottom edge.
///
/// Each map has a subclass that says how to load it (RescuerViewTests for Manila, MabuhayRescuerViewTests for Mabuhay).
/// This base class is not a fixture of its own.
/// </summary>
public abstract class RescuerViewChecks
{
    protected PlayerRescuer player;
    protected CharacterAnimator body;
    protected SimpleBoat boat;
    PlayerRescuer.PovMode viewBefore;

    /// <summary>Loads the map and leaves it ready: the swimmer in the water beside the boat.</summary>
    protected abstract IEnumerator LoadTheMap();

    /// <summary>Anything the map needs putting back after the test.</summary>
    protected virtual void AfterTheTest() { }

    [UnitySetUp]
    public IEnumerator Load()
    {
        viewBefore = GameSettings.SwimView;
        LoadoutState.Clear();
        yield return LoadTheMap();
        StillWater();
        boat = Boat();
        player = Player();
        body = player.GetComponentInChildren<CharacterAnimator>();
        Assert.IsNotNull(body, "the swimmer has no character");
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        GameSettings.SwimView = viewBefore;
        LoadoutState.Clear();
        Time.timeScale = 1f;
        AfterTheTest();
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
    public IEnumerator In_the_water_in_first_person_his_head_is_on_but_out_of_his_view()
    {
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        yield return WaitSeconds(0.3f);

        Assert.IsFalse(player.IsMounted, "the swimmer should be in the water");
        Assert.IsFalse(body.HeadHidden, "the head stays on: his shadow and the view from the boat have one");
        Assert.Greater(body.Bone("Head").localScale.x, 0.5f);
        Assert.AreEqual(0, CountHeadInView(SwimmersView()), "part of his own head is in front of his eye");
    }

    [UnityTest]
    public IEnumerator At_the_helm_the_head_is_shown_again()
    {
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        yield return WaitSeconds(0.2f);
        Board(player);
        yield return WaitSeconds(0.3f);

        Assert.IsTrue(player.IsMounted, "the swimmer should be at the helm");
        Assert.IsFalse(body.HeadHidden);
        Assert.Greater(body.Bone("Head").localScale.x, 0.5f, "the head is back to full size");
    }

    [UnityTest]
    public IEnumerator Third_person_always_shows_the_head()
    {
        player.SetView(PlayerRescuer.PovMode.ThirdPerson);
        yield return WaitSeconds(0.3f);

        Assert.AreEqual(PlayerRescuer.PovMode.ThirdPerson, player.povMode);
        Assert.IsFalse(body.HeadHidden);
        Assert.Greater(body.Bone("Head").localScale.x, 0.5f);
    }

    [UnityTest]
    public IEnumerator Looking_ahead_and_a_little_down_nothing_of_his_body_fills_the_middle_of_the_view()
    {
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        var mesh = body.Character.GetComponentInChildren<SkinnedMeshRenderer>();

        foreach (float pitch in new[] { 0f, 10f, 20f, player.firstPersonMaxDown })
        {
            LookAt(pitch);
            yield return WaitSeconds(0.2f);

            int inTheMiddle = CountBodyInTheMiddleOfView(mesh, SwimmersView());
            Assert.AreEqual(0, inTheMiddle, "looking at pitch " + pitch + " his own body is in the middle of the view");
        }
    }

    [UnityTest]
    public IEnumerator The_checks_would_catch_an_eye_inside_the_head_or_looking_steeply_down()
    {
        // The controls for the tests above. An eye in the middle of the head must see some of the head, and looking 50°
        // down (past what first person allows) must put the body in the middle of the view. Otherwise the checks prove nothing.
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        yield return WaitSeconds(0.2f);
        Assert.IsTrue(player.HeadSphere(out Vector3 centre, out _), "the head was measured");
        var inside = SwimmersView();
        inside.transform.position = centre;
        Assert.Greater(CountHeadInView(inside), 0, "an eye inside the head should see the head");

        LookAt(50f);                                // past the limit, placed the way the game would place it
        Invoke(player, "PlaceCamera");
        var eye = SwimmersView();
        int inTheMiddle = CountBodyInTheMiddleOfView(body.Character.GetComponentInChildren<SkinnedMeshRenderer>(), eye);
        Assert.Greater(inTheMiddle, 0, "looking 50° down should have shown the body in the middle of the view");
    }

    /// <summary>Vertices of his head in front of the eye's near plane (anything there could be drawn across the view).</summary>
    int CountHeadInView(Camera eye)
    {
        var mesh = body.Character.GetComponentInChildren<SkinnedMeshRenderer>();
        int head = System.Array.IndexOf(mesh.bones, body.Bone("Head"));
        var baked = new Mesh();
        mesh.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var weights = mesh.sharedMesh.boneWeights;
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            float onHead = (w.boneIndex0 == head ? w.weight0 : 0f) + (w.boneIndex1 == head ? w.weight1 : 0f) +
                           (w.boneIndex2 == head ? w.weight2 : 0f) + (w.boneIndex3 == head ? w.weight3 : 0f);
            if (onHead < 0.05f) continue;
            Vector3 p = mesh.transform.TransformPoint(vertices[i]);
            if (Vector3.Dot(p - eye.transform.position, eye.transform.forward) > eye.nearClipPlane) count++;
        }
        Object.DestroyImmediate(baked);
        Object.DestroyImmediate(eye.gameObject);
        return count;
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
