using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BayanihanHQ
{
    /// <summary>
    /// A small code-built uGUI kit for the HQ, in the style of the game's UiKit (TextMeshPro, the game's fonts and
    /// UiTheme colours). It is separate from UiKit on purpose: UiKit has teammates' work in progress, and the HQ must not
    /// break when it changes.
    /// </summary>
    public static class HqUi
    {
        static TMP_FontAsset heading, body;

        /// <summary>The heading font (Lilita One, like the game), or TMP's default.</summary>
        public static TMP_FontAsset HeadingFont => heading != null ? heading : (heading = RuntimeCopy(UiAssets.Current.headingFont));
        /// <summary>The body font (Merge One, like the game), or TMP's default.</summary>
        public static TMP_FontAsset BodyFont => body != null ? body : (body = RuntimeCopy(UiAssets.Current.bodyFont));

        /// <summary>Uses these font assets instead (the scene builder, for text saved in the scene).</summary>
        public static void UseFonts(TMP_FontAsset headingFont, TMP_FontAsset bodyFont)
        {
            heading = headingFont;
            body = bodyFont;
            arrow = null;
        }

        static TMP_FontAsset fallbackCopy;

        /// <summary>
        /// An in-memory dynamic copy of a game font, made from the same TTF. Dynamic TMP fonts write every new character
        /// into their asset, and the HQ must never change the game's (or a teammate's) font assets, so the office draws
        /// with copies that are never saved. A copy of Liberation Sans is the fallback for symbols the game fonts lack.
        /// </summary>
        static TMP_FontAsset RuntimeCopy(TMP_FontAsset source)
        {
            var file = source != null ? source.sourceFontFile : null;
            if (file == null) return TMP_Settings.defaultFontAsset;
            var copy = TMP_FontAsset.CreateFontAsset(file, 72, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            if (copy == null) return TMP_Settings.defaultFontAsset;
            copy.name = source.name + " (HQ copy)";
            copy.hideFlags = HideFlags.DontSave;
            if (fallbackCopy == null && TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset.sourceFontFile != null)
            {
                fallbackCopy = TMP_FontAsset.CreateFontAsset(TMP_Settings.defaultFontAsset.sourceFontFile, 72, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                if (fallbackCopy != null) { fallbackCopy.name = "Liberation Sans (HQ copy)"; fallbackCopy.hideFlags = HideFlags.DontSave; }
            }
            if (fallbackCopy != null) copy.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallbackCopy };
            return copy;
        }

        public static readonly Color PanelColour = new Color(0.17f, 0.11f, 0.07f, 0.88f);
        public static readonly Color PanelLight = new Color(0.30f, 0.20f, 0.12f, 0.92f);

        /// <summary>A screen-space canvas scaled for 1920×1080, plus an Input System event system if the scene has none.</summary>
        public static Canvas NewCanvas(string name, int sortingOrder)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>A plain RectTransform child.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A coloured panel.</summary>
        public static Image Panel(string name, Transform parent, Color colour)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = colour;
            return img;
        }

        /// <summary>A TextMeshPro label.</summary>
        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color colour, bool heading = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = heading ? HeadingFont : BodyFont;
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A flat button with a label; returns the label so callers can change it (e.g. a toggle's state).</summary>
        public static TextMeshProUGUI Button(string name, Transform parent, string text, Color colour, Action onClick)
        {
            var img = Panel(name, parent, colour);
            var button = img.gameObject.AddComponent<Button>();
            var colours = button.colors;
            colours.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colours.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colours;
            button.onClick.AddListener(() => onClick?.Invoke());
            var label = Label("Label", img.transform, text, 22, UiTheme.Ink, heading: true);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return label;
        }

        /// <summary>Fills the parent, with an optional inset in pixels.</summary>
        public static void Stretch(RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Anchors to a corner or edge (anchor and pivot the same), with a size and an offset from that point.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        static string arrow;

        /// <summary>"→" if the game font can draw it, else "->". Checked once, on first use.</summary>
        public static string Arrow
        {
            get
            {
                if (arrow != null) return arrow;
                var font = BodyFont;
                arrow = font != null && font.HasCharacter('\u2192', true, true) ? "\u2192" : "->";
                return arrow;
            }
        }

        /// <summary>Text from the state model, made safe for the font (the arrow, mainly).</summary>
        public static string Display(string s) => string.IsNullOrEmpty(s) ? "" : (Arrow == "\u2192" ? s : s.Replace("\u2192", Arrow));

        /// <summary>
        /// A 3D TextMeshPro label in the world (monitors, nameplates, boards). Its readable side faces -Z of the parent,
        /// like a uGUI canvas, so an anchor with identity rotation reads from in front.
        /// </summary>
        public static TextMeshPro WorldText(string name, Transform parent, string text, float size, Color colour, Vector2 box,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool heading = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = heading ? HeadingFont : BodyFont;
            t.text = Display(text);
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.rectTransform.sizeDelta = box;
            return t;
        }

        /// <summary>Hex for rich text, e.g. &lt;color=#4E9C2C&gt;.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>The colour each status is drawn in across the office.</summary>
        public static Color StatusColour(AgentStatus s)
        {
            switch (s)
            {
                case AgentStatus.Thinking: return new Color(0.55f, 0.75f, 1f);
                case AgentStatus.Working: return UiTheme.Green;
                case AgentStatus.Waiting: return UiTheme.Notice;
                case AgentStatus.Error: return new Color(1f, 0.35f, 0.3f);
                case AgentStatus.Done: return UiTheme.Cream;
                default: return new Color(0.65f, 0.6f, 0.55f);
            }
        }

        /// <summary>The words shown for each status.</summary>
        public static string StatusText(AgentStatus s) => s == AgentStatus.Waiting ? "Waiting for you" : s.ToString();
    }
}
