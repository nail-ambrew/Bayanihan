using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Figma "Mission Complete" frame: the logo and CONTINUE / RESTART / QUIT down the left, and on the orange board
/// MISSION COMPLETE! with the Mission Stats card: Time Taken, Survivors Saved, Areas Cleared, Supplies Used.
/// FULL REPORT swaps the card for the after-action report (every resident, the score, the safety record, the pack).
/// Shown when <see cref="RescueManager.ReportVisible"/> turns on. Made by <see cref="GameHud"/>.
/// </summary>
public class MissionCompleteScreen : MonoBehaviour
{
    RescueManager manager;
    RectTransform safe;
    GameObject board, report;
    UiMenuItem reportItem;
    bool built;

    public static MissionCompleteScreen Create(Transform canvasRoot, RescueManager manager)
    {
        var root = UiKit.Rect("MissionComplete", canvasRoot);
        UiKit.Stretch(root);
        var screen = root.gameObject.AddComponent<MissionCompleteScreen>();
        screen.manager = manager;
        return screen;
    }

    void Update()
    {
        if (manager == null) return;
        if (manager.ReportVisible && !built) Build();
    }

    void Build()
    {
        built = true;
        transform.SetAsLastSibling();
        var root = (RectTransform)transform;

        var dim = UiKit.Image("Dim", root, new Color(0, 0, 0, 0.4f));
        UiKit.Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        safe = UiKit.Rect("Safe", root);
        UiKit.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>().FitDesign = SafeArea.MenuFit;

        UiKit.Logo(safe, UiKit.TopLeft, new Vector2(16, -12), 140f);
        UiKit.Version(safe);
        var col = UiKit.MenuColumn(safe, new Vector2(16, -140), 32f, 14f, 160f);
        // CONTINUE: pack again for the next run (the loadout). QUIT: the main menu.
        UiKit.MenuItem(col, "CONTINUE", manager.Repack, 32f, 160f);
        UiKit.MenuItem(col, "RESTART", manager.Restart, 32f, 160f);
        reportItem = UiKit.MenuItem(col, "FULL REPORT", ToggleReport, 32f, 160f);
        UiKit.MenuItem(col, "QUIT", manager.ExitToMenu, 32f, 160f);

        board = BuildStats();
    }

    string Heading()
    {
        if (manager.Evacuated >= manager.TotalSurvivors && manager.TotalSurvivors > 0) return "MISSION COMPLETE!";
        return manager.TimeIsUp ? "TIME'S UP!" : "MISSION OVER";
    }

    GameObject BuildStats()
    {
        var a = UiAssets.Current;
        var body = UiKit.Board(safe, Heading(), "MISSION STATS", a.statsIcon, out _, out var cardHeading);
        var stats = MissionStats.From(manager);

        if (a.statsIcon == null)
        {
            // drawn stand-in for the bar-chart icon
            for (int i = 0; i < 3; i++)
            {
                var bar = UiKit.Image("Bar", cardHeading.transform.parent, UiTheme.BrownDark);
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0, 1);
                bar.rectTransform.pivot = new Vector2(0, 0);
                bar.rectTransform.anchoredPosition = new Vector2(36 + i * 9, -38);
                bar.rectTransform.sizeDelta = new Vector2(6, 10 + i * 8);
            }
            cardHeading.rectTransform.anchoredPosition = new Vector2(70, -8);
        }

        var score = UiKit.Label("Score", cardHeading.transform.parent, manager.Score.ToString("N0") + " PTS", UiTheme.BodySize + 6, UiTheme.OrangeBurnt, TextAnchor.MiddleRight, heading: true);
        UiKit.Place(score.rectTransform, UiKit.TopRight, new Vector2(-18, -10), new Vector2(120, 30));

        var rows = UiKit.Rect("Rows", body);
        UiKit.Stretch(rows);
        var v = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = true;
        v.spacing = 0;

        Row(rows, a.timeIcon, "clock", "Time Taken:", stats.TimeText, true);
        Row(rows, a.survivorsIcon, "people", "Survivors Saved:", stats.SavedText, true);
        Row(rows, a.areasIcon, "warning", "Areas Cleared:", stats.AreasText, true);
        Row(rows, a.suppliesIcon, "bag", "Supplies Used:", stats.SuppliesText, false);
        return body.parent.parent.gameObject;
    }

    static void Row(Transform rows, Sprite icon, string glyph, string caption, string value, bool rule)
    {
        var row = UiKit.Rect("Row_" + caption, rows);
        var art = UiKit.Rect("Icon", row);
        UiKit.Place(art, UiKit.Left, new Vector2(20, 0), new Vector2(34, 34));
        if (icon != null)
        {
            var img = UiKit.Image("Sprite", art, Color.white, icon);
            img.preserveAspect = true;
            UiKit.Stretch(img.rectTransform);
        }
        else DrawGlyph(art, glyph);

        var cap = UiKit.Label("Caption", row, caption, UiTheme.BodySize + 7, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
        UiKit.Inset(cap.rectTransform, 68, 0, 150, 0);
        var val = UiKit.Label("Value", row, value, UiTheme.BodySize + 6, UiTheme.OrangeBurnt, TextAnchor.MiddleRight, heading: true);
        UiKit.Inset(val.rectTransform, 210, 0, 12, 0);
        if (rule)
        {
            // the same divider as under the card's heading, along the bottom of the row
            var line = UiKit.Image("Rule", row, UiTheme.Rule);
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(66, 0);
            line.rectTransform.offsetMax = new Vector2(-8, UiTheme.RuleThickness);
        }
    }

    /// <summary>Simple stand-ins for the Figma stat icons until they are exported.</summary>
    static void DrawGlyph(RectTransform at, string glyph)
    {
        var ink = UiTheme.BrownDark;
        switch (glyph)
        {
            case "clock":
            {
                var ring = UiKit.Image("Ring", at, ink, UiKit.Ring(0.1f));
                UiKit.Stretch(ring.rectTransform);
                var hand1 = UiKit.Image("Hand", at, ink);
                hand1.rectTransform.pivot = new Vector2(0.5f, 0f);
                UiKit.Place(hand1.rectTransform, UiKit.Center, Vector2.zero, new Vector2(2.5f, 11));
                hand1.rectTransform.pivot = new Vector2(0.5f, 0f);
                var hand2 = UiKit.Image("Hand", at, ink);
                hand2.rectTransform.anchorMin = hand2.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hand2.rectTransform.pivot = new Vector2(0f, 0.5f);
                hand2.rectTransform.sizeDelta = new Vector2(8, 2.5f);
                hand2.rectTransform.anchoredPosition = Vector2.zero;
                break;
            }
            case "people":
            {
                for (int i = -1; i <= 1; i++)
                {
                    float big = i == 0 ? 1f : 0.8f;
                    var head = UiKit.Image("Head", at, ink, UiKit.Circle());
                    UiKit.Place(head.rectTransform, UiKit.Center, new Vector2(i * 10, 7 * big), new Vector2(9 * big, 9 * big));
                    var body = UiKit.Image("Body", at, ink, UiKit.Rounded(4f * big, 4f * big, 1f, 1f));
                    UiKit.Place(body.rectTransform, UiKit.Center, new Vector2(i * 10, -7), new Vector2(12 * big, 12 * big));
                }
                break;
            }
            case "warning":
            {
                var tri = UiKit.Image("Tri", at, ink, UiKit.Triangle());
                UiKit.Inset(tri.rectTransform, 1, 2, 1, 2);
                var bang = UiKit.Label("Bang", at, "!", 17, UiTheme.Paper, TextAnchor.MiddleCenter, heading: true);
                UiKit.Inset(bang.rectTransform, 0, 9, 0, 1);
                break;
            }
            default:
            {
                var bag = UiKit.Image("Bag", at, ink, UiKit.Rounded(6f, 6f, 3f, 3f));
                UiKit.Inset(bag.rectTransform, 4, 8, 4, 2);
                var handle = UiKit.Image("Handle", at, ink, UiKit.Ring(0.18f));
                UiKit.Place(handle.rectTransform, UiKit.Top, new Vector2(0, 0), new Vector2(14, 14));
                var flap = UiKit.Image("Flap", bag.transform, UiTheme.Fade(UiTheme.Paper, 0.5f));
                UiKit.Place(flap.rectTransform, UiKit.Center, new Vector2(0, 2), new Vector2(16, 1.5f));
                break;
            }
        }
    }

    // ------------------------------------------------------------------ full report
    /// <summary>FULL REPORT swaps the stats for the report and becomes STATS; STATS (or the report's back arrow) swaps back.</summary>
    void ToggleReport()
    {
        if (report != null)
        {
            Destroy(report);
            report = null;
            board.SetActive(true);
            SetReportLabel("FULL REPORT");
            return;
        }
        board.SetActive(false);
        report = BuildReport();
        SetReportLabel("STATS");
    }

    void SetReportLabel(string text)
    {
        if (reportItem == null) return;
        var label = reportItem.transform.Find("Label")?.GetComponent<Text>();
        if (label != null) label.text = text;
    }

    GameObject BuildReport()
    {
        var body = UiKit.Board(safe, manager.ReportTitle ?? "Mission over", "FULL REPORT", null, out var title, out _,
                               close: ToggleReport, name: "Report");
        title.fontSize = UiTheme.TitleSize - 2;
        title.Shrink(10);
        var content = UiKit.Scroll(body, out _, 1f);

        if (manager.ReportSections != null)
        {
            // Two columns in the old report; one long column reads better on a phone: outcome first, then the debrief.
            for (int column = 0; column <= 1; column++)
                foreach (var section in manager.ReportSections)
                {
                    if (Mathf.Clamp(section.column, 0, 1) != column) continue;
                    var head = UiKit.Paragraph(content, section.title.ToUpperInvariant(), UiTheme.BodySize + 3, UiTheme.OrangeBurnt, heading: true);
                    head.gameObject.AddComponent<LayoutElement>().minHeight = 16;
                    foreach (var line in section.lines)
                    {
                        string text = line.hasTag ? "<color=#" + ColorUtility.ToHtmlStringRGB(TriageStyle.Colour(line.tag)) + ">■</color>  " + line.text : line.text;
                        var t = UiKit.Paragraph(content, text, UiTheme.BodySize + 1, Tone(line.tone));
                        t.supportRichText = true;
                    }
                }
        }
        UiKit.Paragraph(content, " ", 4, UiTheme.Ink);
        return body.parent.parent.gameObject;
    }

    static Color Tone(ReportTone tone)
    {
        switch (tone)
        {
            case ReportTone.Good:    return UiTheme.Ok;
            case ReportTone.Warning: return UiTheme.OrangeBurnt;
            case ReportTone.Bad:     return UiTheme.Error;
            case ReportTone.Dim:     return UiTheme.InkSoft;
            default:                 return UiTheme.Ink;
        }
    }
}
