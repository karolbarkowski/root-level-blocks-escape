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
        [Tooltip("Digit models 0-9, in order.")]
        [SerializeField] GameObject[] digits = new GameObject[10];
        [Tooltip("Bubble scale; the model is one cell across.")]
        [SerializeField, Min(0.1f)] float bubbleSize = 0.42f;
        [Tooltip("Height of the bubble's centre above the top of the block, in cells.")]
        [SerializeField] float bubbleHeight = 0.3f;
        [Tooltip("Shift from above the face along the screen (right, up), in cells, so the face stays visible.")]
        [SerializeField] Vector2 bubbleScreenOffset = new Vector2(0.38f, 0.42f);
        [Tooltip("Digit height in the bubble's own units.")]
        [SerializeField, Min(0.05f)] float digitHeight = 0.5f;
        [Tooltip("Centre of the number in the bubble's own space; -Z is its front.")]
        [SerializeField] Vector3 digitOffset = new Vector3(0f, 0f, -0.1f);

        public GameObject SleepBubble => sleepBubble;
        public GameObject AxisArrow => axisArrow;
        public GameObject[] Digits => digits;
        public float BubbleSize => bubbleSize;
        public float BubbleHeight => bubbleHeight;
        public Vector2 BubbleScreenOffset => bubbleScreenOffset;
        public float DigitHeight => digitHeight;
        public Vector3 DigitOffset => digitOffset;
    }
}
