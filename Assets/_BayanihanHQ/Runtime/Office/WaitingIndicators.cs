using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BayanihanHQ
{
    /// <summary>
    /// "Claude Code needs you" has to be impossible to miss, so every waiting responder also gets a pulsing badge on the
    /// screen itself: over their head when they are in view, pinned to the nearest screen edge (with an arrow) when they
    /// are not. Screen-space UI is never hidden behind walls or furniture.
    /// </summary>
    public class WaitingIndicators : MonoBehaviour
    {
        HqOffice office;
        Canvas canvas;
        RectTransform root;
        readonly Dictionary<string, (RectTransform rt, TextMeshProUGUI label, Image bg)> badges = new Dictionary<string, (RectTransform, TextMeshProUGUI, Image)>();

        void Start()
        {
            office = GetComponent<HqOffice>();
            canvas = HqUi.NewCanvas("HQ Waiting Indicators", 40);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;
        }

        void LateUpdate()
        {
            if (office == null || canvas == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
            var size = root.rect.size;

            foreach (var kv in office.Responders)
            {
                var r = kv.Value;
                bool waiting = r.Agent.Status == AgentStatus.Waiting && r.gameObject.activeInHierarchy;
                if (!badges.TryGetValue(kv.Key, out var badge))
                {
                    if (!waiting) continue;
                    badge = Make(r);
                    badges[kv.Key] = badge;
                }
                if (badge.rt.gameObject.activeSelf != waiting) badge.rt.gameObject.SetActive(waiting);
                if (!waiting) continue;

                // Viewport position of the head; behind the camera is flipped so the edge arrow still points the right way.
                var vp = cam.WorldToViewportPoint(r.HeadTop + Vector3.up * 1.9f);
                if (vp.z < 0) vp = new Vector3(1 - vp.x, 1 - vp.y, 0);
                bool onScreen = vp.x > 0.04f && vp.x < 0.96f && vp.y > 0.06f && vp.y < 0.94f;
                var clamped = new Vector2(Mathf.Clamp(vp.x, 0.07f, 0.93f), Mathf.Clamp(vp.y, 0.08f, 0.92f));
                badge.rt.anchoredPosition = new Vector2(clamped.x * size.x, clamped.y * size.y);
                string arrow = onScreen ? "" : (vp.x < 0.04f ? "<< " : vp.x > 0.96f ? ">> " : vp.y > 0.94f ? "^ " : "v ");
                badge.label.text = $"{arrow}<b>!</b>  {r.Agent.Name} needs you";
                badge.bg.color = new Color(1f, 0.82f, 0.22f, 0.75f + 0.25f * pulse);
                badge.rt.localScale = Vector3.one * (0.96f + 0.08f * pulse);
            }
        }

        (RectTransform, TextMeshProUGUI, Image) Make(Responder r)
        {
            var bg = HqUi.Panel("Badge " + r.Agent.Name, root, new Color(1f, 0.82f, 0.22f));
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);   // sits on top of the speech bubble rather than over it
            rt.sizeDelta = new Vector2(300, 52);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.2f, 0.12f, 0.05f, 0.9f);
            outline.effectDistance = new Vector2(3, -3);
            var label = HqUi.Label("Label", bg.transform, "", 26, UiTheme.Ink, heading: true);
            label.alignment = TextAlignmentOptions.Center;
            HqUi.Stretch(label.rectTransform, 6);
            return (rt, label, bg);
        }
    }
}
