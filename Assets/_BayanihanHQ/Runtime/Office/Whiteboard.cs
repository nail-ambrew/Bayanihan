using System.Text;
using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// The whiteboard on the back wall: the lead's hand-offs to each responder and any task list, ticked off as they
    /// finish. Written in marker colours on a white board.
    /// </summary>
    public class Whiteboard : MonoBehaviour
    {
        [Tooltip("Anchor in front of the board surface, readable from -Z.")]
        public Transform surface;
        public Vector2 size = new Vector2(3.4f, 1.9f);

        TextMeshPro title, body;
        float next;
        readonly StringBuilder sb = new StringBuilder(2048);

        void Start()
        {
            title = HqUi.WorldText("Title", surface, "GAWAIN NGAYON  <size=60%>today's work</size>", 2.2f, new Color(0.12f, 0.25f, 0.55f),
                new Vector2(size.x, 0.3f), TextAlignmentOptions.TopLeft, heading: true);
            title.transform.localPosition = new Vector3(0, size.y * 0.5f - 0.18f, 0);
            body = HqUi.WorldText("Items", surface, "", 1.25f, new Color(0.15f, 0.12f, 0.1f), new Vector2(size.x, size.y - 0.4f), TextAlignmentOptions.TopLeft);
            body.transform.localPosition = new Vector3(0, -0.17f, 0);
            body.overflowMode = TextOverflowModes.Truncate;
        }

        void Update()
        {
            var d = HqDirector.Instance;
            if (d == null || body == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            var s = d.State;
            sb.Clear();
            int shown = 0;
            // Open items first (in progress, then waiting), then the most recent finished ones.
            foreach (var pass in new[] { "in_progress", "pending", "done" })
                for (int i = s.Board.Items.Count - 1; i >= 0 && shown < 12; i--)
                {
                    var item = s.Board.Items[i];
                    bool match = pass == "done" ? item.IsDone : item.Status == pass;
                    if (!match) continue;
                    var owner = s.Get(item.Owner);
                    string box = item.Status == "completed" ? "<color=#2E8B2E>[x]</color>" : item.Status == "failed" ? "<color=#C0302A>[!]</color>"
                        : item.Status == "in_progress" ? "<color=#D97C2A>[~]</color>" : "[ ]";
                    string text = HqUi.Display(item.Text);
                    if (item.IsDone) text = "<color=#7A7068><s>" + text + "</s></color>";
                    var who = owner != null ? $" <size=75%><color={HqUi.Hex(owner.Entry.Colour)}>{owner.Name}</color></size>" : "";
                    sb.Append(box).Append(' ').Append(text).Append(who).Append('\n');
                    shown++;
                }
            if (shown == 0) sb.Append("<color=#9A9088>Walang nakasulat pa. (Nothing on the board yet.)</color>");
            body.text = sb.ToString();
        }
    }
}
