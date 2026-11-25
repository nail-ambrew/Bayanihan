using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BayanihanHQ
{
    /// <summary>
    /// The office's screen overlay, built in code like the game's own UI:
    /// a top bar (connection, sessions, working, elapsed, signal, Simulate / Pause / Reset), agent cards on the right,
    /// an event log along the bottom that can be filtered by agent, a banner whenever Claude Code is waiting for the
    /// owner, and a detail panel for the selected agent. Clicking a responder in the room (or their card) focuses the
    /// camera on them and opens the panel. H hides the overlay; F1 shows the plain debug view.
    /// </summary>
    public class HqOverlay : MonoBehaviour
    {
        const int LogLines = 9;

        HqDirector director;
        HqOffice office;
        OfficeCamera officeCamera;
        Canvas canvas;
        RectTransform root;

        TextMeshProUGUI feedLabel, statsLabel, signalLabel, simLabel, pauseLabel;
        Image feedDot, signalChip;
        RectTransform alertBanner;
        TextMeshProUGUI alertText;
        Image alertBg;
        readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        TextMeshProUGUI logText;
        readonly Dictionary<string, Image> filterChips = new Dictionary<string, Image>();
        string logFilter;
        RectTransform detailPanel;
        TextMeshProUGUI detailTitle, detailBody;
        Image detailStripe;
        string selected;
        float nextRefresh;
        bool hidden;
        readonly StringBuilder sb = new StringBuilder(4096);

        class Card
        {
            public Image bg, stripe, chip;
            public TextMeshProUGUI name, status, line;
        }

        void Start()
        {
            director = HqDirector.Instance;
            office = FindAnyObjectByType<HqOffice>();
            officeCamera = FindAnyObjectByType<OfficeCamera>();
            if (director == null) return;
            Build();
            director.StateRebuilt += () => { logFilter = null; Refresh(); };
        }

        // ------------------------------------------------------------------ layout

        void Build()
        {
            canvas = HqUi.NewCanvas("HQ Overlay", 30);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;

            // Top bar.
            var bar = HqUi.Panel("Top Bar", root, HqUi.PanelColour);
            bar.rectTransform.anchorMin = new Vector2(0, 1);
            bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1);
            bar.rectTransform.sizeDelta = new Vector2(0, 64);
            bar.rectTransform.anchoredPosition = Vector2.zero;

            var title = HqUi.Label("Title", bar.transform, "BAYANIHAN HQ", 30, UiTheme.Orange, heading: true);
            HqUi.Place(title.rectTransform, new Vector2(0, 0.5f), new Vector2(260, 50), new Vector2(18, 0));
            title.alignment = TextAlignmentOptions.MidlineLeft;

            feedDot = HqUi.Panel("Feed Dot", bar.transform, Color.gray);
            HqUi.Place(feedDot.rectTransform, new Vector2(0, 0.5f), new Vector2(16, 16), new Vector2(282, 0));
            feedLabel = HqUi.Label("Feed", bar.transform, "", 22, UiTheme.CreamLight, heading: true);
            HqUi.Place(feedLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(190, 50), new Vector2(306, 0));
            feedLabel.alignment = TextAlignmentOptions.MidlineLeft;

            statsLabel = HqUi.Label("Stats", bar.transform, "", 21, UiTheme.Cream);
            HqUi.Place(statsLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(560, 50), new Vector2(500, 0));
            statsLabel.alignment = TextAlignmentOptions.MidlineLeft;

            signalChip = HqUi.Panel("Signal Chip", bar.transform, Color.gray);
            HqUi.Place(signalChip.rectTransform, new Vector2(0, 0.5f), new Vector2(170, 44), new Vector2(1070, 0));
            signalLabel = HqUi.Label("Signal", signalChip.transform, "", 22, UiTheme.Ink, heading: true);
            signalLabel.alignment = TextAlignmentOptions.Center;
            HqUi.Stretch(signalLabel.rectTransform);

            simLabel = HqUi.Button("Simulate", bar.transform, "Simulate", UiTheme.Orange, () => director.SetSimulate(!director.Simulating));
            HqUi.Place((RectTransform)simLabel.transform.parent, new Vector2(1, 0.5f), new Vector2(170, 46), new Vector2(-370, 0));
            pauseLabel = HqUi.Button("Pause", bar.transform, "Pause", UiTheme.Cream, () => director.SetPaused(!director.Paused));
            HqUi.Place((RectTransform)pauseLabel.transform.parent, new Vector2(1, 0.5f), new Vector2(160, 46), new Vector2(-195, 0));
            var reset = HqUi.Button("Reset", bar.transform, "Reset", UiTheme.Cream, () => { director.ResetOffice(); Select(null); });
            HqUi.Place((RectTransform)reset.transform.parent, new Vector2(1, 0.5f), new Vector2(160, 46), new Vector2(-20, 0));

            // Waiting banner, under the top bar.
            alertBg = HqUi.Panel("Waiting Banner", root, UiTheme.Notice);
            alertBanner = alertBg.rectTransform;
            HqUi.Place(alertBanner, new Vector2(0.5f, 1), new Vector2(900, 58), new Vector2(-200, -76));
            var alertButton = alertBg.gameObject.AddComponent<Button>();
            alertButton.onClick.AddListener(() =>
            {
                var w = director.State.WaitingAgents();
                if (w.Count > 0) Select(w[0].Key, focus: true);
            });
            alertText = HqUi.Label("Text", alertBanner, "", 24, UiTheme.Ink, heading: true);
            alertText.alignment = TextAlignmentOptions.Center;
            HqUi.Stretch(alertText.rectTransform, 8);
            alertBanner.gameObject.SetActive(false);

            // Agent cards on the right.
            var cardsPanel = HqUi.Panel("Agent Cards", root, new Color(0, 0, 0, 0));
            var cp = cardsPanel.rectTransform;
            cp.anchorMin = new Vector2(1, 0);
            cp.anchorMax = new Vector2(1, 1);
            cp.pivot = new Vector2(1, 1);
            cp.sizeDelta = new Vector2(400, -84);
            cp.anchoredPosition = new Vector2(-10, -74);
            var layout = cardsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            foreach (var agent in director.State.Agents) cards[agent.Key] = MakeCard(cp, agent);

            // Event log along the bottom.
            var log = HqUi.Panel("Event Log", root, HqUi.PanelColour);
            var lr = log.rectTransform;
            lr.anchorMin = new Vector2(0, 0);
            lr.anchorMax = new Vector2(1, 0);
            lr.pivot = new Vector2(0, 0);
            lr.offsetMin = new Vector2(10, 10);
            lr.offsetMax = new Vector2(-420, 270);
            var logTitle = HqUi.Label("Title", lr, "EVENT LOG", 20, UiTheme.Orange, heading: true);
            HqUi.Place(logTitle.rectTransform, new Vector2(0, 1), new Vector2(160, 36), new Vector2(14, -4));
            var chips = HqUi.Rect("Filters", lr);
            HqUi.Place(chips, new Vector2(0, 1), new Vector2(1300, 34), new Vector2(170, -6));
            var chipLayout = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.spacing = 6;
            chipLayout.childForceExpandWidth = false;
            chipLayout.childControlWidth = false;
            AddChip(chips, null, "All", UiTheme.Cream);
            foreach (var agent in director.State.Agents) AddChip(chips, agent.Key, agent.Name, agent.Entry.Colour);
            logText = HqUi.Label("Lines", lr, "", 19, UiTheme.CreamLight);
            logText.rectTransform.anchorMin = Vector2.zero;
            logText.rectTransform.anchorMax = Vector2.one;
            logText.rectTransform.offsetMin = new Vector2(16, 10);
            logText.rectTransform.offsetMax = new Vector2(-16, -46);
            logText.overflowMode = TextOverflowModes.Truncate;
            logText.textWrappingMode = TextWrappingModes.NoWrap;

            // Detail panel on the left (hidden until someone is selected).
            var detail = HqUi.Panel("Agent Detail", root, HqUi.PanelColour);
            detailPanel = detail.rectTransform;
            detailPanel.anchorMin = new Vector2(0, 1);
            detailPanel.anchorMax = new Vector2(0, 1);
            detailPanel.pivot = new Vector2(0, 1);
            detailPanel.sizeDelta = new Vector2(440, 700);
            detailPanel.anchoredPosition = new Vector2(10, -74);
            detailStripe = HqUi.Panel("Stripe", detailPanel, Color.gray);
            HqUi.Place(detailStripe.rectTransform, new Vector2(0, 1), new Vector2(440, 8), Vector2.zero);
            detailTitle = HqUi.Label("Title", detailPanel, "", 30, UiTheme.CreamLight, heading: true);
            HqUi.Place(detailTitle.rectTransform, new Vector2(0, 1), new Vector2(360, 70), new Vector2(18, -14));
            detailTitle.alignment = TextAlignmentOptions.TopLeft;
            var close = HqUi.Button("Close", detailPanel, "X", UiTheme.Cream, () => Select(null));
            HqUi.Place((RectTransform)close.transform.parent, new Vector2(1, 1), new Vector2(44, 44), new Vector2(-12, -16));
            detailBody = HqUi.Label("Body", detailPanel, "", 19, UiTheme.Cream);
            detailBody.rectTransform.anchorMin = Vector2.zero;
            detailBody.rectTransform.anchorMax = Vector2.one;
            detailBody.rectTransform.offsetMin = new Vector2(18, 14);
            detailBody.rectTransform.offsetMax = new Vector2(-18, -96);
            detailBody.overflowMode = TextOverflowModes.Truncate;
            detailPanel.gameObject.SetActive(false);

            var hint = HqUi.Label("Hint", root, "Right-drag / Q E orbit · scroll zoom · middle-drag / WASD pan · F frame all · click someone for details · H hide · F1 debug", 16, new Color(1, 1, 1, 0.55f));
            HqUi.Place(hint.rectTransform, new Vector2(0, 0), new Vector2(1400, 26), new Vector2(18, 276));
            hint.alignment = TextAlignmentOptions.BottomLeft;
        }

        Card MakeCard(RectTransform parent, AgentState agent)
        {
            var c = new Card();
            c.bg = HqUi.Panel("Card " + agent.Name, parent, HqUi.PanelColour);
            c.bg.rectTransform.sizeDelta = new Vector2(400, 78);
            var button = c.bg.gameObject.AddComponent<Button>();
            var key = agent.Key;
            button.onClick.AddListener(() => Select(key, focus: true));
            c.stripe = HqUi.Panel("Stripe", c.bg.transform, agent.Entry.Colour);
            HqUi.Place(c.stripe.rectTransform, new Vector2(0, 0.5f), new Vector2(8, 78), Vector2.zero);
            c.name = HqUi.Label("Name", c.bg.transform, $"{agent.Name}  <size=70%><color=#B8A48C>{agent.Entry.role}</color></size>", 22, UiTheme.CreamLight, heading: true);
            HqUi.Place(c.name.rectTransform, new Vector2(0, 1), new Vector2(260, 34), new Vector2(18, -4));
            c.name.alignment = TextAlignmentOptions.TopLeft;
            c.chip = HqUi.Panel("Status", c.bg.transform, Color.gray);
            HqUi.Place(c.chip.rectTransform, new Vector2(1, 1), new Vector2(118, 28), new Vector2(-8, -8));
            c.status = HqUi.Label("Text", c.chip.transform, "", 16, UiTheme.Ink, heading: true);
            c.status.alignment = TextAlignmentOptions.Center;
            HqUi.Stretch(c.status.rectTransform);
            c.line = HqUi.Label("Line", c.bg.transform, "", 17, UiTheme.Cream);
            c.line.rectTransform.anchorMin = new Vector2(0, 0);
            c.line.rectTransform.anchorMax = new Vector2(1, 0);
            c.line.rectTransform.pivot = new Vector2(0, 0);
            c.line.rectTransform.offsetMin = new Vector2(18, 6);
            c.line.rectTransform.offsetMax = new Vector2(-10, 40);
            c.line.alignment = TextAlignmentOptions.BottomLeft;
            return c;
        }

        void AddChip(RectTransform parent, string key, string label, Color colour)
        {
            var chip = HqUi.Panel("Chip " + label, parent, new Color(colour.r, colour.g, colour.b, 0.35f));
            chip.rectTransform.sizeDelta = new Vector2(Mathf.Max(54, label.Length * 13 + 24), 30);
            var text = HqUi.Label("Text", chip.transform, label, 16, UiTheme.CreamLight, heading: true);
            text.alignment = TextAlignmentOptions.Center;
            HqUi.Stretch(text.rectTransform);
            chip.gameObject.AddComponent<Button>().onClick.AddListener(() => { logFilter = key; Refresh(); });
            filterChips[key ?? ""] = chip;
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (director == null || canvas == null) return;
            var keys = Keyboard.current;
            if (keys != null && keys.hKey.wasPressedThisFrame)
            {
                hidden = !hidden;
                canvas.enabled = !hidden;
            }
            HandleClick();

            // The banner pulses every frame; the texts refresh five times a second.
            if (alertBanner.gameObject.activeSelf)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                alertBg.color = Color.Lerp(UiTheme.Notice, new Color(1f, 0.6f, 0.25f), p * 0.6f);
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void Refresh()
        {
            if (canvas == null) return;
            var s = director.State;
            long now = HqDirector.NowMs;

            var feed = director.CurrentFeed;
            feedDot.color = feed == HqDirector.Feed.Live ? UiTheme.Green
                : feed == HqDirector.Feed.Simulating ? UiTheme.Orange
                : feed == HqDirector.Feed.Listening ? new Color(0.55f, 0.75f, 1f)
                : new Color(1f, 0.35f, 0.3f);
            feedLabel.text = feed == HqDirector.Feed.Offline ? "Offline" : feed.ToString();
            long start = s.SessionStartMs(now);
            var elapsed = start > 0 ? TimeSpan.FromMilliseconds(now - start) : TimeSpan.Zero;
            statsLabel.text = $"Sessions <b>{s.ActiveSessions(now)}</b>   Working <b>{s.CountStatus(AgentStatus.Working)}</b>   " +
                              $"Waiting <b>{s.CountStatus(AgentStatus.Waiting)}</b>   Session <b>{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}</b>" +
                              (director.Paused ? $"   <color=#FFE06E>PAUSED · {director.Receiver.Pending} queued</color>" : "");
            int signal = s.Signal(now);
            signalChip.color = HqDebugPanel.SignalColour(signal);
            signalLabel.text = "SIGNAL NO. " + signal;
            simLabel.text = director.Simulating ? "Stop Sim" : "Simulate";
            pauseLabel.text = director.Paused ? "Resume" : "Pause";

            // Waiting banner.
            var waiting = s.WaitingAgents();
            alertBanner.gameObject.SetActive(waiting.Count > 0);
            if (waiting.Count > 0)
            {
                var w = waiting[0];
                var what = HqUi.Display(w.Activity);
                alertText.text = $"<b>!</b>  {w.Name} needs you  <size=80%>{(w.WaitingKind == "idle_prompt" ? "Claude is waiting for your next prompt" : what)}</size>" +
                                 (waiting.Count > 1 ? $"  <size=75%>(+{waiting.Count - 1} more)</size>" : "");
            }

            // Cards.
            foreach (var a in s.Agents)
            {
                if (!cards.TryGetValue(a.Key, out var c)) continue;
                c.chip.color = HqUi.StatusColour(a.Status);
                c.status.text = a.Status == AgentStatus.Waiting ? "WAITING" : a.Status.ToString().ToUpperInvariant();
                string line;
                if (!a.Present && !a.IsLead) line = "<color=#8C7B6B>not here yet</color>";
                else if (a.Status == AgentStatus.Idle && a.IdleFor(now) > 60_000) line = $"<color=#B8A48C>on break · idle {a.IdleFor(now) / 60_000} min</color>";
                else line = HqUi.Display(!string.IsNullOrEmpty(a.Activity) ? a.Activity : a.CurrentTask);
                c.line.text = line;
                c.bg.color = a.Key == selected ? HqUi.PanelLight : HqUi.PanelColour;
            }

            // Event log.
            foreach (var kv in filterChips)
            {
                bool on = (kv.Key == "" && logFilter == null) || kv.Key == logFilter;
                var col = kv.Value.color;
                kv.Value.color = new Color(col.r, col.g, col.b, on ? 0.95f : 0.3f);
            }
            sb.Clear();
            int shown = 0;
            for (int i = s.Log.Count - 1; i >= 0 && shown < LogLines; i--)
            {
                var e = s.Log[i];
                if (logFilter != null && e.agent != logFilter) continue;
                var who = s.Get(e.agent);
                var t = DateTimeOffset.FromUnixTimeMilliseconds(e.ts).ToLocalTime();
                string colour = HqUi.Hex(who != null ? who.Entry.Colour : Color.gray);
                string flag = e.ev == "PostToolUseFailure" && !e.intr ? " <color=#FF6A5A>failed</color>"
                    : e.ev == "PermissionRequest" ? " <color=#FFE06E>asks permission</color>" : "";
                sb.Append($"<color=#8C7B6B>{t:HH:mm:ss}</color>  <color={colour}><b>{(who != null ? who.Name : e.agent)}</b></color>  " +
                          $"<color=#B8A48C>{ShortEvent(e.ev)}</color>  {HqUi.Display(e.Summary())}{flag}\n");
                shown++;
            }
            if (shown == 0) sb.Append("<color=#8C7B6B>No events yet. Start a Claude Code session in ~/Documents/Bayanihan, or press Simulate.</color>");
            logText.text = sb.ToString();

            RefreshDetail(now);
        }

        static string ShortEvent(string ev)
        {
            switch (ev)
            {
                case "PreToolUse": return "tool";
                case "PostToolUse": return "done";
                case "PostToolUseFailure": return "fail";
                case "PermissionRequest": return "ask";
                case "UserPromptSubmit": return "prompt";
                case "SubagentStart": return "start";
                case "SubagentStop": return "stop";
                case "Notification": return "notice";
                case "SessionStart": return "session";
                case "SessionEnd": return "end";
                default: return ev;
            }
        }

        // ------------------------------------------------------------------ selection

        /// <summary>Opens (or closes, with null) the detail panel for an agent, optionally gliding the camera to them.</summary>
        public void Select(string key, bool focus = false)
        {
            selected = key;
            detailPanel.gameObject.SetActive(key != null);
            if (key == null) { officeCamera?.FrameAll(); return; }
            if (focus && officeCamera != null && office != null && office.Responders.TryGetValue(key, out var r) && r.gameObject.activeInHierarchy)
                officeCamera.Focus(r.transform.position, 3.4f);
            else if (focus && officeCamera != null && office != null)
            {
                var desk = Array.Find(office.desks, d => d != null && d.Agent != null && d.Agent.Key == key);
                if (desk != null) officeCamera.Focus(desk.transform.position, 3.4f);
            }
            Refresh();
        }

        void RefreshDetail(long now)
        {
            if (selected == null) return;
            var a = director.State.Get(selected);
            if (a == null) { Select(null); return; }
            detailStripe.color = a.Entry.Colour;
            detailTitle.text = $"{a.Name}\n<size=60%><color=#B8A48C>{a.Entry.role}</color></size>";
            sb.Clear();
            sb.Append($"<color={HqUi.Hex(HqUi.StatusColour(a.Status))}><b>{HqUi.StatusText(a.Status)}</b></color>");
            if (a.Status == AgentStatus.Idle && a.Present) sb.Append($"  <color=#8C7B6B>for {a.IdleFor(now) / 1000}s</color>");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(a.CurrentTask)) sb.Append($"<color=#B8A48C>Task</color>  {HqUi.Display(a.CurrentTask)}\n");
            if (!string.IsNullOrEmpty(a.Tool)) sb.Append($"<color=#B8A48C>Tool</color>  {a.Tool}{(string.IsNullOrEmpty(a.File) ? "" : "  " + a.File)}\n");
            if (!string.IsNullOrEmpty(a.Helper)) sb.Append($"<color=#B8A48C>Helper</color>  {a.Helper}\n");
            sb.Append($"<color=#B8A48C>Tool uses</color>  {a.ToolCount}    <color=#B8A48C>Files touched</color>  {a.FilesTouched.Count}\n");
            int n = 0;
            foreach (var f in a.FilesTouched) { if (n++ >= 6) { sb.Append("   …\n"); break; } sb.Append($"   <size=85%>{f}</size>\n"); }
            sb.Append("\n<color=#F09A47><b>Last 20 events</b></color>\n");
            for (int i = a.Recent.Count - 1; i >= 0; i--)
            {
                var e = a.Recent[i];
                var t = DateTimeOffset.FromUnixTimeMilliseconds(e.ts).ToLocalTime();
                sb.Append($"<size=85%><color=#8C7B6B>{t:HH:mm:ss}</color> <color=#B8A48C>{ShortEvent(e.ev)}</color> {HqUi.Display(e.Summary())}</size>\n");
            }
            detailBody.text = sb.ToString();
        }

        /// <summary>
        /// A left click in the room picks the responder nearest the cursor on screen (within about 70 px). Characters have
        /// no colliders, so picking is by screen distance, which also makes small figures easy to hit when zoomed out.
        /// </summary>
        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || office == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main;
            if (cam == null) return;
            var click = mouse.position.ReadValue();
            float best = 70f * Screen.height / 1080f;
            string pick = null;
            foreach (var kv in office.Responders)
            {
                var r = kv.Value;
                if (!r.gameObject.activeInHierarchy) continue;
                var mid = Vector3.Lerp(r.transform.position, r.HeadTop, 0.6f);
                var sp = cam.WorldToScreenPoint(mid);
                if (sp.z < 0) continue;
                float d = Vector2.Distance(click, sp);
                if (d < best) { best = d; pick = kv.Key; }
            }
            if (pick != null) Select(pick, focus: true);
        }
    }
}
