using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // The "well done" when a level is solved: the victory jingle, a fountain of petals out of the empty pen, a hop of the
    // level sign, and the scenery cheering in a wave spreading out from the board (every shown TapReaction reacts, as if
    // tapped). GameFlow plays it once the last block has slid out.
    [DisallowMultipleComponent]
    public sealed class Celebration : MonoBehaviour
    {
        [SerializeField] BoardCamera boardCamera;
        [SerializeField] SoundPlayer sounds;
        [SerializeField] ScreenAnchor levelSign;
        [Tooltip("Petal material (Materials/Leaf.mat).")]
        [SerializeField] Material confetti;
        [SerializeField] Color[] confettiColors =
        {
            new Color(1f, 0.45f, 0.62f), new Color(1f, 1f, 1f), new Color(1f, 0.8f, 0.15f),
            new Color(0.62f, 0.48f, 0.95f), new Color(0.45f, 0.75f, 0.25f), new Color(0.96f, 0.55f, 0.1f),
            new Color(0.9f, 0.3f, 0.25f),
        };
        [SerializeField, Min(0)] int confettiCount = 110;
        [Tooltip("Petal size, in cells.")]
        [SerializeField, Min(0.01f)] float confettiSize = 0.5f;
        [Tooltip("Seconds the cheer takes to spread from the board to the farthest prop.")]
        [SerializeField, Min(0f)] float waveTime = 0.6f;
        [Tooltip("How much bigger the level sign gets at the top of its hop.")]
        [SerializeField, Range(0f, 0.5f)] float signHop = 0.18f;

        ParticleSystem _fx;

        public void Play()
        {
            if (sounds != null) sounds.PlayVictory();
            var board = boardCamera.BoardBounds;
            Burst(board);
            StartCoroutine(HopSign());
            StartCoroutine(Cheer(board.center));
        }

        void Burst(Bounds board)
        {
            if (confetti == null || confettiCount == 0) return;
            if (_fx == null) _fx = ParticleFx.Confetti(transform, confetti, confettiColors);
            _fx.transform.position = new Vector3(board.center.x, board.min.y, board.center.z);
            var shape = _fx.shape;
            shape.radius = Mathf.Max(0.1f, Mathf.Min(board.extents.x, board.extents.z) * 0.5f);
            var main = _fx.main;
            float size = confettiSize * GridUnits.CellSize;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
            _fx.Play();
            _fx.Emit(confettiCount);
        }

        IEnumerator HopSign()
        {
            if (levelSign == null || !levelSign.gameObject.activeInHierarchy) yield break;
            const float duration = 0.7f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                // A quick swell, then a few shrinking wobbles back to rest.
                levelSign.Pop = 1f + signHop * Mathf.Sin(t * 14f) * Mathf.Exp(-t * 5f);
                yield return null;
            }
            levelSign.Pop = 1f;
        }

        IEnumerator Cheer(Vector3 center)
        {
            var props = new List<(TapReaction prop, float distance)>();
            float near = float.MaxValue, far = 0f;
            foreach (var prop in FindObjectsByType<TapReaction>())
            {
                if (!prop.IsShown) continue;
                var offset = prop.transform.position - center;
                float d = new Vector2(offset.x, offset.z).magnitude;
                props.Add((prop, d));
                near = Mathf.Min(near, d);
                far = Mathf.Max(far, d);
            }
            props.Sort((a, b) => a.distance.CompareTo(b.distance));
            float span = Mathf.Max(far - near, 1e-3f), elapsed = 0f;
            int next = 0;
            while (next < props.Count)
            {
                while (next < props.Count && (props[next].distance - near) / span * waveTime <= elapsed)
                {
                    if (props[next].prop != null) props[next].prop.Cheer();
                    next++;
                }
                yield return null;
                elapsed += Time.deltaTime;
            }
        }
    }
}
