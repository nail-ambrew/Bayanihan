using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// A wall board in the shape of PAGASA's tropical cyclone wind signal: the number is the session's health. 1 = all quiet,
    /// up to 5 = errors or something blocked on the owner.
    /// </summary>
    public class SignalBoard : MonoBehaviour
    {
        public Transform face;
        [Tooltip("Five lamps, signal 1 to 5.")]
        public Renderer[] lamps;
        public Renderer backing;

        static readonly string[] Words =
        {
            "", "Tahimik · all quiet", "May ginagawa · work under way", "Abala · busy / a failure",
            "Hinihintay ka · waiting for you", "Delikado · errors / blocked"
        };

        TextMeshPro number, label;
        Material[] lampMats;
        Material backingMat;
        int shown = -1;

        void Start()
        {
            HqUi.WorldText("Heading", face, "TROPICAL CYCLONE\nWIND SIGNAL", 1.1f, Color.white, new Vector2(1.5f, 0.3f), heading: true)
                .transform.localPosition = new Vector3(0, 0.62f, 0);
            number = HqUi.WorldText("Number", face, "1", 6f, Color.white, new Vector2(1.5f, 0.95f), heading: true);
            number.transform.localPosition = new Vector3(0, 0.08f, 0);
            Fit(number, 3f, 7f);
            label = HqUi.WorldText("Label", face, "", 1.0f, Color.white, new Vector2(1.5f, 0.3f));
            label.transform.localPosition = new Vector3(0, -0.45f, 0);
            Fit(label, 0.5f, 1.2f);
            if (lamps != null)
            {
                lampMats = new Material[lamps.Length];
                for (int i = 0; i < lamps.Length; i++) lampMats[i] = lamps[i].material;
            }
            if (backing != null) backingMat = backing.material;
        }

        /// <summary>Lets TMP shrink the text to fit its box, so a long label never disappears into an ellipsis.</summary>
        static void Fit(TextMeshPro t, float min, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
        }

        void Update()
        {
            var d = HqDirector.Instance;
            if (d == null || number == null) return;
            int signal = d.State.Signal(HqDirector.NowMs);
            float pulse = signal >= 4 ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            var colour = HqDebugPanel.SignalColour(signal);
            if (lampMats != null)
                for (int i = 0; i < lampMats.Length; i++)
                {
                    bool on = i < signal;
                    lampMats[i].SetColor("_BaseColor", on ? colour : new Color(0.2f, 0.18f, 0.16f));
                    lampMats[i].SetColor("_EmissionColor", on ? colour * 0.9f * pulse : Color.black);
                }
            if (signal == shown) return;
            shown = signal;
            number.text = signal.ToString();
            label.text = Words[Mathf.Clamp(signal, 1, 5)];
            if (backingMat != null) backingMat.SetColor("_BaseColor", Color.Lerp(new Color(0.1f, 0.2f, 0.45f), colour, 0.35f));
        }
    }
}
