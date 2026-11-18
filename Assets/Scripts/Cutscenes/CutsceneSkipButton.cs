using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The SKIP button every cutscene shows, bottom right from its first frame: a dark, semi-transparent pill with a white
/// label, an orange outline and a key hint. Hovering or pressing brightens it and grows it to 1.05x, with a click.
/// Esc, Space, Enter and a gamepad's Start or B (East) skip too.
///
/// To restyle it, change the values in <see cref="Style"/>.
/// </summary>
public class CutsceneSkipButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
    IPointerUpHandler, IPointerClickHandler
{
    public static class Style
    {
        public static Vector2 Size = new Vector2(122, 36);
        /// <summary>From the bottom-right corner of the safe area.</summary>
        public static Vector2 Margin = new Vector2(-20, 18);
        public static Color Fill = new Color(0.05f, 0.04f, 0.03f, 0.55f);
        public static Color FillHover = new Color(0.12f, 0.09f, 0.06f, 0.82f);
        public static Color Outline = UiTheme.Orange;
        public static Color OutlineHover = new Color(1f, 0.78f, 0.5f);
        public static Color Label = Color.white;
        public static Color Hint = new Color(1f, 1f, 1f, 0.7f);
        public static float OutlineWidth = 2f;
        public static float HoverScale = 1.05f;
        public static float PressScale = 0.98f;
        public const string Text = "SKIP";
    }

    /// <summary>Key presses in the first moments of a cutscene are not taken as a skip: the Enter or Space that launched
    /// it may still be down.</summary>
    public const float KeyGraceSeconds = 0.2f;
    /// <summary>...and the first few frames, however long they take (a heavy scene's first frames can be slow).</summary>
    public const int KeyGraceFrames = 3;

    public event Action Pressed;

    Image fill, frame;
    TMP_Text hint;
    bool hover, down, used;
    float shownAt;
    int shownFrame;

    /// <summary>The button's own rect, for tests.</summary>
    public RectTransform Rect => (RectTransform)transform;

    public static CutsceneSkipButton Create(Transform safeArea, Action onSkip)
    {
        var root = UiKit.Rect("SkipButton", safeArea);
        UiKit.Place(root, UiKit.BottomRight, Style.Margin, Style.Size);
        var b = root.gameObject.AddComponent<CutsceneSkipButton>();
        b.Build();
        if (onSkip != null) b.Pressed += onSkip;
        return b;
    }

    void Build()
    {
        shownAt = Time.unscaledTime;
        shownFrame = Time.frameCount;
        // A see-through dark pill inside a solid orange frame (an Outline effect would show through the fill).
        float radius = Style.Size.y * 0.5f;
        fill = UiKit.Image("Pill", transform, Style.Fill, UiKit.Rounded(radius));
        UiKit.Stretch(fill.rectTransform);
        fill.raycastTarget = true;
        frame = UiKit.Image("Frame", transform, Style.Outline, Frame(radius, Style.OutlineWidth));
        UiKit.Stretch(frame.rectTransform);
        frame.raycastTarget = false;

        // [Esc]  SKIP  ▶▶  — the arrows are drawn, so they never depend on a font having the glyph.
        string key = KeyHint();
        hint = UiKit.Label("Hint", transform, key, UiTheme.SmallSize + 1, Style.Hint, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(hint.rectTransform, UiKit.Left, new Vector2(10, 0), new Vector2(34, 18));
        var cap = UiKit.Panel("KeyCap", hint.transform, new Color(1, 1, 1, 0.14f), 4f);
        UiKit.Stretch(cap.rectTransform);
        cap.transform.SetAsFirstSibling();
        cap.raycastTarget = false;
        hint.gameObject.SetActive(key.Length > 0);          // touch screens: no key to name

        var label = UiKit.Label("Label", transform, Style.Text, UiTheme.ButtonSize, Style.Label, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(label.rectTransform, UiKit.Center, new Vector2(key.Length > 0 ? 8 : -6, 0), new Vector2(50, Style.Size.y));
        label.textWrappingMode = TextWrappingModes.NoWrap;
        for (int i = 0; i < 2; i++)
        {
            var arrow = UiKit.Image("Arrow" + i, transform, Style.Label, UiKit.Triangle());
            UiKit.Place(arrow.rectTransform, UiKit.Right, new Vector2(-12 - (1 - i) * 8, -1), new Vector2(9, 10));
            arrow.rectTransform.localEulerAngles = new Vector3(0, 0, -90);   // the triangle points up; turn it right
            arrow.raycastTarget = false;
        }
    }

    /// <summary>A hollow rounded frame, <paramref name="thickness"/> canvas units wide, 9-sliced like UiKit.Rounded.</summary>
    static Sprite Frame(float radius, float thickness)
    {
        const int k = 4;                                  // texels per canvas unit
        int r = Mathf.RoundToInt(radius * k), t = Mathf.Max(1, Mathf.RoundToInt(thickness * k));
        int w = r * 2 + 4, h = r * 2 + 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float outer = UiKit.RoundedAlpha(x + 0.5f, y + 0.5f, w, h, r, r, r, r);
            float inner = UiKit.RoundedAlpha(x + 0.5f - t, y + 0.5f - t, w - 2 * t, h - 2 * t, r - t, r - t, r - t, r - t);
            bool inside = x >= t && y >= t && x < w - t && y < h - t;
            float a = outer * (1f - (inside ? inner : 0f));
            px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * k, 0, SpriteMeshType.FullRect,
                             new Vector4(r + 1, r + 1, r + 1, r + 1));
    }

    static string KeyHint()
    {
        if (InputHelper.TouchActive) return "";
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null && Keyboard.current == null) return "B";
#endif
        return "Esc";
    }

    /// <summary>Skips, as a click or a key would. Only the first press counts.</summary>
    public void Press()
    {
        if (used) return;
        used = true;
        UiSound.Play(UiSoundKind.Click);
        Pressed?.Invoke();
    }

    /// <summary>Esc, Space, Enter, or a gamepad's Start or B pressed this frame.</summary>
    public static bool SkipKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && (k.escapeKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame ||
                          k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) return true;
        var g = Gamepad.current;
        if (g != null && (g.startButton.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame)) return true;
        return false;
#else
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
               Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton7) || Input.GetKeyDown(KeyCode.JoystickButton1);
#endif
    }

    void Update()
    {
        if (!used && Time.unscaledTime - shownAt >= KeyGraceSeconds && Time.frameCount - shownFrame > KeyGraceFrames &&
            SkipKeyPressed()) Press();

        bool lit = hover || down;
        float target = down ? Style.PressScale * Style.HoverScale : lit ? Style.HoverScale : 1f;
        float s = Mathf.MoveTowards(transform.localScale.x, target, Time.unscaledDeltaTime * 1.2f);
        transform.localScale = new Vector3(s, s, 1f);
        float k = Time.unscaledDeltaTime * 12f;
        fill.color = Color.Lerp(fill.color, lit ? Style.FillHover : Style.Fill, k);
        frame.color = Color.Lerp(frame.color, lit ? Style.OutlineHover : Style.Outline, k);
    }

    public void OnPointerEnter(PointerEventData e) { hover = true; UiSound.Play(UiSoundKind.Hover); }
    public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
    public void OnPointerDown(PointerEventData e) => down = true;
    public void OnPointerUp(PointerEventData e) => down = false;
    public void OnPointerClick(PointerEventData e) => Press();

    /// <summary>Hovered or pressed now (for tests of the highlight).</summary>
    public bool Lit => hover || down;
    public float Scale => transform.localScale.x;
    public void SetHover(bool on) => hover = on;
}
