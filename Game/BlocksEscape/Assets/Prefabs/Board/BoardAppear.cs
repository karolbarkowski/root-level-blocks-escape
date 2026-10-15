using UnityEngine;

namespace BlocksEscape
{
    // Builds the pen up when a level starts and takes it down when it's left, so the board doesn't just appear and vanish
    // around the blocks (BlockAppear): the floor grows out of the meadow from the board's centre, and while it does the
    // fence posts and gate tiles pop up in a wave from the top left corner to the bottom right one. Leaving runs it the
    // other way round: the fence goes first, the floor shrinks away last. Each piece scales about its pivot (it stands on
    // the ground), the floor about the board's centre. Play mode only; removes itself when done (a hidden piece is left at
    // a tiny scale, for Board.Clear).
    [DisallowMultipleComponent]
    public sealed class BoardAppear : MonoBehaviour
    {
        bool _show;
        float _delay, _duration, _time;
        Vector3 _scale, _position, _pivot;

        // Starts after delay; the floor takes floorTime, the fence wave spread seconds to cross the board and each piece
        // duration. Returns the seconds until the last piece is done.
        public static float Sweep(Board board, bool show, float floorTime, float spread, float duration, float delay = 0f)
        {
            var content = board.Content;
            if (content == null) return 0f;
            float diagonal = Mathf.Max(1f, board.XTiles + board.YTiles - 2);
            float fence = delay + (show ? floorTime * 0.5f : 0f); // rises while the floor is still growing
            float end = 0f;
            void Piece(Transform piece)
            {
                // Level coordinates: x grows to the right, y down the board, so x + y grows from the top left corner.
                var cell = board.LocalToLevel(piece.localPosition);
                float start = fence + Mathf.Clamp01((cell.x + cell.y) / diagonal) * spread;
                Play(piece, show, start, duration, piece.localPosition);
                end = Mathf.Max(end, start + duration);
            }
            var walls = content.Find(Board.WallsName);
            if (walls != null) foreach (Transform wall in walls) Piece(wall);
            var gates = content.Find(Board.GatesName);
            if (gates != null)
                foreach (Transform gate in gates)
                foreach (Transform cell in gate)
                    Piece(cell);

            var floor = content.Find(Board.FloorName);
            if (floor != null)
            {
                var centre = board.LevelToLocal(new Vector2((board.XTiles - 1) * 0.5f, (board.YTiles - 1) * 0.5f));
                centre.y = floor.localPosition.y;
                float start = show ? delay : Mathf.Max(delay, end - duration * 0.5f); // leaving: as the last posts go
                Play(floor, show, start, floorTime, centre);
                end = Mathf.Max(end, start + floorTime);
            }
            return end;
        }

        static void Play(Transform piece, bool show, float delay, float duration, Vector3 pivot)
        {
            var old = piece.GetComponent<BoardAppear>();
            if (old != null) DestroyImmediate(old);
            var appear = piece.gameObject.AddComponent<BoardAppear>();
            appear._show = show;
            appear._delay = delay;
            appear._duration = duration;
            appear._scale = piece.localScale;
            appear._position = piece.localPosition;
            appear._pivot = pivot;
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
            transform.localScale = _scale * size;
            transform.localPosition = _pivot + (_position - _pivot) * size;
        }
    }
}
