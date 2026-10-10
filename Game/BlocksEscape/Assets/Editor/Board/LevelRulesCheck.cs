using System;
using System.Collections.Generic;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Replays each level's stored solution through the game's BoardRules. The generator verified the same solution
    // with its own engine, so a failure here means the game's port of the rules has drifted from the generator's.
    static class LevelRulesCheck
    {
        const string LevelsFolder = "Assets/Levels";

        [MenuItem("Tools/Blocks Escape/Verify Levels With Game Rules")]
        static void VerifyAll()
        {
            int passed = 0, failed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { LevelsFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var error = Verify(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
                if (error == null) passed++;
                else { failed++; Debug.LogError($"{path}: {error}"); }
            }
            var summary = $"Game rules check: {passed} levels replay their solution, {failed} failed.";
            if (failed > 0) Debug.LogError(summary);
            else Debug.Log(summary);
        }

        // Null when the solution is legal under the game rules and clears every typed block, otherwise the problem.
        public static string Verify(string json)
        {
            try
            {
                var level = LevelData.Parse(json);
                var rules = new BoardRules(level);
                var index = new Dictionary<string, int>();
                for (int i = 0; i < rules.BlockCount; i++) index[rules.Data(i).id] = i;

                var moves = level.solution ?? Array.Empty<MoveData>();
                for (int m = 0; m < moves.Length; m++)
                {
                    if (!index.TryGetValue(moves[m].block, out int i)) return $"move {m + 1} uses unknown block {moves[m].block}";
                    var to = new Vector2Int(moves[m].to.x, moves[m].to.y);
                    if (!rules.Reachable(i).Contains(to)) return $"move {m + 1}: block {moves[m].block} cannot reach {to}";
                    rules.Drop(i, to);
                }
                return rules.Solved ? null : "solution does not clear every typed block";
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
