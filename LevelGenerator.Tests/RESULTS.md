# Local results

Measured on 9 September 2026 using .NET SDK 10.0.302, Release build, on the supplied Windows workspace. These are small engineering experiments, not a statistically powered benchmark or a player study.

> **Historical snapshot.** The commands below use the options and binary name of that time (`BlockPuzzle.dll`, `--seconds`, `--count`, `--blocks`); the current CLI is `LevelGenerator.dll` and rejects those options (see `../LevelGenerator/README.md`). The `output/` and `examples/` files referenced here were not kept in the repository, so the numbers cannot be reopened from these links.

## Delivered examples

Command at the time:

```powershell
dotnet bin/Release/net10.0/BlockPuzzle.dll generate --seconds 60 --seed 44 --out output/examples --count 3 --min-opening 4
```

The run exported three boards in approximately 60 seconds, including final verification. Each has ten pieces, including one permanent gray blocker, one layered piece, and three initially frozen pieces. Some properties combine on the same piece.

| Example | Occupied usable cells | Minimum drops to any removal event | Minimum drops to two full removals | Proven shortest solution | Directional pieces |
|---|---:|---:|---:|---:|---:|
| 001 | 82.86% | 6 | 7 | 15 | 2 |
| 002 | 82.86% | 6 | 7 | 15 | 2 |
| 003 | 70.59% | 7 | 9 | 16 | 3 |

For example 001, the exported shortest solution moves five distinct pieces before the first removal event. That is an observation about this solution, not a proof that every solution requires five distinct preparatory pieces. A* visited 212 states to establish its 15-drop optimum. Opening BFS visited 13 states before finding the first six-drop removal path.

Examples 001 and 002 are closely related positional variants, not substantially different design families. Example 003 has a different layout. Broader visual/mechanical diversity remains a useful next improvement.

All three JSON files passed the separate `verify` command. The first example's HTML viewer was checked in the browser: initial board, next-step control, and final solution step, where the gray block remains and all colored blocks are gone.

## Random reverse sampling versus optimization

At the time, [benchmark.ps1](benchmark.ps1) ran both methods for three seeds, with the same 15-second main-search budget per run, 6×6 boards, ten ordinary pieces, and a required opening depth of four. Specials are disabled to compare the underlying generation search. Postprocessing gets its own bounded solver/analysis calls, outside the main budget.

The current script keeps the seeds, methods and per-run budget, but composition now follows `--coverage`/`--single-cell-ratio` defaults instead of a fixed ten pieces, and each run is stopped externally at the budget. Its results are therefore not directly comparable with this table.

| Seed | Random reverse sampling | Reverse construction + annealing |
|---|---|---|
| 44 | No qualifying board found | Opening 5; shortest solution 17 |
| 45 | Opening 4; shortest solution 13 | Opening 6; shortest solution 15 |
| 71 | No qualifying board found | Opening 5; shortest solution 17 |

The optimized method produced a qualifying board in **3/3 runs**, versus **1/3** for random sampling. Every displayed opening and solution length in this table was proven exact by the respective searches. Lack of an export means the generator failed to find a board meeting the threshold in its budget, not that no such board exists.

The methods generate different candidate distributions and consume different numbers of random draws; identical seeds do not imply matched individual boards. Runs were sequential in one desktop session, without CPU isolation. This supports the default method as a practical choice for this configuration, but does not establish superiority over genetic algorithms, MCTS, or other implementations.

The current script writes raw configuration, logs, and measurements to `output/benchmark/summary.json` and sibling per-run directories. `run.json` records attempts and evaluations; mutation evaluations are much cheaper than constructing a fresh board, so equal attempt counts would not be an equivalent compute comparison.

## Fixed-layout mode

```powershell
dotnet bin/Release/net10.0/BlockPuzzle.dll generate --layout examples/layout.json --blocks 10 --seconds 25 --seed 44 --min-opening 4 --out output/fixed-layout
```

This produced a replay-verified board with an exact opening depth of six and a proven shortest solution of 16 drops. The exported walls and gates match the supplied layout.

## Validation scope

* Release build: zero warnings and errors.
* Sixteen mechanics/solver tests passed at the time (27 self-tests as of October 2026), including combined frozen/layered/directional behavior and the 20×20 state representation.
* The delivered examples and the fixed-layout example pass solution replay.
* The visual replay reaches the expected terminal board.

Dense 20×20 generation has not been performance-validated. The engine supports the dimensions; the results above apply to 6×6. The generator certifies an opening threshold and solvability, not human-perceived challenge or enjoyment.
