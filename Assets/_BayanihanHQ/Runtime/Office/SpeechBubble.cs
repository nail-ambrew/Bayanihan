using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// A speech bubble above a responder's head saying what they are doing ("Edit → SimpleBoat.cs"). It always faces the
    /// camera, sizes its card to the text, and is tinted by tone: cream for work, yellow when waiting, red on an error.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        public enum Tone { Normal, Waiting, Error, Done }

        TextMeshPro text;
        Transform card;
        Material cardMat;
        float hideAt;
        bool shown;
        Vector3 anchor;

        /// <summary>Makes a hidden bubble under the responder.</summary>
        public static SpeechBubble Create(Transform parent)
        {
            var go = new GameObject("Speech Bubble");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<SpeechBubble>();

            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "Card";
            Destroy(card.GetComponent<Collider>());
            card.transform.SetParent(go.transform, false);
            card.transform.localPosition = new Vector3(0, 0, 0.01f);
            b.cardMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HQ Bubble" };
            card.GetComponent<MeshRenderer>().sharedMaterial = b.cardMat;
            card.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.card = card.transform;

            b.text = HqUi.WorldText("Text", go.transform, "", 1.6f, UiTheme.Ink, new Vector2(2.2f, 0.6f));
            b.text.overflowMode = TextOverflowModes.Ellipsis;
            b.text.textWrappingMode = TextWrappingModes.Normal;
            go.SetActive(false);
            return b;
        }

        /// <summary>Shows a line for a while. Waiting and Error bubbles are given a long time and hidden by the caller.</summary>
        public void Show(string line, Tone tone, float seconds)
        {
            if (string.IsNullOrEmpty(line)) { Hide(); return; }
            text.text = line;
            Color bg, fg;
            switch (tone)
            {
                case Tone.Waiting: bg = new Color(1f, 0.86f, 0.3f); fg = UiTheme.Ink; break;
                case Tone.Error: bg = new Color(0.86f, 0.24f, 0.2f); fg = Color.white; break;
                case Tone.Done: bg = new Color(0.56f, 0.89f, 0.42f); fg = UiTheme.Ink; break;
                default: bg = new Color(0.99f, 0.95f, 0.87f); fg = UiTheme.Ink; break;
            }
            cardMat.SetColor("_BaseColor", bg);
            text.color = fg;
            hideAt = Time.time + seconds;
            shown = true;
            gameObject.SetActive(true);
            text.ForceMeshUpdate();
            var size = text.GetRenderedValues(false);
            card.localScale = new Vector3(Mathf.Clamp(size.x + 0.18f, 0.4f, 2.4f), Mathf.Clamp(size.y + 0.12f, 0.24f, 0.75f), 1f);
        }

        public void Hide()
        {
            shown = false;
            gameObject.SetActive(false);
        }

        /// <summary>Where the bubble sits (above the head); set every frame by the responder.</summary>
        public void Follow(Vector3 worldPoint) => anchor = worldPoint;

        void LateUpdate()
        {
            if (!shown) return;
            if (Time.time > hideAt) { Hide(); return; }
            var cam = Camera.main;
            transform.position = anchor;
            if (cam == null) return;
            transform.rotation = cam.transform.rotation;   // flat to the screen, like a sticker
            // Keep the text a readable size on screen: bigger when zoomed out, smaller up close.
            float scale = cam.orthographic ? Mathf.Clamp(cam.orthographicSize / 6f, 0.55f, 1.6f) : 1f;
            transform.localScale = Vector3.one * scale;
        }
    }
}
