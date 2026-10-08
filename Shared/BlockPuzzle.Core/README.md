# Shared puzzle rules

This netstandard2.1 library (`BlockPuzzle.Core.csproj` → `BlockPuzzle.Core.dll`) owns the level JSON
model, serialization, board geometry and deterministic gameplay engine. It has no engine
dependencies; its only package is `System.Text.Json` 8.0.5. The .NET 10 `LevelGenerator` references
it, and the planned Unity client is meant to load the same compiled assembly.

Namespace `BlockPuzzle` and JSON versions are unchanged. Search, generation and HTML rendering
remain in `LevelGenerator`; the inspection CLI is `LevelGenerator.Tools`. See `../../LevelGenerator/FORMAT.md` for the level contract.

## Using it from Unity

Build Release; `bin/Release/netstandard2.1` then contains `BlockPuzzle.Core.dll` and its package
dependencies. Copy `BlockPuzzle.Core.dll`, `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`,
`Microsoft.Bcl.AsyncInterfaces.dll` and `System.Runtime.CompilerServices.Unsafe.dll` into the Unity
project's `Assets/Plugins`. Unity's .NET Standard 2.1 profile normally provides `System.Memory`,
`System.Buffers`, `System.Numerics.Vectors` and `System.Threading.Tasks.Extensions`; add those only if
Unity reports them missing, since duplicates cause conflicts. This has not yet been tried in a Unity
project, and IL2CPP builds may need a `link.xml` to keep the reflection-based JSON members.

The source is compiled with the latest C# but avoids features whose consumers need a newer compiler
than Unity's C# 9: `Level` uses `[JsonRequired]` rather than `required` members. `Polyfills.cs`
supplies `IsExternalInit` for records and `init` accessors.
