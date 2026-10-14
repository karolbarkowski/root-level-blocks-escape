using UnityEngine;

namespace BlocksEscape
{
    // The wooden level sign: spells the level number with the 3D digit models (Prefabs/Scenery/Digit_0..9), centred on
    // the sign's panel. With no number set, it shows the leading number of the board's level file name
    // (0173-cross_8x8-001 -> 173) until a level list provides real numbers.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LevelSign : MonoBehaviour
    {
        const string NumberRootName = "Number";

        [SerializeField] Board board;
        [Tooltip("Digit models 0-9, in order.")]
        [SerializeField] GameObject[] digits = new GameObject[10];
        [Tooltip("Centre of the number on the sign, in the sign's local space.")]
        [SerializeField] Vector3 numberCenter = new Vector3(0f, 1f, -0.16f);
        [SerializeField, Min(0.01f)] float digitHeight = 0.6f;
        [Tooltip("Space between digits, in digit heights.")]
        [SerializeField, Min(0f)] float spacing = 0.06f;
        [Tooltip("Shown instead of the level file's number when above zero.")]
        [SerializeField, Min(0)] int number;

        Board _subscribed;

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
            var root = DigitText.Build(transform, NumberRootName, Number > 0 ? Number : -1, digits, digitHeight, spacing);
            if (root != null) root.localPosition = numberCenter;
        }
    }
}
