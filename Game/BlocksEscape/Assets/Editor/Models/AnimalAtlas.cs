using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Merges an animal's two atlases from animals.py (<Animal>_Albedo.png, awake, and <Animal>_Sleep_Albedo.png, eyes
    // shut; same layout) into one, <Animal>_Atlas.png, plus a tiny remap texture, <Animal>_SleepRemap.png, for
    // Shaders/Animal.shader. The two differ only around the eyes, so the merged atlas is the awake one with the sleeping
    // version of every differing tile copied into space no model uses; the remap texture has one texel per tile telling
    // the shader how far to move a lookup to reach that copy (the atlas must repeat, Unity's default wrap mode: moves
    // are stored modulo the atlas). The game then ships and loads one atlas per animal.
    //
    // The atlas is cut into 64 x 64 tiles. A tile is copied when the two atlases differ in it or within a margin around
    // it, and is copied with a gutter of its surroundings, so filtering and mipmaps never reach across to the awake eyes.
    // Space counts as unused when no triangle of the animal's models has its UVs near it.
    // The source atlases stay in the project as the inputs (no material uses them, so builds leave them out).
    static class AnimalAtlas
    {
        const int Tiles = 64;         // tiles across the atlas; the remap texture is Tiles x Tiles
        const int Steps = Tiles * 4;  // the shader moves lookups in 1 / Steps of the atlas (Animal.shader: 256)
        const int UsedMargin = 8;     // pixels kept free around the models' UVs (animals.py pads its islands by 6)

        // Builds the two textures unless they are already newer than their inputs. False when the atlases can't be merged
        // (different sizes, or not enough unused space); the caller then keeps the separate sleep atlas.
        public static bool Build(string awakePath, string sleepPath, IReadOnlyList<string> modelPaths, string atlasPath, string remapPath)
        {
            var inputs = modelPaths.Append(awakePath).Append(sleepPath);
            if (File.Exists(atlasPath) && File.Exists(remapPath) &&
                inputs.All(p => File.GetLastWriteTimeUtc(p) <= File.GetLastWriteTimeUtc(atlasPath)))
                return true;

            var awake = Load(awakePath, out int size, out int height);
            var sleep = Load(sleepPath, out int sleepSize, out int sleepHeight);
            if (size != height || sleepSize != size || sleepHeight != size || size % Steps != 0)
            {
                Debug.LogError($"{awakePath} and its sleep atlas must be square, the same size and a multiple of {Steps} pixels.");
                return false;
            }
            int step = size / Steps, tile = size / Tiles, gutter = step, cell = tile + 2 * gutter; // 16, 64, 16, 96 at 4096

            bool[] differs = new bool[size * size];
            for (int i = 0; i < differs.Length; i++)
                differs[i] = awake[i].r != sleep[i].r || awake[i].g != sleep[i].g || awake[i].b != sleep[i].b;
            bool[] used = UsedSteps(modelPaths, size, step);

            // With the margin a copied tile's neighbours are never left showing a blurred open eye; without it, as a
            // fallback when space is short, only the tiles that differ themselves are copied.
            foreach (int margin in new[] { 2 * step, 0 })
            {
                var tiles = new List<Vector2Int>();
                for (int ty = 0; ty < Tiles; ty++)
                for (int tx = 0; tx < Tiles; tx++)
                    if (Any(differs, size, tx * tile - margin, ty * tile - margin, tile + 2 * margin)) tiles.Add(new Vector2Int(tx, ty));

                var cells = Allocate(used, tiles.Count, cell / step);
                if (cells == null) continue;

                var remap = new Color32[Tiles * Tiles];
                for (int i = 0; i < remap.Length; i++) remap[i] = new Color32(0, 0, 0, 255);
                for (int i = 0; i < tiles.Count; i++)
                {
                    int sx = tiles[i].x * tile - gutter, sy = tiles[i].y * tile - gutter; // source, gutter included
                    int dx = cells[i].x * step, dy = cells[i].y * step;
                    for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                        awake[(dy + y) * size + dx + x] =
                            sleep[Mathf.Clamp(sy + y, 0, size - 1) * size + Mathf.Clamp(sx + x, 0, size - 1)];
                    // The move in steps, modulo the whole atlas: the atlas repeats, so a move to the left or down is
                    // the same as the rest of the way round to the right or up, and one byte per axis holds it.
                    remap[tiles[i].y * Tiles + tiles[i].x] =
                        new Color32((byte)((dx - sx) / step & 255), (byte)((dy - sy) / step & 255), 0, 255);
                }

                Save(atlasPath, awake, size, size);
                Save(remapPath, remap, Tiles, Tiles);
                AssetDatabase.ImportAsset(atlasPath);
                AssetDatabase.ImportAsset(remapPath);
                SetUpRemap(remapPath);
                Debug.Log($"{atlasPath}: {tiles.Count} sleeping tiles merged in.");
                return true;
            }
            Debug.LogError($"{awakePath}: not enough unused space for the sleeping tiles; keeping the separate sleep atlas.");
            return false;
        }

        // The remap texture holds numbers, not colours: exact texels, no filtering, no compression on any platform.
        static void SetUpRemap(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = Tiles;
                settings.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(settings);
            }
            importer.SaveAndReimport();
        }

        // Pixels of an image file, bottom row first like UVs, whatever its import settings.
        static Color32[] Load(string path, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.LoadImage(File.ReadAllBytes(path));
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static void Save(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // Which step-sized squares of the atlas the models' triangles reach (by their UV bounds, plus the margin).
        static bool[] UsedSteps(IReadOnlyList<string> modelPaths, int size, int step)
        {
            var used = new bool[Steps * Steps];
            foreach (var path in modelPaths)
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
            {
                var uv = mesh.uv;
                var triangles = mesh.triangles;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector2 a = uv[triangles[t]] * size, b = uv[triangles[t + 1]] * size, c = uv[triangles[t + 2]] * size;
                    int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - UsedMargin) / step);
                    int x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) + UsedMargin) / step);
                    int y0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y, c.y) - UsedMargin) / step);
                    int y1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y, c.y) + UsedMargin) / step);
                    for (int y = Mathf.Max(y0, 0); y <= Mathf.Min(y1, Steps - 1); y++)
                    for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, Steps - 1); x++)
                        used[y * Steps + x] = true;
                }
            }
            return used;
        }

        static bool Any(bool[] mask, int size, int x0, int y0, int span)
        {
            for (int y = Mathf.Max(y0, 0); y < Mathf.Min(y0 + span, size); y++)
            for (int x = Mathf.Max(x0, 0); x < Mathf.Min(x0 + span, size); x++)
                if (mask[y * size + x]) return true;
            return false;
        }

        // Places count square cells of span steps a side on unused steps; their corners in steps, or null if they don't fit.
        static List<Vector2Int> Allocate(bool[] used, int count, int span)
        {
            var taken = (bool[])used.Clone();
            var cells = new List<Vector2Int>(count);
            for (int y = 0; y + span <= Steps && cells.Count < count; y++)
            for (int x = 0; x + span <= Steps && cells.Count < count; x++)
            {
                bool free = true;
                for (int j = 0; j < span && free; j++)
                for (int i = 0; i < span && free; i++)
                    free = !taken[(y + j) * Steps + x + i];
                if (!free) continue;
                for (int j = 0; j < span; j++)
                for (int i = 0; i < span; i++)
                    taken[(y + j) * Steps + x + i] = true;
                cells.Add(new Vector2Int(x, y));
            }
            return cells.Count == count ? cells : null;
        }
    }
}
