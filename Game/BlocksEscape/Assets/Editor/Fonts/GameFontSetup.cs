using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace BlocksEscape.EditorTools
{
    // Builds the game's font asset, Fonts/Fredoka SDF.asset, from Fonts/Fredoka-SemiBold.ttf (Fredoka, SIL Open Font
    // License, see Fonts/Fredoka-OFL.txt; a SemiBold instance of the variable font). The atlas is dynamic, so any
    // character the game shows is added when first needed; the common ones are baked in. Its material is the lettering
    // style used everywhere: cream face, dark-brown outline and a soft shadow below. It uses the plain Distance Field
    // shader: the URP Lit text shader washes the outline colours out under the scene's lighting.
    static class GameFontSetup
    {
        const string FontPath = "Assets/Fonts/Fredoka-SemiBold.ttf";
        const string AssetPath = "Assets/Fonts/Fredoka SDF.asset";
        const string Baked = " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!?.,:;'\"-+/()%&";

        static readonly Color Face = new(1f, 0.945f, 0.84f);       // scenery.py "cream"
        static readonly Color Outline = new(0.42f, 0.243f, 0.133f); // scenery.py "letter_edge"

        [MenuItem("Blocks Escape/Build Game Font")]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) { Debug.LogError($"No font at {FontPath}."); return; }

            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (asset == null)
            {
                // Generous padding: the outline and the shadow are drawn inside it.
                asset = TMP_FontAsset.CreateFontAsset(font, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                asset.name = "Fredoka SDF";
                AssetDatabase.CreateAsset(asset, AssetPath);
                asset.atlasTextures[0].name = "Fredoka SDF Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                asset.material.name = "Fredoka SDF Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            // It also reports characters that were already in the atlas, so check what is actually absent.
            asset.TryAddCharacters(Baked, out _);
            var missing = new System.Text.StringBuilder();
            foreach (char c in Baked)
                if (!asset.HasCharacter(c)) missing.Append(c);
            if (missing.Length > 0) Debug.LogWarning($"Fredoka has no glyphs for: {missing}");
            Style(asset.material);
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(asset.material);
            AssetDatabase.SaveAssets();
            Debug.Log("Built the game font.", asset);
        }

        static void Style(Material material)
        {
            material.shader = Shader.Find("TextMeshPro/Distance Field");
            material.SetColor(ShaderUtilities.ID_FaceColor, Face);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.3f);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.12f, 0.06f, 0.02f, 0.45f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.1f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
            // The text meshes are padded from these ratios; stale ones clip the outline at the glyphs' edges.
            ShaderUtilities.UpdateShaderRatios(material);
        }
    }
}
