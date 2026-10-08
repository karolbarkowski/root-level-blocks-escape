# Level generator tools

Command-line tools for checking and inspecting saved levels. They do not generate levels; that is `../LevelGenerator`. The project references `LevelGenerator` for its solver (`Search`) and replay renderer (`Renderer`).

`../LevelGenerator/build.ps1` builds this project with the rest of the solution. From this folder:

```powershell
dotnet bin/Release/net10.0/LevelGenerator.Tools.dll verify ../LevelGenerator/levels/level-001.json
dotnet bin/Release/net10.0/LevelGenerator.Tools.dll solve ../LevelGenerator/levels/level-001.json --seconds 30 --states 100000 --out output/shortest.json
dotnet bin/Release/net10.0/LevelGenerator.Tools.dll analyze ../LevelGenerator/levels/level-001.json --seconds 10 --states 100000
dotnet bin/Release/net10.0/LevelGenerator.Tools.dll render output/shortest.json --out output/shortest.html
```

| Command | What it does | Options |
|---|---|---|
| `verify` | Replays the level's stored solution and confirms it clears every colored block | none |
| `solve` | Searches for a shortest solution; writes the level with it plus an HTML replay | `--seconds` 30, `--states` 100000, `--out` (default `<input>.solved.json`) |
| `analyze` | Measures drops to the first removal event and to the first two full removals | `--seconds` 10, `--states` 100000, `--out` to save the level with these measurements |
| `render` | Writes an HTML replay of the stored solution | `--out` (default `<input>.html`) |

Exit codes: 0 success, 1 invalid input or illegal solution, 2 when `solve` returns no solution. A solver timeout is not proof of unsolvability; inspect the printed status.
