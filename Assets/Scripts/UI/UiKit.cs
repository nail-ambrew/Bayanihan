using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>How a button is painted.</summary>
public enum ButtonStyle
{
    /// <summary>Orange with white text: Login, CONTINUE, NO.</summary>
    Primary,
    /// <summary>Cream with orange text: RECOMMENDED PACK, CLEAR, YES, Return to Login.</summary>
    Secondary,
    /// <summary>Green with dark text: LAUNCH.</summary>
    Go,
    /// <summary>Just text, for links: "Forgot password?", "Create new account".</summary>
    Link
}

/// <summary>
/// Builds the Figma screens out of uGUI in code: a canvas laid out in Figma units, labels in the two faces, rounded
/// panels, the four kinds of button, the menu items with their orange tab, dialogs, sliders and scroll views.
/// Every screen in the game uses this, so they all look alike and none of them needs a prefab.
/// </summary>
public static class UiKit
{
    // ------------------------------------------------------------------ fonts
    // Every label is a TextMesh Pro label: signed-distance-field glyphs stay sharp at any size and scale, where the
    // legacy uGUI Text baked a bitmap per size and went soft and jagged on a phone.

    public static TMP_FontAsset HeadingFont => UiAssets.Current.headingFont != null ? UiAssets.Current.headingFont : TMP_Settings.defaultFontAsset;
    public static TMP_FontAsset BodyFont => UiAssets.Current.bodyFont != null ? UiAssets.Current.bodyFont : TMP_Settings.defaultFontAsset;
    /// <summary>The default font has no chunky weight of its own, so headings set in it are made bold.</summary>
    static FontStyles HeadingStyle => UiAssets.Current.headingFont != null ? FontStyles.Normal : FontStyles.Bold;

    /// <summary>The uGUI anchor the screens are written in, as TMP's alignment.</summary>
    public static TextAlignmentOptions Align(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
            case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
            default:                      return TextAlignmentOptions.Center;
        }
    }

    // ------------------------------------------------------------------ text effects
    // uGUI's Shadow and Outline components do nothing to a TMP label, so the drop shadow is the font material's
    // underlay and the logo's edge its outline. Each look is one shared material, made once from the preset in
    // UiAssets (whose keyword keeps the shader variant in the build), so labels that look alike still batch.
    static readonly Dictionary<string, Material> textEffects = new Dictionary<string, Material>();

    /// <summary>A drop shadow under the label, down and to the right; <paramref name="offset"/> is in SDF units (about a tenth of an em at 1).</summary>
    public static TMP_Text TextShadow(this TMP_Text t, Color colour, float offset = 0.6f)
    {
        t.fontSharedMaterial = TextEffect(t.font, false, colour, offset);
        return t;
    }

    /// <summary>An edge round each letter; <paramref name="width"/> is TMP's outline width (0..1).</summary>
    public static TMP_Text TextOutline(this TMP_Text t, Color colour, float width = 0.25f)
    {
        t.fontSharedMaterial = TextEffect(t.font, true, colour, width);
        return t;
    }

    static Material TextEffect(TMP_FontAsset font, bool outline, Color colour, float amount)
    {
        string key = $"{font.GetInstanceID()}/{(outline ? "o" : "s")}/{colour}/{amount}";
        if (textEffects.TryGetValue(key, out var cached) && cached != null) return cached;

        var a = UiAssets.Current;
        var preset = outline ? a.headingOutline : (font == a.bodyFont ? a.bodyShadow : a.headingShadow);
        // A preset made for another font would draw this font's glyphs from the wrong atlas.
        if (preset == null || preset.mainTexture != font.material.mainTexture) preset = font.material;

        var mat = new Material(preset) { name = font.name + (outline ? " Outline " : " Shadow ") + colour };
        if (outline)
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, colour);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, amount);
        }
        else
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, colour);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, amount);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -amount);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.15f);
        }
        textEffects[key] = mat;
        return mat;
    }

    // ------------------------------------------------------------------ canvas
    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    /// <summary>
    /// A screen-space canvas laid out on the Figma frame. Returns the safe area inside it (clear of the notch and the
    /// rounded corners), which is where everything but full-screen backgrounds goes.
    /// </summary>
    /// <param name="centred">Menus: on a screen taller than the Figma frame, keep the frame's shape and centre it.
    /// False for the HUD, whose pieces belong in the screen's corners.</param>
    public static RectTransform NewCanvas(string name, int sortingOrder, out Canvas canvas, bool centred = true)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = UiTheme.Reference;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // the whole Figma frame always fits
        var safe = Rect("SafeArea", go.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>().FitDesign = centred ? SafeArea.MenuFit : 0f;
        return safe;
    }

    /// <summary>The flooded-street still behind the menus, or a dark gradient when it has not been exported yet.</summary>
    public static Image Background(Transform canvasRoot)
    {
        var img = Image("Background", canvasRoot, Color.white);
        var art = UiAssets.Current.background;
        if (art != null)
        {
            img.sprite = art;
            img.preserveAspect = false;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = art.rect.width / Mathf.Max(1f, art.rect.height);
        }
        else
        {
            img.sprite = VerticalGradient(new Color(0.36f, 0.36f, 0.36f), new Color(0.18f, 0.14f, 0.10f));
            Stretch(img.rectTransform);
        }
        img.raycastTarget = false;
        // the image itself is behind the safe area, so it fills the whole screen
        img.transform.SetAsFirstSibling();
        return img;
    }

    /// <summary>BAYANIHAN! / DISASTER RESCUE GAME, top left as in every Figma frame.</summary>
    public static RectTransform Logo(Transform parent, Vector2 anchor, Vector2 pos, float width = 150f)
    {
        var art = UiAssets.Current.logo;
        if (art != null)
        {
            var img = Image("Logo", parent, Color.white);
            img.sprite = art;
            img.preserveAspect = true;
            Place(img.rectTransform, anchor, pos, new Vector2(width, width * art.rect.height / Mathf.Max(1f, art.rect.width)));
            return img.rectTransform;
        }

        var root = Rect("Logo", parent);
        Place(root, anchor, pos, new Vector2(width, width * 0.36f));
        var name = Label("Name", root, "BAYANIHAN!", Mathf.RoundToInt(width * 0.175f), UiTheme.Orange, TextAnchor.UpperLeft, heading: true);
        Place(name.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(width, width * 0.24f));
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.TextOutline(UiTheme.OrangeBurnt, 0.2f);
        var tag = Label("Tagline", root, "DISASTER RESCUE GAME", Mathf.RoundToInt(width * 0.062f), UiTheme.Orange, TextAnchor.UpperLeft, heading: true);
        Place(tag.rectTransform, new Vector2(0, 1), new Vector2(1f, -width * 0.235f), new Vector2(width, width * 0.1f));
        tag.textWrappingMode = TextWrappingModes.NoWrap;
        tag.TextOutline(UiTheme.OrangeBurnt, 0.3f);
        return root;
    }

    /// <summary>The build number, bottom right of every menu frame.</summary>
    public static TMP_Text Version(Transform parent)
    {
        var t = Label("Version", parent, Application.version, UiTheme.SmallSize + 1, UiTheme.White, TextAnchor.LowerRight, heading: true);
        Place(t.rectTransform, new Vector2(1, 0), new Vector2(-14, 8), new Vector2(80, 14));
        return t;
    }

    // ------------------------------------------------------------------ primitives
    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // UI
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = sprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        }
        return img;
    }

    /// <summary>A rounded panel. Radius in Figma units.</summary>
    public static Image Panel(string name, Transform parent, Color color, float radius = UiTheme.Radius)
    {
        return Image(name, parent, color, Rounded(radius));
    }

    /// <summary>
    /// A TextMesh Pro label in one of the two faces. It wraps, and a line that does not fit its rect still shows
    /// (TMP's lines are a little taller than the legacy Text's, so cutting them off would hide labels in tight rows).
    /// </summary>
    public static TextMeshProUGUI Label(string name, Transform parent, string text, int size, Color color,
                                        TextAnchor align = TextAnchor.UpperLeft, bool heading = false)
    {
        var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        var font = heading ? HeadingFont : BodyFont;
        if (font != null) t.font = font;
        t.fontStyle = heading ? HeadingStyle : FontStyles.Normal;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = Align(align);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.enableAutoSizing = false;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

    /// <summary>Lets long text shrink to fit instead of running out of its rect (descriptions on small cards).</summary>
    public static TMP_Text Shrink(this TMP_Text t, int min)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = t.fontSize;
        return t;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void Inset(RectTransform rt, float left, float top, float right, float bottom)
    {
        Stretch(rt);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static void Inset(RectTransform rt, float all) => Inset(rt, all, all, all, all);

    /// <summary>Anchor and pivot at the same point, then position and size from there.</summary>
    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static readonly Vector2 TopLeft = new Vector2(0, 1), TopRight = new Vector2(1, 1), Top = new Vector2(0.5f, 1),
        Center = new Vector2(0.5f, 0.5f), Left = new Vector2(0, 0.5f), Right = new Vector2(1, 0.5f),
        Bottom = new Vector2(0.5f, 0), BottomLeft = Vector2.zero, BottomRight = new Vector2(1, 0);

    // ------------------------------------------------------------------ buttons
    /// <summary>
    /// A button in one of the Figma styles. Its label is returned too, so it can be changed later. It clicks when pressed (a
    /// Go button sounds a go-ahead); <paramref name="sound"/> gives it another sound, or <see cref="UiSoundKind.None"/> for none.
    /// </summary>
    public static Button Button(Transform parent, string text, ButtonStyle style, UnityAction onClick, out TMP_Text label,
                                int fontSize = UiTheme.ButtonSize, UiSoundKind? sound = null)
    {
        var press = sound ?? (style == ButtonStyle.Go ? UiSoundKind.Confirm : UiSoundKind.Click);
        Color fill, ink;
        switch (style)
        {
            case ButtonStyle.Secondary: fill = UiTheme.CreamLight; ink = UiTheme.OrangeDeep; break;
            case ButtonStyle.Go:        fill = UiTheme.Green; ink = UiTheme.Ink; break;
            case ButtonStyle.Link:      fill = new Color(0, 0, 0, 0); ink = UiTheme.InkSoft; break;
            default:                    fill = UiTheme.Orange; ink = UiTheme.White; break;
        }

        var img = style == ButtonStyle.Link ? Image("Button_" + text, parent, fill) : Panel("Button_" + text, parent, fill, UiTheme.SmallRadius);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var c = b.colors;
        c.normalColor = Color.white;
        c.highlightedColor = new Color(1f, 0.93f, 0.86f);
        c.selectedColor = Color.white;
        c.pressedColor = new Color(0.82f, 0.72f, 0.62f);
        c.disabledColor = new Color(1f, 1f, 1f, 0.45f);
        c.fadeDuration = 0.06f;
        b.colors = c;
        b.onClick.AddListener(() => UiSound.Play(press));
        if (onClick != null) b.onClick.AddListener(onClick);

        label = Label("Label", img.transform, text, fontSize, ink, style == ButtonStyle.Link ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                      heading: style != ButtonStyle.Link);
        // One line, shrinking a little rather than spilling out of a narrow button.
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.Shrink(Mathf.Max(6, fontSize * 2 / 3));
        Inset(label.rectTransform, 4, 0, 4, 0);
        if (style == ButtonStyle.Link)
        {
            c.highlightedColor = Color.white;
            b.colors = c;
            b.transition = Selectable.Transition.None;
            label.raycastTarget = true;
        }
        return b;
    }

    public static Button Button(Transform parent, string text, ButtonStyle style, UnityAction onClick, int fontSize = UiTheme.ButtonSize,
                                UiSoundKind? sound = null) =>
        Button(parent, text, style, onClick, out _, fontSize, sound);

    /// <summary>
    /// One line of a Figma menu: the orange tab and a white word. The highlighted line (hovered, or the last one
    /// tapped) becomes a full orange pill with cream text, as in the Figma frames.
    /// </summary>
    public static UiMenuItem MenuItem(Transform parent, string text, UnityAction onClick, float height = 36f, float width = 170f)
    {
        var root = Rect("Menu_" + text, parent);
        root.sizeDelta = new Vector2(width, height);
        var item = root.gameObject.AddComponent<UiMenuItem>();
        item.Build(text, onClick, height);
        return item;
    }

    /// <summary>
    /// A column of menu items down the left of the screen, under the logo: the Figma main menu, pause and mission
    /// complete frames all lay their menus out like this.
    /// </summary>
    public static RectTransform MenuColumn(Transform parent, Vector2 topLeft, float itemHeight, float gap, float width = 170f)
    {
        UiMenuItem.ClearHighlight();
        var col = Rect("Menu", parent);
        col.anchorMin = col.anchorMax = col.pivot = TopLeft;
        col.anchoredPosition = topLeft;
        col.sizeDelta = new Vector2(width, 10);
        var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = gap;
        v.childControlWidth = false;
        v.childControlHeight = false;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        col.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return col;
    }

    /// <summary>
    /// The see-through orange board of the Figma "Mission Complete" frame: a white title across the top and a paper
    /// card under it. The pages that have no frame of their own (How to Play, Settings, Credits, the full report)
    /// use it too. Returns the paper card; its heading goes in <paramref name="cardTitle"/>.
    /// </summary>
    public static RectTransform Board(Transform parent, string title, string cardTitle, Sprite cardIcon, out TMP_Text titleText, out TMP_Text cardHeading,
                                      float left = 201f, UnityAction close = null, string name = "Board")
    {
        // From `left` to 43 units in from the right edge, 30 down from the top, 335 tall (the Figma board).
        var board = Panel(name, parent, UiTheme.Board, UiTheme.RadiusBoard);
        var brt = board.rectTransform;
        brt.anchorMin = new Vector2(0, 1);
        brt.anchorMax = new Vector2(1, 1);
        brt.pivot = new Vector2(0.5f, 1);
        brt.offsetMin = new Vector2(left, -30 - 335);
        brt.offsetMax = new Vector2(-43, -30);
        board.raycastTarget = true;
        float side = left > 230f ? 26f : 86f;      // a narrower board keeps less margin round its card
        titleText = Label("Title", board.transform, title, UiTheme.TitleSize + 4, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        float titleSide = close != null ? 40f : 12f;     // clear of the back arrow, and centred all the same
        Inset(titleText.rectTransform, titleSide, 10, titleSide, 335 - 44);
        titleText.TextShadow(UiTheme.TitleShadow);
        // A board can always be closed by tapping, not only with Esc or Android's back key: iOS has neither.
        if (close != null) BackButton(board.transform, TopLeft, new Vector2(6, -8), close, 18f, UiTheme.White).name = "Close";

        var paper = Panel("Card", board.transform, UiTheme.Paper, UiTheme.RadiusPanel);
        Inset(paper.rectTransform, side, 56, side, 17);
        paper.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.25f);

        cardHeading = Label("Heading", paper.transform, cardTitle, UiTheme.TitleSize + 2, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
        Place(cardHeading.rectTransform, TopLeft, new Vector2(cardIcon != null ? 74 : 24, -8), new Vector2(340, 34));
        if (cardIcon != null) OptionalIcon(paper.transform, cardIcon, TopLeft, new Vector2(36, -8), new Vector2(32, 32));
        Rule(paper.transform, -46f);

        var body = Rect("Body", paper.transform);
        Inset(body, 16, 52, 16, 10);
        return body;
    }

    /// <summary>
    /// The orange back arrow at the top left of the sign-in card and the loadout: the Figma export when there is one,
    /// otherwise a drawn chevron. The touch target is bigger than the arrow.
    /// </summary>
    /// <param name="colour">The drawn arrow's colour: orange on paper and over the street, white on an orange board.</param>
    public static Button BackButton(Transform parent, Vector2 anchor, Vector2 pos, UnityAction onClick, float size = 20f, Color? colour = null)
    {
        Color ink = colour ?? UiTheme.Orange;
        var hit = Image("Back", parent, new Color(0, 0, 0, 0));
        Place(hit.rectTransform, anchor, pos, new Vector2(size + 16f, size + 12f));
        hit.raycastTarget = true;
        var art = UiAssets.Current.backArrow;
        if (art != null)
        {
            var img = Image("Arrow", hit.transform, Color.white, art);
            img.preserveAspect = true;
            Place(img.rectTransform, Center, Vector2.zero, new Vector2(size, size * 0.6f));
        }
        else
        {
            float len = size * 0.55f, thick = Mathf.Max(2.5f, size * 0.16f);
            for (int i = 0; i < 2; i++)
            {
                var bar = Image("Chevron", hit.transform, ink, Rounded(thick * 0.5f));
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                bar.rectTransform.pivot = new Vector2(0f, 0.5f);
                bar.rectTransform.sizeDelta = new Vector2(len, thick);
                bar.rectTransform.anchoredPosition = new Vector2(-size * 0.22f, 0);
                bar.rectTransform.localRotation = Quaternion.Euler(0, 0, i == 0 ? 40f : -40f);
            }
            var shaft = Image("Shaft", hit.transform, ink, Rounded(thick * 0.5f));
            Place(shaft.rectTransform, Center, new Vector2(size * 0.08f, 0), new Vector2(size * 0.62f, thick));
        }
        var b = hit.gameObject.AddComponent<Button>();
        PressFeedback(b);
        b.onClick.AddListener(() => UiSound.Play(UiSoundKind.Back));
        if (onClick != null) b.onClick.AddListener(onClick);
        return b;
    }

    /// <summary>
    /// A press the finger can see on a control that has no fill of its own (the back arrow, the item cards): it shrinks
    /// a little while held, the way the orange buttons darken.
    /// </summary>
    public static void PressFeedback(Selectable s)
    {
        s.transition = Selectable.Transition.None;
        if (s.GetComponent<PressScale>() == null) s.gameObject.AddComponent<PressScale>();
    }

    // ------------------------------------------------------------------ fields
    /// <summary>A text field with the orange rim of the sign-in card.</summary>
    public static TMP_InputField Field(Transform parent, string placeholder, TMP_InputField.ContentType type, int limit)
    {
        // The fill is the rim's shape one unit in, so both curves share a centre.
        var rim = Panel("Field_" + placeholder, parent, UiTheme.Orange, UiTheme.RadiusSmall);
        var fill = Panel("Fill", rim.transform, UiTheme.White, UiTheme.RadiusSmall - 1f);
        Inset(fill.rectTransform, 1f);

        // TMP's field scrolls its text inside a masked viewport rather than cutting it at the rim.
        var area = Rect("Text Area", rim.transform);
        Inset(area, 7, 0, 7, 0);
        area.gameObject.AddComponent<RectMask2D>();
        var text = Label("Text", area, "", UiTheme.BodySize, UiTheme.Ink, TextAnchor.MiddleLeft);
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(text.rectTransform);
        var hint = Label("Placeholder", area, placeholder, UiTheme.BodySize, UiTheme.Placeholder, TextAnchor.MiddleLeft);
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(hint.rectTransform);

        // Wired up while inactive, so the field's OnEnable sees its text and makes its caret.
        bool wasActive = rim.gameObject.activeSelf;
        rim.gameObject.SetActive(false);
        var field = rim.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = fill;
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = hint;
        field.fontAsset = text.font;
        field.richText = false;
        field.characterLimit = limit;
        field.contentType = type;
        field.selectionColor = UiTheme.Fade(UiTheme.Orange, 0.35f);
        field.customCaretColor = true;
        field.caretColor = UiTheme.Ink;
        field.caretWidth = 1;
        rim.gameObject.SetActive(wasActive);
        return field;
    }

    // ------------------------------------------------------------------ settings widgets
    /// <summary>A labelled slider, 0..1 unless told otherwise.</summary>
    public static Slider Slider(Transform parent, string caption, float value, UnityAction<float> onChange, float min = 0f, float max = 1f)
    {
        var row = Rect("Slider_" + caption, parent);
        row.sizeDelta = new Vector2(300, 22);
        var cap = Label("Caption", row, caption, UiTheme.BodySize + 1, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
        Place(cap.rectTransform, Left, Vector2.zero, new Vector2(110, 22));

        var area = Rect("Slider", row);
        area.anchorMin = new Vector2(0, 0.5f);
        area.anchorMax = new Vector2(1, 0.5f);
        area.offsetMin = new Vector2(118, -6);
        area.offsetMax = new Vector2(-4, 6);

        // a 6-high bar: rounded at half its height, a true pill
        var track = Panel("Track", area, UiTheme.Cream, UiTheme.RadiusSmall);
        Inset(track.rectTransform, 0, 3, 0, 3);
        var fillArea = Rect("FillArea", area);
        Inset(fillArea, 0, 3, 0, 3);
        var fill = Panel("Fill", fillArea, UiTheme.Orange, UiTheme.RadiusSmall);
        Stretch(fill.rectTransform);
        var handleArea = Rect("HandleArea", area);
        Inset(handleArea, 6, 0, 6, 0);
        var handle = Image("Handle", handleArea, UiTheme.OrangeDeep, Circle());
        handle.rectTransform.sizeDelta = new Vector2(12, 0);

        var s = area.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = handle;
        s.minValue = min;
        s.maxValue = max;
        s.SetValueWithoutNotify(value);
        s.onValueChanged.AddListener(_ => UiSound.Play(UiSoundKind.Tick));          // a soft tick as it is dragged (rate-limited)
        if (onChange != null) s.onValueChanged.AddListener(onChange);
        return s;
    }

    /// <summary>A row of choices where one is on: Low / Medium / High, Left / Right, Auto / On / Off.</summary>
    public static ChoiceRow Choice(Transform parent, string caption, string[] options, int selected, Action<int> onPick)
    {
        var row = Rect("Choice_" + caption, parent);
        row.sizeDelta = new Vector2(300, 22);
        var cap = Label("Caption", row, caption, UiTheme.BodySize + 1, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
        Place(cap.rectTransform, Left, Vector2.zero, new Vector2(110, 22));
        var choice = row.gameObject.AddComponent<ChoiceRow>();
        choice.Build(options, selected, onPick);
        return choice;
    }

    // ------------------------------------------------------------------ scrolling
    /// <summary>A vertical scroll view. Put things in the returned content; it grows to fit them.</summary>
    public static RectTransform Scroll(Transform parent, out ScrollRect scroll, float spacing = 6f, RectOffset padding = null)
    {
        var view = Rect("Scroll", parent);
        Stretch(view);
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);     // catches drags on the empty parts

        var content = Rect("Content", view);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding ?? new RectOffset(4, 4, 4, 4);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 12f;
        scroll.viewport = view;
        return content;
    }

    /// <summary>A paragraph that sizes itself in a layout (for scroll views).</summary>
    public static TMP_Text Paragraph(Transform parent, string text, int size, Color color, bool heading = false, TextAnchor align = TextAnchor.UpperLeft)
    {
        return Label(heading ? "Heading" : "Text", parent, text, size, color, align, heading);
    }

    // ------------------------------------------------------------------ dialogs
    /// <summary>
    /// A question over a dimmed screen, as in the Figma pause frames: "Are you sure to abort the operation?" NO / YES.
    /// The first button is drawn orange (the safe answer), the rest cream. Any button closes the dialog.
    /// </summary>
    public static GameObject Confirm(Transform root, string question, params (string text, UnityAction action)[] answers)
    {
        UiSound.Play(UiSoundKind.Question);
        var scrim = Image("Dialog", root, UiTheme.Scrim);
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        var q = Label("Question", scrim.transform, question, UiTheme.MenuSize, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        Place(q.rectTransform, Center, new Vector2(0, 26), new Vector2(420, 40));

        // The Figma NO / YES buttons are 97 x 32, 16 apart.
        const float answerW = 97f, answerH = 32f, gap = 16f;
        var row = Rect("Answers", scrim.transform);
        Place(row, Center, new Vector2(0, -18), new Vector2(answers.Length * answerW + (answers.Length - 1) * gap, answerH));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = gap;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < answers.Length; i++)
        {
            var a = answers[i];
            var go = scrim.gameObject;
            Button(row, a.text, i == 0 ? ButtonStyle.Primary : ButtonStyle.Secondary, () =>
            {
                UnityEngine.Object.Destroy(go);
                a.action?.Invoke();
            }, UiTheme.ButtonSize + 1);
        }
        return scrim.gameObject;
    }

    /// <summary>A stack of wide buttons over a dimmed screen: the Figma quit dialog (Return to Login / Exit Game / CANCEL).</summary>
    public static GameObject Choices(Transform root, params (string text, ButtonStyle style, UnityAction action)[] choices)
    {
        UiSound.Play(UiSoundKind.Question);
        var scrim = Image("Choices", root, UiTheme.Scrim);
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
        var column = Rect("Column", scrim.transform);
        Place(column, Center, Vector2.zero, new Vector2(186, choices.Length * 30f));
        var v = column.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 7;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = true;

        var go = scrim.gameObject;
        foreach (var c in choices)
        {
            var choice = c;
            Button(column, choice.text, choice.style, () =>
            {
                UnityEngine.Object.Destroy(go);
                choice.action?.Invoke();
            }, UiTheme.ButtonSize + 2);
        }
        return go;
    }

    /// <summary>A paper card with a heading, text and one button: notices such as "Forgot password?".</summary>
    public static GameObject Notice(Transform root, string title, string body, string button = "OK", UnityAction onClose = null)
    {
        UiSound.Play(UiSoundKind.Question);
        var scrim = Image("Notice", root, UiTheme.Scrim);
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
        var card = Panel("Card", scrim.transform, UiTheme.Paper, UiTheme.RadiusBoard);
        card.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.25f);
        Place(card.rectTransform, Center, Vector2.zero, new Vector2(250, 150));
        var t = Label("Title", card.transform, title, UiTheme.TitleSize - 8, UiTheme.Ink, TextAnchor.MiddleCenter, heading: true);
        Place(t.rectTransform, Top, new Vector2(0, -10), new Vector2(230, 24));
        var b = Label("Body", card.transform, body, UiTheme.BodySize, UiTheme.InkSoft, TextAnchor.UpperCenter);
        Place(b.rectTransform, Top, new Vector2(0, -38), new Vector2(222, 70));
        b.Shrink(8);
        var go = scrim.gameObject;
        var ok = Button(card.transform, button, ButtonStyle.Primary, () => { UnityEngine.Object.Destroy(go); onClose?.Invoke(); });
        Place((RectTransform)ok.transform, Bottom, new Vector2(0, 10), new Vector2(150, 22));
        return go;
    }

    // ------------------------------------------------------------------ drawn sprites
    /// <summary>Rounded sprites are drawn at this many texels per Figma unit, so their corners stay smooth when scaled up.</summary>
    const float RoundedScale = 0.25f;
    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sprites.Clear(); textEffects.Clear(); }

    /// <summary>A white rounded rectangle, 9-sliced. The radius is in Figma units.</summary>
    public static Sprite Rounded(float radius) => Rounded(radius, radius, radius, radius);

    /// <summary>A rounded rectangle with its own radius at each corner (top-left, top-right, bottom-right, bottom-left).</summary>
    public static Sprite Rounded(float tl, float tr, float br, float bl)
    {
        string key = $"r{tl}/{tr}/{br}/{bl}";
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;

        // texels: four per Figma unit, so the curve is still smooth at the canvas's scale on a phone
        int k = Mathf.RoundToInt(1f / RoundedScale);
        int rtl = Mathf.RoundToInt(tl * k), rtr = Mathf.RoundToInt(tr * k), rbr = Mathf.RoundToInt(br * k), rbl = Mathf.RoundToInt(bl * k);
        int left = Mathf.Max(rtl, rbl), right = Mathf.Max(rtr, rbr), top = Mathf.Max(rtl, rtr), bottom = Mathf.Max(rbl, rbr);
        int w = left + right + 4, h = top + bottom + 4;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = new Color32(255, 255, 255, (byte)(RoundedAlpha(x + 0.5f, y + 0.5f, w, h, rtl, rtr, rbr, rbl) * 255));
        tex.SetPixels32(px);
        tex.Apply();

        // 100 pixels per unit is one canvas unit per texel; k texels per unit keeps the corners exactly `radius` units.
        // The 9-slice border is the corner itself, no more, so a pill whose radius is half its height is not squashed.
        var sprite = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * k, 0,
            SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
        sprite.name = key;
        sprites[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// How opaque one texel of a rounded rectangle is (texel centre at fx, fy; texture w by h; radii in texels). A texel
    /// belongs to a corner only inside that corner's own r-by-r box, so a sprite whose corners differ (the menu tab,
    /// round on the right and square on the left) has no step where a big curve meets the flat edge.
    /// </summary>
    public static float RoundedAlpha(float fx, float fy, int w, int h, int tl, int tr, int br, int bl)
    {
        float alpha = 1f;
        Corner(tl, fx < tl, fy > h - tl, tl, h - tl);
        Corner(tr, fx > w - tr, fy > h - tr, w - tr, h - tr);
        Corner(br, fx > w - br, fy < br, w - br, br);
        Corner(bl, fx < bl, fy < bl, bl, bl);
        return alpha;

        void Corner(int r, bool inX, bool inY, float cx, float cy)
        {
            if (r <= 0 || !inX || !inY) return;
            float a = Mathf.Clamp01(r - Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy)) + 0.5f);
            alpha = Mathf.Min(alpha, a);
        }
    }

    /// <summary>The largest radius a box of this size can round without its corners overlapping: half its short side.</summary>
    public static float Fit(float radius, Vector2 size) => Mathf.Min(radius, Mathf.Min(size.x, size.y) * 0.5f);

    /// <summary>A divider line in the one style every screen uses, across the top of <paramref name="parent"/>'s rect at <paramref name="y"/> from the top.</summary>
    public static Image Rule(Transform parent, float y, float insetLeft = 16f, float insetRight = 16f, string name = "Rule")
    {
        var rule = Image(name, parent, UiTheme.Rule);
        rule.raycastTarget = false;
        rule.rectTransform.anchorMin = new Vector2(0, 1);
        rule.rectTransform.anchorMax = new Vector2(1, 1);
        rule.rectTransform.offsetMin = new Vector2(insetLeft, y - UiTheme.RuleThickness);
        rule.rectTransform.offsetMax = new Vector2(-insetRight, y);
        return rule;
    }

    /// <summary>The thin edge round a card or panel.</summary>
    public static Outline Edge(Graphic g, Color? colour = null)
    {
        if (!g.gameObject.TryGetComponent(out Outline o)) o = g.gameObject.AddComponent<Outline>();
        o.effectColor = colour ?? UiTheme.CardEdge;
        o.effectDistance = UiTheme.EdgeWidth;
        return o;
    }

    /// <summary>
    /// Round sprites are drawn this many texels across. The joystick ring is over 500 px wide on a phone, and a small
    /// texture stretched that far shows a soft, stepped edge; mipmaps keep the same texture clean on a 6-unit pip.
    /// </summary>
    const int RoundSize = 256;

    /// <summary>A white disc (joystick, touch buttons, slider handle, pips).</summary>
    public static Sprite Circle()
    {
        const string key = "circle";
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        return RoundSprite(key, 0f);
    }

    /// <summary>A ring (the joystick's base, the throw target).</summary>
    public static Sprite Ring(float thickness01 = 0.12f)
    {
        string key = "ring" + thickness01;
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        return RoundSprite(key, thickness01);
    }

    /// <summary>A disc, or a ring when <paramref name="thickness01"/> is above 0, with a one-texel smooth edge inside and out.</summary>
    static Sprite RoundSprite(string key, float thickness01)
    {
        const int size = RoundSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            name = key
        };
        var px = new Color32[size * size];
        // a texel of clear margin all round, so no edge texel is cut off by the sprite's bounds
        float c = size * 0.5f, r = c - 1f, inner = thickness01 > 0f ? r * (1f - thickness01 * 2f) : -1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
            float a = Mathf.Clamp01(r - d + 0.5f);
            if (inner >= 0f) a *= Mathf.Clamp01(d - inner + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply(true, true);     // build the mipmaps, then free the CPU copy
        var sprite = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        sprites[key] = sprite;
        return sprite;
    }

    /// <summary>A white triangle pointing up, with rounded-ish edges (the warning sign).</summary>
    public static Sprite Triangle()
    {
        const string key = "triangle";
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fy = (y + 0.5f) / size;                  // 0 bottom .. 1 top
            float half = (1f - fy) * 0.5f;                  // half-width of the triangle at this height
            float dx = Mathf.Abs((x + 0.5f) / size - 0.5f);
            float a = Mathf.Clamp01((half - dx) * size) * Mathf.Clamp01(fy * size);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        var sprite = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        sprites[key] = sprite;
        return sprite;
    }

    public static Sprite VerticalGradient(Color top, Color bottom)
    {
        string key = "grad" + top + bottom;
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        tex.Apply();
        var sprite = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
        sprites[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// An item's icon: the Figma export if there is one, otherwise a disc in the item's colour with its letter on it.
    /// </summary>
    public static Image ItemIcon(Transform parent, RescueItemDef def)
    {
        var art = UiAssets.Current.ItemIcon(def.id);
        if (art != null)
        {
            var img = Image("Icon", parent, Color.white, art);
            img.preserveAspect = true;
            return img;
        }
        var disc = Image("Icon", parent, def.color, Circle());
        disc.preserveAspect = true;
        var g = Label("Glyph", disc.transform, def.glyph, 14, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        Stretch(g.rectTransform);
        g.enableAutoSizing = true;
        g.fontSizeMin = 4;
        g.fontSizeMax = 40;
        return disc;
    }

    /// <summary>A header icon from Figma, or nothing when it has not been exported (the header text carries on alone).</summary>
    public static Image OptionalIcon(Transform parent, Sprite art, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        if (art == null) return null;
        var img = Image("Icon", parent, Color.white, art);
        img.preserveAspect = true;
        Place(img.rectTransform, anchor, pos, size);
        return img;
    }
}

/// <summary>Keeps its RectTransform inside Screen.safeArea, clear of a phone's notch and rounded corners.</summary>
[RequireComponent(typeof(RectTransform))]
/// <summary>Shrinks a control a little while a finger is on it, so a press on a card or arrow can be seen.</summary>
public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    const float Pressed = 0.95f;
    Vector3 rest = Vector3.one;
    bool down;

    void Awake() => rest = transform.localScale;
    public void OnPointerDown(PointerEventData e)
    {
        var s = GetComponent<Selectable>();
        if (s != null && !s.IsInteractable()) return;
        down = true;
        transform.localScale = rest * Pressed;
    }
    public void OnPointerUp(PointerEventData e) => Release();
    public void OnPointerExit(PointerEventData e) => Release();
    void OnDisable() => Release();
    void Release()
    {
        if (!down) return;
        down = false;
        transform.localScale = rest;
    }
}

public class SafeArea : MonoBehaviour
{
    /// <summary>How much taller than the Figma frame a menu may grow before it is centred instead (tablets, 16:9).</summary>
    public const float MenuFit = 1.15f;

    Rect applied;
    Vector2Int size;
    float fitDesign;

    /// <summary>
    /// 0 = fill the whole safe area (the HUD, which sits in the corners). Otherwise the area is never taller than the
    /// Figma frame's shape times this, and is centred: on a 4:3 tablet a menu sits in the middle, not in the top half.
    /// </summary>
    public float FitDesign
    {
        get => fitDesign;
        set { fitDesign = value; Apply(); }
    }

    void OnEnable() => Apply();
    void Update()
    {
        if (applied != Screen.safeArea || size.x != Screen.width || size.y != Screen.height) Apply();
    }

    void Apply()
    {
        applied = Screen.safeArea;
        size = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0) return;
        Rect area = applied;
        if (fitDesign > 0f)
        {
            float maxHeight = area.width * (UiTheme.Reference.y / UiTheme.Reference.x) * fitDesign;
            if (area.height > maxHeight)
            {
                float trim = (area.height - maxHeight) * 0.5f;
                area = new Rect(area.x, area.y + trim, area.width, maxHeight);
            }
        }
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        rt.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

/// <summary>
/// A line of a Figma menu: an orange tab and a white word, which turns into an orange pill while it is hovered or was
/// the last one tapped.
/// </summary>
public class UiMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static UiMenuItem Highlighted;
    Image tab, pill;
    TMP_Text label;
    Button button;
    bool hovered;

    public Button Button => button;

    float textLeft, pillLeft;

    public void Build(string text, UnityAction onClick, float height)
    {
        var rt = (RectTransform)transform;
        float r = height * 0.5f;
        // The whole line is the button, the word as much as the tab: an invisible hit area under everything. Without it
        // only the small tab caught a click (the pill is hidden until highlighted, and text never catches clicks), so a
        // click on CONTINUE or PLAY fell through to whatever was behind.
        var hit = gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);
        hit.raycastTarget = true;
        pill = UiKit.Panel("Pill", rt, UiTheme.Orange, r);
        UiKit.Stretch(pill.rectTransform);
        // The Figma tab is a square-edged block with a half circle on its right (a 21 x 37 rectangle under a 37 circle).
        tab = UiKit.Image("Tab", rt, UiTheme.Orange, UiKit.Rounded(0f, r, r, 0f));
        UiKit.Place(tab.rectTransform, UiKit.Left, Vector2.zero, new Vector2(height, height));
        textLeft = height + 10f;
        pillLeft = r;
        label = UiKit.Label("Label", rt, text, Mathf.RoundToInt(height * 0.45f), UiTheme.White, TextAnchor.MiddleLeft, heading: true);
        UiKit.Inset(label.rectTransform, textLeft, 0, 6, 0);
        label.TextShadow(new Color(0, 0, 0, 0.35f));

        button = gameObject.AddComponent<Button>();
        button.targetGraphic = pill;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { UiSound.Play(UiSoundKind.Open); Highlighted = this; Refresh(); });
        if (onClick != null) button.onClick.AddListener(onClick);
        Refresh();
    }

    /// <summary>Nothing lit: a menu that is built again, or whose page was closed, starts plain.</summary>
    public static void ClearHighlight()
    {
        Highlighted = null;
        foreach (var m in FindObjectsByType<UiMenuItem>(FindObjectsSortMode.None)) m.Refresh();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Highlighted = null;

    public void OnPointerEnter(PointerEventData e)
    {
        // A soft tick as the pointer reaches a new line (a mouse: a tap goes straight to the click).
        if (Highlighted != this && e.pointerId < 0) UiSound.Play(UiSoundKind.Hover);
        hovered = true;
        Highlighted = this;
        RefreshAll();
    }
    public void OnPointerExit(PointerEventData e) { hovered = false; Refresh(); }

    void RefreshAll()
    {
        foreach (var m in FindObjectsByType<UiMenuItem>(FindObjectsSortMode.None)) m.Refresh();
    }

    void Refresh()
    {
        bool on = hovered || Highlighted == this;
        pill.enabled = on;
        tab.enabled = !on;
        label.color = on ? UiTheme.CreamLight : UiTheme.White;
        label.rectTransform.offsetMin = new Vector2(on ? pillLeft : textLeft, 0);
        label.alignment = TextAlignmentOptions.Left;
    }
}

/// <summary>A row of buttons where exactly one is on (the settings' quality, control side and touch controls).</summary>
public class ChoiceRow : MonoBehaviour
{
    readonly List<Image> backs = new List<Image>();
    readonly List<TMP_Text> labels = new List<TMP_Text>();
    int selected;

    public int Selected => selected;

    public void Build(string[] options, int pick, Action<int> onPick)
    {
        var area = UiKit.Rect("Options", transform);
        area.anchorMin = new Vector2(0, 0);
        area.anchorMax = new Vector2(1, 1);
        area.offsetMin = new Vector2(118, 0);
        area.offsetMax = new Vector2(-4, 0);
        var h = area.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;

        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var b = UiKit.Button(area, options[i], ButtonStyle.Secondary, () => { Select(index); onPick?.Invoke(index); }, out var label, UiTheme.BodySize + 1,
                                 UiSoundKind.Toggle);
            backs.Add((Image)b.targetGraphic);
            labels.Add(label);
        }
        Select(pick);
    }

    public void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, backs.Count - 1);
        for (int i = 0; i < backs.Count; i++)
        {
            backs[i].color = i == selected ? UiTheme.Orange : UiTheme.CreamLight;
            labels[i].color = i == selected ? UiTheme.White : UiTheme.OrangeDeep;
        }
    }
}
