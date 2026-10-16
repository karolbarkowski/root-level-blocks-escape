using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // Every sound the game plays, collected from Assets/Sounds by GameSoundsCollector whenever files there change:
    // Sounds/music holds the background track, the clips directly in Sounds are effects found by file name (victory,
    // click, door_open, ...), and Sounds/<block type id> (cow, sheep, ...) holds any number of clips for that animal.
    // Builds can't list folders, so the clips are referenced from here.
    public sealed class GameSounds : ScriptableObject
    {
        [Serializable]
        public struct TypeSounds
        {
            public string type;
            public AudioClip[] clips;
        }

        [SerializeField] AudioClip music;
        [SerializeField] AudioClip[] effects = Array.Empty<AudioClip>();
        [SerializeField] TypeSounds[] blockTypes = Array.Empty<TypeSounds>();

        public AudioClip Music => music;

        // The effect clip with this file name; null when there is none.
        public AudioClip Effect(string name)
        {
            foreach (var clip in effects)
                if (clip != null && clip.name == name) return clip;
            return null;
        }

        // The clips for a block type id; empty when the type has no folder.
        public IReadOnlyList<AudioClip> ClipsOf(string type)
        {
            foreach (var entry in blockTypes)
                if (entry.type == type) return entry.clips ?? Array.Empty<AudioClip>();
            return Array.Empty<AudioClip>();
        }

#if UNITY_EDITOR
        public void Set(AudioClip musicClip, AudioClip[] effectClips, TypeSounds[] types)
        {
            music = musicClip;
            effects = effectClips;
            blockTypes = types;
        }
#endif
    }
}
