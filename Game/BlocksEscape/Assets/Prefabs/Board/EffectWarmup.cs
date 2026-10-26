using System;
using System.Collections;
using BlocksEscape.Levels;
using UnityEngine;

namespace BlocksEscape
{
    // Plays every effect once out of sight when the app starts, so the first real block exit, gate glow or particle burst
    // doesn't stall. On Vulkan and Metal the GPU pipeline for a shader is built the first time something draws with it,
    // for this camera's exact render pass; on a phone that takes long enough to drop frames mid-animation. Drawing them
    // through the real camera, under the ground (opaque, drawn first, so nothing shows), builds those pipelines up front.
    // Once per app session: the pipelines stay cached afterwards.
    public sealed class EffectWarmup : MonoBehaviour
    {
        [SerializeField] Board board;
        [Tooltip("Particle materials (Shaders/Particle.shader) used by taps and the celebration: one hidden burst each. " +
                 "The block exit's sparkle material is warmed by the exit itself.")]
        [SerializeField] Material[] particleMaterials = Array.Empty<Material>();
        [Tooltip("How far below the ground the effects play, in cells.")]
        [SerializeField, Min(1f)] float depth = 2f;
        [Tooltip("Frames to keep the effects alive, so each is drawn at least once.")]
        [SerializeField, Min(1)] int frames = 3;

        static bool _done;

        IEnumerator Start()
        {
            if (_done || board == null) yield break;
            yield return null; // let the board camera frame the view first

            var cam = Camera.main;
            if (cam == null) yield break;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) yield break;
            _done = true;

            var root = new GameObject("Effect Warmup").transform;
            root.position = ray.GetPoint(distance) + Vector3.down * (depth * GridUnits.CellSize);

            var models = board.Models;
            var blocks = models != null ? models.BlocksOf(null) : null; // the neutral blockers
            var gate = models != null ? models.AnyGate() : null;

            // Block exit: the dissolve shader (with its shadow pass) and the sparkle particles.
            if (blocks != null && blocks.Count > 0 && blocks[0] != null && board.Exit.dissolve != null)
            {
                var block = Instantiate(blocks[0].gameObject, root, false);
                BlockExit.Play(block, Vector3.zero, Array.Empty<GateCell>(), board.GateGlowMaterial, board.Exit);
            }

            // Gate glow. The tile itself is hidden: its outline draws with no depth test and would show through the ground.
            if (gate != null && board.GateGlowMaterial != null)
            {
                var cell = Instantiate(gate, root, false);
                foreach (var r in cell.GetComponentsInChildren<Renderer>()) r.enabled = false;
                cell.SetGlow(1f, board.GateGlowMaterial);
            }

            // Particles: each material has its own blend state, so each needs a draw.
            foreach (var material in particleMaterials)
            {
                if (material == null) continue;
                var ps = ParticleFx.Leaves(root, material, Color.white, Color.white);
                ps.Emit(4);
            }

            for (int i = 0; i < frames; i++) yield return null;
            Destroy(root.gameObject);
        }
    }
}
