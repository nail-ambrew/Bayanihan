using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The drawn shapes every screen is made of: rounded corners follow their circle with no step, pills are not squashed,
/// and a radius never asks for more than a box can hold.
/// </summary>
public class UiShapeTests
{
    const int TexelsPerUnit = 4;

    /// <summary>
    /// Checks a rounded sprite against the geometry: a texel more than a texel outside its corner's circle is clear, one
    /// more than a texel inside it (or not in any corner) is solid.
    /// </summary>
    static void AssertFollowsCurves(float tl, float tr, float br, float bl)
    {
        var sprite = UiKit.Rounded(tl, tr, br, bl);
        var tex = sprite.texture;
        int w = tex.width, h = tex.height;
        int[] r = { Mathf.RoundToInt(tl * TexelsPerUnit), Mathf.RoundToInt(tr * TexelsPerUnit), Mathf.RoundToInt(br * TexelsPerUnit), Mathf.RoundToInt(bl * TexelsPerUnit) };
        var px = tex.GetPixels32();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float worst = float.MinValue;   // how far outside the nearest relevant corner circle this texel is
            bool inAnyCorner = false;
            Check(r[0], fx < r[0], fy > h - r[0], r[0], h - r[0]);
            Check(r[1], fx > w - r[1], fy > h - r[1], w - r[1], h - r[1]);
            Check(r[2], fx > w - r[2], fy < r[2], w - r[2], r[2]);
            Check(r[3], fx < r[3], fy < r[3], r[3], r[3]);
            byte a = px[y * w + x].a;
            string where = $"texel ({x},{y}) of Rounded({tl},{tr},{br},{bl})";
            if (!inAnyCorner || worst < -1f) Assert.AreEqual(255, a, where + " should be solid");
            else if (worst > 1f) Assert.AreEqual(0, a, where + " is outside the curve and should be clear");

            void Check(int radius, bool inX, bool inY, float cx, float cy)
            {
                if (radius <= 0 || !inX || !inY) return;
                inAnyCorner = true;
                worst = Mathf.Max(worst, Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy)) - radius);
            }
        }
    }

    [Test]
    public void The_menu_tab_is_square_on_the_left_and_a_clean_half_circle_on_the_right() => AssertFollowsCurves(0f, 19f, 19f, 0f);

    [Test]
    public void A_HUD_edge_strip_rounds_only_its_left_corners() => AssertFollowsCurves(UiTheme.RadiusCard, 0f, 0f, UiTheme.RadiusCard);

    [Test]
    public void Equal_corners_are_still_round() => AssertFollowsCurves(UiTheme.RadiusBoard, UiTheme.RadiusBoard, UiTheme.RadiusBoard, UiTheme.RadiusBoard);

    [Test]
    public void A_glyph_with_big_top_and_small_bottom_corners_follows_both() => AssertFollowsCurves(4f, 4f, 1f, 1f);

    [Test]
    public void The_nine_slice_border_is_exactly_the_corner_so_a_pill_is_not_squashed()
    {
        const float h = 32f;                         // the pause menu's pill: radius half its height
        var sprite = UiKit.Rounded(h * 0.5f);
        float unitsPerTexel = 100f / sprite.pixelsPerUnit;
        Vector4 b = sprite.border * unitsPerTexel;   // left, bottom, right, top in canvas units
        Assert.AreEqual(h, b.y + b.w, 1e-3f, "top and bottom corners together are exactly the pill's height");
        Assert.LessOrEqual(b.x + b.z, sprite.rect.width * unitsPerTexel, "the corners fit side by side");
    }

    [Test]
    public void Fit_never_lets_a_radius_pass_half_the_short_side()
    {
        Assert.AreEqual(2f, UiKit.Fit(UiTheme.RadiusSmall, new Vector2(660, 4)), "the loading bar is 4 high");
        Assert.AreEqual(3f, UiKit.Fit(UiTheme.RadiusSmall, new Vector2(100, 6)), "the slider is 6 high");
        Assert.AreEqual(UiTheme.RadiusBoard, UiKit.Fit(UiTheme.RadiusBoard, new Vector2(300, 200)), "a big board keeps its radius");
    }

    [Test]
    public void The_shape_scale_steps_down_from_boards_to_small_boxes()
    {
        Assert.Greater(UiTheme.RadiusBoard, UiTheme.RadiusPanel);
        Assert.Greater(UiTheme.RadiusPanel, UiTheme.RadiusCard);
        Assert.Greater(UiTheme.RadiusCard, UiTheme.RadiusButton);
        Assert.Greater(UiTheme.RadiusButton, UiTheme.RadiusSmall);
        Assert.Greater(UiTheme.RadiusSmall, 1f, "a field's fill is its rim's radius minus one, and must stay round");
    }
}
