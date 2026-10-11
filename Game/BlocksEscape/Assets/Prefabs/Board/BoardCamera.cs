using UnityEngine;

namespace BlocksEscape
{
    // Frames the board with a perspective camera looking down from the level's bottom edge,
    // tilted so block sides show slightly. Fits everything the board renders (surface, walls, gates, blocks)
    // with a margin, centered on screen, and re-frames when a level is initialized or the aspect ratio changes.
    //
    // After framing it publishes the board's vertical extent on screen as _TiltShiftBand (viewport y min, max),
    // so the tilt-shift pass (Shaders/TiltShift.shader) keeps the whole board sharp and blurs only above and below it.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        static readonly int TiltShiftBandId = Shader.PropertyToID("_TiltShiftBand");

        [SerializeField] Board board;
        [SerializeField, Range(30f, 90f)] float pitch = 70f;      // 90 = straight down
        [SerializeField, Range(10f, 60f)] float fieldOfView = 30f; // lower = flatter, less perspective distortion
        [SerializeField, Min(0f)] float margin = 0.4f;             // in cells, around the framed content

        Camera _camera;
        float _framedAspect;
        Board _subscribed;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Subscribe();
            Frame();
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.Initialized -= Frame;
            _subscribed = null;
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled || _camera == null) return;
            Subscribe();
            Frame();
        }

        void Subscribe()
        {
            if (_subscribed == board) return;
            if (_subscribed != null) _subscribed.Initialized -= Frame;
            _subscribed = board;
            if (_subscribed != null) _subscribed.Initialized += Frame;
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(_framedAspect, _camera.aspect)) Frame();
        }

        public void Frame()
        {
            if (board == null || _camera == null) return;
            _framedAspect = _camera.aspect;
            _camera.orthographic = false;
            _camera.fieldOfView = fieldOfView;

            var rotation = board.transform.rotation * Quaternion.Euler(pitch, 0f, 0f);
            var content = ContentBounds();
            var bounds = content;
            bounds.Expand(margin * 2f * GridUnits.CellSize);

            // In camera-aligned axes around the bounds center, a point (x, y, z) is in view when
            // |x - cx| <= tanH (z - cz) and |y - cy| <= tanV (z - cz). Solving for the camera position (cx, cy, cz)
            // that touches the frustum on both sides of the tighter axis keeps the content centered.
            float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * _camera.aspect;
            float up = float.MinValue, down = float.MinValue, right = float.MinValue, left = float.MinValue;
            var toCamera = Quaternion.Inverse(rotation);
            for (int i = 0; i < 8; i++)
            {
                var p = toCamera * (Corner(bounds, i) - bounds.center);
                up = Mathf.Max(up, p.y - tanV * p.z);
                down = Mathf.Max(down, -p.y - tanV * p.z);
                right = Mathf.Max(right, p.x - tanH * p.z);
                left = Mathf.Max(left, -p.x - tanH * p.z);
            }

            float cz = Mathf.Min(-(up + down) / (2f * tanV), -(right + left) / (2f * tanH));
            var local = new Vector3((right - left) * 0.5f, (up - down) * 0.5f, cz);
            transform.SetPositionAndRotation(bounds.center + rotation * local, rotation);

            // The board's on-screen band, without the margin, for the tilt-shift pass.
            float bandMin = float.MaxValue, bandMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float y = _camera.WorldToViewportPoint(Corner(content, i)).y;
                bandMin = Mathf.Min(bandMin, y);
                bandMax = Mathf.Max(bandMax, y);
            }
            Shader.SetGlobalVector(TiltShiftBandId, new Vector4(bandMin, bandMax, 0f, 0f));
        }

        static Vector3 Corner(Bounds b, int i) =>
            b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));

        Bounds ContentBounds()
        {
            var renderers = board.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                var size = board.Size;
                return new Bounds(board.transform.TransformPoint(new Vector3(size.x, 0f, size.y) * 0.5f), new Vector3(size.x, 0f, size.y));
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
