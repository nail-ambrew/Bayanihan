using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu logo's entrance and shine: it pops in once (scale 0.6 to 1.08 to 1 with a fade), then a soft glint
/// sweeps across it every few seconds. With Reduce Motion the logo is simply there, with no pop and no glint.
/// </summary>
public class LogoFx : MonoBehaviour
{
    public const float PopSeconds = 0.55f;
    public float shimmerEvery = 4.5f;
    public float shimmerSeconds = 0.9f;

    CanvasGroup group;
    RectTransform glint;
    float startedAt;

    public bool Popped => Time.unscaledTime - startedAt >= PopSeconds;
    /// <summary>The glint's position across the logo, 0..1, or -1 when it is not showing.</summary>
    public float GlintAt { get; private set; } = -1f;

    public static LogoFx Add(RectTransform logo)
    {
        var fx = logo.gameObject.AddComponent<LogoFx>();
        fx.Build();
        return fx;
    }

    void Build()
    {
        startedAt = Time.unscaledTime;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        // The glint is clipped to the logo's own shape when the logo is an image, to its box otherwise.
        if (GetComponent<Image>() != null) gameObject.AddComponent<Mask>().showMaskGraphic = true;
        else gameObject.AddComponent<RectMask2D>();
        var img = UiKit.Image("Glint", transform, new Color(1f, 1f, 1f, 0.55f), UiKit.VerticalGradient(new Color(1, 1, 1, 0), new Color(1, 1, 1, 0)));
        img.sprite = GlintSprite();
        img.raycastTarget = false;
        glint = img.rectTransform;
        glint.anchorMin = new Vector2(0, 0);
        glint.anchorMax = new Vector2(0, 1);
        glint.pivot = new Vector2(0.5f, 0.5f);
        glint.sizeDelta = new Vector2(34f, 30f);
        glint.localEulerAngles = new Vector3(0, 0, -18f);
        glint.gameObject.SetActive(false);
        if (GameSettings.ReduceMotion) startedAt -= PopSeconds;     // already in
        Update();
    }

    void Update()
    {
        float t = Time.unscaledTime - startedAt;
        bool calm = GameSettings.ReduceMotion;
        if (t < PopSeconds && !calm)
        {
            float k = t / PopSeconds;
            float s = k < 0.7f ? Mathf.Lerp(0.6f, 1.08f, k / 0.7f) : Mathf.Lerp(1.08f, 1f, (k - 0.7f) / 0.3f);
            transform.localScale = new Vector3(s, s, 1f);
            group.alpha = Mathf.Clamp01(k * 2.5f);
            return;
        }
        transform.localScale = Vector3.one;
        group.alpha = 1f;

        float cycle = (t - PopSeconds) % shimmerEvery;
        bool on = !calm && cycle < shimmerSeconds;
        glint.gameObject.SetActive(on);
        GlintAt = on ? cycle / shimmerSeconds : -1f;
        if (on)
        {
            float w = ((RectTransform)transform).rect.width;
            glint.anchoredPosition = new Vector2(Mathf.Lerp(-40f, w + 40f, Mathf.SmoothStep(0f, 1f, GlintAt)), 0f);
        }
    }

    static Sprite glintSprite;

    /// <summary>A soft vertical band, bright in the middle.</summary>
    static Sprite GlintSprite()
    {
        if (glintSprite != null) return glintSprite;
        const int w = 32, h = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float a = Mathf.Pow(Mathf.Sin(Mathf.PI * (x + 0.5f) / w), 2f);
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        tex.Apply();
        glintSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        return glintSprite;
    }
}
