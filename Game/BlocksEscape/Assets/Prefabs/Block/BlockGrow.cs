using UnityEngine;

namespace BlocksEscape
{
    // After a peel, the remaining layers stand at the height they had under the layer that left; this grows the
    // respawned block from there to full height, resting on the board, with a little overshoot. Play mode only; removes
    // itself when done. If the block is grabbed meanwhile, only the height finishes and the dragger keeps the position.
    [DisallowMultipleComponent]
    public sealed class BlockGrow : MonoBehaviour
    {
        const float Duration = 0.3f;

        float _from, _delay, _time, _pivotHeight, _bottom, _writtenY;
        bool _moving = true;

        // from is the starting height as a fraction of the full one; delay waits for the outer layer to slide off.
        // The block must stand at its full-size resting pose.
        public static void Play(Block block, float from, float delay)
        {
            var grow = block.gameObject.AddComponent<BlockGrow>();
            grow._from = from;
            grow._delay = delay;
            grow._pivotHeight = block.PivotHeight;
            grow._bottom = block.transform.localPosition.y - grow._pivotHeight;
            grow.Apply(from);
        }

        void Update()
        {
            _time += Time.deltaTime;
            float t = Mathf.Clamp01((_time - _delay) / Duration);
            Apply(Mathf.LerpUnclamped(_from, 1f, EaseOutBack(t)));
            if (t >= 1f) Destroy(this);
        }

        void Apply(float height)
        {
            var t = transform;
            t.localScale = new Vector3(t.localScale.x, height, t.localScale.z);
            if (_moving && _time > 0f && !Mathf.Approximately(t.localPosition.y, _writtenY)) _moving = false;
            if (!_moving) return;
            // Keep the bottom on the board: the pivot sits PivotHeight above it at full height.
            var p = t.localPosition;
            p.y = _bottom + _pivotHeight * height;
            t.localPosition = p;
            _writtenY = p.y;
        }

        static float EaseOutBack(float t)
        {
            const float c = 1.4f;
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }
    }
}
