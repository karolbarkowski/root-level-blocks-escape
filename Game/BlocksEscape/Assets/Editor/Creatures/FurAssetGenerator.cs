using System.IO;
using UnityEditor;
using UnityEngine;
using static BlocksEscape.EditorTools.AssetGeneration;

namespace BlocksEscape.EditorTools
{
    // Generates the creature fur assets: the strand noise texture and a default fur material.
    // Runs once automatically when the material is missing; "Regenerate" rewrites the noise in place
    // and leaves the material's tuned values alone.
    [InitializeOnLoad]
    static class FurAssetGenerator
    {
        const string ArtFolder = "Assets/Art/Fur";
        const string NoisePath = ArtFolder + "/FurNoise.png";
        const string MaterialPath = ArtFolder + "/CreatureFur.mat";
        const string ShaderName = "BlocksEscape/Fur Shells";

        // One texel per strand. Mipmaps average the strands away in the distance, so small creatures
        // turn into a soft fuzzy outline instead of shimmering.
        const int NoiseSize = 128;
        const int NoiseSeed = 1337;

        static readonly Color DefaultColor = new Color(0.95f, 0.72f, 0.55f);

        static FurAssetGenerator()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
                    Generate();
            };
        }

        [MenuItem("Tools/Blocks Escape/Regenerate Fur Assets")]
        public static void Generate()
        {
            EnsureFolder(ArtFolder);
            WriteNoise();

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Fur assets: shader '{ShaderName}' not found.");
                return;
            }

            var material = LoadOrCreate(MaterialPath, () =>
            {
                var created = new Material(shader);
                created.SetColor("_BaseColor", DefaultColor);
                return created;
            });
            material.shader = shader;
            material.SetTexture("_FurNoise", AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        // Each texel is a strand length in 0..1, uniformly distributed, so the share of strands reaching
        // a shell falls off linearly from the skin to the tips.
        static void WriteNoise()
        {
            var random = new System.Random(NoiseSeed);
            var texture = new Texture2D(NoiseSize, NoiseSize, TextureFormat.R8, false, true);
            var pixels = new Color32[NoiseSize * NoiseSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = (byte)random.Next(256);
                pixels[i] = new Color32(value, value, value, 255);
            }
            texture.SetPixels32(pixels);
            File.WriteAllBytes(NoisePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(NoisePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(NoisePath);
            importer.textureType = TextureImporterType.SingleChannel;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}
