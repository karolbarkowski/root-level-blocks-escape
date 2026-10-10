namespace BlockPuzzle;

public sealed partial class Generator
{
    private sealed record ShapeChoice(Cell[] Shape, string[] Colors);
    private sealed record CompositionPlan(int Count, int Singles, int Area);
    private sealed record Catalog(ShapeChoice[] Shapes, string[] Colors, List<CompositionPlan> Plans, bool[,] Sums);
    private Layout? cachedBoard;
    private Catalog? cachedCatalog;
    internal static int RequiredCells(int floor, decimal coverage) => (int)Math.Ceiling(floor * coverage / 100m);
    internal static int RequiredSingles(int count, decimal ratio) => (int)Math.Round(count * ratio, MidpointRounding.AwayFromZero);

    private Catalog GetCatalog(Layout board)
    {
        if (ReferenceEquals(board,cachedBoard) && cachedCatalog != null) return cachedCatalog;
        int floor=new Geometry(board).FloorCount;
        var colors=board.Gates.Select(g=>g.Color).Distinct().ToArray();
        if (floor==0 || colors.Length==0) throw new ArgumentException("Generation needs playable floor and at least one gate color.");
        var choices=new List<ShapeChoice>();
        var seen=new HashSet<string>();
        foreach(var original in Shapes) foreach(var source in new[]{original,original.Select(c=>new Cell(-c.X,c.Y)).ToArray()})
        for(int rotation=0;rotation<4;rotation++)
        {
            var shape=Rotate(source,rotation);
            if (!seen.Add(string.Join(";",shape.OrderBy(c=>c.Y).ThenBy(c=>c.X)))) continue;
            var allowed=new List<string>();
            foreach(var color in colors)
            {
                var e=new Engine(new Level {Board=board,Blocks=[new("probe",shape,new(0,0),[color])]});
                var state=new State([-1],[0]);
                var used=e.Occupied(state);
                if (Enumerable.Range(0,board.Width*board.Height).Any(p=>e.Fits(0,p,used)&&e.Exit(state,0,p,color)!=null)) allowed.Add(color);
            }
            if (allowed.Count>0) choices.Add(new(shape,allowed.ToArray()));
        }
        foreach(var color in colors)
            if (!choices.Any(c=>c.Colors.Contains(color) && (options.SingleCellRatio!=0 || c.Shape.Length>1) && (options.SingleCellRatio!=1 || c.Shape.Length==1)))
                throw new ArgumentException($"Gate color '{color}' has no usable shape with this 1x1 ratio. Check its approach and gate width.");
        var sizes=choices.Where(c=>c.Shape.Length>1).Select(c=>c.Shape.Length).Distinct().ToArray();
        var sums=new bool[floor+1,floor+1]; sums[0,0]=true;
        for(int n=1;n<=floor;n++) for(int area=0;area<=floor;area++)
            sums[n,area]=sizes.Any(size=>area>=size && sums[n-1,area-size]);
        int target=RequiredCells(floor,options.Coverage);
        var plans=new List<CompositionPlan>();
        for(int count=Math.Max(2,colors.Length);count<=floor;count++)
        {
            int singles=RequiredSingles(count,options.SingleCellRatio), larger=count-singles;
            if (singles>0 && !choices.Any(c=>c.Shape.Length==1)) continue;
            for(int area=target;area<=floor;area++)
                if(area>=singles && sums[larger,area-singles]) plans.Add(new(count,singles,area));
        }
        if(plans.Count==0) throw new ArgumentException("Coverage and 1x1 ratio cannot fit the playable floor with the available shapes and gate colors.");
        // Use the smallest achievable area at or above the requested coverage.
        int minimum=plans.Min(p=>p.Area); plans=plans.Where(p=>p.Area==minimum).ToList();
        var catalog=new Catalog(choices.ToArray(),colors,plans,sums);
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
        var chosen=new List<ShapeChoice>();
        for(int i=0;i<plan.Singles;i++) chosen.Add(catalog.Shapes.First(c=>c.Shape.Length==1));
        int remaining=plan.Count-plan.Singles, area=plan.Area-plan.Singles;
        while(remaining>0)
        {
            var candidates=catalog.Shapes.Where(c=>c.Shape.Length>1 && area>=c.Shape.Length && catalog.Sums[remaining-1,area-c.Shape.Length]).ToArray();
            var choice=candidates[random.Next(candidates.Length)]; chosen.Add(choice); area-=choice.Shape.Length; remaining--;
        }
        chosen=chosen.OrderBy(_=>random.Next()).ToList();
        // Match every distinct color to a compatible block before filling the rest.
        var assigned=Enumerable.Repeat(-1,chosen.Count).ToArray();
        bool Match(int color, HashSet<int> visited)
        {
            foreach(int i in Enumerable.Range(0,chosen.Count).OrderBy(_=>random.Next()))
            {
                if(!chosen[i].Colors.Contains(catalog.Colors[color]) || !visited.Add(i)) continue;
                if(assigned[i]<0 || Match(assigned[i],visited)) {assigned[i]=color;return true;}
            }
            return false;
        }
        for(int color=0;color<catalog.Colors.Length;color++) if(!Match(color,[])) return null;
        return chosen.Select((c,i)=>new Piece($"b{i+1}",c.Shape,new(0,0),
            [assigned[i]>=0 ? catalog.Colors[assigned[i]] : c.Colors[random.Next(c.Colors.Length)]])).ToList();
    }

    internal bool MeetsComposition(Level level)
    {
        var pieces=level.Blocks;
        var colors=pieces.Where(p=>p.Colors.Length>0).Select(p=>p.Colors[0]).ToHashSet();
        return pieces.Sum(p=>p.Shape.Length)>=RequiredCells(new Geometry(level.Board).FloorCount,options.Coverage) &&
            pieces.Count(p=>p.Shape.Length==1)==RequiredSingles(pieces.Length,options.SingleCellRatio) &&
            colors.SetEquals(level.Board.Gates.Select(g=>g.Color));
    }
}
