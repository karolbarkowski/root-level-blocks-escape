using System;
using UnityEngine;

namespace BlocksEscape
{
    // The player's settings, kept in PlayerPrefs. Changed is raised after any of them changes.
    public static class GameSettings
    {
        const string MusicKey = "settings.music";
        const string SoundKey = "settings.sound";

        public static event Action Changed;

        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt(MusicKey, 1) != 0;
            set => Set(MusicKey, value);
        }

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) != 0;
            set => Set(SoundKey, value);
        }

        static void Set(string key, bool on)
        {
            PlayerPrefs.SetInt(key, on ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
