using UnityEngine;

namespace BlocksEscape
{
    // Frames the board with a perspective camera looking down from the level's bottom edge,
    // tilted so block sides show slightly. Fits everything the board renders (surface, walls, gates, blocks)
    // with a margin, centered on screen, and re-frames when a level is initialized or the aspect ratio changes.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        [SerializeField] Board board;
        [SerializeField, Range(30f, 90f)] float pitch = 70f;      // 90 = straight down
        [SerializeField, Range(10f, 60f)] float fieldOfView = 30f; // lower = flatter, less perspective distortion
        [SerializeField, Min(0f)] float margin = 0.4f;             // in cells, around the framed content

        Camera _camera;
        float _framedAspect;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            if (board != null) board.Initialized += Frame;
            Frame();
        }

        void OnDisable()
        {
            if (board != null) board.Initialized -= Frame;
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            OnDisable();
            OnEnable();
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
            var bounds = ContentBounds();
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
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                var p = toCamera * (corner - bounds.center);
                up = Mathf.Max(up, p.y - tanV * p.z);
                down = Mathf.Max(down, -p.y - tanV * p.z);
                right = Mathf.Max(right, p.x - tanH * p.z);
                left = Mathf.Max(left, -p.x - tanH * p.z);
            }

            float cz = Mathf.Min(-(up + down) / (2f * tanV), -(right + left) / (2f * tanH));
            var local = new Vector3((right - left) * 0.5f, (up - down) * 0.5f, cz);
            transform.SetPositionAndRotation(bounds.center + rotation * local, rotation);
        }

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
