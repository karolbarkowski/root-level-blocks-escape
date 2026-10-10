using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlockPuzzle;

// Edits block type ids in level and layout JSON files in place, keeping every other field as written.
static class TypeFiles
{
    // The v1 color names and the block types that replaced them in block-puzzle/v2.
    static readonly Dictionary<string, string> LegacyColors = new(StringComparer.Ordinal)
    {
        ["blue"] = "cow", ["purple"] = "pig", ["red"] = "animal3", ["yellow"] = "animal4",
        ["green"] = "animal5", ["orange"] = "animal6", ["cyan"] = "animal7",
    };

    static readonly JsonSerializerOptions Output = new() { WriteIndented = true };
    static readonly string[] NotLevels = ["run.json", "package.json", "block-types.json"];

    public static IEnumerable<string> Find(IEnumerable<string> paths) => paths
        .SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.json", SearchOption.AllDirectories) : [p])
        .Where(f => !NotLevels.Contains(Path.GetFileName(f)) && !f.EndsWith(".schema.json", StringComparison.Ordinal))
        .OrderBy(f => f, StringComparer.Ordinal);

    public static string Migrate(string path)
    {
        var root = Read(path, out var board);
        if (board == null) return "skipped, not a level or layout";
        if (root["format"] is JsonNode format)
        {
            if ((string?)format == Level.CurrentFormat) return "already " + Level.CurrentFormat;
            if ((string?)format != "block-puzzle/v1") throw new ArgumentException($"{path}: unsupported format '{format}'.");
            root["format"] = Level.CurrentFormat;
        }
        else if (Gates(board).Any(g => g.ContainsKey("type"))) return "already uses types";

        foreach (var gate in Gates(board))
            RenameKey(gate, "color", "type", value => Legacy(path, value));
        foreach (var block in Blocks(root))
            RenameKey(block, "colors", "types", value => new JsonArray(value!.AsArray().Select(c => Legacy(path, c)).ToArray()));
        if (root["analysis"]?["difficultyFeatures"] is JsonObject features)
        {
            RenameKey(features, "colorLayers", "typeLayers");
            RenameKey(features, "grayBlocks", "neutralBlocks");
        }
        Write(path, root);
        return "migrated";
    }

    public static int Rename(string path, string from, string to)
    {
        var root = Read(path, out var board);
        if (board == null) return 0;
        int count = 0;
        foreach (var gate in Gates(board))
            if ((string?)gate["type"] == from) { gate["type"] = to; count++; }
        foreach (var block in Blocks(root))
            if (block["types"] is JsonArray types)
                for (int i = 0; i < types.Count; i++)
                    if ((string?)types[i] == from) { types[i] = to; count++; }
        if (count > 0) Write(path, root);
        return count;
    }

    // A level keeps its layout under "board"; a layout file is the board itself.
    static JsonObject Read(string path, out JsonObject? board)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new ArgumentException($"{path}: not a JSON object.");
        board = root["board"] as JsonObject ?? (root.ContainsKey("width") && root.ContainsKey("gates") ? root : null);
        return root;
    }

    // Keeps the file's trailing newline, or its absence (generated levels have none, editor exports do).
    static void Write(string path, JsonObject root)
    {
        bool newline = File.ReadAllText(path).EndsWith("\n", StringComparison.Ordinal);
        File.WriteAllText(path, root.ToJsonString(Output) + (newline ? Environment.NewLine : ""));
    }

    static IEnumerable<JsonObject> Gates(JsonObject board) => (board["gates"] as JsonArray ?? []).OfType<JsonObject>();

    static IEnumerable<JsonObject> Blocks(JsonObject root) => (root["blocks"] as JsonArray ?? []).OfType<JsonObject>();

    static JsonNode Legacy(string path, JsonNode? color) =>
        LegacyColors.TryGetValue((string?)color ?? "", out var type) ? type
            : throw new ArgumentException($"{path}: color '{color}' has no block type; add it to the migrate mapping.");

    // Renames a property in place, keeping its position among the others.
    static void RenameKey(JsonObject o, string from, string to, Func<JsonNode?, JsonNode?>? map = null)
    {
        if (!o.ContainsKey(from)) return;
        var entries = o.ToList();
        o.Clear();
        foreach (var (key, value) in entries)
            o[key == from ? to : key] = key == from && map != null ? map(value) : value;
    }
}
