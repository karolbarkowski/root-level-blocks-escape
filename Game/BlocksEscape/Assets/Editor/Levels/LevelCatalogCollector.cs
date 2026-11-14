using System;
using System.IO;
using System.Linq;
using System.Text;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Keeps Levels/LevelCatalog.asset in step with the Levels folder: whenever a level file (Assets/Levels/*.json) is
    // added, changed, removed or moved, every level file there, sorted by file name, is packed into Levels/LevelPack.bytes
    // (see LevelCatalog for the layout), which is what the game ships and loads. Each level goes in minified and without
    // its solution and analysis, which only the editor tools read (from the level files themselves).
    sealed class LevelCatalogCollector : AssetPostprocessor
    {
        const string Root = "Assets/Levels";
        const string AssetPath = Root + "/LevelCatalog.asset";
        const string PackPath = Root + "/LevelPack.bytes";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            static bool IsLevel(string p) => p.StartsWith(Root + "/") && p.EndsWith(".json");
            // After the import finishes: creating or saving assets from inside it is not allowed.
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsLevel)) EditorApplication.delayCall += Collect;
        }

        [MenuItem("Blocks Escape/Collect Levels")]
        public static void Collect()
        {
            var paths = AssetDatabase.FindAssets("t:TextAsset", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".json"))
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToArray();

            var offsets = new int[paths.Length + 1];
            using var data = new MemoryStream();
            for (int i = 0; i < paths.Length; i++)
            {
                var level = LevelData.Parse(File.ReadAllText(paths[i]));
                level.solution = null;
                level.analysis = null;
                var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(level));
                data.Write(json, 0, json.Length);
                offsets[i + 1] = (int)data.Length;
            }

            using (var file = new BinaryWriter(File.Create(PackPath)))
            {
                file.Write(paths.Length);
                foreach (int offset in offsets) file.Write(offset);
                file.Write(data.GetBuffer(), 0, (int)data.Length);
            }
            AssetDatabase.ImportAsset(PackPath, ImportAssetOptions.ForceUpdate);

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }
            catalog.Set(AssetDatabase.LoadAssetAtPath<TextAsset>(PackPath));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Packed {paths.Length} levels into {PackPath} ({new FileInfo(PackPath).Length / 1024} KB).", catalog);
        }
    }
}
