using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // One block type from the shared block-types.json. Levels refer to types by Id;
    // a gate accepts blocks whose active (outermost) type matches its own.
    public sealed class BlockType
    {
        public string Id { get; }
        public string Name { get; }
        public Color Color { get; }

        public BlockType(string id, string name, Color color)
        {
            Id = id;
            Name = name;
            Color = color;
        }
    }

    // The block types shared with the level generator and layout editor. The file lives in Shared/BlockTypes,
    // which the project references as a local package, so there is one copy for every tool.
    public sealed class BlockTypeSet
    {
        public const string Format = "block-types/v1";
        public const string PackagePath = "Packages/com.rootlevel.blocktypes/block-types.json";

        // Blocks with no type are neutral blockers that never leave the board.
        public static readonly Color NeutralColor = new Color(0.55f, 0.55f, 0.55f);

        [Serializable]
        class FileData
        {
            public string format;
            public TypeData[] types;
        }

        [Serializable]
        class TypeData
        {
            public string id;
            public string name;
            public string color;
        }

        readonly Dictionary<string, BlockType> _byId = new Dictionary<string, BlockType>(StringComparer.Ordinal);
        readonly List<BlockType> _all = new List<BlockType>();

        public IReadOnlyList<BlockType> All => _all;

        public static BlockTypeSet Parse(string json)
        {
            var file = JsonUtility.FromJson<FileData>(json);
            if (file == null || file.format != Format || file.types == null || file.types.Length == 0)
                throw new ArgumentException($"Block type file is missing or not '{Format}'.");

            var set = new BlockTypeSet();
            foreach (var t in file.types)
            {
                if (string.IsNullOrWhiteSpace(t.id) || set._byId.ContainsKey(t.id))
                    throw new ArgumentException("Block type ids must be unique and non-empty.");
                if (string.IsNullOrEmpty(t.color) || t.color.Length != 7 || !ColorUtility.TryParseHtmlString(t.color, out var color))
                    throw new ArgumentException($"Block type '{t.id}' needs a #RRGGBB color.");
                var type = new BlockType(t.id, t.name, color);
                set._byId.Add(type.Id, type);
                set._all.Add(type);
            }
            return set;
        }

        public bool TryGet(string id, out BlockType type)
        {
            type = null;
            return id != null && _byId.TryGetValue(id, out type);
        }

        public BlockType Get(string id) =>
            TryGet(id, out var type) ? type : throw new ArgumentException($"Unknown block type '{id}'. Add it to Shared/BlockTypes/block-types.json.");

        // Display color of a block's active type; neutral gray when it has none.
        public Color ColorOf(IReadOnlyList<string> types) => types != null && types.Count > 0 ? Get(types[0]).Color : NeutralColor;
    }
}
