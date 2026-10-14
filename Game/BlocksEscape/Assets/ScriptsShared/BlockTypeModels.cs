using System;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // Game-only look of block types: the material a block of that type is drawn with.
    // Type ids come from the shared block-types.json; types without an entry are tinted with their color instead.
    [CreateAssetMenu(menuName = "Blocks Escape/Block Type Materials", fileName = "BlockTypeMaterials")]
    public sealed class BlockTypeMaterials : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string type;
            public Material material;
        }

        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        public Material Get(string type)
        {
            foreach (var entry in entries)
                if (entry.type == type) return entry.material;
            return null;
        }
    }
}
