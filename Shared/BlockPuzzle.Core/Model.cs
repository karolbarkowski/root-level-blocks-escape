using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlockPuzzle;

public readonly record struct Cell(int X, int Y);
public enum Axis { Free, Horizontal, Vertical }
public enum Side { Top, Right, Bottom, Left }
public enum BoundaryMode { Rectangle, Enclosed }
public sealed record Gate(string Id, Side Side, int Start, int Length, string Color,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Line = null);
public sealed record Piece(string Id, Cell[] Shape, Cell Position, string[] Colors, Axis Axis = Axis.Free, int FrozenFor = 0);
public sealed record Layout(int Width, int Height, Cell[] Walls, Gate[] Gates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] BoundaryMode BoundaryMode = BoundaryMode.Rectangle);
public sealed record Move(string Block, Cell To);
public sealed record Level
{
    public string Format { get; init; } = "block-puzzle/v1";
    public string Rules { get; init; } = "grid-drag-drop/v1";
    public int Seed { get; init; }
    // [JsonRequired] instead of `required`: Unity's C# 9 compiler cannot construct types with required members.
    [JsonRequired] public Layout Board { get; init; } = null!;
    [JsonRequired] public Piece[] Blocks { get; init; } = null!;
    public Move[] Solution { get; set; } = [];
    public Dictionary<string, object> Analysis { get; set; } = [];
}

public static class Files
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new ArgumentException("Empty JSON document.");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }
}

// Layer is the number of colors already peeled, including the final removal.
// A removed colored piece has Position=-1 and Layer=Colors.Length.
public sealed class State(int[] positions, int[] layers)
{
    public int[] Positions { get; } = positions;
    public int[] Layers { get; } = layers;
    public int Events => Layers.Sum();
    public State Copy() => new((int[])Positions.Clone(), (int[])Layers.Clone());
    public string Key()
    {
        var data = new char[Positions.Length * 2];
        for (int i = 0; i < Positions.Length; i++)
        {
            data[i * 2] = (char)(Positions[i] + 1);
            data[i * 2 + 1] = (char)Layers[i];
        }
        return new string(data);
    }
}

public sealed class Engine
{
    public Level Level { get; }
    public Layout Board => Level.Board;
    public Piece[] Pieces => Level.Blocks;
    private readonly bool[] walls;
    public Geometry Geometry { get; }
    public Engine(Level level)
    {
        Level = level;
        if(level.Board is null || level.Blocks is null || level.Board.Walls is null || level.Board.Gates is null ||
            level.Board.Gates.Any(g=>g is null) || level.Blocks.Any(p=>p is null || p.Shape is null || p.Colors is null))
            throw new ArgumentException("Board, blocks, shapes, colors, walls and gates must not be null.");
        if (level.Format != "block-puzzle/v1" || level.Rules != "grid-drag-drop/v1")
            throw new ArgumentException("Unsupported format or rules version.");
        if (Board.Width is < 2 or > 20 || Board.Height is < 2 or > 20)
            throw new ArgumentException("Board dimensions must be between 2 and 20.");
        walls = new bool[Board.Width * Board.Height];
        foreach (var cell in Board.Walls)
        {
            if (!Inside(cell)) throw new ArgumentException("Wall outside board.");
            if (walls[Encode(cell)]) throw new ArgumentException("Duplicate wall.");
            walls[Encode(cell)] = true;
        }
        Geometry = new Geometry(Board);
        if (Pieces.Length == 0 || Pieces.Select(p => p.Id).Distinct().Count() != Pieces.Length)
            throw new ArgumentException("Blocks must have unique IDs and cannot be empty.");
        int events = Pieces.Sum(p => p.Colors.Length);
        foreach (var p in Pieces)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Shape.Length == 0 || p.Shape.Distinct().Count() != p.Shape.Length ||
                p.Shape.Any(c => c.X < 0 || c.Y < 0 || c.X >= Board.Width || c.Y >= Board.Height) ||
                p.Shape.Min(c => c.X) != 0 || p.Shape.Min(c => c.Y) != 0)
                throw new ArgumentException("Shape must be unique, normalized, nonnegative cells.");
            var connected = new HashSet<Cell> { p.Shape[0] };
            bool changed;
            do
            {
                changed = false;
                foreach (var c in p.Shape)
                    if (!connected.Contains(c) && connected.Any(n => Math.Abs(n.X-c.X)+Math.Abs(n.Y-c.Y)==1))
                        changed |= connected.Add(c);
            } while (changed);
            if (connected.Count != p.Shape.Length) throw new ArgumentException("Disconnected shape.");
            if (p.FrozenFor < 0 || p.FrozenFor > events || p.Colors.Length > 32 ||
                p.Colors.Any(c => string.IsNullOrWhiteSpace(c) || !Board.Gates.Any(g => g.Color == c)))
                throw new ArgumentException("Invalid colors or freeze threshold.");
        }
    }
    public int Encode(Cell c) => c.Y * Board.Width + c.X;
    public Cell Decode(int p) => new(p % Board.Width, p / Board.Width);
    public bool Inside(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Board.Width && c.Y < Board.Height;
    public State Initial()
    {
        if (Pieces.Any(p => !Inside(p.Position))) throw new ArgumentException("Block anchor outside board.");
        var s = new State(Pieces.Select(p => Encode(p.Position)).ToArray(), new int[Pieces.Length]);
        var used = (bool[])walls.Clone();
        for (int i = 0; i < Pieces.Length; i++)
        {
            if (!Fits(i, s.Positions[i], used)) throw new ArgumentException($"Block {Pieces[i].Id} overlaps or lies outside the board.");
            Mark(used, i, s.Positions[i]);
        }
        return s;
    }
    public bool Solved(State s) => Pieces.Select((p, i) => p.Colors.Length == 0 || s.Positions[i] < 0).All(x => x);
    public int RemovedBlocks(State s) => s.Positions.Count(p => p < 0);
    public bool[] Occupied(State s, int except = -1)
    {
        var used = (bool[])walls.Clone();
        for (int i = 0; i < Pieces.Length; i++) if (i != except && s.Positions[i] >= 0) Mark(used, i, s.Positions[i]);
        return used;
    }
    private void Mark(bool[] used, int i, int pos)
    {
        var a = Decode(pos);
        foreach (var c in Pieces[i].Shape) used[(a.Y + c.Y)*Board.Width + a.X + c.X] = true;
    }
    public bool Fits(int i, int pos, bool[] used)
    {
        if (pos < 0 || pos >= walls.Length) return false;
        var a = Decode(pos);
        foreach (var c in Pieces[i].Shape)
        {
            var t = new Cell(a.X+c.X, a.Y+c.Y);
            if (!Inside(t) || !Geometry.Floor[Encode(t)] || used[Encode(t)]) return false;
        }
        return true;
    }
    public List<int> Reachable(State s, int i, bool ignoreFreeze = false)
    {
        if (s.Positions[i] < 0 || (!ignoreFreeze && s.Events < Pieces[i].FrozenFor)) return [];
        var used = Occupied(s, i);
        var seen = new bool[walls.Length];
        var result = new List<int> { s.Positions[i] };
        seen[s.Positions[i]] = true;
        for (int head = 0; head < result.Count; head++)
        {
            var a = Decode(result[head]);
            foreach (var delta in Directions(Pieces[i].Axis))
            {
                var c = new Cell(a.X + delta.X, a.Y + delta.Y);
                if (!Inside(c)) continue;
                int pos = Encode(c);
                if (!seen[pos] && Fits(i, pos, used)) { seen[pos] = true; result.Add(pos); }
            }
        }
        return result;
    }
    public static Cell[] Directions(Axis axis) => axis switch
    {
        Axis.Horizontal => [new(-1,0),new(1,0)], Axis.Vertical => [new(0,-1),new(0,1)],
        _ => [new(-1,0),new(1,0),new(0,-1),new(0,1)]
    };
    // Whole silhouette must translate straight out without hitting walls or blocks.
    // The complete transverse span must be contained in one matching gate.
    public Gate? Exit(State s, int i, int pos, string color)
    {
        var a = Decode(pos);
        int width = Pieces[i].Shape.Max(c => c.X)+1, height = Pieces[i].Shape.Max(c => c.Y)+1;
        var used = Occupied(s, i);
        foreach (var g in Board.Gates.Where(g => g.Color == color))
        {
            // Axis restrictions also apply to the outward motion.
            if (Pieces[i].Axis == Axis.Horizontal && g.Side is Side.Top or Side.Bottom) continue;
            if (Pieces[i].Axis == Axis.Vertical && g.Side is Side.Left or Side.Right) continue;
            bool aligned = g.Side switch
            {
                Side.Top => a.Y == (g.Line + 1 ?? 0) && a.X >= g.Start && a.X + width <= g.Start+g.Length,
                Side.Bottom => a.Y+height == (g.Line ?? Board.Height) && a.X >= g.Start && a.X+width <= g.Start+g.Length,
                Side.Left => a.X == (g.Line + 1 ?? 0) && a.Y >= g.Start && a.Y+height <= g.Start+g.Length,
                _ => a.X+width == (g.Line ?? Board.Width) && a.Y >= g.Start && a.Y+height <= g.Start+g.Length
            };
            if (!aligned) continue;
            Cell d = g.Side switch { Side.Top => new(0,-1), Side.Bottom => new(0,1), Side.Left => new(-1,0), _ => new(1,0) };
            bool clear = true;
            for (int step = 1; step <= Math.Max(width,height) + (g.Line.HasValue ? 1 : 0) && clear; step++)
                foreach (var c in Pieces[i].Shape)
                {
                    var t = new Cell(a.X+c.X+d.X*step,a.Y+c.Y+d.Y*step);
                    if (Inside(t) && (used[Encode(t)] ||
                        (g.Line.HasValue && Board.Gates.Any(other => other != g && Geometry.GateCells(Board, other).Contains(t))))) { clear = false; break; }
                }
            if (clear) return g;
        }
        return null;
    }
    public State Drop(State s, int i, int pos)
    {
        var next = s.Copy(); next.Positions[i] = pos;
        var colors = Pieces[i].Colors;
        if (colors.Length > 0 && Exit(s,i,pos,colors[s.Layers[i]]) != null)
        {
            next.Layers[i]++;
            if (next.Layers[i] == colors.Length) next.Positions[i] = -1;
        }
        return next;
    }
    public IEnumerable<(Move Move, State State)> Successors(State s)
    {
        for (int i = 0; i < Pieces.Length; i++)
            foreach (int pos in Reachable(s,i))
            {
                var next = Drop(s,i,pos);
                if (pos == s.Positions[i] && next.Layers[i] == s.Layers[i]) continue;
                yield return (new Move(Pieces[i].Id,Decode(pos)),next);
            }
    }
    public State Apply(State s, Move move)
    {
        int i = Array.FindIndex(Pieces,p => p.Id == move.Block);
        if (i < 0 || !Inside(move.To) || !Reachable(s,i).Contains(Encode(move.To)))
            throw new ArgumentException($"Illegal move: {move.Block} -> {move.To}.");
        return Drop(s,i,Encode(move.To));
    }
    public List<State> Replay(IEnumerable<Move> moves, bool requireSolved = true)
    {
        var frames = new List<State> { Initial() };
        foreach (var move in moves) frames.Add(Apply(frames[^1],move));
        if (requireSolved && !Solved(frames[^1])) throw new ArgumentException("Solution does not clear all colored blocks.");
        return frames;
    }
}

