# Blocks Escape · level pipeline

Tools for producing solvable colored-block sliding puzzles: draw a layout, generate verified levels for it, rate their difficulty, and load them in the game through a shared rules library.

| Folder | What it is | Output |
|---|---|---|
| `LevelGenerator/` | Console generator (.NET 10) | `LevelGenerator.dll` |
| `LevelGenerator.Tools/` | Inspect saved levels: verify, solve, analyze, render, rate | `LevelGenerator.Tools.dll` |
| `LevelGenerator.Tests/` | Self-tests, method benchmark, historical results | `LevelGenerator.Tests.dll` |
| `Shared/BlockPuzzle.Core/` | Rules engine and JSON model, shared with the game (netstandard2.1) | `BlockPuzzle.Core.dll` |
| `LayoutEditor/` | Browser editor for fixed layouts (Node.js 20+) | layout JSON |

`LevelGenerator.slnx` contains the four .NET projects. Keep the folders together; projects reference each other by relative path.

Reference documents: [FORMAT.md](LevelGenerator/FORMAT.md) and [level.schema.json](LevelGenerator/level.schema.json) (level JSON contract), [RESEARCH.md](LevelGenerator/RESEARCH.md) (generation method), [RESULTS.md](LevelGenerator.Tests/RESULTS.md) (September 2026 measurements, using options that no longer exist).

## Build

```powershell
.\LevelGenerator\build.ps1
```

This restores packages from nuget.org, builds the solution in Release and runs the self-tests. Network access is needed the first time because `BlockPuzzle.Core` references `System.Text.Json`. The script temporarily points APPDATA at `LevelGenerator/.build-env` so NuGet configuration stays inside the workspace.

## Typical workflow

1. Draw a layout in the [layout editor](#layout-editor) and save the export to `LevelGenerator/layouts/`.
2. Generate levels, from `LevelGenerator/`:
   ```powershell
   dotnet bin/Release/net10.0/LevelGenerator.dll generate --layout layouts/simple.json --coverage 80 --maxCandidates 3 --out levels
   ```
3. Sort `levels/` by name to see them in difficulty order. Open the `.html` files to watch the solution replay.

## Generating levels

Each verified level is saved immediately as JSON, an SVG preview and an HTML solution replay. Generation stops after `--maxCandidates` distinct levels; there is no time limit, and settings with no qualifying puzzle run until cancelled. Ctrl+C stops gracefully, keeping saved files (exit code 130). Existing files are never overwritten.

| Option | Meaning | Default |
|---|---|---|
| `--layout` | Fixed walls/gates JSON, including its canvas size | generated layout |
| `--width`, `--height` | Canvas size without a layout; 2–20 | 6 |
| `--coverage` | Minimum % of playable floor occupied at the start; (0, 100] | 80 |
| `--single-cell-ratio` | Share of blocks that are 1×1; 0–1 | 0.4 |
| `--min-opening` | Minimum drops before anything can be removed; 2–12 | 4 |
| `--maxCandidates` | Levels to save before stopping (alias `--max-candidates`) | 1 |
| `--specials` | Add frozen, layered, directional and gray blocks: true/false | true |
| `--method` | `anneal` or `random` | anneal |
| `--seed` | Random seed | 42 |
| `--out` | Output folder | output |

- Coverage rounds up to whole cells and counts only playable floor (not walls, gates or outside space). 80% of 38 cells means at least 31.
- With N blocks, round(N × ratio) are 1×1, halves rounded up. Block count and larger shapes are chosen automatically.
- Every gate color appears on at least one block's outer layer. Special blocks never break coverage, the 1×1 count or color representation.
- Every exported solution is checked by replay. It is the shortest solution only when the solver proves that within its per-level budget, which large boards rarely allow.
- Seeds help experiments, but machine speed affects bounded searches; the saved JSON is the reproducible artifact.

`run.json` in the output folder records the invocation, saved files, attempts, timing and whether the run completed. Each level's `analysis` holds composition, solver status, opening bounds (`opening`, `firstTwoFullRemovals`) and the difficulty rating.

### Difficulty and file names

Files are named `<difficulty>-<layout>-<counter>`, for example `0423-simple-001.json`:

- **Difficulty**: 0001–1000, four digits, so name order is difficulty order. Also stored as `analysis.difficulty`.
- **Layout**: the `--layout` file name, or `random`.
- **Counter**: per layout and never reused, so equal scores never collide.

The score doesn't rely on a shortest solution:

| Weight | Measurement |
|---:|---|
| 30% | How often a simulated weak player fails (24 seeded playouts: usually takes an available exit, otherwise picks among a few sampled drops, with a move cap) |
| 15% | How little of the board that player clears on average |
| 20% | Forced opening: drops before the first removal and before two full removals |
| 15% | Size: total color layers to clear |
| 10% | Special mechanics: kinds used and share of special blocks |
| 10% | The player's moves per color layer when it succeeds |

The same level always gets the same score. In local tests, plain 5×5 boards scored about 30–60, typical boards with specials 170–360, and a crowded 7×7 board with a seven-drop forced opening 788. The weights in `LevelGenerator/Difficulty.cs` are first estimates; calibrate them against playtest data. Every input is stored in `analysis.difficultyFeatures`, and `rate` (below) re-scores and renames existing files after a change.

## Level tools

From `LevelGenerator.Tools/`, as `dotnet bin/Release/net10.0/LevelGenerator.Tools.dll <command>`:

| Command | What it does | Options |
|---|---|---|
| `verify <level>` | Replays the stored solution and confirms it clears every colored block | none |
| `solve <level>` | Searches for a shortest solution; writes the level with it plus an HTML replay | `--seconds` 30, `--states` 100000, `--out` (default `<input>.solved.json`) |
| `analyze <level>` | Measures drops to the first removal and to two full removals | `--seconds` 10, `--states` 100000, `--out` to save |
| `render <level>` | Writes an HTML replay | `--out` (default `<input>.html`) |
| `rate <files or folders>` | Recomputes difficulty, refreshes the HTML replay and renames the JSON/HTML/SVG files to the new score | none |

Exit codes: 0 success, 1 invalid input or illegal solution, 2 when `solve` finds no solution. A solver timeout does not prove a level unsolvable.

## Rules

- Blocks are connected grid cells with a fixed orientation: no overlap, pushing or rotation.
- One drag may turn corners through free space while other blocks stay put. Each drop counts as one move, however long the drag.
- A block leaves through a gate of its active color when its whole width fits the gate and its outward path is clear. Merely passing a gate during a drag does nothing; dropping next to it removes exactly one color layer.
- Colors are listed outside to inside. Removing the last layer frees the block's cells.
- **Directional** blocks move only horizontally or vertically (both ways) and exit only along that axis.
- **Frozen** blocks (`frozenFor`) can't move until that many layers have been removed anywhere on the board.
- **Gray** blocks (no colors) are permanent movable obstacles; winning means clearing every colored block.
- Boards are either legacy rectangles with gates on the edge, or `enclosed`: walls and gates drawn on an empty canvas, where everything reachable from the canvas edge is outside space that blocks can't enter.

## Layout editor

A local web app with no build step or dependencies. The Node server only serves files; editing, validation and export run in the browser.

```powershell
cd LayoutEditor
node server.mjs
```

Open http://127.0.0.1:8770 (set `PORT` to change it). `start.ps1` and `npm start` run the same server; any static HTTP server works too, but not opening `index.html` as a file.

1. Start a **New layout**, or **Open JSON** / drag a file in (a full generated level works; only its board is kept).
2. **Draw walls** by clicking or dragging. Close a boundary with walls and gates: enclosed floor turns white. Shift-drag, right-drag or the Erase tool erases.
3. Pick a **gate color** and **exit direction**, then drag a straight span. Gates replace walls and the arrow points outward. Click a gate to edit its ID, color, direction, Line, Start and Length. For left/right gates, Line is the column and Start the first row; for top/bottom gates, Line is the row and Start the first column.
4. Fix anything the validation panel reports. Overlapping or misoriented gates, duplicate IDs and an open boundary block export. Missing gates and disconnected floor are warnings only.
5. **Export JSON** downloads the layout; pass it to `generate --layout`.

Undo/redo keeps 100 edits, with a drag stroke counted as one. Resizing keeps coordinates and removes whole out-of-bounds gates after confirmation. The current draft is kept in browser storage, but only Export produces a durable file. Shortcuts: **1** wall, **2** erase, **3** gate, **4** select, **Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** undo/redo, **Ctrl+O** open, **Ctrl+S** export, **Delete** removes the selected gate. Arrow keys move a focused cell, and Enter/Space paints it. **Load example layout** opens `examples/enclosed-layout.json`. Custom color strings are kept exactly. Legacy rectangular files keep their geometry and can be converted with the Boundary mode selector.

Code: `model.js` holds pure layout logic and validation, `app.js` the UI, `server.mjs` the localhost-only server.

## Testing

```powershell
dotnet LevelGenerator.Tests/bin/Release/net10.0/LevelGenerator.Tests.dll   # 29 self-tests; build.ps1 runs them too
cd LayoutEditor; node --test tests/*.test.mjs                               # 19 editor tests
.\LevelGenerator.Tests\benchmark.ps1 -Seconds 15                            # random vs anneal
```

The self-tests cover rules, solver bounds, enclosed boards, composition, cancellation, file naming and difficulty. They can see the generator's internal composition code (`InternalsVisibleTo`). The editor tests use the fixtures in `LayoutEditor/verification/`. The benchmark runs both methods for seeds 44, 45 and 71 without specials, stops each run after `-Seconds`, and writes `output/benchmark/summary.json` in `LevelGenerator.Tests/`.

## Using BlockPuzzle.Core in Unity

The game is planned in Unity, so the rules library targets netstandard2.1. After a Release build, copy these files from `Shared/BlockPuzzle.Core/bin/Release/netstandard2.1/` into `Assets/Plugins`:

- `BlockPuzzle.Core.dll`
- `System.Text.Json.dll`
- `System.Text.Encodings.Web.dll`
- `Microsoft.Bcl.AsyncInterfaces.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`

Unity normally provides `System.Memory`, `System.Buffers`, `System.Numerics.Vectors` and `System.Threading.Tasks.Extensions`. Copy those too only if Unity reports them missing, since duplicates conflict.

This hasn't been tried in a Unity project yet. IL2CPP builds may need a `link.xml` to keep the JSON members that are accessed through reflection.

The library is compiled with the latest C#, but it avoids anything Unity's C# 9 compiler can't consume. For example, `Level` uses `[JsonRequired]` instead of `required`, and `Polyfills.cs` supplies `IsExternalInit`.
