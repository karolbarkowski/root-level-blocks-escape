using UnityEngine;

namespace BlocksEscape
{
    // A word spelled with the 3D glyph models (Prefabs/Scenery/Letter_A..Z, Digit_0..9) on a prop, such as the menu
    // planks. Lowercase is shown in capitals. The glyphs are placed at Center, turned by Rotation (90 on X lays them on
    // top of a flat prop, reading up the screen), and shrunk to Max Width if the word is too long.
    // The text hangs under the prop's model (its first child), so it squashes along with it when a WorldButton is pressed.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class WorldLabel : MonoBehaviour
    {
        const string TextRootName = "Label";

        [SerializeField] string text = "";
        [Tooltip("Letter models A-Z, in order.")]
        [SerializeField] GameObject[] letters = new GameObject[26];
        [Tooltip("Digit models 0-9, in order.")]
        [SerializeField] GameObject[] digits = new GameObject[10];
        [Tooltip("Centre of the text, in the prop's local space.")]
        [SerializeField] Vector3 center = new Vector3(0f, 0.33f, 0f);
        [SerializeField] Vector3 rotation = new Vector3(90f, 0f, 0f);
        [SerializeField, Min(0.01f)] float letterHeight = 0.45f;
        [Tooltip("Space between letters, in letter heights.")]
        [SerializeField, Min(0f)] float spacing = 0.04f;
        [Tooltip("Widest the text may get, in the prop's local units; 0 for no limit.")]
        [SerializeField, Min(0f)] float maxWidth = 3f;

        public string Text
        {
            get => text;
            set
            {
                if (text == value) return;
                text = value;
                Rebuild();
            }
        }

        void OnEnable() => Rebuild();

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            // Creating objects is not allowed during OnValidate.
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#endif
        }

        GameObject GlyphOf(char c)
        {
            c = char.ToUpperInvariant(c);
            if (c is >= 'A' and <= 'Z' && letters != null && letters.Length == 26) return letters[c - 'A'];
            if (c is >= '0' and <= '9' && digits != null && digits.Length == 10) return digits[c - '0'];
            return null;
        }

        public void Rebuild()
        {
            var parent = transform.childCount > 0 ? transform.GetChild(0) : transform;
            var root = DigitText.Build(parent, TextRootName, text, GlyphOf, letterHeight, spacing, maxWidth);
            if (root == null) return;
            root.localPosition = center;
            root.localRotation = Quaternion.Euler(rotation);
        }
    }
}
