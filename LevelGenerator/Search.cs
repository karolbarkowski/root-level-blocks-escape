using System.Diagnostics;

namespace BlockPuzzle;

public sealed record DepthResult(int? Exact, int LowerBound, string Status, int Visited);
public sealed record SolveResult(string Status, Move[] Moves, int Visited, bool Optimal);

public static class Search
{
    // BFS over pickup/drop actions, stopping at the first event or kth full removal.
    // A completed frontier proves that the target needs more moves than that frontier.
    public static DepthResult Opening(Engine engine, State initial, int maxDepth, int maxStates,
        DateTime deadline, int fullRemovals = 0)
    {
        var frontier = new List<State> { initial };
        var seen = new HashSet<string> { initial.Key() };
        int lower = 1;
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var next = new List<State>();
            foreach (var state in frontier)
            {
                if (DateTime.UtcNow >= deadline) return new(null,lower,"timeBudget",seen.Count);
                foreach (var edge in engine.Successors(state))
                {
                    bool target = fullRemovals == 0 ? edge.State.Events > initial.Events :
                        engine.RemovedBlocks(edge.State) - engine.RemovedBlocks(initial) >= fullRemovals;
                    if (target) return new(depth,depth,"exact",seen.Count);
                    if (!seen.Add(edge.State.Key())) continue;
                    if (seen.Count >= maxStates) return new(null,lower,"stateBudget",seen.Count);
                    next.Add(edge.State);
                }
            }
            lower = depth + 1;
            if (next.Count == 0) return new(null,lower,"unreachable",seen.Count);
            frontier = next;
        }
        return new(null,lower,"depthLimit",seen.Count);
    }

    // Remaining layers is admissible: at most one is cleared by each drop.
    // Unlike Manhattan distance, it respects arbitrarily long, turning drags.
    // A weight above 1 trades optimality for speed (weighted A*); only weight 1 proves a shortest solution.
    public static SolveResult Solve(Engine engine, int maxStates, DateTime deadline, double weight = 1)
    {
        var initial = engine.Initial();
        var start = initial.Key();
        int total = engine.Pieces.Sum(p => p.Colors.Length);
        var queue = new PriorityQueue<(State State,int G), (int F,int H,long Tie)>();
        long tie = 0;
        var costs = new Dictionary<string,int> { [start] = 0 };
        var parents = new Dictionary<string,(string Previous,Move Move)>();
        queue.Enqueue((initial,0),((int)Math.Ceiling(weight*total),total,tie++));
        while (queue.TryDequeue(out var node,out _))
        {
            if (DateTime.UtcNow >= deadline) return new("timeBudget",[],costs.Count,false);
            string key = node.State.Key();
            if (costs[key] != node.G) continue;
            if (engine.Solved(node.State))
            {
                var moves = new List<Move>();
                while (key != start) { var p = parents[key]; moves.Add(p.Move); key = p.Previous; }
                moves.Reverse();
                return new("solved",moves.ToArray(),costs.Count,weight<=1);
            }
            foreach (var edge in engine.Successors(node.State))
            {
                var nextKey = edge.State.Key();
                int g = node.G+1;
                if (costs.TryGetValue(nextKey,out int old) && old <= g) continue;
                if (costs.Count >= maxStates) return new("stateBudget",[],costs.Count,false);
                costs[nextKey] = g; parents[nextKey] = (key,edge.Move);
                int h = total-edge.State.Events;
                queue.Enqueue((edge.State,g),(g+(int)Math.Ceiling(weight*h),h,tie++));
            }
        }
        return new("unsolvable",[],costs.Count,false);
    }

    // Best solution within budget: exact A* first, then weighted A* if the witness is still long.
    // Weighted search expands far fewer nodes per depth, so it gets a larger state budget.
    public static (Move[] Moves, SolveResult Result) Improve(Engine engine, Move[] witness, int maxStates, double seconds)
    {
        var exact = Solve(engine, maxStates, DateTime.UtcNow.AddSeconds(seconds * .5));
        if (exact.Status == "solved") return (exact.Moves, exact);
        // Higher weights behave almost like greedy best-first search: fast, rarely shortest.
        foreach (double weight in new[] { 2.0, 20.0 })
        {
            var greedy = Solve(engine, maxStates * 4, DateTime.UtcNow.AddSeconds(seconds * .25), weight);
            if (greedy.Status == "solved" && greedy.Moves.Length < witness.Length) return (greedy.Moves, exact);
        }
        return (witness, exact);
    }

    public static Move[] Shorten(Engine engine, Move[] witness)
    {
        // Erase state cycles, then merge consecutive moves of the same block only
        // when replay confirms identical state (including layers and counters).
        var states = new List<State> { engine.Initial() };
        var moves = new List<Move>();
        var indices = new Dictionary<string,int> { [states[0].Key()] = 0 };
        foreach (var move in witness)
        {
            var next = engine.Apply(states[^1],move);
            string key = next.Key();
            if (indices.TryGetValue(key,out int index))
            {
                for (int i=states.Count-1;i>index;i--) indices.Remove(states[i].Key());
                states.RemoveRange(index+1,states.Count-index-1);
                moves.RemoveRange(index,moves.Count-index);
            }
            else { moves.Add(move); states.Add(next); indices[key]=states.Count-1; }
        }
        for (int i=moves.Count-2;i>=0;i--)
        {
            if (moves[i].Block != moves[i+1].Block) continue;
            try
            {
                var next = engine.Apply(states[i],moves[i+1]);
                if (next.Key() != states[i+2].Key()) continue;
                moves.RemoveAt(i); states.RemoveAt(i+1);
            }
            catch (ArgumentException) { /* An intervening peel/unlock may be essential. */ }
        }
        engine.Replay(moves);
        return moves.ToArray();
    }
}
