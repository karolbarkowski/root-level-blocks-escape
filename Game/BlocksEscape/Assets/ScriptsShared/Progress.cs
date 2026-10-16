using System;
using UnityEngine;

namespace BlocksEscape
{
    // The player's progress, kept in PlayerPrefs: which levels are solved (a bit per level index, stored as base64)
    // and the last level played. Every playable level is open, so the solved levels can have gaps.
    public static class Progress
    {
        const string SolvedKey = "progress.solved";
        const string LastPlayedKey = "progress.lastPlayed";

        static byte[] _solved;

        static byte[] Solved
        {
            get
            {
                if (_solved != null) return _solved;
                try { _solved = Convert.FromBase64String(PlayerPrefs.GetString(SolvedKey, "")); }
                catch (FormatException) { _solved = Array.Empty<byte>(); }
                return _solved;
            }
        }

        public static bool IsSolved(int index) =>
            index >= 0 && index / 8 < Solved.Length && (Solved[index / 8] & (1 << (index % 8))) != 0;

        public static void MarkSolved(int index)
        {
            if (index < 0 || IsSolved(index)) return;
            if (index / 8 >= Solved.Length) Array.Resize(ref _solved, index / 8 + 1);
            _solved[index / 8] |= (byte)(1 << (index % 8));
            PlayerPrefs.SetString(SolvedKey, Convert.ToBase64String(_solved));
            PlayerPrefs.Save();
        }

        // Index of the last level started, or -1 before the first.
        public static int LastPlayed
        {
            get => PlayerPrefs.GetInt(LastPlayedKey, -1);
            set
            {
                PlayerPrefs.SetInt(LastPlayedKey, value);
                PlayerPrefs.Save();
            }
        }

        public static bool HasStarted => LastPlayed >= 0;

        // Where Continue goes: back to the last level played if it is unsolved, otherwise the first unsolved level after
        // it (wrapping around to the start). With every level solved, the one after the last played.
        public static int ContinueLevel(int levelCount)
        {
            if (levelCount <= 0) return -1;
            int last = Mathf.Clamp(LastPlayed, 0, levelCount - 1);
            if (!IsSolved(last)) return last;
            for (int step = 1; step < levelCount; step++)
            {
                int i = (last + step) % levelCount;
                if (!IsSolved(i)) return i;
            }
            return (last + 1) % levelCount;
        }

        // Whether every level below levelCount is solved.
        public static bool AllSolved(int levelCount)
        {
            for (int i = 0; i < levelCount; i++)
                if (!IsSolved(i)) return false;
            return true;
        }

        public static void Reset()
        {
            _solved = null;
            PlayerPrefs.DeleteKey(SolvedKey);
            PlayerPrefs.DeleteKey(LastPlayedKey);
            PlayerPrefs.Save();
        }
    }
}
