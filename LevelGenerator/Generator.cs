using System.Diagnostics;

namespace BlockPuzzle;

public sealed record GenerateOptions(int Seed = 42, int Width = 6, int Height = 6,
    decimal Coverage = 80, decimal SingleCellRatio = .2m, int MaxCandidates = 1,
    int MinOpening = 3, bool Specials = true, Layout? FixedLayout = null, string Method = "anneal", decimal BlockSize = 2.8m);
public sealed record GenerationResult(List<Level> Levels, int Attempts, int Constructed, int Evaluated, double Seconds);
internal sealed record Candidate(Level Level, double Score, DepthResult Opening, double Density);

public sealed partial class Generator(GenerateOptions options)
{
    private readonly Random random = new(options.Seed);
    private readonly Stopwatch watch = new();
    private Action reportProgress = () => { };
    private const int StallLimit = 150;
    private static readonly string[] Palette = ["red","blue","yellow","green"];
    // Up to four cells; the catalog adds every rotation and mirror image (so J, Z and the like).
    private static readonly Cell[][] Shapes =
    [
        [new(0,0)], [new(0,0),new(1,0)], [new(0,0),new(1,0),new(2,0)],
        [new(0,0),new(0,1),new(1,1)],
        [new(0,0),new(1,0),new(2,0),new(3,0)],
        [new(0,0),new(1,0),new(0,1),new(1,1)],
        [new(0,0),new(0,1),new(0,2),new(1,2)],
        [new(1,0),new(2,0),new(0,1),new(1,1)],
        [new(0,0),new(1,0),new(2,0),new(1,1)]
    ];

    public GenerationResult Run(Action<string> log, Action<Level, int>? onCandidate = null, CancellationToken cancellation = default)
    {
        if (options.Width is <2 or >20 || options.Height is <2 or >20 || options.Coverage <= 0 || options.Coverage > 100 ||
            options.SingleCellRatio < 0 || options.SingleCellRatio > 1 || options.MaxCandidates < 1 ||
            options.MinOpening < 2 || options.MinOpening > 12 || options.Method is not ("anneal" or "random") ||
            options.BlockSize < 1 || options.BlockSize > 4)
            throw new ArgumentException("Use coverage in (0,100], single-cell-ratio in [0,1], block-size in [1,4], maxCandidates >= 1 and min-opening in [2,12].");
        if (options.FixedLayout != null) GetCatalog(options.FixedLayout);
        watch.Restart();
        var levels = new List<Level>();
        var exported = new HashSet<string>();
        Candidate? current = null;
        int attempts=0, constructed=0, evaluated=0, chainSteps=0, stall=0, bestOpening=0;
        double lastLog=0, chainBest=double.MinValue;
        log($"Starting generation: coverage >= {options.Coverage}%, 1x1 ratio {options.SingleCellRatio}, {options.MaxCandidates} verified candidates; no time limit.");
        reportProgress = () =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds-lastLog < 10) return;
            // The best forced opening so far tells whether --min-opening is reachable on this layout.
            log($"{watch.Elapsed.TotalSeconds:F0}s: {attempts} attempts, {evaluated} evaluated, {levels.Count}/{options.MaxCandidates} saved; best opening {bestOpening} (need {options.MinOpening}).");
            lastLog=watch.Elapsed.TotalSeconds;
        };
        try
        {
            while (levels.Count < options.MaxCandidates)
            {
                reportProgress(); attempts++;
                // Keep annealing one board until it stops improving; large boards need long chains
                // to shuffle every block away from its exit.
                bool fresh=options.Method!="anneal" || current==null || stall>=StallLimit;
                if (fresh) { chainSteps=0; stall=0; chainBest=double.MinValue; }
                var level=fresh ? Construct() : Mutate(current!.Level);
                if (level==null) { stall++; continue; }
                constructed++;
                var candidate=Evaluate(level); evaluated++;
                // Reheat every 200 steps of a chain; an unbounded run has no time-based cooling schedule.
                double temperature=Math.Max(.1,5*(1-(chainSteps++%200)/200.0));
                if (fresh || current==null || candidate.Score>current.Score || random.NextDouble()<Math.Exp((candidate.Score-current.Score)/temperature)) current=candidate;
                if (candidate.Score>chainBest) { chainBest=candidate.Score; stall=0; } else stall++;
                bestOpening=Math.Max(bestOpening,candidate.Opening.LowerBound);
                if (candidate.Opening.LowerBound<options.MinOpening || candidate.Opening.Status=="unreachable") continue;
                var finished=FinalizeCandidate(candidate,attempts);
                if (finished==null || !MeetsComposition(finished) || !exported.Add(Signature(finished))) continue;
                new Engine(finished).Replay(finished.Solution);
                levels.Add(finished);
                onCandidate?.Invoke(finished,levels.Count);
                log($"Candidate {levels.Count}/{options.MaxCandidates} verified: {finished.Blocks.Length} blocks, {finished.Analysis["coveredCells"]} floor cells covered.");
                current=null;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            log($"Stopped: {levels.Count}/{options.MaxCandidates} verified candidates retained.");
        }
        return new(levels,attempts,constructed,evaluated,watch.Elapsed.TotalSeconds);
    }

    private Level? FinalizeCandidate(Candidate original, int attempts)
    {
        var c=options.Specials?Evaluate(Decorate(original.Level),DateTime.UtcNow.AddSeconds(2)):original;
        if(c.Opening.LowerBound<options.MinOpening)return null;
        var engine = new Engine(c.Level);
        c.Level.Solution = Search.Shorten(engine,c.Level.Solution);
        var (improved,solved)=Search.Improve(engine,c.Level.Solution,100000,8);
        c.Level.Solution=Search.Shorten(engine,improved);
        engine.Replay(c.Level.Solution);
        var opening=Search.Opening(engine,engine.Initial(),12,50000,DateTime.UtcNow.AddSeconds(2));
        if(opening.LowerBound<c.Opening.LowerBound)opening=c.Opening;
        var two=Search.Opening(engine,engine.Initial(),12,50000,DateTime.UtcNow.AddSeconds(2),2);
        var frames=engine.Replay(c.Level.Solution);
        int first=frames.FindIndex(s=>s.Events>0);
        var difficulty=Difficulty.Rate(engine,opening,two);
        c.Level.Analysis = new()
        {
            ["difficulty"] = difficulty.Score, ["difficultyFeatures"] = difficulty.Features,
            ["method"] = options.Method == "anneal" ? "reverse construction + simulated annealing" : "reverse construction + random sampling",
            ["solvability"] = "verifiedByReplay", ["solutionOptimal"] = solved.Optimal,
            ["solverStatus"] = solved.Status,["solverVisited"] = solved.Visited,
            ["solutionMoves"] = c.Level.Solution.Length, ["opening"] = opening,
            ["firstTwoFullRemovals"] = two,
            ["distinctBlocksBeforeFirstEventInWitness"] = c.Level.Solution.Take(Math.Max(0,first-1)).Select(m=>m.Block).Distinct().Count(),
            ["occupancy"] = Math.Round(c.Density,4), ["score"] = Math.Round(c.Score,3),
            ["floorCells"] = engine.Geometry.FloorCount,
            ["coveredCells"] = c.Level.Blocks.Sum(p=>p.Shape.Length),
            ["coverageRequested"] = options.Coverage,
            ["singleCellRatioRequested"] = options.SingleCellRatio, ["blockSizeRequested"] = options.BlockSize,
            ["blockCount"] = c.Level.Blocks.Length,
            ["singleCellCount"] = c.Level.Blocks.Count(p=>p.Shape.Length==1),
            ["singleCellRatioActual"] = (double)c.Level.Blocks.Count(p=>p.Shape.Length==1)/c.Level.Blocks.Length,
            ["minimumOpeningRequired"] = options.MinOpening,
            ["seed"] = options.Seed, ["attempts"] = attempts,
            ["generationSeconds"] = Math.Round(watch.Elapsed.TotalSeconds,3)
        };
        return c.Level;
    }

    private Level? Construct()
    {
        Layout board = options.FixedLayout ?? CreateLayout();
        var pieces = CreateComposition(board);
        if (pieces == null) return null;
        var template = new Level { Seed=options.Seed,Board=board,Blocks=pieces.ToArray() };
        var engine = new Engine(template);
        var state = new State(Enumerable.Repeat(-1,pieces.Count).ToArray(),pieces.Select(p=>p.Colors.Length).ToArray());
        for (int i=0;i<pieces.Count;i++) if (pieces[i].Colors.Length==0)
        {
            var used=engine.Occupied(state);
            var fits=Enumerable.Range(0,board.Width*board.Height).Where(p=>engine.Fits(i,p,used)).ToArray();
            if (fits.Length==0) return null;
            state.Positions[i]=fits[random.Next(fits.Length)];
        }
        var undo = new List<Move>();
        int afterComplete=0;
        for (int step=0;step<pieces.Count*45;step++)
        {
            reportProgress();
            bool complete=state.Events==0 && state.Positions.All(p=>p>=0);
            if (complete && afterComplete++>=20) break;
            bool restored=false;
            if (!complete && random.NextDouble()<.75)
            {
                // Restore large pieces first, while there is still room to bring them in through a gate.
                var order=Enumerable.Range(0,pieces.Count).Where(i=>state.Layers[i]>0)
                    .OrderByDescending(i=>pieces[i].Shape.Length+random.NextDouble()*1.5).ToArray();
                foreach (int i in order)
                    if (TryRestore(engine,state,i,out var previous,out var forward))
                    { state=previous!; undo.Add(forward!); restored=true; break; }
            }
            if (!restored && TryReverseShift(engine,state,out var shifted,out var move))
            { state=shifted!; undo.Add(move!); }
        }
        if (state.Events!=0 || state.Positions.Any(p=>p<0)) return null;
        var level=template with { Blocks=pieces.Select((p,i)=>p with { Position=engine.Decode(state.Positions[i]) }).ToArray(),Solution=undo.AsEnumerable().Reverse().ToArray() };
        new Engine(level).Replay(level.Solution);
        return level;
    }

    private bool TryRestore(Engine engine,State state,int i,out State? previous,out Move? forward)
    {
        previous=null;forward=null;
        int layer=state.Layers[i]-1;
        if (state.Events-1<engine.Pieces[i].FrozenFor) return false;
        var restored=state.Copy();restored.Layers[i]=layer;
        var used=engine.Occupied(state,i);
        IEnumerable<int> goals=state.Positions[i]>=0 ? [state.Positions[i]] :
            Enumerable.Range(0,engine.Board.Width*engine.Board.Height).OrderBy(_=>random.Next());
        foreach (int goal in goals)
        {
            if (!engine.Fits(i,goal,used)) continue;
            restored.Positions[i]=goal;
            if (engine.Exit(restored,i,goal,engine.Pieces[i].Colors[layer])==null) continue;
            var reachable=engine.Reachable(restored,i);
            if (reachable.Count==0) continue;
            // Bias insertion away from the boundary to keep gate approaches free
            // for later inverse insertions. Exploration still includes all poses.
            var interiorPoses=reachable.Where(p=>engine.Exit(restored,i,p,engine.Pieces[i].Colors[layer])==null).ToArray();
            if(interiorPoses.Length==0)continue;
            int pos=interiorPoses.OrderByDescending(p=>Interior(engine,p)+random.NextDouble()*3).First();
            restored.Positions[i]=pos;
            var action=new Move(engine.Pieces[i].Id,engine.Decode(goal));
            if (engine.Apply(restored,action).Key()!=state.Key()) continue;
            previous=restored;forward=action;return true;
        }
        return false;
    }
    private static int Interior(Engine e,int pos)
    {
        var c=e.Decode(pos);return Math.Min(Math.Min(c.X,e.Board.Width-1-c.X),Math.Min(c.Y,e.Board.Height-1-c.Y));
    }
    private bool TryReverseShift(Engine engine,State state,out State? previous,out Move? forward)
    {
        previous=null;forward=null;
        foreach (int i in Enumerable.Range(0,engine.Pieces.Length).OrderBy(_=>random.Next()))
        {
            if (state.Positions[i]<0) continue;
            var p=engine.Pieces[i];
            if (p.Colors.Length>0 && engine.Exit(state,i,state.Positions[i],p.Colors[state.Layers[i]])!=null) continue;
            var reachable=engine.Reachable(state,i).Where(pos=>pos!=state.Positions[i] &&
                (p.Colors.Length==0 || engine.Exit(state,i,pos,p.Colors[state.Layers[i]])==null)).ToArray();
            if (reachable.Length==0) continue;
            previous=state.Copy();previous.Positions[i]=reachable[random.Next(reachable.Length)];
            forward=new Move(p.Id,engine.Decode(state.Positions[i]));return true;
        }
        return false;
    }
    private Level? Mutate(Level level)
    {
        var engine=new Engine(level);var state=engine.Initial();var undo=new List<Move>();
        for (int j=0;j<random.Next(1,5);j++)
            if (TryReverseShift(engine,state,out var previous,out var forward)) { state=previous!;undo.Add(forward!); }
        if (undo.Count==0) return null;
        return level with { Blocks=level.Blocks.Select((p,i)=>p with { Position=engine.Decode(state.Positions[i]) }).ToArray(),
            Solution=undo.AsEnumerable().Reverse().Concat(level.Solution).ToArray(),Analysis=[] };
    }
    private Candidate Evaluate(Level level,DateTime? evaluationDeadline=null)
    {
        var engine=new Engine(level);var initial=engine.Initial();
        var opening=Search.Opening(engine,initial,Math.Max(options.MinOpening,5),12000,
            evaluationDeadline??DateTime.UtcNow.AddMilliseconds(800));
        double density=(double)level.Blocks.Sum(p=>p.Shape.Length)/engine.Geometry.FloorCount;
        int mobile=0,direct=0,exitPoses=0;
        for(int i=0;i<level.Blocks.Length;i++)
        {
            var reachable=engine.Reachable(initial,i);
            if(reachable.Count>1)mobile++;
            if(level.Blocks[i].Colors.Length==0)continue;
            int exits=reachable.Count(p=>engine.Exit(initial,i,p,level.Blocks[i].Colors[0])!=null);
            if(exits>0)direct++;
            exitPoses+=exits;
        }
        double score=opening.LowerBound*20-direct*7-exitPoses*.15+density*8-Math.Abs(mobile-3)*.25;
        if (opening.Status=="unreachable") score=-1000;
        return new(level,score,opening,density);
    }

    private Level Decorate(Level original)
    {
        // Start from a known witness. Every accepted modifier is replayed through
        // the same forward rules, then the opening is measured again from scratch.
        var level=original with { Solution=Search.Shorten(new Engine(original),original.Solution),Analysis=[] };
        var baseEngine=new Engine(level);
        level.Solution=Search.Improve(baseEngine,level.Solution,30000,1.5).Moves;
        string last=level.Solution[^1].Block;
        var gray=level with { Blocks=level.Blocks.Select(p=>p.Id==last?p with {Colors=[]}:p).ToArray(),Solution=level.Solution[..^1] };
        if (MeetsComposition(gray)) level=gray;
        new Engine(level).Replay(level.Solution);

        // Insert one extra peel after the first full removal. A return drop restores
        // the witness geometry; revalidation catches any earlier accidental peel.
        var engine=new Engine(level);var frames=engine.Replay(level.Solution);
        bool layered=false;
        for(int t=1;t<frames.Count-1&&!layered;t++)
        {
            var s=frames[t];if(s.Events==0)continue;
            for(int i=0;i<level.Blocks.Length&&!layered;i++)
            {
                var p=level.Blocks[i];if(s.Positions[i]<0||p.Colors.Length==0)continue;
                foreach(int pos in engine.Reachable(s,i))
                {
                    string? color=level.Board.Gates.Select(g=>g.Color).Distinct().FirstOrDefault(color=>color!=p.Colors[0]&&engine.Exit(s,i,pos,color)!=null);
                    if(color==null)continue;
                    var blocks=level.Blocks.ToArray();blocks[i]=p with {Colors=[color,..p.Colors]};
                    var moves=level.Solution.Take(t).Concat([new Move(p.Id,engine.Decode(pos)),new Move(p.Id,engine.Decode(s.Positions[i]))]).Concat(level.Solution.Skip(t)).ToArray();
                    var candidate=level with {Blocks=blocks,Solution=moves};
                    if (!MeetsComposition(candidate)) continue;
                    try
                    {
                        new Engine(candidate).Replay(moves);
                        var assessment=Evaluate(candidate,DateTime.UtcNow.AddMilliseconds(400));
                        if(assessment.Opening.LowerBound<options.MinOpening)continue;
                        level=candidate;layered=true;break;
                    }
                    catch(ArgumentException) { }
                }
            }
        }

        // Specials are an occasional twist in the genre, so cap them instead of applying them
        // to every eligible block. Freeze late-used pieces; the counter is global and layers count too.
        int cap=1+level.Blocks.Length/8, frozen=0, directional=0;
        engine=new Engine(level);frames=engine.Replay(level.Solution);
        foreach(int i in Enumerable.Range(0,level.Blocks.Length).OrderBy(_=>random.Next()))
        {
            if(frozen>=cap)break;
            var p=level.Blocks[i];int first=Array.FindIndex(level.Solution,m=>m.Block==p.Id);
            if(first<0||frames[first].Events==0)continue;
            var blocks=level.Blocks.ToArray();blocks[i]=p with {FrozenFor=Math.Min(3,frames[first].Events)};
            var candidate=level with {Blocks=blocks};
            try {new Engine(candidate).Replay(candidate.Solution);level=candidate;frozen++;}catch(ArgumentException) { }
        }
        // Try both axis restrictions; corner-turning witnesses are rejected. Prefer pieces that
        // actually move in the solution, so the arrow matters.
        var moved=level.Solution.Select(m=>m.Block).ToHashSet();
        foreach(int i in Enumerable.Range(0,level.Blocks.Length).OrderBy(i=>(moved.Contains(level.Blocks[i].Id)?0:1)+random.NextDouble()))
        {
            if(directional>=cap)break;
            if(level.Blocks[i].Shape.Length==1 && random.NextDouble()<.5)continue;
            foreach(var axis in new[]{Axis.Horizontal,Axis.Vertical}.OrderBy(_=>random.Next()))
            {
                var blocks=level.Blocks.ToArray();blocks[i]=blocks[i] with {Axis=axis};
                var candidate=level with {Blocks=blocks};
                try {new Engine(candidate).Replay(candidate.Solution);level=candidate;directional++;break;}catch(ArgumentException) { }
            }
        }
        new Engine(level).Replay(level.Solution);
        return level;
    }
    private Layout CreateLayout()
    {
        int w=options.Width,h=options.Height;
        var gates=new List<Gate>();
        for (int i=0;i<4;i++)
        {
            int size=i%2==0?w:h;
            int length=Math.Min(size,random.Next(2,4));
            gates.Add(new($"g{i+1}",(Side)i,random.Next(size-length+1),length,Palette[i]));
        }
        var walls=new HashSet<Cell>();
        if (w>=5 && h>=5)
            for (int i=0;i<random.Next(1,4);i++) walls.Add(new(random.Next(1,w-1),random.Next(1,h-1)));
        return new(w,h,walls.ToArray(),gates.ToArray());
    }
    private static Cell[] Rotate(Cell[] cells,int times)
    {
        var result=cells.ToArray();
        for (int i=0;i<times;i++) result=result.Select(c=>new Cell(-c.Y,c.X)).ToArray();
        int x=result.Min(c=>c.X),y=result.Min(c=>c.Y);
        return result.Select(c=>new Cell(c.X-x,c.Y-y)).ToArray();
    }
    private static string Signature(Level l)=>System.Text.Json.JsonSerializer.Serialize(new { l.Board,l.Blocks },Files.Json);
}

