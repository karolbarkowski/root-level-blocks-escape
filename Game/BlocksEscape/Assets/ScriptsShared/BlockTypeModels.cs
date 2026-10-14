using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // Game-only look of block types: per type id (from the shared block-types.json) the block prefabs, one per shape,
    // and the gate cell prefab. Blocks without a type (neutral blockers) use the neutral block prefabs.
    // Built by Blocks Escape/Animals/Build All Animal Prefabs from the models in Prefabs/Block/<Animal>.
    [CreateAssetMenu(menuName = "Blocks Escape/Block Type Models", fileName = "BlockTypeModels")]
    public sealed class BlockTypeModels : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string type;
            public Block[] blocks; // rotations are matched, mirror images are separate prefabs
            public GateCell gate;
        }

        [SerializeField] Entry[] entries = Array.Empty<Entry>();
        [SerializeField] Block[] neutralBlocks = Array.Empty<Block>();

        // Block prefabs for a block whose outermost remaining type is types[0].
        public IReadOnlyList<Block> BlocksOf(IReadOnlyList<string> types)
        {
            if (types == null || types.Count == 0) return neutralBlocks;
            return TryGet(types[0], out var entry) && entry.blocks != null ? entry.blocks : Array.Empty<Block>();
        }

        public GateCell GateOf(string type) => TryGet(type, out var entry) ? entry.gate : null;

        bool TryGet(string type, out Entry found)
        {
            foreach (var entry in entries)
            {
                if (entry.type != type) continue;
                found = entry;
                return true;
            }
            found = default;
            return false;
        }
    }
}
