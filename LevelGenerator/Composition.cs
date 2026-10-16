namespace BlockPuzzle;

public sealed partial class Generator
{
    private sealed record ShapeChoice(Cell[] Shape, string[] Types);
    private sealed record CompositionPlan(int Count, int Singles, int Area);
    private sealed record Catalog(ShapeChoice[] Shapes, string[] Types, List<CompositionPlan> Plans, bool[,] Sums);
    private Layout? cachedBoard;
    private Catalog? cachedCatalog;
    internal static int RequiredCells(int floor, decimal coverage) => (int)Math.Ceiling(floor * coverage / 100m);
    internal static int RequiredSingles(int count, decimal ratio) => (int)Math.Round(count * ratio, MidpointRounding.AwayFromZero);

    private Catalog GetCatalog(Layout board)
    {
        if (ReferenceEquals(board,cachedBoard) && cachedCatalog != null) return cachedCatalog;
        int floor=new Geometry(board).FloorCount;
        var types=board.Gates.Select(g=>g.Type).Distinct().ToArray();
        if (floor==0 || types.Length==0) throw new ArgumentException("Generation needs playable floor and at least one gate.");
        var choices=new List<ShapeChoice>();
        var seen=new HashSet<string>();
        foreach(var original in Shapes) foreach(var source in new[]{original,original.Select(c=>new Cell(-c.X,c.Y)).ToArray()})
        for(int rotation=0;rotation<4;rotation++)
        {
            var shape=Rotate(source,rotation);
            if (!seen.Add(string.Join(";",shape.OrderBy(c=>c.Y).ThenBy(c=>c.X)))) continue;
            var allowed=new List<string>();
            foreach(var type in types)
            {
                var e=new Engine(new Level {Board=board,Blocks=[new("probe",shape,new(0,0),[type])]});
                var state=new State([-1],[0]);
                var used=e.Occupied(state);
                if (Enumerable.Range(0,board.Width*board.Height).Any(p=>e.Fits(0,p,used)&&e.Exit(state,0,p,type)!=null)) allowed.Add(type);
            }
            if (allowed.Count>0) choices.Add(new(shape,allowed.ToArray()));
        }
        foreach(var type in types)
            if (!choices.Any(c=>c.Types.Contains(type) && (options.SingleCellRatio!=0 || c.Shape.Length>1) && (options.SingleCellRatio!=1 || c.Shape.Length==1)))
                throw new ArgumentException($"Gate type '{type}' has no usable shape with this 1x1 ratio. Check its approach and gate width.");
        var sizes=choices.Where(c=>c.Shape.Length>1).Select(c=>c.Shape.Length).Distinct().ToArray();
        var sums=new bool[floor+1,floor+1]; sums[0,0]=true;
        for(int n=1;n<=floor;n++) for(int area=0;area<=floor;area++)
            sums[n,area]=sizes.Any(size=>area>=size && sums[n-1,area-size]);
        int target=RequiredCells(floor,options.Coverage);
        var plans=new List<CompositionPlan>();
        for(int count=Math.Max(2,types.Length);count<=floor;count++)
        {
            int singles=RequiredSingles(count,options.SingleCellRatio), larger=count-singles;
            if (singles>0 && !choices.Any(c=>c.Shape.Length==1)) continue;
            for(int area=target;area<=floor;area++)
                if(area>=singles && sums[larger,area-singles]) plans.Add(new(count,singles,area));
        }
        if(plans.Count==0) throw new ArgumentException("Coverage and 1x1 ratio cannot fit the playable floor with the available shapes and gate types.");
        // Use the smallest achievable area at or above the requested coverage.
        int minimum=plans.Min(p=>p.Area); plans=plans.Where(p=>p.Area==minimum).ToList();
        var catalog=new Catalog(choices.ToArray(),types,plans,sums);
        if(options.FixedLayout!=null) {cachedBoard=board;cachedCatalog=catalog;}
        return catalog;
    }

    internal List<Piece>? CreateComposition(Layout board)
    {
        Catalog catalog;
        try { catalog=GetCatalog(board); }
        catch(ArgumentException) when(options.FixedLayout==null) {return null;}
        // Prefer block counts near area / block-size: the genre uses fewer, larger pieces.
        // A Gaussian weight keeps neighbouring counts possible when an ideal plan fails to construct.
        double ideal=catalog.Plans[0].Area/(double)options.BlockSize;
        var weights=catalog.Plans.Select(p=>Math.Exp(-Math.Pow(p.Count-ideal,2)/2)).ToArray();
        double pick=random.NextDouble()*weights.Sum();
        int planIndex=0;
        while(planIndex<weights.Length-1 && (pick-=weights[planIndex])>0) planIndex++;
        var plan=catalog.Plans[planIndex];
        // Pick the type first, then a shape that can leave through that type's gate. Drawing shapes
        // blind lets wide gates collect most blocks, and filtering afterwards keeps only boards of
        // small, flexible pieces. Targets are as even as the block count allows; a type with no
        // fitting shape left gives its slot to the next most wanted one.
        int typeCount=catalog.Types.Length;
        var wanted=Enumerable.Repeat(plan.Count/typeCount,typeCount).ToArray();
        foreach(int t in Enumerable.Range(0,typeCount).OrderBy(_=>random.Next()).Take(plan.Count%typeCount)) wanted[t]++;
        var counts=new int[typeCount];
        var chosen=new List<(ShapeChoice Choice,int Type)>();
        int remaining=plan.Count-plan.Singles, area=plan.Area-plan.Singles;
        for(int slot=0;slot<plan.Count;slot++)
        {
            bool single=slot<plan.Singles;
            ShapeChoice[] Fitting(string type)=>catalog.Shapes.Where(c=>c.Types.Contains(type) && (single ? c.Shape.Length==1 :
                c.Shape.Length>1 && area>=c.Shape.Length && catalog.Sums[remaining-1,area-c.Shape.Length])).ToArray();
            var best=Enumerable.Range(0,typeCount).OrderByDescending(t=>wanted[t]-counts[t]).ThenBy(_=>random.Next())
                .Select(t=>(Type:t,Shapes:Fitting(catalog.Types[t]))).FirstOrDefault(o=>o.Shapes.Length>0);
            if(best.Shapes==null) return null;
            var choice=best.Shapes[random.Next(best.Shapes.Length)];
            chosen.Add((choice,best.Type)); counts[best.Type]++;
            if(!single) {area-=choice.Shape.Length; remaining--;}
        }
        if(counts.Min()==0 || counts.Max()-counts.Min()>options.TypeSpread) return null;
        return chosen.OrderBy(_=>random.Next()).Select((c,i)=>new Piece($"b{i+1}",c.Choice.Shape,new(0,0),[catalog.Types[c.Type]])).ToList();
    }

    // Difference between the most and least common outer-layer type; neutral blocks don't count.
    private static int Spread(IEnumerable<Piece> pieces, IEnumerable<string> types)
    {
        var counts=types.Distinct().Select(t=>pieces.Count(p=>p.Types.Length>0 && p.Types[0]==t)).ToArray();
        return counts.Max()-counts.Min();
    }
    internal static Dictionary<string,int> TypeCounts(Level level)=>
        level.Board.Gates.Select(g=>g.Type).Distinct().ToDictionary(t=>t,t=>level.Blocks.Count(p=>p.Types.Length>0 && p.Types[0]==t));

    internal bool MeetsComposition(Level level)
    {
        var pieces=level.Blocks;
        var types=pieces.Where(p=>p.Types.Length>0).Select(p=>p.Types[0]).ToHashSet();
        return pieces.Sum(p=>p.Shape.Length)>=RequiredCells(new Geometry(level.Board).FloorCount,options.Coverage) &&
            pieces.Count(p=>p.Shape.Length==1)==RequiredSingles(pieces.Length,options.SingleCellRatio) &&
            types.SetEquals(level.Board.Gates.Select(g=>g.Type)) &&
            Spread(pieces,level.Board.Gates.Select(g=>g.Type))<=options.TypeSpread;
    }
}
