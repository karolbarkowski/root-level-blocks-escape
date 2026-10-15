using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // Shows or hides screen-anchored props (menu planks, the level sign, buttons) by scaling them through
    // ScreenAnchor.Pop, one after another: in with a little overshoot, out with a little wind-up first.
    // Run as a coroutine. Showing activates the props first; hiding deactivates them at the end and skips inactive ones.
    public static class PopTransition
    {
        public static IEnumerator Run(IReadOnlyList<ScreenAnchor> props, bool show, float time, float stagger)
        {
            var list = new List<ScreenAnchor>();
            foreach (var p in props)
                if (p != null && (show || p.gameObject.activeSelf)) list.Add(p);
            if (list.Count == 0) yield break;
            if (show)
                foreach (var p in list)
                {
                    p.Pop = 0f;
                    p.gameObject.SetActive(true);
                }

            float elapsed = 0f, total = time + stagger * (list.Count - 1);
            while (true)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    float t = Mathf.Clamp01((elapsed - i * stagger) / time);
                    list[i].Pop = show ? Ease.OutBack(t) : 1f - Ease.InBack(t);
                }
                if (elapsed >= total) break;
                yield return null;
                elapsed += Time.deltaTime;
            }

            foreach (var p in list)
            {
                if (!show) p.gameObject.SetActive(false);
                p.Pop = 1f;
            }
        }
    }
}
