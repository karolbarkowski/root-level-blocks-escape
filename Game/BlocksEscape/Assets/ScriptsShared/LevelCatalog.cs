using System;
using System.Text;
using UnityEngine;

namespace BlocksEscape
{
    // Every level in play order: level number n is level index n - 1. The levels are packed into one binary asset
    // (Levels/LevelPack.bytes) by LevelCatalogCollector whenever the level files in Assets/Levels change (sorted by file
    // name), since builds can't list folders. The pack holds each level's JSON minified and without the solution and
    // analysis the editor tools use, so the game keeps a few megabytes in memory instead of every level file, and reads
    // one level's text out of it when that level starts.
    // Progress is stored by position in this list, so reordering levels that players have already solved moves their
    // progress along with the positions.
    //
    // Pack layout, little endian: int32 level count, then count + 1 int32 offsets into the data that follows (level i is
    // the bytes from offset i up to offset i + 1), then every level's JSON as UTF-8.
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] TextAsset pack;

        int _count = -1;

        public int Count
        {
            get
            {
                if (_count < 0) _count = pack != null && pack.dataSize >= 4 ? ReadInt(pack.GetData<byte>(), 0) : 0;
                return _count;
            }
        }

        // The JSON of the level at this index (LevelData.Parse reads it).
        public string Json(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            var data = pack.GetData<byte>(); // a view of the asset's bytes, not a copy
            int header = 4 + 4 * (Count + 1);
            int start = header + ReadInt(data, 4 + 4 * index);
            int end = header + ReadInt(data, 4 + 4 * (index + 1));
            return Encoding.UTF8.GetString(data.GetSubArray(start, end - start).AsReadOnlySpan());
        }

        static int ReadInt(Unity.Collections.NativeArray<byte> data, int at) =>
            data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24;

        void OnEnable() => _count = -1; // the pack may have been rebuilt

#if UNITY_EDITOR
        public void Set(TextAsset levelPack)
        {
            pack = levelPack;
            _count = -1;
        }
#endif
    }
}
