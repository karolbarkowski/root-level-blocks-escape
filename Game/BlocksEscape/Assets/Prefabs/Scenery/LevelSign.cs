using TMPro;
using UnityEngine;

namespace BlocksEscape
{
    // The wooden level sign: shows the level number in the game font (WorldText), centred on the sign's panel. With no number set, it shows the leading number of the board's level file name
    // (0173-cross_8x8-001 -> 173) until a level list provides real numbers. A caption (the level picker's "PICK LEVEL"
    // or section name) shows instead of the number while set.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LevelSign : MonoBehaviour
    {
        const string NumberRootName = "Number";

        [SerializeField] Board board;
        [SerializeField] TMP_FontAsset font;
        [Tooltip("Centre of the number on the sign, in the sign's local space.")]
        [SerializeField] Vector3 numberCenter = new Vector3(0f, 1f, -0.16f);
        [SerializeField, Min(0.01f)] float digitHeight = 0.6f;
        [Tooltip("Widest the number may get on the panel, in the sign's local units.")]
        [SerializeField, Min(0.1f)] float maxWidth = 3.4f;
        [Tooltip("Shown instead of the level file's number when above zero.")]
        [SerializeField, Min(0)] int number;

        Board _subscribed;
        string _caption;

        public int Number
        {
            get => number > 0 ? number : NumberFromLevel();
            set { number = Mathf.Max(0, value); Rebuild(); }
        }

        void OnEnable()
        {
            if (board == null) board = FindAnyObjectByType<Board>();
            Subscribe();
            Rebuild();
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.Initialized -= Rebuild;
            _subscribed = null;
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Subscribe();
#if UNITY_EDITOR
            // Creating objects is not allowed during OnValidate.
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#endif
        }

        void Subscribe()
        {
            if (_subscribed == board) return;
            if (_subscribed != null) _subscribed.Initialized -= Rebuild;
            _subscribed = board;
            if (_subscribed != null) _subscribed.Initialized += Rebuild;
        }

        // Text shown instead of the number; null or empty shows the number again. Not saved.
        public string Caption
        {
            get => _caption;
            set { _caption = value; Rebuild(); }
        }

        int NumberFromLevel()
        {
            if (board == null || board.Level == null) return 0;
            int n = 0;
            foreach (char c in board.Level.name)
            {
                if (!char.IsDigit(c)) break;
                n = n * 10 + (c - '0');
            }
            return n;
        }

        public void Rebuild()
        {
            string shown = !string.IsNullOrEmpty(_caption) ? _caption : Number > 0 ? Number.ToString() : null;
            var text = WorldText.Show(transform, NumberRootName, shown, font, digitHeight, maxWidth);
            if (text != null) text.transform.localPosition = numberCenter;
        }
    }
}
