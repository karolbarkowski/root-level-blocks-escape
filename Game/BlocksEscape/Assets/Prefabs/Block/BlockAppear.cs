using UnityEngine;

namespace BlocksEscape
{
    // Pops a block up out of the board when a level starts, or shrinks it into the board when a level is left, scaling it
    // about its bottom so it stays on the board. Sweep does every block of the board in a wave from the top left corner to
    // the bottom right one. Play mode only; removes itself when done (a hidden block is left at a tiny scale).
    [DisallowMultipleComponent]
    public sealed class BlockAppear : MonoBehaviour
    {
        bool _show;
        float _delay, _duration, _time, _pivotHeight, _bottom;
        Vector3 _scale;

        // Starts every block of the board after a delay growing with its distance from the top left corner, so the wave
        // takes spread seconds to cross the board whatever its size. Returns the seconds until the last block is done.
        public static float Sweep(Board board, bool show, float spread, float duration)
        {
            if (board.Rules == null) return 0f;
            float diagonal = Mathf.Max(1f, board.XTiles + board.YTiles - 2);
            bool any = false;
            for (int i = 0; i < board.Rules.BlockCount; i++)
            {
                var block = board.BlockAt(i);
                if (block == null) continue;
                // Level coordinates: x grows to the right, y down the board, so x + y grows from the top left corner.
                var cell = board.LocalToLevel(block.transform.localPosition);
                Play(block, show, Mathf.Clamp01((cell.x + cell.y) / diagonal) * spread, duration);
                any = true;
            }
            return any ? spread + duration : 0f;
        }

        public static void Play(Block block, bool show, float delay, float duration)
        {
            var old = block.GetComponent<BlockAppear>();
            if (old != null) DestroyImmediate(old);
            var appear = block.gameObject.AddComponent<BlockAppear>();
            appear._show = show;
            appear._delay = delay;
            appear._duration = duration;
            appear._scale = block.transform.localScale;
            appear._pivotHeight = block.PivotHeight;
            appear._bottom = block.transform.localPosition.y - block.PivotHeight;
            appear.Apply(show ? 0f : 1f);
        }

        void Update()
        {
            _time += Time.deltaTime;
            float t = Mathf.Clamp01((_time - _delay) / _duration);
            Apply(_show ? Ease.OutBack(t) : 1f - Ease.InBack(t));
            if (t >= 1f) Destroy(this);
        }

        void Apply(float size)
        {
            size = Mathf.Max(size, 1e-3f);
            var t = transform;
            t.localScale = _scale * size;
            var p = t.localPosition;
            p.y = _bottom + _pivotHeight * size;
            t.localPosition = p;
        }
    }
}
