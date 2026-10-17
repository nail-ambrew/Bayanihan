using UnityEngine;

/// <summary>
/// What the crew can read off the screen about the residents. Above anybody they can see (within assessment range,
/// with nothing in the way) there is a card: their tag, who they are, how far, what they need and whether it is
/// packed, and how they are holding up. Those they have already found get a small marker so they can be steered
/// back to. A roster panel lists everybody on the mission and how far each has got. Live wires in the water are
/// marked too, with the countdown once the power company has been called.
///
/// It is the answer to "I did not know what anyone needed until the boat was refused": size-up comes first.
/// Added to the RescueManager's object by the manager.
/// </summary>
public class MissionHud : MonoBehaviour
{
    [Tooltip("Found residents further than this are not marked on screen (they are still on the roster).")]
    public float knownMarkerRange = 90f;
    [Tooltip("Live wires are labelled on screen when the edge of their zone is closer than this (metres).")]
    public float hazardMarkerRange = 70f;

    RescueManager manager;
    Camera cam;
    GUIStyle nameStyle, smallStyle, rosterStyle, rosterRight, headerStyle, glyphStyle;

    static readonly Color Panel = new Color(0f, 0f, 0f, 0.6f);

    void Awake() { manager = GetComponent<RescueManager>(); }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (manager == null || manager.Roster == null || manager.ReportVisible) return;

        EnsureStyles();
        if (cam == null) cam = Camera.main;

        if (cam != null)
        {
            // Once the mission is over there is nobody left to find, but the desk may still be writing names down.
            if (!manager.GameOver)
            {
                // Those only remembered go down first, so the cards for people in view are never hidden behind them.
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (var record in manager.Roster.Records)
                    {
                        var s = record.survivor;
                        if (s == null || s.IsRescued || !s.Discovered) continue;
                        if (s.InView == (pass == 0)) continue;
                        DrawMarker(s, s.InView);
                    }
                }

                DrawHazards();
            }

            DrawRegistration();
        }

        DrawRoster();
    }

    void EnsureStyles()
    {
        if (nameStyle != null) return;
        nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        nameStyle.normal.textColor = Color.white;
        smallStyle = new GUIStyle(nameStyle) { fontSize = 12, fontStyle = FontStyle.Normal };
        smallStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
        rosterStyle = new GUIStyle(nameStyle) { fontSize = 15, fontStyle = FontStyle.Normal };
        rosterRight = new GUIStyle(rosterStyle) { alignment = TextAnchor.MiddleRight };
        headerStyle = new GUIStyle(nameStyle) { fontSize = 16 };
        headerStyle.normal.textColor = new Color(1f, 0.86f, 0.45f);
        glyphStyle = new GUIStyle(nameStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
    }

    // ------------------------------------------------------------------ over the residents' heads

    void DrawMarker(Survivor s, bool live)
    {
        Vector3 world = s.SightPoint + Vector3.up * 0.7f;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;                                         // behind the camera

        float distance = Vector3.Distance(cam.transform.position, s.transform.position);
        if (!live && distance > knownMarkerRange) return;
        if (sp.x < 40f || sp.x > Screen.width - 40f || sp.y < 40f || sp.y > Screen.height - 40f) return;

        Vector2 at = new Vector2(sp.x, Screen.height - sp.y);
        var tagColour = TriageStyle.Colour(s.Triage);

        if (!live)
        {
            // Found, but out of sight right now: just enough to steer by.
            var rect = new Rect(at.x - 60f, at.y - 11f, 120f, 22f);
            Fill(rect, new Color(0f, 0f, 0f, 0.4f));
            Fill(new Rect(rect.x + 4f, rect.y + 5f, 12f, 12f), new Color(tagColour.r, tagColour.g, tagColour.b, 0.7f));
            GUI.Label(new Rect(rect.x + 20f, rect.y, rect.width - 22f, rect.height), FirstName(s) + " · " + distance.ToString("0") + " m", smallStyle);
            return;
        }

        const float w = 178f, h = 62f;
        var card = new Rect(at.x - w * 0.5f, at.y - h - 4f, w, h);
        Fill(card, Panel);
        Fill(new Rect(card.x, card.y, 6f, h), tagColour);

        GUI.Label(new Rect(card.x + 12f, card.y + 2f, w - 14f, 22f),
                  FirstName(s) + " · " + distance.ToString("0") + " m", nameStyle);

        // What they need, and whether it is packed.
        float gx = card.x + 12f, gy = card.y + 26f;
        if ((s.needs & Need.Lifebuoy) != 0) { DrawNeed(ref gx, gy, RescueItemId.Lifebuoy); }
        if ((s.needs & Need.Rope) != 0) { DrawNeed(ref gx, gy, RescueItemId.Rope); }
        if (s.NeedsFirstAidKit) { DrawNeed(ref gx, gy, RescueItemId.FirstAid); }

        // How they are holding up.
        float cond = s.Condition != null ? s.Condition.Condition01 : 1f;
        var bar = new Rect(card.x + 100f, card.y + 31f, 68f, 10f);
        Fill(bar, new Color(0f, 0f, 0f, 0.7f));
        Fill(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * cond, bar.height - 2f), tagColour);

        string where = s.situation == Situation.Rooftop ? "on a roof" :
                       s.situation == Situation.Trapped ? "behind debris" :
                       s.situation == Situation.Clinging ? "clinging to a post" : "in the water";
        if (s.IsCritical) where = "CRITICAL · " + where;
        else if (s.RequiresLine && !s.LineDelivered) where += " · line (T)";
        GUI.Label(new Rect(card.x + 12f, card.y + 42f, w - 14f, 18f), where, smallStyle);
    }

    // ------------------------------------------------------------------ the desk

    /// <summary>Over the resident who is being written on the list: who they are, and how far through it is.</summary>
    void DrawRegistration()
    {
        var desk = manager.Desk;
        if (desk == null || desk.Current == null || desk.Current.landedAs == null) return;

        Vector3 sp = cam.WorldToScreenPoint(desk.Current.landedAs.position + Vector3.up * 2.2f);
        if (sp.z <= 0f) return;
        if (sp.x < 100f || sp.x > Screen.width - 100f || sp.y < 40f || sp.y > Screen.height - 40f) return;

        const float w = 190f, h = 44f;
        var rect = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);
        Fill(rect, Panel);
        Fill(new Rect(rect.x, rect.y, 6f, h), new Color(1f, 0.82f, 0.25f));
        GUI.Label(new Rect(rect.x + 12f, rect.y + 2f, w - 14f, 22f), desk.Current.name, nameStyle);

        var bar = new Rect(rect.x + 12f, rect.y + 28f, w - 24f, 9f);
        Fill(bar, new Color(0f, 0f, 0f, 0.7f));
        Fill(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * desk.Progress01, bar.height - 2f), new Color(1f, 0.82f, 0.25f));
    }

    // ------------------------------------------------------------------ hazards

    /// <summary>A label over each live wire: how far, and once the power company has been called, how long to go.</summary>
    void DrawHazards()
    {
        foreach (var zone in LiveWireZone.All)
        {
            if (zone == null || !zone.IsLive) continue;

            Vector3 sp = cam.WorldToScreenPoint(zone.transform.position + Vector3.up * 1.6f);
            if (sp.z <= 0f) continue;                                   // behind the camera

            float away = Mathf.Max(0f, zone.DistanceToEdge(cam.transform.position));
            if (away > hazardMarkerRange) continue;

            // Kept on screen: a wire straight ahead at the edge of the view still wants its label.
            sp.x = Mathf.Clamp(sp.x, 100f, Screen.width - 100f);
            sp.y = Mathf.Clamp(sp.y, 60f, Screen.height - 60f);

            string text = zone.CutRequested
                ? "POWER OFF IN " + Mathf.CeilToInt(zone.CutTimeLeft) + " s"
                : "LIVE WIRE · " + (away < 1f ? "here" : away.ToString("0") + " m");
            var colour = zone.CutRequested ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.9f, 0.2f, 0.15f);

            const float w = 168f, h = 24f;
            var rect = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h * 0.5f, w, h);
            Fill(rect, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(rect.x, rect.y, 6f, h), colour);
            var prior = GUI.color;
            GUI.color = colour;
            GUI.Label(new Rect(rect.x + 12f, rect.y, w - 14f, h), text, nameStyle);
            GUI.color = prior;
        }
    }

    void DrawNeed(ref float x, float y, RescueItemId item)
    {
        var def = RescueItems.Get(item);
        bool packed = LoadoutState.Has(item);
        var box = new Rect(x, y, 22f, 20f);

        Fill(box, packed ? def.color : new Color(0.25f, 0.25f, 0.28f));
        if (!packed)
        {
            // Not in the pack: a red frame, so the crew can tell at a glance that this one is out of reach.
            var red = new Color(0.95f, 0.25f, 0.2f);
            Fill(new Rect(box.x, box.y, box.width, 2f), red);
            Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), red);
            Fill(new Rect(box.x, box.y, 2f, box.height), red);
            Fill(new Rect(box.xMax - 2f, box.y, 2f, box.height), red);
        }
        var prior = GUI.color;
        GUI.color = packed ? Color.white : new Color(1f, 0.6f, 0.55f);
        GUI.Label(box, def.glyph, glyphStyle);
        GUI.color = prior;
        x += 26f;
    }

    static string FirstName(Survivor s)
    {
        string full = s.DisplayName;
        int space = full.IndexOf(' ');
        return space > 0 ? full.Substring(0, space) : full;
    }

    // ------------------------------------------------------------------ the roster

    /// <summary>Screen height kept clear below the roster for the prompts (throw, pick-up, power cut, status).</summary>
    const float PromptReserve = 250f;

    void DrawRoster()
    {
        var records = manager.Roster.Records;
        if (records.Count == 0) return;

        const float x = 20f, y = 150f, w = 300f, header = 30f;
        int safe = manager.Roster.CountWith(ResidentStatus.Registered);

        // The prompts live at the bottom of the screen. In a short window the list is squeezed to the room there is,
        // and below a readable size it folds up to the headline: "3 / 10 safe".
        float room = Screen.height - y - PromptReserve - header;
        float row = Mathf.Min(21f, room / records.Count);
        bool folded = row < 12f;

        float h = folded ? header : header + records.Count * row;
        Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.45f));
        GUI.Label(new Rect(x + 10f, y + 3f, w - 20f, 24f), "ROSTER", headerStyle);
        GUI.Label(new Rect(x + 10f, y + 3f, w - 20f, 24f), safe + " / " + records.Count + " safe", rosterRight);
        if (folded) return;

        int fontSize = Mathf.Clamp(Mathf.RoundToInt(15f * row / 21f), 11, 15);
        rosterStyle.fontSize = fontSize;
        rosterRight.fontSize = fontSize;

        float ry = y + header - 2f;
        foreach (var r in records)
        {
            var s = r.survivor;
            bool done = r.status == ResidentStatus.Registered;
            bool known = s != null && s.Discovered;

            Color tag = done ? new Color(0.35f, 0.85f, 0.4f) : known ? TriageStyle.Colour(s.Triage) : new Color(0.4f, 0.42f, 0.46f);
            float pip = Mathf.Min(11f, row - 6f);
            Fill(new Rect(x + 10f, ry + (row - pip) * 0.5f, pip, pip), tag);

            var prior = GUI.color;
            GUI.color = done ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            GUI.Label(new Rect(x + 28f, ry, 170f, row), r.name, rosterStyle);
            GUI.Label(new Rect(x + 190f, ry, w - 200f, row), StatusText(r, known, manager.Desk), rosterRight);
            GUI.color = prior;
            ry += row;
        }
    }

    static string StatusText(ResidentRecord r, bool known, RegistrationDesk desk)
    {
        switch (r.status)
        {
            case ResidentStatus.Registered: return "safe";
            case ResidentStatus.Ashore:     return desk != null && desk.Current == r ? "registering" : "at the hall";
            case ResidentStatus.Aboard:     return "aboard";
            case ResidentStatus.Held:       return "coming in";
            default:
                if (!known) return "?";
                var s = r.survivor;
                return s != null && s.IsCritical ? "critical" : r.situation == Situation.Rooftop ? "roof" :
                       r.situation == Situation.Clinging ? "post" : r.situation == Situation.Trapped ? "debris" : "water";
        }
    }

    static void Fill(Rect rect, Color colour)
    {
        var prior = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prior;
    }
}
