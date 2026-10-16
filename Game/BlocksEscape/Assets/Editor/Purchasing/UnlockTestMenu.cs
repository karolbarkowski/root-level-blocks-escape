using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Testing helpers for the unlock (Entitlements). In the editor the store is Unity's fake store, which owns nothing at
    // the start of play mode, so forgetting the cached unlock locks the game again.
    static class UnlockTestMenu
    {
        const int FreeLevels = 200;

        [MenuItem("Blocks Escape/Testing/Forget Unlock")]
        static void ForgetUnlock()
        {
            Entitlements.ResetCache();
            Debug.Log("Unlock forgotten; sections 2-6 are locked until it is bought again.");
        }

        // Marks every Meadow level solved, so Continue and the next level lead to the unlock screen.
        [MenuItem("Blocks Escape/Testing/Solve Free Levels")]
        static void SolveFreeLevels()
        {
            for (int i = 0; i < FreeLevels; i++) Progress.MarkSolved(i);
            Progress.LastPlayed = FreeLevels - 1;
            Debug.Log($"Levels 1-{FreeLevels} marked solved.");
        }
    }
}
