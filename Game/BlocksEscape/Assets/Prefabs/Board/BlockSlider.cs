using BlocksEscape.Levels;
using UnityEngine;

namespace BlocksEscape
{
    // Continuous movement of one held block across the grid, in level coordinates (x right, y down).
    //
    // Collision is plain grid geometry: obstacles (walls, gates, outside space, other blocks) are whole cells,
    // so the block slides up to them and along them. When it is blocked while slightly off the grid, it is eased
    // sideways into the gap ahead (corner assist), so it slips into a corridor exactly its width without
    // pixel-perfect aiming. Squares touching only at a corner still block, so nothing passes diagonally between them.
    public sealed class BlockSlider
    {
        const float MaxStep = 0.2f; // movement substep in cells; below half a cell, so nothing tunnels through a cell
        const float Epsilon = 0.001f;

        readonly BoardRules _rules;
        readonly int _block;
        readonly Vector2Int[] _shape;
        readonly float _cornerAssist;

        // cornerAssist: how far, in cells, a blocked block may be eased sideways to line up with a gap.
        public BlockSlider(BoardRules rules, int block, float cornerAssist)
        {
            _rules = rules;
            _block = block;
            _cornerAssist = cornerAssist;
            var cells = rules.Data(block).shape;
            _shape = new Vector2Int[cells.Length];
            for (int i = 0; i < cells.Length; i++) _shape[i] = new Vector2Int(cells[i].x, cells[i].y);
        }

        // Moves the anchor toward the target in small steps, one axis at a time, stopping at obstacles and sliding along them.
        public Vector2 Slide(Vector2 position, Vector2 target)
        {
            for (int iteration = 0; iteration < 256; iteration++)
            {
                var delta = target - position;
                if (delta.sqrMagnitude < Epsilon * Epsilon) break;
                var step = Vector2.ClampMagnitude(delta, MaxStep);
                // The dominant axis may be assisted; the other one only slides, and is skipped after an assist,
                // so pulling toward the pointer does not undo the easing into a gap.
                int primary = Mathf.Abs(step.x) >= Mathf.Abs(step.y) ? 0 : 1;
                bool moved = MoveAxis(ref position, primary, step, true, out bool assisted);
                if (!assisted) moved |= MoveAxis(ref position, 1 - primary, step, false, out _);
                if (!moved) break;
            }
            return position;
        }

        // The whole cell to drop at: the nearest one, which always fits because every cell the block overlaps is free.
        public Vector2Int Snap(Vector2 position)
        {
            var nearest = Vector2Int.RoundToInt(position);
            if (_rules.Fits(_block, nearest)) return nearest;
            // Defensive fallback: the best of the surrounding whole cells, or where the block started.
            var best = _rules.Anchor(_block);
            float bestDistance = float.MaxValue;
            for (int dx = 0; dx <= 1; dx++)
            for (int dy = 0; dy <= 1; dy++)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(position.x) + dx, Mathf.FloorToInt(position.y) + dy);
                float distance = (cell - position).sqrMagnitude;
                if (distance < bestDistance && _rules.Fits(_block, cell)) { best = cell; bestDistance = distance; }
            }
            return best;
        }

        // True when no cell of the block, placed at a fractional anchor, overlaps a blocked cell.
        public bool Free(Vector2 anchor)
        {
            foreach (var c in _shape)
            {
                float x = anchor.x + c.x, y = anchor.y + c.y;
                for (int cx = Mathf.CeilToInt(x - 1f + Epsilon); cx <= Mathf.FloorToInt(x + 1f - Epsilon); cx++)
                for (int cy = Mathf.CeilToInt(y - 1f + Epsilon); cy <= Mathf.FloorToInt(y + 1f - Epsilon); cy++)
                    if (_rules.IsBlocked(new Vector2Int(cx, cy), _block)) return false;
            }
            return true;
        }

        bool MoveAxis(ref Vector2 position, int axis, Vector2 step, bool allowAssist, out bool assisted)
        {
            assisted = false;
            float s = step[axis];
            if (Mathf.Abs(s) < Epsilon) return false;
            var next = position;
            next[axis] += s;
            if (Free(next)) { position = next; return true; }

            // Obstacles sit on whole cells, so the last free position before one is a whole number.
            var contact = position;
            contact[axis] = s > 0f ? Mathf.Floor(next[axis] + Epsilon) : Mathf.Ceil(next[axis] - Epsilon);
            if ((contact[axis] - position[axis]) * s > 0f && Free(contact)) { position = contact; return true; }

            return allowAssist && (assisted = Assist(ref position, axis, s));
        }

        // Blocked along one axis while off the grid on the other: if lining up with the nearer (or else the farther)
        // whole cell opens the way, ease toward it as fast as the drag moves, giving a smooth diagonal slide into the gap.
        bool Assist(ref Vector2 position, int axis, float s)
        {
            int other = 1 - axis;
            float value = position[other];
            float low = Mathf.Floor(value), high = Mathf.Ceil(value);
            if (high - low < Epsilon) return false; // already aligned: the obstacle is real

            bool lowFirst = value - low <= high - value;
            for (int k = 0; k < 2; k++)
            {
                float aligned = (k == 0) == lowFirst ? low : high;
                if (Mathf.Abs(aligned - value) > _cornerAssist) continue;
                var lined = position;
                lined[other] = aligned;
                var probe = lined;
                probe[axis] += Mathf.Sign(s) * 0.05f;
                if (!Free(lined) || !Free(probe)) continue;
                var eased = position;
                eased[other] = Mathf.MoveTowards(value, aligned, Mathf.Abs(s));
                if (!Free(eased)) continue;
                position = eased;
                return true;
            }
            return false;
        }
    }
}
