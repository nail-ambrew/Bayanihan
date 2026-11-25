using TMPro;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// A fixed sign in the office (the DRRM banner, the sari-sari sign). The text is made when Play starts rather than
    /// saved in the scene: building TextMeshPro text in the Editor marks TMP's shared fallback font as changed, and the
    /// HQ must never touch the game's files. In Play the HQ draws with in-memory font copies (see HqUi).
    /// </summary>
    public class WorldLabel : MonoBehaviour
    {
        [TextArea] public string text = "";
        public float size = 1f;
        public Color colour = Color.white;
        public Vector2 box = new Vector2(2, 0.5f);
        public bool heading = true;

        void Start()
        {
            var t = HqUi.WorldText("Text", transform, text, size, colour, box, TextAlignmentOptions.Center, heading);
            t.overflowMode = TextOverflowModes.Truncate;
        }
    }
}
