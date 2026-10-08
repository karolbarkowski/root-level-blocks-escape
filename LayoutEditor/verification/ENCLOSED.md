# Enclosed layout verification

- 19 editor model tests and 22 generator mechanics tests passed.
- Release build completed without warnings or errors.
- Browser checks covered the 44-cell recessed example, inset gate placement, gate length editing, leak detection, inverted exit directions, undo/redo, and JSON download.
- The actual browser download is `enclosed-export.json`. Its board matches `enclosed/level-001.json` exactly.
- The generated example has 10 blocks, a replay-verified 16-drop solution, and an exact seven-drop first-removal opening. Legacy example replay also passed.

Historical generation command. It predates the current generator options (`--blocks` and `--seconds` were removed) and the folder renames (`Generator` → `LevelGenerator`, `Editor` → `LayoutEditor`):

```powershell
dotnet Generator/bin/Release/net10.0/BlockPuzzle.dll generate --layout Generator/examples/enclosed-layout.json --blocks 10 --seconds 30 --seed 17 --min-opening 2 --out Editor/verification/enclosed
dotnet Generator/bin/Release/net10.0/BlockPuzzle.dll verify Editor/verification/enclosed/level-001.json
```

To re-verify the saved level with the current build (from `LevelGenerator.Tools`):

```powershell
dotnet bin/Release/net10.0/LevelGenerator.Tools.dll verify ../LayoutEditor/verification/enclosed/level-001.json
```

The shape is a layout example, not a promise that every requested block count succeeds. An 18-block run found no complete candidate in 60 seconds. The generator reports failure rather than exporting an unverified fallback.
