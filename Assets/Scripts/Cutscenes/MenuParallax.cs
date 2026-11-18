using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A little depth for the main menu over its moving background: the menu buttons drift a few units against the
/// pointer (or slowly on their own, on a touch screen), and a vignette darkens the corners. Reduce Motion: no drift.
/// </summary>
public class MenuParallax : MonoBehaviour
{
    public float range = 6f;
    RectTransform target;
    Vector2 home;
    Vector2 offset;

    public Vector2 Offset => offset;

    public static MenuParallax Add(RectTransform target)
    {
        var p = target.gameObject.AddComponent<MenuParallax>();
        p.target = target;
        p.home = target.anchoredPosition;
        return p;
    }

    void Update()
    {
        Vector2 want = Vector2.zero;
        if (!GameSettings.ReduceMotion)
        {
            Vector2 pointer = new Vector2(0.5f, 0.5f);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && !InputHelper.TouchActive)
            {
                var p = Mouse.current.position.ReadValue();
                pointer = new Vector2(p.x / Mathf.Max(1, Screen.width), p.y / Mathf.Max(1, Screen.height));
            }
            else
#endif
            {
                float t = Time.unscaledTime * 0.25f;
                pointer = new Vector2(0.5f + 0.3f * Mathf.Sin(t), 0.5f + 0.2f * Mathf.Sin(t * 0.7f));
            }
            want = -(pointer - new Vector2(0.5f, 0.5f)) * 2f * range;
        }
        offset = Vector2.Lerp(offset, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
        target.anchoredPosition = home + offset;
    }

    /// <summary>A full-screen vignette behind the menu's UI and over the picture.</summary>
    public static Image Vignette(Transform canvasRoot, float strength = 0.55f)
    {
        var img = UiKit.Image("Vignette", canvasRoot, new Color(0, 0, 0, strength), VignetteSprite());
        UiKit.Stretch(img.rectTransform);
        img.raycastTarget = false;
        img.transform.SetAsFirstSibling();
        return img;
    }

    static Sprite vignette;

    static Sprite VignetteSprite()
    {
        if (vignette != null) return vignette;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
            float a = Mathf.SmoothStep(0.45f, 1.25f, d);
            // the menu sits on the left: a little more shade there, so the white words read over any frame
            a = Mathf.Clamp01(a + Mathf.SmoothStep(0.2f, -1f, dx) * 0.35f);
            tex.SetPixel(x, y, new Color(0, 0, 0, a));
        }
        tex.Apply();
        vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return vignette;
    }
}
