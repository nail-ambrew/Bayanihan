using UnityEngine;

/// <summary>
/// Works out where an OnGUI marker for something in the world belongs: on top of it while it is in view, slid out
/// to the screen edge in its direction when it is not. Shared by the radio ping and the evacuation-centre pointer.
/// </summary>
public static class HudMarker
{
    /// <summary>
    /// GUI-space position (origin top-left) for a world point, kept at least <paramref name="margin"/> pixels in
    /// from every edge.
    /// </summary>
    public static Vector2 ScreenPosition(Camera cam, Vector3 world, float margin)
    {
        return ScreenPosition(cam, world, margin, margin);
    }

    /// <summary>As above, with separate margins for the left/right and top/bottom edges.</summary>
    public static Vector2 ScreenPosition(Camera cam, Vector3 world, float marginX, float marginY)
    {
        float w = Screen.width, h = Screen.height;
        Vector3 sp = cam.WorldToScreenPoint(world);

        bool inView = sp.z > 0f && sp.x >= marginX && sp.x <= w - marginX && sp.y >= marginY && sp.y <= h - marginY;
        if (!inView)
        {
            // Off to one side, or behind: push it to the edge along the direction it lies in as the camera sees
            // it. Mirroring a behind-the-camera point (the obvious trick) can land it mid-screen, which reads as
            // "straight ahead" when it is really straight behind.
            Vector3 local = cam.transform.InverseTransformPoint(world);
            var dir = new Vector2(local.x, local.y);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;       // dead astern
            dir.Normalize();

            float halfW = w * 0.5f - marginX, halfH = h * 0.5f - marginY;
            float reachX = Mathf.Abs(dir.x) > 1e-5f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float reachY = Mathf.Abs(dir.y) > 1e-5f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            float reach = Mathf.Min(reachX, reachY);

            sp.x = w * 0.5f + dir.x * reach;
            sp.y = h * 0.5f + dir.y * reach;
        }

        return new Vector2(sp.x, h - sp.y);     // GUI y runs downward
    }
}
