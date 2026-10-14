using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // Spells a number with the 3D digit models (Prefabs/Scenery/Digit_0..9) under a parent, centred on its origin.
    // The digits are modelled standing with their baseline at the origin and their front facing -Z.
    public static class DigitText
    {
        // Replaces the parent's previous number. The created objects are never saved: callers rebuild them on load.
        public static Transform Build(Transform parent, string name, int value, GameObject[] digits, float height, float spacing)
        {
            // Destroy is deferred in Play mode, so detach the old number first: a second rebuild in the same frame would
            // otherwise find it again and leave the first rebuild's digits behind.
            for (var old = parent.Find(name); old != null; old = parent.Find(name))
            {
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(old.gameObject);
                else Object.DestroyImmediate(old.gameObject);
            }
            if (value < 0 || digits == null || digits.Length < 10) return null;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.gameObject.hideFlags = HideFlags.DontSave;

            var spawned = new List<(Transform t, Bounds b)>();
            foreach (char c in value.ToString())
            {
                var prefab = digits[c - '0'];
                if (prefab == null) continue;
                var digit = Object.Instantiate(prefab, root, false).transform;
                digit.gameObject.hideFlags = HideFlags.DontSave;
                spawned.Add((digit, digit.GetComponentInChildren<MeshFilter>().sharedMesh.bounds));
            }
            if (spawned.Count == 0) return root;

            float scale = height / Mathf.Max(spawned[0].b.size.y, 1e-4f), total = 0f;
            for (int i = 0; i < spawned.Count; i++) total += spawned[i].b.size.x * scale + (i > 0 ? spacing * height : 0f);
            float x = -total * 0.5f;
            foreach (var (t, b) in spawned)
            {
                float w = b.size.x * scale;
                t.localScale = Vector3.one * scale;
                t.localPosition = new Vector3(x + w * 0.5f - b.center.x * scale, -b.center.y * scale, 0f);
                x += w + spacing * height;
            }
            return root;
        }
    }
}
