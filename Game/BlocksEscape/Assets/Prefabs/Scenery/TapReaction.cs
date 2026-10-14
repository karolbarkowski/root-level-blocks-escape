using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // How a scenery prop answers a tap (SceneryTapper): trees shake and drop a few leaves, bushes shrug off leaves,
    // flowers and grass wobble, animals hop, the pond ripples where it was touched, the barn swings its doors open and
    // shut, and a resting butterfly takes off. Animates the prop's model (its first child) from its resting pose, so the
    // root keeps the position and scale ScreenAnchor gives it. Idle props cost nothing but an early return. Taps are found
    // with mesh colliders added in Play mode, which only cost anything when something raycasts.
    [DisallowMultipleComponent]
    public sealed class TapReaction : MonoBehaviour
    {
        public enum Kind { Shake, Shrug, Wobble, Hop, Ripple, Doors, TakeOff }


        [SerializeField] Kind kind;
        [Tooltip("Particles for leaves (Leaf.mat) or ripples (Ripple.mat); none for no particles.")]
        [SerializeField] Material particles;
        [SerializeField] Color particleColor = new Color(0.36f, 0.62f, 0.25f, 1f);
        [SerializeField] Color particleColor2 = new Color(0.55f, 0.75f, 0.3f, 1f);
        [SerializeField, Min(0)] int particleCount = 6;
        [Tooltip("Hop height in the model's own units.")]
        [SerializeField] float hopHeight = 0.6f;

        Transform _model;
        Renderer[] _renderers;
        Vector3 _restPosition, _restScale;
        Quaternion _restRotation;
        Transform[] _hinges;
        float[] _hingeSigns;
        ParticleSystem _fx;
        float _time = -1f;
        float _seed;

        public Kind Reaction => kind;

        void Awake()
        {
            _model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _renderers = GetComponentsInChildren<Renderer>();
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        void OnDisable()
        {
            if (_time >= 0f) Finish();
        }

        // The prop the ray meets first, if it answers taps.
        public static TapReaction Under(Ray ray, out Vector3 point)
        {
            point = default;
            if (!Physics.Raycast(ray, out var hit, 1000f)) return null;
            var reaction = hit.collider.GetComponentInParent<TapReaction>();
            if (reaction == null) return null;
            point = reaction.kind == Kind.Ripple ? reaction.OnWater(ray) : hit.point;
            return reaction;
        }

        public void Tap(Vector3 point)
        {
            if (kind == Kind.TakeOff)
            {
                if (TryGetComponent<ButterflyFlight>(out var flight)) flight.TakeOff();
                return;
            }
            if (_time < 0f)
            {
                _restPosition = _model.localPosition;
                _restRotation = _model.localRotation;
                _restScale = _model.localScale;
            }
            else if (kind == Kind.Doors && _time < 1.2f) return; // let the doors finish swinging
            _time = 0f;
            _seed = Random.value * 10f;
            Burst(point);
        }

        void Update()
        {
            if (_time < 0f) return;
            _time += Time.deltaTime;
            float t = _time;
            switch (kind)
            {
                case Kind.Shake:
                {
                    float a = 8f * Mathf.Exp(-t * 4.5f);
                    _model.localRotation = _restRotation * Quaternion.Euler(a * Mathf.Sin(t * 27f + _seed), 0f, a * Mathf.Sin(t * 23f));
                    if (t > 1.2f) Finish();
                    break;
                }
                case Kind.Shrug:
                {
                    float s = Mathf.Exp(-t * 6f) * Mathf.Sin(t * 22f);
                    _model.localScale = Vector3.Scale(_restScale, new Vector3(1f + 0.08f * s, 1f - 0.14f * s, 1f + 0.08f * s));
                    _model.localRotation = _restRotation * Quaternion.Euler(0f, 0f, 4f * Mathf.Exp(-t * 5f) * Mathf.Sin(t * 17f + _seed));
                    if (t > 0.9f) Finish();
                    break;
                }
                case Kind.Wobble:
                {
                    float a = 12f * Mathf.Exp(-t * 5f);
                    _model.localRotation = _restRotation * Quaternion.Euler(a * Mathf.Sin(t * 20f), 0f, a * Mathf.Sin(t * 16f + _seed));
                    if (t > 0.9f) Finish();
                    break;
                }
                case Kind.Hop:
                {
                    // Crouch, jump, land with a squash.
                    const float crouch = 0.08f, air = 0.42f;
                    float y = 0f, squash = 0f;
                    if (t < crouch) squash = Mathf.Sin(t / crouch * Mathf.PI) * 0.18f;
                    else if (t < crouch + air) { float u = (t - crouch) / air; y = 4f * u * (1f - u); squash = -0.08f * Mathf.Sin(u * Mathf.PI); }
                    else squash = Mathf.Exp(-(t - crouch - air) * 12f) * Mathf.Sin((t - crouch - air) * 30f) * 0.15f;
                    _model.localPosition = _restPosition + Vector3.up * (hopHeight * y);
                    _model.localScale = Vector3.Scale(_restScale, new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f));
                    if (t > crouch + air + 0.4f) Finish();
                    break;
                }
                case Kind.Doors:
                {
                    // Open briskly, pause, swing shut with a little bounce.
                    float open = t < 0.35f ? EaseOut(t / 0.35f) : t < 0.9f ? 1f : 1f - BounceIn(Mathf.Clamp01((t - 0.9f) / 0.5f));
                    for (int i = 0; i < Hinges().Length; i++)
                        _hinges[i].localRotation = Quaternion.Euler(0f, _hingeSigns[i] * 75f * open, 0f);
                    if (t > 1.45f) Finish();
                    break;
                }
                default:
                    if (t > 2f) Finish();
                    break;
            }
        }

        void Finish()
        {
            _time = -1f;
            if (kind == Kind.Ripple || kind == Kind.TakeOff) return;
            _model.localPosition = _restPosition;
            _model.localRotation = _restRotation;
            _model.localScale = _restScale;
            if (kind == Kind.Doors) foreach (var hinge in Hinges()) hinge.localRotation = Quaternion.identity;
        }

        // Door hinges are the model's children named "Hinge ..." (SceneryModelsSetup), each holding one leaf. A leaf swings
        // towards the camera (-Z), whichever side of its hinge it is on.
        Transform[] Hinges()
        {
            if (_hinges != null) return _hinges;
            var hinges = new List<Transform>();
            var signs = new List<float>();
            foreach (Transform child in _model)
            {
                if (!child.name.StartsWith("Hinge") || child.childCount == 0) continue;
                var leaf = child.GetChild(0);
                var mesh = leaf.GetComponent<MeshFilter>();
                var center = leaf.localPosition + (mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds.center : Vector3.zero);
                hinges.Add(child);
                signs.Add(Mathf.Sign(center.x));
            }
            _hinges = hinges.ToArray();
            _hingeSigns = signs.ToArray();
            return _hinges;
        }

        // Where the ray meets the pond's water, kept inside the water so the rings don't spill onto the bank.
        Vector3 OnWater(Ray ray)
        {
            var water = new Plane(Vector3.up, _model.TransformPoint(new Vector3(0f, 0.045f, 0f)));
            if (!water.Raycast(ray, out float d)) return _model.position;
            var local = _model.InverseTransformPoint(ray.GetPoint(d));
            var flat = new Vector2(local.x / 1.1f, local.z / 0.85f); // the water's half extents, a bit inside the edge
            if (flat.sqrMagnitude > 1f) flat.Normalize();
            return _model.TransformPoint(new Vector3(flat.x * 1.1f, 0.045f, flat.y * 0.85f));
        }

        void Burst(Vector3 point)
        {
            if (particles == null || particleCount == 0) return;
            if (_fx == null) _fx = kind == Kind.Ripple ? ParticleFx.Ripples(transform, particles, particleColor) : ParticleFx.Leaves(transform, particles, particleColor, particleColor2);
            var bounds = WorldBounds();
            if (kind == Kind.Ripple)
            {
                // The rings come from the system's timed bursts, so it restarts rather than emitting at once.
                _fx.transform.position = point + Vector3.up * 0.01f;
                ParticleFx.SetRippleSize(_fx, bounds.size.x * 0.32f);
                _fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _fx.Play();
                return;
            }
            // Leaves come off the top of the crown, where the camera above can see them.
            _fx.transform.position = new Vector3(bounds.center.x, bounds.max.y - bounds.extents.y * 0.25f, bounds.center.z);
            ParticleFx.SetLeafSpread(_fx, bounds.extents.x * 0.7f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.1f);
            _fx.Play();
            _fx.Emit(particleCount);
        }

        Bounds WorldBounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return b;
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        static float BounceIn(float t)
        {
            // Closing: accelerate shut, then a small rebound before resting.
            if (t < 0.7f) { float u = t / 0.7f; return u * u; }
            float v = (t - 0.7f) / 0.3f;
            return 1f - 0.12f * Mathf.Sin(v * Mathf.PI);
        }
    }
}
