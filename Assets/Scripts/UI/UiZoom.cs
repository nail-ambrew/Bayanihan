using UnityEngine;

/// <summary>
/// Draws a full-screen layer bigger than the Figma frame without moving it off the screen: the layer is made
/// 1/zoom the size of its parent and scaled up by zoom, so it still fills the parent exactly and everything anchored
/// to its corners stays in the corners, just larger. The HUD and the touch controls use it so they read and tap like a
/// phone game's, while the menus keep the Figma frame.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UiZoom : MonoBehaviour
{
    float zoom = 1f;
    Vector2 fitted = new Vector2(-1f, -1f);

    public float Zoom
    {
        get => zoom;
        set { zoom = Mathf.Max(0.1f, value); fitted = new Vector2(-1f, -1f); Fit(); }
    }

    public static UiZoom Apply(RectTransform rt, float zoom)
    {
        var z = rt.GetComponent<UiZoom>();
        if (z == null) z = rt.gameObject.AddComponent<UiZoom>();
        z.Zoom = zoom;
        return z;
    }

    void OnEnable() => Fit();
    void LateUpdate() => Fit();

    void Fit()
    {
        var rt = (RectTransform)transform;
        var parent = rt.parent as RectTransform;
        if (parent == null) return;
        Vector2 size = parent.rect.size;
        if (size == fitted) return;
        fitted = size;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size / zoom;
        rt.localScale = new Vector3(zoom, zoom, 1f);
    }
}
