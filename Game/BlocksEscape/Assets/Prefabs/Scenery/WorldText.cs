using TMPro;
using UnityEngine;

namespace BlocksEscape
{
    // Text on 3D props (menu planks, the level sign, sleep bubbles) in the game font (Fonts/Fredoka SDF, built by
    // "Blocks Escape/Build Game Font"). The text is a TextMeshPro child, centred on the child's origin by its letters'
    // shapes, with its face towards -Z like the props' fronts. The child is never saved: callers rebuild it on load.
    public static class WorldText
    {
        // Creates or updates the parent's text child; no text removes it. Cap Height is the height of a capital letter or
        // a digit in the parent's units; with Max Width above zero, longer text is shrunk to fit.
        public static TextMeshPro Show(Transform parent, string name, string text, TMP_FontAsset font, float capHeight,
            float maxWidth = 0f)
        {
            var child = parent.Find(name);
            TextMeshPro tmp = null;
            if (child != null && (string.IsNullOrEmpty(text) || font == null || !child.TryGetComponent(out tmp)))
            {
                // Detached first: Destroy waits for the end of the frame, and a rebuild in the meantime must not find it.
                child.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else Object.DestroyImmediate(child.gameObject);
                tmp = null;
            }
            if (string.IsNullOrEmpty(text) || font == null) return null;

            if (tmp == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(parent, false);
                tmp = go.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.CenterGeoAligned;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.extraPadding = true; // room for the outline and shadow around each glyph
                var renderer = tmp.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            tmp.font = font;
            // TextMeshPro sizes 3D text at a tenth of a unit per point, relative to the size the font was sampled at.
            var face = font.faceInfo;
            float size = capHeight / Mathf.Max(0.1f * face.capLine / face.pointSize * face.scale, 1e-5f);
            int lines = 1; // room for every line, so shrinking to fit only ever answers to the width
            foreach (char c in text) if (c == '\n') lines++;
            tmp.rectTransform.sizeDelta = new Vector2(maxWidth > 0f ? maxWidth : capHeight * 50f, capHeight * 3f * lines);
            tmp.enableAutoSizing = maxWidth > 0f;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = size * 0.3f;
            tmp.fontSize = size;
            tmp.text = text;
            return tmp;
        }
    }
}
