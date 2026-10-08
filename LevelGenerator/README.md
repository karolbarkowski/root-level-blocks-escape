# Block Puzzle Level Generator

C# console generator for solvable colored-block sliding puzzles. The CLI (`LevelGenerator`, .NET 10) uses the shared rules library in the sibling `Shared/BlockPuzzle.Core` project (netstandard2.1). Keep the folders together:

```
root-level-blocks-escape/
├── LevelGenerator.slnx      solution: all .NET projects below
├── LevelGenerator/          this CLI → LevelGenerator.dll
├── LevelGenerator.Tests/    self-tests, benchmark script and historical results
├── LevelGenerator.Tools/    verify, solve, analyze and render saved levels → LevelGenerator.Tools.dll
├── Shared/BlockPuzzle.Core/ rules engine and JSON model → BlockPuzzle.Core.dll
└── LayoutEditor/            browser editor for fixed layouts (see its README)
```

## Build and run

From this `LevelGenerator` folder in PowerShell:

```powershell
.\build.ps1
dotnet bin/Release/net10.0/LevelGenerator.dll generate --layout layouts/sh_106.json --coverage 80 --single-cell-ratio 0.4 --maxCandidates 3 --out levels
```

`build.ps1` restores using the local `NuGet.Config` (nuget.org only), builds the whole solution in Release (Core, this CLI, LevelGenerator.Tools and the tests) and runs the self-tests. It temporarily points APPDATA at `.build-env` so NuGet configuration discovery stays inside the workspace, and restores it afterwards. Restore needs network access the first time because `BlockPuzzle.Core` references the `System.Text.Json` package.

All commands below assume this folder as the working directory; relative paths resolve against it.

Each qualifying level is saved immediately as JSON, an SVG and an HTML solution replay. Generation stops after the requested number of distinct verified levels. Existing level files are not overwritten; the next unused number is selected. Ctrl+C stops gracefully and retains completed files (exit code 130). If no qualifying puzzle exists for the requested settings, generation can continue indefinitely until cancelled.

## Options

| Option | Meaning | Default |
|---|---|---|
| `--coverage` | Minimum initial occupied percentage of playable floor; greater than 0, at most 100 | 80 |
| `--single-cell-ratio` | Fraction of initial blocks whose shape is 1x1; 0 through 1 | 0.4 |
| `--maxCandidates` | Verified levels to save before stopping; `--max-candidates` is an alias | 1 |
| `--min-opening` | Minimum drops before any removal event; 2 through 12 | 4 |
| `--layout` | Fixed walls/gates JSON, including its own canvas dimensions | generated layout |
| `--width`, `--height` | Canvas dimensions without a fixed layout; 2 through 20 | 6 |
| `--seed` | Random seed | 42 |
| `--specials` | Enable special-property decoration: true/false | true |
| `--method` | anneal or random | anneal |
| `--out` | Output folder | output |

`--blocks`, `--seconds`, `--count` and `--iterations` are no longer generation options and are rejected as unknown. There is no overall generation deadline. Individual candidate solver analyses remain bounded so one hard candidate cannot monopolize the run. LevelGenerator.Tools' solve/analyze commands retain their `--seconds`/`--states` budgets.

Coverage rounds up to whole cells, excluding walls, gate cells and exterior space. An 80% request on 38 playable cells requires at least 31 occupied cells. Composition planning prefers the smallest achievable area at or above the target.

For N initial blocks, the single-cell count is round(N * ratio), with halves rounded up. For ratio 0.4 and N=17 this is 7 blocks (41.18%); exact ratios are not always possible. Permanent blockers and pieces with special properties count by shape. Block count and larger shapes are chosen automatically.

Every distinct gate color appears on at least one initial block's outer layer. Duplicate gates of the same color do not require additional blocks. Shapes must have usable exits. Decoration preserves coverage, single-cell count and color representation, skipping modifiers that would violate them.

## More examples

```powershell
dotnet bin/Release/net10.0/LevelGenerator.dll generate --seed 44 --coverage 80 --single-cell-ratio 0.4 --maxCandidates 3 --out output/my-levels
dotnet bin/Release/net10.0/LevelGenerator.dll generate --specials false --min-opening 5 --maxCandidates 1 --out output/plain
```

Generation always validates its solution witness by replay before export; shortest solutions are used when proven within the per-candidate solver budget.

To check, re-solve, analyze or re-render a saved level, use `../LevelGenerator.Tools`. Self-tests and the method benchmark live in `../LevelGenerator.Tests`. Both have their own READMEs.

## Repository contents

| Path | Contents |
|---|---|
| `layouts/` | Fixed enclosed layouts for `--layout` (`simple.json`, `sh_106.json`) |
| `levels/` | Generated levels from `sh_106.json` plus their `run.json` |
| `level.schema.json`, `FORMAT.md` | Level JSON contract |
| `RESEARCH.md` | Design rationale; measurements are in `../LevelGenerator.Tests/RESULTS.md` |

Fixed layouts are drawn in `../LayoutEditor`; its `examples/enclosed-layout.json` is the recessed reference shape.

## Rules and file format

See `FORMAT.md` and `level.schema.json` for the full JSON contract.

- Pieces are connected relative grid cells with a fixed orientation. No overlap, pushing or rotation during play.
- One drag may turn corners through free placements while other pieces remain fixed. Drops, not path length, count as moves.
- The whole transverse span must fit a single matching gate, with a clear outward silhouette.
- Passing a gate while held does not remove anything. A qualifying drop automatically removes exactly one color layer.
- Colors are ordered outside to inside; the first entry is active. Final-layer removal frees the piece's cells.
- Horizontal/vertical restrictions permit both directions on their axis and also constrain exits.
- `frozenFor` counts all layer removals, including final removals, across the board.
- Empty colors mean a movable permanent blocker. Victory requires clearing colored blocks, not permanent blockers.

Without `boundaryMode`, a board keeps the legacy rectangular rules with perimeter gates. In `enclosed` mode, walls and gate cells close a boundary on an empty canvas. Four-neighbor flood fill identifies exterior space; pieces cannot occupy exterior or gate cells. Gates use `line` to specify their row/column inside the canvas. See `../LayoutEditor/examples/enclosed-layout.json` and the LayoutEditor project.

## Outputs and measurements

`run.json` records the latest invocation, saved filenames, attempts, elapsed time, exported count and completion/cancellation status. It is updated after each save and at shutdown. Each level's analysis reports requested/actual coverage and single-cell composition, solution length, solver status, opening bounds and generation provenance.

`opening.exact` is the shortest number of drops before any removal when known; `opening.lowerBound` is a proven bound even when the search is incomplete. `firstTwoFullRemovals` measures physical space creation. Witness-specific measurements are not necessarily minima over all solutions.

Seeds are useful for experiments, but bounded analysis and machine performance can affect selection. Saved JSON is the exact reproducible board. `RESEARCH.md` and `../LevelGenerator.Tests/RESULTS.md` document the earlier research and benchmarks; their measurements predate the current generation options.

## Game integration

The gameplay rules, JSON model and serialization live in `Shared/BlockPuzzle.Core`, which has no engine dependencies and targets netstandard2.1 so Unity can load it. An earlier Godot integration is not part of this repository; the planned game client is Unity. See `Shared/BlockPuzzle.Core/README.md` for which DLLs to copy into a Unity project.
