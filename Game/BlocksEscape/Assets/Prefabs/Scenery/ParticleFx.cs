using UnityEngine;

namespace BlocksEscape
{
    // Small particle systems for scenery taps, built in code and emitted in bursts (ParticleSystem.Emit), so nothing
    // simulates while idle. Materials use Shaders/Particle.shader. Sizes are in world units: the systems ignore the
    // ScreenAnchor scale of the prop they hang on.
    public static class ParticleFx
    {
        // Leaves that tumble out and drift down.
        public static ParticleSystem Leaves(Transform parent, Material material, Color a, Color b)
        {
            var ps = Create("Leaves", parent, material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.35f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f; // flutter rather than fall like stones
            Fade(ps, 0.1f, 0.75f);
            return ps;
        }

        public static void SetLeafSpread(ParticleSystem ps, float radius, float size)
        {
            var shape = ps.shape;
            shape.radius = Mathf.Max(radius, 0.01f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
        }

        // Petals thrown up from the ground in a fountain, tumbling down in random colours from the list (a level solved).
        public static ParticleSystem Confetti(Transform parent, Material material, Color[] colors)
        {
            var ps = Create("Confetti", parent, material);
            var main = ps.main;
            main.maxParticles = 150;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.9f;
            var gradient = new Gradient { mode = GradientMode.Fixed };
            int n = Mathf.Clamp(colors.Length, 1, 8);
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++) keys[i] = new GradientColorKey(colors[i], (i + 1f) / n);
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // the cone opens along +Z; turn it to point up
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            Fade(ps, 0.03f, 0.75f);
            return ps;
        }

        // Rings spreading on water, lying flat.
        public static ParticleSystem Ripples(Transform parent, Material material, Color color)
        {
            var ps = Create("Ripples", parent, material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.2f);
            main.startSpeed = 0f;
            main.startColor = color;
            var shape = ps.shape;
            shape.enabled = false;
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 2f), new Keyframe(1f, 1f)));
            // Three rings, one after another.
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            Fade(ps, 0.05f, 0.4f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
        }

        public static void SetRippleSize(ParticleSystem ps, float size)
        {
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.1f);
            // Emit(count) puts them all down at once; staggered rings come from the emission bursts instead.
            var emission = ps.emission;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.15f, 1), new ParticleSystem.Burst(0.32f, 1),
            });
        }

        static ParticleSystem Create(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void Fade(ParticleSystem ps, float fadeIn, float fadeOutFrom)
        {
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutFrom), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
        }
    }
}
