using TMPro;
using UnityEngine;

namespace BlocksEscape
{
    // A word on a prop, such as the menu planks, in the game font (WorldText). The text is placed at Center, turned by
    // Rotation (90 on X lays it on top of a flat prop, reading up the screen), and shrunk to Max Width if it is too long.
    // It hangs under the prop's model (its first child), so it squashes along with it when a WorldButton is pressed.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class WorldLabel : MonoBehaviour
    {
        const string TextName = "Label";

        [SerializeField] string text = "";
        [SerializeField] TMP_FontAsset font;
        [Tooltip("Centre of the text, in the prop's local space.")]
        [SerializeField] Vector3 center = new Vector3(0f, 0.31f, 0f);
        [SerializeField] Vector3 rotation = new Vector3(90f, 0f, 0f);
        [Tooltip("Height of a capital letter, in the prop's local units.")]
        [SerializeField, Min(0.01f)] float letterHeight = 0.42f;
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

        public void Rebuild()
        {
            var parent = transform.childCount > 0 ? transform.GetChild(0) : transform;
            var label = WorldText.Show(parent, TextName, text, font, letterHeight, maxWidth);
            if (label == null) return;
            label.transform.localPosition = center;
            label.transform.localRotation = Quaternion.Euler(rotation);
        }
    }
}
