using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Keeps Levels/LevelCatalog.asset in step with the Levels folder: whenever a level file (Assets/Levels/*.json) is
    // added, removed or moved, the catalog is rebuilt from every level file there, sorted by file name.
    sealed class LevelCatalogCollector : AssetPostprocessor
    {
        const string Root = "Assets/Levels";
        const string AssetPath = Root + "/LevelCatalog.asset";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            static bool IsLevel(string p) => p.StartsWith(Root + "/") && p.EndsWith(".json");
            // After the import finishes: creating or saving assets from inside it is not allowed.
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsLevel)) EditorApplication.delayCall += Collect;
        }

        [MenuItem("Blocks Escape/Collect Levels")]
        static void Collect()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }
            var levels = AssetDatabase.FindAssets("t:TextAsset", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".json"))
                .OrderBy(p => Path.GetFileName(p), System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(t => t != null)
                .ToArray();
            catalog.Set(levels);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Collected {levels.Length} levels.", catalog);
        }
    }
}
