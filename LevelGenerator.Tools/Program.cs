using BlockPuzzle;

// Inspection tools for saved levels. Generation itself lives in LevelGenerator.
try
{
    if (args.Length==0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("""
        Block Puzzle — level tools

        dotnet run -c Release -- verify output/level-001.json
        dotnet run -c Release -- solve level.json --seconds 30 --states 100000
        dotnet run -c Release -- analyze level.json --seconds 10 --states 100000
        dotnet run -c Release -- render level.json --out replay.html
        dotnet run -c Release -- rate levels-folder-or-files...
          Re-rates difficulty and renames <difficulty>-<layout>-<counter> files to the new score.
        """);
        return 0;
    }
    var opts=new Dictionary<string,string>();
    var positional=new List<string>();
    for (int i=1;i<args.Length;i++)
    {
        if (!args[i].StartsWith("--")) { positional.Add(args[i]);continue; }
        string key=args[i][2..];
        if (++i>=args.Length || args[i].StartsWith("--")) throw new ArgumentException($"Missing value for --{key}.");
        if (!opts.TryAdd(key,args[i])) throw new ArgumentException($"Duplicate option --{key}.");
    }
    string Text(string key,string fallback)=>opts.GetValueOrDefault(key,fallback);
    int Number(string key,int fallback)=>int.Parse(Text(key,fallback.ToString()),System.Globalization.CultureInfo.InvariantCulture);
    string Input()=>positional.Count==1?positional[0]:throw new ArgumentException("Expected one input file.");
    string[] allowed=args[0] switch
    {
        "solve" or "analyze"=>["seconds","states","out"],"render"=>["out"],"verify" or "rate"=>[],
        "generate"=>throw new ArgumentException("generate lives in the LevelGenerator project."),
        _=>throw new ArgumentException("Unknown command.")
    };
    if (opts.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown option: "+string.Join(", ",opts.Keys.Except(allowed)));
    switch (args[0])
    {
        case "verify":
        {
            var level=Files.Read<Level>(Input());var engine=new Engine(level);
            var frames=engine.Replay(level.Solution);
            Console.WriteLine($"VALID: {level.Blocks.Length} blocks, {level.Solution.Length} legal drops, {frames[^1].Events} removal events; all colored blocks cleared.");
            break;
        }
        case "solve":
        {
            string path=Input();var level=Files.Read<Level>(path);var engine=new Engine(level);
            int seconds=Number("seconds",30),states=Number("states",100000);
            if (seconds<1 || states<1) throw new ArgumentException("Search budgets must be positive.");
            var result=Search.Solve(engine,states,DateTime.UtcNow.AddSeconds(seconds));
            Console.WriteLine($"{result.Status}: {result.Visited} states; {result.Moves.Length} moves; optimal={result.Optimal}.");
            if (result.Status!="solved") return 2;
            engine.Replay(result.Moves);
            level.Solution=result.Moves;level.Analysis["solutionOptimal"]=true;level.Analysis["solutionMoves"]=result.Moves.Length;
            string output=Text("out",Path.ChangeExtension(path,"solved.json"));
            Files.Write(output,level);Renderer.Write(level,Path.ChangeExtension(output,"html"));
            break;
        }
        case "analyze":
        {
            string path=Input();var level=Files.Read<Level>(path);var engine=new Engine(level);
            int seconds=Number("seconds",10),states=Number("states",100000);
            if(seconds<1||states<1)throw new ArgumentException("Search budgets must be positive.");
            var first=Search.Opening(engine,engine.Initial(),20,states,DateTime.UtcNow.AddSeconds(seconds));
            var two=Search.Opening(engine,engine.Initial(),20,states,DateTime.UtcNow.AddSeconds(seconds),2);
            level.Analysis["opening"]=first;level.Analysis["firstTwoFullRemovals"]=two;
            Console.WriteLine($"First removal event: {first}\nFirst two full removals: {two}");
            if(opts.TryGetValue("out",out string? output))Files.Write(output,level);
            break;
        }
        case "render":
        {
            string path=Input();Renderer.Write(Files.Read<Level>(path),Text("out",Path.ChangeExtension(path,"html")));break;
        }
        case "rate":
        {
            if (positional.Count==0) throw new ArgumentException("Expected level files or folders.");
            var files=positional.SelectMany(p=>Directory.Exists(p)?Directory.GetFiles(p,"*.json").Where(f=>Path.GetFileName(f)!="run.json"):[p])
                .OrderBy(f=>f,StringComparer.Ordinal).ToArray();
            foreach (string path in files)
            {
                var level=Files.Read<Level>(path);
                var result=Difficulty.Rate(level);
                level.Analysis["difficulty"]=result.Score;level.Analysis["difficultyFeatures"]=result.Features;
                string folder=Path.GetDirectoryName(Path.GetFullPath(path))!,stem=Path.GetFileNameWithoutExtension(path),target=stem;
                if (LevelNames.TryParse(stem,out _,out string layout,out int counter)) target=LevelNames.Stem(result.Score,layout,counter);
                if (target!=stem && LevelNames.Extensions.Any(ext=>File.Exists(Path.Combine(folder,target+ext))))
                    throw new IOException($"Cannot rename {stem}: {target} already exists.");
                Files.Write(path,level);
                Renderer.Write(level,Path.Combine(folder,stem+".html"));
                if (target!=stem)
                    foreach (string ext in LevelNames.Extensions)
                        if (File.Exists(Path.Combine(folder,stem+ext))) File.Move(Path.Combine(folder,stem+ext),Path.Combine(folder,target+ext));
                Console.WriteLine(target==stem?$"{stem}: {result.Score}":$"{stem} -> {target}");
            }
            break;
        }
    }
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or System.Text.Json.JsonException or FormatException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);return 1;
}
