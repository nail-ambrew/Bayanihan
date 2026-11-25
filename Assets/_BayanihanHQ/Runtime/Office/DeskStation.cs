using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// One desk: the chair a responder sits on, the monitor that shows their current task, the nameplate and the desk
    /// lamp. The scene builder makes the furniture and fills in the references; the texts are made here at runtime.
    /// </summary>
    public class DeskStation : MonoBehaviour
    {
        [Tooltip("Desk slot from roster.json (0 = the lead's big desk).")]
        public int slot;
        [Tooltip("Floor point in front of the chair, facing the desk. The responder stands and sits here.")]
        public Transform seat;
        [Tooltip("Height of the chair's seat above the floor.")]
        public float seatHeight = 0.46f;
        [Tooltip("Anchor just in front of the monitor screen (readable from -Z).")]
        public Transform screenAnchor;
        public Renderer screen;
        public Transform nameplateAnchor;
        public Renderer nameplateStripe;
        public Light lamp;
        public Renderer lampBulb;
        [Tooltip("Monitor text box size in metres.")]
        public Vector2 screenSize = new Vector2(0.56f, 0.34f);

        /// <summary>The desk's owner, once bound.</summary>
        public AgentState Agent { get; private set; }

        TextMeshPro monitorText, nameText;
        Material screenMat, stripeMat, bulbMat;
        Color lampWarm;
        float nextRefresh;

        /// <summary>Puts a responder's name and colour on the desk.</summary>
        public void Bind(AgentState agent)
        {
            Agent = agent;
            var colour = agent.Entry.Colour;
            if (screen != null) screenMat = screen.material;
            if (lampBulb != null) bulbMat = lampBulb.material;
            if (lamp != null) lampWarm = lamp.color;
            if (nameplateStripe != null)
            {
                stripeMat = nameplateStripe.material;
                stripeMat.SetColor("_BaseColor", colour);
            }
            if (nameplateAnchor != null && nameText == null)
            {
                nameText = HqUi.WorldText("Nameplate", nameplateAnchor, "", 1.1f, UiTheme.Ink, new Vector2(0.62f, 0.16f), heading: true);
                nameText.overflowMode = TextOverflowModes.Truncate;
            }
            if (nameText != null) nameText.text = $"{agent.Name}\n<size=60%>{agent.Entry.role}</size>";
            if (screenAnchor != null && monitorText == null)
            {
                monitorText = HqUi.WorldText("Monitor", screenAnchor, "", 0.55f, UiTheme.CreamLight, screenSize, TextAlignmentOptions.TopLeft);
                monitorText.margin = new Vector4(0.02f, 0.02f, 0.02f, 0.02f);
            }
            Refresh();
        }

        void Update()
        {
            if (Agent == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        /// <summary>Monitor text, screen glow and lamp colour from the agent's status.</summary>
        void Refresh()
        {
            var a = Agent;
            var status = a.Status;
            var statusColour = HqUi.StatusColour(status);

            if (monitorText != null)
            {
                string body;
                if (!a.Present && !a.IsLead) body = "<color=#8C7B6B>Wala pa · not here yet</color>";
                else
                {
                    body = $"<color={HqUi.Hex(statusColour)}><b>{HqUi.StatusText(status).ToUpperInvariant()}</b></color>\n";
                    if (!string.IsNullOrEmpty(a.CurrentTask)) body += $"<size=80%>{HqUi.Display(a.CurrentTask)}</size>\n";
                    if (!string.IsNullOrEmpty(a.Activity)) body += $"<color=#FDEACF>{HqUi.Display(a.Activity)}</color>\n";
                    body += $"<size=70%><color=#B8A48C>tools {a.ToolCount} · files {a.FilesTouched.Count}</color></size>";
                }
                monitorText.text = body;
            }

            if (screenMat != null)
            {
                // A sleeping monitor is dim; work makes it glow in the status colour.
                float glow = status == AgentStatus.Idle ? 0.15f : status == AgentStatus.Working ? 1.1f : 0.6f;
                var tint = Color.Lerp(new Color(0.08f, 0.12f, 0.16f), statusColour, status == AgentStatus.Idle ? 0.05f : 0.25f);
                screenMat.SetColor("_BaseColor", tint);
                screenMat.SetColor("_EmissionColor", tint * glow);
            }

            var lampColour = status == AgentStatus.Error ? new Color(1f, 0.2f, 0.15f)
                : status == AgentStatus.Waiting ? new Color(1f, 0.85f, 0.3f)
                : lampWarm;
            if (lamp != null)
            {
                lamp.color = lampColour;
                lamp.intensity = status == AgentStatus.Error || status == AgentStatus.Waiting ? 2.2f : a.Present ? 1.0f : 0.25f;
            }
            if (bulbMat != null) bulbMat.SetColor("_EmissionColor", lampColour * (a.Present ? 2f : 0.3f));
        }
    }
}
