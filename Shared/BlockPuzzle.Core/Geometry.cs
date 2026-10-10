namespace BlockPuzzle;

// Gate cells close the boundary during flood fill but are never playable floor.
public sealed class Geometry
{
    public bool[] Floor { get; }
    public int FloorCount => Floor.Count(value => value);
    public static IEnumerable<Cell> GateCells(Layout b, Gate g)
    {
        for (int n = g.Start; n < g.Start + g.Length; n++)
            yield return g.Side switch
            {
                Side.Top => new(n, g.Line ?? 0), Side.Bottom => new(n, g.Line ?? b.Height - 1),
                Side.Left => new(g.Line ?? 0, n), _ => new(g.Line ?? b.Width - 1, n)
            };
    }
    public Geometry(Layout b)
    {
        if (b.Width is < 2 or > 20 || b.Height is < 2 or > 20 || !Enum.IsDefined(typeof(BoundaryMode), b.BoundaryMode))
            throw new ArgumentException("Invalid canvas dimensions or boundary mode.");
        bool enclosed = b.BoundaryMode == BoundaryMode.Enclosed;
        bool Inside(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < b.Width && c.Y < b.Height;
        int Index(Cell c) => c.Y * b.Width + c.X;
        var barrier = new bool[b.Width * b.Height];
        foreach (var c in b.Walls)
        {
            if (!Inside(c) || barrier[Index(c)]) throw new ArgumentException("Invalid or duplicate wall.");
            barrier[Index(c)] = true;
        }
        var edges = new HashSet<(Side, int)>();
        var ids = new HashSet<string>();
        foreach (var g in b.Gates)
        {
            int size = g.Side is Side.Top or Side.Bottom ? b.Width : b.Height;
            int depth = g.Side is Side.Top or Side.Bottom ? b.Height : b.Width;
            if (!BlockTypes.IsKnown(g.Type)) throw new ArgumentException($"Gate {g.Id} has unknown block type '{g.Type}'.");
            if (!Enum.IsDefined(typeof(Side), g.Side) || string.IsNullOrWhiteSpace(g.Id) || !ids.Add(g.Id) ||
                g.Start < 0 || g.Length < 1 || g.Length > size || g.Start > size - g.Length ||
                (enclosed ? !g.Line.HasValue || g.Line < 0 || g.Line >= depth : g.Line.HasValue))
                throw new ArgumentException("Invalid gate: enclosed gates require an in-bounds line; rectangular gates must omit it.");
            foreach (var c in GateCells(b, g))
            {
                if (barrier[Index(c)] || (!enclosed && !edges.Add((g.Side, g.Side is Side.Top or Side.Bottom ? c.X : c.Y))))
                    throw new ArgumentException("Gate overlaps a wall or another gate.");
                if (enclosed) barrier[Index(c)] = true;
            }
        }
        var exterior = new bool[barrier.Length];
        if (enclosed)
        {
            var queue = new Queue<Cell>();
            void Visit(Cell c)
            {
                if (!Inside(c) || barrier[Index(c)] || exterior[Index(c)]) return;
                exterior[Index(c)] = true; queue.Enqueue(c);
            }
            for (int x = 0; x < b.Width; x++) { Visit(new(x, 0)); Visit(new(x, b.Height - 1)); }
            for (int y = 0; y < b.Height; y++) { Visit(new(0, y)); Visit(new(b.Width - 1, y)); }
            while (queue.TryDequeue(out var c)) foreach (var d in Engine.Directions(Axis.Free)) Visit(new(c.X + d.X, c.Y + d.Y));
        }
        Floor = barrier.Select((blocked, i) => !blocked && !exterior[i]).ToArray();
        if (!enclosed) return;
        if (FloorCount == 0) throw new ArgumentException("No enclosed floor. Close the level boundary with walls and gates.");
        foreach (var g in b.Gates)
        {
            Cell outward = g.Side switch { Side.Top => new(0, -1), Side.Bottom => new(0, 1), Side.Left => new(-1, 0), _ => new(1, 0) };
            foreach (var c in GateCells(b, g))
            {
                var inner = new Cell(c.X - outward.X, c.Y - outward.Y);
                var outer = new Cell(c.X + outward.X, c.Y + outward.Y);
                if (!Inside(inner) || !Floor[Index(inner)] || (Inside(outer) && !exterior[Index(outer)]))
                    throw new ArgumentException($"Gate {g.Id} must face enclosed floor inward and exterior space outward. Check its exit direction.");
            }
        }
    }
}
