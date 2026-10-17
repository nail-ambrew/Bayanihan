using NUnit.Framework;
using UnityEngine;

/// <summary>Where the radio ping and the evacuation pointer are drawn on screen.</summary>
public class HudMarkerTests
{
    const float Margin = 20f;

    Camera cam;

    [SetUp]
    public void MakeCamera()
    {
        var go = new GameObject("HudMarker test camera");
        cam = go.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // looking down +Z
        cam.fieldOfView = 60f;
    }

    [TearDown]
    public void DestroyCamera()
    {
        Object.DestroyImmediate(cam.gameObject);
    }

    [Test]
    public void Something_straight_ahead_is_marked_where_it_appears()
    {
        Vector2 at = HudMarker.ScreenPosition(cam, new Vector3(0f, 0f, 10f), Margin);

        Assert.AreEqual(Screen.width * 0.5f, at.x, 1f);
        Assert.AreEqual(Screen.height * 0.5f, at.y, 1f);
    }

    [Test]
    public void Something_straight_behind_goes_to_the_bottom_edge_not_the_middle()
    {
        Vector2 at = HudMarker.ScreenPosition(cam, new Vector3(0f, 0f, -10f), Margin);

        Assert.AreEqual(Screen.width * 0.5f, at.x, 1f);
        Assert.AreEqual(Screen.height - Margin, at.y, 1f);
    }

    [Test]
    public void Something_behind_and_to_the_right_goes_to_the_right_edge()
    {
        Vector2 at = HudMarker.ScreenPosition(cam, new Vector3(30f, 0f, -5f), Margin);

        Assert.AreEqual(Screen.width - Margin, at.x, 1f);
    }

    [Test]
    public void Something_far_off_to_the_left_stays_inside_the_margin()
    {
        Vector2 at = HudMarker.ScreenPosition(cam, new Vector3(-500f, 0f, 5f), Margin);

        Assert.AreEqual(Margin, at.x, 1f);
        Assert.GreaterOrEqual(at.y, Margin - 0.01f);
        Assert.LessOrEqual(at.y, Screen.height - Margin + 0.01f);
    }

    [Test]
    public void Separate_margins_are_respected_on_each_axis()
    {
        // Above and behind: slides to the top edge, which uses the vertical margin.
        Vector2 at = HudMarker.ScreenPosition(cam, new Vector3(0f, 40f, -5f), 80f, 30f);

        Assert.AreEqual(30f, at.y, 1f);
    }
}
