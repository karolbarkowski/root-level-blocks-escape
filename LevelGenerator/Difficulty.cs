using System.Diagnostics;

namespace BlockPuzzle;

public sealed record DifficultyResult(int Score, Dictionary<string, object> Features);

// Heuristic 1-1000 difficulty for ordering levels. It does not need a proven shortest
// solution, which large boards rarely get. The weights are initial guesses meant to be
// recalibrated against player data; every input is stored so levels can be re-rated.
public static class Difficulty
{
    public const int MinScore = 1, MaxScore = 1000;
    private const int Playouts = 24;
    private const double PlayerSeconds = 5;
    // Few sampled drops, noise and a tight move cap keep the player weak enough to fail sometimes.
    private const int SampledMoves = 3;

    public static DifficultyResult Rate(Level level)
    {
        var engine = new Engine(level);
        var opening = Search.Opening(engine, engine.Initial(), 12, 50000, DateTime.UtcNow.AddSeconds(2));
        var two = Search.Opening(engine, engine.Initial(), 12, 50000, DateTime.UtcNow.AddSeconds(2), 2);
        return Rate(engine, opening, two);
    }

    public static DifficultyResult Rate(Engine engine, DepthResult opening, DepthResult two)
    {
        var pieces = engine.Pieces;
        int layers = pieces.Sum(p => p.Types.Length);
        int frozen = pieces.Count(p => p.FrozenFor > 0), extraLayers = pieces.Sum(p => Math.Max(0, p.Types.Length - 1));
        int directional = pieces.Count(p => p.Axis != Axis.Free), neutral = pieces.Count(p => p.Types.Length == 0);
        var player = Simulate(engine, StableSeed(engine.Level));

        // Each term is normalized to 0..1; the weights sum to 1.
        double openingTerm = .7 * Clamp((opening.LowerBound - 2) / 10.0) + .3 * Clamp((two.LowerBound - 2) / 14.0);
        double sizeTerm = Clamp((layers - 4) / 26.0);
        // The generator freezes most late-used blocks, so count variety and share, not totals.
        int mechanics = new[] { frozen, extraLayers, directional, neutral }.Count(n => n > 0);
        int specialBlocks = pieces.Count(p => p.FrozenFor > 0 || p.Types.Length != 1 || p.Axis != Axis.Free);
        double specialTerm = .5 * mechanics / 4.0 + .5 * specialBlocks / (double)pieces.Length;
        double effortTerm = player.MeanSteps is double steps ? Clamp((steps / Math.Max(1, layers) - 1) / 2) : 1;
        double raw = .30 * (1 - player.SuccessRate) + .15 * (1 - player.Progress) + .20 * openingTerm
            + .15 * sizeTerm + .10 * specialTerm + .10 * effortTerm;
        int score = Math.Clamp(MinScore + (int)Math.Round((MaxScore - MinScore) * raw), MinScore, MaxScore);

        return new(score, new()
        {
            ["openingLowerBound"] = opening.LowerBound, ["firstTwoFullRemovalsLowerBound"] = two.LowerBound,
            ["blocks"] = pieces.Length, ["typeLayers"] = layers, ["floorCells"] = engine.Geometry.FloorCount,
            ["frozenBlocks"] = frozen, ["extraLayers"] = extraLayers, ["directionalBlocks"] = directional, ["neutralBlocks"] = neutral,
            ["mechanicTypes"] = mechanics, ["specialBlocks"] = specialBlocks,
            ["playerPlayouts"] = player.Playouts, ["playerSuccessRate"] = Math.Round(player.SuccessRate, 3),
            ["playerProgress"] = Math.Round(player.Progress, 3),
            ["playerMeanSteps"] = player.MeanSteps is double mean ? Math.Round(mean, 1) : (object)"none",
            ["model"] = "heuristic-v2"
        });
    }

    private sealed record PlayerResult(int Playouts, double SuccessRate, double Progress, double? MeanSteps);

    // A deliberately weak player: usually takes an available exit, otherwise samples a few
    // drops and prefers ones that open exits, with noise and a dislike of repeated states.
    private static PlayerResult Simulate(Engine engine, int seed)
    {
        var watch = Stopwatch.StartNew();
        var initial = engine.Initial();
        int layers = engine.Pieces.Sum(p => p.Types.Length), cap = 10 + 3 * layers;
        int played = 0, solved = 0;
        double progress = 0, steps = 0;
        for (; played < Playouts && (played < 4 || watch.Elapsed.TotalSeconds < PlayerSeconds); played++)
        {
            var random = new Random(seed + played);
            var state = initial;
            var visited = new HashSet<string> { state.Key() };
            int step = 0;
            for (; step < cap && !engine.Solved(state); step++)
            {
                var moves = engine.Successors(state).ToList();
                if (moves.Count == 0) break;
                var peels = moves.Where(m => m.State.Events > state.Events).ToList();
                if (peels.Count > 0 && random.NextDouble() < .9) state = peels[random.Next(peels.Count)].State;
                else state = moves.OrderBy(_ => random.Next()).Take(SampledMoves)
                    .OrderByDescending(m => Opportunities(engine, m.State) + random.NextDouble() * 2
                        - (visited.Contains(m.State.Key()) ? 1.5 : 0)).First().State;
                visited.Add(state.Key());
            }
            progress += layers == 0 ? 1 : (double)state.Events / layers;
            if (engine.Solved(state)) { solved++; steps += step; }
        }
        return new(played, (double)solved / played, progress / played, solved > 0 ? steps / solved : null);
    }

    private static int Opportunities(Engine engine, State state)
    {
        int count = 0;
        for (int i = 0; i < engine.Pieces.Length; i++)
        {
            var types = engine.Pieces[i].Types;
            if (state.Positions[i] < 0 || types.Length == 0) continue;
            if (engine.Reachable(state, i).Any(pos => engine.Exit(state, i, pos, types[state.Layers[i]]) != null)) count++;
        }
        return count;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    // FNV-1a over the board and blocks, so a level always gets the same playouts.
    private static int StableSeed(Level level)
    {
        uint hash = 2166136261;
        foreach (char c in System.Text.Json.JsonSerializer.Serialize(new { level.Board, level.Blocks }, Files.Json))
            hash = (hash ^ c) * 16777619;
        return (int)(hash & 0x3FFFFFFF);
    }
}
