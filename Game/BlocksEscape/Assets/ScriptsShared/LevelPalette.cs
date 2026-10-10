using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // Display colors for the color strings used by levels (gates and block layers).
    public static class LevelPalette
    {
        static readonly Dictionary<string, Color> Named = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "red", new Color(0.90f, 0.22f, 0.21f) },
            { "blue", new Color(0.20f, 0.45f, 0.90f) },
            { "green", new Color(0.26f, 0.70f, 0.31f) },
            { "yellow", new Color(0.98f, 0.82f, 0.18f) },
            { "orange", new Color(0.98f, 0.55f, 0.15f) },
            { "purple", new Color(0.58f, 0.32f, 0.80f) },
            { "pink", new Color(0.96f, 0.47f, 0.70f) },
            { "cyan", new Color(0.20f, 0.80f, 0.85f) },
            { "brown", new Color(0.55f, 0.36f, 0.20f) },
            { "white", new Color(0.95f, 0.95f, 0.95f) },
            { "black", new Color(0.12f, 0.12f, 0.12f) },
        };

        public static readonly Color Gray = new Color(0.55f, 0.55f, 0.55f);

        public static Color Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return Gray;
            if (Named.TryGetValue(name, out var named)) return named;
            if (ColorUtility.TryParseHtmlString(name, out var parsed)) return parsed;

            // Unknown custom color string: derive a stable hue from its name.
            int hash = 17;
            foreach (var ch in name) hash = hash * 31 + ch;
            return Color.HSVToRGB((hash & 0xffff) / 65535f, 0.65f, 0.9f);
        }
    }
}
