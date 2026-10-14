using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Keeps Sounds/GameSounds.asset in step with the Sounds folder: whenever an audio file under Assets/Sounds is added,
    // removed or moved, the music (Sounds/music) and each block type's clips (Sounds/<type id>) are collected again.
    // Also sets import settings: the music streams, the short animal calls load fully decompressed.
    sealed class GameSoundsCollector : AssetPostprocessor
    {
        const string Root = "Assets/Sounds";
        const string MusicFolder = Root + "/music";
        const string AssetPath = Root + "/GameSounds.asset";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root + "/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            if (assetPath.StartsWith(MusicFolder + "/"))
            {
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
            }
            else
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.forceToMono = true;
            }
            importer.defaultSampleSettings = settings;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            static bool IsSound(string p) => p.StartsWith(Root + "/") && !p.EndsWith(".asset") && !p.EndsWith(".cs");
            // After the import finishes: creating or saving assets from inside it is not allowed.
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsSound)) EditorApplication.delayCall += Collect;
        }

        [MenuItem("Blocks Escape/Collect Sounds")]
        static void Collect()
        {
            var sounds = AssetDatabase.LoadAssetAtPath<GameSounds>(AssetPath);
            if (sounds == null)
            {
                sounds = ScriptableObject.CreateInstance<GameSounds>();
                AssetDatabase.CreateAsset(sounds, AssetPath);
            }

            AudioClip music = Clips(MusicFolder).FirstOrDefault();
            var types = new List<GameSounds.TypeSounds>();
            foreach (var folder in AssetDatabase.GetSubFolders(Root).OrderBy(f => f))
            {
                if (folder == MusicFolder) continue;
                var clips = Clips(folder);
                if (clips.Length > 0) types.Add(new GameSounds.TypeSounds { type = Path.GetFileName(folder), clips = clips });
            }
            sounds.Set(music, types.ToArray());
            EditorUtility.SetDirty(sounds);
            AssetDatabase.SaveAssets();
            Debug.Log($"Collected sounds: music {(music != null ? music.name : "none")}, {types.Count} block types.", sounds);
        }

        static AudioClip[] Clips(string folder) =>
            AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p)?.Replace('\\', '/') == folder)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(c => c != null)
                .ToArray();
    }
}
