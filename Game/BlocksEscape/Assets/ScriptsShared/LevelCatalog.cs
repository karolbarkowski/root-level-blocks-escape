using System;
using UnityEngine;

namespace BlocksEscape
{
    // Every level in play order: level number n is Levels[n - 1]. Filled from Assets/Levels by LevelCatalogCollector
    // whenever level files change (sorted by file name), since builds can't list folders.
    // Progress is stored by position in this list, so reordering levels that players have already solved moves their
    // progress along with the positions.
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] TextAsset[] levels = Array.Empty<TextAsset>();

        public int Count => levels.Length;
        public TextAsset this[int index] => levels[index];

        public int IndexOf(TextAsset level) => Array.IndexOf(levels, level);

#if UNITY_EDITOR
        public void Set(TextAsset[] list) => levels = list;
#endif
    }
}
