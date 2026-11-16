using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// The swimmer's two views: first person, from just in front of their face, and third person, from behind and above. The head
/// is on in both. In first person no part of it ever comes into view, whichever way the player looks; in third person the
/// camera keeps out of walls and off the water. The SWIM VIEW setting and V switch between them.
/// </summary>
public class SwimViewTests
{
    PlayerRescuer player;
    PlayerRescuer.PovMode viewBefore;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [UnitySetUp]
    public IEnumerator Load()
    {
        viewBefore = GameSettings.SwimView;
        LoadoutState.Clear();
        yield return LoadGameplay();
        ClearTheStage();
        StillWater();
        player = Player();
        yield return WaitSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        GameSettings.SwimView = viewBefore;
        LoadoutState.Clear();
        yield return null;
    }

    void Look(float yaw, float pitch, bool turnTheBody)
    {
        typeof(PlayerRescuer).GetField("camYaw", Private).SetValue(player, yaw);
        typeof(PlayerRescuer).GetField("camPitch", Private).SetValue(player, pitch);
        if (turnTheBody)
        {
            typeof(PlayerRescuer).GetField("yaw", Private).SetValue(player, yaw);
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        Invoke(player, "PlaceCamera");
    }

    Transform Head() => player.GetComponentInChildren<CharacterAnimator>().Bone("Head");

    /// <summary>The head's vertices where they are drawn now.</summary>
    List<Vector3> HeadPoints()
    {
        var skin = player.GetComponentInChildren<SkinnedMeshRenderer>();
        int index = System.Array.IndexOf(skin.bones, Head());
        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var weights = skin.sharedMesh.boneWeights;
        var points = new List<Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            float onHead = (w.boneIndex0 == index ? w.weight0 : 0f) + (w.boneIndex1 == index ? w.weight1 : 0f) +
                           (w.boneIndex2 == index ? w.weight2 : 0f) + (w.boneIndex3 == index ? w.weight3 : 0f);
            if (onHead > 0.05f) points.Add(skin.transform.TransformPoint(vertices[i]));
        }
        Object.Destroy(baked);
        return points;
    }

    [UnityTest]
    public IEnumerator First_person_keeps_the_head_on_but_never_in_view()
    {
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        yield return null;
        Assert.Greater(Head().localScale.x, 0.5f, "the head is on in first person (its shadow, the view from the boat)");
        Assert.IsTrue(player.HeadSphere(out _, out float radius) && radius > 0.05f, "the head was measured");

        var cam = player.SwimCamera;
        float near = 0.05f;
        float facing = player.transform.eulerAngles.y;
        foreach (float yaw in new[] { 0f, 60f, 135f, 200f, 300f })
            foreach (float pitch in new[] { player.pitchLimits.x, 0f, 15f, player.firstPersonMaxDown })
            {
                Look(facing + yaw, pitch, turnTheBody: true);
                foreach (var p in HeadPoints())
                {
                    float ahead = Vector3.Dot(p - cam.position, cam.forward);
                    Assert.Less(ahead, near, "looking " + yaw + "°, " + pitch + "° down, part of the head is in front of the eye");
                }
            }
    }

    [UnityTest]
    public IEnumerator First_person_cannot_look_down_into_the_vest_and_the_body_turns_with_the_view()
    {
        player.SetView(PlayerRescuer.PovMode.FirstPerson);
        typeof(PlayerRescuer).GetField("camPitch", Private).SetValue(player, 80f);
        Invoke(player, "Look");
        Assert.LessOrEqual((float)typeof(PlayerRescuer).GetField("camPitch", Private).GetValue(player), player.firstPersonMaxDown + 0.01f);

        float turnTo = player.transform.eulerAngles.y + 120f;
        typeof(PlayerRescuer).GetField("camYaw", Private).SetValue(player, turnTo);
        yield return WaitSeconds(1f);
        Assert.AreEqual(0f, Mathf.DeltaAngle(player.transform.eulerAngles.y, turnTo), 3f, "the swimmer faces where they look");
    }

    [UnityTest]
    public IEnumerator Third_person_shows_the_whole_swimmer_from_behind()
    {
        player.SetView(PlayerRescuer.PovMode.ThirdPerson);
        yield return null;
        Look(player.transform.eulerAngles.y, 15f, turnTheBody: true);
        Assert.Greater(Head().localScale.x, 0.5f, "the head is on");

        var cam = player.SwimCamera;
        Vector3 pivot = player.transform.position + Vector3.up * player.eyeHeight;
        Assert.AreEqual(player.cameraDistance, Vector3.Distance(cam.position, pivot), 0.3f, "in open water the camera sits back at its distance");
        Assert.Less(Vector3.Dot(cam.position - player.transform.position, player.transform.forward), 0f, "behind the swimmer");

        // The head is in front of the camera and inside its field of view.
        Vector3 toHead = Head().position - cam.position;
        Assert.Greater(Vector3.Dot(toHead, cam.forward), 0.5f);
        Assert.Less(Vector3.Angle(cam.forward, toHead), player.fieldOfView * 0.5f, "the swimmer's head is in the picture");
    }

    [UnityTest]
    public IEnumerator Third_person_keeps_out_of_walls_and_off_the_water()
    {
        player.SetView(PlayerRescuer.PovMode.ThirdPerson);
        yield return null;
        float facing = player.transform.eulerAngles.y;

        // A wall two metres behind the swimmer.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var back = Quaternion.Euler(0f, facing, 0f) * Vector3.back;
        wall.transform.position = player.transform.position + back * 2f + Vector3.up * 1f;
        wall.transform.rotation = Quaternion.Euler(0f, facing, 0f);
        wall.transform.localScale = new Vector3(8f, 6f, 0.4f);
        Physics.SyncTransforms();

        Look(facing, 15f, turnTheBody: true);
        var cam = player.SwimCamera;
        float toWall = Vector3.Dot(wall.transform.position - player.transform.position, back) - 0.2f;
        float camBack = Vector3.Dot(cam.position - player.transform.position, back);
        Assert.Less(camBack, toWall, "the camera stays on the swimmer's side of the wall");
        Object.Destroy(wall);
        Physics.SyncTransforms();

        // Looking up: the camera swings low, but not into the water.
        typeof(PlayerRescuer).GetField("shownDistance", Private).SetValue(player, 0f);
        Look(facing, player.pitchLimits.x, turnTheBody: true);
        float water = WaterSurface.Instance.GetHeight(cam.position);
        Assert.Greater(cam.position.y, water + 0.4f, "the camera stays above the water");
    }

    [UnityTest]
    public IEnumerator The_setting_and_V_switch_the_view()
    {
        GameSettings.SwimView = PlayerRescuer.PovMode.ThirdPerson;
        yield return null;
        Assert.AreEqual(PlayerRescuer.PovMode.ThirdPerson, player.povMode, "the settings page switches the view");

        GameSettings.SwimView = PlayerRescuer.PovMode.FirstPerson;
        yield return null;
        Assert.AreEqual(PlayerRescuer.PovMode.FirstPerson, player.povMode);

        player.SetView(PlayerRescuer.PovMode.ThirdPerson);       // what V does
        Assert.AreEqual(PlayerRescuer.PovMode.ThirdPerson, GameSettings.SwimView, "and the choice is kept");
    }
}
