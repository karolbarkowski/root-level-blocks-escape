using System.Net;
using System.Text;
using System.Text.Json;

namespace BlockPuzzle;

public static class Renderer
{
    private static string Escape(string s)=>WebUtility.HtmlEncode(s);
    private const string NeutralColor="#92999e";
    private static string Color(string type)=>BlockTypes.Get(type).Color;
    private static string Name(string type)=>BlockTypes.Get(type).Name;
    public static string Svg(Engine e,State s)
    {
        const int cell=48,pad=28;
        int w=e.Board.Width*cell+pad*2,h=e.Board.Height*cell+pad*2;
        var b=new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {w} {h}' role='img' aria-label='Puzzle board'>");
        b.Append($"<rect x='20' y='20' width='{w-40}' height='{h-40}' rx='9' fill='{(e.Board.BoundaryMode == BoundaryMode.Enclosed ? "#f0f1f2" : "#303b47")}'/>");
        for(int y=0;y<e.Board.Height;y++) for(int x=0;x<e.Board.Width;x++)
            b.Append($"<rect x='{pad+x*cell}' y='{pad+y*cell}' width='47' height='47' fill='{(e.Geometry.Floor[y*e.Board.Width+x] ? "#e5e7e8" : "#f0f1f2")}'/>");
        foreach(var c in e.Board.Walls)
            b.Append($"<rect x='{pad+c.X*cell}' y='{pad+c.Y*cell}' width='48' height='48' fill='#303b47'/>");
        foreach(var g in e.Board.Gates)
        {
            bool horizontal=g.Side is Side.Top or Side.Bottom;
            int x=g.Side switch { Side.Left=>pad-10,Side.Right=>pad+e.Board.Width*cell,_=>pad+g.Start*cell };
            int y=g.Side switch { Side.Top=>pad-10,Side.Bottom=>pad+e.Board.Height*cell,_=>pad+g.Start*cell };
            if (g.Line.HasValue) { if (horizontal) y=pad+g.Line.Value*cell; else x=pad+g.Line.Value*cell; }
            int thickness=g.Line.HasValue?cell:10;
            b.Append($"<rect x='{x}' y='{y}' width='{(horizontal?g.Length*cell:thickness)}' height='{(horizontal?thickness:g.Length*cell)}' rx='3' fill='{Color(g.Type)}'><title>{Escape(g.Id)}: {Escape(Name(g.Type))}; exits {g.Side}</title></rect>");
        }
        for(int i=0;i<e.Pieces.Length;i++)
        {
            if(s.Positions[i]<0)continue;
            var p=e.Pieces[i];var a=e.Decode(s.Positions[i]);
            string color=p.Types.Length==0?NeutralColor:Color(p.Types[s.Layers[i]]);
            bool frozen=s.Events<p.FrozenFor;
            b.Append($"<g><title>{Escape(p.Id)}: {Escape(p.Types.Length==0?"neutral blocker":string.Join(" → ",p.Types.Skip(s.Layers[i]).Select(Name)))}; {p.Axis}; frozen for {Math.Max(0,p.FrozenFor-s.Events)} more events</title>");
            var shape=p.Shape.ToHashSet();
            foreach(var c in p.Shape)
            {
                int x=pad+(a.X+c.X)*cell,y=pad+(a.Y+c.Y)*cell;
                b.Append($"<rect x='{x+1}' y='{y+1}' width='46' height='46' rx='4' fill='{color}'/>");
                if(shape.Contains(new(c.X+1,c.Y)))b.Append($"<rect x='{x+40}' y='{y+1}' width='15' height='46' fill='{color}'/>");
                if(shape.Contains(new(c.X,c.Y+1)))b.Append($"<rect x='{x+1}' y='{y+40}' width='46' height='15' fill='{color}'/>");
            }
            var label=p.Shape[0];int lx=pad+(a.X+label.X)*cell+24,ly=pad+(a.Y+label.Y)*cell+19;
            b.Append($"<text x='{lx}' y='{ly}' text-anchor='middle' font-family='system-ui,sans-serif' font-size='12' font-weight='700' fill='#172735'>{Escape(p.Id)}</text>");
            string mark=(p.Axis switch {Axis.Horizontal=>"↔",Axis.Vertical=>"↕",_=>""})+(frozen?$" ❄{p.FrozenFor-s.Events}":"");
            if(p.Types.Length-s.Layers[i]>1)mark+=$" /{p.Types.Length-s.Layers[i]}";
            b.Append($"<text x='{lx}' y='{ly+18}' text-anchor='middle' font-family='system-ui,sans-serif' font-size='13' fill='#172735'>{Escape(mark)}</text></g>");
        }
        return b.Append("</svg>").ToString();
    }
    public static void Write(Level level,string path)
    {
        var e=new Engine(level);var states=e.Replay(level.Solution);
        var frames=states.Select(s=>Svg(e,s)).ToArray();
        var captions=states.Select((s,i)=>i==0?"Initial board":$"Drop {i}: {level.Solution[i-1].Block} → ({level.Solution[i-1].To.X}, {level.Solution[i-1].To.Y}) · {s.Events} removal events").ToArray();
        string data=JsonSerializer.Serialize(new{frames,captions},Files.Json);
        string html="""
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Block puzzle · solution replay</title>
        <style>
        *{box-sizing:border-box}body{margin:0;background:#f5f3ee;color:#23303b;font:16px system-ui,sans-serif}
        main{max-width:1000px;margin:auto;padding:32px 24px}header{display:flex;justify-content:space-between;align-items:baseline;border-bottom:1px solid #d6d3cb;margin-bottom:20px}
        h1{font-size:26px;font-weight:600;letter-spacing:-1px}small{color:#647078}.layout{display:grid;grid-template-columns:minmax(280px,620px) 1fr;gap:32px}
        #board svg{display:block;width:100%;max-height:72vh}button{font:inherit;background:#23303b;color:white;border:0;border-radius:5px;padding:10px 17px;cursor:pointer}button:disabled{opacity:.35;cursor:default}
        .controls{display:flex;gap:10px;margin:20px 0}input{width:100%;accent-color:#23303b}aside p{line-height:1.6}dt{color:#68737a;font-size:13px;margin-top:18px}dd{margin:4px 0;font-size:20px}
        #caption{min-height:48px;font-size:14px}details{margin-top:24px;font-size:13px}pre{white-space:pre-wrap;overflow-wrap:anywhere}@media(max-width:680px){.layout{grid-template-columns:1fr}main{padding:16px}aside{border-top:1px solid #d6d3cb;padding-top:16px}}
        </style><main><header><h1>Block puzzle</h1><small>Verified solution replay</small></header><div class="layout"><section>
        <div id="board"></div><div class="controls"><button id="previous">← Back</button><button id="play">Play</button><button id="next">Next →</button><button id="reset">Reset</button></div>
        <input id="step" type="range" min="0" value="0" aria-label="Solution step"><p id="caption" aria-live="polite"></p></section><aside>
        <p>Move through the solution one drop at a time. Each step may include a drag around corners.</p>
        <dl><dt>Board</dt><dd>BOARD_SIZE</dd><dt>Solution</dt><dd>MOVE_COUNT drops</dd><dt>Seed</dt><dd>SEED_VALUE</dd></dl>
        <p>↔ / ↕ restrict movement.<br>❄ is the remaining freeze counter.<br>/2 means two type layers remain.<br>Gray (neutral) blocks stay on the board.</p>
        <p>Coordinates start at (0, 0) in the top-left corner. Hover over a piece for its type stack.</p>
        <details><summary>Generation measurements</summary><pre>METRICS</pre></details></aside></div></main>
        <script>const data=DATA_VALUE;let index=0,timer=null;
        const board=document.getElementById('board'),slider=document.getElementById('step'),caption=document.getElementById('caption');
        slider.max=data.frames.length-1;
        function draw(){board.innerHTML=data.frames[index];caption.textContent=data.captions[index];slider.value=index;document.getElementById('previous').disabled=index===0;document.getElementById('next').disabled=index===data.frames.length-1;}
        function stop(){clearInterval(timer);timer=null;document.getElementById('play').textContent='Play';}
        document.getElementById('previous').onclick=()=>{stop();index=Math.max(0,index-1);draw();};
        document.getElementById('next').onclick=()=>{stop();index=Math.min(data.frames.length-1,index+1);draw();};
        document.getElementById('reset').onclick=()=>{stop();index=0;draw();};slider.oninput=()=>{stop();index=+slider.value;draw();};
        document.getElementById('play').onclick=()=>{if(timer){stop();return;}if(index===data.frames.length-1)index=0;document.getElementById('play').textContent='Pause';timer=setInterval(()=>{index++;draw();if(index===data.frames.length-1)stop();},700);};draw();
        </script></html>
        """;
        html=html.Replace("BOARD_SIZE",$"{e.Board.Width} × {e.Board.Height}").Replace("MOVE_COUNT",level.Solution.Length.ToString())
            .Replace("SEED_VALUE",level.Seed.ToString()).Replace("METRICS",Escape(JsonSerializer.Serialize(level.Analysis,Files.Json))).Replace("DATA_VALUE",data);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,html);
    }
}
