using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksEscape
{
    // Spells a number or a word with the 3D glyph models (Prefabs/Scenery/Digit_0..9, Letter_A..Z) under a parent,
    // centred on its origin. The glyphs are modelled standing with their baseline at the origin and their front facing -Z.
    public static class DigitText
    {
        // Replaces the parent's previous number. The created objects are never saved: callers rebuild them on load.
        public static Transform Build(Transform parent, string name, int value, GameObject[] digits, float height, float spacing) =>
            Build(parent, name, value < 0 ? null : value.ToString(),
                c => digits != null && digits.Length >= 10 && c is >= '0' and <= '9' ? digits[c - '0'] : null, height, spacing);

        // Replaces the parent's previous text. Spaces leave a gap; characters without a glyph are skipped.
        // With maxWidth above zero, text that would be wider is scaled down to fit.
        public static Transform Build(Transform parent, string name, string text, Func<char, GameObject> glyphOf,
            float height, float spacing, float maxWidth = 0f)
        {
            // Destroy is deferred in Play mode, so detach the old text first: a second rebuild in the same frame would
            // otherwise find it again and leave the first rebuild's glyphs behind.
            for (var old = parent.Find(name); old != null; old = parent.Find(name))
            {
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(old.gameObject);
                else Object.DestroyImmediate(old.gameObject);
            }
            if (string.IsNullOrEmpty(text)) return null;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.gameObject.hideFlags = HideFlags.DontSave;

            var spawned = new List<(Transform t, Bounds b)>(); // null transform: a space
            foreach (char c in text)
            {
                if (c == ' ') { spawned.Add((null, default)); continue; }
                var prefab = glyphOf(c);
                if (prefab == null) continue;
                var glyph = Object.Instantiate(prefab, root, false).transform;
                glyph.gameObject.hideFlags = HideFlags.DontSave;
                spawned.Add((glyph, glyph.GetComponentInChildren<MeshFilter>().sharedMesh.bounds));
            }
            var first = spawned.Find(s => s.t != null);
            if (first.t == null) return root;

            float scale = height / Mathf.Max(first.b.size.y, 1e-4f);
            float Width(Bounds b, Transform t) => t != null ? b.size.x * scale : height * 0.45f;
            float total = 0f;
            for (int i = 0; i < spawned.Count; i++) total += Width(spawned[i].b, spawned[i].t) + (i > 0 ? spacing * height : 0f);
            if (maxWidth > 0f && total > maxWidth)
            {
                float fit = maxWidth / total;
                scale *= fit; height *= fit; total = maxWidth;
            }
            float x = -total * 0.5f;
            foreach (var (t, b) in spawned)
            {
                float w = Width(b, t);
                if (t != null)
                {
                    t.localScale = Vector3.one * scale;
                    // One baseline for all: centred on the first glyph's height, so a Q's tail hangs below the rest.
                    t.localPosition = new Vector3(x + w * 0.5f - b.center.x * scale, -first.b.center.y * scale, 0f);
                }
                x += w + spacing * height;
            }
            return root;
        }
    }
}
