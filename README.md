# Blocks Escape · level pipeline

Tools for producing solvable block-type sliding puzzles: draw a layout, generate verified levels for it, rate their difficulty, and load them in the game through a shared rules library.

| Folder | What it is | Output |
|---|---|---|
| `LevelGenerator/` | Console generator (.NET 10) | `LevelGenerator.dll` |
| `LevelGenerator.Tools/` | Inspect saved levels: verify, solve, analyze, render, rate | `LevelGenerator.Tools.dll` |
| `LevelGenerator.Tests/` | Self-tests, method benchmark, historical results | `LevelGenerator.Tests.dll` |
| `Shared/BlockPuzzle.Core/` | Rules engine and JSON model, shared with the game (netstandard2.1) | `BlockPuzzle.Core.dll` |
| `Shared/BlockTypes/` | The block types (`block-types.json`) used by the generator, layout editor and game | — |
| `LayoutEditor/` | Browser editor for fixed layouts (Node.js 20+) | layout JSON |

`LevelGenerator.slnx` contains the four .NET projects. Keep the folders together; projects reference each other by relative path.

## Block types

Gates and blocks use **block types** (eventually one per animal) instead of colors. They are listed once in [Shared/BlockTypes/block-types.json](Shared/BlockTypes/block-types.json), each with an `id` (stored in levels and layouts), a display `name` and a `#RRGGBB` `color`:

- The generator embeds the file in `BlockPuzzle.Core` at build time, so rebuild after editing it. Levels and layouts with an unknown type are rejected.
- The layout editor's server publishes it as `/block-types.json` for the gate type picker.
- The game references the folder as a local package (`com.rootlevel.blocktypes` in `Packages/manifest.json`); the Board prefab points at its `block-types.json`.

To rename a type, change its id in the file, rebuild, then update existing files with `rename-type` (see [Level tools](#level-tools)). Files from before block types (`block-puzzle/v1`, with `color`/`colors`) convert with `migrate`.

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
   dotnet bin/Release/net10.0/LevelGenerator.dll generate --layout layouts/6x6_rect_5types.json --maxCandidates 3 --out levels
   ```
3. Sort `levels/` by name to see them in difficulty order. Open the `.html` files to watch the solution replay.

## Generating levels

Each verified level is saved immediately as JSON, an SVG preview and an HTML solution replay. Generation stops after `--maxCandidates` distinct levels; there is no time limit, and settings with no qualifying puzzle run until cancelled. Ctrl+C stops gracefully, keeping saved files (exit code 130). Existing files are never overwritten.

| Option | Meaning | Default |
|---|---|---|
| `--layout` | Fixed walls/gates JSON, including its canvas size | generated layout |
| `--width`, `--height` | Canvas size without a layout; 2–20 | 6 |
| `--coverage` | Minimum % of playable floor occupied at the start; (0, 100] | 80 |
| `--single-cell-ratio` | Share of blocks that are 1×1; 0–1 | 0.2 |
| `--block-size` | Target average cells per block; 1–4. Lower means more, smaller blocks | 2.8 |
| `--min-opening` | Minimum drops before anything can be removed; 2–12 | 3 |
| `--maxCandidates` | Levels to save before stopping (alias `--max-candidates`) | 1 |
| `--specials` | Add frozen, layered, directional and neutral (gray) blocks: true/false | true |
| `--method` | `anneal` or `random` | anneal |
| `--seed` | Random seed | 42 |
| `--out` | Output folder | output |

- Coverage rounds up to whole cells and counts only playable floor (not walls, gates or outside space). 80% of 38 cells means at least 31.
- With N blocks, round(N × ratio) are 1×1, halves rounded up. The block count is steered towards covered area ÷ `--block-size`. Shapes are picked from 1–4-cell pieces in every rotation and mirror image. Targets above 3 force nearly every block to 4 cells, which barely move in narrow layouts.
- Every gate type appears on at least one block's outer layer.
- Specials stay occasional, as in the genre: one neutral blocker, one layered block, and at most 1 + blocks ÷ 8 frozen and 1 + blocks ÷ 8 directional blocks. They never break coverage, the 1×1 count or type representation.
- Every exported solution is checked by replay. The solver first tries to prove a shortest solution. If its budget runs out, a faster weighted search finds a short one instead, and `analysis.solutionOptimal` is false.
- If a run seems stuck, check the progress line, e.g. `best opening 2 (need 3)`. It means the layout can't reach `--min-opening`; lower it.
- Seeds help experiments, but machine speed affects bounded searches; the saved JSON is the reproducible artifact.

`run.json` in the output folder records the invocation, saved files, attempts, timing and whether the run completed. Each level's `analysis` holds composition, solver status, opening bounds (`opening`, `firstTwoFullRemovals`) and the difficulty rating.

### Sample layouts

`LevelGenerator/layouts/` holds 120 enclosed layouts written by `make-layouts.mjs` (`node make-layouts.mjs` from `LevelGenerator/` rewrites them). File names are `<width>x<height>_<shape>_<N>types.json`: the floor's bounding box (the canvas adds one wall cell on every side), the shape, and how many block types the gates use. Each shape comes in five sizes between 5×5 and 8×10, each with 4, 5, 6 and 7 types:

| Shape | Sizes | What it is |
|---|---|---|
| `rect` | 5x5, 5x7, 6x6, 7x8, 8x10 | plain rectangle |
| `notched` | 5x6, 6x7, 6x8, 7x9, 8x10 | corners cut by 1 cell (2 from 7×7 up) |
| `pillars` | 5x7, 6x7, 7x7, 7x9, 8x9 | single-cell interior pillars forming lanes |
| `tworoom` | 5x7, 6x8, 7x9, 8x9, 8x10 | two rooms joined by a narrower waist |
| `recessed` | 6x8, 7x7, 7x8, 8x9, 8x10 | room one cell smaller on every side; every gate sits at the end of a 1-cell alcove |
| `mixed` | 6x8 (6x9 for 7 types), 7x9, 7x8, 8x8, 8x10 | alcove gates top and bottom, cut corners, centre pillar |

Gates sit on the outline. There are about one per 4.5 perimeter cells, and never fewer than the type count. They are 2 cells wide where a side has room, and neighbouring gates get different types. The type set rotates through `block-types.json` from layout to layout, so every animal appears. A few layouts use another rotation or one gate more or fewer (`OVERRIDES` in the script), because their default gates couldn't reach the generator's default forced opening. Smaller recessed and mixed boards were dropped for the same reason.

Every layout generates levels with the default options. `generate-all.mjs` fills `levels/<layout>/` with 10 levels per layout, retrying slow layouts with fresh seeds and longer limits and deleting duplicate levels. Difficulty of those 1,200 levels:

| Shape | Difficulty range | Median |
|---|---|---:|
| `rect` | 130–684 | 294 |
| `notched` | 121–735 | 330 |
| `pillars` | 139–666 | 272 |
| `tworoom` | 121–760 | 270 |
| `recessed` | 111–741 | 344 |
| `mixed` | 135–682 | 289 |

Most layouts produce a level within seconds. Pillars and mixed boards with 6–7 types on 7×9–8×9 can take 10+ minutes per level: narrow lanes and alcoves leave few ways to keep blocks away from their exits.

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
| 15% | Size: total type layers to clear |
| 10% | Special mechanics: kinds used and share of special blocks |
| 10% | The player's moves per type layer when it succeeds |

The same level always gets the same score. With the default settings, the sample layouts score about 170–590 (table above). Higher scores need bigger, more crowded boards or a deeper `--min-opening`; a crowded 7×7 board with a seven-drop forced opening scored 788. The weights in `LevelGenerator/Difficulty.cs` are first estimates; calibrate them against playtest data. Every input is stored in `analysis.difficultyFeatures`, and `rate` (below) re-scores and renames existing files after a change.

## Level tools

From `LevelGenerator.Tools/`, as `dotnet bin/Release/net10.0/LevelGenerator.Tools.dll <command>`:

| Command | What it does | Options |
|---|---|---|
| `verify <level>` | Replays the stored solution and confirms it clears every typed block | none |
| `solve <level>` | Searches for a shortest solution; writes the level with it plus an HTML replay | `--seconds` 30, `--states` 100000, `--out` (default `<input>.solved.json`) |
| `analyze <level>` | Measures drops to the first removal and to two full removals | `--seconds` 10, `--states` 100000, `--out` to save |
| `render <level>` | Writes an HTML replay | `--out` (default `<input>.html`) |
| `rate <files or folders>` | Recomputes difficulty, refreshes the HTML replay and renames the JSON/HTML/SVG files to the new score | none |
| `migrate <files or folders>` | Converts `block-puzzle/v1` levels and layouts from colors to block types, in place | none |
| `rename-type <old> <new> <files or folders>` | Renames a block type id in levels and layouts, in place; `<new>` must already be in `block-types.json` | none |

Exit codes: 0 success, 1 invalid input or illegal solution, 2 when `solve` finds no solution. A solver timeout does not prove a level unsolvable.

## Rules

- Blocks are connected grid cells with a fixed orientation: no overlap, pushing or rotation.
- One drag may turn corners through free space while other blocks stay put. Each drop counts as one move, however long the drag.
- A block leaves through a gate of its active type when its whole width fits the gate and its outward path is clear. Merely passing a gate during a drag does nothing; dropping next to it removes exactly one type layer.
- Types are listed outside to inside. Removing the last layer frees the block's cells.
- **Directional** blocks move only horizontally or vertically (both ways) and exit only along that axis.
- **Frozen** blocks (`frozenFor`) can't move until that many layers have been removed anywhere on the board.
- **Neutral** (gray) blocks have no type and are permanent movable obstacles; winning means clearing every typed block.
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
3. Pick a **gate block type** and **exit direction**, then drag a straight span. Gates replace walls and the arrow points outward. Click a gate to edit its ID, block type, direction, Line, Start and Length. For left/right gates, Line is the column and Start the first row; for top/bottom gates, Line is the row and Start the first column.
4. Fix anything the validation panel reports. Overlapping or misoriented gates, duplicate IDs and an open boundary block export. Missing gates and disconnected floor are warnings only.
5. **Export JSON** downloads the layout; pass it to `generate --layout`.

Undo/redo keeps 100 edits, with a drag stroke counted as one. Resizing keeps coordinates and removes whole out-of-bounds gates after confirmation. The current draft is kept in browser storage, but only Export produces a durable file. Shortcuts: **1** wall, **2** erase, **3** gate, **4** select, **Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** undo/redo, **Ctrl+O** open, **Ctrl+S** export, **Delete** removes the selected gate. Arrow keys move a focused cell, and Enter/Space paints it. **Load example layout** opens `examples/enclosed-layout.json`. Gate types must be in `block-types.json`; layouts from before block types must be converted with `migrate` first. Legacy rectangular files keep their geometry and can be converted with the Boundary mode selector.

Code: `model.js` holds pure layout logic and validation, `app.js` the UI, `server.mjs` the localhost-only server.

## Testing

```powershell
dotnet LevelGenerator.Tests/bin/Release/net10.0/LevelGenerator.Tests.dll   # 31 self-tests; build.ps1 runs them too
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
