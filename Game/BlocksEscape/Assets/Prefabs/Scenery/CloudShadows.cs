using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksEscape
{
    // Soft cloud shadows drifting over the farm: a tiling noise texture generated at start-up, used as the sun's light
    // cookie and scrolled slowly. Everything lit by the sun darkens under a cloud, so it costs one texture lookup in the
    // lighting and no extra geometry. Goes on the directional light.
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public sealed class CloudShadows : MonoBehaviour
    {
        [Tooltip("World size of one tile of clouds.")]
        [SerializeField, Min(1f)] float tileSize = 26f;
        [Tooltip("How dark the shadow under a cloud is (0 none, 1 black).")]
        [SerializeField, Range(0f, 1f)] float darkness = 0.28f;
        [Tooltip("Share of the sky covered by clouds.")]
        [SerializeField, Range(0f, 1f)] float coverage = 0.45f;
        [Tooltip("Drift in world units per second.")]
        [SerializeField] Vector2 wind = new Vector2(0.35f, 0.15f);
        [SerializeField, Range(32, 512)] int resolution = 128;
        [SerializeField] int seed = 7;

        Texture2D _texture;
        Light _light;
        UniversalAdditionalLightData _lightData;
        Vector2 _offset;
        bool _dirty;

        void OnEnable()
        {
            _light = GetComponent<Light>();
            _lightData = GetComponent<UniversalAdditionalLightData>();
            Build();
        }

        void OnValidate() => _dirty = true; // rebuilt on the next update, outside OnValidate

        void OnDisable()
        {
            if (_light != null && _light.cookie == _texture) _light.cookie = null;
            if (_texture != null) DestroyImmediate(_texture);
            _texture = null;
        }

        void Update()
        {
            if (_dirty) { _dirty = false; Build(); }
            if (_lightData == null) return;
            _offset += wind * Time.deltaTime;
            _offset.x %= tileSize;
            _offset.y %= tileSize;
            _lightData.lightCookieOffset = _offset;
        }

        void Build()
        {
            if (_light == null) return;
            if (_texture != null) DestroyImmediate(_texture);
            int n = resolution;
            _texture = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = "Cloud Shadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[n * n];
            var rng = new System.Random(seed);
            var lattice = new float[4][];
            int[] periods = { 4, 8, 16, 32 };
            for (int o = 0; o < lattice.Length; o++)
            {
                int p = periods[o];
                lattice[o] = new float[p * p];
                for (int k = 0; k < lattice[o].Length; k++) lattice[o][k] = (float)rng.NextDouble();
            }
            float edge = 0.12f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Tiling fractal value noise: each octave wraps on its own lattice period.
                float value = 0f, weight = 0.5f, total = 0f;
                for (int o = 0; o < lattice.Length; o++)
                {
                    value += weight * Lattice(lattice[o], periods[o], (float)x / n * periods[o], (float)y / n * periods[o]);
                    total += weight;
                    weight *= 0.5f;
                }
                value /= total;
                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - coverage - edge, 1f - coverage + edge, value));
                byte light = (byte)Mathf.RoundToInt(255f * (1f - darkness * cloud));
                pixels[y * n + x] = new Color32(light, light, light, 255);
            }
            _texture.SetPixels32(pixels);
            _texture.Apply(true, false);
            _light.cookie = _texture;
            if (_lightData != null) _lightData.lightCookieSize = Vector2.one * tileSize;
        }

        static float Lattice(float[] grid, int period, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float At(int i, int j) => grid[((j % period + period) % period) * period + (i % period + period) % period];
            return Mathf.Lerp(Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }
    }
}
