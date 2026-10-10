using BlockPuzzle;

try
{
    if (args.Length==0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("""
        Block Puzzle — verified level generation

        dotnet run -c Release -- generate --coverage 80 --single-cell-ratio 0.2 --block-size 2.8 --maxCandidates 1 --seed 42 --out output
          --width 6 --height 6 --min-opening 3
          --specials true --layout layout.json --method anneal|random
          Runs until maxCandidates verified levels are saved. Ctrl+C stops and keeps saved levels.

        verify, solve, analyze and render live in the LevelGenerator.Tools project.
        """);
        return 0;
    }
    var opts=new Dictionary<string,string>();
    var positional=new List<string>();
    for (int i=1;i<args.Length;i++)
    {
        if (!args[i].StartsWith("--")) { positional.Add(args[i]);continue; }
        string key=args[i][2..];
        if (key=="max-candidates") key="maxCandidates";
        if (++i>=args.Length || args[i].StartsWith("--")) throw new ArgumentException($"Missing value for --{key}.");
        if (!opts.TryAdd(key,args[i])) throw new ArgumentException($"Duplicate option --{key}.");
    }
    string Text(string key,string fallback)=>opts.GetValueOrDefault(key,fallback);
    int Number(string key,int fallback)=>int.Parse(Text(key,fallback.ToString()),System.Globalization.CultureInfo.InvariantCulture);
    decimal Decimal(string key,decimal fallback)=>decimal.Parse(Text(key,fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)),System.Globalization.CultureInfo.InvariantCulture);
    string[] allowed=args[0] switch
    {
        "generate"=>["seed","width","height","coverage","single-cell-ratio","block-size","maxCandidates","min-opening","specials","layout","out","method"],
        _=>throw new ArgumentException("Unknown command. verify, solve, analyze and render moved to LevelGenerator.Tools.")
    };
    if (opts.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown option: "+string.Join(", ",opts.Keys.Except(allowed)));
    switch (args[0])
    {
        case "generate":
        {
            if (positional.Count>0) throw new ArgumentException("Unexpected positional argument.");
            var options=new GenerateOptions(Seed:Number("seed",42),Width:Number("width",6),Height:Number("height",6),
                Coverage:Decimal("coverage",80),SingleCellRatio:Decimal("single-cell-ratio",.2m),BlockSize:Decimal("block-size",2.8m),MaxCandidates:Number("maxCandidates",1),
                MinOpening:Number("min-opening",3),Specials:bool.Parse(Text("specials","true")),
                FixedLayout:opts.TryGetValue("layout",out var layout)?Files.Read<Layout>(layout):null,Method:Text("method","anneal"));
            string folder=Path.GetFullPath(Text("out","output"));
            Directory.CreateDirectory(folder);
            var saved=new List<string>();
            // Files are named <difficulty 0001-1000>-<layout>-<counter>; the counter is per layout and never reused.
            string layoutName=LevelNames.Layout(opts.GetValueOrDefault("layout"));
            int nextCounter=LevelNames.NextCounter(folder,layoutName);
            using var cancellation=new CancellationTokenSource();
            ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
            Console.CancelKeyPress+=cancel;
            try
            {
                var result=new Generator(options).Run(Console.WriteLine,(level,index)=>
                {
                    string stem=Path.Combine(folder,LevelNames.Stem((int)level.Analysis["difficulty"],layoutName,nextCounter++));
                    var engine=new Engine(level);
                    engine.Replay(level.Solution);
                    Renderer.Write(level,stem+".html");
                    File.WriteAllText(stem+".svg",Renderer.Svg(engine,engine.Initial()));
                    Files.Write(stem+".json",level);
                    saved.Add(Path.GetFileName(stem)+".json");
                    Files.Write(Path.Combine(folder,"run.json"),new {options,status="running",exported=saved.Count,files=saved});
                    Console.WriteLine($"Saved {stem}.json ({index}/{options.MaxCandidates}; {level.Solution.Length} replay-verified drops).");
                },cancellation.Token);
                Files.Write(Path.Combine(folder,"run.json"),new {options,status=result.Levels.Count==options.MaxCandidates?"complete":"cancelled",
                    result.Attempts,result.Constructed,result.Evaluated,result.Seconds,exported=saved.Count,files=saved});
                if(result.Levels.Count<options.MaxCandidates) return 130;
            }
            finally {Console.CancelKeyPress-=cancel;}
            break;
        }
    }
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or System.Text.Json.JsonException or FormatException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);return 1;
}
