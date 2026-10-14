using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape.Levels
{
    public enum DropResult
    {
        Moved,   // no matching gate: the block just stays where it was dropped
        Peeled,  // a matching gate removed the outer type; inner types remain
        Removed, // the last type left through a gate; the block's cells are free again
    }

    // Live game state of one level: block positions, peeled layers and the global removal count.
    // A port of the generator's rules engine (Shared/BlockPuzzle.Core: Engine.Fits, Reachable, Exit, Drop);
    // "Tools > Blocks Escape > Verify Levels With Game Rules" replays every stored solution through it to keep the two in step.
    // Cells use level coordinates: x right, y down.
    public sealed class BoardRules
    {
        sealed class Piece
        {
            public BlockData data;
            public Vector2Int[] shape;
            public Vector2Int size; // shape bounds
            public Vector2Int anchor;
            public int layer;       // types already peeled
            public bool removed;
        }

        readonly LevelData _level;
        readonly int _width, _height;
        readonly bool[] _floor, _wall;
        readonly Piece[] _pieces;
        readonly int[] _owner; // block index on each cell, -1 when free

        public int Width => _width;
        public int Height => _height;
        public int BlockCount => _pieces.Length;
        public int Events { get; private set; } // layers removed anywhere so far; thaws frozen blocks
        public bool Solved
        {
            get
            {
                foreach (var p in _pieces)
                    if (!p.removed && Types(p).Length > 0) return false;
                return true;
            }
        }

        public BoardRules(LevelData level)
        {
            _level = level;
            _width = level.board.width;
            _height = level.board.height;
            _floor = ComputeFloor(level);
            _wall = new bool[_width * _height];
            foreach (var w in level.board.walls ?? Array.Empty<CellData>()) _wall[w.y * _width + w.x] = true;

            var blocks = level.blocks ?? Array.Empty<BlockData>();
            _pieces = new Piece[blocks.Length];
            _owner = new int[_width * _height];
            for (int i = 0; i < _owner.Length; i++) _owner[i] = -1;
            for (int i = 0; i < blocks.Length; i++)
            {
                var shape = new Vector2Int[blocks[i].shape.Length];
                var size = Vector2Int.zero;
                for (int c = 0; c < shape.Length; c++)
                {
                    shape[c] = new Vector2Int(blocks[i].shape[c].x, blocks[i].shape[c].y);
                    size = Vector2Int.Max(size, shape[c] + Vector2Int.one);
                }
                _pieces[i] = new Piece { data = blocks[i], shape = shape, size = size, anchor = new Vector2Int(blocks[i].position.x, blocks[i].position.y) };
                if (!Fits(i, _pieces[i].anchor)) throw new ArgumentException($"Block {blocks[i].id} overlaps or lies outside the floor.");
                Mark(i, true);
            }
        }

        public BlockData Data(int i) => _pieces[i].data;
        public Vector2Int Anchor(int i) => _pieces[i].anchor;
        public bool IsRemoved(int i) => _pieces[i].removed;
        public string[] RemainingTypes(int i)
        {
            var types = Types(_pieces[i]);
            var rest = new string[types.Length - _pieces[i].layer];
            Array.Copy(types, _pieces[i].layer, rest, 0, rest.Length);
            return rest;
        }
        public string Axis(int i) => string.IsNullOrEmpty(_pieces[i].data.axis) ? "free" : _pieces[i].data.axis;
        public bool IsFrozen(int i) => Events < _pieces[i].data.frozenFor;
        public bool CanMove(int i) => !_pieces[i].removed && !IsFrozen(i);
        // Layers that still have to leave the board (anywhere) before block i thaws; 0 when it can move.
        public int TurnsToWake(int i) => Mathf.Max(0, _pieces[i].data.frozenFor - Events);

        public int BlockAt(Vector2Int cell) => Inside(cell) ? _owner[Index(cell)] : -1;

        // Whether a cell is closed to block i: outside the canvas, not floor (walls, gates, exterior), or another block.
        public bool IsBlocked(Vector2Int cell, int i)
        {
            if (!Inside(cell) || !_floor[Index(cell)]) return true;
            int owner = _owner[Index(cell)];
            return owner >= 0 && owner != i;
        }

        public bool Fits(int i, Vector2Int anchor)
        {
            foreach (var c in _pieces[i].shape)
                if (IsBlocked(anchor + c, i)) return false;
            return true;
        }

        // Anchors block i can reach in one drag, turning corners through free space (Engine.Reachable).
        public List<Vector2Int> Reachable(int i)
        {
            var result = new List<Vector2Int>();
            if (!CanMove(i)) return result;
            var seen = new HashSet<Vector2Int> { _pieces[i].anchor };
            result.Add(_pieces[i].anchor);
            for (int head = 0; head < result.Count; head++)
                foreach (var d in Directions(Axis(i)))
                {
                    var next = result[head] + d;
                    if (seen.Add(next) && Fits(i, next)) result.Add(next);
                }
            return result;
        }

        // Drops block i at anchor. A matching gate peels one type, or removes the block with its last type.
        public DropResult Drop(int i, Vector2Int anchor)
        {
            if (!CanMove(i) || !Fits(i, anchor)) throw new InvalidOperationException($"Block {_pieces[i].data.id} cannot be dropped at {anchor}.");
            var p = _pieces[i];
            Mark(i, false);
            p.anchor = anchor;
            Mark(i, true);

            var types = Types(p);
            if (types.Length == 0 || Exit(i, types[p.layer]) == null) return DropResult.Moved;
            p.layer++;
            Events++;
            if (p.layer < types.Length) return DropResult.Peeled;
            Mark(i, false);
            p.removed = true;
            return DropResult.Removed;
        }

        // The gate of the given type that block i can leave through from its current anchor (Engine.Exit):
        // the block sits right inside the gate, its whole span fits the gate, and the outward sweep is clear.
        public GateData Exit(int i, string type)
        {
            var p = _pieces[i];
            bool enclosed = _level.IsEnclosed;
            string axis = Axis(i);
            foreach (var g in _level.board.gates ?? Array.Empty<GateData>())
            {
                if (g.type != type) continue;
                bool vertical = g.side == "top" || g.side == "bottom";
                if (axis == "horizontal" && vertical) continue;
                if (axis == "vertical" && !vertical) continue;

                var a = p.anchor;
                bool aligned = g.side switch
                {
                    "top" => a.y == (enclosed ? g.line + 1 : 0) && a.x >= g.start && a.x + p.size.x <= g.start + g.length,
                    "bottom" => a.y + p.size.y == (enclosed ? g.line : _height) && a.x >= g.start && a.x + p.size.x <= g.start + g.length,
                    "left" => a.x == (enclosed ? g.line + 1 : 0) && a.y >= g.start && a.y + p.size.y <= g.start + g.length,
                    _ => a.x + p.size.x == (enclosed ? g.line : _width) && a.y >= g.start && a.y + p.size.y <= g.start + g.length,
                };
                if (!aligned) continue;

                var d = Outward(g.side);
                bool clear = true;
                int steps = Mathf.Max(p.size.x, p.size.y) + (enclosed ? 1 : 0);
                for (int step = 1; step <= steps && clear; step++)
                    foreach (var c in p.shape)
                    {
                        var t = a + c + d * step;
                        if (!Inside(t)) continue;
                        int owner = _owner[Index(t)];
                        if (_wall[Index(t)] || (owner >= 0 && owner != i) || (enclosed && IsOtherGateCell(t, g))) { clear = false; break; }
                    }
                if (clear) return g;
            }
            return null;
        }

        bool IsOtherGateCell(Vector2Int cell, GateData gate)
        {
            foreach (var other in _level.board.gates)
                if (other != gate)
                    foreach (var c in _level.GateCells(other))
                        if (c == cell) return true;
            return false;
        }

        // Playable floor: in enclosed levels, empty cells not reachable from the canvas edge
        // (walls and gate cells are barriers); in rectangular levels, every non-wall cell.
        public static bool[] ComputeFloor(LevelData level)
        {
            int w = level.board.width, h = level.board.height;
            var barrier = new bool[w * h];
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
            foreach (var c in level.board.walls ?? Array.Empty<CellData>())
                if (Inside(c.x, c.y)) barrier[c.y * w + c.x] = true;

            var floor = new bool[w * h];
            if (!level.IsEnclosed)
            {
                for (int i = 0; i < floor.Length; i++) floor[i] = !barrier[i];
                return floor;
            }

            foreach (var g in level.board.gates ?? Array.Empty<GateData>())
            foreach (var c in level.GateCells(g))
                if (Inside(c.x, c.y)) barrier[c.y * w + c.x] = true;

            var exterior = new bool[w * h];
            var queue = new Queue<Vector2Int>();
            void Visit(int x, int y)
            {
                if (!Inside(x, y) || barrier[y * w + x] || exterior[y * w + x]) return;
                exterior[y * w + x] = true;
                queue.Enqueue(new Vector2Int(x, y));
            }
            for (int x = 0; x < w; x++) { Visit(x, 0); Visit(x, h - 1); }
            for (int y = 0; y < h; y++) { Visit(0, y); Visit(w - 1, y); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                Visit(c.x + 1, c.y); Visit(c.x - 1, c.y); Visit(c.x, c.y + 1); Visit(c.x, c.y - 1);
            }
            for (int i = 0; i < floor.Length; i++) floor[i] = !barrier[i] && !exterior[i];
            return floor;
        }

        public static Vector2Int Outward(string side) => side switch
        {
            "top" => new Vector2Int(0, -1),
            "bottom" => new Vector2Int(0, 1),
            "left" => new Vector2Int(-1, 0),
            _ => new Vector2Int(1, 0),
        };

        static readonly Vector2Int[] FreeDirections = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
        static readonly Vector2Int[] HorizontalDirections = { Vector2Int.left, Vector2Int.right };
        static readonly Vector2Int[] VerticalDirections = { Vector2Int.down, Vector2Int.up };

        static Vector2Int[] Directions(string axis) =>
            axis == "horizontal" ? HorizontalDirections : axis == "vertical" ? VerticalDirections : FreeDirections;

        static string[] Types(Piece p) => p.data.types ?? Array.Empty<string>();

        void Mark(int i, bool occupied)
        {
            foreach (var c in _pieces[i].shape)
                _owner[Index(_pieces[i].anchor + c)] = occupied ? i : -1;
        }

        bool Inside(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < _width && c.y < _height;
        int Index(Vector2Int c) => c.y * _width + c.x;
    }
}
