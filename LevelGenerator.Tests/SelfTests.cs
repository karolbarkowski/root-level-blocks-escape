namespace BlockPuzzle;

public static class SelfTests
{
    public static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL "+ex.Message); return 1; }
    }
    public static void Run()
    {
        int count=0;
        void Test(string name,Action body) { body();Console.WriteLine("PASS "+name);count++; }
        void Check(bool value,string message="Assertion failed") { if(!value)throw new InvalidOperationException(message); }
        Engine Make(int w,int h,Piece[] blocks,Gate[]? gates=null,Cell[]? walls=null)=>new(new Level { Board=new(w,h,walls??[],gates??[]),Blocks=blocks });
        Test("one drag turns corners",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(0,0),["red"])],[new("g",Side.Right,2,1,"red")],[new(1,0),new(1,1)]);
            var solved=e.Apply(e.Initial(),new("a",new(2,2)));Check(e.Solved(solved));
            Check(Search.Solve(e,1000,DateTime.UtcNow.AddSeconds(2)).Moves.Length==1);
        });
        Test("swept path cannot cross a wall",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(0,0),[])],walls:[new(1,0),new(1,1),new(1,2)]);
            Check(!e.Reachable(e.Initial(),0).Contains(e.Encode(new(2,0))));
        });
        Test("horizontal axis permits both directions only",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(1,1),[],Axis.Horizontal)]);
            var r=e.Reachable(e.Initial(),0);Check(r.Count==3);Check(r.Contains(e.Encode(new(0,1)))&&r.Contains(e.Encode(new(2,1))));
        });
        Test("whole shape must fit gate",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0),new(1,0),new(0,1)],new(0,0),["red"])],[new("g",Side.Top,0,1,"red")]);
            Check(e.Apply(e.Initial(),new("a",new(0,0))).Events==0);
        });
        Test("interlocking neighbor blocks an outward silhouette",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0),new(0,1),new(1,1)],new(0,0),["red"]),new("b",[new(0,0)],new(1,0),[])],[new("g",Side.Top,0,2,"red")]);
            Check(e.Exit(e.Initial(),0,0,"red")==null);
        });
        Test("layer peel retains shape and thaws another piece",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(0,0),["red","blue"]),new("b",[new(0,0)],new(1,1),["blue"],Axis.Vertical,1)],
                [new("r",Side.Top,0,1,"red"),new("b",Side.Bottom,0,3,"blue")]);
            var s=e.Initial();Check(e.Reachable(s,1).Count==0);var n=e.Apply(s,new("a",new(0,0)));
            Check(n.Positions[0]==s.Positions[0]&&n.Layers[0]==1&&n.Events==1);Check(e.Reachable(n,1).Count>0);
        });
        Test("passing a gate while held does not remove a layer",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(0,0),["red"])],[new("r",Side.Top,1,1,"red")],
                [new(0,1),new(1,1),new(2,1)]);
            Check(e.Apply(e.Initial(),new("a",new(2,0))).Events==0);
        });
        Test("gray blocks move and survive victory",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(0,0),["red"]),new("b",[new(0,0)],new(1,1),[])],[new("r",Side.Top,0,3,"red")]);
            var s=e.Apply(e.Initial(),new("b",new(2,0)));Check(s.Events==0&&s.Positions[1]>=0);
            s=e.Apply(s,new("a",new(0,0)));Check(e.Solved(s)&&s.Positions[1]>=0);
        });
        Test("one drop peels exactly one color",()=>
        {
            var e=Make(2,2,[new("a",[new(0,0)],new(0,0),["red","red"])],[new("r",Side.Top,0,2,"red")]);
            var s=e.Apply(e.Initial(),new("a",new(0,0)));Check(!e.Solved(s)&&s.Events==1);
            Check(e.Solved(e.Apply(s,new("a",new(0,0)))));
        });
        Test("opening bound does not inflate when budget runs out",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(1,1),["red"])],[new("r",Side.Top,0,3,"red")]);
            var r=Search.Opening(e,e.Initial(),5,1,DateTime.UtcNow.AddSeconds(1));Check(r.LowerBound==1);
        });
        Test("overlap and disconnected shapes rejected",()=>
        {
            bool rejected=false;try { Make(3,3,[new("a",[new(0,0),new(2,0)],new(0,0),[])]); }catch(ArgumentException){rejected=true;}Check(rejected);
            rejected=false;try { Make(3,3,[new("a",[new(0,0)],new(0,0),[]),new("b",[new(0,0)],new(0,0),[])]).Initial(); }catch(ArgumentException){rejected=true;}Check(rejected);
        });
        Test("20 by 20 state representation",()=>
        {
            var e=Make(20,20,[new("a",[new(0,0)],new(19,19),[]) ]);Check(e.Reachable(e.Initial(),0).Count==400);
        });
        Test("frozen-only deadlock is unsolvable, budget is unknown",()=>
        {
            var e=Make(2,2,[new("a",[new(0,0)],new(0,0),["red"],Axis.Free,1)],[new("r",Side.Top,0,2,"red")]);
            Check(Search.Solve(e,100,DateTime.UtcNow.AddSeconds(1)).Status=="unsolvable");
            Check(Search.Solve(e,100,DateTime.UtcNow.AddSeconds(-1)).Status=="timeBudget");
        });
        Test("frozen layered directional piece",()=>
        {
            var e=Make(4,3,[new("a",[new(0,0)],new(0,0),["red"]),
                new("b",[new(0,0),new(1,0)],new(1,1),["red","blue"],Axis.Horizontal,1)],
                [new("r",Side.Left,0,3,"red"),new("b",Side.Right,0,3,"blue")]);
            var frames=e.Replay([new("a",new(0,0)),new("b",new(0,1)),new("b",new(2,1))]);
            Check(frames[1].Events==1&&frames[2].Positions[1]==4&&frames[3].Events==3);
        });
        Test("gate does not override restricted outward axis",()=>
        {
            var e=Make(3,3,[new("a",[new(0,0)],new(1,0),["red"],Axis.Horizontal)],
                [new("r",Side.Top,0,3,"red")]);
            Check(e.Apply(e.Initial(),new("a",new(1,0))).Events==0);
        });
        Test("all disconnected and overlapping gates rejected",()=>
        {
            bool rejected=false;try {Make(3,3,[new("a",[new(0,0)],new(0,0),[])],
                [new("r",Side.Top,0,2,"red"),new("b",Side.Top,1,2,"blue")]);}catch(ArgumentException){rejected=true;}Check(rejected);
        });
        Layout Room(Side side = Side.Left, int length = 3)
        {
            var gate = new Gate("g", side, 2, length, "red", side is Side.Left or Side.Top ? 1 : 5);
            var cells = Geometry.GateCells(new(7,7,[],[]),gate).ToHashSet();
            var border = Enumerable.Range(1,5).SelectMany(n => new Cell[] { new(n,1),new(n,5),new(1,n),new(5,n) }).Distinct().Where(c=>!cells.Contains(c)).ToArray();
            return new(7,7,border,[gate],BoundaryMode.Enclosed);
        }
        Engine InRoom(Layout room, params Piece[] pieces) => new(new Level { Board=room,Blocks=pieces });
        void Reject(Action action) { bool rejected=false;try {action();}catch(ArgumentException){rejected=true;}Check(rejected); }
        Test("enclosed floor excludes exterior, walls and gate cells",()=>
        {
            var e=InRoom(Room(),new Piece("a",[new(0,0)],new(3,3),["red"]));
            Check(e.Geometry.FloorCount==9);Check(e.Reachable(e.Initial(),0).Count==9);
            foreach(var c in new Cell[]{new(0,0),new(1,2),new(1,1)})
                Reject(()=>InRoom(Room(),new Piece("a",[new(0,0)],c,["red"])).Initial());
        });
        Test("inset exits work in all four directions",()=>
        {
            foreach(var side in Enum.GetValues<Side>())
            {
                var e=InRoom(Room(side),new Piece("a",[new(0,0)],new(3,3),["red"]));
                Cell target=side switch {Side.Left=>new(2,3),Side.Right=>new(4,3),Side.Top=>new(3,2),_=>new(3,4)};
                Check(e.Solved(e.Apply(e.Initial(),new Move("a",target))));
                Check(Search.Solve(e,100,DateTime.UtcNow.AddSeconds(1)).Moves.Length==1);
            }
        });
        Test("enclosure leaks, inverted gates and overlaps are rejected",()=>
        {
            var b=Room();
            Reject(()=>new Geometry(b with {Walls=b.Walls.Where(c=>c!=new Cell(3,1)).ToArray()}));
            Reject(()=>new Geometry(b with {Gates=[b.Gates[0] with {Side=Side.Right}]}));
            Reject(()=>new Geometry(b with {Gates=[b.Gates[0],b.Gates[0] with {Id="duplicate"}]}));
            Reject(()=>new Geometry(b with {Gates=[b.Gates[0] with {Line=null}]}));
            Reject(()=>new Geometry(b with {BoundaryMode=BoundaryMode.Rectangle}));
        });
        Test("inset gate still requires the entire silhouette to fit",()=>
        {
            var e=InRoom(Room(Side.Left,1),new Piece("a",[new(0,0),new(0,1)],new(2,2),["red"]));
            Check(e.Exit(e.Initial(),0,e.Encode(new(2,2)),"red")==null);
            var interlocked=InRoom(Room(Side.Top),new Piece("a",[new(0,0),new(0,1),new(1,1)],new(2,2),["red"]),new Piece("b",[new(0,0)],new(3,2),[]));
            Check(interlocked.Exit(interlocked.Initial(),0,interlocked.Encode(new(2,2)),"red")==null);
        });
        Test("inset layer peeling, freezing and permanent blocks retain rules",()=>
        {
            var e=InRoom(Room(),new Piece("a",[new(0,0)],new(2,2),["red","red"],Axis.Horizontal),
                new Piece("b",[new(0,0)],new(4,4),["red"],Axis.Horizontal,1),new Piece("gray",[new(0,0)],new(3,3),[]));
            var s=e.Initial();Check(e.Reachable(s,1).Count==0);
            s=e.Apply(s,new Move("a",new(2,2)));Check(s.Events==1&&s.Positions[0]>=0&&e.Reachable(s,1).Count>0);
            s=e.Apply(s,new Move("a",new(2,2)));s=e.Apply(s,new Move("b",new(2,4)));Check(e.Solved(s)&&s.Positions[2]>=0);
            var vertical=InRoom(Room(),new Piece("a",[new(0,0)],new(2,2),["red"],Axis.Vertical));
            Check(vertical.Exit(vertical.Initial(),0,vertical.Encode(new(2,2)),"red")==null);
        });
        Test("enclosed layout JSON round trip and 20x20 canvas",()=>
        {
            var b=Room() with {Width=20,Height=20};
            var json=System.Text.Json.JsonSerializer.Serialize(b,Files.Json);
            var restored=System.Text.Json.JsonSerializer.Deserialize<Layout>(json,Files.Json)!;
            Check(restored.BoundaryMode==BoundaryMode.Enclosed&&restored.Gates[0].Line==1&&new Geometry(restored).FloorCount==9);
        });
        Test("coverage uses ceiling and single-cell ratio rounds block counts",()=>
        {
            Check(Generator.RequiredCells(100,80)==80);
            Check(Generator.RequiredCells(33,80)==27);
            Check(Generator.RequiredSingles(10,.4m)==4);
            Check(Generator.RequiredSingles(6,.4m)==2);
            Check(Generator.RequiredSingles(5,.5m)==3);
        });
        Test("composition covers playable floor and represents every custom gate color",()=>
        {
            var layout=new Layout(10,10,[],[new("a",Side.Top,0,5,"Ocean"),new("b",Side.Bottom,0,5,"Rose"),new("c",Side.Right,0,5,"Ocean")]);
            foreach(decimal ratio in new[]{0m,.4m,1m})
            {
                var generator=new Generator(new(FixedLayout:layout,Coverage:80,SingleCellRatio:ratio));
                for(int attempt=0;attempt<20;attempt++)
                {
                    var blocks=generator.CreateComposition(layout) ?? throw new InvalidOperationException("Composition failed.");
                    Check(blocks!.Sum(p=>p.Shape.Length)>=80);
                    Check(blocks.Count(p=>p.Shape.Length==1)==Generator.RequiredSingles(blocks.Count,ratio));
                    Check(generator.MeetsComposition(new Level {Board=layout,Blocks=blocks.ToArray()}));
                    Check(blocks.Select(p=>p.Colors[0]).ToHashSet().SetEquals(["Ocean","Rose"]));
                }
            }
        });
        Test("enclosed composition excludes exterior and rejects loss of a required color",()=>
        {
            var layout=Room();var generator=new Generator(new(FixedLayout:layout,Coverage:80,SingleCellRatio:.4m));
            var blocks=generator.CreateComposition(layout)!;
            Check(blocks.Sum(p=>p.Shape.Length)==8);
            var level=new Level {Board=layout,Blocks=blocks.ToArray()}; Check(generator.MeetsComposition(level));
            Check(!generator.MeetsComposition(level with {Blocks=blocks.Select(p=>p with {Colors=[]}).ToArray()}));
        });
        Test("generation rejects invalid composition options and supports cancellation",()=>
        {
            foreach(var options in new GenerateOptions[]{new(Coverage:0),new(Coverage:101),new(SingleCellRatio:-.1m),new(SingleCellRatio:1.1m),new(MaxCandidates:0)})
                Reject(()=>new Generator(options).Run(_=>{}));
            using var stop=new CancellationTokenSource();stop.Cancel();
            var result=new Generator(new(FixedLayout:Room())).Run(_=>{},cancellation:stop.Token);
            Check(result.Attempts==0&&result.Levels.Count==0);
        });
        Test("candidate limit saves verified levels immediately and cancellation retains them",()=>
        {
            var layout=new Layout(6,6,[new(2,2)],
                [new("r",Side.Top,0,3,"red"),new("b",Side.Right,0,3,"blue"),new("y",Side.Bottom,3,3,"yellow"),new("g",Side.Left,3,3,"green")]);
            foreach(bool cancelAfterFirst in new[]{false,true})
            {
                using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var generator=new Generator(new(FixedLayout:layout,Coverage:65,SingleCellRatio:.4m,MaxCandidates:2,MinOpening:2));
                int saved=0;
                var result=generator.Run(_=>{},(level,index)=>
                {
                    Check(index==++saved);Check(generator.MeetsComposition(level));
                    new Engine(level).Replay(level.Solution);
                    if(cancelAfterFirst)stop.Cancel();
                },stop.Token);
                Check(saved==(cancelAfterFirst?1:2));Check(result.Levels.Count==saved);
                if(!cancelAfterFirst) Check(System.Text.Json.JsonSerializer.Serialize(result.Levels[0].Blocks,Files.Json)!=System.Text.Json.JsonSerializer.Serialize(result.Levels[1].Blocks,Files.Json));
            }
        });
        Test("level names sort by difficulty and keep a per-layout counter",()=>
        {
            Check(LevelNames.Stem(7,"sh_106",3)=="0007-sh_106-003"&&LevelNames.Stem(5000,"x",1)=="1000-x-001");
            Check(LevelNames.Layout(null)=="random"&&LevelNames.Layout("C:/a/my layout!.json")=="my_layout");
            Check(LevelNames.TryParse("0423-sh-106-012",out int d,out string l,out int c)&&d==423&&l=="sh-106"&&c==12);
            Check(!LevelNames.TryParse("level-001",out _,out _,out _));
            string folder=Path.Combine(Path.GetTempPath(),"blockpuzzle-names-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Check(LevelNames.NextCounter(folder,"room")==1);
                File.WriteAllText(Path.Combine(folder,"0900-room-004.svg"),"");File.WriteAllText(Path.Combine(folder,"0100-other-009.json"),"");
                Check(LevelNames.NextCounter(folder,"room")==5&&LevelNames.NextCounter(folder,"other")==10);
            }
            finally {Directory.Delete(folder,true);}
        });
        Test("difficulty is deterministic, bounded and ranks a trivial level low",()=>
        {
            var trivial=new Level {Board=new(3,3,[],[new("r",Side.Top,0,3,"red")]),Blocks=[new("a",[new(0,0)],new(0,0),["red"])]};
            var first=Difficulty.Rate(trivial);
            Check(first.Score==Difficulty.Rate(trivial).Score);
            Check(first.Score>=Difficulty.MinScore&&first.Score<=Difficulty.MaxScore);
            Check(first.Score<200,$"trivial level rated {first.Score}");
            var layout=new Layout(6,6,[new(2,2)],
                [new("r",Side.Top,0,3,"red"),new("b",Side.Right,0,3,"blue"),new("y",Side.Bottom,3,3,"yellow"),new("g",Side.Left,3,3,"green")]);
            var generated=new Generator(new(FixedLayout:layout,Coverage:65,SingleCellRatio:.4m,MinOpening:3)).Run(_=>{}).Levels[0];
            int rated=(int)generated.Analysis["difficulty"];
            Check(rated>first.Score,$"generated level {rated} not above trivial {first.Score}");
        });
        Console.WriteLine($"{count} tests passed.");
    }
}




