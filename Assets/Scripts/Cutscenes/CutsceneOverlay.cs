using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What a cutscene draws over the picture: 2.39:1 letterbox bars that slide in, subtitles for spoken lines, the
/// "BAYANIHAN" title card, and the SKIP button. Made by <see cref="CutsceneManager"/> and destroyed with it.
/// </summary>
public class CutsceneOverlay : MonoBehaviour
{
    public const float Aspect = 2.39f;
    public const float BarSlideSeconds = 0.45f;

    public static CutsceneOverlay Current { get; private set; }

    Canvas canvas;
    RectTransform canvasRect, safe;
    Image top, bottom;
    float barsTarget, bars;
    RectTransform subtitleBox;
    TMP_Text subtitle;
    string subtitleLine;
    RectTransform title;
    CanvasGroup titleGroup;
    TMP_Text titleText, titleTag;
    float titleAt = -1f, titleHold;

    public CutsceneSkipButton Skip { get; private set; }

    /// <summary>The letterbox bars, 0 (gone) .. 1 (fully in).</summary>
    public float Bars => bars;
    /// <summary>The subtitle line on screen now, or null.</summary>
    public string SubtitleShown => subtitleBox != null && subtitleBox.gameObject.activeSelf ? subtitle.text : null;
    public bool TitleShowing => titleGroup != null && titleGroup.alpha > 0.01f;

    public static CutsceneOverlay Create(string name, bool letterbox, bool skippable, System.Action onSkip, int sortingOrder = 900)
    {
        UiKit.EnsureEventSystem();
        var safe = UiKit.NewCanvas(name + "Overlay", sortingOrder, out var canvas, centred: false);
        var o = canvas.gameObject.AddComponent<CutsceneOverlay>();
        o.canvas = canvas;
        o.canvasRect = (RectTransform)canvas.transform;
        o.safe = safe;
        o.Build(letterbox, skippable, onSkip);
        Current = o;
        return o;
    }

    void Build(bool letterbox, bool skippable, System.Action onSkip)
    {
        top = Bar("LetterboxTop", 1f);
        bottom = Bar("LetterboxBottom", 0f);
        barsTarget = letterbox ? 1f : 0f;

        // Subtitles: centred above the bottom bar, on a soft dark backing.
        subtitleBox = UiKit.Rect("Subtitles", safe);
        subtitleBox.anchorMin = new Vector2(0.5f, 0f);
        subtitleBox.anchorMax = new Vector2(0.5f, 0f);
        subtitleBox.pivot = new Vector2(0.5f, 0f);
        subtitleBox.sizeDelta = new Vector2(560, 34);
        var back = UiKit.Panel("Backing", subtitleBox, new Color(0, 0, 0, 0.55f), 6f);
        UiKit.Stretch(back.rectTransform);
        subtitle = UiKit.Label("Line", subtitleBox, "", UiTheme.BodySize + 2, Color.white, TextAnchor.MiddleCenter);
        UiKit.Inset(subtitle.rectTransform, 12, 4, 12, 4);
        subtitleBox.gameObject.SetActive(false);

        // Title card: BAYANIHAN! over its tagline, orange like the logo.
        title = UiKit.Rect("TitleCard", safe);
        UiKit.Place(title, UiKit.Center, new Vector2(0, 18), new Vector2(620, 120));
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleGroup.blocksRaycasts = false;
        titleText = UiKit.Label("Title", title, "BAYANIHAN!", 64, UiTheme.Orange, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(titleText.rectTransform, UiKit.Top, Vector2.zero, new Vector2(620, 80));
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.TextOutline(UiTheme.OrangeBurnt, 0.2f);
        titleText.TextShadow(new Color(0, 0, 0, 0.6f), 1.2f);
        titleTag = UiKit.Label("Tagline", title, "WHEN THE WATER RISES, WE RISE TOGETHER", UiTheme.BodySize + 4, Color.white, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(titleTag.rectTransform, UiKit.Bottom, new Vector2(0, 4), new Vector2(620, 28));
        titleTag.TextShadow(new Color(0, 0, 0, 0.7f));

        if (skippable) Skip = CutsceneSkipButton.Create(safe, onSkip);
    }

    Image Bar(string name, float edge)
    {
        var img = UiKit.Image(name, canvas.transform, Color.black);
        img.rectTransform.anchorMin = new Vector2(0, edge);
        img.rectTransform.anchorMax = new Vector2(1, edge);
        img.rectTransform.pivot = new Vector2(0.5f, edge);
        img.rectTransform.sizeDelta = Vector2.zero;
        img.raycastTarget = false;
        img.transform.SetAsFirstSibling();
        return img;
    }

    /// <summary>The bars' full height in canvas units: whatever the screen has beyond a 2.39:1 picture.</summary>
    public float BarHeight
    {
        get
        {
            var size = canvasRect.rect.size;
            return Mathf.Max(0f, (size.y - size.x / Aspect) * 0.5f);
        }
    }

    public void SetLetterbox(bool on) => barsTarget = on ? 1f : 0f;

    /// <summary>Shows a spoken line (if Subtitles are on); null or empty hides it.</summary>
    public void ShowSubtitle(string line)
    {
        subtitleLine = string.IsNullOrEmpty(line) ? null : line;
        bool show = subtitleLine != null && GameSettings.Subtitles;
        subtitleBox.gameObject.SetActive(show);
        if (show) subtitle.text = subtitleLine;
    }

    /// <summary>The title card: pops in, holds for <paramref name="hold"/> seconds, fades.</summary>
    public void ShowTitle(float hold = 2.2f)
    {
        titleAt = Time.unscaledTime;
        titleHold = hold;
    }

    void LateUpdate()
    {
        bars = Mathf.MoveTowards(bars, barsTarget, Time.unscaledDeltaTime / BarSlideSeconds);
        float h = BarHeight * Mathf.SmoothStep(0f, 1f, bars);
        top.rectTransform.sizeDelta = new Vector2(0, h);
        bottom.rectTransform.sizeDelta = new Vector2(0, h);
        subtitleBox.anchoredPosition = new Vector2(0, Mathf.Max(h - safe.offsetMin.y, 0f) + 14f);

        if (titleAt >= 0f)
        {
            float t = Time.unscaledTime - titleAt;
            float a = t < 0.25f ? t / 0.25f : t < 0.25f + titleHold ? 1f : 1f - (t - 0.25f - titleHold) / 0.6f;
            titleGroup.alpha = Mathf.Clamp01(a);
            // Pop: overshoot to 1.12, settle at 1. Reduce Motion: no scale, just the fade.
            float s = GameSettings.ReduceMotion ? 1f : t < 0.25f ? Mathf.Lerp(0.6f, 1.12f, t / 0.25f) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((t - 0.25f) / 0.2f));
            title.localScale = new Vector3(s, s, 1f);
            if (a <= 0f && t > 0.25f) titleAt = -1f;
        }
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;
}
