using TMPro;
using UnityEngine;

namespace BlocksEscape
{
    // Models and sizes for the state markers Block.ShowStatus puts on blocks: the sleep bubble over a frozen animal's
    // head (with its turns-to-wake number) and the arrow on an axis-locked block.
    [CreateAssetMenu(menuName = "Blocks Escape/Block Status Art")]
    public sealed class BlockStatusArt : ScriptableObject
    {
        [SerializeField] GameObject sleepBubble;
        [SerializeField] GameObject axisArrow;
        [Tooltip("Font of the turns-to-wake number (Fonts/Fredoka SDF).")]
        [SerializeField] TMP_FontAsset font;
        [Tooltip("Bubble scale; the model is one cell across.")]
        [SerializeField, Min(0.1f)] float bubbleSize = 0.42f;
        [Tooltip("Height of the bubble's centre above the top of the block, in cells.")]
        [SerializeField] float bubbleHeight = 0.05f;
        [Tooltip("Shift from the face centre towards the forehead, in cells, so the bubble sits between the eyes.")]
        [SerializeField] float bubbleForward = 0.05f;
        [Tooltip("Further shift along the screen (right, up), in cells.")]
        [SerializeField] Vector2 bubbleScreenOffset = Vector2.zero;
        [Tooltip("Digit height (of the font's capitals) in the bubble's own units.")]
        [SerializeField, Min(0.05f)] float digitHeight = 0.75f;
        [Tooltip("Centre of the number in the bubble's own space; -Z is its front.")]
        [SerializeField] Vector3 digitOffset = new Vector3(0f, 0f, -0.1f);

        public GameObject SleepBubble => sleepBubble;
        public GameObject AxisArrow => axisArrow;
        public TMP_FontAsset Font => font;
        public float BubbleSize => bubbleSize;
        public float BubbleHeight => bubbleHeight;
        public float BubbleForward => bubbleForward;
        public Vector2 BubbleScreenOffset => bubbleScreenOffset;
        public float DigitHeight => digitHeight;
        public Vector3 DigitOffset => digitOffset;
    }
}
