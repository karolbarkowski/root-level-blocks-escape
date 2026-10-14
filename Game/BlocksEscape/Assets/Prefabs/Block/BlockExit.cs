using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // Look and timing of a block leaving through its gate (BlockExit), set on the Board.
    [Serializable]
    public sealed class BlockExitSettings
    {
        [Tooltip("Shaders/BlockDissolve.shader material; each leaving block gets a copy per texture.")]
        public Material dissolve;
        [Tooltip("Additive dot particle material (Shaders/Particle.shader) for the sparkles.")]
        public Material sparkle;
        public Color sparkleColor = new Color(0.65f, 0.9f, 1f, 1f);
        [Tooltip("Time to slide one cell into the gate, in seconds.")]
        [Min(0.01f)] public float slideTime = 0.22f;
        [Tooltip("Time to rise and dissolve, in seconds; it starts while the slide is finishing.")]
        [Min(0.01f)] public float dissolveTime = 0.55f;
        [Tooltip("How far the block rises while it dissolves, in cells.")]
        public float rise = 0.45f;
        [Tooltip("Time for the gate's flare to fade after the block is gone, in seconds.")]
        [Min(0.01f)] public float flareFade = 0.3f;
    }

    // Plays a block leaving the board in Play mode: it slides one cell out into its gate while the gate flares, then rises
    // and dissolves from the bottom up into light with sparkles drifting up, and is destroyed. The rules have already
    // removed it, so the board plays on meanwhile. For a peel, Board strips the leaving object down to its top layer.
    [DisallowMultipleComponent]
    public sealed class BlockExit : MonoBehaviour
    {
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int BottomId = Shader.PropertyToID("_Bottom");
        static readonly int HeightId = Shader.PropertyToID("_Height");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        BlockExitSettings _settings;
        readonly List<Material> _materials = new();
        IReadOnlyList<GateCell> _gates;
        Material _gateGlow;
        ParticleSystem _sparkles;
        Vector3 _start, _slide, _center, _size;
        float _bottom, _height, _time;

        // outward is the gate's direction in world space, one cell long.
        public static void Play(GameObject block, Vector3 outward, IReadOnlyList<GateCell> gates, Material gateGlow, BlockExitSettings settings)
        {
            if (block.TryGetComponent<Block>(out var component)) component.enabled = false;
            var exit = block.AddComponent<BlockExit>();
            exit._settings = settings;
            exit._gates = gates;
            exit._gateGlow = gateGlow;
            exit._start = block.transform.position;
            exit._slide = outward;
            exit.SwapMaterials();
            exit.MakeSparkles();
        }

        // Every visible model of the block dissolves with its own texture; the outline material is dropped.
        void SwapMaterials()
        {
            var byTexture = new Dictionary<Texture, Material>();
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled) continue;
                var source = r.sharedMaterial;
                var texture = source != null && source.HasProperty(BaseMapId) ? source.GetTexture(BaseMapId) : null;
                if (!byTexture.TryGetValue(texture ?? Texture2D.whiteTexture, out var material))
                {
                    material = new Material(_settings.dissolve);
                    if (texture != null) material.SetTexture(BaseMapId, texture);
                    byTexture[texture ?? Texture2D.whiteTexture] = material;
                    _materials.Add(material);
                }
                r.sharedMaterials = new[] { material };
                if (any) bounds.Encapsulate(r.bounds);
                else { bounds = r.bounds; any = true; }
            }
            _bottom = bounds.min.y;
            _center = bounds.center;
            _size = bounds.size;
            _height = bounds.size.y;
            Apply(0f, 0f);
        }

        void MakeSparkles()
        {
            if (_settings.sparkle == null) return;
            var go = new GameObject("Exit Sparkles");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = _center + _slide;
            _sparkles = go.AddComponent<ParticleSystem>();
            _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _sparkles.main;
            main.duration = _settings.slideTime + _settings.dissolveTime;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startColor = _settings.sparkleColor;
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var emission = _sparkles.emission;
            emission.rateOverTime = 90f;
            var shape = _sparkles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_size.x, _size.z, _size.y * 0.8f); // turned to emit upwards: z is height
            shape.rotation = new Vector3(-90f, 0f, 0f); // emit upwards
            var fade = _sparkles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _settings.sparkle;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sparkles.Play();
        }

        void Update()
        {
            _time += Time.deltaTime;
            float slideTime = _settings.slideTime, dissolveTime = _settings.dissolveTime;
            float slide = Mathf.Clamp01(_time / slideTime);
            float dissolveStart = slideTime * 0.6f;
            float dissolve = Mathf.Clamp01((_time - dissolveStart) / dissolveTime);

            float eased = slide * slide * (3f - 2f * slide);
            transform.position = _start + _slide * eased + Vector3.up * (_settings.rise * GridUnits.CellSize * EaseOut(dissolve));
            Apply(dissolve, Mathf.Clamp01(dissolve * 1.5f));

            // The gate flares to full while the block goes, then fades out.
            float done = dissolveStart + dissolveTime;
            float flare = _time < done ? Mathf.Clamp01(_time / 0.1f) : 1f - Mathf.Clamp01((_time - done) / _settings.flareFade);
            foreach (var gate in _gates) if (gate != null) gate.SetGlow(flare, _gateGlow);

            if (_time >= done)
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                if (_time >= done + _settings.flareFade) Destroy(gameObject);
            }
        }

        void Apply(float dissolve, float glow)
        {
            foreach (var m in _materials)
            {
                m.SetFloat(DissolveId, dissolve);
                m.SetFloat(GlowId, glow);
                m.SetFloat(BottomId, _bottom + _settings.rise * GridUnits.CellSize * EaseOut(dissolve)); // it rises as it goes
                m.SetFloat(HeightId, _height);
            }
        }

        void OnDestroy()
        {
            foreach (var m in _materials) Destroy(m);
            foreach (var gate in _gates ?? Array.Empty<GateCell>()) if (gate != null) gate.SetGlow(0f, _gateGlow);
            if (_sparkles != null)
            {
                _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_sparkles.gameObject, 1f);
            }
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }
}
