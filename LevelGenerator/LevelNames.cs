using System.Text.RegularExpressions;

namespace BlockPuzzle;

// Level files are named "<difficulty>-<layout>-<counter>", e.g. 0423-sh_106-001.json,
// so sorting by name sorts by difficulty. The counter is per layout and only grows.
public static class LevelNames
{
    public static readonly string[] Extensions = [".json", ".html", ".svg"];
    private static readonly Regex Name = new(@"^(\d{4})-(.+)-(\d{3,})$");

    public static string Layout(string? layoutPath)
    {
        if (string.IsNullOrWhiteSpace(layoutPath)) return "random";
        string name = Regex.Replace(Path.GetFileNameWithoutExtension(layoutPath), @"[^A-Za-z0-9_.]+", "_").Trim('_', '.');
        return name.Length == 0 ? "layout" : name;
    }

    public static string Stem(int difficulty, string layout, int counter) =>
        $"{Math.Clamp(difficulty, Difficulty.MinScore, Difficulty.MaxScore):0000}-{layout}-{counter:000}";

    public static bool TryParse(string stem, out int difficulty, out string layout, out int counter)
    {
        var match = Name.Match(stem);
        difficulty = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        layout = match.Success ? match.Groups[2].Value : "";
        counter = match.Success ? int.Parse(match.Groups[3].Value) : 0;
        return match.Success;
    }

    public static int NextCounter(string folder, string layout) =>
        Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => TryParse(Path.GetFileNameWithoutExtension(f), out _, out string l, out int c) && l == layout ? c : 0)
            .DefaultIfEmpty(0).Max() + 1;
}
