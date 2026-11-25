using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// Phase 2's plain view of the state model: every desk, the whiteboard, the signal and the event log, as text, plus
    /// the Simulate / Pause / Reset toggles. The 3D office replaces it as the main view later; F1 still shows it for
    /// checking what the office is being told.
    /// </summary>
    public class HqDebugPanel : MonoBehaviour
    {
        [Tooltip("Shown when the scene starts. F1 toggles it.")]
        public bool visible = true;

        HqDirector director;
        Canvas canvas;
        TextMeshProUGUI header, agentsText, boardText, logText, simLabel, pauseLabel;
        float nextRefresh;
        readonly StringBuilder sb = new StringBuilder(4096);

        void Start()
        {
            director = HqDirector.Instance != null ? HqDirector.Instance : FindAnyObjectByType<HqDirector>();
            Build();
            canvas.enabled = visible;
        }

        void Build()
        {
            canvas = HqUi.NewCanvas("HQ Debug Panel", 50);
            canvas.transform.SetParent(transform, false);
            var root = HqUi.Panel("Backdrop", canvas.transform, new Color(0.09f, 0.06f, 0.04f, 0.94f));
            HqUi.Stretch(root.rectTransform, 16);

            header = HqUi.Label("Header", root.transform, "", 26, UiTheme.Cream, heading: true);
            HqUi.Place(header.rectTransform, new Vector2(0, 1), new Vector2(1300, 60), new Vector2(20, -10));

            simLabel = HqUi.Button("Simulate", root.transform, "Simulate", UiTheme.Orange, () => director.SetSimulate(!director.Simulating));
            HqUi.Place((RectTransform)simLabel.transform.parent, new Vector2(1, 1), new Vector2(170, 50), new Vector2(-380, -14));
            pauseLabel = HqUi.Button("Pause", root.transform, "Pause", UiTheme.Cream, () => director.SetPaused(!director.Paused));
            HqUi.Place((RectTransform)pauseLabel.transform.parent, new Vector2(1, 1), new Vector2(170, 50), new Vector2(-200, -14));
            HqUi.Button("Reset", root.transform, "Reset", UiTheme.Cream, () => director.ResetOffice());
            HqUi.Place((RectTransform)root.transform.Find("Reset"), new Vector2(1, 1), new Vector2(160, 50), new Vector2(-20, -14));

            agentsText = Column(root.transform, "Desks", 0f, 0.47f);
            boardText = Column(root.transform, "Whiteboard", 0.47f, 0.70f);
            logText = Column(root.transform, "Event log", 0.70f, 1f);
        }

        TextMeshProUGUI Column(Transform parent, string name, float from, float to)
        {
            var bg = HqUi.Panel(name, parent, HqUi.PanelLight);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(from, 0);
            rt.anchorMax = new Vector2(to, 1);
            rt.offsetMin = new Vector2(10, 10);
            rt.offsetMax = new Vector2(-10, -80);
            var t = HqUi.Label("Text", bg.transform, "", 17, UiTheme.CreamLight);
            t.font = HqUi.BodyFont;
            t.overflowMode = TextOverflowModes.Truncate;
            HqUi.Stretch(t.rectTransform, 12);
            return t;
        }

        void Update()
        {
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
                canvas.enabled = visible;
            }
            if (!visible || director == null || director.State == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void Refresh()
        {
            var s = director.State;
            long now = HqDirector.NowMs;
            int signal = s.Signal(now);
            long start = s.SessionStartMs(now);
            var elapsed = start > 0 ? TimeSpan.FromMilliseconds(now - start) : TimeSpan.Zero;
            header.text = $"BAYANIHAN HQ  |  {director.CurrentFeed}  |  sessions {s.ActiveSessions(now)}  |  working {s.CountStatus(AgentStatus.Working)}  |  " +
                          $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}  |  <color={HqUi.Hex(SignalColour(signal))}>SIGNAL NO. {signal}</color>" +
                          (director.Paused ? $"  |  <color=#FFE06E>PAUSED ({director.Receiver.Pending} queued)</color>" : "") +
                          $"  <size=60%>(replayed {director.Replayed}, received {director.Receiver.Received}, applied {s.EventsApplied})</size>";
            simLabel.text = director.Simulating ? "Simulating *" : "Simulate";
            pauseLabel.text = director.Paused ? "Resume" : "Pause";

            sb.Clear();
            foreach (var a in s.Agents)
            {
                var c = HqUi.Hex(a.Entry.Colour);
                var st = HqUi.Hex(HqUi.StatusColour(a.Status));
                sb.Append($"<color={c}><b>{a.Name}</b></color> <size=75%>{a.Entry.role}</size>  <color={st}><b>{HqUi.StatusText(a.Status)}</b></color>");
                if (a.Status == AgentStatus.Idle && a.Present) sb.Append($" <size=75%>{a.IdleFor(now) / 1000}s</size>");
                if (!a.Present) sb.Append(" <size=75%>(away)</size>");
                sb.Append('\n');
                if (!string.IsNullOrEmpty(a.CurrentTask)) sb.Append($"   <size=80%>task: {a.CurrentTask}</size>\n");
                if (!string.IsNullOrEmpty(a.Activity)) sb.Append($"   <size=80%>{a.Activity}{(string.IsNullOrEmpty(a.Helper) ? "" : "  [" + a.Helper + "]")}</size>\n");
                if (a.ToolCount > 0) sb.Append($"   <size=70%>tools {a.ToolCount} | files {a.FilesTouched.Count}</size>\n");
            }
            agentsText.text = sb.ToString();

            sb.Clear();
            sb.Append($"<b>{s.Board.CountDone()}/{s.Board.Items.Count} done</b>\n\n");
            foreach (var item in s.Board.Items)
            {
                var owner = s.Get(item.Owner);
                string mark = item.Status == "completed" ? "<color=#8EE36A>[x]</color>" : item.Status == "failed" ? "<color=#FF5A4D>[!]</color>" : item.Status == "in_progress" ? "<color=#FFE06E>[>]</color>" : "[ ]";
                sb.Append($"{mark} {item.Text} <size=70%><color={HqUi.Hex(owner != null ? owner.Entry.Colour : Color.gray)}>{(owner != null ? owner.Name : item.Owner)}</color> | {item.Kind}</size>\n");
            }
            boardText.text = sb.ToString();

            sb.Clear();
            for (int i = s.Log.Count - 1, n = 0; i >= 0 && n < 40; i--, n++)
            {
                var e = s.Log[i];
                var who = s.Get(e.agent);
                var t = DateTimeOffset.FromUnixTimeMilliseconds(e.ts).ToLocalTime();
                sb.Append($"<size=75%>{t:HH:mm:ss}</size> <color={HqUi.Hex(who != null ? who.Entry.Colour : Color.gray)}>{(who != null ? who.Name : e.agent)}</color> <size=80%>{e.ev}</size> {e.Summary()}\n");
            }
            logText.text = sb.ToString();
        }

        /// <summary>PAGASA signal colours, from calm to severe.</summary>
        public static Color SignalColour(int signal)
        {
            switch (signal)
            {
                case 1: return new Color(0.55f, 0.8f, 1f);
                case 2: return new Color(0.56f, 0.89f, 0.42f);
                case 3: return new Color(1f, 0.88f, 0.43f);
                case 4: return new Color(1f, 0.6f, 0.36f);
                default: return new Color(1f, 0.33f, 0.3f);
            }
        }
    }
}
