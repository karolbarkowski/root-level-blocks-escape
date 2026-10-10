using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlockPuzzle;

// One block type from Shared/BlockTypes/block-types.json. Level files store the Id: a gate has one type and
// accepts blocks whose active (outermost remaining) type matches it. Color is a #RRGGBB display color for tools.
public sealed record BlockType(string Id, string Name, string Color);

// The shared block types, embedded into this assembly at build time so every tool reads the same list.
public static class BlockTypes
{
    public const string Format = "block-types/v1";
    private sealed record TypeFile(string Format, BlockType[] Types);

    // Unknown fields are allowed: the game will add visual data the generator does not need.
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, BlockType> ById;

    public static IReadOnlyList<BlockType> All { get; }

    static BlockTypes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("block-types.json")
            ?? throw new InvalidOperationException("block-types.json is not embedded in BlockPuzzle.Core.");
        All = Parse(new StreamReader(stream).ReadToEnd());
        ById = All.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }

    public static bool IsKnown(string? id) => id != null && ById.ContainsKey(id);

    public static BlockType Get(string id) =>
        ById.TryGetValue(id, out var type) ? type : throw new ArgumentException($"Unknown block type '{id}'.");

    public static BlockType[] Parse(string json)
    {
        var file = JsonSerializer.Deserialize<TypeFile>(json, Json) ?? throw new ArgumentException("Empty block type file.");
        if (file.Format != Format) throw new ArgumentException($"Block type file must have format '{Format}'.");
        if (file.Types == null || file.Types.Length == 0) throw new ArgumentException("Block type file lists no types.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in file.Types)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Id) || t.Id.Trim() != t.Id || !ids.Add(t.Id))
                throw new ArgumentException("Block type ids must be unique, non-empty and without surrounding spaces.");
            if (string.IsNullOrWhiteSpace(t.Name) || t.Color == null || !Regex.IsMatch(t.Color, "^#[0-9A-Fa-f]{6}$"))
                throw new ArgumentException($"Block type '{t.Id}' needs a name and a #RRGGBB color.");
        }
        return file.Types;
    }
}
