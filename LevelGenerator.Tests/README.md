# Level generator tests and benchmarks

Everything here checks or measures the generator; nothing in it participates in level generation.

| File | Purpose |
|---|---|
| `SelfTests.cs` | 27 rule, solver and generation self-tests; the project's entry point |
| `benchmark.ps1` | Compares the `random` and `anneal` generation methods |
| `RESULTS.md` | Historical measurements from September 2026 |

The project references `LevelGenerator`, whose `InternalsVisibleTo` exposes composition internals to the tests.

## Run the tests

`../LevelGenerator/build.ps1` restores, builds and runs them. To run them alone after a build, from this folder:

```powershell
dotnet bin/Release/net10.0/LevelGenerator.Tests.dll
```

Exit code 0 means every test passed; a failure prints `FAIL` with the message and exits with 1.

## Benchmark

Build first, then from this folder:

```powershell
.\benchmark.ps1 -Seconds 15
```

It runs both methods for seeds 44, 45 and 71 with specials disabled and the default composition options. Each run asks for one level and is stopped after `-Seconds`. Results go to `output/benchmark/summary.json`, with per-run console logs in sibling folders.
